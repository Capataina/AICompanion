# Survival — recover a safe body state before exposure becomes fatal

This action supplies the reason to escape; the shared movement system owns how to execute the escape. Its score combines observed personal danger with the geometric time to air relative to remaining breath, scaled by the survival urgency in BehaviourWeights. It can begin before the old low-breath threshold when even an optimistic escape would consume the remaining allowance. Crossing water normally remains ordinary travel.

```
Survival/
├─ CLAUDE.md           this contract and the failure properties it must preserve
└─ SurviveAction.cs    survival scoring, dry refuge selection and a stable breathing target
```

Execute asks for a dry, standable refuge. While submerged, TryEscape asks movement for breathing air using Terraria's native head collision test, including partial liquid levels. A bounded flood over body-sized occupiable positions orders the search along passages, allowing retreat beneath an overhang before jumping. Liquid is traversable; solid collision shapes constrain clearance. The air target is separate from the landing target because dry floor behind rock is not a reachable shore. Once the head emerges mid-jump, survival retains control until a dry landing instead of cancelling the lateral motion needed to reach the bank.

The air target stays fixed while a control prefix is searched and executed. Re-selecting the nearest air cell after every step can alternate between different pockets and undo useful travel. Terrain revision, loss of the target's dry/open geometry, action exit or an exhausted control search releases it. The movement owner cancels retained search state when the objective changes; a prefix proved for a previous objective does not acquire a new meaning silently.

SearchControlSequences can move away from a slope, cross a low passage and jump after gaining clearance, using the same body backend as navigation. It retains physically verified prefixes and unfinished search work within a bounded allowance. Partial progress is judged at stable outcomes such as landings, because choosing a jump apex repeatedly can improve momentary height while returning to the same floor forever. A stationary repeated jump is insufficient beneath an awning and is no longer the rescue strategy.

The urgency must exceed a committed guard, including its commitment bonus; guard and survival weights form one ladder. Environmental danger and hostile danger remain separate observations, so a safe player cannot hide the companion's drowning state. Survival does not grant flight, swimming, teleportation or an extra jump.

God's eye records the air target, escape stage, remaining breathing ticks, control source, pending search and retained-control length. Survival control feeds the same body-level progress monitor as ordinary travel. Native fixtures exercise both the survival controller and the full brain from captured pool geometry, plus mirrored awnings. The old pool snapshot lacks partial liquid amounts and is explicitly reconstructed as full cells; newer event captures preserve those amounts. A one-cell dry-head test is insufficient: full-brain checks require sustained native breathing while alive. These checks prove those inputs, not escape from every flooded cave; an actually sealed pocket can still be fatal.
