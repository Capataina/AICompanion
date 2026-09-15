# Weapon knowledge and attack planning: one system in three layers

Status: draft plan, 15 September 2026, written against main at `a533f72` after the modded weapon survey (`../Game and Mod Case Studies/Modded Weapon Behaviours.md`). Not approved, not built. Building waits for the owner's playtest of the current package and the mastery session.

## Verdict

The owner's picture is right that choosing a weapon, a target, an aim and a place to stand is one decision and not four, and that learning how a weapon behaves is what makes that decision work for any mod. It needs two corrections to become buildable.

**Learning is a layer beneath the decision, not a peer of it.** Learning answers "if I fire this item from here at this angle, what happens": which projectiles leave, where each flies, what it does at a wall and at an enemy, what it spawns. Planning answers "which of the things I could do is worth most". The planner asks the knowledge layer thousands of questions per decision; the knowledge layer never asks the planner anything. Putting them on one layer is how today's system ended up learning a single damage multiplier on top of a prediction that never improves: the learned number sits beside the geometry instead of inside it, so a ball that bounces into five enemies teaches "this weapon beats its forecast" and never "the ball goes there", and no stand can be chosen to line the bounce up.

**Pareto optimality filters options; it cannot pick one.** A set of options where none is beaten on every objective at once is the Pareto front, and with six or seven objectives almost every sensible option is on it. The companion still fires one shot and flies to one place per tick, so the last step is always a weighting of the objectives. What the Pareto framing buys is real but narrower: dominated options are dropped before weighting, so no single objective can buy a clearly worse option, and the recorder can say which options were never in contention and which lost only on the weighting. The weights are continuous functions of what the senses report — the player's danger, the companion's life, how much of the encounter's life is a boss — never a branch per scenario.

Positioning splits the other way from where it first looks. **The planner is the only thing that values a firing stand; the positioner only says whether and how the body can get there.** Stand proposals come from the planner, because only the planner knows that a line through three enemies or a spot low beside a group is interesting for this weapon. For each proposal the positioner returns a verdict and no score: reach in the reach sense's three values, travel ticks, predicted exposure along the route, and whether the stand belongs to the region the request is admitted against. Those verdict numbers enter the planner's objective vector as harm taken and time to first damage. A `LineOfFire` or `Guard` request then carries the stand the planner chose, and the positioner's job for it is route or refuse. `FiringStandShare`, `StandoffFromTarget` and the line-of-fire and guard score formulas in `ChooseUsefulPosition` go rather than being taught to read the plan, because a positioner still multiplying its own band, sight and standoff terms over the planner's stands would commit to a different stand from the plan it was asked to fly.

**Fighting is one activity, and it is the only thing that fires.** The owner ruled on 15 September 2026 that hunting and guarding merge into a single Combat activity, the only child of the Combat family, so the brain has six activities in three families: Combat; Gathering with mining and chopping; Nearby assistance with lighting, collecting and keeping company. The weapons fire only while Combat is the current activity. Mining, chopping, lighting, collecting and keeping company never shoot, and an enemy worth shooting is a reason for Combat to win the comparison rather than a shot taken in passing. The evade layer is untouched: bending away from a predicted hit rides on top of every activity, Combat included. The ruling removes the third place positioning lived — a hunting walk to a place from which the hands, separately, might find a shot — so where to stand, which weapon, which target and which aim are one decision in one activity with one owner. It reverses the earlier rule that the hands fire in every job, which was written when combat rarely won and the companion could not shoot while following or working at all, so it holds only if Combat wins readily whenever something it can hurt is near; making it do so is part of this plan, tuned with the owner in play rather than by a fixed ordering. The owner's intended feel is mining and chopping, then combat, then lighting and looting, with combat almost always beating keeping company while enemies are on screen, and danger to the player lifting combat above work; the existing utility terms — remaining work, travel, danger, switching cost — produce that ordering, and fixture rows measure it rather than a priority list enforcing it.

```
                    ┌─────────────────────────────────────────────────────────────────┐
  observations ───► │ 1 Weapon knowledge   what a use puts into the world, and what    │
  (every shot, the  │                      each projectile does: volley, flight law,   │
  player's too)     │                      walls, hits, children, push, damage ratio   │
                    └───────────────┬─────────────────────────────────────────────────┘
                                    │ SimulateUse(item, modifiers, muzzle, aim, predicted enemies, terrain)
                    ┌───────────────▼─────────────────────────────────────────────────┐
                    │ 2 Attack planning    candidate (stand, weapon, target set, aim)   │ ◄── stand verdicts
                    │                      and short sequences of them, each scored as │     (reach, travel,
                    │                      an objective vector; Pareto filter; weights │      exposure, region)
                    │                      from the senses; one committed plan          │ ──► stand proposals
                    └───────┬───────────────────────────────────┬─────────────────────┘      to the positioner
                            │ plan's first step                  │ plan's value and stand
                    ┌───────▼────────┐                  ┌───────▼──────────────────────┐
                    │ 3a Hands       │                  │ 3b Combat, the one activity   │
                    │ fire the step, │                  │ that fires: offers the plan's │
                    │ reproduce the  │                  │ value; when it wins it asks   │
                    │ volley, apply  │                  │ the positioner for the        │
                    │ modifiers      │                  │ plan's stand                  │
                    └───────┬────────┘                  └───────────────────────────────┘
                            │ spawned projectiles, landed hits, events
                            └──────────────────────────► back to layer 1
```

## Layer 1 — Weapon knowledge

### How a use is predicted: three routes, one chosen

```
predict what a use does, for any mod
├─ ✗ run a copy of the projectile's own AI ahead of time
│     lost: a copy is not a sandbox. Its AI spawns real children into Main.projectile, real
│     explosions break tiles in Kill, dust, sound and gore are emitted, Main.rand is consumed so
│     the world's randomness shifts, a hit inside Update runs NPC immunity and on-hit hooks for
│     real, and modded AI reads ModPlayer and static state no sandbox flag reaches. It is also
│     thousands of AI ticks per rescore. No mod in the survey does this: every lead-shot helper
│     found is a closed form for one known projectile. Native AI stays in the fixtures as the
│     ground truth learning is judged against, which is where it is safe.
├─ ✗ call the item's own Shoot hook on a stand-in to see what leaves
│     lost as the prediction route: it runs item code the 14 September ruling keeps out, still
│     misses vanilla spreads (they live in Player.ItemCheck_Shoot, not a hook), and says nothing
│     about flight. Kept as question 1 below for the volley alone.
└─ ● learn each property from observed flights, fly the learned model
      a closed term library per projectile type plus learned volley, wall, hit and child
      responses. Approximates the survey's hardest cases (the smart-bounce retarget, three
      generations of children) where a copy would be exact, and in exchange has no side
      effects, costs a model tick per simulated tick, and improves with every shot either the
      player or the companion fires.
```

### What is learned, and what it is keyed by

Every property in the survey's taxonomy is either read from the game, learned from observation, or explicitly out of scope with a revival condition. Nothing is keyed by weapon category.

| Property | Key | Source | Learned as |
|---|---|---|---|
| Volley shape: how many projectiles per use, angle and speed offsets, where they start relative to the shooter and the aim point | item | the player's own uses of that item (spawn hook, `EntitySource_ItemUse_WithAmmo` from the local player), each sample carrying the player's ammo and his multishot-relevant state (archery potion, quivers), and the companion's own uses | a small empirical distribution per item and per volley slot: count, offset angle from the aim line, speed ratio, origin offset from the shooter and from the cursor, and each projectile's damage as a share of the item's damage at that moment, read from the spawned projectile, because a vanilla shotgun's pellets each carry the item's damage and a forty-bullet modded shotgun's plainly do not, and a simulator that priced every pellet at full damage would leave the residual learner correcting a forty-fold error; the projectile type is substituted from the companion's own ammo at fire time where the item uses ammo, and learned per volley slot where it does not (card weapons and the Zenith fire several distinct types from no ammo), and samples taken under a multishot effect the companion lacks are kept apart from those taken without |
| Flight law | projectile type | full traces of every watched projectile of that type to its death, the player's as well as the companion's, with the player's cursor recorded beside his shots | a sum of terms from a fixed library, each kept only if it earns its place in the fit (below); the player fires most types hundreds of times before handing the item over, so most laws arrive already learned |
| Tile response | projectile type | velocity just before and after every tile contact the trace crosses, with the contact normal from the mod's own tile test | dies, passes through, reflects with a per-axis restitution and speed change, stops, with a bounce count and a behaviour change at the last bounce |
| Enemy response | projectile type | the landed-hit ledger and the outcome window | distinct bodies struck before death against the instance's declared `penetrate`, damage decay per hit, repeat hits on one body against the sample's hit cooldown |
| Children | projectile type → child type | the existing parent-source attribution in `TrackLandedHits` | trigger (hit, wall, timer, death), child count, offset and velocity relative to the parent; each child flies its own learned law |
| Area | projectile type | bodies struck off the trace near the impact | an impact radius |
| Push | item × enemy type | unchanged: `LearnWeaponEffectsOnEnemies` | unchanged |
| Damage ratio and unmodelled value | item × context | unchanged in shape: `LearnAttackOutcomes` | now a residual on a much better prior, so it should shrink toward one as the layers above it improve; its size is a measure of how much the simulator still misses |
| Debuff chance and length | item × enemy type | unchanged | unchanged |

### The flight law is a library of terms, not a formula

Today's learner fits four numbers — gravity onset, gravity, horizontal drag, fall cap — and marks anything else unfittable, which the gear then refuses. The survey says modded flight is built from a small set of motions, so the replacement fits a sum of terms chosen from a closed library and keeps a term only when it reduces the trace residual by more than its own cost. This is the approach known as sparse identification of dynamics: propose many candidate terms, keep the few the data needs.

```
velocity next tick = velocity this tick
  + gravity after an onset tick, up to a fall cap        (today's four numbers)
  + horizontal and vertical drag
  + speed change over time                              (accelerating and decelerating shots)
  + homing: blend toward the nearest eligible enemy,     (fitted blend weight, activation radius,
    within a radius, after a delay                        onset delay)
  + steer toward the aim point, at a capped turn rate    (cursor-steered projectiles, once the
                                                          cursor they read is the companion's aim)
  + return toward the owner after a turn tick            (boomerangs; the owner is the player, below)
and at a tile contact: die | pass | reflect(restitution per axis, speed factor, count) | stop
and at a lifetime: die, read from the sample's timeLeft
```

A type whose residual stays large after the library is exhausted is **not refused**. It is marked unpredictable, fired at the intercept, and valued by the outcome learner alone, which is what already happens to any value outside the trace today. Refusal on unfittability goes, because it turns an imperfect prediction into no weapon at all.

### Owner and cursor reads

Companion projectiles are owned by the local player (`ItemWeapon.cs`, `Projectile.NewProjectile(..., Main.myPlayer)`) so kills, drops and on-hit effects stay the player's. That ownership reaches two kinds of AI read, and they have different answers.

**Cursor reads are spoofed.** A projectile whose AI reads `Main.MouseWorld` steers toward the player's mouse. A `GlobalProjectile` `PreAI` sets the mouse position to the companion's current aim point for registered companion shots and `PostAI` restores it, which is the mechanism TerraGuardians ships in `ProjMod.cs`. `Main.MouseWorld` is the screen position plus the mouse in screen pixels, so an aim point off screen is a negative or oversized mouse value, and AI that reads `ClampedMouseWorld` clamps it to the screen edge; the spoof writes the true value and the flight law learns the clamp where a type applies one. The steer-to-aim term is only learnable from companion shots once this is in, so it lands early; it is learnable from the player's shots immediately, because his cursor is recorded beside them.

**Owner-position reads are not solved by the spoof.** A boomerang's AI returns to `Main.player[owner]`, which is the player, however the cursor is set. Three options:

```
boomerangs, spears and anything anchored to its owner's body
├─ ● keep refusing them at the slot, by the property "its AI reads the owner's position",
│     detected from the player's own shots of that type (the flight returns toward him)
│     rather than by a list of aiStyles — the existing planning card proposes exactly this
├─ ○ accept "returns to the player" as the learned law and let outcomes price it
│     → cheap, but a boomerang thrown from a stand far from the player flies to the player
│       and hits whatever lies between, which is a behaviour no player expects
└─ ○ make the stand-in player the owner during companion projectile AI, at the companion's body
      → exact, but kills, drops and on-hit effects then belong to the stand-in and have to be
        re-attributed to the player, and the stand-in is today only active inside hostile AI
```

### Modifiers are inputs to the simulator, never absorbed by learning

Mastery's Piercing and Extra projectile nodes, and anything later, change a use after the item's own behaviour has been decided. They enter at one seam, `ApplyCompanionModifiers`, between the volley and the spawn, and they are recorded on every observation. Consequences:

- The volley is learned from the player's unmodified uses, so an extra companion projectile never teaches "this item fires two".
- The flight law is keyed by projectile type and is indifferent to how many were fired.
- Hits are learned relative to the instance's declared `penetrate` as spawned, so a +1 pierce instance is predicted by reading the instance, with nothing to relearn.

### Scope held out of the first build, each with what brings it back

| Out | Why | Revives when |
|---|---|---|
| Channelled beams, charge-then-release, yoyos, flails, whips, spears | their behaviour depends on a held button or live owner state (`player.channel`, `itemAnimation`) that the companion does not produce; spoofing `player.channel` risks the player's own item use | a companion-side hold counter is designed and a fixture shows a vanilla channel beam living exactly as long as the companion holds |
| Summons, sentries, minions | a second actor with its own targeting; the item's own cast is trivial and its value is the minion's | the owner rules that the companion may own minions |
| Class-resource gates (Calamity stealth, Thorium empowerment, Metroid overheat) | the alternate projectile is chosen in item-use code the companion never runs | the owner rules whether the companion mirrors a class resource the way it mirrors mana |
| Items whose behaviour is not fixed per item (Stars Above aspects, Metroid addons) | one key sees several weapons interleaved | detected rather than solved: a law whose residual splits into two clusters by spawned type is split by type automatically, which the per-type key already does for projectiles that differ |
| Whip tags exploited by minions | the effect is in other actors | out with minions |

## Layer 2 — Attack planning

### Candidates

A candidate is a stand, a weapon, a target set and an aim. They are generated, never enumerated exhaustively:

- **Stands** come from proposal generators that read layer 1, plus the current spot and the positioner's own sampled stands:
  - around each priority target at the distances where this weapon's simulated yield per use is highest (a spread weapon's yield falls with distance; a straight single shot's does not; neither is written down anywhere);
  - along lines through pairs and chains of hostiles, extended behind the nearest body to the weapon's useful range, for any projectile whose simulated pierce exceeds one (a worm's segments are a chain);
  - low beside a group at floor height for any projectile whose law has gravity and a floor response;
  - points with no line of sight to the target from which a simulated bounce reaches it, evaluated only when direct stands fail.
- **Weapons** are every weapon in hand, from one slot to however many mastery grants.
- **Target sets** are the hostiles a simulated use strikes; a target is not chosen first and a shot fitted to it second.
- **Aims** are the simulator's own angle sweep at a resolution derived from the angle the target's box subtends at that distance and the projectile's box, plus the volley's learned spread centring.

### Objectives

Each candidate and each plan is scored on a vector, all over the plan's own horizon:

| Objective | Better | Meaning |
|---|---|---|
| Damage landed per second | higher | health-capped, so overkill earns nothing |
| Enemies removed | higher | kills inside the horizon, with the wound credit for danger removed before a kill |
| Harm prevented to the player | higher | expected hits on the player that removed or pushed-away enemies no longer land |
| Harm taken by the companion | lower | predicted incoming damage at the stand and along the travel, from the threat sense |
| Danger added by pushes | lower | unchanged `InducedDanger` |
| Time to first damage | lower | travel plus reload plus flight |
| Mana spent against the pool | lower | the magic gradient the pool already applies |

A finishing preference for low-life enemies is not a separate objective: it falls out of enemies removed and harm prevented, which is where the owner's "low health enemies" wish is paid.

Dominated candidates are dropped. The survivors are weighted by functions of the senses and the highest wins. The first build carries today's blend as the weights, so the switch to a vector changes no behaviour until the weights are deliberately moved.

### Sequences: plan a few segments, commit the first, plan again

A plan is up to a small number of segments, each a stand held while the best attacks from it run until an event ends it: its targets are dead, its value from the next stand is higher, or its time is up. Inside a segment the existing greedy continuation in `EvaluateAttackOutcomes` picks shot after shot. Across segments the planner runs a beam search: the best few first segments, then the best few second segments proposed from the enemies the first leaves alive. This is receding-horizon planning — plan several steps, act on the first, replan at the next rescore — and it is what produces "snipe the boss while the goons are alive, shotgun the goons, then close in and shotgun the boss" without a rule for it: the close-in segment is worth less while goons would hit the companion at the close stand, and worth most once the first segment has removed them.

Alternatives considered for sequencing:

- **Greedy one step (today)** loses because it cannot value moving later; it takes the best shot now and never prices the stand it will need next.
- **Monte Carlo tree search** loses on cost and on reproducibility: many random rollouts per rescore against a frame budget, and a seed per fixture to make any row stable.
- **Beam search over stand segments** wins because stands are few, weapons are few, and the expensive part, simulating a use, is cached across segments.

### Commitment

A committed plan is kept while its first segment's stand still belongs to the region it was admitted against and its targets still exist, and replaced only when the same evaluation prices a different plan strictly higher on the weighted value with the kept plan re-evaluated in the same pass. This is the walker's lesson that a destination is kept by membership, not by a bonus.

### Budget

Evaluation is anytime: candidates are ordered by a cheap upper bound (the simulated value with no geometry in the way, today's `IdealShotValue`), evaluated best-bound first, and cut when the rescore's millisecond budget runs out. A cut plan is reported as unresolved, never as "no attack exists", which is the repository's rule for every bounded search. Simulated uses are cached by (projectile law revision, stand cell, aim bucket, local terrain revision, enemy prediction tick).

## Layer 3 — Execution and the activities

The hands fire only on a tick whose current activity is Combat. On such a tick, with a free hand, they fire the best attack from where the body actually is, which is the committed plan's step when the body is at its stand and the best from-here candidate while it travels there, so a fight is fought in real time on the way to a better spot rather than suspended until arrival. On every other activity's tick the arsenal is not asked for a shot at all, and the recorder's fire outcome reads that the activity does not fight rather than no-target, so a quiet weapon during mining is never mistaken for a missing target. The hands reproduce the learned volley themselves — N spawns at the learned offsets and origins — so the ruling that an item is read and never run survives. Downed and recovery-flight ticks do not fire either, since neither is Combat.

Combat offers the committed plan's value, weighted by the senses, so a threat on the player, a threat on the companion and an enemy that is merely near are one activity at different weights rather than two activities with their own positioning. Combat is offered whenever a damageable hostile is inside the region it may fight in and at least one handed weapon has any candidate; with no weapon in hand it offers nothing, which is the open card about an unarmed orb answered by construction. When it wins, it sends the positioner one request kind carrying the plan's chosen stand, and the positioner routes to it or refuses with a reason; it scores nothing about a firing stand.

Three consumers of today's prior are named so none is orphaned. Protection's `EstimateInterventionTicks` and pursuit's `EstimateRemovalTicks` read the simulator's kill times in place of the arc-free single-hit assumption, because an optimistic removal time that ignores spread and misses is what lets guarding believe a threat is handled. `LearnAttackOutcomes.Context` normalises hostiles along the lane by the hostile count the forecast considered rather than by eight, so the feature keeps a zero-to-one range without a cap.

## Every hard-coded number is one of three kinds

| Today | Kind after | Replacement |
|---|---|---|
| `Arsenal.MaxPierceCounted = 8` | game fact | the instance's `penetrate` and the sample's hit cooldowns; the body buffer is the hostile list |
| `Arsenal.HorizonTicks = 180` | derived | the plan's segment lengths from simulated kill times, bounded by the budget |
| `EvaluateAttackOutcomes.MaxAttacks = 16` | derived | horizon over use time |
| `ProjectileArcs.SamplesPerShot = 45` | game fact | the whole flight, to death or `timeLeft` |
| `MinPairsToFit = 4`, `MaxFlightsKept = 8` | statistical confidence | a term is used once its parameter's uncertainty is below a stated share of its value; traces kept by how much they reduce that uncertainty |
| `WeaponAimOffsetRadians`, `WeaponAimOffsetSteps` | derived | the subtended-angle sweep |
| `ResolveFiringOpportunity` sample radius 14 tiles, stride 2 | derived | the proposal generators and the weapon's simulated useful range |
| `Weights.FiringStandValueFloor = .5` | removed | the objective vector prices the stand directly |

Two kinds of number remain and are named as what they are: compute budgets, whose cuts are always reported, and statistical confidence thresholds, which decide when evidence is enough and say nothing about behaviour.

## Observability: what the god's eye, the recorder, the report and the overlay gain

**God's-eye records** (`Companion/Brain/Infrastructure/Diagnostics/RecordGodsEyeEvents.cs`):

- `volley-observed` — item, who fired (player or companion), projectile count and types, offsets, origin kind.
- `shot` gains a shot id, the plan id and step, and the predicted bodies with predicted hit ticks.
- `shot-event` — per companion projectile, bounded per shot: tile contact with velocity before and after, body hit with tick and damage, child spawn, death and why.
- `motion-law` — when a projectile type's law changes: the terms kept, their values, the residual, the evidence count.
- `attack-plan` — on commit and on replacement: the segments (stand, weapons, targets), the objective vector, the front's size, the three best rejected plans and whether each was dominated or lost on the weights, and whether the budget cut the search.

**Recorder columns** (`RecordBrainTelemetry.cs`): plan id, segment index, segment stand, the chosen plan's objective vector in a compact form, front size, unresolved flag, the learner's sampled and mean factor (named as missing today in `Weapons/CLAUDE.md`), and the last closed shot's forecast error.

**Session report**:

- a new `Tools/SessionReport/Checks/CheckTheWeaponKnowledge.cs`: per projectile type, predicted against landed bodies and damage and the trace error at impact; laws that never converged; items fired whose volley was never observed.
- `CheckTheFight.cs` gains plan decided-against-performed (the stand reached, the shots then fired from it matched the plan's step) and plan churn per minute, which is the oscillation detector.
- measures pinned against a capture: hits per use per weapon and forecast calibration.

**Overlay** (`DrawBrainOverlay.cs`): the aiming layer draws the simulated trace with bounce points and child branches; a new plan layer draws the proposed stands coloured by value, the committed plan as numbered stands with arrows, and the front on hover.

## How bugs are kept out

Each row names what it varies and the mutation that must turn it red. Knowledge rows run native vanilla projectile AI headless, the way `VerifyArcLearning` does, so they test learning against the game's own code. Planning rows plant knowledge directly, so they test planning and not learning.

| Row | Asserts | Mutation that must fail it |
|---|---|---|
| K0 learned from the player | a law fitted only from player-owned flights of a type predicts the companion's shot of that type | watch companion shots only |
| K1 delayed gravity | the throwing knife's law still matches native tick for tick | drop the onset term |
| K2 bounce | Water Bolt's learned law predicts its bounce points against a fixture box | disable the reflect response |
| K3 homing | Chlorophyte bullet's predicted path to a placed body within tolerance | drop the homing term |
| K4 pass-through | a `tileCollide = false` type predicted through a wall | force die-on-contact |
| K5 children | a splitting vanilla projectile's children predicted by trigger and count | drop child simulation |
| K6 cursor spoof | a mouse-reading vanilla projectile follows the companion's aim point | spoof off: it follows the player's mouse |
| K7 modifier isolation | a +1 pierce instance strikes one more body on a line and leaves the learned law unchanged | let observations with modifiers update the law |
| K8 no cap | an unlimited-pierce shot along twenty segments counts twenty | reinstate a cap of eight |
| K9 unpredictable still fires | a type the library cannot fit is fired and valued by outcomes, not refused | restore refusal on unfittability |
| P1 spread closes in | a spread weapon beats a single-shot weapon only near, and the plan closes in on a lone boss | ignore the volley's spread |
| P2 worm line | the chosen stand lines a pierce along a segment chain | remove line proposals |
| P3 goons then boss | the plan's segments come out in that order | depth one |
| P4 floor roller | a low flank stand is chosen for a gravity-and-floor piercer against a group | remove floor proposals |
| P5 bank shot | a target behind a corner is struck by a bouncing weapon and refused by a straight one | disable bounce simulation |
| P6 danger weighting | high player danger picks the harm-prevention plan over the damage plan | constant weights |
| P7 stability | an unchanged scene keeps one plan across rescores | a score-bonus hold instead of membership |
| P8 budget cut | a starved budget reports unresolved, not no attack | report a cut as absence |
| C1 cost | forty hostiles, four weapons, planning inside the frame budget | measured, not asserted |
| F1 only combat fires | with a hostile in reach and a clear line, mining, lighting, collecting and keeping company fire nothing and record that the activity does not fight | let the hands fire on any activity's tick |
| F2 combat is eager | a damageable hostile in reach makes Combat beat keeping company | restore today's hunting and guarding scores unchanged |
| F3 danger lifts combat over work | an enemy on the player takes the body from a vein; an idle enemy far off does not | drop the player-danger term from Combat's offer |
| F4 dodging survives | a projectile at the companion mid-vein bends the body and the vein continues | gate the evade layer on Combat |
| F5 unarmed | empty weapon slots offer no Combat at all | offer Combat on a hostile regardless of weapons |

`Tools/check-navigation-boundary.sh` gains the planning folder in the set that may not run a route search, because stand verdicts come from the reach sense and the positioner. Every build lane is reviewed by a sentinel before merge, and by Codex beside it once Codex is available again.

## Where it lives

```
Companion/Weapons/
├─ CLAUDE.md                          the three layers and their boundary
├─ Knowledge/                         layer 1: learned, never deciding
│  ├─ ObserveWeaponUses.cs            player and companion uses → volley samples, with modifier state
│  ├─ LearnVolleyShape.cs             count, types, offsets and origins per item
│  ├─ FlightLawTerms.cs               the closed term library and one tick of flight under a law
│  ├─ LearnFlightLaws.cs              term selection and fitting per projectile type (replaces Brain/Infrastructure/Aiming/LearnProjectileArcs.cs)
│  ├─ LearnTileResponses.cs           contact events → die, pass, reflect, stop
│  ├─ LearnHitResponses.cs            bodies per life, decay, repeat hits, children, area
│  ├─ LearnWeaponEffectsOnEnemies.cs  moved unchanged
│  └─ LearnAttackOutcomes.cs          moved; now the residual on the simulator
├─ Simulation/
│  ├─ SimulateUse.cs                  volley → modifiers → each projectile's flight, walls, hits and children against predicted enemies
│  ├─ ApplyCompanionModifiers.cs      the one seam mastery's weapon nodes enter by
│  └─ CacheSimulatedUses.cs
├─ Planning/                          layer 2: decides, owns no body and no search
│  ├─ ProposeFiringStands.cs          the four generators
│  ├─ GenerateAttackCandidates.cs
│  ├─ EvaluateAttackOutcomes.cs       moved; returns the objective vector
│  ├─ PlanAttackSequence.cs           beam search over stand segments
│  ├─ ChooseOnTheParetoFront.cs       dominance filter and sense-derived weights
│  └─ CommitAttackPlan.cs             membership hold and replacement
└─ Execution/                         layer 3a: the hands
   ├─ Arsenal.cs                      thinner: fires the plan's step or the best from-here candidate
   ├─ ItemWeapon.cs                   reproduces the volley
   ├─ SpoofOwnerInputForShots.cs      cursor at the aim point during companion projectile AI
   ├─ TrackLandedHits.cs              gains tile-contact and death events
   ├─ ObserveShotOutcomes.cs
   └─ CompanionMana.cs

Companion/Brain/Infrastructure/Aiming/   keeps the enemy-motion prediction; the projectile solver folds into Simulation
Companion/Brain/Infrastructure/Position/ChooseUsefulPosition.cs   returns stand verdicts to the planner and routes to the chosen stand; FiringStandShare, StandoffFromTarget and the LineOfFire and Guard score formulas go
Companion/Brain/Activities/Combat/FightEnemies.cs   the one Combat activity; replaces ProtectPlayer.cs, PursueAttackOpportunity.cs and ResolveFiringOpportunity.cs
Companion/Brain/CoordinateBrainTick.cs   Engage asks for a shot only when the current activity is Combat
Companion/Brain/Infrastructure/Selection/BehaviourWeights.cs   the Guard* and Hunt* weights become Combat's, or go where the plan's vector replaces them
Companion/Brain/Infrastructure/Selection/ChooseBehaviour.cs   six activities in three families
Companion/Brain/Infrastructure/Position/   one firing request kind in place of LineOfFire and Guard
Companion/PlayerIntegration/ConfigureCompanionPreferences.cs   the saved Hunting preference becomes Combat, reading the old "hunting" key so saves keep the player's choice
Companion/Brain/Infrastructure/Diagnostics/RecordBrainTelemetry.cs   combat labels in place of hunt and guard; the fire outcome names an activity that does not fight
Tools/SessionReport/  CheckTheFight, CheckTheChoices, CheckDecisionContracts, CheckThePlayersReference, CheckTheRecord, MultiRunReport, MeasureCommitmentAndChoice and the chronicle tests read combat labels, and keep reading hunt and guard in older captures
Tools/EngineReplay/   the twelve fixtures that name hunting or guarding are rewritten against Combat, and F1–F5 added
Tools/EngineReplay/Combat/Knowledge/     K rows
Tools/EngineReplay/Combat/Planning/      P rows and C1
Tools/SessionReport/Checks/CheckTheWeaponKnowledge.cs
```

## Build order

Each phase keeps the suite green and lands behind its rows. Phases 1–3 are independent of each other; the rest are ordered.

```
0 instruments       shot-event and motion-law records, CheckTheWeaponKnowledge, forecast calibration measured on today's code
A one combat        hunting and guarding merge on today's arsenal and stand pricing; the hands fire only in Combat;
                    F1–F5; then a play session to tune how readily Combat wins before anything below changes the fight
1 caps              game-fact pierce and lifetime; K8
2 cursor spoof      K6
3 volley            learned from the player's uses and reproduced by the hands; K7's volley half
4 flight library    terms, tile responses, unpredictable-still-fires; K1–K5, K9
5 hit responses     bodies per life, children, area
6 vector evaluator  today's weights, behaviour unchanged; then P6
7 stand proposals   P1, P2, P4, P5
8 sequences         P3, P7, P8, C1
9 modifier seam     reads mastery's bonuses when that lands; K7
```

## Questions for the owner, each with the default taken if unanswered

1. **Where the volley comes from.** Default: learned from the player's own uses and reproduced by the companion, keeping the ruling that an item is read and never run; a weapon the player has never fired fires one projectile until he does. The alternative is calling the item's own `Shoot` hook on a stand-in, which reaches modded volleys without the player but runs item code, and still misses vanilla spreads, which live in `Player.ItemCheck_Shoot`.
2. **Cursor spoofing during companion projectile AI.** Default: yes, as TerraGuardians does.
3. **Held and charged weapons.** Default: refused in the first build, with the revival condition above.
4. **Projectiles anchored to their owner's body.** Default: refused by that observed property, as drawn under owner reads.

Decided rather than open: hunting and guarding are one Combat activity and only Combat fires (owner, 15 September 2026, recorded in the verdict).

## The roadmap cards this plan absorbs

These open cards on the AICompanion board are parts of this plan rather than separate work, and each is re-parented or closed into it when building starts:

- The companion walks to where the shot is worth most, so it lines a pierce up through three enemies — phase 7.
- Position selection prices each candidate firing spot by the best attack every handed weapon could make from there (AIC-395) — not open after all: it landed in `b987d8a` and was closed on the board when this plan was checked against the code; the one-valuer split in phase 7 replaces it.
- The arsenal learns what each weapon leaves on each enemy type and how the other weapon's hits change against it — kept as layer 1's debuff row, unchanged in shape.
- The gear refuses a weapon whose projectile anchors to its owner's body by that property — question 5.
- A sword that also fires a projectile sweeps its arc as well as shooting — the volley row, once a use can put a swing and a projectile into the world together.
- The item weapon composes damage and speed with the engine rules it still omits (archery potion, quivers) — the volley row's multishot state, and the damage composition beside it.
- A companion with no weapon in its slots neither shoots nor treats combat as its job — phase A, row F5.
- A mode icon beside the notch shows what the companion is doing, with a shield for guarding (AIC-92) — the icon names Combat instead.

## What this deliberately does not do

It does not shoot outside Combat, run item-use code, predict enemy decisions beyond their observed motion, count damage over time, or give the companion minions. It does not promise that every modded weapon is used well: it promises that every weapon inside the taxonomy is represented, that anything outside it is still fired and valued by its outcomes, and that the report says which is which.

## What would refute it

If the flight library's residual on the survey's vanilla examples (Water Bolt, Chlorophyte bullet, Meowmere, boomerang) stays large with every term available, the term library is the wrong representation and a non-parametric trace memory is the next candidate. If C1 cannot fit forty hostiles and four weapons in the frame budget with caching, sequences drop to one segment and the plan's stand is kept longer instead.
