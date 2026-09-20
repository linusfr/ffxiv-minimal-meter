using System.Collections.Generic;
using Dalamud.Configuration;

namespace MinimalMeter;

public enum WindowStyle
{
    Classic,     // Dark background, bold title bar
    Minimal,     // Borderless, semi-transparent
    Modern,      // Rounded corners, subtle gradient background
    Transparent, // No window, no panel — bars and text floating over the game
}

public static class WindowStyleExtensions
{
    /// Single-line condensed rows. Minimal and Transparent share the row layout.
    public static bool IsCompact(this WindowStyle s)
        => s is WindowStyle.Minimal or WindowStyle.Transparent;

    /// No window background, no border, no panel fills — only bars and text.
    public static bool IsTransparent(this WindowStyle s)
        => s == WindowStyle.Transparent;
}

public enum DataSource
{
    Auto,   // Use IINACT when it is connected, otherwise the built-in hooks
    Hooks,  // Always use the built-in ActionEffect hooks
    Iinact, // Always use IINACT; show nothing if it is unavailable
}

public enum ViewMode
{
    Chart,  // Existing bar meter
    Graph,  // Line graph of selected metric over time, per combatant
}

[System.Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ── Data source ───────────────────────────────────────────────────────────
    /// Where combat numbers come from. Auto prefers IINACT when present because
    /// the built-in hooks cannot see other players' DoT ticks at all.
    public DataSource Source { get; set; } = DataSource.Auto;

    // ── Other players' DoTs (experimental) ────────────────────────────────────
    /// Hook ActorControl (category 0x17) to credit DoT ticks to whoever applied
    /// the status. This is the only way to see other players' damage over time
    /// without IINACT. OFF by default: the packet's argument layout has not been
    /// verified in game yet — see docs/DOT_ATTRIBUTION.md before trusting it.
    public bool EnableDotAttribution { get; set; } = false;
    /// Log every category 0x17 packet to the Dalamud log. Use this to verify the
    /// argument layout, then turn it back off — it is noisy in combat.
    public bool LogDotPackets { get; set; } = false;

    // ── Meter ────────────────────────────────────────────────────────────────
    public MeterType CurrentMeter { get; set; } = MeterType.DamageDealt;
    public ViewMode  CurrentView  { get; set; } = ViewMode.Chart;

    // ── Display ──────────────────────────────────────────────────────────────
    /// Show the exact numeric value next to the bar.
    public bool ShowFullValues  { get; set; } = true;
    /// Show each player's share of the group total (e.g. "43%").
    public bool ShowPercentage  { get; set; } = true;

    // ── Extra per-row figures ────────────────────────────────────────────────
    // Drawn right-to-left after the primary value, each independently optional.
    // The healing pair renders only for combatants who actually healed, so a
    // party of seven DPS and one healer gains one number, not eight columns.
    /// Damage per second alongside the primary value.
    public bool ShowDps           { get; set; } = false;
    /// Total healing done, where nonzero.
    public bool ShowHealingValue  { get; set; } = false;
    /// Healing per second, where nonzero.
    public bool ShowHps           { get; set; } = false;
    /// Show "@Server" suffix on player names.
    public bool ShowPlayerServer { get; set; } = true;
    /// Show player's full name. If false, uses initials only.
    public bool ShowFullName    { get; set; } = true;
    /// Show job icon to the left of each row.
    public bool ShowJobIcon     { get; set; } = true;
    /// Lock the window in place (no dragging/resizing).
    public bool LockWindow      { get; set; } = false;

    /// Pin the bottom edge and let the meter grow upward as combatants appear,
    /// instead of keeping a fixed box that scrolls. Height becomes content-driven
    /// so nothing is ever hidden; width stays draggable.
    public bool GrowUpward      { get; set; } = true;
    /// Ceiling for GrowUpward, counted in rows rather than pixels so it keeps
    /// its meaning when RowHeight changes. Defaults to 8 — a full party — which
    /// is the size the meter is worth glancing at. An alliance raid still tracks
    /// all 24, but the window stops growing and scrolls past this.
    public int MaxGrowRows      { get; set; } = 8;

    // ── Filters ───────────────────────────────────────────────────────────────
    /// Include enemy combatants in the meter.
    public bool ShowEnemyGroup     { get; set; } = true;
    /// Include friendly (non-party) players in the meter.
    public bool ShowFriendlyGroup  { get; set; } = true;
    /// Show "Total X:" stat line in the encounter header.
    public bool ShowEncounterTotal { get; set; } = true;
    /// Show group accordion headers (Party / Friendly / Enemies).
    public bool ShowGroupHeaders   { get; set; } = true;
    /// Show the "DAMAGE METER" title bar strip at the top.
    public bool ShowTitleBar       { get; set; } = true;

    // ── Colors (ImGui ABGR uint — 0xAABBGGRR) ────────────────────────────────
    // Default: red for damage, green for healing, blue for damage taken,
    //          teal for overhealing, orange for avoidable damage.
    public Dictionary<MeterType, uint> BarColors { get; set; } = new()
    {
        [MeterType.DamageDealt]          = 0xCC2828C8,  // red
        [MeterType.DPS]                  = 0xCC2828C8,  // red
        [MeterType.HealingDone]          = 0xCC28C828,  // green
        [MeterType.HPS]                  = 0xCC28C828,  // green
        [MeterType.Overhealing]          = 0xCC28C828,  // green
        [MeterType.DamageTaken]          = 0xCCC82828,  // blue
        [MeterType.AvoidableDamageTaken] = 0xCCC82828,  // blue
    };

    // ── Window ────────────────────────────────────────────────────────────────
    public WindowStyle Style   { get; set; } = WindowStyle.Modern;
    public float       Opacity { get; set; } = 0.92f;
    public float       RowHeight { get; set; } = 22f;

    // ── Transparent style ─────────────────────────────────────────────────────
    /// Drop shadow behind glyphs. Without a panel behind it, text sits on
    /// whatever the game is drawing, so this is what keeps it readable.
    public bool  TextShadow { get; set; } = true;
    /// Opacity of the proportional bar fill, 0..1. The bar is the only fill
    /// left in Transparent, so this is the main dial for how loud it reads.
    public float BarAlpha   { get; set; } = 0.38f;
    /// Hide the toolbar until the cursor is over the meter.
    public bool  AutoHideToolbar { get; set; } = true;

    // ── History ────────────────────────────────────────────────────────────────
    /// Maximum number of temporary (auto) sessions to keep before pruning oldest.
    public int MaxTempHistory { get; set; } = 20;

    // ── Internal ──────────────────────────────────────────────────────────────
    public void MigrateIfNeeded()
    {
        // v1 → future: place migrations here
    }

    // Helpers -----------------------------------------------------------------
    public uint GetBarColor(MeterType type)
        => BarColors.TryGetValue(type, out var c) ? c : 0xCC888888;
}
