# Minimal Meter

[![latest](https://img.shields.io/github/v/release/linusfr/ffxiv-minimal-meter?sort=semver&display_name=tag&label=latest&color=blue)](https://github.com/linusfr/ffxiv-minimal-meter/releases/latest)
[![ci](https://img.shields.io/github/actions/workflow/status/linusfr/ffxiv-minimal-meter/ci.yml?branch=main&label=ci)](https://github.com/linusfr/ffxiv-minimal-meter/actions/workflows/ci.yml)
[![licence](https://img.shields.io/github/license/linusfr/ffxiv-minimal-meter?color=blue)](LICENSE)

> A damage meter that shuts up and shows you the numbers.

Most FFXIV meters want to be a dashboard: panels, gradients, title bars, a strip
along the top reminding you this is a damage meter. Minimal Meter is the same
parser with the furniture taken out. In **Transparent** style there is no window,
no border, no striping — just a bar per player over the game.

```
 1  SAM  Kaito Mizuhara            12.4k  31%
 2  BLM  Tessa Varr                 9.8k  24%
 3  WHM  Aeryn Solace  4.2k  1.1k   3.1k   8%
                       ↑heal ↑hps   ↑dmg  ↑pct
```

No ACT. No IINACT required. No browser overlay. One plugin.

## Credit

**A reskin of [Sansflaire/DamageMeter](https://github.com/Sansflaire/DamageMeter).**
Sansflaire wrote the hard parts — the ActionEffect hook, the bit-level effect
decoding, the DoT simulator, the Skia render pipeline. Go star their repo.

Added here: the Transparent style, DoT/HoT attribution for other players, IINACT
as an optional source, optional DPS/HPS/healing figures, grow-upward sizing, and
a pass over some inherited crashes and races.

## Install

### Dalamud repo (recommended — auto-updates)

`/xlsettings` → **Experimental** → Custom Plugin Repositories → paste, `+`, then
click the **save** icon:

```
https://raw.githubusercontent.com/linusfr/ffxiv-minimal-meter/main/pluginmaster.json
```

Then `/xlplugins` → search **Minimal Meter** → Install.

### Direct download

[![latest](https://img.shields.io/github/v/release/linusfr/ffxiv-minimal-meter?sort=semver&display_name=tag&label=&color=blue)](https://github.com/linusfr/ffxiv-minimal-meter/releases/latest)
 ← current version

| | |
|---|---|
| Latest | [`MinimalMeter.zip`](https://github.com/linusfr/ffxiv-minimal-meter/releases/latest/download/MinimalMeter.zip) |
| All releases | [releases](https://github.com/linusfr/ffxiv-minimal-meter/releases) |

### Rolling back

Prefer the repo above for normal use — it updates itself, and a manually
installed zip never will. Pin a version when you need to go *backwards*: a
release broke something, or you're reproducing a bug on the exact build someone
reported. Tags are unprefixed, so substitute the version directly:

```
https://github.com/linusfr/ffxiv-minimal-meter/releases/download/1.2.0/MinimalMeter.zip
```

Extract into `~/.xlcore/devPlugins/MinimalMeter/` (Linux) or
`%AppData%\XIVLauncher\devPlugins\MinimalMeter\` (Windows) and reload dev plugins.

### Commands

`/dmeter` toggles the meter · `/dmhistory` past sessions · `/dmsettings` settings

## How it works

It hooks the game rather than reading a log. `ActionEffectHandler.Receive` gives
it the server's own damage and healing numbers, decoded from the raw effect
struct — the same data ACT parses, caught after the client decodes it. That is
why there is no npcap, no driver, and no admin prompt.

Direct damage, healing, crit and direct-hit flags are **exact**.

The one gap is damage/healing over time, which never passes through that hook.
Your own DoTs are reconstructed by `DoTSimulator` to within a few percent. Other
players' need one of:

- **Tick attribution** — hooks ActorControl (category `0x17`, where ticks
  actually arrive) and credits them via the status applications the meter already
  sees. Self-contained, experimental, off by default.
  → [`docs/DOT_ATTRIBUTION.md`](docs/DOT_ATTRIBUTION.md)
- **IINACT** — reads the packet stream directly. Proven, but a second plugin.
  Detected and used automatically when installed.
  → [`docs/IINACT_SOURCE.md`](docs/IINACT_SOURCE.md)

## Display

All under settings → **Window**.

- **Grow upward** — bottom edge pinned, rows appear above it. **Max rows** caps
  growth at 8 (a full party) by default; alliance raids scroll past that
- **Extra row figures** — optional DPS, HPS and total healing. Healing renders in
  green and only for combatants who healed
- **Transparent** — bar opacity and text shadow; the shadow is what keeps names
  readable over a bright AoE
- Sorting follows the metric in the toolbar dropdown

## Development

```bash
just build      # debug build
just install    # build and drop into devPlugins
just check      # pre-commit hooks
just fmt        # dotnet format (style + analyzers; the whitespace pass would
                # flatten this codebase's deliberate column alignment)
just hooks      # install git hooks, once
```

Hermit pins prek/gitleaks/jq. The .NET SDK comes from nixpkgs via the justfile,
so no system install is needed. CI builds on `windows-latest` because Dalamud
needs the Windows targeting pack.

## Versioning

`go-semantic-release` reads conventional commits on `main`: `fix:` → patch,
`feat:` → minor, `feat!:` → major. It tags, releases, and CI attaches the zip and
updates `pluginmaster.json`. commitizen gates commit messages as a hook.

## Licence

MIT — see [`LICENSE`](LICENSE). Lineage in
[`docs/PROVENANCE.md`](docs/PROVENANCE.md).
