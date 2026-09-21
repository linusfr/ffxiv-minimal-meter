using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using MinimalMeter.Windows;

namespace MinimalMeter;

public sealed class Plugin : IDalamudPlugin
{
    // ── Injected services ─────────────────────────────────────────────────────
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static IChatGui                ChatGui         { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;
    [PluginService] internal static IObjectTable            ObjectTable     { get; private set; } = null!;
    [PluginService] internal static ICondition              Condition       { get; private set; } = null!;
    [PluginService] internal static IFramework              Framework       { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager     { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider    GameInterop     { get; private set; } = null!;
    [PluginService] internal static ITextureProvider        TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPartyList              PartyList       { get; private set; } = null!;
    [PluginService] internal static IFlyTextGui             FlyTextGui      { get; private set; } = null!;

    // ── Cross-assembly static accessor (for MinimalMeterUmbra) ─────────────────
    public static Plugin? Instance { get; private set; }

    // ── Plugin state ──────────────────────────────────────────────────────────
    internal Configuration Config  { get; }
    public   CombatTracker Tracker { get; }
    public   IinactSource Iinact  { get; }

    internal readonly MainWindow     _mainWindow;
    internal readonly HistoryWindow  _historyWindow;
    internal readonly SettingsWindow _settingsWindow;
    internal readonly StatusApi      _statusApi;
    internal readonly IpcBridge      _ipcBridge;

    private const string CmdMain     = "/dm";
    private const string CmdHistory  = "/dmhistory";
    private const string CmdSettings = "/dmsettings";

    // ── Constructor ───────────────────────────────────────────────────────────
    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.MigrateIfNeeded();

        var configDir = PluginInterface.GetPluginConfigDirectory();

        Tracker = new CombatTracker(
            GameInterop, Log, Condition, ObjectTable,
            ClientState, Framework, DataManager, PartyList,
            FlyTextGui, ChatGui, Config, configDir);

        Iinact = new IinactSource(PluginInterface, Log);
        Iinact.Start();

        _mainWindow     = new MainWindow(this);
        _historyWindow  = new HistoryWindow(this);
        _settingsWindow = new SettingsWindow(this);
        _statusApi      = new StatusApi(this);
        _ipcBridge      = new IpcBridge(this);

        // Clear any history-window pin the moment a fresh combat session starts —
        // the user clearly wants live data once they swing again.
        Tracker.OnSessionStarted += _ => _historyWindow.ClearPin();

        CommandManager.AddHandler(CmdMain, new Dalamud.Game.Command.CommandInfo(OnMainCommand)
        {
            HelpMessage = "Toggle the Minimal Meter window."
        });
        CommandManager.AddHandler(CmdHistory, new Dalamud.Game.Command.CommandInfo(OnHistoryCommand)
        {
            HelpMessage = "Open the Minimal Meter session history."
        });
        CommandManager.AddHandler(CmdSettings, new Dalamud.Game.Command.CommandInfo(OnSettingsCommand)
        {
            HelpMessage = "Open Minimal Meter settings."
        });

        PluginInterface.UiBuilder.Draw          += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi    += OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi  += OnOpenSettings;

        Instance = this;
        Log.Info("MinimalMeter: Plugin loaded.");
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    private void OnMainCommand(string cmd, string args)
    {
        _mainWindow.IsVisible = !_mainWindow.IsVisible;
        Config.MeterVisible   = _mainWindow.IsVisible;
        SaveConfig();
    }
    private void OnHistoryCommand(string cmd, string args)  => _historyWindow.IsVisible  = !_historyWindow.IsVisible;
    private void OnSettingsCommand(string cmd, string args) => _settingsWindow.IsVisible = !_settingsWindow.IsVisible;

    private void OnOpenMainUi()   => _mainWindow.IsVisible     = true;
    private void OnOpenSettings() => _settingsWindow.IsVisible = true;

    // ── Draw loop ─────────────────────────────────────────────────────────────
    private void OnDraw()
    {
        _mainWindow.Draw();
        _historyWindow.Draw();
        _settingsWindow.Draw();
    }

    // ── Save config ───────────────────────────────────────────────────────────
    internal void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    // ── Dispose ───────────────────────────────────────────────────────────────
    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw         -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi   -= OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenSettings;

        CommandManager.RemoveHandler(CmdMain);
        CommandManager.RemoveHandler(CmdHistory);
        CommandManager.RemoveHandler(CmdSettings);

        // StatusApi serves from a background thread and reaches into the main
        // window's canvas, so it has to stop before that window is torn down.
        _statusApi.Dispose();
        _mainWindow.Dispose();
        _historyWindow.Dispose();
        _settingsWindow.Dispose();

        _ipcBridge.Dispose();
        Iinact.Dispose();
        Tracker.Dispose();
        Instance = null;

        Log.Info("MinimalMeter: Plugin unloaded.");
    }
}
