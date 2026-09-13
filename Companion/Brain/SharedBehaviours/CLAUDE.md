# Shared behaviours — they can take the body without winning a family

These are not the seven activities and they do not compete in the comparison. They steal the feet (or skip the chooser) when the situation demands it.

```
SharedBehaviours/
├─ CLAUDE.md
├─ Safety/      environmental escape, collision avoidance, combat space
└─ Recovery/    distant flight home
```

Shooting is not here. A free hand asks `Companion/Weapons/Arsenal.cs` after movement; that kit is equipment, not a brain behaviour. Movement and aiming are infrastructure those behaviours call.

Safety can run with no ordinary offer at all. Recovery requires a WithPlayer reunion request with a free hand and a distance beyond the recovery threshold. Both submit one request to Grants; neither writes the NPC.

## Recovery is separate from mastery flight

Recovery flight is an explicit coordinator branch that starts when an activity or safety has no more work and the companion sits beyond the recovery distance. The unbuilt mastery tree may later add movement abilities like air jumps, dash, swimming or continuous flight; these would be methods the activities use while their own work is scoring, not a separate system that claims the feet like recovery does. Recovery cannot be started by combat, work or downing — it requires the condition that all ordinary options have closed and the companion is left to follow. A destination in Combat or Gathering does not grant recovery even if those coordinates equal the player's position, because a standing activity offer is not the same as no offer at all.

## Traps

- Safety and recovery are independent paths that compose rather than replace each other. An environmental escape suspends ordinary work without cancelling recovery; when the escape completes, recovery can still claim the feet if no ordinary offer exists.
- Recovery starts only when the coordinator has no ordinary activity to execute and compares it as a future option, not as an immediate action. A freshly un-suppressed activity can still win that comparison.

## Current state — 2026-09-14

Safety runs after observation and can interrupt any activity to escape lava/breath loss or avoid immediate collision. Recovery is a coordinator-level reflex that flies the companion home when it sits beyond the comfort distance and has no ordinary work. Both are separate from the future mastery movement abilities, which would be scoring methods the activities invoke, not systems that own the feet at coordinator level.
