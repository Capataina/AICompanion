# Activities — the six competing jobs

The chooser prepares concrete activities, applies shared factors once, and compares the best nomination from each family. Family folders organise those responsibilities; they do not contain separate movement or weapon implementations.

```
Activities/
├─ CLAUDE.md
├─ CompanionAction.cs            the shared contract: prepare, score, execute, a place
├─ ClassifyOffersAndAttempts.cs  offer eligibility and attempt outcomes
├─ RecordCandidateFunnel.cs      what one preparation did with each candidate: the stage that refused it and what it read
├─ WorkPolicies.cs               mining/chopping mimic vs opportunistic vs off
├─ Combat/                       the one fighting stance, with a guard side and a hunt side
│  ├─ FightEnemies.cs            guarding's threat binding with hunting's admissibility and stall deferral
│  └─ ResolveFiringOpportunity.cs whether a reachable stand can shoot, shared by both sides
├─ Gathering/                    retained ore veins and tree jobs
│  ├─ MineOre.cs                 ore-only vein work under the chosen policy
│  ├─ ChopTree.cs                trunk-preference work under the chosen policy
│  └─ DescribeOreJobEnd.cs       the bounded vein's state at job end, with attribution
└─ NearbyAssistance/             accompanying the player and useful local help
   ├─ LightUsefulArea.cs         a torch wherever his smart cursor could place one in the dark
   ├─ CollectNearbyItems.cs      drops priced where they will land
   ├─ KeepCompany.cs             reunion and nearby movement
   └─ PerformNearbyWorldWork.cs  the shared pot/torch discovery and interaction adapter
```

All six ordinary activities live in this tree: **Combat** is the one fighting stance; **Gathering** nominates mining or chopping; **NearbyAssistance** nominates lighting, collecting or keeping company. Their declared PurposeFamily, not their folder, determines nomination. Nearby assistance also contains the reusable pot/torch interaction adapter; it is not another selectable activity. Safety, recovery, movement, native tools, hands and activity lifecycle retain their existing owners.

## The shared contract and offer classification

Every activity implements `CompanionAction`: a `Prepare` pass that populates the candidate pool, a `Score` pass that values each candidate, an `Execute` pass that performs the selected work, and a `Place` query that returns the tile or target where the work occurs. `ClassifyOffersAndAttempts.cs` owns the vocabulary of offer eligibility: **usable** if a prepared candidate has a proven working pose or target; **unresolved** at value zero if the approach is still unknown and cannot yet decide; **known-unusable** if the work is inherently impossible here (a reachability veto, a tool that cannot damage the target, or a pick whose material does not exist nearby). Optional work does not nominate unresolved candidates — mining and the hunt side publish Unknown at value zero rather than walking toward an unanswered search. **Deferred**, also at value zero, is an offer set aside for a reason that is neither an unanswered search nor a proof: a family whose preparation share was spent before the child ran, and a nearby-work stand outside a finished flood's known radius, which no amount of waiting on that flood will answer.

## A preparation says what it did with each candidate

An offer names only why a search ended; the question a capture is read for is usually about one candidate — why this tile, this drop, this enemy was passed over. `RecordCandidateFunnel.cs` is the record of that, shared by every activity that searches. An activity declares its stages in the order a candidate meets them, with `offered` last, and adds each candidate it looks at with the stage that refused it and the numbers that stage read; a candidate that meets its stages in a line gets the stage it passed from that order, and one refused at one of several alternatives at the same point names the stage it passed itself. Every candidate is counted by its refusing stage, the nearest few by the activity's own cost are kept, and the candidate that got furthest is kept whatever its distance, because a nearest-only sample of a search over a lit bubble holds only tiles refused for being lit. The counts are the whole search and the entries a sample of it, never the other way round. Lighting, collection and combat fill one; the recorder writes the furthest stage of each as a column every row and the funnel as an occurrence when its counts or its furthest stage change.

**An exhausted bound is not a proven negative, and the vocabulary now separates them everywhere.** Every bounded search under this tree — the stand sweep around a hunt target, the positioner's own candidate shortlist, a lighting site scan cut by the tick's planning deadline — can stop because it ran out rather than because it found nothing. A pass that solved or already remembered a refusal for every candidate reports the absence and is **known-unusable**; a pass that stopped early reports an unfinished search and is **unresolved**, and the chooser must be able to ask which happened without knowing how the search works. Collapsing the two is the defect that vetoed a hunt on the tick a millisecond budget expired and handed the body to keeping company by default. The rule holds however the bounds are tuned, which is why raising any of them was refused as an instance fix: whatever the numbers are, a search can exceed them, and the only question is whether the cut is reported as a cut.

**Every activity's "near the player" radius is measured to the player's intent region, in one place.** `CompanionAction.AllowsTarget` is that place, so mining, chopping, lighting, collection and combat all inherit the anchor rather than each carrying a copy of the test. Anchored on the player's feet a radius walks backwards as he does: work a few tiles ahead of a travelling player sits at the far edge of a circle centred behind him and drops out of range at the moment he sets off towards it, which is the one moment it is worth anything. Anchored on the region it leads him. The anchor is the region's **centre**, not its leading edge, because each caller subtracts its own comfortable distance and measuring to the boundary would subtract it twice. Threat, safety and protection readers deliberately keep reading his body, because danger to the player is about where the player is and not about where he is going. Two activities also centre their own resource *search* on the region — mining's near-player ore scan and lighting's work radius — which is a separate read from the radius test above, and chopping's has not been moved with them.

## No activity asks for a route to find out whether a place is reachable

It reads the reach sense, which floods that answer once for every tile in the region; a route search happens afterwards, once, for the single destination the activity chose, through the movement request. `Tools/check-navigation-boundary.sh` refuses the search names under this tree, so an activity written later inherits the rule by failing a check rather than by anyone having read this file. The verdict type `Reachability.Reach` is not a search and stays.

The rule is worth enforcing because breaking it costs nothing visible at the call site. A bounded search per candidate answers Unknown when it runs out of expansions; an Unknown cannot be remembered, because remembering one writes a place off on evidence that does not exist; so a caller that rations how many candidates it asks about re-asks the same nearest ones for ever, and its offer says, correctly and uselessly, that the question is not finished. On the 2026-09-14 capture that was 79% of lighting's offers, with a settled flood beside 7,003 of those rows already holding the answer.

Two consequences are rulings rather than side effects. Mining and chopping ask the flood over free space, so a vein behind a flooded passage is approached through it, because every liquid is air to the orb, and nothing distinguishes going from coming back because an orb's flood has no one-way edge. And an activity's first rescores after a world change read NotYet for everywhere the flood has not grown to yet, which is the correct answer and means optional work starts a beat later than it used to.

## Traps

- A prepared candidate with an unknown approach still reads as an unresolved offer. Its value of zero prevents it from winning; it does not defer the decision to wait for the answer.
- **A fixture that edits terrain or moves a body after its setup, then primes with "resolve while the region is incomplete", primes nothing.** A stale region is complete, so the loop returns at once and every reach question is answered about the world before the edit. `VerifyOreWork.ResettleReach` throws the region away and floods it again; it also nulls the two `ContinueRouteSearch` objects, because `Refresh` reuses a live one from the same feet and a reused one hands back the tiles it had already expanded through.
- A work activity exits and clears its retained job if the policy is disabled, a recheck changes the tile, a reach change invalidates the approach, or the native tool permission is revoked. Exit and suspension clear independently; a suspended job held through recovery or downing still owns the tool.
- Changing the player's active tool is new evidence in every search, reopening discovery on the same preparation for both mining and chopping.

## Current state — 2026-09-14

The six activities are in place under three families, and all of them read Light, Reach and now the player's intent region from Observation.Senses rather than computing privately. Two things changed here in the 0.25.0 build and neither is a new activity.

The intent region became the third sense, and `AllowsTarget` became its single consumer for every work radius, so what counts as near the player now leads him instead of trailing him. `MineOre` and `LightUsefulArea` moved their own search centres onto the region with it; `ChopTree` did not, so opportunistic chopping still scans from the player's body while mining scans from the region — an inconsistency recorded rather than repaired, since this folder documents and does not edit code.

And a bounded search that ran out now says so. The three-valued offer vocabulary above is the same rule one layer down from the positioner's own, and it is what stopped a hunt being branded impossible on the tick a budget expired.

Companionship may still fly toward the player while optional work waits on its answers, which is the behaviour to expect rather than debug for the first hundred-odd ticks after any world change. The body became an orb on 2026-09-15 and every activity here was re-requested rather than redesigned: a work activity asks for a hover beside its tile through the shared tool-access query, collect dips to the drop, and nothing under this tree asks for a jump, a stand or a take-off any more.
