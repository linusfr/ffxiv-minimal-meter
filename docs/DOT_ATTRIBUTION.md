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

### The missing half

The tick packet names the **target** and the **amount** — never the source. On
its own it cannot be credited to anyone.

But status *applications* are already visible on the ActionEffect hook as
`EffectKind.ApplyStatusEffectTarget` (kind 14), carrying caster, target, and the
status id in the effect value. `DotAttribution` records those into a
`(target, statusId) → source` map, and a tick becomes a lookup.

```
ActionEffect  kind=14  caster=Bard target=Boss value=1200   → remember
ActorControl  0x17     target=Boss status=1200 amount=4321  → credit Bard
```

## Verification — do this before trusting it

The argument layout in `HandlePeriodicTick` is **a guess**. It assumes
`arg1 = effect kind` (with `HotEffectKind = 4` meaning heal), `arg2 = status id`,
`arg3 = amount`. None of that has been confirmed against a live packet.

Getting `arg1` wrong does not merely lose heals — it books them as **damage**.

1. Settings → Window → *Other players' DoTs* → tick **Log raw DoT packets**.
   Leave **Credit DoT and HoT ticks** off.
2. Apply a DoT you can identify — Caustic Bite is ideal, status id **1200**, and
   upstream already ground-truthed that id.
3. Watch the Dalamud log for:
   ```
   MinimalMeter: ActorControl 0x17 target=... arg1=... arg2=... arg3=...
   ```
4. Check which argument holds `1200`, and which holds a number that looks like
   tick damage. If they are not `arg2` and `arg3`, correct the two lines in
   `CombatTracker.HandlePeriodicTick` — they are marked and adjacent.
5. Apply a HoT to an ally and confirm `arg1` differs from the DoT case; that
   value is `HotEffectKind`.
6. Turn logging off, turn crediting on, and sanity-check a Bard or Summoner
   against a known parse — and a healer's HPS against theirs.

## Known limitations

- **Duplicate statuses.** FFXIV tracks one instance of a status per source per
  target, but the tick packet carries no source, so two players landing the
  *same* DoT on one target are indistinguishable. The later applier takes credit
  for both. Rare outside stacked duplicate jobs.
- **Applications missed before you arrived.** A DoT applied before the meter
  started, or before you zoned in, has no recorded application — those ticks are
  dropped rather than mis-credited.
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
