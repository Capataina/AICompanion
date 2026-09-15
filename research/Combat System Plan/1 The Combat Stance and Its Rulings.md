# 1 The Combat Stance and Its Rulings — what is being built, why, and what lost

## The verdict in one paragraph

The companion fights in one stance. When anything is worth fighting it enters Combat, and inside Combat one decision chooses where to be, which weapon, which targets, how to aim and when, over the next few seconds, weighing damage, kills, harm to the player, harm to itself, pushes, time and staying with the player all at once. That decision is powered by what the companion has learned each of its weapons actually does — how many projectiles leave, how each flies, what it does at a wall and at an enemy, what it spawns — learned from every shot it and the player fire. Outside Combat it never fires; everywhere, Combat included, it still bends away from a predicted hit.

## The behaviours this must produce

These are written as behaviours rather than mechanisms, because they are what a replacement must not lose and what the fixtures in file 8 prove. Each came from the owner's own examples.

| # | Behaviour |
|---|---|
| B1 | A slime behind a player walking away is fought from a spot that keeps the companion near the player, not by flying back to the slime and then racing to catch up. |
| B2 | A long straight weapon (a sniper with no drop, a bow) fights from range; a shotgun closes in until its spread lands, because where it stands is chosen for the weapon it means to use. |
| B3 | A shotgun that outdamages a sniper only when every pellet lands makes the companion close on a lone boss. |
| B4 | A piercing weapon against a worm is fired from where one shot crosses the most segments. |
| B5 | A sniper and a shotgun against a boss that spawns goons: snipe the boss while the goons live, clear the goons with the shotgun, then close in and shotgun the boss — a sequence, not a rule. |
| B6 | A bouncing, piercing ball is fired from low at the side of a group so it rolls through all of it. |
| B7 | A grenade dropped from above a group, then, as it explodes, a piercing shot from low at the group's flank through what is left. |
| B8 | A bouncing shot banks off a cave wall to hit an enemy with no line of sight. |
| B9 | An enemy on the player is dealt with first; one far off and idle does not pull the companion off a vein. |
| B10 | No shot is taken while mining, chopping, lighting, collecting or keeping company. |
| B11 | Fighting is a held episode: it does not flicker in and out of Combat between shots. |
| B12 | Any weapon from any mod inside the taxonomy is represented; anything outside it is still fired and valued by what it achieves; nothing is refused merely for being hard to predict. |
| B13 | A mastery upgrade to pierce or projectile count changes what the companion does at once, with nothing relearned. |
| B14 | Every combat decision can be replayed offline and shown to be on the best-found frontier for what the companion knew, or not, with the reason. |

## Three layers, one stance

```
 the player's shots + the companion's shots
                │
 ┌──────────────▼──────────────────────────────────────────────────────────────┐
 │ WEAPON KNOWLEDGE (file 2)   volley shapes, flight laws, wall and enemy       │
 │ Brain/Infrastructure/       responses, children, area, push, debuffs,        │
 │ WeaponKnowledge/            residual value; saved per character              │
 └──────────────┬──────────────────────────────────────────────────────────────┘
                │ SimulateUse(weapon, muzzle, aim, tick, enemy forecast)   (file 3)
 ┌──────────────▼──────────────────────────────────────────────────────────────┐
 │ ATTACK PLANNING (file 4)    stand proposals from what the weapons do;        │ ◄── stand verdicts: reach,
 │ Brain/Activities/Combat/    timed plans across stands and weapons; an        │     travel, exposure, region
 │ Planning/                   objective vector; undominated plans; built-in    │     (Position, file 5)
 │                             weights from the senses; one committed plan      │
 └───────┬───────────────────────────────────────┬─────────────────────────────┘
         │ the plan's value                        │ the plan's due use
 ┌───────▼─────────────────────────┐   ┌──────────▼───────────────────────────────┐
 │ COMBAT ACTIVITY (file 5)        │   │ FIRING (file 5)                          │
 │ Brain/Activities/Combat/        │   │ Brain/Infrastructure/Interactions/Firing/│
 │ FightEnemies.cs — offers the    │   │ fires only on a Combat tick: spawns the  │
 │ plan's value to the chooser;    │   │ volley, spoofs the cursor, tracks what   │
 │ when it wins, flies to its stand│   │ was hit and feeds knowledge              │
 └─────────────────────────────────┘   └──────────────────────────────────────────┘
```

**Learning is beneath the decision, not beside it.** The planner asks knowledge thousands of questions per decision and knowledge asks the planner none. Today's learner sits beside the geometry as a single damage multiplier, so a ball that bounces into five enemies teaches "this weapon beats its forecast" and never where the ball goes, and no stand can be chosen to line the bounce up.

**The planner is the only valuer of a firing stand; the positioner only says whether and how the body gets there.** Its verdicts — reach, travel ticks, exposure, region membership — become objectives in the planner's vector. A positioner still multiplying its own band, sight and standoff terms would commit to a different stand from the plan it was asked to fly.

**Combat lives in the brain, split by kind the way mining already is.** Mining is an activity (`MineOre`), a tool interaction (`Interactions/Mining`) and the senses it reads. Combat becomes an activity with its planner, a firing interaction, and a weapon model beside the senses. The alternative — one combat folder outside the brain, which is where weapons live today — lost because the rule that put them outside ("the brain grants a free hand; the arsenal chooses") existed only while shooting was hands work independent of behaviour, which the owner's ruling ended, and because it would make combat the one family organised differently from every other. File 6 is the layout.

## The rulings

**Only Combat fires** (owner, 15 September 2026). Hunting and guarding merge into one Combat activity, the Combat family's only child, so the brain has six activities in three families. It reverses the law that the hands fire in every job, which was written when combat rarely won and the companion could not shoot while following or working at all; the reversal holds only if Combat wins readily whenever something it can hurt is near, which phase A tunes with the owner in play.

**Combat is a stance, not a detour** (owner, 15 September 2026). Staying with the player is one of Combat's objectives, measured as how far the plan takes the body outside the player's intent region over its horizon, so B1 falls out of the vector rather than out of a rule. An opportunistic kill is a short Combat episode — enter, shoot, leave — and nothing prevents one; what is gone is a shot taken by an activity that is not fighting.

**Objectives are built in, never player settings** (owner, 15 September 2026). There is no "prioritise damage" option. The weights are functions of the senses with their constants in `BehaviourWeights`, tuned by the audit tool's weight sweeps over recorded decisions (file 7), and changed in code.

**Pareto optimality filters; the weights choose.** A plan is on the front when no other plan is at least as good on every objective and better on one. With eight objectives most sensible plans are on it, so the last step is always a weighting. The front still earns its place three ways: dominated plans are dropped before weighting, so no objective can buy a clearly worse plan; the recorder can say which plans were never in contention and which lost only on weights; and the audit can say whether the chosen plan was on the front the exhaustive search found.

**Handed gear is read, never run** (standing law). Volleys are learned from the player's own uses and reproduced by the companion rather than produced by calling item code.

## How a use is predicted, and what lost

```
predict what a use does, for any mod
├─ ✗ run a copy of the projectile's own AI ahead of time
│     a copy is not a sandbox: its AI spawns real children into Main.projectile, Kill breaks
│     tiles, dust and sound are emitted, Main.rand is consumed so the world's randomness shifts,
│     a hit inside Update runs NPC immunity and on-hit hooks for real, and modded AI reads
│     ModPlayer and static state no sandbox reaches; and it is thousands of AI ticks per
│     decision. No mod in the survey does this. Native AI stays in fixtures as ground truth.
├─ ✗ call the item's Shoot hook on a stand-in to see what leaves
│     runs item code the law keeps out, misses vanilla spreads (they live in
│     Player.ItemCheck_Shoot), and says nothing about flight
└─ ● learn each property from observed flights and fly the learned model
      approximates the survey's hardest cases where a copy would be exact, and in exchange has
      no side effects, costs a few operations per simulated update, improves with every shot
      either the player or the companion fires, and can be saved and audited
```

## How a fight is planned, and what lost

```
choose stands, weapons, targets and aims over the next seconds
├─ ✗ greedy one step (today)          cannot value moving later; takes the best shot now and never
│                                     prices the stand it will need next; cannot produce B5 or B7
├─ ✗ Monte Carlo tree search          many random rollouts per decision against a frame budget,
│                                     and a seed per fixture to make any row stable
├─ ✗ a phase script per situation     the owner's own rule against scenario-specific rules
└─ ● beam search over timed segments  stands are few, weapons are few, simulated uses are cached
                                      across segments; deterministic; anytime under a budget
```

## Questions for the owner, each with the default taken if unanswered

1. **Where volleys are learned from.** Default: the player's own uses, reproduced by the companion; a weapon he has never fired fires its single projectile until he does. Alternative: calling the item's `Shoot` hook on a stand-in, which runs item code and still misses vanilla spreads.
2. **Cursor spoofing during companion projectile AI.** Default: yes, as TerraGuardians does.
3. **Held and charged weapons.** Default: refused in the first build, with the revival condition in file 2.
4. **Projectiles anchored to their owner's body** (boomerangs). Default: refused by that observed property, because companion shots are player-owned and would fly back to the player.
5. **Keeping what was learned across sessions.** Default: saved per character, keyed by full item and projectile names, dropped on a knowledge schema change.

## The roadmap cards this plan absorbs

- The companion walks to where the shot is worth most, so it lines a pierce up through three enemies — phase E.
- Position selection prices each candidate firing spot by the best attack every handed weapon could make from there (AIC-395) — not open after all: it landed in `b987d8a` and was closed on the board when this plan was checked against the code; phase A keeps it, phase E replaces it.
- The arsenal learns what each weapon leaves on each enemy type and how the other weapon's hits change against it — layer 1's debuff row, unchanged in shape.
- The gear refuses a weapon whose projectile anchors to its owner's body by that property — question 4.
- A sword that also fires a projectile sweeps its arc as well as shooting — the volley row, once a use can put a swing and a projectile into the world together.
- The item weapon composes damage and speed with the engine rules it still omits (archery potion, quivers) — the volley row's multishot state, and the damage composition beside it.
- A companion with no weapon in its slots neither shoots nor treats combat as its job — phase A, row F5.
- A mode icon beside the notch shows what the companion is doing, with a shield for guarding (AIC-92) — the icon names Combat instead.

## What this deliberately does not do

It does not shoot outside Combat, run item-use code, predict enemy decisions beyond their observed motion, count damage over time, give the companion minions, or offer the player objective settings. It does not promise that every modded weapon is used well: it promises that every weapon inside the taxonomy is represented, that anything outside it is still fired and valued by its outcomes, and that the report says which is which.

## What would refute it

If the flight library's residual on the survey's vanilla examples (Water Bolt, Chlorophyte bullet, Meowmere, boomerang) stays large with every term available, the term library is the wrong representation and a non-parametric trace memory is the next candidate. If C1 cannot fit forty hostiles, four weapons and a forty-pellet volley in the frame budget with caching, plans drop to one segment and a committed plan is held longer instead. If the audit finds chosen plans off the exhaustive front on more than a small share of decisions with the budget not cut, the proposal generators are missing a kind of stand, and the audit names which.
