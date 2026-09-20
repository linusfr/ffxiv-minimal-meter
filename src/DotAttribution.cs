using System;
using System.Collections.Generic;

namespace MinimalMeter;

/// <summary>
/// Answers "who applied the DoT that just ticked on this target?".
///
/// DoT ticks arrive as ActorControl packets (category 0x17, HoT_DoT). That
/// packet names the *target* and the amount, never the source — so on its own
/// it cannot be credited to anyone. But every status application is already
/// visible on the ActionEffect hook as EffectKind.ApplyStatusEffectTarget,
/// carrying caster, target and status id. Recording those gives the missing
/// half, and a tick is then a lookup on (target, statusId).
///
/// This is what lets the meter see *other players'* DoTs, which neither the
/// ActionEffect hook (ticks never reach it) nor FlyText (the client only draws
/// your own numbers) nor DoTSimulator (local player only) can do.
/// </summary>
public sealed class DotAttribution
{
    /// A DoT nobody refreshes still ticks for a while; drop stale applications
    /// well past the longest reasonable duration rather than leaking them.
    private const long EntryLifetimeMs = 180_000;

    /// Sweeping every lookup would be wasteful; sweep on a timer instead.
    private const long SweepIntervalMs = 30_000;

    private readonly struct Application
    {
        public readonly uint SourceId;
        public readonly long AppliedMs;

        public Application(uint sourceId, long appliedMs)
        {
            SourceId  = sourceId;
            AppliedMs = appliedMs;
        }
    }

    // (targetEntityId, statusId) → who put it there
    private readonly Dictionary<(uint Target, uint Status), Application> _applications = new();
    private long _lastSweepMs;

    /// <summary>A status landed on a target. Called for every caster, not just local.</summary>
    public void OnStatusApplied(uint targetId, uint statusId, uint sourceId)
    {
        if (targetId == 0 || statusId == 0 || sourceId == 0) return;

        // Monotonic and process-wide. Callers used to pass session-relative
        // milliseconds, which restart near zero every fight — that made both the
        // sweep and the staleness check compare against a larger number from the
        // previous session, go negative, and silently stop working forever.
        long nowMs = Environment.TickCount64;

        // Last application wins. FFXIV tracks one instance of a given status per
        // source per target, but the tick packet carries no source, so two
        // players landing the *same* status on one target are indistinguishable
        // here — the later applier takes the credit for both. Rare outside
        // stacked duplicate jobs, and documented in docs/DOT_ATTRIBUTION.md.
        _applications[(targetId, statusId)] = new Application(sourceId, nowMs);

        Sweep(nowMs);
    }

    /// <summary>A target lost a status — stop crediting ticks for it.</summary>
    public void OnStatusRemoved(uint targetId, uint statusId)
        => _applications.Remove((targetId, statusId));

    /// <summary>Target died or despawned; drop everything on it.</summary>
    public void OnTargetGone(uint targetId)
    {
        var doomed = new List<(uint, uint)>();
        foreach (var key in _applications.Keys)
            if (key.Target == targetId)
                doomed.Add(key);
        foreach (var key in doomed)
            _applications.Remove(key);
    }

    /// <summary>Who owns a tick of <paramref name="statusId"/> on this target, if known.</summary>
    public uint? Resolve(uint targetId, uint statusId)
    {
        if (!_applications.TryGetValue((targetId, statusId), out var app))
            return null;
        long nowMs = Environment.TickCount64;
        if (nowMs - app.AppliedMs > EntryLifetimeMs)
        {
            _applications.Remove((targetId, statusId));
            return null;
        }
        return app.SourceId;
    }

    public void Clear() => _applications.Clear();

    public int TrackedCount => _applications.Count;

    private void Sweep(long nowMs)
    {
        if (nowMs - _lastSweepMs < SweepIntervalMs) return;
        _lastSweepMs = nowMs;

        var doomed = new List<(uint, uint)>();
        foreach (var kv in _applications)
            if (nowMs - kv.Value.AppliedMs > EntryLifetimeMs)
                doomed.Add(kv.Key);
        foreach (var key in doomed)
            _applications.Remove(key);
    }
}
