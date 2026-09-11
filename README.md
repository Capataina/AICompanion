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

A blue slime appears at the left edge of the screen, bouncing away from you, and the companion does nothing about it at all. It has looked at that slime and worked through it honestly: there is no way to hit it from here and there is no way for it to reposition itself easily like by jumping or going on a near hill to hit it either, and the only thing that would put it in range is a long walk to the left — a walk that ends with the companion thirty tiles behind you and a slime dead that was never going to reach either of you. It is not worth doing, so it is not done, and the companion does not fidget about it or half-start toward it.

Change one thing and the answer changes with it. Stop walking, turn to a tree and start swinging, and the same slime becomes worth killing, because now the walk costs nothing — you are not going anywhere, and the companion will be back at your side before you have finished the trunk. Change a different thing instead: leave the slime where it is but have it bouncing toward you rather than away, and it becomes worth killing immediately, because it is going to arrive whether anyone walks anywhere or not. The companion draws and shoots it, and it does this while still walking, because aiming and walking are separate things and it has never had to stop doing one to do the other. The only thing the companion does differently however is while walking to the right, it loses line of sight for the slime and to be able to still kill the slime, it jumps and shoots from midair to hit the slime and kill it. You do not break stride. Neither does it. What it never does is put itself between you and something to soak a hit; it is a fighter standing where it can shoot, not a shield. Although, after you start breaking a second tree, it looks around and notices that the nearest threat, another slime, is so far that it would realistically not reach you any time soon but the companion doesnt have a clear line of sight regardless of what small move it can make. The companion then decides, that instead of a grand repositioning just to kill a "not so urgent" threat, it joins you on chopping wood.

### 4:00 — Trees, and reading where you are going

You walk past a single oak and neither of you stops, because one tree is not worth breaking a journey for. Two minutes later you pass a stand of four grown together, and this time the companion peels off, chops the nearest trunk down, sweeps up the wood and the acorns, and comes back — eight seconds gone and twelve tiles to make up, which it makes up without fuss. What decided the difference was not a rule about how many trees are enough; it was that four trees together are worth more than the twelve tiles, and one is not. The tradeoff the companion sees there is not a hard gate. It does not have a deterministic rule that defines how many trees are worth it to mine, or how far it can be from you, or any other similar calculation. It simply evaluates all options and decides that it is the best option at the time.

While that is happening the ground ahead splits into a high ledge and a low path, both running right, and you jump up to the ledge. The companion stays on the low path and keeps pace. It has not lost you and it is not confused: you are going right, the low road goes right with less climbing, and fifty tiles on the two paths meet and you are side by side again. Then you change your mind, turn, and drop into a ravine, and the companion comes down with you straight away — down the near wall, which happens to be the easier side, so it reaches the bottom before you do. You think better of it and climb back out, and it is out before you are.

There is a way to get this wrong, and it matters that the companion does not. Reading where you are heading is not predicting where you will be in five seconds and sprinting there. If you are walking right and the companion is on your left, and you turn left for a moment to grab something, it does not go charging away to the left — it is already on your left, it is already where that brief turn was pointing, and a moment's turn is not a change of plan. It simply keeps walking with you, and when you resume right, nothing has been lost and no ground has opened up between you. It hurries only when it is genuinely far from where you are going.

In another scenario, you and the companion walk to the right again. You jump up to the high ledge and the companion continues from the low path, both running right still. But then the companion realises that the low path leads to a mine which means the companion wont be able to follow you through the path once it hits the end of the low path, so it reroutes, walking back out of the low path, climbing the high ledge with you and then tries to match you in terms of location. The companion is not omniscient, it simply didnt know that the low path wasnt going to come out on the other side, nor did it know that your intention was to come out of the other side, as it doesnt have some form of future vision, it just knew you were walking to the right and assumed that was your intention and tried to match it as best as it could.

It was very well likely that the companion reads your intention as climbing the high ledge and just climbs with you, preventing the entire 2 different ways divergence. This goes to show how undeterministic the agents actions are. The same exact scenario with even the slightest different tunes; maybe your momentum or the way you jumped or the way you moved and so on or even the companions own position, where the enemies are and everything might influence the decision the companion makes in terms of how it follows you, and in this scenario, it either comes up the ledge with you or continues from the low path, but still makes its way to the right side regardless the way it chooses. 

### 6:00 — The cave, and going in without you

There is a hole in the ground ahead and the inside of it is dark. The companion is aware of that darkness the way it is aware of the trees — as a place with a property, twelve tiles wide and unlit, sitting over there — rather than as something it notices only once it is standing in it. It is already holding a torch by the time you reach the lip, though it places nothing, because the surface is bright and there is nothing here to light.

You keep walking along the surface and go straight past the hole. **The companion goes in anyway.** It drops in, lights the first chamber, comes back out and catches up with you on the surface, and at no point did it look to you for a decision. Had you gone in instead, it would already have been down there ahead of you doing the same thing. Keeping the place lit is its own responsibility and it discharges it whether or not you are participating; you have never once had to place a torch to make it place one.

This really highlights the agency of the companion. The companion doesnt simply follow you around, it makes decisions of its own. At this given time, it weighed all its options and decided that going into the cave to light it up would be within budget and it wouldnt stray away from what it thought the players intended action was. Even if the players intended action really did stray away from what the companion was doing at the time, it was okay because the companion weighs the options and as already clarified, decides that it simply has the time to do so, regardless of what decision the player might make.

### 8:00 — Crevices, and who gets the light first

This time you do go down, and you head along a cave system that runs mostly sideways, with narrow side passages branching off it into the dark every so often. The companion ducks into them. It goes in, lights what is in there, and climbs back out to rejoin you — and before it goes in at all it has made certain it can get back out, so a crevice that is a one-way drop gets lit from the lip or not at all. It is never trapped by its own curiosity.

Which dark it deals with first depends on you, and it reads that sensibly. If you are down here carrying nothing that gives off light, standing in your own small patch of black, then you are the dark that matters and the crevices can wait; the thing that gets someone killed is not seeing the skeleton beside them. If you are carrying a torch or you have thrown a glowstick ahead of you, your own bubble is handled, and the companion takes itself off on a proper expedition — the crevices, the corridor ahead, the chamber you have not walked into yet. Either way what it is pursuing is the same thing: that this cave is lit.

Twenty tiles further on there is a fourth passage and the companion does not go into it. It has looked at the entrance the same way it looked at the other three, and the answer came back differently: the floor drops away just inside, far enough that it could get down and not back up, and there is no ledge partway and nothing to climb. So it stands at the lip and places a torch from there, which lights the first part of the passage and leaves the back of it dark. That is not a failure and the companion does not treat it as one or hover at the entrance reconsidering. A passage half lit that it can walk away from is worth more than a passage fully lit that it is standing at the bottom of, and it makes that trade without hesitating and moves on.

Change what is in the passage and the answer moves again. Suppose there is something alive down there — a bat, say, awake and coming up toward the mouth. Now there are two reasons to deal with that passage rather than one, and both of them point the same way, so it deals with it: it backs off to where it has a clear line up the shaft and kills the bat on its way out, then lights what it can see. But suppose instead the thing down there is a slime sitting at the bottom of a passage narrow enough that getting to it means standing next to it. Then the second reason points the other way from the first, because a fight at arm's length in a hole it cannot climb out of is exactly the situation it has spent the last minute avoiding. It lights the mouth of the passage and leaves the slime alone, and it is content to do that.

None of this comes from a rule about crevices. There is no threshold for how dark a place has to be before it is worth a trip, no maximum depth, no list of which enemies are safe to follow into a hole. Each passage is simply one of the things the companion could be doing at that moment, weighed against walking with you, against the corridor ahead, against the bat, against standing still — and it picks the best one and then picks again a moment later, which is why the same passage can be worth entering at one moment and not at the next without anything about the passage having changed at all.

### 10:00 — Copper, and being sure

There is copper in the wall to the left, six tiles up a slope, and the companion breaks off what it was doing and goes for it — not because ore is sacred, but because a vein it can actually get to is worth more than another torch right now.

The important part happens before it moves. It makes certain it can get there. Not nearly certain, not optimistic, not "it looks close enough" — it establishes that it can stand somewhere it can swing from, and only then does it commit. Where it can get partway and no further, it goes as far as it is sure of and works the question out again from there, which often changes the answer, because being closer makes things clearer. Where it cannot be sure it can arrive, **it does not choose the ore at all.** There is nothing to abandon and nothing to be stuck on, because it never went. The vein behind two metres of solid dirt is simply not something it is considering, in the same way a slime on the far side of a wall is not something it is considering.

Once it is there it swings until the vein is gone, and when the last piece of it is out it picks up what fell. There is copper two tiles above the ledge it is standing on, higher than it can reach, and it jumps and swings at the top of the jump. That is not a special trick for ceilings — getting a tool onto something includes every way its body can move, and jumping is one of those ways, exactly as it is when the question is how to line up a shot.

There is more copper across the chamber, and this time the answer is no. There is a gap in the floor between here and there with nothing to cross it, and the far side is a tile too high to reach from a standing jump on this side. The companion does not walk to the edge of the gap and stand there looking at it, which is the thing that would read as broken; it simply never chose that vein, so there is nothing to abandon and nothing to be stuck on. It goes and does the next best thing available, which turns out to be an unlit corner behind it, and the copper across the gap stops being part of its thinking entirely.

Then you walk over and knock out two blocks to bridge the gap, and within a second or two the companion is on its way across. Nobody told it the world had changed and it was not sitting there hoping. It re-asks what is worth doing constantly, and what is worth doing is a function of what is actually there right now, so a wall coming down and an enemy arriving and a vein running out are all the same kind of event to it: the answer to "what now" came back different from the last time it asked.

The same thing runs the other way. Halfway up the slope to a vein it can reach, a skeleton walks into the corridor between it and the ore. The companion does not carry on to the vein and get hit on the way, and it also does not treat the interruption as the end of the job — it deals with the skeleton, and then, because the vein is still there and still worth having and it is still nearly on top of it, it carries on up the slope and mines it. Had the skeleton instead been three of them, arriving from the direction it would have to retreat toward, the vein would have lost outright and it would have backed off down the slope and fought from there. Neither of those is a rule about skeletons. They are the same question asked twice with different numbers in it.

The part of this the companion is strict about is the difference between *no* and *not sure yet*. It treats those as completely different answers. A vein it has established it cannot reach is out of its thinking. A vein it has not yet worked out is not a vein it walks at hopefully; if being closer would settle the question, it gets closer as part of doing something else it was going to do anyway, and asks again from there. What it never does is commit to a journey on the strength of not having ruled it out.

### 12:00 — One thing after another

You are at a chest and stationary. Ahead of the companion, in a line, there is a pot on a ledge, an unlit corner above the pot, and a vein ten tiles past both. It wants the vein, and getting to the vein takes it right under the other two.

So it breaks the pot on the way, with whatever weapon is in its hand — a thrown knife if that is what it has, a sword swing if that is what it has, because a pot is a thing you hit and not a job you travel for. The contents drop at its feet and it scoops them up without breaking stride. The corner is one hop above the path, so it hops and lights it. Then it arrives at the vein and starts mining. From the outside this looks like a plan, four things done in sensible order, and it is nothing of the kind — each of those things was nearly free because the journey was already being made. This is how it behaves constantly. Terraria is a game of doing three things on the way to a fourth, and the companion plays it that way.

Halfway through the vein a bat comes in through a gap in the ceiling and makes for you. The companion stops mid-swing — the swing is not sacred either — kills the bat, and goes straight back to the same vein and finishes it. That return is seamless. It does not stand around reconsidering its life, and it does not wander off to something else on the way back; the vein was the best thing available before the bat and it still is.

It matters what it does *not* do on that trip out. It does not walk past the pot on the way to the ore and come back for it afterwards, which would be two journeys where one would do. It does not stop at the pot, break it, go all the way back to you to drop things off, and set out again. And it does not decide that because it is currently interested in ore, the pot and the dark corner are somebody else's problem. Nothing in its head is organised as a task it is in the middle of; there is only what is worth doing right now, and a pot that costs one swing because it is already standing next to it is worth a great deal for what it costs.

Move the pot and the answer changes without anything else changing. Put it twenty tiles off the route, up a ledge, and the companion walks past the ledge and does not go up it — not because pots stop being worth breaking at some distance, but because that pot now costs a climb and a climb back and the ore does not. Move it back onto the path and it is worth it again. The unlit corner behaves the same way: one hop above the route it takes it, four tiles off to the side down a dead end it leaves it, and if you stand in that dead end a moment later it becomes worth lighting again because now there is a second reason.

Change the timing instead of the geometry. If the bat had come in while the companion was still walking toward the vein rather than mid-swing, the pot and the torch would simply not have happened — it would have turned and dealt with the bat from wherever it was, and picked the journey up afterwards with whatever was still on it. If the bat had come in and you had killed it yourself before the companion got there, it would never have broken stride at all. And if by the time it finished with the bat you had walked forty tiles further on and the vein was now well behind it, the vein would have lost to catching up with you and it would have left the copper in the wall without any sense of having failed at something.

From the outside all of this looks like a plan being executed, and it is worth being clear that it is not one. Nothing sequenced those four things. Each was chosen on its own, a moment apart, and the reason they came out in a sensible order is that being nearly on top of something is most of what makes it cheap. Terraria is a game of doing three things on the way to a fourth, and the companion ends up playing it that way without ever holding a list.

### 14:00 — The slimes it was ignoring

Three slimes have been audible for a while in a chamber off to the side, and the companion has done nothing about them the whole time, because they cannot get out and it has been busy with things that were worth more. Now the vein is gone, the drops are collected, the corner is lit, there is nothing else on the floor, and you are still standing at your chest. With everything better used up, the slimes are now the best thing there is, and the companion goes and deals with them.

It gets itself somewhere it can shoot from and starts putting damage in. They come at it, and it backs off as they come — over them where the ceiling allows, under them where the floor does, sideways along the corridor otherwise — and it kills all three without taking a hit. It collects what they drop. Then it looks around again, finds a lit cave with nothing on the floor, no ore, nothing alive and a player who has not moved, and comes back to stand near you. Nobody told it to do any of that in any particular order; the order fell out of what each thing was worth at the time.

The interesting thing here is the two minutes when it did nothing about them, because that is the part that usually reads as broken and is in fact the point. It was aware of those slimes the whole time. It did not forget them, it was not unable to see them, and it was not waiting for a signal. They were simply worth less than a vein of copper and less than an unlit chamber and less than staying near you, and a thing worth less than the thing you are doing is a thing you do not do. The moment everything above them was gone they rose to the top without any change in how it felt about slimes.

Change the circumstances and the timing moves. If one of those slimes had found its way out of the chamber and started bouncing toward you while the companion was still mining, it would have left the vein for that one and gone back to the vein afterwards — not because a slime outranks copper, but because a slime that is arriving is a completely different thing from a slime that is sitting in a sealed room. If you had got up from the chest and started walking on while it was fighting them, it would have finished the fight only if finishing was quick, and otherwise broken off mid-fight and come with you, leaving two slimes alive in a room it has no further interest in. And if the chamber had been sealed in a way it could never open, the slimes would have stayed at the bottom of its list for ever and it would have gone and found something else to do rather than pressing at the wall.

There is no stage it moves through here and no list it works down. "There is nothing else to do so I will go and clear that room" is not a decision it makes; what happens is that the value of clearing the room stays exactly where it was all along while everything above it disappears, and the top of the pile changes hands without the pile being reordered.

### 16:00 — Everything at once

Terraria does not hand you one problem at a time, and this is the part that matters most.

You are up on a dry ledge on the left. Below and to the right is a water pool with two jellyfish in it, there is unmined ore in the floor of that pool, two zombies are closing from the right on the far side of the water, and the whole area is half-lit. The companion has just dropped into the water going after that ore, so it is now standing on the thing it wanted, underwater with a breath running down, within reach of two jellyfish, with zombies inbound and you dry and safe twenty tiles away.

It does not freeze and it does not try to finish what it started. It leaves — out of the water, to the left, up onto your ledge — and it does that not because you are there but because that is the only place in this scene that is not actively costing it. Standing on the ore means drowning slowly while two things hit it, and a copper vein is not worth that. Once it is out and dry, the jellyfish stop being a problem at all, instantly and completely, because jellyfish cannot leave water and the companion is aware of that. It turns and starts shooting the zombies as they come, giving ground to the left to keep the gap open.

Then a bat comes in from the left, behind it, and it is pinched. What it does here is the whole character of the thing. It kills the bat first — not because bats are more dangerous than zombies, but because the bat is sitting in the direction it was retreating, and an escape route is worth more than a zombie. Set the same scene slightly differently — zombies already on top of it, bat still distant — and it kills the zombies instead and gives ground to the right, away from the bat. Same reasoning, opposite action, and nothing in it was decided in advance.

Giving ground to the right means the water. It works out that it can clear the pool in a single jump, so it jumps it rather than wading through it, and while it is in the air the ceiling lip cuts its line to the bat and leaves it with half a second and nothing to aim at — so it puts a shot down into the jellyfish below on the way over. It lands on the far side, kills the bat, and then stands on that ledge and shoots the jellyfish dead from above, dry and out of reach the whole time. With the pool safe it drops in, mines the ore it came for in the first place, and collects the lot on its way back up — jellyfish drops, zombie drops, the bat, the ore — and comes back to you.

Three things it did not do are as important as what it did. It did not finish the ore. It was standing on it, it had wanted it thirty seconds earlier, and it left it — because a vein is worth a fixed amount and standing in water being bitten is not, and no amount of being partway through something makes finishing it worth more than it is worth. It did not run to you for safety, either; it ended up on your ledge, but it went there because that ledge was dry and out of reach of everything, and it would have gone to an identical ledge in the opposite direction just as readily. And it did not stop shooting while it was retreating. Backing out of the water, it was still putting arrows into whatever it had a line to.

Now change the water. Make it shallow enough to stand in with its head clear, and most of this scene evaporates: there is no clock, so the ore is worth finishing, and the companion stays in the pool, kills the jellyfish from where it stands because they are now the nearest thing that can hit it, and mines. The zombies arriving change that only when they arrive. Make the water deep but remove the jellyfish, and it stays a while longer — the clock is real but nothing is hitting it, so there is time for a few swings before it has to surface, and it takes them and comes back down after.

Change who is in the water instead. If *you* had dropped into that pool with two jellyfish and the companion were dry on the ledge, it would not jump in after you. It would shoot the jellyfish from the ledge, which is where it can hit them and they cannot hit it, and it would do that faster than it does anything else, because the thing hurting you outranks the thing hurting it. Joining you in the water would put a second body in the same trouble and remove the only angle from which the problem could be solved.

And change the bat's arrival to a moment later. If it had come in after the zombies were already dead, the companion would simply have shot it from where it stood and gone back to the ore, and none of the jumping and repositioning would have happened at all. Nothing about the bat decided that sequence; the sequence came from the bat arriving at a moment when it was already backing toward where the bat was. That is the character of the whole scene — every move in it was the best available answer to a question that was different one second earlier, and none of them were chosen in advance.

### 18:00 — The drop it should not have taken

You jump down a shaft and it follows you in. Halfway down it registers what is at the bottom: water, two jellyfish, no ledge within reach. It throws in a second jump in mid-air and pulls itself sideways onto a lip it can hold — because by this point in its development it has more ways to move than walking and jumping, and every one of those is part of what it considers possible when it commits to anything.

That last part is the load-bearing half of this scene. What the companion is willing to commit to is a function of what its body can currently do, and that changes as it grows. Early on, a shaft with a bad bottom and no ledge is a shaft it does not enter, because entering it means landing in whatever is down there. Once it has a second jump, the same shaft is fine, because there is now a way out partway down and it knew that before it went in. The judgement did not change and the shaft did not change; the set of things it can do changed, and every decision that rests on "can I get out of there" quietly moved with it. It should never be the case that it has an ability and does not use it when choosing, or that it plans as though it has one it does not.

If the second jump does not save it — the lip is too far, or it has already spent the jump — it takes the landing and immediately starts backing away from the jellyfish, to the left if they are on the right, rather than standing in the water being bitten while it works out what to do. Backing away is not a retreat to somewhere in particular; it is putting distance between itself and the thing hitting it while it looks for the real answer, and the real answer here is usually the bank.

Put the jellyfish on both sides and it changes again. Now backing away horizontally buys nothing, so the answer is upward — a jump toward the nearest lip whether or not that lip is a place it wanted to be, because anything dry beats anything wet in this situation. And if there is no lip at all and the shaft is a sealed column of water with two jellyfish in it, then the honest answer is that it is in trouble, and what it does is kill them, because in a hole with no exit the only way to stop being bitten is for the biting to stop. It should reach that conclusion quickly rather than spending its breath swimming at walls.

### 20:00 — Knocked in

You are walking right along the top of a deep pit with ore visible at the bottom of it. The companion looks in, works out that it could get down there but not back up, and stays out — the ore is real and so is being stuck, and being stuck costs more.

A bat clips it and knocks it in anyway. It does not announce that it is stuck. It looks around at where it now is and makes the best of it, exactly as a person would: it mines all the ore, kills whatever is down there with it, lights the place, picks up every last thing that fell. Only when there is genuinely nothing left to do does it turn its attention to getting out, and it keeps working at that as the world changes around it — and if you have walked far enough away by then, it comes to you regardless. There is no point at which it stands still doing nothing while there is something to do.

The two halves of that are worth separating, because they look contradictory and are not. Before it was knocked in, the ore was not worth the pit. After it was knocked in, the ore is worth everything, because the cost that made it a bad idea — getting stuck down here — has already been paid and cannot be un-paid by declining the copper. Nothing about its assessment of the pit changed. What changed is which options are actually on the table, and the option "stay out of the pit" stopped being one of them.

Take the ore out of the pit and it behaves the same way for a shorter time. Knocked into an empty hole, it looks around, finds nothing to mine, nothing to light, nothing to pick up and nothing alive, and turns to getting out immediately — which is the same behaviour, not a different one, because "make the best of where you are" resolves in a second when there is nothing here.

Now come and stand at the lip. If you break two blocks into the side of the pit and open a way up, it takes it as soon as it is there, mid-swing if need be. If instead you stand at the top doing nothing, that changes nothing at all — it does not look up at you expectantly and it does not stop working because you have arrived. And if you walk off while it is still mining, it keeps mining, because you walking away is not yet a reason to stop; only you walking far enough away is, and when that happens it abandons the copper without ceremony.

The thing it must never be is idle and trapped at the same time. Being stuck and being out of things to do are two separate facts, and it only says the first one out loud once the second is also true.

### 22:00 — Somewhere it cannot follow

You take a route it cannot take. It does not stand at the entrance waiting for you, because it is not a dog and waiting is not a thing it does. It gets on with whatever is where it is — the trees on this side, a slime, drops on the floor, a dark corner — and it does that for as long as there is anything worth doing. When there is nothing left, and only then, does the distance between you become the thing that matters, and it comes and finds you.

The clearest version of this is the surface. You have wings and you fly up to a floating island; the companion does not have wings and cannot get there. What it does *not* do is hover at the highest point it can reach, looking up, doing nothing for the four minutes you are gone. It goes back down and chops the trees, kills the slimes that wander past, picks up what they drop, lights whatever needs lighting. From the ground it looks like a companion getting on with its afternoon, which is exactly right, and when you come down it is there with a bag of wood.

Where there is genuinely nothing — a bare stone corridor with no trees, no enemies, nothing on the floor and nothing dark — then the distance to you is the only thing left on the table, and it acts on that immediately rather than after a delay. It does not have a waiting state to fall into. "Nothing to do" and "go to the player" are the same answer arrived at from two directions.

An enemy arriving changes it back. Something wanders into the corridor while you are still up on the island and the companion deals with it, then returns to the question. That is not it forgetting about you; the distance to you was the best thing available for as long as nothing better existed, and something better existed for thirty seconds.

The thing to be clear about is that none of this is a special mode for being separated. It is the same weighing it does when you are standing next to it, running with one of the options — being near you — worth a great deal more than usual because it is a long way off.

### 24:00 — Home, and getting out of the way

You head back with a full bag and it comes with you, sweeping up anything it passes on the route. It picks up everything. A single block of dirt on the floor is worth the two steps, because it has no way of knowing what any given thing is worth to you and the wrong guess costs more than the walk.

At home you start sinking a shaft — the two-wide platform drop straight down — and the companion is standing exactly where the next platform goes. It moves. While you are holding something you can place and your cursor is anywhere near it, that cursor is a place it stays out of, so it jumps, or sidesteps, and it keeps doing that as you work your way down the shaft, out of the way each time before you click rather than after.

Swap what is in your hand and it stops caring. Holding a sword, a pickaxe or a torch, it stands wherever it likes, because none of those are blocked by a body being in the way and getting shoved around by your cursor for no reason would be its own kind of annoying. The thing it is reacting to is not your cursor; it is the fact that you are about to put something where it is standing.

The same courtesy is wider than building. It is never the thing standing in the one-tile gap you are walking through, and if it finds itself there it leaves before you arrive rather than after you have bumped into it. On a narrow ledge with a drop on either side it gives you the inside line. In a doorway it goes through first or waits on its own side, rather than meeting you in the frame. None of that is a set of rules about doorways and ledges; it is one idea — being where you are trying to be is a cost — applied everywhere it comes up.

There is a version of this that would be worse than not having it. If it took the idea too seriously it would spend the whole session skittering away from you, unable to stand anywhere because you might want to be there. What stops that is that being in your way is a small cost rather than a prohibition: it is enough to move it out of the tile you are about to fill, and nowhere near enough to make it abandon a good firing position because you glanced in that direction.

### 26:00 — The eye

Night falls and you use the Suspicious Looking Eye. The music changes and the Eye of Cthulhu comes down out of the dark.

Everything else the companion was interested in stops mattering. It is not mining, it is not lighting anything, it is not sweeping the floor, and it is not standing near you — because nothing else in the world is worth anything while this thing is alive. It has two concerns now and they are in a strict order: **do not get hit, then do damage.** In that order, and the order is not negotiable. If the only way to get the boss in range is to step somewhere it will take a hit, it does not step there; it stays out and does less damage, because it cannot do any damage at all once it is on the floor.

It also stops trying to look after you, and that is correct. Ordinarily, when something comes for you, the companion makes a judgement — that zombie is heading for him, but he can handle a zombie, so I will finish this vein and step in if it turns serious. Against a boss that judgement is meaningless, because there is no version of this where it stands between you and the Eye of Cthulhu and that helps. So it drops the idea entirely and fights.

It also stops being tethered to you the way it normally is. Boss arenas are enormous; a companion held on a short leash spends the whole fight bobbing around your shoulder instead of fighting. It ranges much further than it ever would ordinarily, with a gentle pull back that gets stronger the further out it goes, so it has room to manoeuvre without ever genuinely leaving. The same is true whenever the game itself turns into an event — a Blood Moon, a Solar Eclipse, a Goblin Invasion, a Pumpkin Moon — because those are the same situation: the world is now about one thing, and standing next to you being helpful is not it.

It is worth being precise about what "stops looking after you" means, because it is narrower than it sounds. In ordinary play, something coming for you is a judgement call with several answers in it — that is a zombie and you can handle a zombie, so it finishes the vein and steps in only if the zombie turns into three. Against a boss there is no version of that judgement that helps. It cannot stand between you and the Eye of Cthulhu and it cannot remove the threat from you by being nearby, so the entire idea is set down. What it does not set down is you being in trouble for reasons it *can* act on: if the small eyes swarm you specifically, they are things it can shoot and they are on you, and it shoots those before it goes back to the boss.

Nothing else survives the fight either. It does not mine the wall of the arena because there is copper in it. It does not stop to light a dark corner. It does not pick up the drops until the fight is over — they are not going anywhere, and a detour for loot mid-fight is how a fight goes wrong. Everything it would ordinarily do is still there in its head and is simply worth nothing at all right now, which is a different thing from being switched off.

If you die halfway through, it does not stop fighting and it does not stand over your gravestone. The boss is still alive, it is still the only thing that matters, and it keeps going for as long as it can — which is often long enough to matter, because a boss that is nearly dead when you respawn is a very different fight from one at full health.

Take the same evening and replace the boss with a Blood Moon and very little changes. There is no single enormous thing to shoot, but the world is still about one thing, so the companion is still fighting rather than mining, still ranging further than usual, and still prioritising its own survival over the last few points of damage. What tells it this is happening is the state of the world rather than the identity of the monster, which is why an invasion from a mod it has never heard of produces the same behaviour.

### 28:00 — The fight

It fights the way a decent player fights. It stays at the range where it can hit and not be hit, and it keeps moving. When the Eye stops circling and lines itself up for a charge, the companion is out of that line before the charge lands — not by recognising this particular boss, which it has never heard of, but by seeing something aiming itself and accelerating and getting off the line it is aiming along. That works the first time it meets an attack nobody has ever described to it.

When the Eye splits and starts throwing out a swarm of small eyes, it changes weapon, and it changes weapon for the right reason. Something that punches through a line of targets is worth far more against a swarm than against one large thing, and something that hits one target very hard is the reverse. It is not that it knows the Eye of Cthulhu takes single-target damage; it is looking at what it is holding, what is in front of it and how those things are arranged, and taking the better of the two. Hand it a weapon nobody has ever seen and it makes the same judgement correctly.

Hand it only one weapon and nothing about that reasoning breaks. It still asks which of the things in front of it that weapon is best used on, and it still declines shots that are not worth taking, and the answer is simply thinner. Hand it three and the same question has three answers to choose between rather than two. What it never has is a note somewhere saying that a particular weapon is the boss weapon, because the moment such a note exists the next weapon added needs one too.

If you have built platforms in the arena, it uses them, from the moment you place them. You throw down a run of platforms mid-fight and within a second they are simply part of the terrain it moves through — it will jump to one to get above a charge, or drop through one to get under a swarm, without anything being rebuilt or reloaded. Anything you build is immediately part of what it can do, and anything you destroy stops being part of it just as fast.

Its own survival keeps winning ties all the way through. At full health with a clear line it is aggressive, sits at the edge of its range and fires constantly. At a third health it is noticeably more careful — wider standoff, quicker to break off, more willing to spend a second getting somewhere safe rather than squeezing in another shot — not because a threshold was crossed but because getting hit costs more when there is less left. If you are at full health and it is nearly dead, it looks after itself, because you can take a hit and it cannot, and a companion that dies to prove a point contributes nothing for the rest of the fight.

If it does go down mid-fight, it is back on its feet within ten seconds and rejoins, so a bad moment costs you its help for ten seconds rather than for the rest of the fight.

### 30:00 — After

The Eye dies and the loot scatters across the arena. The companion collects what it can reach and brings it over.

The return to ordinary is immediate and complete. There is no cooldown, no winding down, no standing still for a moment — the enormous thing that was outweighing everything else simply is not there any more, and the next-best thing on the pile takes over, which in a boss arena at night is usually the loot and then the fact that half the arena is unlit. Within a few seconds it is a companion again: standing near you, looking around, and if there is a tree or a dark corner or a vein anywhere close, it has already gone to deal with it.

Summon the next boss thirty seconds later and it drops everything again just as fast, mid-swing if it is mid-swing. There is nothing to reset and nothing that has to finish first. The thing that made it fight was the boss being alive, and the thing that made it stop was the boss being dead, and both of those are facts about the world it re-reads constantly rather than states it entered and has to leave.

That is the whole character of it, and it is worth stating plainly at the end because every scene above is an instance of it. The companion never holds a plan, never occupies a mode, and never carries an unfinished task across a change in circumstances. It looks at everything it could be doing, decides which is worth the most right now, does that, and asks again — and everything that reads as intent, patience, opportunism or good judgement is that one loop running against a world that keeps changing underneath it.

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

# Behaviour By Behaviour

The three sections above are written to be read whole. This one breaks the same ground into named responsibilities so that a single question — how far off are we, and on what — can be answered at a glance and then in detail.

Each entry carries five things. **Expected** is what the companion should do, written the same way the Expected Behaviour section is written: as a responsibility, with no reference to how it might be achieved, so that the entry stays true whatever we end up building. **Current** is what it actually does, also with no reference to how, so the two can be compared without one contaminating the other. **System** is the machinery that exists today to close that gap. **Aligned** is a rough percentage where 100% means the current behaviour and the expected behaviour are indistinguishable in play. **Cause confidence** is a separate judgement, and the reason it is separate is that a behaviour can be failing while the system responsible for it is fine — kiting could be implemented perfectly and still look broken because the route planning underneath it is not — so this column says how sure we are that the system named is where the problem actually lives.

**The percentages are judgements, not measurements.** They are informed by the recordings, but only one of them — decision commitment — has a before-and-after measurement behind it. Read them as a way of ordering the work, not as data.

| Behaviour | Aligned | Cause confidence |
|---|---|---|
| Getting up after being downed | 90% | high |
| Respecting the player's base | 70% | low |
| Recovering when it cannot follow | 70% | medium |
| Weapon selection | 70% | low |
| Looting | 65% | medium |
| Staying with the player | 55% | low |
| Reporting what it is doing | 40% | medium |
| Opportunistic mining | 35% | high |
| Dodging and kiting | 35% | low |
| Breaking containers | 30% | medium |
| Protecting the player | 30% | medium |
| Traversing terrain | 30% | high |
| Deciding what counts as a threat | 25% | high |
| Opportunistic chopping | — | unmeasured |
| Lighting the area | 20% | high |
| Self-preservation | 20% | high |
| Firing position | 20% | medium |
| Committing to a decision | 20% | high |
| Knowing what it cannot do | 20% | high |
| Chaining several jobs into one trip | 15% | medium |
| Enemy selection | 15% | medium |
| Reading where the player is going | 10% | high |
| Getting out of the player's way | 0% | high |
| Boss and event behaviour | 0% | high |

---

### Getting up after being downed — 90%, high confidence

**Expected.** Being downed costs the companion its participation for a short while and then stops costing anything. The player helping speeds it up and is never required.

**Current.** It gets up on its own after ten seconds, or in three if the player stands over it.

**System.** A downed timer and a separate assistance timer in the companion's body, the second decaying at twice its gain rate so passing by does not bank credit.

**Cause confidence.** High, in the sense that this is simply done. What is not done is preventing the down in the first place, which belongs to self-preservation below.

### Respecting the player's base — 70%, low confidence

**Expected.** The companion never alters anything the player built or lives in. It can light and mine freely in the wild and not at home.

**Current.** It does not appear to damage bases, but no session has stress-tested this.

**System.** Bed-anchored room protection vetoes autonomous edits inside a detected room, with a conservative radius fallback for open or oversized structures. The veto is rechecked at the moment of the edit rather than at discovery.

**Cause confidence.** Low, because it has barely been exercised. No recorded session took place in a base.

### Recovering when it cannot follow — 70%, medium confidence

**Expected.** When the player goes somewhere unreachable, the companion works where it is until there is nothing left, and then rejoins by whatever means it has.

**Current.** The rejoining half works. The working-where-it-is half is untested in play.

**System.** A distant-follow recovery flight that starts only when ordinary following has won and the player is beyond the recovery distance, interrupts the active route and flies continuously to a clear arrival. Combat and work cannot start it and it never becomes learned route memory. Separately, a stranded companion is meant to roam its pocket looking for a way out.

**Cause confidence.** Medium. The flight has headless coverage and is convincing. The roam behaviour has never been observed in a recorded session.

### Weapon selection — 70%, low confidence

**Expected.** The companion uses whichever of the weapons it holds is best for what is in front of it right now — something that punches through a line against a line, something that hits hard against one large target — and it reaches that answer for a weapon it has never seen before.

**Current.** Hard to judge. It fires so rarely that there is very little evidence of it choosing between weapons at all.

**System.** The arsenal compares every legal weapon-and-target pairing by simulating what the shot would actually land: time-discounted effective damage, projected kills with health reserved so overkill cannot earn credit twice, harm prevented, and a small finishing bonus, over a bounded follow-up window. Piercing value comes from flying the arc and seeing what it crosses, not from counting enemies near the target. Weapons supply physical facts and never a suitability score of their own.

**Cause confidence.** Low, and deliberately so. This is the right shape by construction and it is almost certainly not where the problem is; the problem is that nothing reaches it. Fixing enemy selection would tell us far more about this than any change here.

### Looting — 65%, medium confidence

**Expected.** It picks up everything it can reach, with no judgement about what an item is worth, because in a modded playthrough that judgement cannot be made reliably.

**Current.** It loots, and reasonably often — 149 pickups in one ten-minute session, with looting selected 4% to 12% of the time. It also abandons pickups mid-approach frequently, which is the general churn rather than anything about looting.

**System.** The nearest drop that fits somewhere — the player's stack or the companion's bag — and has a standable tile beside it, with contact pickup happening regardless of what the companion is doing.

**Cause confidence.** Medium. Looting itself looks sound; what it suffers from is being interrupted, which is a commitment problem.

### Staying with the player — 55%, low confidence

**Expected.** It keeps pace, picks its own line, and is at the player's side without being underfoot.

**Current.** It broadly keeps up, and periodically stops keeping up for several seconds at a time. Recorded stretches of 194 to 325 ticks show it wanting to follow and making no route progress at all, usually with a vertical gap between the two bodies. Of 180 requests to be with the player, 7 were reached.

**System.** A follow objective with separate horizontal and vertical comfort limits plus a local sight test, so a tile that is close in a straight line but on a different floor does not count as arrival. Following is the only thing permitted to take a one-way drop, and only when the reachable region actually contains the player.

**Cause confidence.** Low. The failures are vertical and look like terrain traversal rather than the follow logic. The same stretches show the body not completing route steps.

### Reporting what it is doing — 40%, medium confidence

**Expected.** A glance at the companion tells you what it is up to, truthfully, in plain words.

**Current.** It does report, and the most frequent thing it reports is being stuck — which is honest and useless, because it is usually stuck in a situation where something useful was within ten tiles.

**System.** A status string derived from the current behaviour and the body's progress flag.

**Cause confidence.** Medium. The reporting is accurate about the thing it is reporting; the problem is that the underlying state is "stuck" far too often. Fixing the report is not the work.

### Opportunistic mining — 35%, high confidence

**Expected.** When the companion judges there is time, it goes to nearby ore and mines it — actively seeking it out rather than waiting for ore to appear next to it — and finishes the vein.

**Current.** Sometimes it goes to ore and mines the whole vein. Sometimes it goes toward ore and never arrives. Sometimes it arrives and does not swing. Sometimes it stands near obvious ore and does nothing at all. Of 8,944 ticks spent mining in the most recent session, 6,242 were spent walking toward ore it had not established it could reach and 1,794 were spent actually swinging.

**System.** A retained vein job, triggered by the mining policy and held across interruptions, that revalidates tile type, tool damage, sight and approach on every resume. Its approach question returns three answers — reachable, not reachable, or undecided — and the behaviour deliberately walks at undecided ore, on the reasoning that the search only becomes decidable by getting closer and scoring it zero would prevent the approach that resolves it.

**Cause confidence.** High. The `approaching unproven ore` status is 70% of mining time and is the system doing exactly what it was written to do; the question is whether walking at an undecided target is the right answer at all.

### Dodging and kiting — 35%, low confidence

**Expected.** While fighting, it avoids taking damage — moving away, jumping over, dropping below, breaking line of sight behind terrain, using whatever movement it has — rather than trading hits.

**Current.** It does move away from things and it does get hit anyway, including walking into enemies while travelling. It was recorded dying in a pool at 0.96 danger having attempted the same escape jump three times.

**System.** Two separate things. A reflex, run before any behaviour is chosen, predicts where hostiles and hostile projectiles will be and marks future body states unsafe; the movement system then picks avoidance controls, and the reflex owns the feet for that tick. Separately, a kiting behaviour backs away from a walker inside melee reach.

**Cause confidence.** Low, and this is the clearest case of the column being worth having. The recorded death shows a jump beginning and being cut short three times with the reflex engaged throughout, which points at the reflex pre-empting a multi-tick move rather than at the avoidance logic being wrong. Kiting itself is selected on well under 1% of ticks, so there is almost no evidence about it either way.

### Breaking containers — 30%, medium confidence

**Expected.** Pots are things it hits with whatever it is holding, on the way past, and the contents are just loot.

**Current.** It breaks pots, and it does so by travelling to them, and it can spend a very long time on one — fifty unbroken seconds on a single pot in one session, moving two tiles sideways and eleven up in that time, while thirteen reachable enemies were present and the player was fighting.

**System.** A shared executor for pots and permanent torches that discovers candidates within an envelope, caches an approach position, and now abandons the target after three seconds of covering no ground, deferring that tile for thirty.

**Cause confidence.** Medium. The abandonment path was added after the session that produced the fifty-second figure and has not been observed in play. What remains wrong regardless is that a pot is treated as a destination rather than as something to hit from where you are.

### Protecting the player — 30%, medium confidence

**Expected.** When something is coming for the player, the companion goes and deals with it, judging whether the player needs help at all rather than reacting to every enemy.

**Current.** Guarding is one of the most-selected behaviours — 9% to 38% across sessions — and during it the companion frequently does nothing observable. One stretch has it guarding for 1,717 ticks, nineteen tiles from a player who was fighting ten enemies, firing nothing.

**System.** Guard scores the observed protection urgency, which compares when the enemy will arrive against when the companion could intervene, and asks for a position within a wide band of the player with a standoff from the threat. Its anchor now walks toward the threat, clamped to a leash around the player, rather than sitting on the player's own position.

**Cause confidence.** Medium. The scoring and the anchor look right. The visible failure is that it stands in the right area and does not shoot, which is enemy selection rather than guarding.

### Traversing terrain — 30%, high confidence

**Expected.** It gets where it decided to go. Jumps land and hold, drops land where intended, and it knows in advance which of those it can do.

**Current.** Fifty-three of 323 jumps completed. Thirty-two of 213 drops. One thousand three hundred and five of 7,372 walks.

**System.** One class per kind of move, each containing both the proof and the performance so the two cannot drift apart, over a tile graph searched by A*. Every move is re-proven from the body state the engine actually left before it is executed. A running jump's arc is proven from the take-off the run-up actually reaches rather than the profile's nominal speed.

**Cause confidence.** High, in the sense that this is where the failures land. It is also the most heavily worked part of the codebase and carries the most recorded dead ends, so "the system is wrong" is a weaker claim here than the numbers suggest — many of those interruptions are ordinary replanning rather than physical failure.

### Deciding what counts as a threat — 25%, high confidence

**Expected.** Something that cannot reach the player or the companion is not a threat, whatever it is doing on screen.

**Current.** Almost everything counts. Across 37,584 ticks with hostiles present, there was not one tick where the companion judged zero of them reachable.

**System.** A bounded search of at most 400 steps returns one of three answers — a route exists, no route exists, or nothing was established — and the threat sense rounds "nothing established" to reachable. The stated reasoning is that a threat wrongly ignored costs the player a hit while one wrongly feared costs a little caution.

**Cause confidence.** High. The reasoning is correct for deciding what is dangerous and is the exact opposite of correct for deciding what is worth going after, and the same answer is used for both.

### Opportunistic chopping — unmeasured

**Expected.** The same as mining, for trees.

**Current.** Unknown. Chopping was selected for 0% of all four recorded sessions — but every one of those sessions was underground, where there are no trees, so this is an absence of evidence rather than evidence of failure.

**System.** The same policy structure as mining, through a tree finder and an axe, with the trunk bottom classified for radius and home protection while the standing spot stays a separate movement question.

**Cause confidence.** Not assessable. A surface session would settle it in ten minutes.

### Lighting the area — 20%, high confidence

**Expected.** The companion keeps the place lit, travelling to dark regions for that purpose and prioritising the player's own surroundings when the player is the dark part.

**Current.** It places torches occasionally and only where it already is. It cannot travel toward darkness because it has no idea where darkness is. Torch placement fell from 19% of one session to 4–7% of the three since.

**System.** A single brightness reading at the companion's own position, excluding its own glow, with hysteresis and a minimum hold. Placement uses the game's own Smart Cursor torch rules including spacing, prefers elevated sites and consumes an item only after a tile actually appears.

**Cause confidence.** High, and this is a missing component rather than a wrong one. Nothing in the repository records brightness anywhere except where the companion is standing, so travelling toward dark is not something the current system does badly — it is something it cannot express.

### Self-preservation — 20%, high confidence

**Expected.** It is very hard to kill. It outranges nearly everything before hardmode and can always break off, so dying should require something unusual.

**Current.** It died once in ten minutes, in a pool, at 0.96 of its own danger reading, while guarding, having never once chosen to save itself — the behaviour that exists to do that scored 0.000 on every tick of its death.

**System.** A survival behaviour scoring observed personal danger against the geometric time to reach air relative to remaining breath, scaled by an urgency constant sized to outrank a committed guard.

**Cause confidence.** High. A behaviour that scores zero throughout the death it exists to prevent is either not being asked the right question or is asking one too narrow — its score is built mainly around drowning, and the recorded death was drowning-adjacent but fundamentally about being hit.

### Firing position — 20%, medium confidence

**Expected.** When it wants to shoot something it cannot currently hit, it goes somewhere it can.

**Current.** It asked for a position with a line of fire 594 times and reached one 45 times.

**System.** Candidate tiles around the anchor are scored cheaply on band, danger, openness, travel bias and a straight-ray sight test, and then a small fixed budget of the best few are given to the real trajectory solver. A spot with no firing solution scores almost zero and gets no incumbency preference.

**Cause confidence.** Medium. The shortlisting carries sight information deliberately, which is the right fix for the obvious failure mode, so the low arrival rate is as likely to be travel failing as selection failing.

### Committing to a decision — 20%, high confidence

**Expected.** Having chosen something, it sticks with it unless something genuinely better turns up. It looks decisive.

**Current.** It re-chooses every 22 ticks, and 86% of its choices last less than half a second. Churn has worsened at every version measured.

**System.** A flat multiplier of 1.15 applied to whatever is already running, so that a near-tie does not oscillate.

**Cause confidence.** High, and this is the only entry in the table with a measured before-and-after: making that multiplier conditional on the body's progress took churn from 481 switches to 1,727 in a comparable session, with 69% of the new switches firing on a tick flagged as stalled. It has been reverted. The remaining 55-ticks-per-decision baseline is still far short of what the expected behaviour needs.

### Knowing what it cannot do — 20%, high confidence

**Expected.** It commits only to things it has established it can do, so it is rarely in the position of failing at something.

**Current.** It routinely commits to things it cannot do — walking at ore whose reachability is undecided, hunting enemies no position can shoot — and the visible result is a companion pressing at walls.

**System.** Reachability is genuinely three-valued and every call site names its own rounding, which is the right structure. The roundings chosen are the problem: the threat sense treats undecided as reachable, and mining deliberately walks at undecided ore on the reasoning that only getting closer can resolve the question.

**Cause confidence.** High. Both of those are conscious decisions with written reasoning behind them, and both produce the behaviour the player complains about most.

### Chaining several jobs into one trip — 15%, medium confidence

**Expected.** Things on the way get done on the way. A pot, a torch site and a vein in a line are one journey.

**Current.** They are separate competitions, won and lost independently, so the companion crosses the same ground repeatedly or abandons the journey partway.

**System.** None, and that is the finding. Every behaviour is scored as though the body were already where the work is; how far away it is affects the score only through the leash and the return-time discount, not as a cost of the job itself.

**Cause confidence.** Medium. This is an absence rather than a defect, and whether it is the right absence to fill depends on decisions not yet made.

### Enemy selection — 15%, medium confidence

**Expected.** Of the things it could shoot, it shoots the one worth shooting: what is attacking the player, what is about to reach it, what it can finish.

**Current.** It has no target selected on 60% to 71% of every session, at every version measured, regardless of how many enemies are present. Fired shots account for 0% to 1% of ticks.

**System.** The arsenal picks the target and weapon together, by expected landed outcome, and re-validates Terraria's own chase predicate at retention, evaluation and firing so an invulnerable hostile cannot attract a shot. A failed trajectory trace is cached only against an unchanged muzzle, target pose and terrain revision.

**Cause confidence.** Medium. That the number is identical across four versions and two major reworks says the cause is upstream of the arsenal's own choosing — most likely in what reaches it as a candidate, or in the trajectory solve refusing arcs it should accept. This is the single highest-value thing in the table to investigate, because it gates weapon selection, firing position, protection and hunting all at once.

### Reading where the player is going — 10%, high confidence

**Expected.** It moves with the player's heading rather than toward the player's feet, which lets it take a different route to the same place and sometimes arrive first.

**Current.** It follows the player's position. The only sense in which heading enters is a predicted lead used to smooth sustained travel.

**System.** A follow objective built around the player's current feet, with a predicted location used for lead and dropped whenever a climb, fall or collision makes the prediction unusable.

**Cause confidence.** High. This is close to unbuilt rather than built badly.

### Getting out of the player's way — 0%, high confidence

**Expected.** It never occupies the tile the player is about to place a block in, or the gap the player is walking through.

**Current.** Nothing of this exists.

**System.** None.

**Cause confidence.** High, trivially.

### Boss and event behaviour — 0%, high confidence

**Expected.** During a boss or a world event the companion drops everything, stops trying to protect the player, ranges far wider, and optimises its own survival first and damage second.

**Current.** Nothing distinguishes a boss fight from ordinary play. No session has recorded one.

**System.** None. There is no notion of a boss, an event, or a wider leash anywhere.

**Cause confidence.** High, trivially. Worth noting that much of what this needs — survival outranking damage, weapons chosen by property, positions chosen for a line of fire — exists already and is not boss-specific, so the gap may be smaller than 0% suggests.

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
