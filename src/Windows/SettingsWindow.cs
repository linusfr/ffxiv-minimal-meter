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
    private int  _sizePct;
    private bool _sizePctEditing;
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
            if (ImGui.BeginTabItem("Preview"))    { DrawPreviewTab();    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Developer"))  { DrawDeveloperTab();  ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    // ── Display tab ───────────────────────────────────────────────────────────
    private void DrawDisplayTab()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Name Display");
        ImGui.Separator();

        // One choice, not two checkboxes wired to the same boolean with a note
        // explaining why they fight each other.
        if (ImGui.RadioButton("Full names", Config.ShowFullName))
        {
            Config.ShowFullName = true;
            Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Initials only (T.M.)", !Config.ShowFullName))
        {
            Config.ShowFullName = false;
            Save();
        }

        var showSvr = Config.ShowPlayerServer;
        if (ImGui.Checkbox("Show @Server suffix", ref showSvr))
        {
            Config.ShowPlayerServer = showSvr;
            Save();
        }

        ImGui.TextDisabled("Job column");
        foreach (JobDisplay jd in Enum.GetValues<JobDisplay>())
        {
            var label = jd switch
            {
                JobDisplay.None => "Hidden",
                JobDisplay.Icon => "Icon",
                JobDisplay.Text => "Text (WAR, WHM)",
                _               => jd.ToString(),
            };
            if (ImGui.RadioButton(label, Config.JobColumn == jd))
            {
                Config.JobColumn = jd;
                Save();
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Appearance");
        ImGui.Separator();

        var panelAlpha = Config.PanelAlpha < 0f ? 1f : Config.PanelAlpha;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Background opacity", ref panelAlpha, 0.0f, 1.0f, "%.2f"))
        {
            Config.PanelAlpha = panelAlpha;
            Save();
        }
        ImGui.TextWrapped(
            "0 leaves only bars and text over the game. Text shadow below is what " +
            "keeps names readable once the panel is gone.");

        var barAlpha = Config.BarAlpha;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Bar opacity", ref barAlpha, 0.0f, 1.0f, "%.2f"))
        {
            Config.BarAlpha = barAlpha;
            Save();
        }

        var shadow = Config.TextShadow;
        if (ImGui.Checkbox("Text outline", ref shadow))
        {
            Config.TextShadow = shadow;
            Save();
        }
        ImGui.TextWrapped(
            "Surrounds every glyph rather than darkening one side, so names stay " +
            "readable over bright ground effects without raising the background.");

        if (Config.TextShadow)
        {
            var strength = Config.OutlineStrength <= 0f ? 1f : Config.OutlineStrength;
            ImGui.SetNextItemWidth(200);
            if (ImGui.SliderFloat("Outline strength", ref strength, 0.25f, 2.0f, "%.2f"))
            {
                Config.OutlineStrength = strength;
                Save();
            }
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Values");
        ImGui.Separator();

        var abbrev = Config.AbbreviateValues;
        if (ImGui.Checkbox("Abbreviate numbers (12.4k rather than 12,431)", ref abbrev))
        {
            Config.AbbreviateValues = abbrev;
            Save();
        }

        var showPct = Config.ShowPercentage;
        if (ImGui.Checkbox("Show % of group total", ref showPct))
        {
            Config.ShowPercentage = showPct;
            Save();
        }

        ImGui.Spacing();
    }

    // ── Preview tab ───────────────────────────────────────────────────────────
    private void DrawPreviewTab()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Placement preview");
        ImGui.Separator();
        ImGui.TextWrapped(
            "Fills the meter with synthetic combatants so you can position and " +
            "size it without being in content. Values climb like a real pull, and " +
            "healers carry healing so the optional columns show up where they " +
            "would in a real party.");

        ImGui.Spacing();

        foreach (var (label, count) in DemoSession.Presets)
        {
            if (ImGui.RadioButton(label, Config.DemoCombatants == count))
            {
                Config.DemoCombatants = count;
                Save();
            }
        }
        if (ImGui.RadioButton("Off", Config.DemoCombatants == 0))
        {
            Config.DemoCombatants = 0;
            Save();
        }

        if (Config.DemoCombatants > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f),
                "Preview is active — the meter shows synthetic data, and every " +
                "auto-hide rule is suspended so you can position it anywhere.");
        }
    }

    // ── Developer tab ─────────────────────────────────────────────────────────
    private void DrawDeveloperTab()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Tick attribution");
        ImGui.Separator();

        var dotAttr = Config.EnableDotAttribution;
        if (ImGui.Checkbox("Credit DoT and HoT ticks via ActorControl", ref dotAttr))
        {
            Config.EnableDotAttribution = dotAttr;
            Save();
        }
        ImGui.TextWrapped(
            "Hooks ActorControl category 0x17, where damage- and healing-over-" +
            "time ticks arrive. The packet names its own source actor, so ticks " +
            "are credited directly. This is the only way to see other players' " +
            "DoTs and HoTs without IINACT.");

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Diagnostics");
        ImGui.Separator();

        var logDot = Config.LogDotPackets;
        if (ImGui.Checkbox("Log raw ActorControl packets", ref logDot))
        {
            Config.LogDotPackets = logDot;
            Save();
        }
        ImGui.TextWrapped(
            "Writes every ActorControl category and its arguments to the Dalamud " +
            "log: 12 lines per category, with 0x17 (the tick packets) uncapped. " +
            "Toggling it off and on starts a fresh capture. Noisy in combat, so " +
            "leave it off unless you are checking something.");
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

        var byAlliance = Config.GroupByAlliance;
        if (ImGui.Checkbox("Split non-party players by alliance", ref byAlliance))
        {
            Config.GroupByAlliance = byAlliance;
            Save();
        }
        ImGui.TextWrapped(
            "In alliance raids, shows Alliance A / B / C instead of one Friendly " +
            "lump. Needs group headers on to be legible, and does nothing outside " +
            "alliance content.");

        var autoHideSolo = Config.AutoHideSoloGroupHeader;
        if (ImGui.Checkbox("Hide the group header when there is only one group", ref autoHideSolo))
        {
            Config.AutoHideSoloGroupHeader = autoHideSolo;
            Save();
        }

        var showGroupHeaders = Config.ShowGroupHeaders;
        if (ImGui.Checkbox("Show group accordion headers", ref showGroupHeaders))
        {
            Config.ShowGroupHeaders = showGroupHeaders;
            Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Total row");
        ImGui.Separator();


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

        var showDmg = Config.ShowDamageValue;
        if (ImGui.Checkbox("Total damage", ref showDmg))
        {
            Config.ShowDamageValue = showDmg;
            Save();
        }

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

        var showTaken = Config.ShowDamageTaken;
        if (ImGui.Checkbox("Damage taken", ref showTaken))
        {
            Config.ShowDamageTaken = showTaken;
            Save();
        }

        var showAvoid = Config.ShowAvoidable;
        if (ImGui.Checkbox("Avoidable damage taken", ref showAvoid))
        {
            Config.ShowAvoidable = showAvoid;
            Save();
        }

        var showOverheal = Config.ShowOverhealing;
        if (ImGui.Checkbox("Overhealing", ref showOverheal))
        {
            Config.ShowOverhealing = showOverheal;
            Save();
        }

        var showHeal = Config.ShowHealingValue;
        if (ImGui.Checkbox("Total healing", ref showHeal))
        {
            Config.ShowHealingValue = showHeal;
            Save();
        }
        ImGui.TextDisabled("A column is skipped when the selected metric already shows it.");
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
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Meter");
        ImGui.Separator();

        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("Metric", Config.CurrentMeter.DisplayName()))
        {
            foreach (MeterType mt in Enum.GetValues<MeterType>())
            {
                if (ImGui.Selectable(mt.DisplayName(), mt == Config.CurrentMeter))
                {
                    Config.CurrentMeter = mt;
                    Save();
                }
            }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped("Also sets the sort order.");

        if (Plugin.Instance?._historyWindow.PinnedSession != null)
        {
            if (ImGui.Button("\u2190 Return to live"))
                Plugin.Instance._historyWindow.ClearPin();
            ImGui.SameLine();
            ImGui.TextDisabled("(a past session is pinned)");
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Sizing");
        ImGui.Separator();

        // Buffered: re-deriving from config each frame clamped the first digit
        // you typed (1 -> 50) and threw the rest away, leaving only the step
        // buttons usable. Clamp on commit, not on every keystroke.
        if (!_sizePctEditing)
            _sizePct = (int)MathF.Round((Config.UiScale <= 0f ? 1f : Config.UiScale) * 100f);

        ImGui.SetNextItemWidth(200);
        ImGui.InputInt("Size (%)", ref _sizePct, 5, 25);
        _sizePctEditing = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _sizePct = Math.Clamp(_sizePct, 50, 250);
            Config.UiScale = _sizePct / 100f;
            Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("50-250");
        ImGui.TextWrapped(
            "Rows, text, the job icon and padding all scale from this, so the " +
            "meter grows or shrinks as one piece.");

        ImGui.TextDisabled("Grow direction");
        foreach (GrowDirection gd in Enum.GetValues<GrowDirection>())
        {
            var label = gd == GrowDirection.Up ? "Upward (pin bottom edge)"
                                               : "Downward (pin top edge)";
            if (ImGui.RadioButton(label, Config.Grow == gd))
            {
                Config.Grow = gd;
                Save();
            }
        }
        ImGui.TextWrapped(
            "Height follows the number of combatants either way; this picks which " +
            "edge stays put as rows appear.");

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
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Auto-hide");
        ImGui.Separator();
        ImGui.TextWrapped("Hides the meter without touching the /dm toggle.");

        // The override is easy to forget and looks exactly like these rules being
        // broken, so say so here rather than only in the Preview tab.
        if (Config.DemoCombatants > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f),
                "Placement preview is ON — it overrides every rule below, so the " +
                "meter stays visible everywhere until you turn it off in the " +
                "Preview tab.");
        }

        ImGui.TextDisabled("Out of combat");
        foreach (HideDelay hd in Enum.GetValues<HideDelay>())
        {
            var label = hd switch
            {
                HideDelay.Never       => "Never hide",
                HideDelay.Immediately => "Immediately",
                HideDelay.After10s    => "After 10s",
                HideDelay.After30s    => "After 30s",
                _                     => hd.ToString(),
            };
            if (ImGui.RadioButton(label, Config.HideOutOfCombat == hd))
            {
                Config.HideOutOfCombat = hd;
                Save();
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();

        var hideEmpty = Config.HideWhenEmpty;
        if (ImGui.Checkbox("Hide when there is no encounter data", ref hideEmpty))
        {
            Config.HideWhenEmpty = hideEmpty;
            Save();
        }
        ImGui.TextWrapped(
            "Uses the same delay as above, so an empty meter lingers exactly as " +
            "long as a finished pull.");

        ImGui.Spacing();
        ImGui.TextDisabled("Show the meter in");

        var inst = Config.ShowInInstances;
        if (ImGui.Checkbox("Instances (dungeons, trials, raids)", ref inst))
        {
            Config.ShowInInstances = inst;
            Save();
        }

        var pvp = Config.ShowInPvP;
        if (ImGui.Checkbox("PvP", ref pvp))
        {
            Config.ShowInPvP = pvp;
            Save();
        }

        var open = Config.ShowInOpenWorld;
        if (ImGui.Checkbox("Open world (overworld, cities, FATEs)", ref open))
        {
            Config.ShowInOpenWorld = open;
            Save();
        }

        ImGui.TextDisabled("Placement preview overrides all of these.");

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Behaviour");
        ImGui.Separator();

        var locked = Config.LockWindow;
        if (ImGui.Checkbox("Lock window (no move / resize)", ref locked))
        {
            Config.LockWindow = locked;
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
