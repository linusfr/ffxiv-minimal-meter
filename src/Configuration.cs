using System.Collections.Generic;
using Dalamud.Configuration;

namespace MinimalMeter;

public enum HideDelay
{
    Never,       // always visible
    Immediately, // the moment combat ends
    After10s,
    After30s,
}

public enum GrowDirection
{
    Up,    // bottom edge pinned, rows appear above — sits above hotbars
    Down,  // top edge pinned, rows appear below
}

public enum JobDisplay
{
    None,  // no job column at all
    Icon,  // the job's icon
    Text,  // the abbreviation (WAR, WHM)
}

public enum DataSource
{
    Auto,   // Use IINACT when it is connected, otherwise the built-in hooks
    Hooks,  // Always use the built-in ActionEffect hooks
    Iinact, // Always use IINACT; show nothing if it is unavailable
}

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ── Data source ───────────────────────────────────────────────────────────
    /// Where combat numbers come from. Auto prefers IINACT when present because
    /// the built-in hooks cannot see other players' DoT ticks at all.
    public DataSource Source { get; set; } = DataSource.Auto;

    // ── Other players' DoTs and HoTs ──────────────────────────────────────────
    /// Hook ActorControl (category 0x17) and credit damage- and healing-over-time
    /// ticks to the source actor the packet names. This is the only way to see
    /// other players' DoTs and HoTs without IINACT, and without it a DoT-heavy
    /// job reads low — so it is on. See docs/DOT_ATTRIBUTION.md.
    public bool EnableDotAttribution { get; set; } = true;
    /// Log raw ActorControl packets to the Dalamud log, for confirming the packet
    /// layout. Noisy in combat; a diagnostic, not a feature.
    public bool LogDotPackets { get; set; } = false;

    // ── Meter ────────────────────────────────────────────────────────────────
    public MeterType CurrentMeter { get; set; } = MeterType.DamageDealt;

    // ── Display ──────────────────────────────────────────────────────────────
    /// Abbreviate numbers (12.4k) rather than printing them in full (12,431).
    /// This used to be ShowFullValues, which despite the name only hid the column
    /// — the numbers were always abbreviated.
    public bool AbbreviateValues { get; set; } = true;
    /// Show each player's share of the group total (e.g. "43%"). Off by default:
    /// the bar already shows the share, so the number is a second telling of it.
    public bool ShowPercentage  { get; set; } = false;

    // ── Extra per-row figures ────────────────────────────────────────────────
    // Drawn right-to-left after the primary value, each independently optional.
    // The healing pair renders only for combatants who actually healed, so a
    // party of seven DPS and one healer gains one number, not eight columns.
    /// Total damage dealt alongside the primary value.
    public bool ShowDamageValue   { get; set; } = false;
    /// Damage per second alongside the primary value.
    public bool ShowDps           { get; set; } = false;
    /// Total healing done, where nonzero.
    public bool ShowHealingValue  { get; set; } = false;
    /// Healing per second, where nonzero.
    public bool ShowHps           { get; set; } = false;
    /// Total damage taken, where nonzero.
    public bool ShowDamageTaken   { get; set; } = false;
    /// Avoidable damage taken, where nonzero.
    public bool ShowAvoidable     { get; set; } = false;
    /// Healing wasted on full-HP targets, where nonzero.
    public bool ShowOverhealing   { get; set; } = false;
    /// Show "@Server" suffix on player names.
    public bool ShowPlayerServer { get; set; } = false;
    /// Show player's full name. If false, uses initials only.
    public bool ShowFullName    { get; set; } = true;
    /// How the job is shown at the left of each row. One setting rather than two
    /// booleans: "text instead of icon" used to require "show icon" to be on,
    /// which is a contradiction nobody should have to work out.
    public JobDisplay JobColumn { get; set; } = JobDisplay.Icon;

    // Superseded by JobColumn; retained so v2 configs can be migrated, and read
    // nowhere else.
    public bool ShowJobIcon     { get; set; } = true;
    public bool JobAsText       { get; set; } = false;
    /// Number of synthetic combatants to display for placement and styling
    /// without being in content. 0 = off. See DemoSession.
    public int DemoCombatants  { get; set; } = 0;

    /// Whether the meter window is open. Visible on first install so the plugin
    /// is not invisible until you find the command; closing it persists.
    public bool MeterVisible    { get; set; } = true;

    // ── Where the meter is active ─────────────────────────────────────────────
    // Phrased as "show here" rather than "hide there": the question you actually
    // ask yourself is where you want a meter, not where you object to one.
    /// Dungeons, trials, raids — anything that binds you to a duty.
    public bool ShowInInstances { get; set; } = true;
    /// PvP content.
    public bool ShowInPvP       { get; set; } = true;
    /// Everywhere else: overworld, cities, hunts, FATEs.
    public bool ShowInOpenWorld { get; set; } = true;


    /// Hide the meter once combat ends, optionally after a delay so the numbers
    /// are still readable for a moment after a pull.
    public HideDelay HideOutOfCombat { get; set; } = HideDelay.Never;
    /// Hide when there is nothing to show — no session, or a session with no
    /// combatants in it. Uses the same delay as HideOutOfCombat, so an empty
    /// meter lingers exactly as long as a finished one.
    public bool HideWhenEmpty { get; set; } = true;

    /// Lock the window in place (no dragging/resizing).
    public bool LockWindow      { get; set; } = false;

    /// Which edge stays put as the meter grows. Height is content-driven either
    /// way, so nothing is hidden and width stays draggable. Up is the default:
    /// a meter anchored above your hotbars should not creep down over them.
    public GrowDirection Grow   { get; set; } = GrowDirection.Up;

    // Superseded by Grow; retained so v2 configs migrate.
    public bool GrowUpward      { get; set; } = true;
    /// Ceiling for GrowUpward, counted in rows rather than pixels so it keeps
    /// its meaning when the size scale changes. Defaults to 24 so a full alliance
    /// raid fits; 72 covers Frontline, and lower keeps the meter party-sized.
    public int MaxGrowRows      { get; set; } = 24;

    // ── Filters ───────────────────────────────────────────────────────────────
    /// Include enemy combatants in the meter.
    public bool ShowEnemyGroup     { get; set; } = false;
    /// Include friendly (non-party) players in the meter.
    public bool ShowFriendlyGroup  { get; set; } = false;
    /// Show the combined total row above the combatants.
    public bool ShowEncounterTotal { get; set; } = true;
    /// Split non-party players into their alliance groups instead of one
    /// "Friendly" lump. Only does anything in alliance content; elsewhere there
    /// are no alliances to split by.
    public bool GroupByAlliance    { get; set; } = false;
    /// Drop the group header when there is only one group to head. With the
    /// other groups filtered out there is nothing to distinguish, so the header
    /// is a row of chrome labelling the obvious.
    public bool AutoHideSoloGroupHeader { get; set; } = true;
    /// Show group accordion headers (Party / Friendly / Enemies). Off by
    /// default: with the other two groups hidden there is only one group, and a
    /// header for it is pure chrome.
    public bool ShowGroupHeaders   { get; set; } = false;

    // ── Colors (ImGui ABGR uint — 0xAABBGGRR) ────────────────────────────────
    // Default: red for damage, green for healing, blue for damage taken,
    //          teal for overhealing, orange for avoidable damage.
    public Dictionary<MeterType, uint> BarColors { get; set; } = new()
    {
        [MeterType.DamageDealt]          = 0xCC2828C8,  // red
        [MeterType.DPS]                  = 0xCC2828C8,  // red
        [MeterType.HealingDone]          = 0xCC28C828,  // green
        [MeterType.HPS]                  = 0xCC28C828,  // green
        // Overhealing and avoidable damage get their own colours: sharing with
        // healing and damage-taken made the two pairs indistinguishable when
        // switching metric, which is exactly when you need to tell them apart.
        [MeterType.Overhealing]          = 0xCCA0B028,  // teal
        [MeterType.DamageTaken]          = 0xCCC82828,  // blue
        [MeterType.AvoidableDamageTaken] = 0xCC4CB4E8,  // amber
    };

    // ── Window ────────────────────────────────────────────────────────────────


    /// One dial for size. Rows, glyphs, the job icon and padding all derive
    /// from it, so the meter scales as a single piece — previously RowHeight and
    /// FontScale multiplied together, which meant changing the row height also
    /// resized the text.
    public float       UiScale { get; set; } = 1.0f;


    // ── Transparent style ─────────────────────────────────────────────────────
    /// Drop shadow behind glyphs. Without a panel behind it, text sits on
    /// whatever the game is drawing, so this is what keeps it readable.
    public bool  TextShadow { get; set; } = true;
    /// Multiplier on the outline width. The default is tuned for 100% size; go
    /// heavier if you run a low background opacity over bright content.
    public float OutlineStrength { get; set; } = 0.5f;
    /// Opacity of the panel behind the rows — the background, the row striping
    /// and the separators. 0 is fully transparent, which leaves only bars and
    /// text floating over the game. Defaults to 0.5 to match the game's own chat
    /// window: enough to sit the text on something, not enough to be a box.
    public float PanelAlpha { get; set; } = 0.5f;
    /// Opacity of the proportional bar fill, 0..1. The bar is the only fill
    /// left in Transparent, so this is the main dial for how loud it reads.
    public float BarAlpha   { get; set; } = 0.38f;

    // ── History ────────────────────────────────────────────────────────────────
    /// Maximum number of temporary (auto) sessions to keep before pruning oldest.
    public int MaxTempHistory { get; set; } = 20;

    // ── Internal ──────────────────────────────────────────────────────────────
    public void MigrateIfNeeded()
    {
        // v1 → v2: overhealing and avoidable damage shared a colour with their
        // siblings. Only replace values still sitting on the old duplicate, so a
        // deliberate choice is never overwritten.
        if (Version < 2)
        {
            if (BarColors.TryGetValue(MeterType.Overhealing, out var oh) && oh == 0xCC28C828)
                BarColors[MeterType.Overhealing] = 0xCCA0B028;
            if (BarColors.TryGetValue(MeterType.AvoidableDamageTaken, out var av) && av == 0xCCC82828)
                BarColors[MeterType.AvoidableDamageTaken] = 0xCC4CB4E8;
            Version = 2;
        }

        // v2 → v3: ShowJobIcon + JobAsText collapse into JobColumn.
        if (Version < 3)
        {
            JobColumn = !ShowJobIcon ? JobDisplay.None
                      : JobAsText   ? JobDisplay.Text
                                    : JobDisplay.Icon;
            Grow = GrowUpward ? GrowDirection.Up : GrowDirection.Down;
            Version = 3;
        }
    }

    // Helpers -----------------------------------------------------------------
    public uint GetBarColor(MeterType type)
        => BarColors.TryGetValue(type, out var c) ? c : 0xCC888888;
}
