# Shared behaviours — outside the family comparison: recovery takes the body, safety only predicts

These are not the six activities and they do not compete in the comparison. Recovery steals the feet when the companion is left far behind; safety takes nothing and hands movement the hit it predicts.

```
SharedBehaviours/
├─ CLAUDE.md
├─ Safety/      the hit prediction movement's evade layer bends against
└─ Recovery/    distant flight home
```

Shooting is not here. A free hand asks the arsenal in `Companion/Brain/Infrastructure/Interactions/Firing/Arsenal.cs` after movement; the choice is a brain behaviour whose joint evaluator lives under the Combat activity, and this folder owns neither. Movement and aiming are infrastructure those behaviours call.

Safety's prediction is built every tick, with or without an ordinary offer, and submits nothing. Recovery requires a WithPlayer reunion request with a free hand and a distance beyond the recovery threshold; it submits one request to Grants and never writes the NPC.

## Recovery is separate from mastery flight

Recovery flight is an explicit coordinator branch that starts when an activity has no more work and the companion sits beyond the recovery distance. The unbuilt mastery tree may later change what movement costs the body — a dash or a faster pace; those would change what the activities' own requests can reach, not add a separate system that claims the feet like recovery does. Recovery cannot be started by combat, work or downing — it requires the condition that all ordinary options have closed and the companion is left to follow. A destination in Combat or Gathering does not grant recovery even if those coordinates equal the player's position, because a standing activity offer is not the same as no offer at all.

## Traps

- Recovery starts only when the coordinator has no ordinary activity to execute and compares it as a future option, not as an immediate action. A freshly un-suppressed activity can still win that comparison.

## Both of these measure to the player's body, and the activities do not

The player's intent region — his feet plus a lead of his own observed pace — is the sense every activity's "near the player" test measures to, because work a few tiles ahead of a travelling player is worth reaching and a radius anchored behind him drops it at the moment he sets off towards it. **Safety, recovery, threat and protection deliberately do not read it.** They read his body, and the two are answering different questions:

```
the question                          measured to        because
├─ is this work near the player?      the intent region  work is worth reaching where he is going
└─ is the player in danger here?      his body           danger is about where he is, not
                                                         where he is heading
```

A reader who has just learned that the intent region answers "how far from the player" will reach for it here and will be wrong. A change that rewrote this folder to measure to the region would put the companion's danger response a lead-length away from the thing endangering him.

## Current state — 2026-09-15

Safety runs after observation and interrupts nothing: it supplies the collision prediction, and movement bends whatever the body is doing away from the hit without suspending the job. Until 15 September 2026 it also interrupted any activity to leave water or lava; every liquid became air to the orb that day, and the escape went. Recovery is a coordinator-level reflex that flies the companion home when it sits beyond the recovery distance — the same threshold this file names above, not a second one — and has no ordinary work. Both are separate from the future mastery movement abilities, which would be scoring methods the activities invoke, not systems that own the feet at coordinator level.
