# Brain — how the companion behaves

This file is the whole account of how the companion decides and acts. It is written so that someone who has never opened a source file can predict what the companion will do in a given situation and, when it does something else, know which part to open. It carries the shape of every rule and none of the tunables that fill the shapes in: a weight, a threshold, a budget or a distance lives in exactly one place in the code (`DecisionMatrix/Decision/Weights.cs` for most of them, the owning class for the rest) and is read there, because a number copied here is wrong the first time someone tunes it. Every subfolder has its own `CLAUDE.md` with the file map, its own mechanism at its own depth, and the traps that bit there; this file is how the parts fit and hand off.

Nothing here draws or touches health; that is `../Companion/`. The weapons are equipment and live in `../Combat/`. Nothing in the brain has been watched running beyond one short surface run; every claim below is what the code does, and "verified in play" is stated where it is true and nowhere else.

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
│  ├─ 4c  Reflexes.TryTake     if a threat will hit the standing body inside the lookahead and a simulated jump or step clears it, take the body for a few ticks and STOP the tick here
│  ├─ 4d  Chooser.Choose       every action scores itself; the best (with commitment and the horizon charge) becomes Current
│  ├─ 4e  action.Execute       the winner acts (fires, swings, sets the held item) and returns a PositionRequest
│  ├─ 4f  Positioner.Resolve   the request becomes a feet position, or null for Hold
│  └─ 4g  Navigator.MoveTo     plan or follow a path to it through the motor; or stop and clear the path
├─ 5  Motor.ApplySteps          the game's StepUp/StepDown, so one-tile kerbs are walked not jumped
├─ 6  CollectTouchedItems       anything the body overlaps goes to the player's stacks or the bag
├─ 7  Torch.Update              decide lit/out from the light sense; if lit AND the hand is free, emit light and reveal the map
└─ 8  if the torch is shown, the hand holds it
```

Then the game applies gravity and tile collision to the velocity the motor set. The body is drawn afterwards from the held item and the animation, so what you see in its hand is whatever step 4e or step 8 left there.

**What the reflex stop means:** on a tick a reflex takes the body, nothing is scored, no action runs, the position request is stale and the navigator is not called. The hold lasts a few ticks, then a refractory period during which the reflex will not fire again, so an enemy that stays adjacent cannot hold the companion in an endless dodge while the chooser never gets to shoot.

## Senses: what the companion knows, and the numbers it derives

The world model is rebuilt every tick and read by everything else. Senses do not have weights; they produce facts and a few derived numbers, and the *actions* weight those numbers in their scores. Five senses:

**PlayerSense.** Position, bottom, velocity, health fraction, dead, attacking (item animation running with a damaging item). Travel *intent*: the horizontal velocity smoothed toward its current value while moving and decaying while still, so it converges over a few seconds of travel and fades over a couple of seconds of standing; `IsTravelling` is intent above a small floor, and `Predict(ticks)` is bottom plus intent times ticks, where the player will be if they keep going, which is what the walk-with action aims at. Also whether the player really hit a tree or an ore in the last moment, from the tile damage watcher, and whether the companion has a sight line to the player.

**ThreatSense.** One record per active hostile that is not friendly, alive, deals damage, is not a critter and can be chased. For each: its movement class (phaser if it ignores tiles, flyer if it ignores gravity, else walker); whether it is *reachable* (walkers by the same A* the companion uses from the enemy's feet to the player's, flyers by a flood fill through connected air, phasers always; both searches are bounded and a search that runs out of budget answers *reachable*, because a threat wrongly ignored costs more than one wrongly feared; refreshed on a stagger so only a fraction of enemies refresh on any tick); its observed speed (the peak of its speed so far, decaying slowly, floored, so a dashing enemy is underestimated until its first dash is seen); whether it shoots (a fixed list of shooters, or any enemy that had a hostile projectile appear beside it recently; Demon Eyes are deliberately not shooters, their AI never spawns a projectile); its distance to player and companion; whether it has a sight line to the player; and `TicksToPlayer`, distance to player divided by observed speed.

From those, two numbers the whole brain leans on:

```
Urgency(threat)  = weight × closeness × sight                        (0 if unreachable)
   weight        = 1 for a boss, else its damage as a share of the player's max life, clamped to a floor and 1
   closeness     = 1 at contact, falling linearly to 0 a few seconds out;
                   raised to near 1 for a shooter with a sight line (it is effectively already there)
   sight         = 1 with a sight line to the player, else a fraction

PlayerDanger     = max Urgency over threats                          "PlayerIsSafe" is danger under a small level
Horizon          = min over reachable threats of max(0, arrives − companionReturnTicks)
   arrives       = 0 for a shooter with sight, else TicksToPlayer
   return        = companion-to-player distance ÷ walk speed
                 = float.MaxValue with no reachable threat
```

The three factors multiply on purpose: a zero on any one is fatal and no other can buy it back, so an unreachable threat is nothing, a distant one is almost nothing, and only close, reachable and in sight makes a full urgency. The horizon is the mechanism behind every "go do something, but come back in time" behaviour: it is how many ticks the companion may stay away before the player is at risk. Any action that forecasts a job longer than that is charged for the overrun (next section).

**LootSense.** Every ground item within a reach that can be grabbed, sorted nearest first, each with a value: coins at the top, everything else a floor rising with sell value. Junk is still collected; value only ranks.

**LightSense.** Three brightness readings, refreshed every few ticks: at the player's tile, at the companion's tile, and *ambient*, the mean over a coarse grid of a screen-sized window centred on the companion with a disc around the companion cut out that is wider than a torch's glow. Ambient is the one the torch reads, because it is the only one the companion's own torch cannot raise. The window follows the companion and not the camera, so a companion sent into a cave while the player stands in daylight reads the cave. Off screen the lighting engine holds nothing and every sample reads 0, so a far companion lights its torch wherever it is.

**TileDamageWatcher.** Not polled: the game's own `KillTile` hook fires for every axe or pickaxe hit including the ones that only crack the tile, and this records the last tree (its trunk bottom) and the last ore (tile and type) the *player* hit, with a tick stamp; the companion's own tools raise a flag around their hits so they are excluded. This is what makes "the player is really chopping" true only when an axe is really hitting a tree, and never when an axe-sword is swung at a boss.

## Decision: how an action wins

Every tick every action returns a score in 0..1, built as a product of *considerations*, named curves from a sensed value to 0..1 (`Inverse` falls from 1 to 0 over a range, `Rising` climbs, `Band` is 1 inside a range and falls off outside, `Step` is a switch, `AtLeast` is a floor). Because they multiply, any single zero vetoes the action and no other term can buy it back; an action that should stay eligible uses a floor.

The chooser then applies two adjustments and takes the highest:

```
final = raw
      × a commitment bonus if this action is the one currently running   (no flicker between near-equal scores)
      × a charge that falls linearly to 0 as forecast overruns horizon     (only when forecast > horizon)
```

`forecast` is the action's own estimate of how many ticks it keeps the companion away from the player (0 for anything that stays with them). So a far loot run is fine with no threats (horizon infinite), gets discounted when a slime is a few seconds out, and is impossible when a shooter has a line on the player (horizon 0). An all-zero board falls to the last action, wander, which holds still when the player is dead.

The actions and what each one values. `safe` below is `1 − PlayerDanger` with a floor, so danger discounts a job without ever vetoing it outright.

| action | scores on | forecast | asks the positioner for | in hand |
|---|---|---|---|---|
| **guard** | danger, raised by distance from the player; zero when the player is dead | 0 | Guard near the player, target = the most urgent threat | the chosen weapon (it fires) |
| **kite** | how close the nearest reachable threat is to the companion: full at contact, gone a short way out | 0 | Retreat, target = most urgent | the chosen weapon (it fires) |
| **hunt** | `safe` × nearness of the target (floored so a far target still scores) × a boss bonus; the target is the threat best on a blend of urgency and nearness to the companion, among threats that are reachable or on screen with a solvable shot | the walk to firing range plus a fight allowance | LineOfFire at the target | the chosen weapon |
| **loot** | nearness of the item (floored) × item value × `safe`, for the nearest pickup that fits somewhere and has a standable tile beside it | the walk, padded for the pickup | Exact at the item | empty |
| **chop** | a fixed working score × `safe`, while the player has hit a tree just now or the job is younger than its memory; target = the nearest other standing tree in range with a standing spot | the walk plus a chopping allowance | Exact at the standing spot, then Hold while swinging | empty on the walk, the player's axe in position |
| **mine** | the same fixed working score × `safe`, while the player hit an ore within the job's memory; target = the nearest same-type ore outside the player's vein, else any ore, with a standing spot in reach the walker can get to | the walk plus a mining allowance | Exact at the standing spot, then Hold while swinging | empty on the walk, the player's pickaxe in position |
| **walk-with** | travelling player: the gap to the player's predicted position (floored, so it keeps a pull); standing player: zero inside the calm band, rising past it; either way full beyond the hard leash | 0 | WithPlayer, anchored at the predicted position (or the player when still) | empty |
| **wander** | a flat trickle; zero when the player is dead | 0 | Hold while standing, Exact at a random spot inside the calm band while strolling; a rare hop | empty |

Three consequences to read off that table. Guard beats everything as danger approaches 1, which is what pulls the companion off a hunt or a loot run when a shooter gets a line on the player. The working score of chop and mine is set above walk-with's ceiling while the player travels and above loot unless the loot is close and valuable, so a job continues while the player keeps working and stops once the job's memory of the player's last hit runs out; the mine memory is much longer than the chop memory, because a vein takes longer to clear than a tree takes to fell. Wander only wins when everything else is zero, which by design is "the player is standing still, nothing is around, the companion is inside the calm band".

## Positioning: where it stands once it knows what it is doing

A position request has a kind, an anchor and an optional target. `Hold` means stand still; `Exact` means the nearest standable tile close to the point, no scoring. The other four are *scored*: every standable tile in a box around the anchor, sampled at a stride, gets a product of factors, and the best wins. Rescored on a cadence or when the request kind or target changes, so the companion does not twitch between two equal spots.

The factors, all 0..1:

- **band**: a `Band` on the distance to the anchor, with a narrow near-and-close band when the player is in danger and a wide calm band when safe, times a mild preference for the nearer end of the band.
- **sight**: full if the spot's eye can see the player, a fraction otherwise.
- **fire**: full if the aimer solves a shot at the target from the spot with the chosen weapon, a small fraction otherwise (only for requests with a target, and only computed for the handful of best candidates by the cheap factors, because an aimer solve is a sweep of arcs simulated tick by tick).
- **danger**: full if any reachable threat's predicted hitbox passes through the body at the spot inside the lookahead, else a fraction falling with the nearest threat's distance.
- **open**: how few solid tiles surround eye height, penalising crevices.
- **travel**: a penalty for a spot behind a travelling player.
- **standoff**: a `Band` on the distance to the target, so a firing spot is neither on top of the enemy nor out of range.

```
WithPlayer   band × sight × (1 − k·danger) × open × travel
Guard        a tight band on the player × sight × fire × (1 − k·danger) × open
LineOfFire   fire × max(band, floor) × (1 − k·danger) × open × standoff
Retreat      (1 − danger) × max(band, floor) × fire × open
```

Each kind's `k` is its own tolerance for danger: guarding accepts more than following. Only ground tiles are candidates. A jump apex as a firing spot (the "jump to shoot over the hill" idea) is not sampled yet.

## Navigation: how it gets there, and why the ledge climb is possible

The navigator receives a feet position each tick. Within a short arrival slack it is "arrived". Otherwise it plans when the goal tile changes, on a cadence, when the path is finished, or after a stretch without moving; a plan that failed is not retried for a while unless the goal moves, because a full failed search is the expensive case. With no path it walks straight at the target and jumps only at a wall.

The grid is not the tile map; it is the set of *feet tiles a one-wide, three-tall body can stand on*: support beneath (solid or a platform), a clear body column above, no lava anywhere in the column. Edges are generated on the fly from what the body can do, and this is what answers the ledge question:

```
from a feet tile, the neighbours are
├─ walk      the tile beside it, if standable                                            cheapest
├─ step      the tile beside and one up, if standable and the column above is clear      a little more
├─ drop      off an edge, straight down to the first standable tile within the fall limit  cost grows with the fall
└─ jump      any standable tile inside the jump envelope (so many up, so many across), given
             headroom above the start, a clear body column at the apex above the start and
             a clear row to the landing; also a same-row gap the jump can clear            cost grows with distance and rise
```

A* (the textbook best-first search, with a heuristic that weights horizontal distance more than vertical) runs over those edges with a bounded budget. So the three-ledge climb to a high ore in the sketch is found in the ordinary way: each ledge is a node, each hop between them is a jump edge, and the search chains them because a path is just a sequence of edges. What A* "as you know it" could not do is the part the edge generator does: deciding that a jump from here lands there. The limits are the edge generator's, not the search's: the jump envelope is derived from the jump velocity and gravity rather than measured; the arc check is coarse (apex column above the start plus the landing row, so a low ceiling mid-arc is missed and shows up as a stuck counter and a replan); and a jump that needs a run-up is not modelled, the follower jumps from standing.

Following the path: advance past every step whose feet point is within a small slack; for a walk step, move toward it and jump only for a real wall (a horizontal collision after the game's step-up has already handled one-tile kerbs) or a rise of two or more tiles, at the jump height the rise needs (the fighter AI's own table, scaled from the full jump); for a jump step, jump from the ground at that height and steer in the air; for a drop, walk off at reduced speed and let gravity work. The motor lerps horizontal velocity toward the walk speed.

No digging, no building, by ruling: the grid never plans through a tile.

## Reflexes: the dodge that skips scoring

Before any scoring, for each reachable, moving threat: if its predicted hitbox (straight-line for flyers and phasers, under NPC gravity for walkers) meets the standing body at any sampled tick inside the lookahead, simulate both dodges against the same prediction. The jump: the body offset by the real jump arc, from the motor's own jump velocity and the game's gravity. The step-back: the body offset by the motor's own lerped acceleration away from the threat, with a room check that way. Take the jump if it never intersects, else the step if it never intersects and there is room; if neither clears it, take the hit and rest briefly rather than moving into the enemy. Hostile projectiles are not yet considered; that is a second loop to add.

## Aiming: how a shot is solved, and what it cannot solve

Every ranged weapon hands the aimer a *flight profile*: launch speed, how many ticks the projectile flies straight before gravity, the gravity added per tick after that, a terminal fall speed, a maximum flight time and a hitbox size; the arrow's profile is the arrow AI style's own numbers with the speed being bow plus arrow, and the knife drops from launch. The aimer sweeps launch angles from the direct line outward in small steps, above the line first, alternating below and above to their respective limits, and for each angle simulates the flight tick by tick against solid tiles, advancing the target's hitbox along its velocity for the lead; the first arc that lands is the answer. So a clear shot is a straight shot, a lob is chosen only when the straight line fails, and a target behind a wall or under the floor gets no shot.

Two limits. The profile is per weapon class, not read off the projectile's AI: seven bows firing vanilla arrows are seven correct speeds on one correct arc, but a projectile with different physics (a rocket, a boomerang, a modded arrow with its own AI) needs its profile written by hand, and until it is, the aimer would lob it like an arrow. And a solve is expensive, so whether a target is engageable is cached per enemy for a short while, a failed solve rests the weapon briefly, and the positioner only solves for its best few candidates.

Weapon choice: each weapon rates its *suitability* for the target (the bow prefers range, a single target and a boss; the knife prefers close crowds that are not a boss) and the higher wins. Fire rate is handicapped and every shot is rotated by a little noise until the mastery tree lifts either. Every projectile is spawned with the player as owner and damage from the player's ranged stat, so kills, drops and on-hit accessories are the player's.

## Work: the tools, and the torch's place among them

A tool never decides; it swings when its action says so, with the game's own formula and the player's held item's numbers. The chopper applies the vanilla axe formula (power scaled, more on cactus) to its own crack table, the tile breaks at full damage, every non-lethal hit is a fail-hit for the sound and dust, and the cracks renderer draws the table. The miner does not copy anything: it runs the game's own `Player.PickTile` on the companion's drawing-only player, so the per-type multipliers, the minimum-power gates by tile and depth, a modded tile's power check, the crack table and the break are all the game's; if the player's pickaxe could not break a tile, neither can the companion's. The "can this pick damage that tile" question the ore finder asks before committing is answered by a delegate bound to the game's private damage formula, not a copy. Reach is the player's own reach as the game keeps it, wider sideways than up and down, so an accessory that extends the player's reach extends the companion's.

Finding ore is the expensive half, so it runs only when the player starts on a new ore or the companion's target is gone, on a cooldown, never per tick. The search floods the player's vein (bounded), scans a box around the companion for the nearest ore of that type outside the vein, then any ore, and for each candidate looks for a standing tile inside reach with a sight line from the eye that the walker can reach from where it is. A companion that arrives and finds the tile not swingable from there (the stand was approximate, or the world changed) never swings anyway; it drops that tile and takes the next of the patch. After a tile dies the next is the nearest patch tile still in reach of the current stand, and only when none is does it re-approach.

**The torch is the odd one out: it has no action, and it is not in the chooser.** It is what the hand does when nothing else wants it. Step 7 of the tick decides lit or out from the ambient light with hysteresis (lit below one level, out above a higher one, never switching within a minimum hold of the last switch); the light, the map reveal and the torch in the hand then all follow one answer, whether the hand is free this tick. Actions that want the hand: guard, kite and hunt (the weapon, but only while a shot is being taken at a live target), chop and mine (the tool, but only in position). So the answer to "does it hold the torch while walking to an ore in the dark" is yes: the mine action leaves the hand empty on the walk and takes the pickaxe out when it arrives, and the torch fills the gap both before and after the swing. On a mine job the hand goes torch → pickaxe → torch as it arrives, mines and leaves, and the light and the reveal go with it; while the pickaxe is out there is no glow, because the torch is not out. A shown torch reveals the map on a cadence: a flood through air tiles to the torch's reach marks the air and the first solid face, never what is behind a wall, on or off screen alike, and each changed map tile is queued the way the game queues its own changed tiles so only those are redrawn.

## What is verified and what is not

Verified in play (the first two runs): the body draws and swings, chopping the right tree at the trunk, arrows fly, the health bar, persistence, and that the brain runs (first tick logged, action = wander). Everything else in this file describes code that compiles and has not been watched: the horizon charge, kiting, dodging after the simulation rewrite, the navigator on anything but flat ground, mining, the torch thresholds against real cave light, the map reveal and the map head. The overlay (key left of 1) shows every score, the danger and horizon, the light readings and whether the torch is shown, the chosen spot and the path, and is how a wrong choice is read rather than guessed; telemetry to a file with a "this looked wrong" key is the next instrument (AIC-51).

## Where a new thing goes

A new fact about the world is a field on a sense, computed once. A new thing the companion can do is a `CompanionAction` in the `Actions/` family it belongs to, with a score, a forecast and a position request; a new family (opportunistic tasks, missions) is a new folder beside the four and one line in the chooser. The tool that action drives is a class under `Work/` in its own subfolder. A new reason to prefer one spot over another is a factor in the positioner. A new kind of move (a grapple, a mount) is an edge kind in the navigation grid. A number you want to tune is in `Weights.cs` and nowhere else.

## Traps

- **An action that computes a world fact itself is wrong even if it works**, because the next action computes it differently; put it on a sense.
- **The horizon is float.MaxValue with no threats.** Any arithmetic on it must handle that, or a forecast compared against it overflows into nonsense.
- **Scores are 0..1 and considerations multiply.** A consideration returning 0 vetoes; use `Consideration.AtLeast` when an action should stay eligible.
- **An action that holds a tool on the walk hides the torch.** Hold the tool only in position; the hand must be empty on the way.
- **An action's `Score` that searches the world is called every tick for every action, winner or not.** Put a search behind a trigger and a cooldown, as the mine action does, or the losing action pays it while something else runs.
- **`Senses` and `Reflexes` are both a namespace and a class.** From outside `DecisionMatrix` write `DecisionMatrix.Senses.Senses`; inside it the short form resolves.
