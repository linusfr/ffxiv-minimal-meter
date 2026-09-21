using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;

namespace MinimalMeter.Windows;

/// <summary>
/// Main meter window.
/// The bar area is rendered via MeterCanvas (SkiaSharp / Panache pipeline).
/// Right-click any row for a full ability-by-ability breakdown popup.
/// </summary>
public sealed class MainWindow : IDisposable
{
    // Mirrors Config.MeterVisible: visible on first install, because a meter you
    // must discover a command to see is one most people conclude is broken — and
    // closing it persists, because ImGui's X should mean what it says.
    private bool _isVisible;
    public bool IsVisible { get => _isVisible; set => _isVisible = value; }

    private readonly Plugin _plugin;
    private Configuration Config  => _plugin.Config;
    private CombatTracker Tracker => _plugin.Tracker;

    private readonly MeterCanvas _meter;
    internal MeterCanvas Meter => _meter;

    // Right-click detail state
    private uint           _detailEntityId;
    private CombatSession? _detailSession;

    // Scroll state
    private float _scrollY = 0f;

    // Layout state shared between DrawCanvasHeader and DrawCanvasBody
    private Vector2 _imgOrigin;
    private Vector2 _bodyOrigin;
    private float   _headerH;
    private float   _texH;
    private float   _bodyTexH;
    private float   _bodyViewH;
    private float   _maxScroll;

    // GrowUpward: the window's height tracks its content, and the bottom edge
    // stays put. ImGui anchors windows at the top-left, so each time the height
    // changes we shift the position up by the same delta — which keeps the
    // bottom pinned while leaving the user free to drag the window normally.
    private float   _lastAutoHeight;
    private Vector2 _lastWindowPos;
    private Vector2 _lastWindowSize;
    private int     _lastGroupCount = 1;
    private DateTime? _leftCombatAt;
    private DateTime? _emptySince;
    private DateTime _demoStart = DateTime.MinValue;


    private int     _canvasW;
    private CombatSession? _frameSession;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainWindow(Plugin plugin)
    {
        _plugin = plugin;
        _meter  = new MeterCanvas(Plugin.TextureProvider);
        _isVisible = _plugin.Config.MeterVisible;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────
    public void Draw()
    {
        if (!_isVisible) return;
        if (HiddenByContext()) return;


        var flags = ImGuiWindowFlags.NoTitleBar
                  | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse;
        if (Config.LockWindow)
            flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;

        // Dynamic min size: always fit at least 4 rows + 1 group header + toolbar + canvas header
        const float WinPadV  = 10f; // 5px top + 5px bottom window padding
        const float MinRows  = 4f;
        const float MinWidth = 280f;
        float dynHeaderH = MeterCanvas.GetEffectiveHeaderH(new MeterCanvas.DisplayOptions
        {
            ShowEncounterTotal = Config.ShowEncounterTotal,
            UiScale            = Config.UiScale,   // or the header measures at 1x
        });
        float dynRowH   = MeterCanvas.EffectiveRowH(new MeterCanvas.DisplayOptions
        {
            UiScale = Config.UiScale,
        });
        float dynGroupH = Config.ShowGroupHeaders ? MeterCanvas.GroupH : 0f;
        float minHeight = dynHeaderH + 1f + dynGroupH + MinRows * dynRowH + WinPadV;

        // Height always follows content; only the anchored edge varies.
        {
            // Height the content wants: last frame's texture plus the chrome that
            // sits outside it. Clamped so an alliance raid cannot fill the screen.
            float contentH = (_meter.TotalHeight > 0 ? _meter.TotalHeight : minHeight)
                             + WinPadV;

            // When growing upward the window IS the content, so the usual
            // four-row floor is wrong: it left slack under a solo or light-party
            // meter, and since rows draw from the top that slack appeared as the
            // meter drifting off the bottom edge. One row is the real floor.
            float growFloor = dynHeaderH + 1f + dynRowH + WinPadV;
            // Ceiling expressed in rows: header + chrome + N rows. A full party
            // is 8, so by default the meter is exactly party-sized and only an
            // alliance raid pushes it into scrolling.
            float capH = dynHeaderH + 1f
                         + MeterCanvas.GroupOverhead(_lastGroupCount, ShowHeadersFor(_lastGroupCount))
                         + Math.Clamp(Config.MaxGrowRows, 1, 72) * dynRowH
                         + WinPadV;
            float autoH    = Math.Clamp(contentH, growFloor, Math.Max(growFloor, capH));

            // Lock height to the content, leave width draggable.
            ImGui.SetNextWindowSizeConstraints(new Vector2(MinWidth, autoH),
                                               new Vector2(1000, autoH));
            // Force it: a constraint alone will not shrink a window that ImGui
            // already sized larger, so dropping 8 rows to 1 would never contract.
            ImGui.SetNextWindowSize(new Vector2(
                _lastWindowSize.X >= MinWidth ? _lastWindowSize.X : 420f, autoH),
                ImGuiCond.Always);

            // Grew or shrank since last frame. Growing UP means moving the top
            // edge by the delta so the bottom stays put; growing DOWN means
            // leaving the position alone, since ImGui already anchors top-left.
            if (Config.Grow == GrowDirection.Up
                && _lastAutoHeight > 0f && Math.Abs(autoH - _lastAutoHeight) > 0.5f)
            {
                // _lastWindowPos is captured after Begin() below; calling
                // GetWindowPos() out here would read whichever window ImGui
                // happened to close last, not ours.
                ImGui.SetNextWindowPos(
                    new Vector2(_lastWindowPos.X,
                                _lastWindowPos.Y - (autoH - _lastAutoHeight)),
                    ImGuiCond.Always);
            }
            _lastAutoHeight = autoH;
        }

        const float WinPad = 5f;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,  new Vector2(WinPad, WinPad));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,    Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        // The Skia canvas paints its own panel, so ImGui contributes nothing
        // visual: no fill (it would darken the canvas a second time) and no
        // border (with a clear fill it was just a thin box floating around the
        // meter). The canvas is the single source of the backdrop.
        ImGui.PushStyleColor(ImGuiCol.Border,   new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0f));
        bool open = ImGui.Begin("###MinimalMeterMain", ref _isVisible, flags);
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);

        if (!open) { ImGui.End(); return; }

        // Remembered for the next frame's bottom-anchored reposition, and updated
        // here so a user drag is picked up like any other position change.
        _lastWindowPos  = ImGui.GetWindowPos();
        _lastWindowSize = ImGui.GetWindowSize();


        DrawCanvasHeader();   // renders SkiaSharp canvas + draws header slice
        DrawCanvasBody();     // body slice + scrollbar + hit-test buttons
        DrawDetailPopup();

        ImGui.End();
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────


    // ── Phase 1: render canvas + draw header slice ────────────────────────────
    // Stores layout state in fields for DrawCanvasBody to consume.
    private void DrawCanvasHeader()
    {
        _frameSession  = GetDisplaySession();
        var metric     = Config.CurrentMeter;
        var dur        = _frameSession?.DurationSeconds ?? 0;
        bool pinned    = _plugin._historyWindow.PinnedSession != null;
        uint localId   = Plugin.ObjectTable.LocalPlayer?.EntityId ?? 0;
        var avail      = ImGui.GetContentRegionAvail();
        _canvasW       = (int)Math.Max(1, avail.X);

        // Build group data
        var groups = new List<MeterCanvas.GroupData>();
        if (_frameSession != null)
        {
            var party    = _frameSession.GetSortedByType(metric, CombatantType.PartyMember);
            var friendly = _frameSession.GetSortedByType(metric, CombatantType.FriendlyPlayer);
            var enemies  = _frameSession.GetSortedByType(metric, CombatantType.Enemy);

            if (party.Count    > 0) groups.Add(new MeterCanvas.GroupData { Label = "Party",    Combatants = party,    Accent = MeterCanvas.GroupAccent(CombatantType.PartyMember) });
            bool showFriendly = Config.ShowFriendlyGroup || Config.DemoCombatants > 0;
            if (friendly.Count > 0 && showFriendly)
            {
                if (Config.GroupByAlliance)
                {
                    // Alliance members are indistinguishable from bystanders in
                    // IPartyList, so the tracker asks GroupManager which alliance
                    // each is in. Anything with no alliance stays "Friendly".
                    foreach (var byAlliance in friendly
                                 .Where(c => c.AllianceIndex >= 0)
                                 .GroupBy(c => c.AllianceIndex)
                                 .OrderBy(g => g.Key))
                    {
                        groups.Add(new MeterCanvas.GroupData
                        {
                            Label      = "Alliance " + (char)('A' + byAlliance.Key),
                            Combatants = byAlliance.ToList(),
                            Accent     = MeterCanvas.GroupAccent(CombatantType.FriendlyPlayer),
                        });
                    }

                    var loose = friendly.Where(c => c.AllianceIndex < 0).ToList();
                    if (loose.Count > 0)
                        groups.Add(new MeterCanvas.GroupData { Label = "Friendly", Combatants = loose, Accent = MeterCanvas.GroupAccent(CombatantType.FriendlyPlayer) });
                }
                else
                {
                    groups.Add(new MeterCanvas.GroupData { Label = "Friendly", Combatants = friendly, Accent = MeterCanvas.GroupAccent(CombatantType.FriendlyPlayer) });
                }
            }
            if (enemies.Count  > 0 && Config.ShowEnemyGroup)    groups.Add(new MeterCanvas.GroupData { Label = "Enemies",  Combatants = enemies,  Accent = MeterCanvas.GroupAccent(CombatantType.Enemy) });

            if (groups.Count == 0 && _frameSession.Combatants.Count > 0)
            {
                var all = _frameSession.GetSortedByType(metric, CombatantType.Unknown);
                if (all.Count == 0)
                    all = _frameSession.Combatants.Values.OrderByDescending(c => c.GetValue(metric, dur)).ToList();
                if (all.Count > 0)
                    groups.Add(new MeterCanvas.GroupData { Label = "Combatants", Combatants = all, Accent = MeterCanvas.GroupAccent(CombatantType.Unknown) });
            }
        }

        const float SbTrackW = 8f;
        var opts = new MeterCanvas.DisplayOptions
        {
            ShowFullName       = Config.ShowFullName,
            ShowPlayerServer   = Config.ShowPlayerServer,
            JobColumn          = Config.JobColumn,
            ShowPercentage     = Config.ShowPercentage,
            BarColorAbgr       = Config.GetBarColor(metric),
            ShowEncounterTotal = Config.ShowEncounterTotal,
            ShowGroupHeaders   = ShowHeadersFor(groups.Count),
            TextShadow         = Config.TextShadow,
            OutlineStrength    = Config.OutlineStrength,
            BarAlpha           = Config.BarAlpha,
            PanelAlpha         = Config.PanelAlpha,
            AbbreviateValues   = Config.AbbreviateValues,
            Style_Metric       = metric,
            ShowDamageValue    = Config.ShowDamageValue,
            ShowDps            = Config.ShowDps,
            ShowHealingValue   = Config.ShowHealingValue,
            ShowHps            = Config.ShowHps,
            UiScale            = Config.UiScale,
            ShowDamageTaken    = Config.ShowDamageTaken,
            ShowAvoidable      = Config.ShowAvoidable,
            ShowOverhealing    = Config.ShowOverhealing,
        };

        _lastGroupCount = Math.Max(1, groups.Count);
        _headerH = MeterCanvas.GetEffectiveHeaderH(opts);

        // Use last frame's TotalHeight to decide whether a scrollbar is needed
        float prevTexH   = _meter.TotalHeight > 0 ? _meter.TotalHeight : 40f;
        float prevBodyTH = Math.Max(0f, prevTexH - _headerH);
        float prevBodyVH = Math.Max(0f, avail.Y - _headerH);
        // Tolerance: with the window height derived from the texture height, the
        // two can disagree by a fraction of a pixel through rounding and padding.
        // Without slack that shows a scrollbar on content that fits exactly.
        const float ScrollSlack = 2f;
        opts.ScrollbarW  = prevBodyTH > prevBodyVH + ScrollSlack ? SbTrackW : 0f;

        _meter.Render(_canvasW, _frameSession, groups, metric, dur, pinned, localId, opts);
        _texH    = _meter.TotalHeight > 0 ? _meter.TotalHeight : 40f;
        _bodyTexH  = Math.Max(0f, _texH - _headerH);
        _bodyViewH = Math.Max(0f, avail.Y - _headerH);
        _maxScroll = _bodyTexH > _bodyViewH + ScrollSlack
            ? _bodyTexH - _bodyViewH
            : 0f;

        // Mouse wheel (processed here so scroll updates before body is drawn)
        if (ImGui.IsWindowHovered() && _maxScroll > 0f)
        {
            float wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
                _scrollY = Math.Clamp(_scrollY - wheel * MeterCanvas.RowH, 0f, _maxScroll);
        }
        _scrollY = Math.Clamp(_scrollY, 0f, _maxScroll);

        if (!_meter.Handle.HasValue) { ImGui.TextDisabled("Rendering…"); return; }

        _imgOrigin = ImGui.GetCursorScreenPos();

        // Draw the header slice — toolbar will be placed right after this by Draw()
        if (_headerH > 0f)
        {
            ImGui.Image(_meter.Handle.Value, new Vector2(_canvasW, _headerH),
                new Vector2(0f, 0f), new Vector2(1f, _headerH / _texH));

            // Each figure in the total row is a sort control: the columns already
            // exist and are already labelled by their own numbers, so clicking a
            // summed value is the most direct way to say "order by this".
            var dl = ImGui.GetWindowDrawList();
            foreach (var (x0, x1, m) in _meter.TotalHits)
            {
                var tl = new Vector2(_imgOrigin.X + x0, _imgOrigin.Y);
                var sz = new Vector2(Math.Max(1f, x1 - x0), _headerH);

                ImGui.SetCursorScreenPos(tl);
                if (ImGui.InvisibleButton($"##sort{(int)m}", sz))
                {
                    Config.CurrentMeter = m;
                    _plugin.SaveConfig();
                }

                if (ImGui.IsItemHovered())
                {
                    // Underline rather than a fill: the point of the meter is to
                    // not draw boxes at people.
                    dl.AddLine(new Vector2(tl.X, tl.Y + sz.Y - 1f),
                               new Vector2(tl.X + sz.X, tl.Y + sz.Y - 1f),
                               m == Config.CurrentMeter ? 0xFFFFFFFFu : 0xB0FFFFFFu, 1.5f);
                    ImGui.SetTooltip("Sort by " + m.DisplayName());
                }
                else if (m == Config.CurrentMeter)
                {
                    dl.AddLine(new Vector2(tl.X, tl.Y + sz.Y - 1f),
                               new Vector2(tl.X + sz.X, tl.Y + sz.Y - 1f),
                               0x66FFFFFFu, 1.5f);
                }
            }
        }

        // Restore cursor to the end of the header so the body follows it directly
        ImGui.SetCursorScreenPos(new Vector2(_imgOrigin.X, _imgOrigin.Y + _headerH));
    }

    // ── Phase 2: draw body slice + scrollbar + hit-test ───────────────────────
    private void DrawCanvasBody()
    {
        if (!_meter.Handle.HasValue) return;

        _bodyOrigin = ImGui.GetCursorScreenPos();

        if (_bodyViewH > 0f)
        {
            float availBodyContent = Math.Max(0f, _bodyTexH - _scrollY);
            float displayBodyH     = Math.Min(_bodyViewH, availBodyContent);
            if (displayBodyH > 0f)
            {
                float uv0y = (_headerH + _scrollY) / _texH;
                float uv1y = Math.Min(1f, (_headerH + _scrollY + displayBodyH) / _texH);
                ImGui.Image(_meter.Handle.Value, new Vector2(_canvasW, displayBodyH),
                    new Vector2(0f, uv0y), new Vector2(1f, uv1y));
            }
        }

        var dl = ImGui.GetWindowDrawList();

        // Scrollbar thumb
        if (_maxScroll > 0f && _bodyViewH > 0f)
        {
            const float sbW = 4f;
            float barH      = Math.Max(20f, _bodyViewH * (_bodyViewH / _bodyTexH));
            float barY      = _bodyOrigin.Y + (_scrollY / _maxScroll) * (_bodyViewH - barH);
            float barX      = _imgOrigin.X + _canvasW - sbW - 2f;
            dl.AddRectFilled(new Vector2(barX, barY), new Vector2(barX + sbW, barY + barH),
                0x55FFFFFF, 2f);
        }

        // ── Left-click → accordion group toggle ──────────────────────────────
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsPopupOpen("##CombatantDetail"))
        {
            var mp = ImGui.GetMousePos();
            if (mp.X >= _imgOrigin.X && mp.X < _imgOrigin.X + _canvasW &&
                mp.Y >= _bodyOrigin.Y && mp.Y < _bodyOrigin.Y + _bodyViewH)
            {
                float canvasY = (mp.Y - _bodyOrigin.Y) + _headerH + _scrollY;
                var grp = _meter.HitTestGroup(canvasY);
                if (grp != null) _meter.ToggleGroup(grp);
            }
        }

        // ── Right-click → detail popup ────────────────────────────────────────
        if (_frameSession != null && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            var mp = ImGui.GetMousePos();
            if (mp.X >= _imgOrigin.X && mp.X < _imgOrigin.X + _canvasW &&
                mp.Y >= _bodyOrigin.Y && mp.Y < _bodyOrigin.Y + _bodyViewH)
            {
                float canvasY = (mp.Y - _bodyOrigin.Y) + _headerH + _scrollY;
                var hit = _meter.HitTest(canvasY);
                if (hit != null)
                {
                    _detailEntityId = hit.EntityId;
                    _detailSession  = _frameSession;
                    ImGui.OpenPopup("##CombatantDetail");
                }
            }
        }
    }

    // ── Graph view body ───────────────────────────────────────────────────────
    // Time-series line chart. X-axis = elapsed seconds since session start, Y-axis
    // = cumulative value of the active MeterType per combatant. One line per top-N
    // combatant, color-coded. For per-second metrics (DPS / HPS / DTPS), the value
    // shown is the rolling cumulative-divided-by-elapsed at each bin so the line
    // converges to the player's average rate.


    // Returns the cumulative value of the selected metric at nBins evenly-spaced
    // time samples from 0..dur. For rate metrics (DPS / HPS), divides by elapsed.
    // Events are appended in time order, so we use a single forward sweep.

    // ── Detail popup (right-click) ────────────────────────────────────────────
    private void DrawDetailPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(560, 560), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##CombatantDetail", ImGuiWindowFlags.NoResize)) return;

        if (_detailSession == null
            || !_detailSession.Combatants.TryGetValue(_detailEntityId, out var c))
        {
            ImGui.Text("No data available.");
            ImGui.EndPopup();
            return;
        }

        var dur = _detailSession.DurationSeconds;

        // ── Header ────────────────────────────────────────────────────────────
        ImGui.BeginGroup();
        var displayName = string.IsNullOrEmpty(c.World) ? c.Name : $"{c.Name}@{c.World}";
        ImGui.TextColored(new Vector4(1f, 1f, 0.6f, 1f), displayName);
        var typeLabel = c.Type switch
        {
            CombatantType.PartyMember    => "Party Member",
            CombatantType.FriendlyPlayer => "Friendly Player",
            CombatantType.Enemy          => "Enemy",
            _                            => "Unknown"
        };
        ImGui.TextDisabled($"{typeLabel}   ●   {_detailSession.FormattedDuration}");
        ImGui.EndGroup();

        ImGui.Separator();

        // ── Stat summary ──────────────────────────────────────────────────────
        ImGui.TextColored(new Vector4(1f,   0.4f, 0.4f, 1f), $"DMG  {FormatNumber(c.TotalDamageDealt)}");
        ImGui.SameLine(0, 20);
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.3f, 1f), $"HEAL {FormatNumber(c.TotalHealingDone)}");
        ImGui.SameLine(0, 20);
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1f), $"OHEAL {FormatNumber(c.TotalOverhealingDone)}");
        ImGui.SameLine(0, 20);
        ImGui.TextColored(new Vector4(0.5f, 0.7f, 1f,   1f), $"TAKEN {FormatNumber(c.TotalDamageTaken)}");
        ImGui.TextDisabled($"DPS {FormatNumber((long)c.GetDps(dur))}/s   HPS {FormatNumber((long)c.GetHps(dur))}/s");

        ImGui.Separator();

        // ── Tabs ──────────────────────────────────────────────────────────────
        if (ImGui.BeginTabBar("##DetailTabs"))
        {
            if (ImGui.BeginTabItem($"Damage Dealt ({c.DamageByAbility.Count})"))
            {
                DrawAbilityTable(c.DamageByAbility, c.TotalDamageDealt, dur, "DPS", showOverheal: false);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Healing Done ({c.HealingByAbility.Count})"))
            {
                DrawAbilityTable(c.HealingByAbility, c.TotalHealingDone, dur, "HPS", showOverheal: true);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem($"Damage Taken ({c.DamageTakenByAbility.Count})"))
            {
                DrawAbilityTable(c.DamageTakenByAbility, c.TotalDamageTaken, dur, "DTPS", showOverheal: false);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.EndPopup();
    }

    private void DrawAbilityTable(
        Dictionary<uint, AbilityStats> abilities, long grandTotal,
        double durationSeconds, string perSecLabel, bool showOverheal)
    {
        if (abilities.Count == 0) { ImGui.TextDisabled("No data recorded."); return; }

        var sorted = abilities.Values.OrderByDescending(a => a.TotalAmount).ToList();

        // SizingStretchProp + Resizable lets the user drag column borders. We seed
        // each numeric column with InitWidthOrWeight so the initial layout matches
        // what shipped pre-resize, but everything except Ability still claims a
        // proportional slice so columns shrink/grow with window width.
        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg
                       | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp
                       | ImGuiTableFlags.Resizable;
        // Sortable was set but TableGetSortSpecs() was never read, so the headers
        // drew arrows and did nothing. Rows are ordered by total descending,
        // which is the order that matters here.

        int colCount = showOverheal ? 8 : 7;
        if (!ImGui.BeginTable("##AbilityTable", colCount, tableFlags,
            new Vector2(0, 380f))) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Ability",     ImGuiTableColumnFlags.WidthStretch, 3.0f);
        ImGui.TableSetupColumn("Hits",        ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("Total",       ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn(perSecLabel,   ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Avg",         ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Min",         ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupColumn("Max",         ImGuiTableColumnFlags.WidthStretch, 0.9f);
        if (showOverheal) ImGui.TableSetupColumn("Overheal", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableHeadersRow();

        foreach (var a in sorted)
        {
            var pct = grandTotal > 0 ? (float)a.TotalAmount / grandTotal * 100f : 0f;
            var perSec = durationSeconds > 0 ? a.TotalAmount / durationSeconds : 0;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            var cellMin  = ImGui.GetCursorScreenPos();
            var cellW    = ImGui.GetContentRegionAvail().X;
            var cellH    = ImGui.GetTextLineHeightWithSpacing();
            var barColor = showOverheal ? 0xAA33CC33u : 0xAA3333CCu;
            ImGui.GetWindowDrawList().AddRectFilled(
                cellMin,
                new Vector2(cellMin.X + cellW * (pct / 100f), cellMin.Y + cellH),
                barColor);
            ImGui.TextUnformatted($"{a.Name}  ({pct:F1}%)");

            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(a.Hits.ToString());
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(FormatNumber(a.TotalAmount));
            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(durationSeconds > 0 ? FormatNumber((long)perSec) : "-");
            ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted(FormatNumber((long)a.Average));
            ImGui.TableSetColumnIndex(5); ImGui.TextUnformatted(a.MinHit > 0 ? FormatNumber(a.MinHit) : "-");
            ImGui.TableSetColumnIndex(6); ImGui.TextUnformatted(FormatNumber(a.MaxHit));

            if (showOverheal)
            {
                ImGui.TableSetColumnIndex(7);
                if (a.TotalOverheal > 0)
                    ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f),
                        $"{FormatNumber(a.TotalOverheal)} ({a.OverhealPercent:F0}%)");
                else
                    ImGui.TextDisabled("-");
            }
        }

        ImGui.EndTable();
    }



    /// <summary>
    /// Context rules that hide the meter without touching its visibility toggle:
    /// out of combat, outside instanced content, or in PvP. Kept separate from
    /// _isVisible so that turning it back on does not fight the user's own
    /// /dm state.
    /// </summary>
    /// Whether group headers should draw for a given number of groups. One group
    /// needs no heading; the option exists because someone may still want the
    /// label and the collapse control.
    private bool ShowHeadersFor(int groupCount)
        => Config.ShowGroupHeaders
           && !(Config.AutoHideSoloGroupHeader && groupCount <= 1);

    /// Bound by a duty. Three flags cover it — the game sets different ones for
    /// different content types, and checking only BoundByDuty misses some.
    private static bool InInstance()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
        || Plugin.Condition[ConditionFlag.BoundByDuty56]
        || Plugin.Condition[ConditionFlag.BoundByDuty95];

    /// Seconds to wait before an auto-hide rule takes effect. Never means "do
    /// not hide on combat state", but an empty meter still has nothing to show,
    /// so it reads as no delay rather than no hiding.
    private double HideGraceSeconds() => Config.HideOutOfCombat switch
    {
        HideDelay.After10s    => 10,
        HideDelay.After30s    => 30,
        _                     => 0,
    };

    private bool HiddenByContext()
    {
        // The demo exists to be looked at while none of these hold, so it wins.
        if (Config.DemoCombatants > 0) return false;

        // Exactly one context applies at a time, so this is a switch, not a set
        // of independent hide rules that could contradict each other.
        if (Plugin.ClientState.IsPvP)
        {
            if (!Config.ShowInPvP) return true;
        }
        else if (InInstance())
        {
            if (!Config.ShowInInstances) return true;
        }
        else
        {
            if (!Config.ShowInOpenWorld) return true;
        }

        // Nothing to show: no session at all, or one nobody has landed a hit in.
        // Shares the out-of-combat delay so an empty meter lingers exactly as
        // long as a finished pull does.
        if (Config.HideWhenEmpty)
        {
            var session = GetDisplaySession();
            bool empty  = session == null || session.Combatants.Count == 0;

            if (!empty)
            {
                _emptySince = null;
            }
            else
            {
                _emptySince ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - _emptySince.Value).TotalSeconds >= HideGraceSeconds())
                    return true;
            }
        }

        if (Config.HideOutOfCombat != HideDelay.Never)
        {
            if (Plugin.Condition[ConditionFlag.InCombat])
            {
                _leftCombatAt = null;
            }
            else
            {
                // Start the clock on the first frame out of combat, so the delay
                // is measured from the end of the pull rather than from whenever
                // this happens to be polled.
                _leftCombatAt ??= DateTime.UtcNow;

                if ((DateTime.UtcNow - _leftCombatAt.Value).TotalSeconds >= HideGraceSeconds())
                    return true;
            }
        }

        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private CombatSession? GetDisplaySession()
    {
        // Demo outranks everything: it exists precisely so you can judge
        // placement without waiting for a pull.
        if (Config.DemoCombatants > 0)
        {
            if (_demoStart == DateTime.MinValue) _demoStart = DateTime.UtcNow;
            return DemoSession.Build(Config.DemoCombatants,
                                     (DateTime.UtcNow - _demoStart).TotalSeconds);
        }
        _demoStart = DateTime.MinValue;

        // A pinned history session always wins — the user asked for that one.
        if (_plugin._historyWindow.PinnedSession is { } pinned) return pinned;

        // IINACT sees the packet stream, so it has DoT ticks for everyone. The
        // built-in hooks never do. Prefer it whenever it is actually connected.
        bool useIinact = Config.Source switch
        {
            DataSource.Iinact => true,
            DataSource.Hooks  => false,
            _                 => _plugin.Iinact.Connected,
        };
        if (useIinact)
            return _plugin.Iinact.Snapshot;

        return Tracker.ActiveSession
            ?? Tracker.Store.TempSessions.LastOrDefault();
    }

    internal static string FormatNumber(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:F2}M",
        >= 1_000     => $"{n / 1_000.0:F1}K",
        _            => n.ToString()
    };

    public void Dispose()
    {
        _meter.Dispose();
    }
}
