# Shared behaviours — outside the decision: recovery takes the body, safety only predicts

These are not the six activities and they are not opportunities a course can order. Recovery steals the feet when the companion is left far behind; safety takes nothing and hands movement the hit it predicts. Both sit outside whatever is deciding — they were outside the family chooser's comparison before 21 September 2026 and they are outside the course now — which is why the tick switch changed neither of them.

```
SharedBehaviours/
├─ CLAUDE.md
├─ Safety/      the hit prediction movement's evade layer bends against
└─ Recovery/    distant flight home
```

Shooting is not here. A free hand asks the arsenal — `CompanionCombat` in `Companion/Brain/Infrastructure/Interactions/Firing/CompanionCombat.cs` — after movement; the choice is a brain behaviour whose joint evaluator lives under the Combat activity, and this folder owns neither. Movement and aiming are infrastructure those behaviours call.

Safety's prediction is built every tick, with or without an ordinary offer, and submits nothing. Recovery submits one request to Grants and never writes the NPC.

**What may start recovery flight is a named predicate**, `RecoverDistantCompanion.ReunionRequested(kind, handsBusy, decisionSettled)`, extracted in `7699f72` on 21 September 2026 from an expression inside `CoordinateBrainTick`. Its three tests each carry a reason: the request must be `WithPlayer`, so no executor class and no player-adjacent work destination can start flight; the hands must be free, because a tool mid-job is work in progress; and **the decision must be settled**, which is the test the course brain made necessary, because every tick inside a running decision asks for companionship anyway and without it the brain flies home whenever it is merely thinking. The predicate exists as a predicate because a rule computed inside a tick from a value assigned two lines above it cannot be checked without standing up a whole scene, and the scene that used to drive it cannot produce three of the four inputs any more.

## Recovery is separate from mastery flight

Recovery flight is an explicit coordinator branch that starts when an activity has no more work and the companion sits beyond the recovery distance. The unbuilt mastery tree may later change what movement costs the body — a dash or a faster pace; those would change what the activities' own requests can reach, not add a separate system that claims the feet like recovery does. Recovery cannot be started by combat, work or downing — it requires the condition that all ordinary options have closed and the companion is left to follow. A destination in Combat or Gathering does not grant recovery even if those coordinates equal the player's position, because a standing activity offer is not the same as no offer at all.

## Traps

- Recovery starts only when the coordinator has no ordinary activity to execute and compares it as a future option, not as an immediate action. A freshly un-suppressed activity can still win that comparison.
- **Recovery ignoring terrain makes it the perfect alibi for a following defect.** In the door scene the body flies from column 38 to column 8.9 — thirty tiles *away* from the player, under keeping company, asking WithPlayer, with the navigator seeking a destination — and only then does the distance trip the recovery radius at tick 192, after which it arrives through the sealed wall exactly as specified. Every part of that except the first move is correct behaviour, so the row read as a door or a recovery defect for a day while the cause sat upstream in following. `AIC-442` carries it. The general shape: a body that travels and then trips the recovery radius and a body that trips it first and travels during recovery are opposite defects, and a single distance minimum reports them identically — which is why the door run now reports its travel by phase.

## Both of these measure to the player's body, and the activities do not

The player's intent region — his feet plus a lead of his own observed pace — is the sense every activity's "near the player" test measures to, because work a few tiles ahead of a travelling player is worth reaching and a radius anchored behind him drops it at the moment he sets off towards it. **Safety, recovery, threat and protection deliberately do not read it.** They read his body, and the two are answering different questions:

```
the question                          measured to        because
├─ is this work near the player?      the intent region  work is worth reaching where he is going
└─ is the player in danger here?      his body           danger is about where he is, not
                                                         where he is heading
```

A reader who has just learned that the intent region answers "how far from the player" will reach for it here and will be wrong. A change that rewrote this folder to measure to the region would put the companion's danger response a lead-length away from the thing endangering him.

## Current state — 2026-09-21

Neither of these changed when the course took the tick on 21 September 2026, and the row that grades what may start recovery is green because its rule was extracted rather than rewritten (`7699f72`, above). The one thing open here is not in this folder: `AIC-442`, where a companion that cannot reach the player travels away from him and recovery then carries it through the wall. Recovery's half of that is specified behaviour and is not the fix.

Safety runs after observation and interrupts nothing: it supplies the collision prediction, and movement bends whatever the body is doing away from the hit without suspending the job. Until 15 September 2026 it also interrupted any activity to leave water or lava; every liquid became air to the orb that day, and the escape went. Recovery is a coordinator-level reflex that flies the companion home when it sits beyond the recovery distance — the same threshold this file names above, not a second one — and has no ordinary work. Both are separate from the future mastery movement abilities, which would be scoring methods the activities invoke, not systems that own the feet at coordinator level.
