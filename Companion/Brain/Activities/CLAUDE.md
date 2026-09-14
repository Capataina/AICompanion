# Activities — the seven competing jobs

The chooser prepares concrete activities, applies shared factors once, and compares the best nomination from each family. Family folders organise those responsibilities; they do not contain separate movement or weapon implementations.

```
Activities/
├─ CLAUDE.md
├─ CompanionAction.cs            the shared contract: prepare, score, execute, a place
├─ ClassifyOffersAndAttempts.cs  offer eligibility and attempt outcomes
├─ WorkPolicies.cs               mining/chopping mimic vs opportunistic vs off
├─ Combat/                       protecting the player and pursuing useful attacks
│  ├─ Hunting.cs                 pursuit of enemies with a reachable firing stand
│  └─ Guarding.cs                readiness against companions and protection urgency
├─ Gathering/                    retained ore veins and tree jobs
│  ├─ MineOre.cs                 ore-only vein work under the chosen policy
│  └─ ChopTree.cs                trunk-preference work under the chosen policy
└─ NearbyAssistance/             accompanying the player and useful local help
   ├─ LightUsefulArea.cs         dark-region lighting work
   ├─ CollectNearbyItems.cs      drops within pickup reach
   └─ KeepCompany.cs             reunion and nearby movement
```

All seven ordinary activities live in this tree: **Combat** nominates hunting or guarding; **Gathering** nominates mining or chopping; **NearbyAssistance** nominates lighting, collecting or keeping company. Their declared PurposeFamily, not their folder, determines nomination. Nearby assistance also contains the reusable pot/torch interaction adapter; it is not another selectable activity. Safety, recovery, movement, native tools, hands and activity lifecycle retain their existing owners.

## The shared contract and offer classification

Every activity implements `CompanionAction`: a `Prepare` pass that populates the candidate pool, a `Score` pass that values each candidate, an `Execute` pass that performs the selected work, and a `Place` query that returns the tile or target where the work occurs. `ClassifyOffersAndAttempts.cs` owns the vocabulary of offer eligibility: **usable** if a prepared candidate has a proven working pose or target; **unresolved** at value zero if the approach is still unknown and cannot yet decide; **known-unusable** if the work is inherently impossible here (a reachability veto, a tool that cannot damage the target, or a pick whose material does not exist nearby). Optional work does not nominate unresolved candidates — mining and hunting publish Unknown at value zero rather than walking toward an unanswered search.

## No activity asks for a route to find out whether a place is reachable

It reads the reach sense, which floods that answer once for every tile in the region; a route search happens afterwards, once, for the single destination the activity chose, through the movement request. `Tools/check-navigation-boundary.sh` refuses the search names under this tree, so an activity written later inherits the rule by failing a check rather than by anyone having read this file. The verdict type `Reachability.Reach` is not a search and stays.

The rule is worth enforcing because breaking it costs nothing visible at the call site. A bounded search per candidate answers Unknown when it runs out of expansions; an Unknown cannot be remembered, because remembering one writes a place off on evidence that does not exist; so a caller that rations how many candidates it asks about re-asks the same nearest ones for ever, and its offer says, correctly and uselessly, that the question is not finished. On the 2026-09-14 capture that was 79% of lighting's offers, with a settled flood beside 7,003 of those rows already holding the answer.

Two consequences are rulings rather than side effects. Mining and chopping ask the two-way region now instead of the one-way walker, so a vein across a drop the body cannot climb back from is refused rather than approached. And an activity's first rescores after a world change read NotYet for everywhere the flood has not grown to yet, which is the correct answer and means optional work starts a beat later than it used to.

## Traps

- A prepared candidate with an unknown approach still reads as an unresolved offer. Its value of zero prevents it from winning; it does not defer the decision to wait for the answer.
- **A fixture that edits terrain or moves a body after its setup, then primes with "resolve while the region is incomplete", primes nothing.** A stale region is complete, so the loop returns at once and every reach question is answered about the world before the edit. `VerifyOreWork.ResettleReach` throws the region away and floods it again; it also nulls the two `ContinueRouteSearch` objects, because `Refresh` reuses a live one from the same feet and a reused one hands back the tiles it had already expanded through.
- A work activity exits and clears its retained job if the policy is disabled, a recheck changes the tile, a reach change invalidates the approach, or the native tool permission is revoked. Exit and suspension clear independently; a suspended job held through safety still owns the tool.
- Changing the player's active tool is new evidence in every search, reopening discovery on the same preparation for both mining and chopping.

## Current state — 2026-09-14

The seven activities are in place under three families. Lighting enters as the newest member of NearbyAssistance, a dark-region worker that queries the Light sense and chains discovered sites. All activities read Light and Reach from Observation.Senses rather than computing privately. Mining and hunting correctly publish Unknown at zero and do not nominate on an unanswered search; companionship may still walk toward the player while those work on their answers. The shared offer classification (usable, unresolved, known-unusable) resolves independently in each family.
