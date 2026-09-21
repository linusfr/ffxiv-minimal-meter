using System;
using System.Collections.Generic;

namespace MinimalMeter;

/// <summary>
/// Synthetic combat data for positioning and styling the overlay without being
/// in content. Values grow from a fixed per-player rate against elapsed time, so
/// bars fill and the meter animates the way a real pull does — rather than
/// jittering randomly, which looks wrong and makes placement harder to judge.
/// </summary>
public static class DemoSession
{
    /// name, world, jobId, damage/sec, healing/sec
    private readonly record struct Member(
        string Name, string World, byte JobId, double Dps, double Hps);

    // Two full parties plus an eight-stack, so any preset size slices cleanly off
    // the front and still lands a sensible role mix.
    private static readonly Member[] Roster =
    {
        new("Kaito Mizuhara",  "Omega",     34, 14200, 0),      // SAM
        new("Tessa Varr",      "Phoenix",   25, 13100, 0),      // BLM
        new("Rowan Aldrich",   "Lich",      23, 12400, 0),      // BRD
        new("Sable Wynne",     "Odin",      30, 11900, 0),      // NIN
        new("Bran Thorne",     "Shiva",     19,  7600, 420),    // PLD
        new("Iska Duvant",     "Zodiark",   21,  7100, 260),    // WAR
        new("Aeryn Solace",    "Ragnarok",  24,  3100, 9800),   // WHM
        new("Nael Ptera",      "Cerberus",  28,  3400, 8600),   // SCH

        new("Corvin Ashe",     "Louisoix",  39, 13600, 0),      // RPR
        new("Mira Solveig",    "Spriggan",  42, 12800, 0),      // PCT
        new("Dain Okonkwo",    "Twintania", 31, 12200, 0),      // MCH
        new("Yrsa Lindqvist",  "Alpha",     22, 11700, 0),      // DRG
        new("Talon Reyes",     "Raiden",    32,  7300, 380),    // DRK
        new("Hana Kirigiri",   "Sagittarius", 37, 6900, 300),   // GNB
        new("Lior Ben-Ami",    "Phantom",   33,  3000, 9200),   // AST
        new("Petra Novak",     "Halicarnassus", 40, 3300, 8900),// SGE

        new("Ozren Kovac",     "Maduin",    41, 13300, 0),      // VPR
        new("Suki Tanabe",     "Marilith",  20, 12600, 0),      // MNK
        new("Ferran Oduya",    "Seraph",    35, 12100, 0),      // RDM
        new("Bo Lindgren",     "Kraken",    27, 11500, 0),      // SMN
        new("Vex Marchetti",   "Cuchulainn", 38, 11000, 0),     // DNC
        new("Runa Eskilsen",   "Golem",     19,  7000, 350),    // PLD
        new("Ilya Vashenko",   "Innocence", 24,  2900, 9400),   // WHM
        new("Mei Chastain",    "Pandaemonium", 28, 3200, 8300), // SCH
    };

    /// <summary>Full party size — the boundary between Party and Friendly.</summary>
    private const int PartySize = 8;

    /// <summary>
    /// Build a session of <paramref name="count"/> combatants, as if the pull
    /// started <paramref name="elapsedSeconds"/> ago.
    /// </summary>
    public static CombatSession Build(int count, double elapsedSeconds)
    {
        count = Math.Clamp(count, 1, Roster.Length);
        double elapsed = Math.Max(1.0, elapsedSeconds);

        var session = new CombatSession
        {
            Id        = "demo",
            ZoneName  = "Demo — placement preview",
            StartTime = DateTime.UtcNow.AddSeconds(-elapsed),
        };

        for (int i = 0; i < count; i++)
        {
            var m = Roster[i];

            // A gentle per-player wobble so bars are not perfectly static and the
            // ordering occasionally swaps, which is what you want to see when
            // judging whether the layout holds still enough to read.
            double wobble = 1.0 + 0.05 * Math.Sin(elapsed * 0.6 + i * 1.3);

            var key = (uint)(0xDE_00_00_00 | i);
            session.Combatants[key] = new CombatantData
            {
                EntityId   = key,
                Name       = m.Name,
                World      = m.World,
                ClassJobId = m.JobId,
                // Beyond a full party the rest read as alliance/bystanders, which
                // is how the live grouping classifies them too.
                Type       = i < PartySize
                    ? CombatantType.PartyMember
                    : CombatantType.FriendlyPlayer,

                TotalDamageDealt     = (long)(m.Dps * elapsed * wobble),
                TotalHealingDone     = (long)(m.Hps * elapsed * wobble),
                TotalOverhealingDone = (long)(m.Hps * elapsed * 0.22),
                TotalDamageTaken     = (long)(elapsed * (m.Hps > 5000 ? 600 : 1500)),
            };
        }

        return session;
    }

    /// <summary>Preset sizes, matching the content shapes worth checking.</summary>
    public static readonly (string Label, int Count)[] Presets =
    {
        ("Solo",            1),
        ("Light party (4)", 4),
        ("Full party (8)",  8),
        ("Alliance (24)",  24),
    };
}
