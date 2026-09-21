# Where the numbers come from, and why IINACT is recommended

Minimal Meter has two data sources. Either works on its own; this document is
about what each one can and cannot see.

## The two sources

### Built-in hooks (always available)

A Dalamud hook on `ActionEffectHandler.Receive`, plus `UseAction`, plus FlyText.
Inherited from upstream, described in the README.

It is exact for direct damage and healing, because those are the server's own
numbers read straight out of the effect struct.

### IINACT (optional, recommended)

IINACT hooks the client's **zone-channel packet receive** function and
unscrambles opcodes:

```csharp
// IINACT/Network/ZoneDownHookManager.cs
private const string GenericDownSignature = "E8 ?? ?? ?? ?? 4C 8B 4F 10 8B 47 1C 45";
private readonly Hook<DownPrototype> zoneDownHook;
using Unscrambler;
```

That is every inbound packet, in-process, after decryption and before dispatch.
It is the same data ACT reads off the wire, obtained without npcap, without a
driver, and without an administrator prompt — which is also why it works under
Wine where ACT does not.

## The hole

**DoT ticks never pass through `ActionEffectHandler.Receive`.** They arrive as
their own packets and are dispatched elsewhere. The hook cannot see them, for
anyone.

For *your own* DoTs there are two fallbacks:

1. **FlyText.** Your client draws your own damage numbers, so ticks show up as
   `AutoAttackOrDot` flytext. Ambiguous with auto-attacks, resolved by a dedup
   buffer against recent auto-attack values from the hook.
2. **`DoTSimulator`.** When the chat-log filter suppresses self-cast continuous
   damage, ticks are reconstructed from the initial hit's implied stats.

Neither helps for **other players**. FlyText is a client-side UI event and your
client never draws anyone else's numbers, so nothing observes their ticks. The
simulator is explicitly local-only — it works from *your* initial hit and *your*
crit rates.

So on the built-in source:

| | hooks alone | hooks + tick attribution | IINACT |
|---|---|---|---|
| Your damage, direct | exact | exact | exact |
| Your DoT ticks | simulated | exact | exact |
| Other players, direct | exact | exact | exact |
| Other players, DoT/HoT ticks | **absent** | exact | exact |

"Tick attribution" is the ActorControl hook described in
[`DOT_ATTRIBUTION.md`](DOT_ATTRIBUTION.md). It is on by default: the packet
carries its own source actor, and the healing path is verified against live
captures. The damage path shares the category and is believed identical but has
not been observed directly, so IINACT remains the fully proven route.

A Bard, Black Mage or Summoner in your party reads low by roughly their DoT
share of total output. Your own number is right either way, but comparisons
between other people are not trustworthy.

With IINACT connected, all four rows are exact.

## How the two are wired together

`Settings → Window → Data source`:

- **Auto** (default) — use IINACT whenever its WebSocket is connected, otherwise
  the hooks. Nothing to configure; plugging IINACT in later just improves the
  numbers.
- **Built-in hooks only** — never talk to IINACT.
- **IINACT only** — show nothing rather than show numbers with the DoT hole.

`IinactSource` finds IINACT over Dalamud IPC (`IINACT.Version`,
`IINACT.Server.Listening`, `IINACT.Server.Uri`), connects to the OverlayPlugin
WebSocket it advertises, and subscribes to `CombatData` — the same interface
kagerou and Ember speak. There is no port to configure. The switch happens in
`MainWindow.GetDisplaySession()`, which is the only place that picks a session.

## Drawbacks of using IINACT

Being honest about what you give up:

- **It is a second plugin.** The pitch for this meter was one plugin and no
  moving parts. Auto mode keeps that true by default, but the recommended
  configuration is no longer self-contained.
- **Less detail.** `CombatData` is a table of aggregates. Per-ability
  breakdowns, the hit/crit distributions behind the detail popup, and the
  DPS/HPS curves all come from the hooks. Those features degrade on the IINACT
  path — the numbers are better, the drill-down is worse.
- **Names, not entity IDs.** `CombatData` identifies combatants by name, so the
  source hashes names into synthetic keys. Two characters with the same name on
  different worlds collide, and the `@World` suffix is unavailable. (Confirmed
  against a live payload: the local player arrives as the literal key `YOU`.)
- **No party/alliance distinction.** The payload does not separate party from
  alliance from bystander, so group headers collapse to a single list. The hook
  path groups as Party / Friendly / Enemies, where "Friendly" is alliance members
  and any other player in the zone — a distinction, though not the one the
  grouping labels suggest.
- **A polling interface over a push one.** Updates arrive at whatever rate
  IINACT broadcasts rather than per effect, so the meter is a beat less live.
- **Another thing that breaks.** Opcodes change every patch, and packet-level
  tooling needs updating before it works again. The built-in hooks survive
  patches somewhat better, since function signatures move less often than
  opcodes. Auto mode falls back on its own when IINACT is down.
- **Same caveat as any parser.** Square Enix's ToS prohibits third-party tools.
  This is not new information if you are already running Dalamud.

## If you want the best of both

The interesting future shape is a merge rather than a switch: keep the hooks for
per-ability detail and curves, and use IINACT purely to cross-check the tick
attribution for non-local combatants. That needs an attribution pass matching IINACT's
aggregates against hook-observed per-ability data, which is real work and not
done here.
