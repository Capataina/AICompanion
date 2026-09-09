# Combat reflexes — recognise an immediate collision

Reflexes run after world observation and before behaviour selection. They identify an incoming hostile or projectile collision and return an `unsafeAtTick` predicate over simulated body states. The shared movement coordinator alone chooses and performs the avoidance controls; reflexes never apply controls or write the NPC.

```
CombatReflexes/
├─ CLAUDE.md
└─ AssessImmediateThreats.cs   predicts hostile and projectile occupied space and supplies the predicate
```

The passive body is stepped first, because existing momentum and gravity can produce a hit even when no new input is given. When it would collide during the lookahead, shared movement evaluates alternatives with its own ability and body rules. A reflex owns the feet for that tick, while the hands still fire if no work tool occupies the arm.

## Trap

The same threat predicate also checks ordinary travel's simulated controls, even when the passive body would have been safe. Checking only the passive trajectory misses a companion walking into an incoming enemy. Its lookahead is bounded by `BehaviourWeights.DodgeLookaheadTicks`; an uncertain distant forecast cannot veto an entire long traversal. Physical macros can be reused, but moving threats are checked again each tick.

- A simulated dodge that moves the NPC directly would create a second movement writer and invalidate the motor’s parity and diagnostics contract.
