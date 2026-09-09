# Brain — how the companion behaves

This file is the whole account of how the companion decides and acts. It is written so that someone who has never opened a source file can predict what the companion will do in a given situation and, when it does something else, know which part to open. It carries the shape of every rule and none of the tunables that fill the shapes in: a weight, a threshold, a budget or a distance lives in exactly one place in the code (`DecisionMatrix/Decision/Weights.cs` for most of them, the owning class for the rest) and is read there, because a number copied here is wrong the first time someone tunes it. Every subfolder has its own `CLAUDE.md` with the file map, its own mechanism at its own depth, and the traps that bit there; this file is how the parts fit and hand off.

Nothing here draws or touches health; that is `../Companion/`. The weapons are equipment and live in `../Combat/`. Nothing in the brain has been watched running beyond one short surface run; every claim below is what the code does, and "verified in play" is stated where it is true and nowhere else.

```
Brain/
├─ CLAUDE.md
├─ Brain.cs            the tick order below; holds one of each decision-matrix part, the last position request, and the wall-clock each phase of the last tick cost, which the telemetry and the overlay read
├─ DecisionMatrix/     how a choice is made, independent of what the choices are
│  ├─ Senses/          the world model and the derived numbers (danger, horizon, light); never decides
│  ├─ Decision/        the chooser, the considerations, the weights, position requests
│  ├─ Positioning/     where to stand, as a score over candidate tiles
│  ├─ Navigation/      how to get there: World/ (tile interface), Body/ (the body's arithmetic), Planning/ (grid, A*, path), Following/ (the navigator)
│  └─ Reflexes/        the fast path that skips scoring: simulated dodges
├─ Actions/            what can be chosen, by family; each scores itself and asks for a spot
│  ├─ Survival/        survive: the body's own rescue
│  ├─ Companionship/   walk-with, guard, wander
│  ├─ Combat/          hunt, kite
│  ├─ Gathering/       loot
│  └─ Work/            chop, mine
├─ Work/               the tools an action drives once chosen, and the two that run outside the chooser
│  ├─ Chopping/        trees and the axe
│  ├─ Mining/          ores and the pickaxe
│  ├─ Torch/           the torch in the dark
│  └─ Doors/           the door in the way: opened on the walk, shut behind
├─ Aiming/             the arc solver every ranged weapon and the positioner share
└─ Debug/              the overlay that shows all of the above (left square bracket), and the per-tick telemetry file
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
│  ├─ 4g  Navigator.MoveTo     plan or follow a path to it through the motor; or stop and clear the path
│  └─ 4h  Engage               the hands: pick the best target and shoot at it, whatever the feet were told, unless a tool is in the arm
├─ 5  Motor.ApplySteps          the game's StepUp/StepDown as a player holding up runs them, so one-tile kerbs and platform treads are walked not jumped — with the platform lift refused while the move in hand is a descent, or the body climbs what it is falling through
├─ 5b Doors.Tick               a closed door ahead swings the way the body is going, or inward when that side is blocked, and shuts again once the body is two tiles past
├─ 6  CollectTouchedItems       anything the body overlaps goes to the player's stacks or the bag
├─ 7  Torch.Update              decide lit/out from the light sense; if lit AND the hand is free, emit light and reveal the map
└─ 8  if the torch is shown, the hand holds it
```

Then the game applies gravity and tile collision to the velocity the motor set. The body is drawn afterwards from the held item and the animation, so what you see in its hand is whatever step 4e or step 8 left there.

**What the reflex stop means:** on a tick a reflex takes the body, nothing is scored, no action runs, the position request is stale and the navigator is not called. The hold lasts a few ticks, then a refractory period during which the reflex will not fire again, so an enemy that stays adjacent cannot hold the companion in an endless dodge while the chooser never gets to shoot.

## Senses: what the companion knows, and the numbers it derives

The world model is rebuilt every tick and read by everything else. Senses do not have weights; they produce facts and a few derived numbers, and the *actions* weight those numbers in their scores. Six senses:

**PlayerSense.** Position, bottom, velocity, health fraction, dead, attacking (item animation running with a damaging item). Travel *intent*: the horizontal velocity smoothed toward its current value while moving and decaying while still, so it converges over a few seconds of travel and fades over a couple of seconds of standing; `IsTravelling` is intent above a small floor, and `Predict(ticks)` is bottom plus intent times ticks, where the player will be if they keep going, which is what the walk-with action aims at. Also whether the player really hit a tree or an ore in the last moment, from the tile damage watcher, and whether the companion has a sight line to the player.

**ThreatSense.** One record per active hostile that is not friendly, alive, deals damage, is not a critter and can be chased. For each: its movement class (phaser if it ignores tiles, flyer if it ignores gravity, else walker); whether it is *reachable* (walkers by the same A* the companion uses from the enemy's feet to the player's, flyers by a flood fill through connected air, phasers always; both searches are bounded and a search that runs out of budget answers *reachable*, because a threat wrongly ignored costs more than one wrongly feared; refreshed on a stagger so only a fraction of enemies refresh on any tick); its observed speed (the peak of its speed so far, decaying slowly, floored, so a dashing enemy is underestimated until its first dash is seen); whether it shoots (a fixed list of shooters, or any enemy that had a hostile projectile appear beside it recently; Demon Eyes are deliberately not shooters, their AI never spawns a projectile); its distance to player and companion; whether it has a sight line to the player; and `TicksToPlayer`, distance to player divided by observed speed.

From those, two numbers the whole brain leans on:

```
Urgency(threat)  = weight × closeness × sight                        (0 if unreachable)
   weight        = 1 for a boss, else its damage as a share of the player's max life, clamped to a floor and 1
   closeness     = 1 at contact, falling linearly to 0 a few seconds out;
                   raised to near 1 for a shooter with a sight line (it is effectively already there)
   sight         = 1 with a sight line to the player, else a fraction

UrgencyToCompanion = the same three factors measured against the companion's own body:
                     its max life, its own distance divided by the threat's speed, and a
                     sight line from the threat to the companion rather than to the player

PlayerDanger     = 1 − Π(1 − Urgency_i)                              over every threat
CompanionDanger  = 1 − Π(1 − UrgencyToCompanion_i)                   "CompanionInTrouble" past a middling level
Horizon          = min over reachable threats of max(0, arrives − companionReturnTicks)
   arrives       = 0 for a shooter with sight, else TicksToPlayer
   return        = companion-to-player distance ÷ walk speed
                 = float.MaxValue with no reachable threat
```

The three factors multiply on purpose: a zero on any one is fatal and no other can buy it back, so an unreachable threat is nothing, a distant one is almost nothing, and only close, reachable and in sight makes a full urgency. The horizon is the mechanism behind every "go do something, but come back in time" behaviour: it is how many ticks the companion may stay away before the player is at risk. Any action that forecasts a job longer than that is charged for the overrun (next section).

**The two danger numbers are aggregated as independent hazards, not as a maximum.** Each threat's urgency is read as its own chance of landing something, so what survives all of them is the product of the misses and the danger is one minus that: the worst threat still dominates, a second one adds with diminishing weight, and the result cannot leave 0..1 however many bodies are in the room. A maximum said that eleven zombies were exactly as dangerous as one, which is the shape that let a fight look survivable while the life bar emptied.

**That there are two of them at all is the correction of one defect, and it is worth stating as a rule rather than a fix.** Every danger term in the brain used to read the danger to the *player*, so a companion far from him lived in a world containing no danger: the hunt score is a product with `1 − PlayerDanger` in it, straying made that factor rise toward one, hunting scored higher for being further away, and the loop ended with a corpse. On 2026-09-09 it lost sixty-five life across five hits in three hundred ticks with the danger column reading 0.00 at every one of them, eighty-four tiles away. A sense measured for one body and consumed as though it were a fact about the world is the general form of that mistake, so the rule is that any question of the shape "is it safe" names whose safety it means, and the companion's own is `CompanionDanger`.

**LootSense.** Every ground item within a reach that can be grabbed, sorted nearest first, each with a value: coins at the top, everything else a floor rising with sell value. Junk is still collected; value only ranks.

**LightSense.** Three brightness readings, refreshed every few ticks: at the player's tile, at the companion's tile, and *ambient*, the mean over a coarse grid of a screen-sized window centred on the companion with a disc around the companion cut out that is wider than a torch's glow. Ambient is the one the torch reads, because it is the only one the companion's own torch cannot raise. The window follows the companion and not the camera, so a companion sent into a cave while the player stands in daylight reads the cave; it is clipped to the screen, because the lighting engine reads 0 outside it and a companion at the screen edge would otherwise count the outside as black. Fully off screen no sample is left, ambient reads 0, and a far companion lights its torch wherever it is.

**CompanionSense.** The companion's own body, kept apart from the threat sense so "the player is safe" never hides "I am drowning": breath and whether the head is under water (the breath itself is body physics on the NPC, the player's own rule: a countdown while the head is under, one unit of breath per completed countdown, damage per countdown at zero, quick recovery in air), lava and fire, life, and damage over the last second. From those, `SelfDanger`, the largest of drowning (nothing until breath is half gone, total near none), burning (total in lava, partial on fire) and bleeding (recent loss scaled by how little life is left).

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

The actions and what each one values. `safe` below is `1 − PlayerDanger` with a floor, so danger discounts a job without ever vetoing it outright. **No action fires a weapon; every one of them is a decision about where the feet go**, and the "in hand" column says only which item the body is holding, because the shooting is step 4h and runs whichever action won.

| action | scores on | forecast | asks the positioner for | in hand |
|---|---|---|---|---|
| **survive** | the companion's own danger alone, rising from nothing to above every other action's ceiling as breath or life runs out; zero while the body is fine | 0 | Exact at the nearest reachable standable tile whose head row is dry and whose column holds no lava | empty |
| **guard** | danger, raised by distance from the player; zero when the player is dead | 0 | Guard near the player, target = the most urgent threat | the chosen weapon |
| **kite** | how close the nearest reachable threat is to the companion: full at contact, gone a short way out | 0 | Retreat, target = most urgent | the chosen weapon |
| **hunt** | `safe` × reach × a boss bonus × a leash on the distance to the player × the companion's own skin, where reach is full for any target whose hitbox meets the screen and falls with distance (floored, so a far target still scores) only beyond it, because distance is already charged as the trip in the forecast and charging it twice let chopping beat a zombie walking across the screen; the target is the threat best on a blend of urgency and nearness to the companion, among threats that are reachable or on screen with a solvable shot | the walk to firing range plus a fight allowance | LineOfFire at the target | the chosen weapon |
| **loot** | nearness of the item (floored) × item value × `safe`, for the nearest pickup that fits somewhere and has a standable tile beside it | the walk, padded for the pickup | Exact at the item | empty |
| **chop** | a fixed working score × `safe`, while the player has hit a tree just now or the job is younger than its memory; target = the nearest other standing tree in range with a standing spot | the walk plus a chopping allowance | Exact at the standing spot, then Hold while swinging | empty on the walk, the player's axe in position |
| **mine** | the same fixed working score × `safe`, while the player hit an ore within the job's memory; target = the nearest same-type ore outside the player's vein, else any ore, with a standing spot in reach the walker can get to | the walk plus a mining allowance | Exact at the standing spot, then Hold while swinging | empty on the walk, the player's pickaxe in position |
| **walk-with** | travelling player: the gap to the player's predicted position (floored, so it keeps a pull); standing player: zero inside the calm band, rising past it; either way full beyond the hard leash | 0 | WithPlayer, anchored at the predicted position (or the player when still) | empty |
| **wander** | a flat trickle; zero when the player is dead | 0 | Hold while standing, Exact at a random spot inside the calm band while strolling; a rare hop | empty |

Five consequences to read off that table. Survive is the only action allowed above the top of the scale, and only near the end, so a drowning or burning companion leaves even a boss fight; it does not keep the companion out of water, because crossing a pool is priced in the navigator and this is the backstop for a crossing that runs longer than the breath. Guard beats everything else as danger approaches 1, which is what pulls the companion off a hunt or a loot run when a shooter gets a line on the player. The working score of chop and mine is set above walk-with's ceiling while the player travels and above loot unless the loot is close and valuable, so a job continues while the player keeps working and stops once the job's memory of the player's last hit runs out; the mine memory is much longer than the chop memory, because a vein takes longer to clear than a tree takes to fell. Wander only wins when everything else is zero, which by design is "the player is standing still, nothing is around, the companion is inside the calm band", with one exception: while the brain says the body is stranded in a sealed pocket, walk-with is discounted and wander scores just under the combat ceiling, so the body walks the pocket instead of pressing the wall (`Actions/Companionship/`). And hunting carries two factors nothing else does, both of which are the same argument: a leash that falls to zero as the companion's distance from the player passes what a screen can hold, and its own skin, `1 − CompanionDanger` with a floor. Hunting is the opportunistic thing a player does when little else is going on, never a mode they enter, so the score has to shrink with the two things that make wandering off wrong — being far from him, and being in trouble itself.

**Then a separate step runs the hands, after the feet have been told where to go.** Firing used to live inside guard, kite and hunt, and the consequence was structural rather than a matter of tuning: a companion that was following, looting, wandering or working was *incapable* of shooting, and no score anywhere could have fixed it, because the code that pulls a trigger was only reachable through three of nine actions. It now runs every tick from `Brain.Engage`, on its own target chosen by expected damage weighted by urgency, and the one thing that stops it is a tool in the arm the throw needs: chop and mine. That is why the hands run *after* the navigator — the motor already has its orders, so facing the target wins the tick and the body aims where it shoots while walking somewhere else, which is what a player looks like. Hunting is now only the decision to walk toward something, never the decision to fight.

## Positioning: where it stands once it knows what it is doing

A position request has a kind, an anchor and an optional target. `Hold` means stand still; `Exact` means the nearest standable tile close to the point that the walker can reach, no scoring. The other four are *scored*: every standable tile in a box around the anchor, sampled at a stride, gets a product of factors, and the best wins. Rescored on a cadence or when the request kind or target changes, so the companion does not twitch between two equal spots. Before any scoring, reachability is a tier and not a factor: on the same cadence the positioner floods the walker's edges outward from the companion's feet to a budget, refusing every edge with no way back so the region means "everywhere I can go and come home from", and while any candidate lies inside that region only those are scored, because a spot the body cannot get to is not a worse spot but no spot; when none does (the flood ran out before it got there, or every candidate is somewhere the body could not leave), every candidate stays and the partial path walks the companion as close as it can. This is what stops a walk-with anchor beside the player resolving to a standable tile inside a sealed cavity, which parked the companion above one on the fourth run of 2026-09-08.

**This tier is where the companion decides whether to enter somewhere it cannot get out of, and it decides generically.** A hunt scores the returnable firing spots on a pit's rim, so the companion shoots down into the pit rather than jumping in, and where no rim spot solves a shot it does not engage — that is the request-kind gate above, which never offers a hunt a one-way drop, and not the tier, because the tier is settled before the aimer runs and a failed solve scores a fraction rather than a zero. The player is the one exception and it is a rule, not a case: when the returnable region does not hold him but the region without the refusal does, the flood runs again and that second region is what gets scored, because being stuck is having no way to the player rather than no way back, so a pocket he is standing in is not somewhere the companion strands itself. A player no region reaches at all is the other reading of the same test and gets no exception, since a body walled off from him is already stranded and opening the tier would only let it fall further in. That exception is load-bearing rather than tidy — a rim tile within the sample box is returnable however deep the shaft below it, so without it the tier stays closed and the companion watches him from above. Nothing in the positioner or the grid names a pit or an enemy, by Caner's ruling of 2026-09-08 that those cases are outcomes a generic system should produce and never rules to write, because a system made of scenario branches gets stuck the first time it meets a scenario nobody wrote.

The factors, all 0..1:

- **band**: a `Band` on the distance to the anchor, with a narrow near-and-close band when the player is in danger and a wide calm band when safe, times a mild preference for the nearer end of the band.
- **sight**: full if the spot's eye can see the player, a fraction otherwise.
- **fire**: full if the aimer solves a shot at the target from the spot with the chosen weapon, a small fraction otherwise (only for requests with a target, and only computed for the handful of best candidates by the cheap factors, because an aimer solve is a sweep of arcs simulated tick by tick).
- **danger**: full if any reachable threat's predicted hitbox passes through the body at the spot inside the lookahead, else a fraction falling with the nearest threat's distance.
- **open**: how few solid tiles surround eye height, penalising crevices.
- **travel**: a penalty for a spot behind a travelling player.
- **standoff**: a `Band` on the distance to the target, so a firing spot is neither on top of the enemy nor out of range, clamped to the held weapon's own reach and leaning outward across the band. Every distance still on the table is one the line-of-fire factor already solved a shot at, so the further of two is strictly better for a body that would rather not be reached.
- **clear way**: a spot whose straight line from where the companion stands passes within a body's width of a reachable enemy is discounted hard, because a firing spot on the far side of a zombie is reached by walking into the zombie.

```
WithPlayer   band × sight × (1 − k·danger) × open × travel × clear way
Guard        a wide band on the player × sight × fire × (1 − k·danger) × open × standoff × clear way
LineOfFire   fire × max(band, floor) × (1 − k·danger) × open × standoff
Retreat      (1 − danger) × max(band, floor) × fire × open
```

Each kind's `k` is its own tolerance for danger: guarding accepts more than following. Only ground tiles the flood reached are candidates. A jump apex as a firing spot (the "jump to shoot over the hill" idea) is not sampled yet.

## Navigation: how it gets there, and why the ledge climb is possible

The navigator receives a feet position each tick. Within a short arrival slack it is "arrived". Otherwise it plans when the goal tile changes, on a cadence (held while the current step is part way through a move a fresh plan would undo, a jump's back-off to its runway mark and the run-in from it, because a plan made from the mark offered the mirror jump from the same take-off and the body circled between the two with no fault), when the path is finished, or after a stretch without moving; a plan that failed is not retried for a while unless the goal moves, because a full failed search is the expensive case. With no path it walks straight at the target and jumps only at a wall. After the navigator runs, the brain counts stranded ticks: a plan toward the player that found nothing while the positioner's flood closed under its budget starts the count, any such plan that finds something clears it, and the count is what turns into the stranded flag the next tick's actions read.

The grid is not the tile map; it is the set of *feet tiles a three-tall body can stand on*: support beneath (solid or a platform), a clear body column above, and at least one open neighbouring column, because the body is wider than a tile and a column walled on both sides is a slot it cannot enter. Reachable enemies are priced on the route like a wall of lava, so a path to a spot beyond one goes round it where a way round exists. Water and lava are priced, not banned, by Caner's ruling: a player crosses a pool and jumps a lava stream to escape two zombies, and so should this. Water is a node like any other, but a slow one: the game halves a wet NPC's movement, so every edge that starts in liquid costs more and the jump envelope from there is halved, which is what makes the search walk out of a pool along its floor instead of jumping in place under a ledge; a node where the head would be under water costs more again, so shallow water is free and deep water is taken only when the dry way is much longer. Lava tiles are nodes with a large flat price per lava tile in the column, offered only while the companion has life to pay with (the brain sets the switch from the self sense each tick), so a one-tile stream is crossed when the detour is long and refused when it is short, and a long lava walk is never chosen. What the grid does not yet do is budget breath along a path: a submerged route longer than the breath is planned like any other, and the survive action is the backstop when that happens. Edges are generated on the fly from what the body can do, and this is what answers the ledge question:

```
from a feet tile, the neighbours are
├─ walk      the tile beside it, on the same row or one up or one down: the body driven
│            from the node at the walk speed, lifted over a kerb or onto a platform at its
│            knee and stepped down a fall of up to a tile by the game's own rules, ends
│            standing there; a body that meets a shape or falls has no walk that way      cheapest, a little more up or down
├─ drop      off an edge: the body itself driven from the lip toward a line in the opening
│            (a few pixels off either wall, or its middle) and let fall, tick by tick, to the
│            first surface that holds it; each landing its own edge carrying the line       cost grows with the fall
├─ fall      through the platform underfoot the same way, the way a player presses down:
│            once on the line the body is asked to pass the platform, then falls            like a drop
└─ jump      any standable tile in the jump box (the full jump's height up, so many across,
             two down) that one of the jump profiles lands in when simulated tick by tick
             against the tiles' shapes with the follower's steering rule: every jump height
             whose apex clears the rise, lowest first, at the walk, half of it and from a
             stand; the edge carries the profile that landed                               cost is the flight time in walked tiles, plus one
```

A drop with no way back is a door that closes behind the body, and a plan takes one only while the companion is following or guarding the player or saving itself, because the player being down there is the one reason to go where there is no way back; a hunt that took one stood at the rim of a sealed pocket for the rest of run 5. Whether a drop has a way back is measured rather than assumed from its depth: for a fall deeper than the tallest climb any traversal can make, the search asks itself whether the landing can reach the take-off, bounded, answering "there is a way back" when the budget runs out, so it refuses only where it has proved a pocket closed. `DecisionMatrix/Navigation/CLAUDE.md` has the probe and why it is affordable.

A* (the textbook best-first search, with a heuristic that weights horizontal distance more than vertical) runs over those edges with a bounded budget, and when the budget or the reachable region runs out it returns the path to the closest node it reached, marked partial, because walking to the nearest reachable point beats standing where the goal went out of view; the follower walks a partial path and the record still counts the plan as failed. So the three-ledge climb to a high ore in the sketch is found in the ordinary way: each ledge is a node, each hop between them is a jump edge, and the search chains them because a path is just a sequence of edges. What A* "as you know it" could not do is the part the edge generator does: deciding that a jump from here lands there. The limits are the edge generator's, not the search's: the jump is simulated with the motor's own arithmetic, so a low ceiling mid-arc or a wall on the way refuses the edge. The speed the body carries into the jump is not part of a node, so the step carries the speed the jump was found with and the follower supplies it, which is the next paragraph.

Following the path: every step belongs to the traversal that proved it (one object per kind of move in `DecisionMatrix/Navigation/Traversals/`, which generates that kind's edges for the search and performs them for the follower with the same functions), and the navigator only advances past every step its traversal says is done, asks the current step's traversal whether it has faulted (stood past twice its proven ticks, come to rest on a tile it never promised, pressed a shape too long, or lost the body inside a shape), and takes the tick's controls from it. For a walk step, move toward it, coasting to rest on its point when the next step starts from rest (a descent, a standing jump), and jump only for a real wall or a rise of two or more tiles, at the jump height the rise needs (the fighter AI's own table, scaled from the full jump); for a jump step, make the jump the plan found, from its take-off tile at its height with its start speed, which means settling onto the take-off for a standing jump, and for a running jump arriving at or past the take-off too slowly coasting back onto a mark as far behind it as the runway needs and the floor allows (with the same steering rule as the landing, because a body that walks through the mark and then reverses stops two tiles later, which on a floor-capped runway is off the back of it), running in at the profile's speed and jumping as the take-off is crossed (a runway too short is run once and jumped from whatever speed it gave, so the body never backs away for ever; the run-up's state belongs to the jump edge, so a replan does not hand one jump's run-up to another), then steering in the air toward the landing column, coasting once inside the stopping distance, with the rule the planner simulated; for a drop, brake to rest if the body arrived faster, then steer with the in-air rule to the line the plan proved the descent along (a few pixels off a wall of the opening, or its middle, and never the tile centre, on which a body wider than a tile still overhangs the edge and stands) and let gravity work; for a fall-through, the same, and once on the line ask the body to pass its platform. A step that faults, or a body that stands still on a step long enough (the count survives the replan cadence, which used to reset it before it could ever reach its threshold, so a parked body never struck in seven runs), has found an edge the grid offers and the body cannot take: the step is priced so the next plan goes another way and a strike is counted, and at two strikes against one goal the brain has the positioner refuse the spot for a while, so the companion tries another route and then another place instead of standing. A running jump exists in the plan only where the floor behind its take-off is long enough to build its speed, with the same slack the performer accepts. A real wall is solid at the feet row *and* the row above, read from the tiles: the game's collision flag alone is not that test, because a one-tile kerb raises it on the tick it is met, before the motor's step-up lifts the body over it, and a follower that jumped on the flag hopped at every kerb. The motor moves the way the player does: speed builds by a small fixed amount per tick up to the walk speed and bleeds by a larger one when stopping or reversing, on the ground and in the air alike, so a reversal takes the ticks it takes a player and cannot happen inside one jump.

No digging, no building, by ruling: the grid never plans through a tile.

## Reflexes: the dodge that skips scoring

Before any scoring, for each reachable, moving threat: if its predicted hitbox (straight-line for flyers and phasers, under NPC gravity for walkers) meets the standing body at any sampled tick inside the lookahead, simulate both dodges against *every* reachable threat's prediction, not only the one that triggered. Both dodges steer away from the trigger for the whole hold, and both simulations carry the body sideways with the motor's own acceleration rule (the same function the motor runs, so the simulation and the real move agree), starting from the speed the body already has; the jump adds the real arc from the motor's jump velocity and the game's gravity. That drift is the point: a jump simulated as vertical while the body kept the run-up the hunt had built toward the enemy landed on the enemy. Take the jump if it never intersects, else the step if it never intersects and there is body room that way; if neither clears it, take the hit and rest briefly rather than moving into the enemy. Hostile projectiles are not yet considered; that is a second loop to add.

## Aiming: how a shot is solved, and what it cannot solve

Every ranged weapon hands the aimer a *flight profile*: launch speed, how many ticks the projectile flies straight before gravity, the gravity added per tick after that, a terminal fall speed, a maximum flight time and a hitbox size; the arrow's profile is the arrow AI style's own numbers with the speed being bow plus arrow, and the knife drops from launch. The aimer sweeps launch angles from the direct line outward in small steps, above the line first, alternating below and above to their respective limits, and for each angle simulates the flight tick by tick against solid tiles, advancing the target's hitbox along its velocity for the lead; the first arc that lands is the answer. So a clear shot is a straight shot, a lob is chosen only when the straight line fails, and a target behind a wall or under the floor gets no shot.

Two limits. The profile is per weapon class, not read off the projectile's AI: seven bows firing vanilla arrows are seven correct speeds on one correct arc, but a projectile with different physics (a rocket, a boomerang, a modded arrow with its own AI) needs its profile written by hand, and until it is, the aimer would lob it like an arrow. And a solve is expensive, so whether a target is engageable is cached per enemy for a short while, a failed solve rests the weapon briefly, and the positioner only solves for its best few candidates.

Weapon choice: each weapon states its numbers and holds no opinion about when to use it, and the arsenal picks by one quantity computed per weapon per target — the damage that weapon would actually land in the next three seconds, fired from where the companion is standing right now. That is deliberately not damage *output* per second, which is blind to what the output lands on: it is counted over a window so a slower weapon that clears a line can beat a faster one that kills singly, every hostile contributes only the life it still has so a shot worth three hundred against a slime worth twenty is worth twenty, and the bodies a shot hits are the ones its simulated arc actually crosses capped by the projectile's own penetration, so a wall or a bad angle removes them without anyone writing a rule about walls or angles. The earlier design had each weapon implement a `Suitability(situation)` returning a product of hand-set multipliers, which is a guess standing where an outcome belongs and is why it is gone. Fire rate is handicapped and every shot is rotated by a little noise until the mastery tree lifts either. Every projectile is spawned with the player as owner and damage from the player's ranged stat, so kills, drops and on-hit accessories are the player's; the companion's shot is a spawn from an NPC source, so it carries none of the player's crit chance and the score does not count any.

## Work: the tools, and the torch's place among them

A tool never decides; it swings when its action says so, with the game's own formula and the player's held item's numbers. The chopper applies the vanilla axe formula (power scaled, more on cactus) to its own crack table, the tile breaks at full damage, every non-lethal hit is a fail-hit for the sound and dust, and the cracks renderer draws the table. The miner does not copy anything: it runs the game's own `Player.PickTile` on the companion's drawing-only player, so the per-type multipliers, the minimum-power gates by tile and depth, a modded tile's power check, the crack table and the break are all the game's; if the player's pickaxe could not break a tile, neither can the companion's. The "can this pick damage that tile" question the ore finder asks before committing is answered by a delegate bound to the game's private damage formula, not a copy. Reach is the player's own reach as the game keeps it, wider sideways than up and down, so an accessory that extends the player's reach extends the companion's.

Finding ore is the expensive half, so it runs only when the player starts on a new ore or the companion's target is gone, on a cooldown, never per tick. The search floods the player's vein (bounded), scans a box around the companion for the nearest ore of that type outside the vein, then any ore, and for each candidate looks for a standing tile inside reach with a sight line from the eye that the walker can reach from where it is. A companion that arrives and finds the tile not swingable from there (the stand was approximate, or the world changed) never swings anyway; it drops that tile and takes the next of the patch. After a tile dies the next is the nearest patch tile still in reach of the current stand, and only when none is does it re-approach.

**The torch is the odd one out: it has no action, and it is not in the chooser.** It is what the hand does when nothing else wants it. Step 7 of the tick decides lit or out from the ambient light with hysteresis (lit below one level, out above a higher one, never switching within a minimum hold of the last switch); the light, the map reveal and the torch in the hand then all follow one answer, whether the hand is free this tick. What wants the hand: the hands step (the weapon, whenever there is a live target worth shooting, whichever action is running the feet), and chop and mine (the tool, but only in position — and those two are the one thing that stops the hands, because a swing and a throw cannot share an arm). So the answer to "does it hold the torch while walking to an ore in the dark" is yes: the mine action leaves the hand empty on the walk and takes the pickaxe out when it arrives, and the torch fills the gap both before and after the swing. On a mine job the hand goes torch → pickaxe → torch as it arrives, mines and leaves, and the light and the reveal go with it; while the pickaxe is out there is no glow, because the torch is not out. A shown torch reveals the map on a cadence: a flood through air tiles to the torch's reach marks the air and the first solid face, never what is behind a wall, on or off screen alike, and each changed map tile is queued the way the game queues its own changed tiles so only those are redrawn.

## What is verified and what is not

Verified in play (the first two runs): the body draws and swings, chopping the right tree at the trunk, arrows fly, the health bar, persistence, and that the brain runs (first tick logged, action = wander). Everything else in this file describes code that compiles and has not been watched: the horizon charge, kiting, dodging after the simulation rewrite, the navigator on anything but flat ground, mining, the torch thresholds against real cave light, the map reveal and the map head. The overlay (left square bracket) shows every score, the danger and horizon, the light readings and whether the torch is shown, the chosen spot and the path; the telemetry in `Debug/` writes the same and more to a file every tick, and is how a wrong choice is read after the session rather than guessed from memory.

## Where a new thing goes

A new fact about the world is a field on a sense, computed once. A new thing the companion can do is a `CompanionAction` in the `Actions/` family it belongs to, with a score, a forecast and a position request; a new family (opportunistic tasks, missions) is a new folder beside the four and one line in the chooser. The tool that action drives is a class under `Work/` in its own subfolder. A new reason to prefer one spot over another is a factor in the positioner. A new kind of move (a grapple, a mount) is an edge kind in the navigation grid. A number you want to tune is in `Weights.cs` and nowhere else.

## Traps

- **An action that computes a world fact itself is wrong even if it works**, because the next action computes it differently; put it on a sense.
- **"Is it safe" is not a question about the world; it is a question about a body.** `PlayerDanger` and `CompanionDanger` are different numbers and reading one where the other belongs is the defect that killed the companion on 2026-09-09. Any new term of the form `1 − danger` names whose.
- **An action cannot fire a weapon.** Shooting is the hands, in `Brain.Engage`, and putting a `TryFire` back inside an action recreates the state where following the player made the companion unable to shoot.
- **The horizon is float.MaxValue with no threats.** Any arithmetic on it must handle that, or a forecast compared against it overflows into nonsense.
- **Scores are 0..1 and considerations multiply.** A consideration returning 0 vetoes; use `Consideration.AtLeast` when an action should stay eligible.
- **An action that holds a tool on the walk hides the torch.** Hold the tool only in position; the hand must be empty on the way.
- **An action's `Score` that searches the world is called every tick for every action, winner or not.** Put a search behind a trigger and a cooldown, as the chop and mine actions do, or the losing action pays it while something else runs.
- **`Senses` and `Reflexes` are both a namespace and a class.** From outside `DecisionMatrix` write `DecisionMatrix.Senses.Senses`; inside it the short form resolves.
