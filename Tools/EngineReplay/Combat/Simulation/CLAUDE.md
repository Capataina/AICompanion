# Combat simulation fixtures — what a use is predicted to put into the world, and what actually leaves the muzzle

The planner never traces a shot itself; it asks the simulator what a use would do and prices the answer. These three files hold that answer from both ends. `VerifySimulatedUses` holds the prediction — pellet shares, pierce, overkill, timed children, determinism, modifier spawns — with knowledge *planted* rather than learned, so a failure is the simulation's and never the learner's. `VerifyItemWeapon` holds the other end: a real item in a real slot, fired through the real arsenal, checked against what the game's own numbers say should leave the muzzle. `VerifyAttackOutcomes` sits between them on pure arithmetic, holding the evaluator that turns a set of hits into a value.

```
Simulation/
├─ CLAUDE.md
├─ VerifySimulatedUses.cs   S1–S6: pierce, overkill, shares, timed children, determinism, modifiers
├─ VerifyItemWeapon.cs      one real item per slot, fired or swung, against the item's own numbers
└─ VerifyAttackOutcomes.cs  the evaluator's arithmetic on synthetic attacks and targets
```

## Every S-row names the mutation it kills

That is what makes them worth their runtime, and it is written at each one:

```
S1  an unlimited-pierce use flown along twenty bodies strikes all twenty
    kills: reinstating the cap of eight, the arsenal's old buffer
S2  four pellets on a body with less life than one pellet record four hits, each at full damage
    kills: stopping pellets at the body's death. The sim reserves nothing; crediting one kill
    and the body's life is the plan evaluator's half, not the simulator's
S3  a volley whose four slots each carry a quarter of the damage fires four pellets at a quarter
    kills: firing every slot at full damage, which is the behaviour before volleys
S4  a timed child's hits land at their simulated tick, after the parent's
    kills: landing children at the parent's tick
S5  an Extra projectile modifier adds its spawn with its damage and spacing, prediction changes at once
    kills: applying the modifier after the sim, which leaves the hit count unchanged
S6  the same decision simulated twice returns identical hits, bounces, deaths, totals and paths
    kills: drawing spread angles at random, or reading any clock, die or unordered map
```

Two of those carry a scene choice that is part of the assertion. S4's children are scythes rather than bullets, because a bullet child would spend its single pierce on the near body and never reach the far one — which is the engine's own immunity behaviour and not the row's subject. S6's scene is a four-pellet volley against a reflecting backstop, so the determinism covers expansion, bounces and immunity alike; timers are S4's, and planting one here would recurse through children of children and buy nothing.

## What the item weapon row establishes, and what it cannot

`VerifyItemWeapon` puts one item in a slot, stands a zombie where it can reach, and asks the arsenal to fire. The bow fires the wooden arrow's projectile, player-owned, at the bow's plus the arrow's speed and damage; the pistol fires the musket ball's projectile at the composed speed, flat at a level target; the wand spends its mana and lands its own damage at a full pool, and after the pool is drained and the reload waited out fires again at the empty factor; the sword strikes a zombie one tile away through the game's own strike path with no projectile, and refuses one ten tiles away; a yoyo forced into a slot beside a bow leaves one enumerated weapon; and an empty gear says `no-weapon` with an infinite removal time.

**Enemy AI does not run and spawned projectiles are never advanced, so this establishes what leaves the muzzle and what a swing does on contact — never a fight.** Two refusals are unverified for the same reason and both are named as such: a `channel` item with an overridden `ModItem.Shoot`, and a modded ammo class. No modded item exists headless.

## Traps

**The scene lays its own floor, every time.** The per-case reset rebuilds `Main.tile` but a fixture reads only terrain it wrote, and a flat bullet once met a tile an earlier case had left in the air.

**The player's strike path reads the banner-buff table off `Main.SceneMetrics`**, which the game constructs at start-up and this host does not. The scene constructs an empty one; without it the strike throws on a null.

**A fresh headless player holds zero damage modifiers.** The game rebuilds them every tick and nothing here does, so a row composing item numbers stands them up itself — every class, because `GetTotalDamage` combines all of them and one zero multiplicative zeroes the total. Without it the shares divide by nothing.

**`VerifyAttackOutcomes` is pure arithmetic and has no engine in it at all.** It builds `Attack`, `Hit` and `Target` records directly and reads the evaluator's value, so a red here is the evaluator's and is never a scene problem. What it holds: health-capped crowd damage beats a single big hit; timely removal beats leaving a nearby threat alive; kill count does not erase useful damage; rapid follow-up shots retarget without overkill; a cooldown beyond the horizon buys no damage; and multiple pellets cannot earn the same kill twice.

## Flags

```
(default suite)     all three files run as named cases in VerifyEngineMotion's table
--simulated-uses    the six S-rows
--attack-outcomes   the evaluator's arithmetic
```

The item weapon row has no flag of its own and is reached through the default suite only.

## Current state — 21 September 2026

Every case in this folder passes on the last fully clean whole-suite run, `Tools/Ledger/runs/4abf171-20260921-212441.jsonl`.

## Planned work

`VerifySimulatedUses.cs` is 413 lines, which is about one reading and is **not** a split candidate worth acting on: it is six rows against one subject, each already carrying its own scene, and dividing it would put the shared modifier setup in one file and its only three callers in another. Recorded as a considered no rather than left for the survey to raise again.

## Cross-folder

`../../../../Companion/Brain/Infrastructure/WeaponKnowledge/Simulation/` is the simulator every S-row drives. `../../../../Companion/Brain/Infrastructure/Interactions/Firing/` is the arsenal and the item-backed weapon `VerifyItemWeapon` fires through. `../Knowledge/` establishes the beliefs planted here; `../Planning/` prices plans out of what this folder predicts. `../CLAUDE.md` owns the shared gear pair and the subset-flag save-path trap.
