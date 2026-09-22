# Gathering fixtures — ore, trees, and who is credited for the work

Seven files. Most of them drive the live mining and chopping code against native tiles, diffing or counting what the world actually lost, or what was actually offered, so that "it mined" cannot be satisfied by an intention.

**Which brain a row drives is the first thing to establish before reading its verdict, and since 22 September 2026 there is only one.** `AIC-419` deleted the family chooser, and with it the three rows here that were the reason it could not be deleted:

```
drives the course                       VerifyOreWork's departure row, VerifyTreeOpportunityCapture,
                                        VerifyGatheringOpportunityDiscovery, VerifyGatheringCourseBindings
drives an observation, not a decision   VerifyOreWork's ReunionChargeReadsDepartureAndTheRouteHome, which
                                        now calls ObserveCompanionship and reads the three numbers the
                                        recorder writes, in place of Chooser.Choose and mining's score
drives an activity or a tool directly   VerifyMiningList, VerifyWorkAccounting — neither calls a course
                                        or a whole tick; they drive mining's own job and the native tool
                                        and read what the world lost
deleted with the chooser                VerifyGatheringCooperation's W04 and G02, and the whole of
                                        VerifyRouteHomeFromEitherEnd
```

`VerifyRouteHomeFromEitherEnd` read `Chooser.RouteDetour` from beside the ore and from beside the player, because the separation cost a job paid was decided from wherever the body happened to be when the job was chosen. The course has no such helper and no such cost: an unresolved route is a typed unresolved answer rather than a number a caller reconstructs, which is the property `VerifyCompanionshipForecast`'s `G15 companionship requires timed travel and actual arrival evidence` holds — a leg with no timed travel refuses the forecast with `timed-travel-evidence-unresolved` instead of costing zero. `../../../Companion/Brain/Infrastructure/Selection/Courses/CLAUDE.md` carries the property with both of the chooser's arms beside it.

```
Gathering/
├─ CLAUDE.md
├─ VerifyOreWork.cs              ore jobs end to end: approach, reach, seals, attribution, departure
├─ VerifyMiningList.cs           the list's known ores, marks and mode, and mining refusing what it leaves
├─ VerifyGatheringOpportunityDiscovery.cs frozen source contracts and sliced native ore progress/removal capture
├─ VerifyTreeOpportunityCapture.cs sliced trunk deduplication, native axe progress, policy changes, missing coverage,
│                                  and the re-answer a moved body forces over sites already found
├─ VerifyGatheringCourseBindings.cs native-use credit, cooldown phases, conditional successors and exact tool validation
├─ VerifyGatheringCooperation.cs working beside the player without competing with him
└─ VerifyWorkAccounting.cs       what a job reports against what the world shows
```

## The mining list: every refusal carries its own control

`--retained-course-tools` binds frozen work through the production binder. A current-pose case requires delayed readiness, a one-tick hand phase, a causal successor and a changed-tool refusal. A travelling case allows a nominal useful prefix but refuses to certify its later enabling successor. The native pick case takes the actual captured tile/tool facts, invokes the existing pick mechanism and compares predicted work with realised damage or removal. A partial-credit case requires the whole physical hit in the successor while useful reward is capped by the remaining claim. The tests do not replace full accepted-grant execution or whole-course ordering checks.

`--retained-course-trees` drives the new native tree capture through one-operation slices, using two separate multi-cell trunks. It requires one job per bottom, a shared native-work denominator, unchanged fact versions on an unchanged completed capture, reduced remaining work after an actual axe hit, and refusal after chopping is disabled. Its empty-snapshot control requires missing census coverage to remain unresolved. These checks prove the observation/source boundary, not course execution or preservation of original job units across every later tree replacement.

**Its one-operation slice bound is the scan's own cell count and must never be a literal again.** It was a literal 10,000 against a box of 81 by 81, and the day the census window became the admission radius the row failed as though the census had hung — the fixture caught its own commit's lesson and then needed it applied to itself.

**`G08 a moved body re-answers every held site and a one-operation sweep still advances` is the row that covers the re-answer, and it exists because nothing did.** When the geometry treadmill was retired for a re-answer over sites already found, a grep for the new budget keys outside the two production files returned nothing, no fixture moved the body between two captures, and the one row touching the path drove a census of a single site — where a cursor completes in one iteration and can neither mix nor stall. `G08` drives two veins, the whole census one operation at a time, then carries the body beyond the new-work allowance and requires every held vein to flip to `outside-allowance-or-protected`. It is proven by mutation: skipping every site after the first reddens it with `25,59 outside-allowance-or-protected; 45,59 observed-native-ore`, which is the stale-site signature exactly.

**Two earlier drafts of `G08` are recorded at the row because both would have failed against correct code**, which is the kind that gets "fixed" by loosening it. One asserted on a site's stand — for a tree that is a free cell beside it, the same point wherever the companion is. One threw the reach flood away and required the verdict to flip to `approach-not-yet`; it did not, correctly, because the flood resettles and the trunks are genuinely still reachable. **Only `InNewActivityAllowance` reads the companion's own position, so only it can witness a re-answer at all**, and a draft that watches anything else is watching a quantity the body's movement does not touch.

Native `WorldGen.GetTreeBottom` stops at `Main.maxTilesY - 50`. Multi-cell tree fixtures in the small replay world must place their trunk and supporting ground above that guard; otherwise the engine returns the queried segment rather than traversing to the bottom, and a census appears to duplicate a tree despite faithfully calling native identity. Single-tile tree fixtures do not expose this condition.

`VerifyMiningList` builds its scenes with `VerifyOreWork.SetUp` and gives every row that shows an ore not being offered a second half in the same scene: the same ore offered once the mark or the mode says so, on the very next preparation. A refusal alone passes against a scene whose ore was never reachable, and the next-preparation timing is itself a property, because it is what the list's revision in the approach key buys. The allowed-ore-beside-a-left-one row first proves, list-blind, that the nearer ore is the one taken, so it cannot pass by the allowed ore simply being nearest. The known-ores row drives the real `CompanionPlayer.PostUpdate` rather than calling the list directly, so it fails if the hook is ever unwired. Each row was shown red against a planted mutation of the mechanism it names before it was trusted; the commit that added the file lists them.

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

What is kept and asserted is what the scene can still witness: **the remaining-work gradient inside the radius**, where a job with one hit left is worth more than a fresh one on identical geometry with the player walking away from the same distance; and **the veto itself**, at the first separation outside the radius, so "the band is empty" is a row rather than a sentence in a comment.

**The gradient's mechanism flipped when the row moved onto the course, and the row asserts the opposite of what it used to.** Under the chooser a whole multi-hit vein was priced as one job, so a fresh vein kept the companion apart from a leaving player for longer and *paid more separation for it* — the gradient lived in the separation share, and the row required that share to be smaller as well as the value. The course binds **one use**, measured `useTicks=1 travelTicks=0` in both arms at every separation, so the two orders have identical duration and pay identical separation to four decimal places and the gradient lives entirely in the reward:

```
separation   fresh nominal   one-hit nominal   companionship, both arms
 400            0.1949           0.8337            0.1490
 480            0.1424           0.7812            0.2016
 576            0.0785           0.7173            0.2654
 800           -0.0697           0.5691            0.4136
```

So the row now requires the two separation charges to be **equal** within a millionth, beside the reward gap — a course that quietly went back to pricing whole jobs reddens it instead of passing, which the old assertion could not do. It is a restatement rather than a loosening, because the unit changed underneath it. Note the 800 px fresh job is worth a negative number and is still chosen: idling costs −0.4102, and doing something slightly unprofitable beats that. Whether the choice should still flip there is a product question rather than a fixture one, carried as `AIC-450`.

The row's earlier history is worth keeping because it explains the deleted half above: it once required more than twice the value, set against about 3.1x measured while a per-tick reunion delay charge docked long jobs; that charge went when every job began paying one separation cost, and the gap shrank to the separation share alone before the course removed the share's role entirely.

## Cooperation, and the tile-map diff that makes it provable

`VerifyGatheringCooperation` runs the cooperation and permission contracts through the whole brain on native tiles, and **every whole-brain case diffs the full tile map**, so an edit to anything but the work target fails it.

Chopping from either side of a trunk with a block beside it must fell it with every strike from actual axe reach and conclude as the companion's own completion. The player hitting the companion's trunk mid-chop must move it to the separate trunk with no further strike on the shared one, end the attempt it left as partial with `player-took-trunk`, and leave no shared completion behind once the player fells the first. A trunk felled externally before any strike is an invalid attempt; after a companion strike, a shared completion.

A bed placed after the approach — for both tools, while walking and after the first strike — must stop every further strike, release the tool hand and end the attempt partial or invalid; removing it must let the work finish. The work policy is switched off, or to Mimic with no player contact, while walking and in the cooldown between strikes, with the same requirements. A player axe hit observed under Mimic, followed by chopping disabled for longer than both the watcher's contact memory and the Mimic job window (the fixture advances the watcher's clock itself), must leave Mimic waiting for the player rather than reading the old contact as recent.

An ore the pick cannot damage must never be struck while the brain fells the usable tree beside it. A retained mining job must leave a new tree prepared and valued on the same board, and must lose to it once the vein is worth nothing. Mining and chopping forecasts, less their native remaining work, must equal the walk in pixels over travel speed. **Trees within ten tiles of the world edge are never searched**, so tree scenes sit inside that margin.

**Two of its rows were on the retired chooser and both went with it on 22 September 2026**, which is what unblocked `AIC-419`. Neither question went with them.

`W04` was the chopping twin of the departure question above, and the mining twin is already on the course and still asserts the remaining-work gradient with the equality assertion that replaced it, so the *property* keeps a witness. What `W04` alone was waiting on is `AIC-450` — whether a job priced at a negative worth should still beat idling — and that is a product question about the course, unanswerable by a fixture driving a chooser.

`G02` asserted that a retained vein does not reserve its *family* for itself, and a family nomination stage is exactly what the course does not have, so there was nothing to port the sentence onto. The finding it had turned into is unchanged and still open: `AIC-449`, a trunk created mid-session published by the census as `usable / observed-native-tree`, present in the live snapshot, examined by its own source every slice, and never appearing in `Course.Admitted` or `LastLeaders` across two hundred consecutive decisions. Relaxing its premise to "the course priced something" would have made it green on zeroes, which is the one outcome worse than a red; a course-side witness belongs in `../DecisionMaking/VerifyAdmittedOpportunitiesBind.cs`, beside the rest of the admission class, and writing one is work for whoever closes `AIC-449`.

**The player's own preferences were the one process-wide static the per-case reset did not restore, and it decided this file's verdicts.** `CompanionPreferences.Current` carries seven fields any case can write — both work policies, combat, pot breaking, torch placement, the distance mode and the mining list. `VerifyOreWork.SetUp` sets the mining policy and never the chopping one, so its sealed-tree row read whichever chopping policy the process happened to hold: alone it found the Opportunistic default and passed, and inside the suite it found a Disabled or Mimic policy left by a cooperation row here, took an early return above the reach block, and reported that unchanged retained work re-searches its reach every scoring tick — **false in both directions**, since the branch was never reached at all rather than reached too often, and the row could not say so because it asserted a bare equality with no number in its message. The reset installs a fresh instance now rather than an explicit list of assignments, because every default lives on the property initialisers and a list drifts from what the game starts with. Two lessons ride with it: setting the missing policy in that one row would have repaired one reader and left every other reader of those seven fields with the same order dependence, unannounced; and a row asserting a bare equality cannot distinguish a branch that ran wrongly from a branch that never ran, so it carries its number — 81 after 20 preparations, where anything below 20 would be the search genuinely re-running.

## Accounting

`VerifyWorkAccounting` checks what a mining job reports against what the world shows. A two-tile vein the companion clears with its own strikes, while the player places a third copper tile touching it, must conclude partial as `tracked-portion-clear-vein-continues` and never complete, and the added tile must become the next job.

Equal remaining work is compared across two scenes differing only in **who** removed one tile of a two-tile vein — the companion's native strikes with its cooldown finished, or the player — with the companion on the same feet: value, forecast, remaining tiles, target and native remaining hits must be identical, and the remaining count must be one. A third scene adds the player's own partial pick damage to the remaining tile and requires the offer unchanged, because that damage lives in the player's hit table.

Work cancelled by range runs under the close distance mode, since an active job's allowance under the standard mode is wider than this world can separate player from ore.

## What these fixtures deliberately do not establish

That the brain can *create* access it does not have, that a chosen work position is comfortable in real terrain, or that any of this holds against a modded tile the game's own tables describe differently. Passing current access alone does not establish that the brain can create that access.

## Current state — 21 September 2026

**`VerifyOreWork` is the fixture this session spent most of itself on, and three of its rows moved for three different reasons.** The maximum-reach row cleared when execution stopped flying to a stand the capture had published from a body several ticks stale, and clearing it revealed sixteen rows behind it that had not run in this tree, because this file aborts on its first failure. `AColdFloodDoesNotLeaveTheBrainResting` was one of them and needed a brain fix rather than a translation — the ore capture was the one capture not keyed on the flood that answered it, so a refusal read under a young flood was frozen for ever; it is **last** in the file now, with the reason at the call site, so it can never blind the rows behind it again. And the departure row moved onto the course and gained the equality assertion above, after the census window was found to be smaller than the allowance it feeds: at 800 px separation the ore sits about 866 px from the player's heading, inside the allowance and outside the sweep, and the course chose keeping company beside a vein it could not see, with the census reporting `examined=0, exhausted=true`.

**A row that stands early in an abort-on-first-failure file is a row that decides what the rest of the file is allowed to report.** That is why a red waiting on somebody's decision is moved to the end and a red clearing this afternoon is left where it is, and it is why a red count falling by one while a new name appears is the normal shape of progress here rather than a regression. Say which it is when reporting.

**`ore work breaks ore without excavating ordinary terrain` is one of the two rows in the whole suite that flake on harness cost rather than on code**, failing 5 of 12 whole-suite runs of 21 September against 3 of 3 passing standalone. `../../CLAUDE.md`'s trap section carries the measurement and what it does and does not settle; the practical consequence here is that a lone red on this row is more likely the harness than your change, and a single green whole-suite run is weak evidence the other way.

**That row lost half its subject on 22 September 2026 and kept the half it was named for.** It called `Chooser.Choose` and read the chosen activity and mining's final score beside the three numbers; the chooser went with `AIC-419` and what it was choosing between has been the course's job since `0bb2c8a`, so the `a calm player is met by quick justified work` assertion and the `mine` column went with it. The three numbers did not: `ObserveCompanionship` runs on every brain tick, the recorder writes all three as columns, and this is still the only witness that the return estimate reads the priced route home rather than a straight line. The row drives that observation directly now.

**`ReunionChargeReadsDepartureAndTheRouteHome` is green on its original assertion, because the one separation cost reads the route home.** From `920b2e0`, when every job began paying one separation cost measured from the player's region, it was red and left red on purpose: the delay it prints grew with the route (0.01760 near against 0.02053 far at 1.5 px a tick, 0.05356 against 0.06144 at 4) but mining's worth was identical on both routes (0.453 and 0.453, and 0.329 and 0.329), because the separation read the straight line and the charge that read the route had been removed as a second price for the same separation. Restating the row would have deleted the only witness of the property it names, so it waited for the owner, who ruled on 15 September 2026 that the one cost reads the route home. The separation's distance now adds the reach flood's detour from the stand to the player (`../../../Companion/Brain/Infrastructure/Selection/CLAUDE.md` owns how), and on 15 September 2026 mining read 0.255 near against 0.116 far at 1.5 px a tick, and 0.124 against 0.000 at 4, where the far route hands the choice to keeping company. With the detour multiplied by zero the row went red again at 1.5 px a tick with 0.453 on both routes. The name still describes the mechanism — departure scales the charge and the route home lengthens it — so it was not renamed.

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
