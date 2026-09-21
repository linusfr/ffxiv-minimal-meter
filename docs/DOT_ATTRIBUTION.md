# Seeing other players' DoTs and HoTs without IINACT

The built-in hooks cannot see damage or healing over time for anyone but you. This is the
self-contained fix. It is **implemented but unverified** — read the verification
section before turning it on.

## Why the hole exists

`ActionEffectHandler.Receive` never sees DoT ticks. They are not action effects;
they arrive separately. Three fallbacks exist and none of them generalise:

- **FlyText** is a client-side UI event, so your client only draws *your*
  numbers — nobody else's ticks are ever observed. Worse, upstream disabled
  damage crediting from FlyText entirely (`BROKEN.md` §6/§7): popups can arrive
  4–6 seconds after their ActionEffect, and the dedup window cannot be stretched
  that far without mis-crediting auto-attack crits to DoT rows.
- **`DoTSimulator`** reconstructs ticks arithmetically, but only for the local
  player — it works from *your* initial hit and *your* crit rates.
- **IINACT** solves it properly by reading the packet stream, at the cost of a
  second plugin (see `IINACT_SOURCE.md`).

## How this works

DoT ticks arrive as **ActorControl, category `0x17`** — `HoT_DoT` in Machina's
`Server_ActorControlCategory`, which is what ACT itself keys on.

FFXIVClientStructs exposes the client-side handler with a signature, so Dalamud
resolves the address and this needs no packet capture, no Unscrambler, and no
opcode table:

```csharp
// FFXIVClientStructs/FFXIV/Client/Network/PacketDispatcher.cs
public static partial void HandleActorControlPacket(
    uint entityId, uint category,
    uint arg1, ... uint arg8,
    GameObjectId targetId, bool isRecorded);
```

This is the same *class* of hook the plugin already uses twice. Dalamud itself,
DelvUI, WrathCombo, TrackyTrack and HaselDebug all hook this function, so it is
ordinary practice rather than anything exotic.

### The packet attributes itself

The tick packet carries the source actor directly, which was not obvious until it
was read off a live capture:

```
cat=0x17  target=268631352  a1=2105  a2=14  a3=2105  a4=268763580
                 ^ receives              ^ amount      ^ SOURCE actor
```

`a4` cross-references against the combat log as a caster, so no status
bookkeeping is needed to credit a tick. `DotAttribution` — a
`(target, statusId) -> applier` map built before this was known — survives only
as a fallback for when `a4` does not resolve to a tracked combatant.

## Verification — do this before trusting it

The layout in `HandlePeriodicTick` was read off live healing ticks:
`arg1 = amount`, `arg2 = kind`, `arg3 = amount again`, `arg4 = source actor`.

**The damage side is still unconfirmed.** Every captured sample so far has been a
heal (`arg2 = 14`), so `HotEffectKind = 14` is inferred from one side only and
"anything else is damage" is an assumption. Getting `arg2` wrong does not merely
lose heals — it books them as **damage**.

1. Settings → Window → *Other players' DoTs* → tick **Log raw DoT packets**.
   Leave **Credit DoT and HoT ticks** off.
2. Apply a DoT you can identify — Caustic Bite is ideal, status id **1200**, and
   upstream already ground-truthed that id.
3. Watch the Dalamud log for:
   ```
   MinimalMeter: ActorControl 0x17 target=... arg1=... arg2=... arg3=...
   ```
4. Compare `a2` against the healing case (`14`). A different value confirms it is
   the heal/damage discriminator; the same value means the assumption is wrong
   and `HotEffectKind` needs rethinking.
5. Confirm `a4` is *your* entity id, with `target` being the dummy — the reverse
   of the healing case, where `target` was the healed player.
6. Turn logging off and sanity-check a Bard or Summoner against a known parse.

Note the raw log caps at 12 lines **per category**, with `0x17` exempt — an
earlier global cap let `0x93C` (hundreds of packets a minute) crowd out the
category being verified.

## Known limitations

- **Fallback attribution is lossy.** When `a4` resolves, credit is exact. When it
  does not and the status map is consulted instead, two players landing the same
  DoT on one target are indistinguishable, and a DoT applied before the meter
  started has no recorded application at all.
- **HoT ticks share the packet.** Category `0x17` carries both, distinguished by
  `arg1`; heals are credited to `TotalHealingDone` and damage to
  `TotalDamageDealt` through the same attribution map. Verify `HotEffectKind`
  along with everything else — a wrong value silently books heals as damage.
- **Entries expire after 180s** and are swept every 30s, so a very long-lived
  status that is never refreshed eventually stops being credited.

## Why this over porting IINACT

Absorbing IINACT's capture layer means vendoring GPL-3.0 code that itself
depends on Ravahn's closed-source `FFXIV_ACT_Plugin.dll` (fetched at runtime,
because it cannot be redistributed), plus `Unscrambler.XIV` pinned to the game
version — a rebuild every patch.

This path needs one hook and a dictionary.
