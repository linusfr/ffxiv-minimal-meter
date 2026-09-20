# Minimal Meter

> A damage meter that shuts up and shows you the numbers.

Most FFXIV meters want to be a *dashboard*. Panels, gradients, title bars, a little
chrome rim, a strip along the top telling you that yes, this is indeed a damage
meter. Meanwhile you are trying to see a boss.

Minimal Meter is the same parser with the furniture taken out. In **Transparent**
style there is no window, no border, no panel, no zebra striping — just a bar per
player floating over the game, and text with a shadow so you can actually read it.

```
 1  WHM  Aeryn Solace          12.4k  31%
 2  SAM  Kaito Mizuhara         9.8k  24%
 3  BLM  Tessa Varr             8.1k  20%
```

That is the whole UI. When you want the toolbar it fades in on hover. When you
don't, it isn't there.

## Credit where it's due

**This is a reskin of [Sansflaire/DamageMeter](https://github.com/Sansflaire/DamageMeter).**

Sansflaire wrote the hard parts — the ActionEffect hook and its bit-level effect
decoding, the DoT simulator, the encounter tracking, and the whole Skia render
pipeline that made a transparent mode possible in about forty lines of diff. All
of the actual cleverness in this repo is theirs. Go star their repo; it had one
star when I found it, which is a crime.

What I changed:

- a **Transparent** window style with no chrome at all
- text shadows, so glyphs survive being drawn over a bright floor AoE
- a configurable bar opacity, since the bar is now the only fill left
- a toolbar that hides until you hover the meter
- **other players' DoTs and HoTs**, via an ActorControl hook — the thing the
  original could not see at all (see below)
- **IINACT as an optional source**, used automatically when it is installed
- optional per-row **DPS, HPS and healing** figures, healing only where nonzero
- **grow upward**: the bottom edge stays pinned and rows appear above it, sized
  to a full party by default instead of scrolling
- a pile of fixes to crashes, races and dead settings inherited from upstream
- my own CI, hooks, and release plumbing

## How it works

No ACT. No IINACT. No OverlayPlugin, no WebSocket, no browser quietly running six
CEF subprocesses inside your game. One plugin, three sources.

### 1. It hooks the game, it doesn't read a log

The primary source is a Dalamud function hook on the client's own
`ActionEffectHandler.Receive`. Every time the server tells your client *"this
caster hit these targets for these amounts"*, the plugin sees the decoded struct
before the game gets round to rendering a number over anyone's head.

From there it walks the raw effect array by hand — 8 bytes per effect, 8 effects
per target — and pulls the numbers out of the bit layout:

```
[0] Type    effect kind (Damage, Heal, Miss, Blocked, ...)
[1] Param0  0x20 = critical, 0x40 = direct hit
[5] Param4  0x40 = value is extended by Param3 << 16
[6] Value   ushort, the actual number
```

That layout is cross-checked against FFXIVClientStructs, Ravahn's
`FFXIV_ACT_Plugin`, and perchbirdd's DamageInfoPlugin — all three agree, which is
about as close to ground truth as this gets without a packet capture.

This is **the same server data ACT parses**, caught one layer further in: after
the client has decoded the packet, rather than off the wire. Which is the whole
reason there's no Machina, no npcap, no driver, and no administrator prompt.

### 2. A second hook for what you pressed

`UseAction` is hooked separately, so the plugin knows who cast what and when,
independently of what eventually landed. Casts and results are different events
and get tracked as such.

### 3. FlyText, for the one thing the hook can't see

Here's the wrinkle: **DoT ticks don't fire an ActionEffect at all.** The primary
hook simply never sees them.

FlyText does — but `AutoAttackOrDot` FlyText fires for auto-attacks *and* DoT
ticks, with nothing to tell them apart. So the tracker keeps a short buffer of
recent auto-attack values from the hook: any FlyText that matches one is the
auto-attack's own text and gets skipped, and anything left unmatched must be a
tick.

Getting this subtly wrong is expensive. An earlier version let direct-ability
values into that buffer, where they sat eating real ticks whose value happened to
collide — Bard DoTs came out roughly **97% under-counted**.

**FlyText crediting is now switched off entirely**, for damage and healing both.
The popups are queued visually and can arrive 4–6 seconds after their
ActionEffect, while the dedup window is 2 seconds — so a late popup escapes the
buffer and gets counted twice, and the client also draws healing it did not
originate. It is kept as a diagnostic signal only. What replaced it is below.

### And when even that fails: simulate

For your *own* DoTs the game's chat-log filter suppresses self-cast continuous
damage, so neither FlyText nor ChatMessage nor LogMessage reports the ticks. ACT
solves this by reading network packets. Dalamud doesn't expose that path.

So `DoTSimulator` reconstructs them arithmetically. Your DoT's observed **initial
hit** is a free stat snapshot — main stat, determination, weapon damage, food,
gear and every active buff are already baked into that one number. Divide out the
multipliers that rolled on it, scale by potency ratio, then roll crit and direct
hit per tick at your own rates:

```
per-tick (no crit) = baseline × (dot_potency / init_potency)
baseline           = observed initial hit ÷ multipliers rolled on it
```

Over a DoT's lifetime that converges on the network truth within a few percent.
The known limitation is buff snapshotting: gain Raging Strikes *after* applying
Stormbite and the real server ticks use the new stats, while the simulator keeps
the apply-time snapshot.

### So how accurate is it, honestly

| | |
|---|---|
| Direct damage & healing | **Exact.** These are the server's own numbers. |
| Crit / direct-hit flags | **Exact.** Read from the effect bits. |
| Your own DoT ticks | **Simulated**, converging within a few percent. |
| Other people's DoT and HoT ticks | **Exact**, with tick attribution enabled — otherwise missing. See below. |

The main numbers aren't an approximation — they're the same values ACT reports,
from the same source. But there is one real hole, and it is worth being blunt
about it.

### The hole: other players' DoTs

DoT ticks never reach `ActionEffectHandler.Receive`, so the hook can't see any of
them. FlyText catches the gap for *you*, because FlyText is a client-side UI
event and your client only draws your own numbers. Nobody else's ticks are ever
drawn, so nothing observes them — and the simulator is explicitly local-only, as
it reconstructs ticks from *your* initial hit and *your* crit rates.

The practical effect: a Bard or Black Mage in your party will read low, by
roughly whatever share of their kit is damage over time. Your own number is fine.
Comparisons between other people are not.

There are two ways out, and the plugin has both.

**Self-contained (experimental).** DoT ticks do arrive somewhere the client can
see: an ActorControl packet, category `0x17`. FFXIVClientStructs exposes that
handler with a signature, so it hooks like anything else — no packet capture, no
opcode tables. The packet names the target and amount but not the source, so the
plugin remembers every status application it already sees on the ActionEffect
hook and joins the two. Off by default until the packet's argument layout is
confirmed in game — the procedure is in
[`docs/DOT_ATTRIBUTION.md`](docs/DOT_ATTRIBUTION.md).

**Or just install IINACT**, which has solved this for years: it hooks the zone-channel packet handler and unscrambles
opcodes, so it sees every tick for every actor, and Minimal Meter connects to it
over Dalamud IPC automatically. Details and drawbacks in
[`docs/IINACT_SOURCE.md`](docs/IINACT_SOURCE.md).

## Install

### Recommended: install IINACT too

Minimal Meter runs standalone, and it has its own way of seeing other players'
DoTs and HoTs — but that path is **experimental and off by default** until its
packet layout is confirmed in game. **IINACT is the proven route today**: it has
solved this for years, and the meter picks it up automatically when installed.

Once you have verified the built-in path (see
[`docs/DOT_ATTRIBUTION.md`](docs/DOT_ATTRIBUTION.md)), the two are equivalent and
IINACT becomes optional.

```
https://raw.githubusercontent.com/marzent/IINACT/main/repo.json
```

You do not need Browsingway or any overlay skin — Minimal Meter draws itself.
IINACT is only there as a data source, and the switch is automatic: the setting
defaults to **Auto**, which uses IINACT when connected and the built-in hooks
when it isn't.

The tradeoffs are real and written up in
[`docs/IINACT_SOURCE.md`](docs/IINACT_SOURCE.md) — in short, better numbers but a
second plugin, coarser per-ability detail, no `@World` suffix, and no
party/alliance split.

### The plugin itself

Add the repo to Dalamud (`/xlsettings` → Experimental → Custom Plugin Repositories):

```
https://raw.githubusercontent.com/linusfr/ffxiv-minimal-meter/main/pluginmaster.json
```

Then `/xlplugins` → search **Minimal Meter** → Install.

Switch to the transparent look in the plugin's settings → **Window** → Style →
**Transparent**.

## Display options

All in the plugin's settings, under **Window**.

**Grow upward** (on by default) pins the bottom edge and lets rows appear above
it, so the meter never scrolls at party size. **Max rows** caps that growth — 8,
a full party, by default. An alliance raid is still tracked in full; the window
just stops growing and scrolls past the cap.

**Extra row figures** adds optional DPS, HPS and total-healing columns beside the
primary value. The healing pair renders in green and only for combatants who
actually healed, so a party of DPS keeps the row it had.

**Row height** is 16–40px for the compact styles. **Bar opacity** and **text
shadow** apply to Transparent, where the bar is the only fill left.

Sorting follows whichever metric is selected in the toolbar dropdown — damage,
DPS, healing, HPS, overhealing, damage taken or avoidable damage taken.

## Development

```bash
just            # list everything
just build      # debug build
just install    # build and drop into ~/.xlcore/devPlugins for /xlplugins dev mode
just check      # run every pre-commit hook
just fmt        # dotnet format (style + analyzers; see below)
just hooks      # install the git hooks (do this once)
```

`just fmt` deliberately skips `dotnet format whitespace`: this codebase aligns
fields and comments into columns, and that pass would flatten every file.

Hermit pins the tooling — `source bin/activate-hermit` and prek/gitleaks/jq are
just there. The .NET SDK is not hermit-managed; CI builds on `windows-latest`
because Dalamud plugins need the Windows targeting pack.

## Versioning

Versions are not hand-edited. `go-semantic-release` reads conventional commits on
`main` and decides:

| commit | bump |
|--------|------|
| `fix: ...` | patch |
| `feat: ...` | minor |
| `feat!: ...` / `BREAKING CHANGE:` | major |
| `chore: ...`, `docs: ...` | nothing |

It tags, cuts the GitHub release, and CI attaches the packaged `.zip` and points
`pluginmaster.json` at it. commitizen runs as a `commit-msg` hook, so a commit
that would confuse the release bot never lands in the first place.

## Licence

**MIT.** See [`LICENSE`](LICENSE).

Derived from [Sansflaire/DamageMeter](https://github.com/Sansflaire/DamageMeter),
with thanks — see [Credit where it's due](#credit-where-its-due) above and
[`docs/PROVENANCE.md`](docs/PROVENANCE.md) for the lineage.
