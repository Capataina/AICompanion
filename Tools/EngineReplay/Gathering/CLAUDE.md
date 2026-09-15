# Gathering fixtures — ore, trees, and who is credited for the work

Three files, all driving the whole brain against native tiles, and all of them diffing or counting what the world actually lost so that "it mined" cannot be satisfied by an intention.

```
Gathering/
├─ CLAUDE.md
├─ VerifyOreWork.cs              ore jobs end to end: approach, reach, seals, attribution, departure
├─ VerifyGatheringCooperation.cs working beside the player without competing with him
└─ VerifyWorkAccounting.cs       what a job reports against what the world shows
```

`--ore-work` runs this group plus the assistance group; the default suite runs each file as its own named case.

## Every approach question reads the reach sense, so a scene must flood for the world it built

`VerifyOreWork.SetUp` floods before returning and ends with a `Hold` resolve so it hands back no retained destination. A row that then edits terrain or moves a body calls `ResettleReach`, which throws the region away — including the two retained search objects, which a refresh would otherwise reuse and refill from tiles it had already expanded through.

**Priming by "resolve while the region is incomplete" does nothing here, because a stale region is complete.** That is the quietest way a fixture can prime nothing and look primed, and it is why `ResettleReach` discards rather than drives.

**Because every setup warms the flood, one row deliberately does not.** `AColdFloodDoesNotLeaveTheBrainResting` empties the region and runs the whole brain against an ore thirty-two tiles out — past tool reach and past where the retired stroll could wander, so no local motion can break the row by carrying the body into range. Getting that wrong is easy and quiet: the row first stood at fourteen tiles, inside the stroll's band, and passed.

The circle it guards is real, and it closed once. With a null region every work activity reads `NotYet` and offers zero, and keeping company wins by default and asks for a hold. For as long as the `Hold` branch of `Resolve` returned before `Refresh`, what opened the circle was a query rather than a decision — the stroll's `SafeStrollGoal` asked `IsReturnable` of each candidate tile and grew the region as a side effect — and the day the stroll was deleted, 15 September 2026, this row went red with `reach-complete=False action=keep-company`. The `Hold` branch refreshes now, so the opener is the decision itself. The row was proven to discriminate rather than assumed to — an early return in `ReachSense.Refresh` while the region is null turns it red with the body beside its spawn — and proving it needed the row hoisted to the front of the list first, because this file aborts on its first failure and the mutation reddens an earlier row.

**A row that produced an undecided approach by starving a millisecond budget is testing nothing**, because that clock no longer reaches the approach at all. Two rows were found still doing it and both now empty the region instead, which is the mechanism that actually produces `Unknown`. `AnUnknownApproachDoesNotSubstituteASealedNeighbour` needs a proven No and an Unknown in one scene, which one flood cannot supply — completeness is a property of the whole flood — so it takes the No from geometry, a sealed ore whose pose scan finds nothing to rank and never consults the region.

## The nearest-first equivalence, and the three walker artefacts inside its oracle

`TheNearestFirstApproachMatchesTheExhaustiveScan` compares the production approach query against a full scan that keeps the minimum, across twenty-five combinations of ore, body position and a sealing wall, with all terrain final and the region resettled before the first query. The property is the **scan order** — nearest-first with an early stop against exhaustive-minimum — and nothing else, so the reference asks the same reach sense the production call asks.

The reference had three walker artefacts in it, and each made it rank or admit stands the orb's geometry never would, so the row compared two different questions and reported the difference as a divergence:

- **The eye.** The ranking key was the distance from the stand *plus thirty pixels of eye height* to the ore, so it preferred a stand two rows below the ore, where a walker's eye came level with it. On the ore at (30, 86) that scored the stand at (30, 88) about two pixels against thirty-four for the stand beside it, and picked it. `FindToolAccess.EyeHeight` is zero for this body.
- **Two extra rows.** The scan ran two rows further down than the reach box, which is where a body standing below and reaching up could be. Reach is a box about the centre now, symmetric by construction.
- **The body's width.** Each stand had to reach the ore from its centre and from eight pixels either side — a body forty-two tall and twenty wide sampled at its edges. The contact is a circle about one point, so that one sample is the whole body.

What is deliberately *not* aligned is the thing under test: the reference stays a full scan keeping the minimum. Ties resolve the same way in both — production breaks them on insertion order, the reference keeps the first strict improvement, over the same dx-outer dy-inner traversal — so an equal-distance pair cannot read as a divergence on its own.

**The sealing wall runs from row 0 to the floor, not from row 80.** A ten-row pillar sealed the corridor for a body that walked along it; this one flies over the top, and all twenty-five pairs read reachable. The row's own closing requirement catches that rather than passing quietly: *a comparison holding no unreachable case compares nothing.*

**The five `from` positions vary less than they look.** They once varied the search origin, when each pose's verdict came from a walker search starting at those feet. The verdict is a flood from the companion's own centre now, which does not move across the twenty-five pairs, and the pose ranking measures each stand against the ore rather than against `from` — so `from` varies only whether the in-reach early return answers before any pose is ranked. That is a real fork and the row keeps it; it is simply not the sweep of origins the five values suggest. Each `from` is a centre one radius clear of the floor, because on the floor line itself it is half inside the floor and the early return is asked about a point no body can occupy.

## Departure: the graded trade-off that no longer has a band to happen in

`DepartingPlayerChangesWhetherWorkIsWorthFinishing` used to require, at one separation, that fresh work lose to reunion while a job with one hit left still won. That half is **deleted rather than re-tuned**, because it was measured across nine separations and there is no value at which it can hold:

```
separation   fresh work     one hit left    keep-company
 480         mine  0.212    mine  0.652     0.160
 576         mine  0.201    mine  0.648     0.160
 640         mine  0.194    mine  0.646     0.160
 800         mine  0        mine  0         0.800
 960..1600   mine  0        mine  0         0.800
2000         mine  0        mine  0         1.000
```

Read the last column with the first. Reunion's own value is flat at its wander floor across the whole range where mining is worth anything, and mining does not decline towards a crossing — it is vetoed outright where the ore leaves the work radius. The table was measured at the old radius; the radius is now measured from where the player's region says he is going, so for a player walking away "past the radius" begins further out than the table shows, and the veto scene sits at the first measured separation past it, with the geometry written at the row. The two radii no longer overlap: by the time the player is far enough for reunion to outrank fresh work, the ore is already worth nothing. That is the same shape as the stroll band sitting outside the follow comfort, and which radius should move is a decision about how the companion feels.

What is kept and asserted is what the scene can still witness: **the remaining-work gradient inside the radius**, where a job with one hit left is worth more than a fresh one on identical geometry with the player walking away from the same distance; and **the veto itself**, at the first separation outside the radius, so "the band is empty" is a row rather than a sentence in a comment. The gradient is asserted as its mechanism rather than its size: the fresh job keeps the companion apart from a leaving player for longer, so it keeps less of its worth after separation, and the row requires that share to be smaller as well as the value. It used to require more than twice the value, set against about 3.1× measured while a per-tick reunion delay charge docked long jobs; that charge went when every job began paying one separation cost, the gap shrank to the separation share alone, and a mutant that ignores the job's duration in the separation makes the two shares equal and reddens the row.

## Cooperation, and the tile-map diff that makes it provable

`VerifyGatheringCooperation` runs the cooperation and permission contracts through the whole brain on native tiles, and **every whole-brain case diffs the full tile map**, so an edit to anything but the work target fails it.

Chopping from either side of a trunk with a block beside it must fell it with every strike from actual axe reach and conclude as the companion's own completion. The player hitting the companion's trunk mid-chop must move it to the separate trunk with no further strike on the shared one, end the attempt it left as partial with `player-took-trunk`, and leave no shared completion behind once the player fells the first. A trunk felled externally before any strike is an invalid attempt; after a companion strike, a shared completion.

A bed placed after the approach — for both tools, while walking and after the first strike — must stop every further strike, release the tool hand and end the attempt partial or invalid; removing it must let the work finish. The work policy is switched off, or to Mimic with no player contact, while walking and in the cooldown between strikes, with the same requirements. A player axe hit observed under Mimic, followed by chopping disabled for longer than both the watcher's contact memory and the Mimic job window (the fixture advances the watcher's clock itself), must leave Mimic waiting for the player rather than reading the old contact as recent.

An ore the pick cannot damage must never be struck while the brain fells the usable tree beside it. A retained mining job must leave a new tree prepared and valued on the same board, and must lose to it once the vein is worth nothing. Mining and chopping forecasts, less their native remaining work, must equal the walk in pixels over travel speed. **Trees within ten tiles of the world edge are never searched**, so tree scenes sit inside that margin.

## Accounting

`VerifyWorkAccounting` checks what a mining job reports against what the world shows. A two-tile vein the companion clears with its own strikes, while the player places a third copper tile touching it, must conclude partial as `tracked-portion-clear-vein-continues` and never complete, and the added tile must become the next job.

Equal remaining work is compared across two scenes differing only in **who** removed one tile of a two-tile vein — the companion's native strikes with its cooldown finished, or the player — with the companion on the same feet: value, forecast, remaining tiles, target and native remaining hits must be identical, and the remaining count must be one. A third scene adds the player's own partial pick damage to the remaining tile and requires the offer unchanged, because that damage lives in the player's hit table.

Work cancelled by range runs under the close distance mode, since an active job's allowance under the standard mode is wider than this world can separate player from ore.

## What these fixtures deliberately do not establish

That the brain can *create* access it does not have, that a chosen work position is comfortable in real terrain, or that any of this holds against a modded tile the game's own tables describe differently. Passing current access alone does not establish that the brain can create that access.

## Current state — 15 September 2026

**One row is red, deliberately, and it waits on the owner.** `ReunionChargeReadsDepartureAndTheRouteHome` requires a departing player's charge to grow with the companion's route home, and since every job began paying one separation cost measured from the player's region (`920b2e0`) it does not. On 15 September 2026 the delay the row prints still grows with the route (0.01760 near against 0.02053 far at 1.5 px a tick, 0.05356 against 0.06144 at 4), but mining's worth is identical on both routes (0.453 and 0.453, and 0.329 and 0.329), because the separation is carried along the player's travel from the stand, not along the route the companion would fly back, and the charge that read the route was removed as a second price for the same separation. Whether the route home belongs inside the separation cost is a decision about what the ruling meant, so the row is left red rather than restated: restating it would delete the only witness of the property it names.

The red the rebuild found here earlier is kept as a record because it is the cleanest instance of a class that reached six other sites.

**`ReunionChargeReadsDepartureAndTheRouteHome` failed its own premise: the far way up must lengthen the priced route home, and it did not.** Across all eight scenes the positioner's route price moved with the terrain while the chooser's return estimate did not:

```
                        return   route
near route, stationary     97     183
far route,  stationary     97     233
near route, 1.5 px/tick   122     183
far route,  1.5 px/tick   122     233
near route, 4 px/tick     157     183
far route,  4 px/tick     157     233
```

`ChooseBehaviour` takes the larger of a straight-line estimate and the positioner's priced route, and it asked for that route to `MovementQueries.Tile(region.Centre)` — and the intent region's centre is the player's feet plus a lead, so `Tile` floored it onto the **solid floor row**. `EstimatedTravelTicks` answers null for a tile no body occupies, so the maximum never happened and the return estimate silently kept its straight-line value. Every `return` above was therefore a straight line, which is why it sat below every `route` and identical in both arms; in play a companion separated from the player by a long detour priced its way home as though it could fly straight there. The route is priced to `MovementQueries.FeetTile(region.Centre)` now — the walker's own query, restored, because the player still stands and the orb resting beside him sits in that same cell — and the same substitution was found at six other sites and taken back at all of them.

**The fix was first written, measured and withdrawn, and the reason it was withdrawn was a second defect wearing the first one's clothes.** Pricing the route home correctly made this row pass and regressed `VerifyTravelEpisodes`: zero journeys recorded where the fixture requires exactly one carrying its downed ticks, and reverting restored it as a single-variable control. That read as the pricing reaching further than this row. It was not: with the route priced, the fixture's second journey was still open when the world unloaded, and the travel-episode observer's close path was handed a live NPC-table lookup that found nothing and returned without writing. The observer closes the companion it watched now, and both rows pass together. A regression that appears when a fixed value flows further is not evidence that the value is wrong; it is the next latent defect downstream, and the single-variable control proves only which change exposed it.
