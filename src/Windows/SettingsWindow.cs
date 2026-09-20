using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace MinimalMeter.Windows;

/// <summary>
/// Settings popup window. Organized into sections:
/// Display, Bar Colors, Window Style, History.
/// </summary>
public sealed class SettingsWindow : IDisposable
{
    private bool _isVisible;
    public bool IsVisible { get => _isVisible; set => _isVisible = value; }

    private readonly Plugin _plugin;
    private Configuration Config => _plugin.Config;

    public SettingsWindow(Plugin plugin)
    {
        _plugin = plugin;
    }

    public void Draw()
    {
        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(440, 520), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.95f);

        if (!ImGui.Begin("Minimal Meter — Settings###MinimalMeterSettings", ref _isVisible))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("##SettingsTabs"))
        {
            if (ImGui.BeginTabItem("Display"))    { DrawDisplayTab();    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Filters"))    { DrawFiltersTab();    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Bar Colors")) { DrawColorsTab();     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Window"))     { DrawWindowTab();     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("History"))    { DrawHistoryTab();    ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    // ── Display tab ───────────────────────────────────────────────────────────
    private void DrawDisplayTab()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Name Display");
        ImGui.Separator();

        var showFull = Config.ShowFullName;
        if (ImGui.Checkbox("Full player names", ref showFull))
        {
            Config.ShowFullName = showFull;
            Save();
        }

        using (new DisabledScope(Config.ShowFullName))
        {
            ImGui.SameLine(200);
            ImGui.TextDisabled("(disabled when full names on)");
        }

        var initOnly = !Config.ShowFullName;
        if (ImGui.Checkbox("Initials only  (e.g. T.M.)", ref initOnly))
        {
            Config.ShowFullName = !initOnly;
            Save();
        }

        var showSvr = Config.ShowPlayerServer;
        if (ImGui.Checkbox("Show @Server suffix", ref showSvr))
        {
            Config.ShowPlayerServer = showSvr;
            Save();
        }

        var showIcon = Config.ShowJobIcon;
        if (ImGui.Checkbox("Show job icon", ref showIcon))
        {
            Config.ShowJobIcon = showIcon;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Values");
        ImGui.Separator();

        var showVal = Config.ShowFullValues;
        if (ImGui.Checkbox("Show full numeric value", ref showVal))
        {
            Config.ShowFullValues = showVal;
            Save();
        }

        var showPct = Config.ShowPercentage;
        if (ImGui.Checkbox("Show % of group total", ref showPct))
        {
            Config.ShowPercentage = showPct;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Row");
        ImGui.Separator();

        var rowH = Config.RowHeight;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Row height (px)", ref rowH, 16f, 40f))
        {
            Config.RowHeight = rowH;
            Save();
        }
    }

    // ── Filters tab ───────────────────────────────────────────────────────────
    private void DrawFiltersTab()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Groups");
        ImGui.Separator();

        var showEnemy = Config.ShowEnemyGroup;
        if (ImGui.Checkbox("Show enemy group", ref showEnemy))
        {
            Config.ShowEnemyGroup = showEnemy;
            Save();
        }

        var showFriendly = Config.ShowFriendlyGroup;
        if (ImGui.Checkbox("Show friendly (non-party) group", ref showFriendly))
        {
            Config.ShowFriendlyGroup = showFriendly;
            Save();
        }

        var showGroupHeaders = Config.ShowGroupHeaders;
        if (ImGui.Checkbox("Show group accordion headers", ref showGroupHeaders))
        {
            Config.ShowGroupHeaders = showGroupHeaders;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Header");
        ImGui.Separator();

        var showTitleBar = Config.ShowTitleBar;
        if (ImGui.Checkbox("Show title bar (\"DAMAGE METER\" strip)", ref showTitleBar))
        {
            Config.ShowTitleBar = showTitleBar;
            Save();
        }

        var showTotal = Config.ShowEncounterTotal;
        if (ImGui.Checkbox("Show total stat line in encounter header", ref showTotal))
        {
            Config.ShowEncounterTotal = showTotal;
            Save();
        }
    }

    // ── Colors tab ────────────────────────────────────────────────────────────
    private void DrawColorsTab()
    {
        ImGui.TextWrapped("Click a color swatch to edit. Colors apply to bar fill for each meter type.");
        ImGui.Spacing();

        foreach (MeterType mt in Enum.GetValues<MeterType>())
        {
            var current = Config.GetBarColor(mt);
            var vec = ImGui.ColorConvertU32ToFloat4(current);
            if (ImGui.ColorEdit4(mt.DisplayName() + "##col_" + mt, ref vec,
                ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreview))
            {
                Config.BarColors[mt] = ImGui.ColorConvertFloat4ToU32(vec);
                Save();
            }
        }
    }

    // ── Window tab ────────────────────────────────────────────────────────────
    private void DrawWindowTab()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Data source");
        ImGui.Separator();

        foreach (DataSource src in Enum.GetValues<DataSource>())
        {
            var label = src switch
            {
                DataSource.Auto   => "Auto (use IINACT when available)",
                DataSource.Hooks  => "Built-in hooks only",
                DataSource.Iinact => "IINACT only",
                _                 => src.ToString(),
            };
            if (ImGui.RadioButton(label, Config.Source == src))
            {
                Config.Source = src;
                Save();
            }
        }

        var iinact = Plugin.Instance?.Iinact;
        var statusColor = iinact is { Connected: true }
            ? new Vector4(0.4f, 0.9f, 0.4f, 1f)
            : new Vector4(0.9f, 0.6f, 0.3f, 1f);
        ImGui.TextColored(statusColor, "IINACT: " + (iinact?.Status ?? "unknown"));

        if (iinact is { Connected: false })
        {
            ImGui.TextWrapped(
                "Without IINACT the meter cannot see other players' damage-over-time " +
                "ticks at all, so DoT-heavy jobs will read low. Your own numbers are " +
                "unaffected.");
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Extra row figures");
        ImGui.Separator();

        var showDps = Config.ShowDps;
        if (ImGui.Checkbox("Damage per second", ref showDps))
        {
            Config.ShowDps = showDps;
            Save();
        }

        var showHps = Config.ShowHps;
        if (ImGui.Checkbox("Healing per second", ref showHps))
        {
            Config.ShowHps = showHps;
            Save();
        }

        var showHeal = Config.ShowHealingValue;
        if (ImGui.Checkbox("Total healing", ref showHeal))
        {
            Config.ShowHealingValue = showHeal;
            Save();
        }
        ImGui.TextWrapped(
            "Healing figures are drawn in green, and only for combatants who " +
            "actually healed — a party of DPS keeps the row it had.");

        if ((Config.ShowHps || Config.ShowHealingValue) && !Config.EnableDotAttribution)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f),
                "Healing is direct-heals-only until DoT/HoT attribution below is " +
                "enabled — HoT ticks are invisible to the meter without it.");
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), "Other players' DoTs (experimental)");
        ImGui.Separator();

        var dotAttr = Config.EnableDotAttribution;
        if (ImGui.Checkbox("Credit DoT and HoT ticks via ActorControl", ref dotAttr))
        {
            Config.EnableDotAttribution = dotAttr;
            Save();
        }
        ImGui.TextWrapped(
            "Hooks the packet DoT and HoT ticks actually arrive on (ActorControl " +
            "category 0x17 carries both), and credits them to whoever applied the " +
            "status. This is the only way to see other players' damage and healing " +
            "over time without IINACT.");
        ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f),
            "UNVERIFIED: the packet's argument layout has not been confirmed in " +
            "game. Verify with the log option below before trusting the numbers.");

        var logDot = Config.LogDotPackets;
        if (ImGui.Checkbox("Log raw DoT packets to the Dalamud log", ref logDot))
        {
            Config.LogDotPackets = logDot;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Style");
        ImGui.Separator();

        foreach (WindowStyle style in Enum.GetValues<WindowStyle>())
        {
            var selected = Config.Style == style;
            if (ImGui.RadioButton(style.ToString(), selected))
            {
                Config.Style = style;
                Save();
            }
        }

        if (Config.Style.IsTransparent())
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Transparent");
            ImGui.Separator();

            var barAlpha = Config.BarAlpha;
            ImGui.SetNextItemWidth(200);
            if (ImGui.SliderFloat("Bar opacity", ref barAlpha, 0.05f, 1.0f, "%.2f"))
            {
                Config.BarAlpha = barAlpha;
                Save();
            }

            var shadow = Config.TextShadow;
            if (ImGui.Checkbox("Text shadow (keeps text readable over the game)", ref shadow))
            {
                Config.TextShadow = shadow;
                Save();
            }

            var autoHide = Config.AutoHideToolbar;
            if (ImGui.Checkbox("Hide toolbar until hovered", ref autoHide))
            {
                Config.AutoHideToolbar = autoHide;
                Save();
            }
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Behaviour");
        ImGui.Separator();

        var grow = Config.GrowUpward;
        if (ImGui.Checkbox("Grow upward (pin the bottom edge)", ref grow))
        {
            Config.GrowUpward = grow;
            Save();
        }
        ImGui.TextWrapped(
            "Height follows the number of combatants and the bottom edge stays " +
            "put, so rows appear above rather than scrolling. Width is still " +
            "draggable.");

        if (Config.GrowUpward)
        {
            var maxRows = Config.MaxGrowRows;
            ImGui.SetNextItemWidth(200);
            if (ImGui.SliderInt("Max rows", ref maxRows, 1, 24))
            {
                Config.MaxGrowRows = maxRows;
                Save();
            }
            ImGui.TextWrapped(
                "8 is a full party. An alliance raid is still tracked in full — " +
                "the window just stops growing and scrolls past this many rows.");
        }

        ImGui.Spacing();

        var locked = Config.LockWindow;
        if (ImGui.Checkbox("Lock window (no move / resize)", ref locked))
        {
            Config.LockWindow = locked;
            Save();
        }

        var opacity = Config.Opacity;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Opacity", ref opacity, 0.2f, 1f))
        {
            Config.Opacity = opacity;
            Save();
        }
    }

    // ── History tab ───────────────────────────────────────────────────────────
    private void DrawHistoryTab()
    {
        ImGui.TextWrapped(
            "Recent sessions are kept automatically up to the limit below. " +
            "When full, the oldest is deleted. Manually saved sessions are never pruned.");
        ImGui.Spacing();

        var maxH = Config.MaxTempHistory;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("Max recent sessions", ref maxH, 1, 50))
        {
            Config.MaxTempHistory = maxH;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f),
            $"Stored: {_plugin.Tracker.Store.TempSessions.Count} recent, " +
            $"{_plugin.Tracker.Store.SavedSessions.Count} saved.");

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.6f, 0.1f, 0.1f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
        if (ImGui.Button("Clear ALL recent sessions"))
        {
            // A pin into one of these would otherwise keep rendering a deleted
            // session, badge and all, with no row left in the history list.
            _plugin._historyWindow.ClearPin();
            lock (CombatTracker.StateGate)
                _plugin.Tracker.Store.TempSessions.Clear();
            _plugin.Tracker.SaveStore();
        }
        ImGui.PopStyleColor(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void Save() => _plugin.SaveConfig();

    public void Dispose() { }

    // Simple RAII helper for disabled state
    private readonly struct DisabledScope : IDisposable
    {
        private readonly bool _wasDisabled;
        public DisabledScope(bool disable)
        {
            _wasDisabled = disable;
            if (disable) ImGui.BeginDisabled();
        }
        public void Dispose() { if (_wasDisabled) ImGui.EndDisabled(); }
    }
}
