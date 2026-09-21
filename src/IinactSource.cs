using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MinimalMeter.Windows;
using Newtonsoft.Json.Linq;

namespace MinimalMeter;

/// <summary>
/// Optional data source backed by IINACT.
///
/// The built-in tracker hooks ActionEffectHandler.Receive, which never sees DoT
/// ticks — so other players' damage over time is invisible to it (see
/// docs/IINACT_SOURCE.md). IINACT hooks the zone-channel packet handler instead
/// and unscrambles opcodes, so it sees every tick for every actor. When it is
/// installed we prefer its numbers.
///
/// Transport is the OverlayPlugin WebSocket — the same interface kagerou and
/// Ember speak. IINACT hands us the endpoint over Dalamud IPC, so there is no
/// port to configure and no guessing whether the server is up.
/// </summary>
public sealed class IinactSource : IDisposable
{
    private const string IpcVersion       = "IINACT.Version";
    private const string IpcServerUri     = "IINACT.Server.Uri";
    private const string IpcServerRunning = "IINACT.Server.Listening";

    private const string SubscribeMessage = "{\"call\":\"subscribe\",\"events\":[\"CombatData\"]}";

    private readonly IDalamudPluginInterface _pi;
    private readonly IPluginLog              _log;
    private readonly CancellationTokenSource _cts = new();

    private Task?          _worker;
    private ClientWebSocket? _socket;
    private bool           _disposed;
    private bool           _sawData;

    /// IINACT is installed and answered an IPC call.
    public bool Available { get; private set; }
    /// The WebSocket is open and subscribed.
    public bool Connected { get; private set; }
    /// Human-readable state for the settings window.
    public string Status { get; private set; } = "not started";
    /// Most recent translated snapshot, or null if nothing has arrived yet.
    public CombatSession? Snapshot { get; private set; }

    public IinactSource(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi  = pi;
        _log = log;
    }

    /// <summary>Probe for IINACT without connecting. Cheap, safe to call often.</summary>
    public bool Probe()
    {
        try
        {
            var version = _pi.GetIpcSubscriber<Version>(IpcVersion).InvokeFunc();
            Available = version != null;
            if (Available) Status = $"IINACT {version} detected";
            return Available;
        }
        catch (Exception)
        {
            // Dalamud throws when the provider is not registered — that is simply
            // "IINACT is not installed", not an error worth logging every frame.
            Available = false;
            Status    = "IINACT not installed";
            return false;
        }
    }

    public void Start()
    {
        if (_worker != null) return;
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!Probe())
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                    continue;
                }

                var uri = ResolveEndpoint();
                if (uri == null)
                {
                    Status = "IINACT present, WebSocket server not running";
                    await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    continue;
                }

                await PumpAsync(uri, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Connected = false;
                Status    = "reconnecting: " + ex.Message;
                _log.Debug($"MinimalMeter: IINACT source error — {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private Uri? ResolveEndpoint()
    {
        try
        {
            if (!_pi.GetIpcSubscriber<bool>(IpcServerRunning).InvokeFunc())
                return null;

            var baseUri = _pi.GetIpcSubscriber<Uri?>(IpcServerUri).InvokeFunc();
            if (baseUri == null) return null;

            // IINACT reports the server's BASE uri (ws://127.0.0.1:10501/). The
            // OverlayPlugin event endpoint is /ws — connecting to the base path
            // succeeds but silently delivers nothing, which looks exactly like a
            // working connection that never produces data.
            if (string.IsNullOrEmpty(baseUri.AbsolutePath) || baseUri.AbsolutePath == "/")
                return new Uri(baseUri, "ws");

            return baseUri;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task PumpAsync(Uri uri, CancellationToken ct)
    {
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(uri, ct).ConfigureAwait(false);

        var subscribe = Encoding.UTF8.GetBytes(SubscribeMessage);
        await _socket.SendAsync(new ArraySegment<byte>(subscribe),
                                WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

        Connected = true;
        Status    = "connected to " + uri;
        _log.Info($"MinimalMeter: IINACT source connected to {uri}");

        var buffer  = new byte[64 * 1024];
        var message = new StringBuilder();

        while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                                      .ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var payload = message.ToString();
            message.Clear();
            TryIngest(payload);
        }

        Connected = false;
        _sawData  = false;
        Status    = "disconnected";
    }

    private void TryIngest(string payload)
    {
        try
        {
            var root = JObject.Parse(payload);
            if ((string?)root["type"] != "CombatData") return;
            Snapshot = Translate(root);

            if (!_sawData)
            {
                _sawData = true;
                _log.Info($"MinimalMeter: first CombatData received "
                          + $"({Snapshot.Combatants.Count} combatants)");
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"MinimalMeter: could not parse CombatData — {ex.Message}");
        }
    }

    /// <summary>
    /// OverlayPlugin CombatData → CombatSession. Every value in that payload is
    /// a preformatted string ("1,234.56"), so each one is scrubbed before parse.
    /// </summary>
    private static CombatSession Translate(JObject root)
    {
        var encounter = root["Encounter"] as JObject;
        var isActive  = string.Equals((string?)root["isActive"], "true",
                                      StringComparison.OrdinalIgnoreCase);

        double durationSeconds = ParseNumber((string?)encounter?["DURATION"]);

        var session = new CombatSession
        {
            Id        = "iinact",
            // OverlayPlugin calls this CurrentZoneName; "zoneName" is kept as a
            // fallback in case an older build of IINACT emits it instead.
            ZoneName  = (string?)encounter?["CurrentZoneName"]
                        ?? (string?)encounter?["zoneName"]
                        ?? "Unknown Zone",
            StartTime = DateTime.UtcNow.AddSeconds(-durationSeconds),
            EndTime   = isActive ? null : DateTime.UtcNow,
        };

        if (root["Combatant"] is JObject combatants)
        {
            foreach (var entry in combatants)
            {
                if (entry.Value is not JObject c) continue;

                var name = (string?)c["name"] ?? entry.Key;
                // "Limit Break" is an encounter-level pseudo-combatant, not a player.
                if (string.Equals(name, "Limit Break", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = StableKey(entry.Key);
                session.Combatants[key] = new CombatantData
                {
                    EntityId   = key,
                    Name       = name,
                    World      = "",
                    ClassJobId = MeterCanvas.JobIdFromAbbr((string?)c["Job"] ?? ""),
                    // CombatData does not distinguish party from alliance, and the
                    // meter only groups by this — everything visible is a player.
                    Type       = CombatantType.PartyMember,

                    TotalDamageDealt     = (long)ParseNumber((string?)c["damage"]),
                    TotalHealingDone     = (long)ParseNumber((string?)c["healed"]),
                    TotalOverhealingDone = (long)ParseNumber((string?)c["overHeal"]),
                    TotalDamageTaken     = (long)ParseNumber((string?)c["damagetaken"]),
                };
            }
        }

        return session;
    }

    /// Names are the only stable identifier CombatData offers; hash to a uint so
    /// the existing Dictionary&lt;uint, CombatantData&gt; keying still works.
    private static uint StableKey(string name)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in name)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return hash;
        }
    }

    private static double ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var cleaned = raw.Replace(",", "").Replace("%", "").Trim();
        return double.TryParse(cleaned, NumberStyles.Any,
                               CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try { _socket?.Abort(); } catch (Exception) { /* shutting down */ }

        // Wait for the loop to actually leave before disposing what it is using.
        // Cancelling and disposing the CTS immediately made `await Task.Delay(ct)`
        // throw ObjectDisposedException — which the inner catch does not handle,
        // so it escaped as an unobserved task exception, and the loop could still
        // be calling Dalamud IPC after the plugin had unloaded.
        try { _worker?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { /* cancellation, expected */ }
        catch (Exception) { /* never block unload on teardown */ }

        _socket?.Dispose();
        _cts.Dispose();
    }
}
