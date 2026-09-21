using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

using MinimalMeter.Panache;

using SkiaSharp;

namespace MinimalMeter.Windows;

/// <summary>
/// Full Panache-style SkiaSharp canvas for the damage meter.
/// Renders the encounter header, group headers, and combatant rows as a single
/// GPU texture displayed via ImGui.Image(). The Panache pipeline:
///   SKCanvas → RenderSurface (CPU RGBA) → TextureManager → ImGui.Image()
/// </summary>
public sealed class MeterCanvas : IDisposable
{
    // ── Section heights ───────────────────────────────────────────────────────
    private const float DividerH   =  1f;
    public  const float GroupH     = 30f;  // per-group title row
    public  const float RowH       = 66f;  // per-combatant row (Modern/Classic)
    public  const float MinRowH    = 26f;  // per-combatant row (Minimal — single line)
    private const float GroupGap   =  6f;

    // ── Card layout ───────────────────────────────────────────────────────────
    private const float CardMargin =  3f;  // inset of card from row allocation
    private const float CardR      =  7f;  // card corner radius
    private const float CardH      = RowH - CardMargin * 2; // 60px card height

    // ── Bar layout (within card) ──────────────────────────────────────────────
    private const float BarH       = 10f;
    private const float BarPadX    =  8f;  // bar left/right padding within card
    private const float BarPadB    =  5f;  // bar bottom padding within card
    private const float BarR       =  5f;  // bar corner radius (pill shape)

    // ── Icon badge ────────────────────────────────────────────────────────────
    private const float BadgeW     = 28f;  // job icon — in rounded box
    private const float BadgeH     = 28f;
    private const float BadgeR     =  5f;

    // ── Row layout (left → right within card) ─────────────────────────────────
    private const float StripeW    =  4f;
    private const float LeftPad    =  8f;
    private const float RankW      = 27f;
    private const float RightPad   = 10f;
    private const float PctW       = 36f;
    private const float ValW       = 72f;

    // ── Font sizes ────────────────────────────────────────────────────────────
    private const float FtHeader = 14f;
    private const float FtZone   = 11f;
    private const float FtTimer  = 22f;
    private const float FtMain   = 13f;
    private const float FtSub    = 11f;
    private const float FtRank   = 17f;
    private const float FtName   = 13f;
    private const float FtValue  = 16f;

    private static SKColor TitleBg     = new(0x0E, 0x0E, 0x10, 0xFF);
    private static SKColor TitleGrad1   = new(0x20, 0x20, 0x24, 0xFF);
    private static SKColor TitleGrad2   = new(0x17, 0x17, 0x1A, 0xFF);
    private static SKColor TitleAccent1 = new(0x6A, 0x72, 0x84, 0xFF);
    private static SKColor TitleAccent2 = new(0x4A, 0x50, 0x5E, 0xFF);
    private static SKColor TitleBorder  = new(0x2C, 0x2C, 0x32, 0xFF);
    private static SKColor Divider      = new(0x2C, 0x2C, 0x32, 0xFF);
    private static SKColor GroupSep     = new(0x26, 0x26, 0x2A, 0xFF);
    private static SKColor RowEven = new(0x1C, 0x1C, 0x1E, 0xFF);
    private static SKColor RowOdd  = new(0x16, 0x16, 0x18, 0xFF);
    private static SKColor RowSep  = new(0x26, 0x26, 0x2A, 0xFF);

    // ── Color palette — metallic rose-grey scheme ─────────────────────────────
    // Background layers
    private static SKColor BgDeep    = new(0x10, 0x0C, 0x0D, 0xFF);  // near-black warm charcoal
    private static SKColor BgGroup   = new(0x1C, 0x16, 0x17, 0xFF);  // group header strip
    private static SKColor BgHeader1 = new(0x1A, 0x14, 0x15, 0xFF);  // encounter grad top
    private static SKColor BgHeader2 = new(0x13, 0x0F, 0x10, 0xFF);  // encounter grad bottom

    // Metallic card layers (replaces BgEven/BgOdd with a gradient system)
    private static SKColor MetalBase   = new(0x2A, 0x1E, 0x20, 0xFF);  // dark rose-pewter base
    private static SKColor MetalWarm   = new(0xB4, 0x68, 0x38, 0xA8);  // copper-orange (left radial)
    private static SKColor MetalCool   = new(0x08, 0x05, 0x06, 0xA0);  // dark right edge (lighter → darker)
    private static SKColor MetalSpec   = new(0x0C, 0x08, 0x09, 0xFF);  // top edge dark vignette
    private static SKColor MetalGlow   = new(0xA0, 0x58, 0x50, 0xFF);  // bloom tint (fixed rose)

    // Text — warm neutral instead of blue-tinted
    private static readonly SKColor TextPrim   = new(0xF2, 0xEB, 0xEC, 0xFF);  // warm white
    private static readonly SKColor TextMuted  = new(0x96, 0x88, 0x8A, 0xFF);  // warm muted grey
    private static readonly SKColor TextDim    = new(0x64, 0x59, 0x5B, 0xFF);  // warm dim
    private static readonly SKColor TextLive   = new(0x30, 0xFF, 0x70, 0xFF);  // green (keep)
    private static readonly SKColor TextEnded  = new(0x8A, 0x82, 0x84, 0xFF);  // neutral warm grey
    private static readonly SKColor TextTimer  = new(0xFF, 0xCC, 0x44, 0xFF);  // gold (keep)
    private static readonly SKColor TextZone   = new(0xC4, 0xB8, 0xBA, 0xFF);  // warm silver

    private static readonly SKColor Gold   = new(0xFF, 0xB8, 0x00, 0xFF);
    private static readonly SKColor Silver = new(0xC8, 0xC0, 0xC2, 0xFF);
    private static readonly SKColor Bronze = new(0xC8, 0x78, 0x28, 0xFF);

    private static readonly SKColor AccentParty    = new(0x44, 0x8C, 0xFF, 0xFF);
    private static readonly SKColor AccentFriendly = new(0x44, 0xCC, 0x88, 0xFF);
    private static readonly SKColor AccentEnemy    = new(0xFF, 0x44, 0x44, 0xFF);
    private static readonly SKColor AccentOther    = new(0x88, 0x88, 0xCC, 0xFF);

    private static readonly SKColor TankCol    = new(0x3B, 0x84, 0xFF, 0xFF);
    private static readonly SKColor HealerCol  = new(0x28, 0xCC, 0x58, 0xFF);
    private static readonly SKColor MeleeCol   = new(0xEE, 0x44, 0x44, 0xFF);
    private static readonly SKColor RangedCol  = new(0xEE, 0xA0, 0x20, 0xFF);
    private static readonly SKColor CasterCol  = new(0xCC, 0x44, 0xEE, 0xFF);
    private static readonly SKColor UnknownCol = new(0x44, 0x55, 0x66, 0xFF);

    private static readonly SKColor LocalAccent = new(0x44, 0xEE, 0xFF, 0xFF);
    // Healing figures sit next to damage ones; colour is what separates them at
    // a glance, since both are just numbers.
    private static readonly SKColor HealTint    = new(0x6C, 0xD8, 0x7A, 0xFF);
    // Incoming and wasted quantities read as a separate family from output.
    private static readonly SKColor TakenTint   = new(0xE0, 0x7A, 0x5A, 0xFF);
    private static readonly SKColor AvoidTint   = new(0xE8, 0xB4, 0x4C, 0xFF);
    // Teal, matching its bar colour. It was a dim green, which next to the
    // healing green read as the same quantity twice.
    private static readonly SKColor OverhealTint = new(0x28, 0xB0, 0xA0, 0xFF);

    // ── Display options ───────────────────────────────────────────────────────
    public struct DisplayOptions
    {
        public bool ShowFullName;
        public bool ShowPlayerServer;
        public bool ShowJobIcon;
        public bool ShowPercentage;
        public uint BarColorAbgr;
        public bool  ShowEncounterTotal;
        public bool  ShowGroupHeaders;
        public float ScrollbarW; // reserved right margin when scrollbar is visible (px)
        public bool  TextShadow;      // draw an outline behind glyphs
        public float OutlineStrength; // multiplier on the outline width
        public float BarAlpha;
        public float PanelAlpha;     // 0..1, panel/striping/separator opacity
        public bool  AbbreviateValues;  // 12.4k rather than 12,431
        public MeterType Style_Metric;  // the primary column's metric
        public bool  ShowDamageValue;   // total damage, alongside the value
        public bool  ShowDps;           // damage per second, alongside the value
        public bool  ShowHealingValue;  // total healing, where nonzero
        public bool  ShowHps;           // healing per second, where nonzero
        public float UiScale;      // single size dial: rows, glyphs, icon, padding
        public bool  ShowDamageTaken;
        public bool  ShowAvoidable;
        public bool  ShowOverhealing;
        public JobDisplay JobColumn;
    }

    /// Clamped scale factor; 0 from an un-migrated config reads as 1.
    /// Apply the configured panel opacity to a chrome colour.
    private static SKColor Panel(SKColor c, DisplayOptions o)
    {
        float a = Math.Clamp(o.PanelAlpha < 0f ? 1f : o.PanelAlpha, 0f, 1f);
        return c.WithAlpha((byte)Math.Clamp(c.Alpha * a, 0f, 255f));
    }

    /// Whether a figure column is drawn: because it is switched on, or because
    /// it is the metric being sorted by. There is no separate "primary" column —
    /// sorting must not move a number to a different slot or recolour it, so
    /// every metric has one fixed position and one fixed colour, always.
    private static bool Col(DisplayOptions o, bool enabled, MeterType m)
        => enabled || o.Style_Metric == m;

    private static float Scale(DisplayOptions o)
        => Math.Clamp(o.UiScale <= 0f ? 1f : o.UiScale, 0.5f, 2.5f);

    /// Row height for the current style. Compact styles honour the configured
    /// value; the card layouts need their fixed height to fit the card.
    /// Vertical space the group headers and the gaps between groups consume, so
    /// the window's growth cap can budget for them. Counting one header (the old
    /// behaviour) made the meter scroll as soon as there was more than one group
    /// — which is exactly what splitting by alliance produces.
    public static float GroupOverhead(int groupCount, bool showHeaders)
    {
        if (groupCount <= 0) return 0f;
        return (showHeaders ? groupCount * GroupH : 0f)
             + Math.Max(0, groupCount - 1) * GroupGap;
    }

    public static float EffectiveRowH(DisplayOptions opts) => MinRowH * Scale(opts);


    // ── Dynamic header height ─────────────────────────────────────────────────
    public static float GetEffectiveHeaderH(DisplayOptions opts)
        // The encounter summary is one row styled like the combatant rows below
        // it, not a 64px panel with a zone name, a timer and a title strip.
        => opts.ShowEncounterTotal ? EffectiveRowH(opts) : 0f;

    // ── Group input ───────────────────────────────────────────────────────────
    public struct GroupData
    {
        public string              Label;
        public List<CombatantData> Combatants;
        public SKColor             Accent;
    }

    private float _outline;

    // Figure columns are measured across ALL rows before drawing, then every row
    // advances by the column width rather than by its own text width. Otherwise a
    // row reading "12.4k" and one reading "980" push the next column to different
    // x positions and nothing lines up vertically.
    private float _colPctW, _colDpsW, _colHpsW, _colHealW, _colRankW;
    private float _colTakenW, _colAvoidW, _colOverhealW, _colJobW, _colDmgW;

    /// Screen-space x ranges of the total row's figures, with the metric each
    /// one represents. The window turns these into invisible buttons so clicking
    /// a summed number sorts by it — the columns are already there, so they may
    /// as well be the sort control.
    public readonly List<(float X0, float X1, MeterType Metric)> TotalHits = new();

    /// True only while the total row is being drawn.
    private bool _recordingTotalHits;

    /// Sum of everything on display — the total row's contents.
    private static CombatantData BuildTotal(List<GroupData> groups)
    {
        var total = new CombatantData { Name = "Total", Type = CombatantType.Unknown };
        foreach (var g in groups)
        {
            foreach (var c in g.Combatants)
            {
                total.TotalDamageDealt         += c.TotalDamageDealt;
                total.TotalHealingDone          += c.TotalHealingDone;
                total.TotalOverhealingDone      += c.TotalOverhealingDone;
                total.TotalDamageTaken          += c.TotalDamageTaken;
                total.TotalAvoidableDamageTaken += c.TotalAvoidableDamageTaken;
            }
        }
        return total;
    }

    private void MeasureColumns(List<GroupData> groups, MeterType metric,
                                double dur, DisplayOptions opts)
    {
        _colPctW = _colDpsW = _colHpsW = _colHealW = 0f;
        _colTakenW = _colAvoidW = _colOverhealW = _colDmgW = 0f;

        float sc = Scale(opts);
        using var bold = Font(11f * sc, true);
        using var reg  = Font(11f * sc, false);

        // Rank is left-aligned, so its column must be wide enough for the largest
        // number that will appear — otherwise every row's icon and name start at
        // a different x.
        // The job column is left-aligned like the rank, so it needs the widest
        // abbreviation that will actually appear — an 18px icon slot is narrower
        // than "WHM" at any reasonable scale, which pushed text into the names.
        _colJobW = opts.JobColumn == JobDisplay.Text ? 0f : 18f * sc;
        if (opts.JobColumn == JobDisplay.Text)
        {
            using var jobFont = Font(11f * sc, false);
            foreach (var g in groups)
                foreach (var c in g.Combatants)
                {
                    var abbr = Jobs.TryGetValue(c.ClassJobId, out var ji) ? ji.Abbr : "???";
                    _colJobW = MathF.Max(_colJobW, jobFont.MeasureText(abbr));
                }
        }

        int maxRank = 0;
        foreach (var g in groups) maxRank = Math.Max(maxRank, g.Combatants.Count);
        using (var rankFont = Font(11f * sc, true))
            _colRankW = rankFont.MeasureText(Math.Max(maxRank, 1).ToString());

        // Percent is bounded, so reserve its widest form rather than scanning.
        if (opts.ShowPercentage) _colPctW = reg.MeasureText("100%");

        // The total row shares these columns, and its numbers are sums — by
        // definition the widest entry. Measuring only individual combatants left
        // every column too narrow, so the totals ran into each other.
        var measured = new List<CombatantData>();
        foreach (var g in groups) measured.AddRange(g.Combatants);
        if (opts.ShowEncounterTotal) measured.Add(BuildTotal(groups));

        {
            foreach (var c in measured)
            {
                if (Col(opts, opts.ShowDamageValue, MeterType.DamageDealt))
                {
                    var t = FormatVal(c.TotalDamageDealt, MeterType.DamageDealt, opts);
                    _colDmgW = MathF.Max(_colDmgW, reg.MeasureText(t));
                }
                if (Col(opts, opts.ShowDps, MeterType.DPS))
                {
                    var t = FormatVal((long)c.GetDps(dur), MeterType.DPS, opts);
                    _colDpsW = MathF.Max(_colDpsW, reg.MeasureText(t));
                }
                if (Col(opts, opts.ShowDamageTaken, MeterType.DamageTaken) && c.TotalDamageTaken > 0)
                {
                    var t = FormatVal(c.TotalDamageTaken, MeterType.DamageTaken, opts);
                    _colTakenW = MathF.Max(_colTakenW, reg.MeasureText(t));
                }
                if (Col(opts, opts.ShowAvoidable, MeterType.AvoidableDamageTaken) && c.TotalAvoidableDamageTaken > 0)
                {
                    var t = FormatVal(c.TotalAvoidableDamageTaken, MeterType.AvoidableDamageTaken, opts);
                    _colAvoidW = MathF.Max(_colAvoidW, reg.MeasureText(t));
                }
                if (Col(opts, opts.ShowOverhealing, MeterType.Overhealing) && c.TotalOverhealingDone > 0)
                {
                    var t = FormatVal(c.TotalOverhealingDone, MeterType.Overhealing, opts);
                    _colOverhealW = MathF.Max(_colOverhealW, reg.MeasureText(t));
                }

                if (c.TotalHealingDone <= 0) continue;
                if (Col(opts, opts.ShowHps, MeterType.HPS))
                {
                    var t = FormatVal((long)c.GetHps(dur), MeterType.HPS, opts);
                    _colHpsW = MathF.Max(_colHpsW, reg.MeasureText(t));
                }
                if (Col(opts, opts.ShowHealingValue, MeterType.HealingDone))
                {
                    var t = FormatVal(c.TotalHealingDone, MeterType.HealingDone, opts);
                    _colHealW = MathF.Max(_colHealW, reg.MeasureText(t));
                }
            }
        }
    }


    // ── Rendering infrastructure ──────────────────────────────────────────────
    private RenderSurface?  _surface;

    // StatusApi serves /meter/image from an HttpListener thread while Render()
    // runs on the game thread and frees the surface on every resize. Reading a
    // freed native SkSurface is an access violation, i.e. a game crash — so all
    // surface lifetime changes and all reads from other threads take this lock.
    private readonly object _surfaceGate = new();
    private readonly TextureManager _tex;
    private readonly SKPaint _p = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    // ── Job icon cache ────────────────────────────────────────────────────────
    private readonly Dictionary<byte, SKImage?> _jobIconCache = new();

    // ── Hit-test tables ───────────────────────────────────────────────────────
    private readonly List<(float Y, float H, CombatantData? Data)>    _hitRows    = new();
    private readonly List<(float Y, float H, string Label)>            _groupHits  = new();

    // ── Animation state ───────────────────────────────────────────────────────
    private DateTime _lastRenderTick = DateTime.UtcNow;
    private float    _animTime       = 0f;


    // Slide-in — triggered when a new combatant first appears
    private readonly HashSet<uint>           _seenEntities  = new();
    private readonly Dictionary<uint, float> _slideProgress = new(); // 0→1
    private MeterType                        _prevMetric    = (MeterType)(-1);

    // Accordion — per-group collapse/expand animation
    private readonly Dictionary<string, bool>  _groupCollapsed = new(); // true = collapsed
    private readonly Dictionary<string, float> _groupExpandT   = new(); // 0=collapsed,1=expanded

    public float        TotalHeight { get; private set; }
    public ImTextureID? Handle      => _tex.Handle;

    public MeterCanvas(ITextureProvider tp) => _tex = new TextureManager(tp);

    // ── Main render entry ─────────────────────────────────────────────────────
    public void Render(int width, CombatSession? session, List<GroupData> groups, MeterType metric, double dur,
                       bool isPinned = false, uint localEntityId = 0, DisplayOptions opts = default)
    {
        // ── Frame time ────────────────────────────────────────────────────────
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastRenderTick).TotalSeconds;
        dt = Math.Min(dt, 0.1f); // cap at 100ms to avoid jumps after pause
        _lastRenderTick = now;
        _animTime += dt;

        // ── On metric change: pre-mark all current combatants as seen ─────────
        // This prevents slide-in animation from triggering when switching metrics.
        if (metric != _prevMetric)
        {
            _prevMetric = metric;
            foreach (var g in groups)
                foreach (var c in g.Combatants)
                    _seenEntities.Add(c.EntityId);
            _slideProgress.Clear();
        }

        // ── Detect new entities (slide-in) ────────────────────────────────────
        foreach (var g in groups)
            foreach (var c in g.Combatants)
                if (_seenEntities.Add(c.EntityId))
                    _slideProgress[c.EntityId] = 0f;

        // ── Advance slide progress ────────────────────────────────────────────
        const float SlideDuration = 0.35f;
        foreach (var id in _slideProgress.Keys.ToList())
        {
            _slideProgress[id] = Math.Min(1f, _slideProgress[id] + dt / SlideDuration);
            if (_slideProgress[id] >= 1f) _slideProgress.Remove(id);
        }


        // ── Initialize & animate accordion groups ─────────────────────────────
        foreach (var g in groups)
        {
            if (!_groupExpandT.ContainsKey(g.Label))
                _groupExpandT[g.Label] = 1f; // default expanded
        }
        const float AccordionSpeed = 8f;
        foreach (var label in _groupExpandT.Keys.ToList())
        {
            bool collapsed = _groupCollapsed.TryGetValue(label, out bool c) && c;
            float target  = collapsed ? 0f : 1f;
            float current = _groupExpandT[label];
            float delta   = target - current;
            if (MathF.Abs(delta) < 0.001f) { _groupExpandT[label] = target; continue; }
            _groupExpandT[label] = current + delta * Math.Min(1f, AccordionSpeed * dt);
        }

        _hitRows.Clear();
        _groupHits.Clear();

        float effectiveHeaderH = GetEffectiveHeaderH(opts);
        float rowH   = EffectiveRowH(opts);
        float totalH = ComputeHeight(groups, session, rowH, opts.ShowGroupHeaders, effectiveHeaderH);
        TotalHeight = totalH;

        int w = Math.Max(1, width);
        int h = Math.Max(1, (int)MathF.Ceiling(totalH));

        if (_surface == null || _surface.Width != w || _surface.Height != h)
        {
            lock (_surfaceGate)
            {
                _surface?.Dispose();
                _surface = new RenderSurface(w, h);
            }
        }

        var canvas = _surface.Canvas;
        _outline = opts.TextShadow ? Math.Clamp(opts.OutlineStrength <= 0f ? 1f : opts.OutlineStrength, 0.25f, 2f) : 0f;
        MeasureColumns(groups, metric, dur, opts);
        // The surface is Premul RGBA and reads back Unpremul, so a transparent
        // clear survives the upload and ImGui blends it over the game.
        canvas.Clear(Panel(BgDeep, opts));

        // Compute group total for encounter header
        double groupTotal = 0;
        foreach (var g in groups)
            foreach (var c in g.Combatants)
                groupTotal += c.GetValue(metric, dur);

        DrawTotalRow(canvas, groups, w, metric, dur, groupTotal, opts, effectiveHeaderH, rowH);
        float y = effectiveHeaderH + DividerH;

        bool firstGroup = true;
        foreach (var group in groups)
        {
            if (group.Combatants.Count == 0) continue;
            if (!firstGroup) y += GroupGap;
            firstGroup = false;

            float expandT = _groupExpandT.TryGetValue(group.Label, out float et) ? et : 1f;
            bool  collapsed = _groupCollapsed.TryGetValue(group.Label, out bool gc) && gc;

            double topVal = group.Combatants.Max(c => c.GetValue(metric, dur));

            if (opts.ShowGroupHeaders)
            {
                DrawGroupHeader(canvas, group, w, y, topVal, metric, dur, opts, collapsed, expandT);
                _groupHits.Add((y, GroupH, group.Label));
                _hitRows.Add((y, GroupH, null));
                y += GroupH;
            }

            if (expandT > 0.001f)
            {
                float rowsH    = group.Combatants.Count * rowH;
                float visibleH = rowsH * expandT;

                // Clip rows to animated accordion height
                int clipSave = canvas.Save();
                canvas.ClipRect(SKRect.Create(0, y, w, visibleH));

                float rowY = y;
                for (int i = 0; i < group.Combatants.Count; i++)
                {
                    var c   = group.Combatants[i];
                    var val = c.GetValue(metric, dur);
                    var pct = topVal > 0 ? val / topVal : 0.0;

                    DrawRowMinimal(canvas, c, i + 1, i, w, rowY, rowH, pct, metric, dur, opts, BarColor(opts));
                    _hitRows.Add((rowY, rowH, c));
                    rowY += rowH;
                }

                canvas.RestoreToCount(clipSave);
                y += visibleH;
            }
        }

        if (firstGroup)
        {
            float msgY = effectiveHeaderH + DividerH + 24f;
            Draw(canvas, "No encounter data yet.", w / 2f, msgY, FtSub, false, TextMuted, Align.Center);
        }

        _tex.Upload(_surface);
    }

    // ── Group public API (for MainWindow click handling) ──────────────────────
    public string? HitTestGroup(float imageY)
    {
        foreach (var (ry, rh, label) in _groupHits)
            if (imageY >= ry && imageY < ry + rh) return label;
        return null;
    }

    public void ToggleGroup(string label)
    {
        bool nowCollapsed = !(_groupCollapsed.TryGetValue(label, out bool c) && c);
        _groupCollapsed[label] = nowCollapsed;
        if (!_groupExpandT.ContainsKey(label))
            _groupExpandT[label] = nowCollapsed ? 0f : 1f;
    }

    // ── PNG export for StatusApi ──────────────────────────────────────────────
    /// Called from StatusApi's HTTP thread, not the game thread.
    public byte[]? GetPngBytes()
    {
        lock (_surfaceGate)
            return _surface?.GetPngBytes();
    }

    // ── Hit test (right-click detail) ─────────────────────────────────────────
    public CombatantData? HitTest(float imageY)
    {
        foreach (var (ry, rh, data) in _hitRows)
            if (imageY >= ry && imageY < ry + rh) return data;
        return null;
    }

    // ── Height computation (respects accordion) ────────────────────────────────
    private float ComputeHeight(List<GroupData> groups, CombatSession? session, float rowH, bool showGroupHeaders, float headerH)
    {
        float h = headerH + DividerH;
        bool first = true;
        foreach (var g in groups)
        {
            if (g.Combatants.Count == 0) continue;
            if (!first) h += GroupGap;
            first = false;
            float expandT = _groupExpandT.TryGetValue(g.Label, out float et) ? et : 1f;
            if (showGroupHeaders) h += GroupH;
            h += g.Combatants.Count * rowH * expandT;
        }
        if (first && session != null) h += 30f;
        return h;
    }

    /// <summary>
    /// The encounter summary, drawn as one row matching the combatant rows: same
    /// columns, same alignment, combined totals. Replaces the old zone/timer/
    /// total panel, which was a different shape from everything under it.
    /// </summary>
    private void DrawTotalRow(SKCanvas canvas, List<GroupData> groups, int w,
                              MeterType metric, double dur, double groupTotal,
                              DisplayOptions opts, float headerH, float rowH)
    {
        if (!opts.ShowEncounterTotal) return;

        float y = 0f;

        var total = BuildTotal(groups);
        TotalHits.Clear();
        _recordingTotalHits = true;


        // rank 0 suppresses the rank glyph and the job icon; pct 1 fills the bar,
        // since the total is by definition the maximum.
        DrawRowMinimal(canvas, total, 0, 0, w, y, rowH,
                       1.0, metric, dur, opts, TextMuted);
        _recordingTotalHits = false;
    }

    // ── Encounter header ──────────────────────────────────────────────────────

    // ── Group header row ──────────────────────────────────────────────────────
    private void DrawGroupHeader(SKCanvas canvas, GroupData group, int w, float y,
                                  double topVal, MeterType metric, double dur, DisplayOptions opts,
                                  bool collapsed, float expandT)
    {
        bool isMinimal = true;

        if (isMinimal)
        {
            _p.Color = Panel(BgGroup, opts);
            canvas.DrawRect(SKRect.Create(0, y, w, GroupH), _p);
            // Bottom separator
            _p.Color = Panel(GroupSep, opts);
            canvas.DrawRect(SKRect.Create(0, y + GroupH - 1f, w, 1f), _p);
        }
        else
        {
            // Group header — dark warm metallic strip with subtle accent bleed
            _p.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w * 0.35f, 0),
                new[] { new SKColor(0x28, 0x1C, 0x1E, 0xFF), BgGroup },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(SKRect.Create(0, y, w, GroupH), _p);
            _p.Shader = null;
        }

        float midY = y + GroupH * 0.5f;

        // Accordion chevron (▶ collapsed / ▼ expanded) — animated opacity
        string chevron = collapsed ? "▶" : "▼";
        Draw(canvas, chevron, LeftPad, midY, 10f, false,
             TextPrim.WithAlpha((byte)(collapsed ? 0xCC : 0xAA)), Align.Left);

        // Group label + count
        using var gFont  = Font(FtMain, true);
        float labelW2    = gFont.MeasureText(group.Label);
        Draw(canvas, group.Label,                     LeftPad + 14f,             midY, FtMain, true,  TextPrim,  Align.Left);
        Draw(canvas, $"  ({group.Combatants.Count})", LeftPad + 14f + labelW2,   midY, FtSub,  false, TextMuted, Align.Left);

        // Group sums, in the same columns as the rows below and the total row
        // above — a header that showed only the group's single highest value made
        // the three levels disagree about what a column means.
        float effW = w - opts.ScrollbarW;
        float sc   = Scale(opts);

        var sum = new CombatantData();
        foreach (var c in group.Combatants)
        {
            sum.TotalDamageDealt         += c.TotalDamageDealt;
            sum.TotalHealingDone          += c.TotalHealingDone;
            sum.TotalOverhealingDone      += c.TotalOverhealingDone;
            sum.TotalDamageTaken          += c.TotalDamageTaken;
            sum.TotalAvoidableDamageTaken += c.TotalAvoidableDamageTaken;
        }

        float rx = effW - RightPad;
        if (opts.ShowPercentage) rx -= _colPctW + 6f;   // keep the column reserved

        if (Col(opts, opts.ShowDamageValue, MeterType.DamageDealt))
            rx = DrawFigure(canvas, FormatVal(sum.TotalDamageDealt, MeterType.DamageDealt, opts),
                            rx, midY, TextPrim, _colDmgW, sc);
        if (Col(opts, opts.ShowDps, MeterType.DPS))
            rx = DrawFigure(canvas, FormatVal((long)sum.GetDps(dur), MeterType.DPS, opts),
                            rx, midY, TextMuted, _colDpsW, sc);
        bool healed = sum.TotalHealingDone > 0;
        if (Col(opts, opts.ShowHps, MeterType.HPS))
            rx = DrawFigure(canvas, healed ? FormatVal((long)sum.GetHps(dur), MeterType.HPS, opts) : "",
                            rx, midY, HealTint, _colHpsW, sc);
        if (Col(opts, opts.ShowHealingValue, MeterType.HealingDone))
            rx = DrawFigure(canvas, healed ? FormatVal(sum.TotalHealingDone, MeterType.HealingDone, opts) : "",
                            rx, midY, HealTint, _colHealW, sc);
        if (Col(opts, opts.ShowOverhealing, MeterType.Overhealing))
            rx = DrawFigure(canvas, sum.TotalOverhealingDone > 0
                                ? FormatVal(sum.TotalOverhealingDone, MeterType.Overhealing, opts) : "",
                            rx, midY, OverhealTint, _colOverhealW, sc);
        if (Col(opts, opts.ShowDamageTaken, MeterType.DamageTaken))
            rx = DrawFigure(canvas, sum.TotalDamageTaken > 0
                                ? FormatVal(sum.TotalDamageTaken, MeterType.DamageTaken, opts) : "",
                            rx, midY, TakenTint, _colTakenW, sc);
        if (Col(opts, opts.ShowAvoidable, MeterType.AvoidableDamageTaken))
            rx = DrawFigure(canvas, sum.TotalAvoidableDamageTaken > 0
                                ? FormatVal(sum.TotalAvoidableDamageTaken, MeterType.AvoidableDamageTaken, opts) : "",
                            rx, midY, AvoidTint, _colAvoidW, sc);
    }

    // ── Combatant row (card style) ────────────────────────────────────────────
    /// Bar fill colour for the current metric.
    private static SKColor BarColor(DisplayOptions opts)
        => new((byte)(opts.BarColorAbgr & 0xFF),
               (byte)((opts.BarColorAbgr >> 8) & 0xFF),
               (byte)((opts.BarColorAbgr >> 16) & 0xFF),
               (byte)((opts.BarColorAbgr >> 24) & 0xFF));

    /// Draw one right-aligned figure inside a fixed-width column and return the
    /// new right edge. Width comes from MeasureColumns, not from this text, so
    /// the column lines up across every row.
    private float DrawFigure(SKCanvas canvas, string text, float rightX, float midY,
                             SKColor col, float colW, float sc,
                             MeterType? sortMetric = null)
    {
        if (colW <= 0f) return rightX;
        Draw(canvas, text, rightX, midY, 11f * sc, false, col, Align.Right);
        if (_recordingTotalHits && sortMetric.HasValue)
            TotalHits.Add((rightX - colW, rightX, sortMetric.Value));
        return rightX - colW - 6f * sc;
    }

    // ── Minimal row (single-line condensed) ──────────────────────────────────
    private void DrawRowMinimal(SKCanvas canvas, CombatantData c, int rank, int rowIdx, int w,
        float y, float rowH, double pct, MeterType metric, double dur,
        DisplayOptions opts, SKColor barColor)
    {
        float sc = Scale(opts);
        float IconSz  = 18f * sc;   // small job icon size
        float RankSzM = 11f * sc;   // rank font size
        float NameSzM = 11f * sc;   // name font size
        float ValSzM  = 11f * sc;   // value font size
        float PadL    =  6f * sc;   // left padding
        float PadR    =  6f * sc;   // right padding

        float midY = y + rowH * 0.5f;
        float effW = w - opts.ScrollbarW; // content width excluding scrollbar track

        // Row background — alternating (full width).
        {
            _p.Color = Panel(rowIdx % 2 == 0 ? RowEven : RowOdd, opts);
            canvas.DrawRect(SKRect.Create(0, y, w, rowH), _p);
        }

        // Bar as background fill (proportional width, within effW). The bar is
        // what carries the comparison, so its opacity is the configured value —
        // previously this read a style branch that no longer exists, which left
        // the setting doing nothing.
        if (pct > 0.001)
        {
            float barFillW = effW * (float)pct;
            byte  alpha    = (byte)Math.Clamp(
                (opts.BarAlpha <= 0f ? 0f : opts.BarAlpha) * 255f, 0f, 255f);
            _p.Color = barColor.WithAlpha(alpha);
            canvas.DrawRect(SKRect.Create(0, y, barFillW, rowH), _p);
        }

        // Bottom separator
        {
            _p.Color = Panel(RowSep, opts);
            canvas.DrawRect(SKRect.Create(0, y + rowH - 1f, w, 1f), _p);
        }

        // Rank number
        SKColor rankCol = rank == 1 ? Gold : rank == 2 ? Silver : rank == 3 ? Bronze : TextMuted;
        float curX = PadL;
        // Right-aligned inside a fixed column, so single and double digits both
        // land the following icon at the same place. rank 0 is the total row:
        // the column is still reserved so its figures line up with the rows.
        if (rank > 0)
            Draw(canvas, rank.ToString(), curX + _colRankW, midY, RankSzM, true,
                 rankCol, Align.Right);
        curX += _colRankW + 6f * sc;

        // Job icon (small, no badge)
        if (opts.JobColumn == JobDisplay.Text && rank > 0)
        {
            // Abbreviation instead of the icon: at 16-22px rows an icon is
            // mostly a smudge, and three letters read cleanly at any scale.
            string abbr = Jobs.TryGetValue(c.ClassJobId, out var jt) ? jt.Abbr : "???";
            Draw(canvas, abbr, curX, midY, NameSzM, false, GetRoleColor(c.ClassJobId), Align.Left);
            curX += _colJobW + 4f * sc;
        }
        else if (opts.JobColumn == JobDisplay.Icon && rank > 0)
        {
            var icon = GetJobIcon(c.ClassJobId);
            float iconY = y + (rowH - IconSz) * 0.5f;
            if (icon != null)
            {
                _p.Color = c.ClassJobId == 0 ? new SKColor(0xFF, 0xCC, 0x55, 0xFF) : SKColors.White;
                canvas.DrawImage(icon, SKRect.Create(curX, iconY, IconSz, IconSz), _p);
            }
            else
            {
                string abbr = Jobs.TryGetValue(c.ClassJobId, out var ji) ? ji.Abbr : "?";
                Draw(canvas, abbr, curX + IconSz * 0.5f, midY, 9f, true, TextMuted, Align.Center);
            }
            curX += _colJobW + 4f * sc;
        }
        else if (opts.JobColumn != JobDisplay.None)
        {
            curX += _colJobW + 4f * sc;   // keep the total row's name aligned
        }

        // Right side: value + pct (measure first so name can truncate)
        float rightX = effW - PadR;
        if (opts.ShowPercentage)
        {
            // Drawn even at ~0% so the column below it never shifts.
            if (pct > 0.001)
                Draw(canvas, $"{pct * 100.0:F0}%", rightX, midY, ValSzM, false,
                     TextMuted, Align.Right);
            rightX -= _colPctW + 6f;
        }
        // Fixed columns, right to left. Each metric owns one slot and one colour
        // whether or not it is the one being sorted by — sorting reorders rows,
        // it does not rearrange or recolour the numbers.
        if (Col(opts, opts.ShowDamageValue, MeterType.DamageDealt))
            rightX = DrawFigure(canvas, FormatVal(c.TotalDamageDealt, MeterType.DamageDealt, opts),
                                rightX, midY, TextPrim, _colDmgW, sc, MeterType.DamageDealt);
        if (Col(opts, opts.ShowDps, MeterType.DPS))
            rightX = DrawFigure(canvas, FormatVal((long)c.GetDps(dur), MeterType.DPS, opts),
                                rightX, midY, TextMuted, _colDpsW, sc, MeterType.DPS);

        // Non-healers leave the healing columns blank rather than closing them
        // up, so every row's name still ends at the same x.
        bool healed = c.TotalHealingDone > 0;
        if (Col(opts, opts.ShowHps, MeterType.HPS))
            rightX = DrawFigure(canvas, healed ? FormatVal((long)c.GetHps(dur), MeterType.HPS, opts) : "",
                                rightX, midY, HealTint, _colHpsW, sc, MeterType.HPS);
        if (Col(opts, opts.ShowHealingValue, MeterType.HealingDone))
            rightX = DrawFigure(canvas, healed ? FormatVal(c.TotalHealingDone, MeterType.HealingDone, opts) : "",
                                rightX, midY, HealTint, _colHealW, sc, MeterType.HealingDone);

        if (Col(opts, opts.ShowOverhealing, MeterType.Overhealing))
            rightX = DrawFigure(canvas,
                                c.TotalOverhealingDone > 0
                                    ? FormatVal(c.TotalOverhealingDone, MeterType.Overhealing, opts) : "",
                                rightX, midY, OverhealTint, _colOverhealW, sc, MeterType.Overhealing);
        if (Col(opts, opts.ShowDamageTaken, MeterType.DamageTaken))
            rightX = DrawFigure(canvas,
                                c.TotalDamageTaken > 0
                                    ? FormatVal(c.TotalDamageTaken, MeterType.DamageTaken, opts) : "",
                                rightX, midY, TakenTint, _colTakenW, sc, MeterType.DamageTaken);
        if (Col(opts, opts.ShowAvoidable, MeterType.AvoidableDamageTaken))
            rightX = DrawFigure(canvas,
                                c.TotalAvoidableDamageTaken > 0
                                    ? FormatVal(c.TotalAvoidableDamageTaken, MeterType.AvoidableDamageTaken, opts) : "",
                                rightX, midY, AvoidTint, _colAvoidW, sc, MeterType.AvoidableDamageTaken);

        // Name (fills remaining space)
        string name = opts.ShowFullName ? c.Name : ToInitials(c.Name);
        if (opts.ShowPlayerServer && !string.IsNullOrEmpty(c.World))
            name += "@" + c.World;
        float nameMaxW = rightX - curX - 2f;
        Draw(canvas, name, curX, midY, NameSzM, rank == 1, TextPrim, Align.Left, nameMaxW);
    }

    // ── Bar Breathing Pulse Glow (Panache DrawPulseGlow technique) ───────────
    private void DrawBarPulseGlow(SKCanvas canvas, SKRect barRect, float r, SKColor barColor, float time)
    {
        // pulse: 0→1 smooth sine — speed halved (π × 0.75), intensity ~0.08
        float pulse = (MathF.Sin(time * MathF.PI * 0.75f) + 1f) * 0.5f;

        // Glow color: lerp from lighter barColor (50% toward white) → near-white
        byte gc_r = (byte)(barColor.Red   + (255 - barColor.Red)   * (0.5f + pulse * 0.5f));
        byte gc_g = (byte)(barColor.Green + (255 - barColor.Green) * (0.5f + pulse * 0.5f));
        byte gc_b = (byte)(barColor.Blue  + (255 - barColor.Blue)  * (0.5f + pulse * 0.5f));

        float blur  = 2f + pulse * 3f;       // 2–5px (tight, subtle)
        byte  alpha = (byte)(12f + pulse * 28f); // 12–40 (very low intensity)

        using var filter = SKImageFilter.CreateBlur(blur, blur);
        using var paint  = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1.0f + pulse * 1.0f,  // 1–2px stroke
            Color       = new SKColor(gc_r, gc_g, gc_b, alpha),
            ImageFilter = filter,
            BlendMode   = SKBlendMode.Screen,
        };
        canvas.DrawRoundRect(barRect, r, r, paint);
    }

    // ── Volumetric Glow / Bloom (same technique as PanacheUI's DrawBloom) ─────
    private void DrawBloom(SKCanvas canvas, SKRect rect, float r, SKColor glowColor, float intensity)
    {
        for (int pass = 1; pass <= 3; pass++)
        {
            float blurR = pass * 5f;
            using var filter     = SKImageFilter.CreateBlur(blurR, blurR);
            using var bloomPaint = new SKPaint
            {
                Color       = glowColor.WithAlpha((byte)(intensity * 60f / pass)),
                ImageFilter = filter,
                BlendMode   = SKBlendMode.Screen,
                IsAntialias = true,
                Style       = SKPaintStyle.Fill,
            };
            canvas.DrawRoundRect(rect, r, r, bloomPaint);
        }
    }

    // ── Easing ────────────────────────────────────────────────────────────────
    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    // ── Name helpers ──────────────────────────────────────────────────────────
    private static string ToInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return name;
        return string.Join(".", parts.Select(p => p.Length > 0 ? p[0].ToString().ToUpperInvariant() : "")) + ".";
    }

    // ── Job icon loading ──────────────────────────────────────────────────────
    private SKImage? GetJobIcon(byte jobId)
    {
        if (_jobIconCache.TryGetValue(jobId, out var cached)) return cached;

        // jobId == 0 → use generic enemy icon (92154) as the "no job" fallback
        uint iconId = jobId == 0 ? 92154u : 62000u + jobId;
        SKImage? result = null;
        try
        {
            string folder = $"{iconId / 1000 * 1000:D6}";
            string path   = $"ui/icon/{folder}/{iconId:D6}_hr1.tex";
            var tex       = Plugin.DataManager.GetFile<Lumina.Data.Files.TexFile>(path);
            if (tex == null)
            {
                path = $"ui/icon/{folder}/{iconId:D6}.tex";
                tex  = Plugin.DataManager.GetFile<Lumina.Data.Files.TexFile>(path);
            }
            if (tex != null)
            {
                // Filter converts any source format (BC1/BC3/BC7/etc.) to B8G8R8A8
                var buf    = tex.TextureBuffer.Filter(0, 0, Lumina.Data.Files.TexFile.TextureFormat.B8G8R8A8);
                int bw     = buf.Width;
                int bh     = buf.Height;
                var raw    = buf.RawData;
                int needed = bw * bh * 4;
                if (raw.Length >= needed)
                {
                    var rgba = new byte[needed];
                    Array.Copy(raw, rgba, needed);
                    for (int i = 0; i < needed; i += 4)
                        (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
                    var info   = new SKImageInfo(bw, bh, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
                    try
                    {
                        // `tmp` wraps pinned memory and must be disposed; it was
                        // leaking a native bitmap per icon loaded.
                        using var tmp = new SKBitmap();
                        tmp.InstallPixels(info, handle.AddrOfPinnedObject(), bw * 4);
                        using var owned = tmp.Copy();
                        result = SKImage.FromBitmap(owned);
                    }
                    finally { handle.Free(); }
                }
            }
        }
        catch { }

        // Only cache successes. Caching null meant an icon that failed once —
        // e.g. the Lumina sheet not being ready during zone-in — never loaded
        // again for the rest of the session.
        if (result != null)
            _jobIconCache[jobId] = result;
        return result;
    }

    // ── Text drawing ──────────────────────────────────────────────────────────
    private enum Align { Left, Center, Right }

    private void DrawShadow(SKCanvas canvas, string text, float x, float cy, float sz, bool bold,
                             float maxW = 0f, Align align = Align.Left)
    {
        using var font = Font(sz, bold);
        font.GetFontMetrics(out var m);
        float baselineY = cy - (m.Ascent + m.Descent) * 0.5f;

        if (maxW > 0f)
        {
            float tw = font.MeasureText(text);
            if (tw > maxW)
            {
                float ew     = font.MeasureText("...");
                float budget = maxW - ew;
                if (budget <= 0f) text = "...";
                else
                {
                    int lo = 0, hi = text.Length;
                    while (lo < hi) { int mid = (lo + hi + 1) / 2; if (font.MeasureText(text[..mid]) <= budget) lo = mid; else hi = mid - 1; }
                    text = text[..lo] + "...";
                }
            }
        }

        SKTextAlign skAlign = align switch {
            Align.Center => SKTextAlign.Center,
            Align.Right  => SKTextAlign.Right,
            _            => SKTextAlign.Left,
        };

        using var shadowPaint = new SKPaint {
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
            Color       = new SKColor(0, 0, 0, 0xA0),
            ImageFilter = SKImageFilter.CreateBlur(1.5f, 1.5f),
        };
        canvas.DrawText(text, x + 1f, baselineY + 1.5f, skAlign, font, shadowPaint);
    }

    private void Draw(SKCanvas canvas, string text, float x, float cy, float sz, bool bold, SKColor col,
                      Align align = Align.Left, float maxW = 0f)
    {
        using var font = Font(sz, bold);
        font.GetFontMetrics(out var m);
        float baselineY = cy - (m.Ascent + m.Descent) * 0.5f;

        if (maxW > 0f)
        {
            float tw = font.MeasureText(text);
            if (tw > maxW)
            {
                float ew     = font.MeasureText("...");
                float budget = maxW - ew;
                if (budget <= 0f) { text = "..."; }
                else
                {
                    int lo = 0, hi = text.Length;
                    while (lo < hi)
                    {
                        int mid = (lo + hi + 1) / 2;
                        if (font.MeasureText(text[..mid]) <= budget) lo = mid; else hi = mid - 1;
                    }
                    text = text[..lo] + "...";
                }
            }
        }

        _p.Shader = null;
        _p.Style  = SKPaintStyle.Fill;
        _p.Color  = col;

        SKTextAlign skAlign = align switch
        {
            Align.Center => SKTextAlign.Center,
            Align.Right  => SKTextAlign.Right,
            _            => SKTextAlign.Left,
        };

        if (_outline > 0f)
        {
            // An outline rather than a drop shadow. A shadow only darkens one
            // side, so text over a bright floor AoE still washes out on the other
            // three; a stroke surrounds every glyph and works on any backdrop —
            // which is what makes low background opacity readable at all.
            //
            // Stroke first, fill over it: the stroke straddles the glyph edge, so
            // drawing the fill second keeps the letterforms their true weight
            // instead of being eaten from the inside.
            var fg = _p.Color;
            var prevStyle = _p.Style;
            var prevWidth = _p.StrokeWidth;

            _p.Style       = SKPaintStyle.Stroke;
            _p.StrokeWidth = MathF.Max(1.5f, sz * 0.16f) * _outline;
            _p.StrokeJoin  = SKStrokeJoin.Round;
            _p.Color       = new SKColor(0, 0, 0, (byte)(fg.Alpha * 0.85f));
            canvas.DrawText(text, x, baselineY, skAlign, font, _p);

            _p.Style       = prevStyle;
            _p.StrokeWidth = prevWidth;
            _p.Color       = fg;
        }

        canvas.DrawText(text, x, baselineY, skAlign, font, _p);
    }

    // Preference order. SKTypeface.Default under Wine is whatever fontconfig
    // happens to hand back — often a serif or a wide grotesque that makes the
    // number columns ragged. These are picked for legibility at 11px and for
    // having even digit widths; the first one present wins.
    private static readonly string[] PreferredFonts =
    {
        "Inter",
        "Segoe UI",
        "Roboto",
        "Noto Sans",
        "Open Sans",
        "Source Sans Pro",
        "DejaVu Sans",
    };

    private static SKTypeface Resolve(SKFontStyleWeight weight)
    {
        foreach (var family in PreferredFonts)
        {
            var face = SKTypeface.FromFamilyName(
                family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            // Skia substitutes rather than returning null, so confirm we actually
            // got the family we asked for before accepting it.
            if (face != null &&
                string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                return face;
            face?.Dispose();
        }

        return SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal,
                                         SKFontStyleSlant.Upright)
               ?? SKTypeface.Default;
    }

    // Resolved once. Previously every Draw() call hit the font manager, which at
    // ~10 calls per row, 8 rows, 60fps is thousands of lookups a second inside
    // the render path — and the typeface handle was left to the finalizer.
    private static readonly SKTypeface TypefaceRegular = Resolve(SKFontStyleWeight.Normal);
    private static readonly SKTypeface TypefaceBold    = Resolve(SKFontStyleWeight.Bold);

    private static SKFont Font(float sz, bool bold) =>
        new(bold ? TypefaceBold : TypefaceRegular, sz);

    // ── Color helpers ─────────────────────────────────────────────────────────
    private static SKColor GetRoleColor(byte jobId)
        => Jobs.TryGetValue(jobId, out var info) ? info.Role : UnknownCol;

    private static SKColor Darken(SKColor c, float f) =>
        new((byte)(c.Red * f), (byte)(c.Green * f), (byte)(c.Blue * f), c.Alpha);

    private static SKColor AbgrToSkColor(uint abgr) =>
        new((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF), (byte)((abgr >> 24) & 0xFF));

    private static SKColor EnsureBright(SKColor c, byte minMax = 150)
    {
        byte max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
        if (max >= minMax) return c;
        if (max == 0) return new SKColor(minMax, minMax, minMax, c.Alpha);
        float scale = (float)minMax / max;
        return new SKColor(
            (byte)Math.Min(255, c.Red   * scale),
            (byte)Math.Min(255, c.Green * scale),
            (byte)Math.Min(255, c.Blue  * scale),
            c.Alpha);
    }

    private static string FormatVal(long v, MeterType m, DisplayOptions opts)
    {
        var n = opts.AbbreviateValues ? MainWindow.FormatNumber(v) : v.ToString("N0");
        return m is MeterType.DPS or MeterType.HPS ? n + "/s" : n;
    }

    // ── Group accent helper ───────────────────────────────────────────────────
    public static SKColor GroupAccent(CombatantType t) => t switch
    {
        CombatantType.PartyMember    => AccentParty,
        CombatantType.FriendlyPlayer => AccentFriendly,
        CombatantType.Enemy          => AccentEnemy,
        _                            => AccentOther,
    };

    // ── Job map ───────────────────────────────────────────────────────────────
    /// Reverse lookup for sources that identify jobs by abbreviation rather
    /// than id — IINACT's CombatData reports "whm", not 24.
    public static byte JobIdFromAbbr(string abbr)
    {
        if (string.IsNullOrWhiteSpace(abbr)) return 0;
        foreach (var kv in Jobs)
            if (string.Equals(kv.Value.Abbr, abbr, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return 0;
    }

    private static readonly Dictionary<byte, (string Abbr, SKColor Role)> Jobs = new()
    {
        [1]  = ("GLA", TankCol),   [2]  = ("PGL", MeleeCol),  [3]  = ("MRD", TankCol),
        [4]  = ("LNC", MeleeCol),  [5]  = ("ARC", RangedCol), [6]  = ("CNJ", HealerCol),
        [7]  = ("THM", CasterCol), [19] = ("PLD", TankCol),   [20] = ("MNK", MeleeCol),
        [21] = ("WAR", TankCol),   [22] = ("DRG", MeleeCol),  [23] = ("BRD", RangedCol),
        [24] = ("WHM", HealerCol), [25] = ("BLM", CasterCol), [26] = ("ACN", CasterCol),
        [27] = ("SMN", CasterCol), [28] = ("SCH", HealerCol), [29] = ("ROG", MeleeCol),
        [30] = ("NIN", MeleeCol),  [31] = ("MCH", RangedCol), [32] = ("DRK", TankCol),
        [33] = ("AST", HealerCol), [34] = ("SAM", MeleeCol),  [35] = ("RDM", CasterCol),
        [36] = ("BLU", CasterCol), [37] = ("GNB", TankCol),   [38] = ("DNC", RangedCol),
        [39] = ("RPR", MeleeCol),  [40] = ("SGE", HealerCol), [41] = ("VPR", MeleeCol),
        [42] = ("PCT", CasterCol),
    };

    // ── Dispose ───────────────────────────────────────────────────────────────
    public void Dispose()
    {
        foreach (var img in _jobIconCache.Values) img?.Dispose();
        _jobIconCache.Clear();
        lock (_surfaceGate)
        {
            _surface?.Dispose();
            _surface = null;   // so a late GetPngBytes() returns null, not a freed handle
        }
        _tex.Dispose();
        _p.Dispose();
    }
}
