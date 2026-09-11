# Multi... Player?

An AI companion for Terraria, as a singleplayer tModLoader mod. It travels with you, fights beside you and joins nearby work. It is intended to feel like another player: free to stop for something useful, close enough to help when you need it. Independent gathering missions are outside that design; a mastery tree will expand its abilities over time.

Early development. `/companion` spawns a companion that follows you, wanders when idle, shares your max life and defence, gets downed instead of dying (stand beside it for three seconds to revive, or wait ten for it to get up on its own), gathers reachable ore and nearby trees, and chooses attacks using predicted effective damage, threat removal and follow-up shots. Click its top-centre health notch for the native profile card: choose Off/Mimic/Auto mining and chopping, toggle voluntary hunting, pot breaking and torch placement, or step following distance through Close, Standard and Free. Drag the notch to move it; right-click it to reset. The card itself drags by its title bar, and Inventory and a selectable eight-branch mastery preview are pages within it, swapping the card's own body rather than opening another window. Mastery selections currently grant no stats or abilities and consume no materials. While a companion is up, enemies spawn twice as often.

Optional work can range beyond ordinary following and retains extra room to finish its current job and gather the drops. Permanent lighting uses Terraria's torch Smart Cursor eligibility and spacing, prefers elevated sites and consumes one torch from the companion's bag before using the player's inventory. Bed-containing rooms are protected from autonomous edits; open or oversized structures use conservative protection near the bed. This is early protection logic, not a general claim to recognise every player-built base.

---

# Expected Behaviour

This section describes the companion we are building, as a player would experience it. It is written as one continuous half-hour of play, from spawn to the first boss. It makes no reference to how any of it is achieved, and no reference to what the mod currently does. It is the target.

One idea generates all of it. **The companion is a competent friend playing the same game you are, at the same time, with its own opinions about what is worth doing.** It is not a pet, not a follower and not a unit you command. It hangs around roughly where you are because that is where the game is happening. It never waits for your permission and it never copies you.

### 0:00 — You load in

The world loads on a clear morning and the companion is already standing a few tiles off, facing whatever direction it last looked in. It does not snap round to look at you and it does not plant itself at your heel; it takes in what is around it — a few trees on the slope, nothing dangerous, nothing dark, nothing on the ground worth picking up — and having found nothing better to do, it stays near and idles. Idling is not standing still. It shifts its weight, turns as you turn, drifts a step or two when you drift. When you set off to the right it comes with you rather than behind you, picking its own line across the terrain, and where there is an easier route a tile or two over it takes that one instead of tracing your footprints.

### 2:00 — The slime that matters and the one that doesn't

A blue slime appears at the left edge of the screen, bouncing away from you, and the companion does nothing about it at all. It has looked at that slime and worked through it honestly: there is no way to hit it from here, there is no way to hit it from the air either, and the only thing that would put it in range is a long walk to the left — a walk that ends with the companion thirty tiles behind you and a slime dead that was never going to reach either of you. It is not worth doing, so it is not done, and the companion does not fidget about it or half-start toward it.

Change one thing and the answer changes with it. Stop walking, turn to a tree and start swinging, and the same slime becomes worth killing, because now the walk costs nothing — you are not going anywhere, and the companion will be back at your side before you have finished the trunk. Change a different thing instead: leave the slime where it is but have it bouncing toward you rather than away, and it becomes worth killing immediately, because it is going to arrive whether anyone walks anywhere or not. The companion draws and shoots it, and it does this while still walking, because aiming and walking are separate things and it has never had to stop doing one to do the other. You do not break stride. Neither does it. What it never does is put itself between you and something to soak a hit; it is a fighter standing where it can shoot, not a shield.

### 4:00 — Trees, and reading where you are going

You walk past a single oak and neither of you stops, because one tree is not worth breaking a journey for. Two minutes later you pass a stand of four grown together, and this time the companion peels off, chops the nearest trunk down, sweeps up the wood and the acorns, and comes back — eight seconds gone and twelve tiles to make up, which it makes up without fuss. What decided the difference was not a rule about how many trees are enough; it was that four trees together are worth more than the twelve tiles, and one is not.

While that is happening the ground ahead splits into a high ledge and a low path, both running right, and you jump up to the ledge. The companion stays on the low path and keeps pace. It has not lost you and it is not confused: you are going right, the low road goes right with less climbing, and fifty tiles on the two paths meet and you are side by side again. Then you change your mind, turn, and drop into a ravine, and the companion comes down with you straight away — down the near wall, which happens to be the easier side, so it reaches the bottom before you do. You think better of it and climb back out, and it is out before you are.

There is a way to get this wrong, and it matters that the companion does not. Reading where you are heading is not predicting where you will be in five seconds and sprinting there. If you are walking right and the companion is on your left, and you turn left for a moment to grab something, it does not go charging away to the left — it is already on your left, it is already where that brief turn was pointing, and a moment's turn is not a change of plan. It simply keeps walking with you, and when you resume right, nothing has been lost and no ground has opened up between you. It hurries only when it is genuinely far from where you are going.

### 6:00 — The cave, and going in without you

There is a hole in the ground ahead and the inside of it is dark. The companion is aware of that darkness the way it is aware of the trees — as a place with a property, twelve tiles wide and unlit, sitting over there — rather than as something it notices only once it is standing in it. It is already holding a torch by the time you reach the lip, though it places nothing, because the surface is bright and there is nothing here to light.

You keep walking along the surface and go straight past the hole. **The companion goes in anyway.** It drops in, lights the first chamber, comes back out and catches up with you on the surface, and at no point did it look to you for a decision. Had you gone in instead, it would already have been down there ahead of you doing the same thing. Keeping the place lit is its own responsibility and it discharges it whether or not you are participating; you have never once had to place a torch to make it place one.

### 8:00 — Crevices, and who gets the light first

This time you do go down, and you head along a cave system that runs mostly sideways, with narrow side passages branching off it into the dark every so often. The companion ducks into them. It goes in, lights what is in there, and climbs back out to rejoin you — and before it goes in at all it has made certain it can get back out, so a crevice that is a one-way drop gets lit from the lip or not at all. It is never trapped by its own curiosity.

Which dark it deals with first depends on you, and it reads that sensibly. If you are down here carrying nothing that gives off light, standing in your own small patch of black, then you are the dark that matters and the crevices can wait; the thing that gets someone killed is not seeing the skeleton beside them. If you are carrying a torch or you have thrown a glowstick ahead of you, your own bubble is handled, and the companion takes itself off on a proper expedition — the crevices, the corridor ahead, the chamber you have not walked into yet. Either way what it is pursuing is the same thing: that this cave is lit.

### 10:00 — Copper, and being sure

There is copper in the wall to the left, six tiles up a slope, and the companion breaks off what it was doing and goes for it — not because ore is sacred, but because a vein it can actually get to is worth more than another torch right now.

The important part happens before it moves. It makes certain it can get there. Not nearly certain, not optimistic, not "it looks close enough" — it establishes that it can stand somewhere it can swing from, and only then does it commit. Where it can get partway and no further, it goes as far as it is sure of and works the question out again from there, which often changes the answer, because being closer makes things clearer. Where it cannot be sure it can arrive, **it does not choose the ore at all.** There is nothing to abandon and nothing to be stuck on, because it never went. The vein behind two metres of solid dirt is simply not something it is considering, in the same way a slime on the far side of a wall is not something it is considering.

Once it is there it swings until the vein is gone, and when the last piece of it is out it picks up what fell. There is copper two tiles above the ledge it is standing on, higher than it can reach, and it jumps and swings at the top of the jump. That is not a special trick for ceilings — getting a tool onto something includes every way its body can move, and jumping is one of those ways, exactly as it is when the question is how to line up a shot.

### 12:00 — One thing after another

You are at a chest and stationary. Ahead of the companion, in a line, there is a pot on a ledge, an unlit corner above the pot, and a vein ten tiles past both. It wants the vein, and getting to the vein takes it right under the other two.

So it breaks the pot on the way, with whatever weapon is in its hand — a thrown knife if that is what it has, a sword swing if that is what it has, because a pot is a thing you hit and not a job you travel for. The contents drop at its feet and it scoops them up without breaking stride. The corner is one hop above the path, so it hops and lights it. Then it arrives at the vein and starts mining. From the outside this looks like a plan, four things done in sensible order, and it is nothing of the kind — each of those things was nearly free because the journey was already being made. This is how it behaves constantly. Terraria is a game of doing three things on the way to a fourth, and the companion plays it that way.

Halfway through the vein a bat comes in through a gap in the ceiling and makes for you. The companion stops mid-swing — the swing is not sacred either — kills the bat, and goes straight back to the same vein and finishes it. That return is seamless. It does not stand around reconsidering its life, and it does not wander off to something else on the way back; the vein was the best thing available before the bat and it still is.

### 14:00 — The slimes it was ignoring

Three slimes have been audible for a while in a chamber off to the side, and the companion has done nothing about them the whole time, because they cannot get out and it has been busy with things that were worth more. Now the vein is gone, the drops are collected, the corner is lit, there is nothing else on the floor, and you are still standing at your chest. With everything better used up, the slimes are now the best thing there is, and the companion goes and deals with them.

It gets itself somewhere it can shoot from and starts putting damage in. They come at it, and it backs off as they come — over them where the ceiling allows, under them where the floor does, sideways along the corridor otherwise — and it kills all three without taking a hit. It collects what they drop. Then it looks around again, finds a lit cave with nothing on the floor, no ore, nothing alive and a player who has not moved, and comes back to stand near you. Nobody told it to do any of that in any particular order; the order fell out of what each thing was worth at the time.

### 16:00 — Everything at once

Terraria does not hand you one problem at a time, and this is the part that matters most.

You are up on a dry ledge on the left. Below and to the right is a water pool with two jellyfish in it, there is unmined ore in the floor of that pool, two zombies are closing from the right on the far side of the water, and the whole area is half-lit. The companion has just dropped into the water going after that ore, so it is now standing on the thing it wanted, underwater with a breath running down, within reach of two jellyfish, with zombies inbound and you dry and safe twenty tiles away.

It does not freeze and it does not try to finish what it started. It leaves — out of the water, to the left, up onto your ledge — and it does that not because you are there but because that is the only place in this scene that is not actively costing it. Standing on the ore means drowning slowly while two things hit it, and a copper vein is not worth that. Once it is out and dry, the jellyfish stop being a problem at all, instantly and completely, because jellyfish cannot leave water and the companion is aware of that. It turns and starts shooting the zombies as they come, giving ground to the left to keep the gap open.

Then a bat comes in from the left, behind it, and it is pinched. What it does here is the whole character of the thing. It kills the bat first — not because bats are more dangerous than zombies, but because the bat is sitting in the direction it was retreating, and an escape route is worth more than a zombie. Set the same scene slightly differently — zombies already on top of it, bat still distant — and it kills the zombies instead and gives ground to the right, away from the bat. Same reasoning, opposite action, and nothing in it was decided in advance.

Giving ground to the right means the water. It works out that it can clear the pool in a single jump, so it jumps it rather than wading through it, and while it is in the air the ceiling lip cuts its line to the bat and leaves it with half a second and nothing to aim at — so it puts a shot down into the jellyfish below on the way over. It lands on the far side, kills the bat, and then stands on that ledge and shoots the jellyfish dead from above, dry and out of reach the whole time. With the pool safe it drops in, mines the ore it came for in the first place, and collects the lot on its way back up — jellyfish drops, zombie drops, the bat, the ore — and comes back to you.

### 18:00 — The drop it should not have taken

You jump down a shaft and it follows you in. Halfway down it registers what is at the bottom: water, two jellyfish, no ledge within reach. It throws in a second jump in mid-air and pulls itself sideways onto a lip it can hold — because by this point in its development it has more ways to move than walking and jumping, and every one of those is part of what it considers possible when it commits to anything. If the second jump does not save it, it takes the landing and immediately starts backing away from the jellyfish, to the left if they are on the right, rather than standing in the water being bitten while it thinks about what to do.

### 20:00 — Knocked in

You are walking right along the top of a deep pit with ore visible at the bottom of it. The companion looks in, works out that it could get down there but not back up, and stays out — the ore is real and so is being stuck, and being stuck costs more.

A bat clips it and knocks it in anyway. It does not announce that it is stuck. It looks around at where it now is and makes the best of it, exactly as a person would: it mines all the ore, kills whatever is down there with it, lights the place, picks up every last thing that fell. Only when there is genuinely nothing left to do does it turn its attention to getting out, and it keeps working at that as the world changes around it — and if you have walked far enough away by then, it comes to you regardless. There is no point at which it stands still doing nothing while there is something to do.

### 22:00 — Somewhere it cannot follow

You take a route it cannot take. It does not stand at the entrance waiting for you, because it is not a dog and waiting is not a thing it does. It gets on with whatever is where it is — the trees on this side, a slime, drops on the floor, a dark corner — and it does that for as long as there is anything worth doing. When there is nothing left, and only then, does the distance between you become the thing that matters, and it comes and finds you.

### 24:00 — Home, and getting out of the way

You head back with a full bag and it comes with you, sweeping up anything it passes on the route. It picks up everything. A single block of dirt on the floor is worth the two steps, because it has no way of knowing what any given thing is worth to you and the wrong guess costs more than the walk.

At home you start sinking a shaft — the two-wide platform drop straight down — and the companion is standing exactly where the next platform goes. It moves. While you are holding something you can place and your cursor is anywhere near it, that cursor is a place it stays out of, so it jumps, or sidesteps, and it keeps doing that as you work your way down. Holding a sword or a torch instead, it does not care and does not move. The same courtesy applies everywhere: it is never the thing standing in the one-tile gap you are trying to walk through.

### 26:00 — The eye

Night falls and you use the Suspicious Looking Eye. The music changes and the Eye of Cthulhu comes down out of the dark.

Everything else the companion was interested in stops mattering. It is not mining, it is not lighting anything, it is not sweeping the floor, and it is not standing near you — because nothing else in the world is worth anything while this thing is alive. It has two concerns now and they are in a strict order: **do not get hit, then do damage.** In that order, and the order is not negotiable. If the only way to get the boss in range is to step somewhere it will take a hit, it does not step there; it stays out and does less damage, because it cannot do any damage at all once it is on the floor.

It also stops trying to look after you, and that is correct. Ordinarily, when something comes for you, the companion makes a judgement — that zombie is heading for him, but he can handle a zombie, so I will finish this vein and step in if it turns serious. Against a boss that judgement is meaningless, because there is no version of this where it stands between you and the Eye of Cthulhu and that helps. So it drops the idea entirely and fights.

It also stops being tethered to you the way it normally is. Boss arenas are enormous; a companion held on a short leash spends the whole fight bobbing around your shoulder instead of fighting. It ranges much further than it ever would ordinarily, with a gentle pull back that gets stronger the further out it goes, so it has room to manoeuvre without ever genuinely leaving. The same is true whenever the game itself turns into an event — a Blood Moon, a Solar Eclipse, a Goblin Invasion, a Pumpkin Moon — because those are the same situation: the world is now about one thing, and standing next to you being helpful is not it.

### 28:00 — The fight

It fights the way a decent player fights. It stays at the range where it can hit and not be hit, and it keeps moving. When the Eye stops circling and lines itself up for a charge, the companion is out of that line before the charge lands — not by recognising this particular boss, which it has never heard of, but by seeing something aiming itself and accelerating and getting off the line it is aiming along. That works the first time it meets an attack nobody has ever described to it.

When the Eye splits and starts throwing out a swarm of small eyes, it changes weapon, and it changes weapon for the right reason. Something that punches through a line of targets is worth far more against a swarm than against one large thing, and something that hits one target very hard is the reverse. It is not that it knows the Eye of Cthulhu takes single-target damage; it is looking at what it is holding, what is in front of it and how those things are arranged, and taking the better of the two. Hand it a weapon nobody has ever seen and it makes the same judgement correctly.

If you have built platforms in the arena, it uses them, from the moment you place them. Anything you build is immediately part of what it can do.

If it does go down mid-fight, it is back on its feet within ten seconds and rejoins, so a bad moment costs you its help for ten seconds rather than for the rest of the fight.

### 30:00 — After

The Eye dies and the loot scatters across the arena. The companion collects what it can reach and brings it over.

Within a few seconds it is ordinary again — standing near you, looking around, and if there is a tree or a dark corner or a vein anywhere close, it has already gone to deal with it.

---

# Current Behaviour

This section describes what the companion actually does today. Every scene here is taken from recorded play. The evidence is four sessions on 2026-09-11 running mod versions 0.13.4, 0.14.0 (twice) and 0.15.0, totalling about thirty-one minutes of play across 110,757 recorded ticks, read together with the commits landed between them. Tick numbers refer to the session named at the head of each scene.

### It re-decides two or three times a second, and it has got worse every version

The single most visible thing about the companion in play is that it looks like it cannot make up its mind, and the recordings agree. Across the four sessions the length of time a chosen behaviour survives has fallen steadily, and the last fall was sharp:

```
version   session      ticks   behaviour changes   held per decision
0.13.4    12:45        13,844          154              89.9 ticks   1.50 s
0.14.0    16:53        31,722          500              63.4 ticks   1.06 s
0.14.0    17:22        26,716          481              55.5 ticks   0.93 s
0.15.0    18:30        38,475        1,727              22.3 ticks   0.37 s
```

In the most recent session, 1,487 of 1,727 chosen behaviours lasted thirty ticks or less, and 1,436 of them went straight back to the behaviour that had just been abandoned. Nothing involving walking anywhere can complete in a third of a second, so most destinations the companion chose were abandoned before it arrived, and this is the direct cause of the "it just stands there" and "I don't know what it's trying to do" impression: it is not choosing badly so much as choosing again, constantly.

The most recent regression is traceable to a single change. In that session 1,197 of the 1,727 changes happened on a tick where the body had been flagged as not making progress, and that flag was raised on only 4.5% of ticks — so under a twentieth of the session generated seven tenths of all the decision churn.

### It almost never shoots anything

Across all four sessions, between 60% and 71% of every tick reports that the shooting system had no target selected, and shots actually fired account for 0% to 1% of ticks. This holds regardless of how many enemies are present.

The clearest instance is in the 0.15.0 session, ticks 11,352 to 13,869 — forty-two seconds. Over that stretch there were ten hostile NPCs present on average, nine of which the companion's own senses judged reachable. You were swinging a weapon for 2,250 of those 2,518 ticks. Ambient light was 0.12, so you were fighting in the dark. The companion spent 1,717 of those ticks guarding and 648 hunting, stood nineteen tiles away from you, and **fired nothing at all** — the fire column reads no-target on every single one of the 2,518 ticks — while holding a lit torch for 2,514 of them. From the outside, that is a companion standing in a dark cave twenty tiles from a fight it is nominally participating in, holding a torch.

### It hunts things it never gets near

Hunting is one of the most-selected behaviours in every session, taking 6% to 32% of the time, and it routinely runs with nothing to shoot at. The proportion of hunting ticks where the shooting system had no target at all was 72% in the 0.13.4 session, 51% and 33% in the two 0.14.0 sessions, and 74% in the 0.15.0 session. So the part of the companion that decides to walk toward an enemy and the part that decides whether that enemy can be shot disagree with each other most of the time, and the walking part wins.

In the 0.15.0 session, ticks 36,082 to 36,423, the companion hunted for 342 consecutive ticks without moving a single tile — it begins and ends the stretch at exactly the same position — while you stood three tiles away attacking and then mining. Two hostiles were present, both judged reachable, and it fired nothing for the whole stretch while holding a torch. A separate check in the reading tool confirms that over the same window the weapon evaluator reported every weapon-and-target pair it considered as out of reach.

The commits between the sessions are informative here. A change landed before the 16:53 session specifically to make a hunt target admissible only when a reachable standing position exists that has a line to it. The proportion of hunting ticks with no target improved from 72% to 51% and then went back up to 74%, and the overall share of ticks with no firing target did not move at all.

### Mining walks at ore it has not established it can reach

Of the 8,944 ticks the 0.15.0 session spent mining, 6,242 — seventy percent — report the status `approaching unproven ore`, and 1,794 report actually swinging a pickaxe. That ratio is the same story in every session, only smaller: the 16:53 session reports 1,648 ticks approaching unproven ore against 531 mining, and the 17:22 session 557 against 267. The companion spends most of its mining time walking toward rock it has not established it can get to, which is what you see as it standing near ore doing nothing, or pressing at a wall with copper behind it.

### It commits to one thing for a very long time when that thing is failing

In the 17:22 session, ticks 18,501 to 21,531 — fifty seconds without a break — the companion was trying to break a single pot. Over that time it moved from tile 2773,539 to tile 2775,528, which is two tiles sideways and eleven up, in fifty seconds. There were fourteen hostiles present on average, thirteen of them judged reachable; you spent 542 of those ticks mining and 633 attacking; and the companion fired nothing for the entire stretch. In the same session the reading tool records the companion's own mining system reporting ore within actual swing reach on 979 of those ticks.

That is the inverse of the previous finding and both are true at once: it abandons things it could finish in a third of a second, and it holds onto things it cannot finish for a minute.

### Its movement fails far more often than it succeeds

The movement ledger from the 0.15.0 session, counting every move the planner proposed and what the body did with it:

```
             planned   completed   interrupted   faulted
Walk           7,372       1,305           864         1
Jump             323          53           180         5
Drop             213          32           230         1
FallThrough       63           4             8         0
```

Fifty-three jumps completed out of 323 begun. Requests for a position with a shot were made 594 times and reached 45 times.

### It died in a pool it could have jumped out of

In the 0.15.0 session the companion went down once, at tick 21,476, and spent 6,100 ticks — a hundred seconds, a sixth of the session — lying on the floor afterwards.

The eighty ticks before that are readable in full. From tick 21,400 it is guarding, with its own danger reading between 0.85 and 0.96, and its life going 17, then 4, then 1. The route it is executing is a jump from tile 3643,489 to 3641,488, and its recorded outcome is `Interrupted`. Its velocity trace repeats the same opening arc three separate times — the identical four-step pattern of a jump beginning and being cut off — and the collision reflex is engaged on every tick of the descent. It attempted the jump you watched it fail, three times, and something pre-empted it each time. Throughout all of this the behaviour that exists to save its own life scored 0.000 on every tick.

### It does not know where the dark is

The companion reads one number for how bright it is where it is standing — across the 0.15.0 session that number ranged from 0.000 to 0.610 — and holds no information about where darkness is anywhere else. Consequently there is no sense in which it can travel toward an unlit area, and lighting only ever happens where it already happens to be. The share of time spent placing torches fell from 19% in the 0.13.4 session to 4–7% in the three sessions since.

### Deciding what to do costs enough to be felt

The single most expensive tick in the 0.15.0 session was tick 31,001, at 43.0 ms against a 16.7 ms frame; 41.8 ms of that was the decision step alone, with the route planner costing 0.02 ms on the same tick. Across the session the decision step exceeded 8 ms on 958 ticks and a whole frame on 73. In the previous session the same step exceeded 8 ms on 273 ticks. The cost roughly doubled in the same version that tripled the number of decisions, which is consistent with them being the same event.

### What the sessions look like as one sentence each

```
0.13.4  12:45   hunts a third of the session, mostly with nothing to shoot;
                places torches often; mines for 137 ticks in total
0.14.0  16:53   follows and hunts; mines for 531 ticks against 1,648 spent
                walking at unproven ore; still no firing target on two thirds
                of ticks
0.14.0  17:22   spends thirteen percent of the session on pots, fifty seconds
                of it on one; guards for over a third; fires almost nothing
0.15.0  18:30   mines more and swings more, decides three times a second,
                fires nothing in a ten-enemy fight, dies in a pool
```

---

# The System In Place

This section enumerates what is actually built, read from the source. Each part names what it is meant to contribute and, where the recordings show a mismatch, what comes out instead. It is a description of the machinery, not a defence of it.

## The shape of one tick

The brain runs once per game tick, in one direction, with no stage allowed to reach back into an earlier one:

```
WorldObservation ──► CombatReflexes ──► SharedMovementSystem ──► the motor ──► the NPC body
       │                    │                     ▲
       └──► BehaviourSelection ──► PositionSelection ┘
                    │
                    └──► WorldInteractions (tools)

the hands run separately, after movement, whenever no tool holds the arm
```

`CoordinateBrainTick.cs` owns that order. The motor is the only thing permitted to write to the live NPC. Outcomes from movement come back to the next tick only as observed facts — the body is stranded, the body has not covered ground — never as a lower stage overruling a higher one.

## World observation

`Brain/WorldObservation/` rebuilds every fact once per tick so that no two consumers derive the same thing differently. It observes the player's position, two-axis travel intent and whether they are fighting; hostile NPCs and their predicted positions; hostile projectiles; collectible drops; the companion's own breath, liquid state, fire and recent damage; and ambient light outside the companion's own glow.

Two of its outputs are load-bearing for this discussion.

**Reachability is three-valued and then rounded per caller.** `Reachability.WalkerReach` runs a bounded search of at most 400 expansions and returns one of three answers: a whole route exists, the reachable region was exhausted without arriving, or nothing was established — the budget ran out, or one end has no standable tile. Two named helpers round that: `WalkerCanReach` treats *nothing established* as reachable, and `WalkerProvenReach` treats it as unreachable. The threat sense uses the first. The documented reasoning is that a threat wrongly ignored costs the player a hit while one wrongly feared costs a little caution, and that reasoning is sound for deciding what is dangerous. The same answer is what makes enemies eligible to be hunted, where the arithmetic is the opposite. In the 0.15.0 session there was not one tick out of 37,584 with hostiles present where the reachable count was zero.

**Light is a single number.** `ObserveLight` samples brightness at the companion's position, excluding its own torch. Nothing in the repository records brightness anywhere else, in any form. This is the whole of what the companion knows about darkness, and it is why the Expected Behaviour section's idea of an unlit region as a place has no representation at all.

## Combat reflexes

`Brain/CombatReflexes/AssessImmediateThreats.cs` runs after observation and before any behaviour is chosen. It predicts where hostiles and hostile projectiles will be and supplies a predicate over simulated future body states — "this body state is unsafe at this tick". When the body as it currently moves would be hit within the lookahead window, the movement system chooses avoidance controls and the reflex owns the feet for that tick. Reflexes never write the body themselves.

This runs ahead of behaviour selection by design, which means it can pre-empt a move in progress. The recorded death at tick 21,476 shows a jump beginning three times with this reflex engaged throughout, which is consistent with a reflex repeatedly taking the feet from a traversal that needed several consecutive ticks to complete, though the recording does not prove that causal link on its own.

## Choosing a behaviour

`Brain/BehaviourSelection/ChooseBehaviour.cs` holds the entire list of things the companion can be doing. There are eleven:

```
SurviveAction        get the body out of danger or out of water
GuardAction          stand where the thing threatening the player can be shot
KiteAction           back away from a walker inside melee reach
HuntAction           walk toward an enemy worth attacking
LootAction           walk to the nearest pickup that fits somewhere
ChopAction           a nearby tree, under the chosen policy
MineAction           a retained ore vein, under the chosen policy
BreakNearbyPots      a pot within the work envelope
PlaceNearbyTorches   a permanent torch site within the work envelope
WalkWithPlayerAction follow the player's two-axis travel
WanderAction         the floor score: stand, stroll, hop
```

Every behaviour returns a number in 0 to 1 built as a **product** of named curves from `EvaluateConsiderations.cs` — `Inverse`, `Rising`, `Band`, `Step` and `AtLeast`. Because the terms multiply, any single zero vetoes the behaviour outright and no other term can buy it back. There are no priority branches anywhere; this is a deliberate project ruling.

Four multipliers are then applied by the chooser itself and recorded separately in telemetry: **protection** (an optional excursion is discounted by how urgently the player needs defending), **commitment** (the behaviour that is already running is multiplied by 1.15), **horizon** (a behaviour forecast to keep the companion away longer than the observed safety horizon is charged for the overrun) and **useful work**.

Because an ordinary behaviour tops out at 1 and the incumbent sits at 1.15, anything that must be able to *interrupt* a running behaviour cannot itself top out at 1. Guard and survive therefore have urgency constants that scale them past the ordinary band, sized as a ladder: guard is scaled to clear a committed ordinary behaviour and survive to clear a committed guard. Those constants live together in `BehaviourWeights.cs`.

This is the machinery intended to produce the Expected Behaviour section's core claim — everything competing all the time with no modes. It is the right shape and it is not what produces the observed churn. The 0.15.0 regression came from making the commitment multiplier conditional: it dropped from 1.15 to 0.6 whenever the body was flagged as not covering ground. The flag is a property of the body rather than of the behaviour, and is not cleared when the behaviour changes, so a newly chosen behaviour inherited the penalty before it had moved at all and lost on the following tick to whatever it had just displaced. That change has been reverted to the flat 1.15.

The folder's own notes record a second finding that survives the revert: **hunting outranks work by being eligible rather than by outscoring it.** In one measured session mining won 1,368 of the 1,734 ticks on which it had a live target and torch placement won the other 366; hunting won none of them. Hunting fills a third of a session because it is eligible three-quarters of the time and everything better is absent. The lever is therefore whatever makes useful work eligible — which returns to ore whose approach was never established — and not the distance between two constants.

## Choosing where to stand

A behaviour does not move the body; it returns a `PositionRequest` naming a kind of place (exact, with-player, line-of-fire, guard, retreat, roam), an anchor and sometimes a target. `Brain/PositionSelection/ChooseUsefulPosition.cs` turns that into a specific feet tile.

It samples standable tiles around the anchor, scores them cheaply on band, danger, openness, travel bias and a straight-ray sight test to the target, then spends a small fixed budget asking the trajectory solver whether the best few candidates actually have a shot. The cheap pass carries the sight test deliberately: only a handful of candidates are ever solved, so a cheap ranking blind to line of fire would select that handful on unrelated grounds and the winner would be whichever blind spot ranked well.

The incumbent tile keeps a small preference so that equally good candidates do not cause a replan every tick. A spot with no firing solution scores 0.02 and gets no incumbency bonus — previously 0.15 with the bonus, which let a position that could not shoot defend itself.

`FollowPlayerObjective.cs` defines what counts as having arrived near the player: separate horizontal and vertical comfort limits plus a local sight connection, so a tile that is close in a straight line but on a different floor does not satisfy following. One-way drops are refused by default, with one exception — following the player downward, and only when the region actually reaches the player, which distinguishes a player below a drop from a player sealed behind a wall.

## Moving

`Brain/SharedMovementSystem/` is the single movement authority. `CoordinateMovement` is the only request surface; `MovementQueries` is the only read surface for geometry and reachability. Nothing else writes controls.

**The body.** `BodySimulation/` holds the body's numbers and shape tests in exactly one place, because a constant existing in two places was the project's first bug class — a jump the planner found and the body could not fly. `BodyMotion.Step` is one tick of the body under the game's own ordering, and it is the rule every traversal is simulated with and the offline replay drives.

**The graph.** `RoutePlanning/NavGrid.cs` defines what a node is: a feet tile where a standing pose exists and no lava sits in the column. Edges are generated by asking each kind of move what it can do from that node. `AStar.cs` searches them.

**The moves.** `MovementExecution/` holds one class per kind of move — walk, jump, drop, fall-through — and each class contains both halves of that move: `Candidates` proves every edge of that kind out of a node by simulation, `Steer` returns the controls that perform it, `Done` says when it is reached and `Check` names why it cannot be. The two halves live together because the defect that ran through every session of 2026-09-08 was a companion standing still holding a valid path, caused by the planner proving a move by one rule and the follower performing it by another.

**Execution.** `Navigator.cs` hands each step to the traversal of its kind. Every tick it asks `Done`, then `Check` — a fault ends the step, prices the tile, counts a strike and replans — and only then `Steer`. `PlanLocalMovement.cs` re-proves the whole remaining move from the body state the engine actually left, because a representative grid pose proposes an edge and does not authorise executing it from a different pose.

**Memory.** Successfully executed traversals become directed connections stored in the world save, fingerprinted by the swept area's tile shapes and liquids, and retired when the terrain changes or the move physically fails.

This is the most heavily worked part of the codebase and it carries the most recorded dead ends. What the sessions show is that it is still where most failures land: 53 of 323 jumps completed, 32 of 213 drops.

## The hands

The hands run every tick from `Brain.Engage`, after movement, independently of whichever behaviour won the feet. This is deliberate and recent: shooting used to live inside hunt, kite and guard, which made the companion structurally incapable of firing while following, looting, wandering or working. Now a following companion shoots exactly as readily as a guarding one.

`Weapons/Arsenal.cs` picks the target and the weapon by simulating what each legal pairing would actually land, using time-discounted effective damage, projected kills and harm prevented over a bounded follow-up window. Weapons supply physical facts and never a private suitability score, so the arsenal's choice is made from what a weapon does rather than from what it is. `Brain/ProjectileAiming/` solves the arc, flattest first, and re-traces the final rotated launch before a projectile is spawned.

This is the machinery that ought to produce the Expected Behaviour section's weapon choice during the Eye of Cthulhu fight, and structurally it is the right shape. The recordings say the problem is upstream of it: it reports no target on two thirds of every session, which is a statement about what reaches it rather than about how it chooses.

## The tools

`Brain/WorldInteractions/` performs the abilities, and never decides when to use them.

Mining calls the game's own `Player.PickTile` on the companion's drawing-only player, so the damage formula, the power gates and modded tile checks are the game's rather than a copy — a copy existed once and drifted in eight places. `OreFinder` finds veins and reports its bounded reachability query's three-valued answer separately from "no ore found". Chopping uses the vanilla axe formula. Torches use the game's own Smart Cursor torch recommendation, including its existing-torch spacing, and consume an item only after a tile actually appears. `WorldProtection` vetoes autonomous edits inside a bed-anchored room.

## What the player can change

`CompanionPreferences` holds per-character choices — mining and chopping policy (off, mimic, opportunistic), hunting, pot breaking, torch placement, and following distance — saved with the character. The tModLoader config holds two client-side switches for the brain inspector and telemetry recording. The overlay's layer selections are saved with the character as well.

---

# Known Limitations Of This Assessment

The mismatches above are attributed to specific systems, and those attributions can be wrong. They are written from reading the code and the recordings together, and a symptom can have a cause in a system nobody suspected. Specifically:

- **Attribution is inference, not measurement.** Where this document says a system causes an observed behaviour, only the commitment-multiplier regression has been demonstrated by a before-and-after measurement across two versions. The rest are consistent explanations that have not been isolated.

- **The recordings are four sessions on one world.** Every number here comes from about half an hour of play on 2026-09-11 in one cave system with one player, one weapon loadout and one set of settings. Nothing here establishes behaviour in a jungle, in the ocean, at night on the surface, in hardmode or with any other mod loaded.

- **The reading tool has its own defects.** At least one check is known to be wrong: `Tools/SessionReport/CheckTheFight.cs` tests whether an evidence string *contains* a phrase and then reports a conclusion about every pair it examined.

- **Terrain snapshots record shape and not material.** A question that needs to know what a tile is made of cannot be answered from the recordings and has to be answered from the decision columns instead, which is a weaker source.

- **`brain_ms` does not cover everything the companion costs a frame.** It sums the brain's own phases. The telemetry recording itself wrote 118 MB across one ten-minute session and is not measured by any column, so the observed in-game stutter has not been fully attributed. A session recorded with telemetry disabled would settle it.

- **Two documented mechanisms do not exist and block work that depends on them.** There is no record anywhere of ground the companion or the player has already covered, and no record of brightness anywhere except where the companion is standing. Several of the behaviours described in Expected Behaviour cannot be attempted until those exist, and any design that assumes them is assuming a component rather than a weight.

---

# Potential Improvements

**Everything in this section is speculative.** These are ideas that came out of comparing the three sections above; none has been researched, costed or weighed against alternatives, and several would be invalidated by a change to the underlying decision system. Nothing here should be built on the strength of appearing in this list.

- **Price the journey into every option.** If what an action costs included getting to where it happens, then a thing on the way would be nearly free and a thing across the map would be expensive, without either being a special case. This would bear on the slime that is not worth walking to, on chaining several small jobs into one trip, and on why a stationary player makes distant work worthwhile. Unexamined: how it interacts with the existing protection and horizon multipliers, whether it makes the decision step more expensive than it already is, and whether it is expressible at all inside a scheme where every term multiplies.

- **Separate the two questions "reachable" currently answers.** Being dangerous and being worth attacking want opposite roundings of the same unknown. Unexamined: whether splitting them doubles the search cost, and whether the hunting side wants a stricter rounding or a different question entirely.

- **Make safety a price on positions rather than a behaviour that competes.** A behaviour can lose; the recorded death has the survival behaviour at 0.000 while guard held. Unexamined: whether this can coexist with the urgency ladder, or replaces it.

- **Follow where the player is going rather than where the player is.** Unexamined: how to read heading without producing a companion that looks erratic, and how to weigh a companion that is already in the right direction against one that is not.

- **Record where the companion and the player have been.** Named in the code as the missing component behind two deferred pieces of work: hunting as exploration, and the rule that a one-way edge may be taken only after the player has taken it.

- **Record brightness as a map rather than a reading.** Named above as the reason travelling toward darkness cannot currently be attempted.

- **Let the companion clear a small amount of ordinary terrain to reach ore it has already chosen.** Unexamined: the interaction with home protection, and how to bound it so it does not become tunnelling.

- **Give a running task a way to report that it is failing.** The 0.15.0 regression was an attempt to infer this from the body's movement, and inferring it from outside is what made it worse. Unexamined: whether this is needed at all, given that a task correctly chosen should rarely fail, which would make the fix belong to choosing rather than to reporting.

---

## Building

Navigation retains unfinished search while executing useful movement, and successful traversals become bounded route memories in the world save. Following recognises horizontal and vertical separation, while completed route steps measure progress through detours. A distant companion following normally can fly continuously back through terrain; combat and work cannot start that recovery, and flight never becomes learned route experience. Short movement searches handle local clearance and underwater escape. Protection retains relevant threats and considers the time needed to remove them. Ore jobs retain their vein through interruptions, revalidate approaches, and never dig ordinary terrain to reach ore. These capabilities have headless regression coverage; difficult caves and comfortable behaviour still need playtesting.

From this folder, `sh Tools/verify.sh` compiles against the installed tModLoader and checks the movement boundary. With the game closed, `dotnet build` also packages the mod for the next launch. A fresh launch avoids the unresolved in-game reload hang.

The complete feature lives under `Companion/`, grouped into its brain, character body, weapons, inventory, player and enemy integration, map integration and HUD. Movement is shared by travel, work and combat through `Companion/Brain/SharedMovementSystem`. Its Terraria integration predicts using the game's collision helpers; `Tools/EngineReplay` compares those predictions with the engine's own NPC collision routine without opening a window. `Tools/NavReplay` exercises recorded terrain scenarios and the movement controller.

The tModLoader mod settings separately control the brain inspector and local telemetry. Both are enabled by default in this development build. Bind **Brain inspector** under Controls → Mod Controls; its default is `[`, but an older saved binding must be changed there. The key opens a layer menu for enemy forecasts, projectile paths, route steps, aiming and movement alternatives, attention and decision scores. Route drawing is selected initially. Closing the menu dismisses only the panel — the layers you selected keep drawing over the world, so you can watch the brain without the menu covering it. Untick **Show world drawings** to hide them all. Your layer selections are saved with the character.

When telemetry is enabled before entering a world, the session records player and companion movement, decision scores, execution and rejection state, actual pickups, projectile launches and contacts, effective damage, and rolling terrain snapshots. Files remain local under the tModLoader save directory's `ModSources/AICompanion/Telemetry` folder; nothing uploads automatically. For a bug report, leave the world and send the files sharing that run's timestamp. Metadata includes game, loader and loaded-mod versions. Disabling recording closes the current files immediately; enabling begins on the next world entry.

After leaving the world, `dotnet run --project Tools/SessionReport -- --timeline Telemetry` prints the chronological record followed by diagnostic findings. Narration is optional. The reader reports capture gaps and distinguishes observed events from inferred hesitation or causes; locally sampled terrain and recorded game state are not a video replay.

For several runs, use `dotnet run --project Tools/SessionReport -- --multirun Telemetry` or pass selected TSV paths. Add `--html /tmp/companion-playtests.html` in place of `--multirun` for a self-contained inspection page with a run selector, time scrubber, event filters, sampled state and causal event details. Rapid load attempts keep separate files, and metadata survives a zero-tick run. Unreadable captures remain explicit gaps. The page reports its sampling limits; full-source reports retain all recorded rows for diagnosis.
