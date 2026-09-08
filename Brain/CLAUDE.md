# Brain — how the companion behaves

This file is the whole account of how the companion decides and acts, as the code stands on 2026-09-08. It is written so that someone who has never opened a source file can predict what the companion will do in a given situation and, when it does something else, know which number or rule to look at. Every subfolder has its own `CLAUDE.md` with the file map and the traps; this one is the mechanism. Where a number appears it is the live constant, and `DecisionMatrix/Decision/Weights.cs` is the only place most of them are set.

Nothing here draws or touches health; that is `../Companion/`. The weapons are equipment and live in `../Combat/`. Nothing in the brain has been watched running as of this writing beyond one short surface run; every claim below is what the code does, and "verified in play" is stated where it is true and nowhere else.

```
Brain/
├─ CLAUDE.md
├─ Brain.cs            the tick order below; holds one of each decision-matrix part and the last position request
├─ DecisionMatrix/     how a choice is made, independent of what the choices are
│  ├─ Senses/          the world model and the derived numbers (danger, horizon, light); never decides
│  ├─ Decision/        the chooser, the considerations, the weights, position requests
│  ├─ Positioning/     where to stand, as a score over candidate tiles
│  ├─ Navigation/      how to get there: grid, A*, path following, reachability
│  └─ Reflexes/        the fast path that skips scoring: simulated dodges
├─ Actions/            what can be chosen, by family; each scores itself and asks for a spot
│  ├─ Companionship/   walk-with, guard, wander
│  ├─ Combat/          hunt, kite
│  ├─ Gathering/       loot
│  └─ Work/            chop, mine
├─ Work/               the tools an action drives once chosen
│  ├─ Chopping/        trees and the axe
│  ├─ Mining/          ores and the pickaxe
│  └─ Torch/           the torch in the dark
├─ Aiming/             the arc solver every ranged weapon and the positioner share
└─ Debug/              the overlay that shows all of the above (key left of 1)
```

## One tick, in order

Every game tick the NPC's `AI()` runs this sequence. The order matters and it is not the order the folder names suggest: reflexes come *second*, before any decision, because a dodge that waits for a score arrives late.

```
CompanionNPC.AI
├─ 1  mirror the player's max life and defence; count down the swing animation; tick the tools' cooldowns
├─ 2  if downed: lie still, count revive progress; stop here
├─ 3  if the player is dead: stop moving; stop here
├─ 4  Brain.Tick
│  ├─ 4a  Senses.Update        read the world into the world model (player, threats, loot, light, tile hits)
│  ├─ 4b  Arsenal.Tick, Chopper.Tick   weapon and tool cooldowns
│  ├─ 4c  Reflexes.TryTake     if a threat will hit the standing body within 20 ticks and a simulated jump or step clears it, take the body for 8 ticks and STOP the tick here
│  ├─ 4d  Chooser.Choose       every action scores itself; the best (with commitment and the horizon charge) becomes Current
│  ├─ 4e  action.Execute       the winner acts (fires, swings, sets the held item) and returns a PositionRequest
│  ├─ 4f  Positioner.Resolve   the request becomes a feet position, or null for Hold
│  └─ 4g  Navigator.MoveTo     plan or follow a path to it through the motor; or stop and clear the path
├─ 5  Motor.ApplySteps          the game's StepUp/StepDown, so one-tile kerbs are walked not jumped
├─ 6  CollectTouchedItems       anything the body overlaps goes to the player's stacks or the bag
├─ 7  Torch.Update              decide lit/out from the light sense; if lit, emit light and reveal the map
└─ 8  if lit and no action set a held item this tick, the hand holds the torch
```

Then the game applies gravity and tile collision to the velocity the motor set. The body is drawn afterwards from `heldItemType` and the animation, so what you see in its hand is whatever step 4e or step 8 left there.

**What the reflex stop means:** on a tick a reflex takes the body, nothing is scored, no action runs, the position request is stale and the navigator is not called. The hold lasts 8 ticks, then a 45-tick refractory period during which the reflex will not fire again, so an enemy that stays adjacent cannot hold the companion in an endless dodge while the chooser never gets to shoot.

## Senses: what the companion knows, and the numbers it derives

The world model is rebuilt every tick and read by everything else. Senses do not have weights; they produce facts and a few derived numbers, and the *actions* weight those numbers in their scores. Five senses:

**PlayerSense.** Position, bottom, velocity, health fraction, dead, attacking (item animation running with a damaging item). Travel *intent*: the horizontal velocity smoothed toward its current value at 6 % per tick while moving faster than 0.5 px/tick, decaying by 1.5 % per tick when still, so it converges over about three seconds and fades over about two. `IsTravelling` is |intent| above 1.2 px/tick. `Predict(ticks)` is bottom plus intent times ticks: where the player will be if they keep going, which is what the walk-with action aims at. Also whether the player really hit a tree or an ore in the last 45 ticks, from the tile damage watcher, and whether the companion has a sight line to the player.

**ThreatSense.** One record per active hostile that is not friendly, alive, deals damage, is not a critter and can be chased. For each: its movement class (phaser if it ignores tiles, flyer if it ignores gravity, else walker); whether it is *reachable* (walkers: the same A* the companion uses, from the enemy's feet to the player's, budget 400 expansions; flyers: a flood fill through air, budget 1500; phasers: always; a search that runs out of budget answers *reachable*, because a threat wrongly ignored costs more than one wrongly feared; refreshed once per 60 ticks per enemy on a stagger so at most a quarter of them refresh on any tick); its observed speed (the peak of |horizontal velocity| for walkers or full velocity for others, decaying 0.5 % per tick, floored at 0.5 px/tick, starting at 1); whether it shoots (a fixed list of pre-hardmode shooters, or any enemy that had a hostile projectile appear within 48 px of it in the last 240 ticks; Demon Eyes are deliberately not shooters, their AI never spawns a projectile); its distance to player and companion; whether it has a sight line to the player; and `TicksToPlayer` = distance to player ÷ observed speed.

From those, two numbers the whole brain leans on:

```
Urgency(threat)  = weight × closeness × sight                            (0 if unreachable)
   weight        = 1 for a boss, else clamp(damage ÷ (25 % of player max life), 0.2, 1)
   closeness     = clamp(1 − TicksToPlayer ÷ 360, 0, 1)                   1 at contact, 0 six seconds out
                   raised to at least 0.8 for a shooter with a sight line   (it is effectively already there)
   sight         = 1 with a sight line to the player, else 0.5

PlayerDanger     = max Urgency over threats                               "PlayerIsSafe" is danger < 0.25
Horizon          = min over reachable threats of max(0, arrives − companionReturnTicks)
   arrives       = 0 for a shooter with sight, else TicksToPlayer
   return        = companion-to-player distance ÷ walk speed (3.5 px/tick)
                 = float.MaxValue with no reachable threat
```

The horizon is the mechanism behind every "go do something, but come back in time" behaviour: it is how many ticks the companion may stay away before the player is at risk. Any action that forecasts a job longer than that is charged for the overrun (next section).

**LootSense.** Every ground item within 1200 px that can be grabbed, sorted nearest first, each with a value: coins 1.0, everything else 0.3 rising with sell value to 1.0. Junk is still collected; value only ranks.

**LightSense.** Three brightness readings, refreshed every 10 ticks: at the player's tile, at the companion's tile, and *ambient*, the mean over a 4-tile grid across the screen with a 10-tile disc around the companion cut out. Ambient is the one the torch reads, because the companion's own torch reaches about eight tiles and so cannot raise it. Off screen the lighting engine holds nothing and every reading is 0.

**TileDamageWatcher.** Not polled: the game's own `KillTile` hook fires for every axe or pickaxe hit including the ones that only crack the tile, and this records the last tree (its trunk bottom) and the last ore (tile and type) the *player* hit, with a tick stamp; the companion's own tools raise a flag around their hits so they are excluded. This is what makes "the player is really chopping" true only when an axe is really hitting a tree, and never when an axe-sword is swung at a boss.

## Decision: how an action wins

Every tick every action returns a score in 0..1, built as a product of *considerations*, named curves from a sensed value to 0..1 (`Inverse` falls from 1 to 0 over a range, `Rising` climbs, `Band` is 1 inside a range and falls off outside, `Step` is a switch, `AtLeast` is a floor). Because they multiply, any single zero vetoes the action and no other term can buy it back; an action that should stay eligible uses a floor.

The chooser then applies two adjustments and takes the highest:

```
final = raw
      × 1.15 if this action is the one currently running          (commitment: no flicker between near-equal scores)
      × max(0, 1 − (forecast − horizon) ÷ 240)   if forecast > horizon   (the horizon charge)
```

`forecast` is the action's own estimate of how many ticks it keeps the companion away from the player (0 for anything that stays with them). An action that would outlast the horizon loses score linearly and hits zero once it overruns by 240 ticks. So a far loot run is fine with no threats (horizon infinite), gets discounted when a slime is three seconds out, and is impossible when a shooter has a line on the player (horizon 0). An all-zero board falls to the last action, wander, which holds still when the player is dead.

The actions and exactly what they value. `safe` below means `max(1 − PlayerDanger, floor)`.

| action | score | forecast | asks the positioner for | in hand |
|---|---|---|---|---|
| **guard** | `danger × max(Rising(distance to player over 400), 0.4)` — zero when the player is dead | 0 | Guard near the player, target = the most urgent threat | the chosen weapon (it fires) |
| **kite** | `Inverse(nearest reachable threat's distance to the companion, 96)` — 1 when touching, 0 at 96 px | 0 | Retreat, target = most urgent | the chosen weapon (it fires) |
| **hunt** | `safe(0.1) × max(Inverse(target distance, 1100), 0.2) × (1 boss / 0.85)`; target = best of `0.4 × urgency + 0.6 × Inverse(distance to companion, 1100)` (+0.3 boss), over threats that are reachable or on screen with a solvable shot | `max(0, distance − 200) ÷ 3.5 + 60` | LineOfFire at the target | the chosen weapon |
| **loot** | `max(Inverse(distance, 900), 0.2) × item value × safe(0.05)` for the nearest pickup that fits somewhere and has a standable tile beside it | `distance ÷ 3.5 × 1.5` | Exact at the item | empty |
| **chop** | `0.7 × safe(0.1)` while the player has hit a tree in the last 45 ticks or the job is under 120 ticks old; target = nearest other standing tree within 40 tiles with a standing spot | `trip ÷ 3.5 + 120` | Exact at the standing spot, then Hold while swinging | empty on the walk, the player's axe in position |
| **mine** | `0.7 × safe(0.1)` while the player hit an ore in the last 45 ticks or in the last 600; target = nearest same-type ore outside the player's vein within 45 tiles, else any ore, with a standing spot in reach | `trip ÷ 3.5 + 180` | Exact at the standing spot, then Hold while swinging | empty on the walk, the player's pickaxe in position |
| **walk-with** | travelling player: `max(Rising(gap to the player's predicted position 45 ticks ahead, 280), 0.3)`; standing player: `Rising(distance − 560, 400) × 0.6`, so zero inside the calm band; either way at least 1 when the companion is past the hard leash of 1400 px | 0 | WithPlayer, anchored at the predicted position (or the player when still) | empty |
| **wander** | a flat 0.05; zero when the player is dead | 0 | Hold while standing, Exact at a random spot within 70 % of the calm band while strolling; a rare hop | empty |

Three consequences worth reading off that table. Guard beats everything as danger approaches 1, which is what pulls the companion off a hunt or a loot run when a shooter gets a line on the player. Chop and mine at 0.7 beat walk-with (at most 0.3 while you travel, 0 while you stand near) and beat loot unless the loot is close and valuable, so a job continues while the player keeps working, and stops within two seconds (chop) or ten (mine) of the player stopping. Wander only wins when everything else is zero, which by design is "the player is standing still, nothing is around, the companion is inside the calm band".

## Positioning: where it stands once it knows what it is doing

A position request has a kind, an anchor and an optional target. `Hold` means stand still; `Exact` means the nearest standable tile within 3 of the point, no scoring. The other four are *scored*: every standable tile in a 29×29 box around the anchor at a stride of 2 (about 200 candidates) gets a product of factors, and the best wins. Rescored every 12 ticks or when the request kind or target changes, so the companion does not twitch between two equal spots.

The factors, all 0..1:

- **band**: `Band(distance to the anchor, near, far, falloff 400) × (0.6 + 0.4 × Inverse(distance, far + 200))`; the band is 96–560 px when the player is safe and 32–160 px when not, so it closes in under threat.
- **sight**: 1 if the spot's eye can see the player, else 0.35.
- **fire**: 1 if the aimer solves a shot at the target from the spot with the chosen weapon, else 0.15 (only for requests with a target, and only computed for the 8 best candidates by the cheap factors, because an aimer solve is up to 48 arcs × 150 ticks of tile checks).
- **danger**: 1 if any reachable threat's predicted hitbox passes through a 20×42 body at the spot in the next 40 ticks, else `0.6 × Inverse(distance to the nearest threat, 160)`.
- **open**: `0.7 × Inverse(solid tiles in a 5×3 box at eye height, 12) + 0.3`, penalising crevices.
- **travel**: 0.6 for a spot behind the travelling player, 1 otherwise.
- **standoff**: `Band(distance to the target, 120, 520, 300)`, so a firing spot is neither on top of the enemy nor out of range.

```
WithPlayer   band × sight × (1 − 0.8·danger) × open × travel
Guard        Band(distance to player, 24, 120, 200) × sight × fire × (1 − 0.5·danger) × open
LineOfFire   fire × max(band, 0.3) × (1 − 0.7·danger) × open × standoff
Retreat      (1 − danger) × max(band, 0.3) × fire × open
```

Only ground tiles are candidates. A jump apex as a firing spot (the "jump to shoot over the hill" idea) is not sampled yet.

## Navigation: how it gets there, and why the ledge climb is possible

The navigator receives a feet position each tick. It is "arrived" within 12 px. Otherwise it plans when the goal tile changes, every 30 ticks, when the path is finished, or after 40 ticks without moving; a plan that failed is not retried for 90 ticks unless the goal moves, because a full failed search costs about 3 ms. With no path it walks straight at the target and jumps only at a wall.

The grid is not the tile map; it is the set of *feet tiles a 1×3 body can stand on*: support beneath (solid or a platform), three clear tiles above, no lava anywhere in the column. Edges are generated on the fly from what the body can do, and this is what answers the ledge question:

```
from a feet tile, the neighbours are
├─ walk      the tile beside it, if standable                                            cost 1
├─ step      the tile beside and one up, if standable and the column above is clear      cost 1.5
├─ drop      off an edge, straight down to the first standable tile within 40           cost 1 + 0.2 per tile
└─ jump      any standable tile up to 5 up and 4 across, given headroom above the start,
             a clear body column at the apex above the start and a clear row to the landing;
             also a same-row gap of 2–4 tiles                                            cost 2 + 0.5 per tile across + 0.5 per tile up
```

A* (the textbook best-first search with the heuristic `|dx| + 0.5·|dy|`) runs over those edges with a budget of 1500 expansions. So the three-ledge climb to a high ore in the sketch is found in the ordinary way: each ledge is a node, each hop between them is a jump edge, and the search chains them because a path is just a sequence of edges. What A* "as you know it" could not do is the part the edge generator does: deciding that a jump from here lands there. The limits are the edge generator's, not the search's: 5 up and 4 across are derived from the jump velocity and gravity, not measured; the arc check is coarse (apex column above the start plus the landing row, so a low ceiling mid-arc is missed and shows up as a stuck counter and a replan); and a jump that needs a run-up is not modelled, the follower jumps from standing.

Following the path: advance past every step whose feet point is within 10 px; for a walk step, move toward it and jump only for a real wall (`collideX` after the game's step-up has already handled one-tile kerbs) or a rise of two or more tiles, at the fighter AI's jump heights (−6 for two tiles, −7 for three, −8 for four, the full −8.5 above); for a jump step, jump from the ground at the height the rise needs and steer in the air; for a drop, walk off at 80 % speed and let gravity work. The motor lerps horizontal velocity toward ±3.5 px/tick at 25 % per tick.

No digging, no building, by ruling: the grid never plans through a tile.

## Reflexes: the dodge that skips scoring

Before any scoring, for each reachable, moving threat: if its predicted hitbox (straight-line for flyers and phasers, under NPC gravity 0.3 for walkers) meets the standing body at any even tick up to 20, simulate both dodges against the same prediction. The jump: the body offset by the real jump arc (`−8.5·t + 0.15·t²`). The step-back: the body offset by the motor's own lerped acceleration away from the threat, with a room check two tiles that way. Take the jump if it never intersects, else the step if it never intersects and there is room; if neither clears it, take the hit and rest 15 ticks rather than moving into the enemy. Hostile projectiles are not yet considered; that is a second loop to add.

## Aiming: how a shot is solved, and what it cannot solve

Every ranged weapon hands the aimer a *flight profile*: launch speed, how many ticks the projectile flies straight before gravity, the gravity added per tick after that, a terminal fall speed, a maximum flight time and a hitbox size. The vanilla arrow profile is speed = bow + arrow shoot speeds, 15 straight ticks, gravity 0.1 (these are the numbers from the arrow's own AI style). The knife drops from tick 0. The aimer sweeps launch angles from the direct line outward in 3° steps, above the line first, alternating below down to 60° and above up to 81°, and for each angle simulates the flight tick by tick against solid tiles, advancing the target's hitbox along its velocity for the lead; the first arc that lands is the answer. So a clear shot is a straight shot, a lob is chosen only when the straight line fails, and a target behind a wall or under the floor gets no shot.

Two limits. The profile is per weapon class, not read off the projectile's AI: seven bows firing vanilla arrows are seven correct speeds on one correct arc, but a projectile with different physics (a rocket, a boomerang, a modded arrow with its own AI) needs its profile written by hand, and until it is, the aimer would lob it like an arrow. And a solve is expensive, so `CanEngage` is cached 20 ticks per enemy, a failed solve rests the weapon 15 ticks, and the positioner only solves for its 8 best candidates.

Weapon choice: each weapon rates its *suitability* for the target (bow: distance over 500 px, single target, boss; knife: within 420 px, crowd of two or more, not a boss) and the higher wins. Fire rate is the item's use time × 2 and every shot is rotated by up to ±4°, until the mastery tree lifts either. Every projectile is spawned with the player as owner and damage from the player's ranged stat, so kills, drops and on-hit accessories are the player's.

## Work: the tools, and the torch's place among them

A tool never decides; it swings when its action says so, with the game's own formula and the player's held item's numbers. The chopper: axe power × 1.2 (× 3 on cactus), the tile breaks at 100 accumulated damage, every non-lethal hit is a fail-hit for the sound and dust, on the companion's own crack table which the cracks renderer draws. The miner: the same shape with `Player.GetPickaxeDamage` copied line for line (per-type multipliers, the minimum-power gates by tile and depth, a modded tile's mine resistance), on the *same* crack table. If the player's pickaxe could not break a tile, neither can the companion's.

**The torch is the odd one out: it has no action, and it is not in the chooser.** It is what the hand does when nothing else wants it. Step 7 of the tick decides lit or out from the ambient light with hysteresis (lit below 0.22, out above 0.42, never switching within 180 ticks of the last switch); step 8 puts the torch in the hand only if no action set a held item this tick. Actions that want the hand: guard, kite and hunt (the weapon, but only while `TryFire` is called with a live target), chop and mine (the tool, but only in position). So the answer to "does it hold the torch while walking to an ore in the dark" is yes: the mine action leaves the hand empty on the walk and takes the pickaxe out when it arrives, and the torch fills the gap both before and after the swing. On a mine job the hand goes torch → pickaxe → torch as it arrives, mines and leaves, and the light goes with it. A lit torch also reveals the map every 10 ticks: a flood through air tiles to 7 tiles marks the air and the first solid face, never what is behind a wall, on or off screen alike.

## The numbers in one place

| constant | value | where | what it does |
|---|---|---|---|
| Commitment | 1.15 | Weights | bonus for the running action |
| HorizonOverrunToZero | 240 ticks | Weights | overrun at which a charged action scores 0 |
| CalmBand | 96–560 px | Weights | distance band to the player when safe |
| ThreatBand | 32–160 px | Weights | the band when the player is in danger |
| LeashHard | 1400 px | Weights | walk-with scores 1 beyond this whatever else |
| WanderFloor | 0.05 | Weights | wander's flat score |
| FollowIntentDistance | 140 px | Weights | walk-with's gap scale (× 2) |
| LootReach / HuntReach | 900 / 1100 px | Weights | distance over which loot/hunt scores fade |
| KiteTrigger | 64 px (× 1.5) | Weights | kite is 1 at contact, 0 at 96 px |
| DodgeLookaheadTicks | 20 | Weights | how far ahead the reflex looks |
| WalkSpeed / JumpVelocity | 3.5 / −8.5 px/tick | Companion/Motor | the body |
| JumpHeightTiles / JumpGapTiles | 5 / 4 | Navigation/NavGrid | jump edges, derived not measured |
| PlanBudget / ReplanInterval | 1500 / 30 | Navigation/Navigator | search size and cadence |
| Walker / Flyer reachability budget | 400 / 1500 | Navigation/Reachability | out of budget = reachable |
| Reflex hold / refractory | 8 / 45 ticks | Reflexes | body taken, then rest |
| Torch raise / lower / hold | 0.22 / 0.42 / 180 ticks | Work/Torch | hysteresis |
| Torch reach | 7 tiles | Work/Torch | map reveal radius |
| Ore search / vein bound / pick reach | 45 tiles / 400 / 5 | Work/Mining | mining ranges |
| FireRateFactor / AimNoise | 2 / 4° | Combat/Weapons | the launch handicaps |

## What is verified and what is not

Verified in play (2026-09-07 and the first run of 2026-09-08): the body draws and swings, chopping the right tree at the trunk, arrows fly, the health bar, persistence, and that the brain runs (first tick logged, action = wander). Everything else in this file describes code that compiles and has not been watched: the horizon charge, kiting, dodging after the simulation rewrite, the navigator on anything but flat ground, mining, the torch thresholds against real cave light, the map reveal and the map head. The overlay (key left of 1) shows every score, the danger and horizon, the chosen spot and the path, and is how a wrong choice is read rather than guessed; telemetry to a file with a "this looked wrong" key is the next instrument (AIC-51).

## Where a new thing goes

A new fact about the world is a field on a sense, computed once. A new thing the companion can do is a `CompanionAction` in the `Actions/` family it belongs to, with a score, a forecast and a position request; a new family (opportunistic tasks, missions) is a new folder beside the four and one line in the chooser. The tool that action drives is a class under `Work/` in its own subfolder. A new reason to prefer one spot over another is a factor in the positioner. A new kind of move (a grapple, a mount) is an edge kind in the navigation grid. A number you want to tune is in `Weights.cs` and nowhere else.

## Traps

- **An action that computes a world fact itself is wrong even if it works**, because the next action computes it differently; put it on a sense.
- **The horizon is float.MaxValue with no threats.** Any arithmetic on it must handle that, or a forecast compared against it overflows into nonsense.
- **Scores are 0..1 and considerations multiply.** A consideration returning 0 vetoes; use `Consideration.AtLeast` when an action should stay eligible.
- **An action that holds a tool on the walk hides the torch.** Hold the tool only in position; the hand must be empty on the way.
- **`Senses` and `Reflexes` are both a namespace and a class.** From outside `DecisionMatrix` write `DecisionMatrix.Senses.Senses`; inside it the short form resolves.
