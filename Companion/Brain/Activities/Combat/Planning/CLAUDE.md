# Combat planning — the joint attack evaluator

```
Planning/
├─ CLAUDE.md                 this guide
└─ EvaluateAttackOutcomes.cs bounded joint attack and follow-up valuation, the wound credit, the push charge and a debuff carried through the continuation
```

This folder holds the decision's arithmetic: given candidate attacks the arsenal already solved and the targets they would land on, what the next seconds of firing are worth. It is pure — no geometry of its own, no world reads — so the arsenal in `../../Infrastructure/Interactions/Firing/` runs it unchanged for the hands, for pursuit's delayed valuation and for the positioner's stand pricing, and every one of those prices the same thing. That placement landed in phase 0 of the combat plan, which moved the evaluator out of the dissolved `Companion/Weapons/` with no logic change; phase D grows the planner around it.

## What it evaluates

`Evaluate(first, alternatives, targets, cooldown, horizon)` values a first attack plus a bounded greedy continuation of follow-ups, at most sixteen attacks inside the horizon. Its records are the vocabulary the arsenal forecasts in: a `Target` with its remaining life and danger, a `Hit` with damage, the danger its push adds, the damage it would do to a debuffed body and that debuff's chance and length, an `Attack` as a use at a tick, and an `Outcome` of damage, kills, prevented harm and one value.

Damage reserves projected health: each attack spends the life it would take from a `remaining` map, so overkill and duplicate pellets cannot repeatedly earn kill credit. A kill earns the enemy's full expected harm times its clamped danger plus a finishing value with a timing term; a wound earns a share of that credit in proportion to the fraction of remaining life it removes, times the partial-harm share, so a hit on a dangerous enemy outvalues the same hit on a harmless one before anything dies. A non-lethal hit is charged the danger its push adds; a kill is never charged, because a dead enemy is pushed nowhere. Cross-weapon debuff synergy is carried through the continuation: once an applied attack lands, its body counts as debuffed by that weapon until the debuff runs out, and a later hit from the other weapon is valued between its plain and debuffed damage by the learned chance — which is how a debuff-then-burst pair opens with the debuff without a written combination rule. Time-discounted effective damage, threat removal and the finishing value share one policy in `BehaviourWeights`. This is an estimate over current geometry, not a simulation of future enemy decisions or a globally optimal attack schedule.

## Planned work — the planner grows around the evaluator

`research/Combat System Plan/` phase D adds the objective vector, the dominance filter, the sense-driven weights, the beam search over timed segments and the commitment that keeps a plan by validity rather than by a bonus; the evaluator returns the vector instead of the scalar. File 4 of the plan specifies it. Until then, this guide describes the one file as it is.

## Fixtures

`Tools/EngineReplay/Combat/Simulation/VerifyAttackOutcomes.cs` holds the arithmetic with no world in it: health-capped crowd damage, timely finishing, useful damage over kills, rapid retargeting, the horizon and cooldown bounds, and pellets that must not earn a kill twice.
