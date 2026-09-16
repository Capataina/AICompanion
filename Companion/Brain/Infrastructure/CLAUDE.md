# Infrastructure — how activities and shared behaviours get done

Nothing in this tree is a competing job. Observation, scoring, places, movement, tools, aiming, grants and recording are the machinery the six activities and Safety/Recovery all use.

```
Infrastructure/
├─ CLAUDE.md
├─ Observation/    one account of the world, rebuilt per tick — except reach, refreshed per rescore
├─ Selection/      utility scoring, family nomination, tunables
├─ Position/       a kind of place into a hover point
├─ Movement/       one request surface and one motor
├─ Interactions/   pickaxe, axe, torch, doors, homes, firing
├─ Aiming/         projectile trajectory solver
├─ WeaponKnowledge/ what each weapon does, learned from its own shots
├─ Grants/         one packet for feet and hand; incidental pots
└─ Diagnostics/    overlay, telemetry, scenario capture
```

Combat lives in the brain, split by kind the way mining is: the joint evaluator under `Activities/Combat/Planning/`, the weapon tables in `WeaponKnowledge/`, the weapon, choice and landed-hit ledger in `Interactions/Firing/`. The brain grants a free hand; the arsenal chooses who to shoot and with what.
