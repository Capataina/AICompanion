# Assistance fixtures

## A scene whose region nobody flooded answers about a different world

**Every access question here reads the reach sense rather than searching, so a fixture that edits terrain or moves a body after its setup must flood the region again — and priming with "resolve while the region is incomplete" does not do that.** A stale region is complete, so that loop returns at once, and every row built on it is answered about the world before the edit. Five rows were found doing exactly this and each failed for a reason that had nothing to do with what it asserted. `VerifyOreWork.ResettleReach` throws the region away and floods it again; it also nulls the two `ContinueRouteSearch` objects, because `Refresh` reuses a live one from the same feet and a reused one hands back the tiles it had already expanded through — emptying only the result sets refills them from a search that walked through the wall before the wall existed.

The mirror of that mistake is handing a whole-brain scene a finished region built from somewhere else. `VerifyOreWork.SetUp` floods before it returns, because the live game has been flooding since the companion spawned and a fixture that calls `Prepare` directly has not; but flooding means resolving the positioner, and a resolve leaves it holding a destination, a request kind and a fresh rescore clock. The setup's last act is therefore a `Hold` resolve, which is the production path that clears exactly those three and refreshes nothing. Without it the safety suite's guard row walked to a spot the ore setup had chosen for a different scene and reported it as guarding's own answer.

## Light and reach senses

`VerifyLightAndReachSenses` holds the acceptance row for the starvation this lane removed: a sealed cavity of dark air nineteen tiles from the companion and a reachable dark floor thirty-seven tiles away, where lighting must offer the far site on its first search with the offer reading `Usable/reachable-interaction`. It asserts the offer string rather than the score, because "something was offered" is satisfied by any of lighting's four exits and which exit it is was the whole finding, and it requires the search ledger to carry a site recorded `unreachable`, because a ledger naming only the chosen site says nothing about the ones walked past.

**Its cavity is carved out of solid rock, and the first version was not.** A site is refused unless its whole neighbourhood reads dark, and that neighbourhood is a mean over open air only — so a thin-shelled chamber hanging in open space has its own shell excluded from the mean and the lit air beyond it counted, every site inside reads lit, and none is ever put to the approach query. The row went green having asked exactly one site, the far one, and the ledger is what showed it.

Planting a three-site budget back into the shared discovery loop turns this row red on `Unresolved/interaction-stand-not-yet-known-reachable`, and a three-drop budget in collection turns `D1` red on `KnownUnusable/drop-unreachable`. Both were run; a row that has never failed and a row that cannot fail print the same word.

## Spatial invalidation

The `e:` rows hold the rule that a terrain edit invalidates retained search work only where the work actually looked. Three of them drive `ContinueRouteSearch` directly and one drives the reach sense through the real positioner, and between them they pin the three things that can go wrong in opposite directions: an edit the query never read must leave it valid and leave the answer it already has intact, an edit at exactly the scan's own reach must invalidate while one row past it must not, and a query nobody asked across more than the record's window must read invalid rather than clean.

Each was mutation-run against the rule it replaces. With the predicate forced true — the world-global compare — all four go red. With the margin narrowed to a body and a tile, only the margin row goes red, which is what that row is for. With a lost window answered clean, only the window row goes red. A margin row that asserts only the invalidating half would pass a margin so wide that nothing ever survives, which is this whole mechanism doing nothing, so both halves are asserted.

**The edits are announced rather than made wherever the row is about the compare.** Invalidation keys on what the game announces, so announcing without touching a tile tests exactly the surface under test and leaves the scene identical for the rows that follow. The one row that is about membership rather than about the compare builds a real wall, taller than the body can jump, and asks the region afterwards.

**These rows are on a hundred-tile world whose flat floor the flood crosses end to end, so "far away" is only available vertically.** That is not a fixture convenience, it is the honest shape of the gain: the sensitive box is two dozen columns either side and tens of rows below, so an edit anywhere near the region still restarts it and only a distant one is spared. A row claiming otherwise would be claiming more than the mechanism delivers.

**A settle loop of "resolve while the region is incomplete" primes nothing when the region is already complete**, which is the trap this folder's first section names and which the first version of the reflood row walked into: the scene's own setup floods before it resets the terrain, so the loop returned at once and the refloods being counted were counted against a flood built under an earlier revision. The row calls `VerifyOreWork.ResettleReach` first. The same shape is worth checking in any row here that asserts `ReachComplete` after a loop it expects to have run.

Lighting, collection, keeping-company strolls, courtesy, capability revision and the shared activity contracts. `--follow` includes company motion and courtesy; `--capability` is the reach/power group.

## Assistance trips

`VerifyAssistanceTrips` (in `--ore-work` and the default run) drives the real lighting and collection activities through the shared nearby-interaction executor on an island scene. The companion and player stand on a short floor whose torch sites sit inside measured lit disc; the only dark sites and pot are on the floor of a pit twelve rows below. Without a staircase the pit is a drop with no way back, and neither lighting site nor pot may be offered; with one-tile steps the pit must offer both. A second scene puts a two-tile shelf eleven columns from the companion, with pot on it as the only dark torch sites. At row 51 its sites are out of standing reach and out of a jump from start, and `HopApproach` finds a take-off; at row 30 it finds none. Each case lifts the planning allowances.

## Capability revision

`VerifyCapabilityRevision` (`--capability`, inside `--ore-work` and the default run) changes one player-derived capability between preparations. Mining at reach 5 swings from feet; at reach 2 the next preparation must send it to a stand that reaches, never stale feet, and at reach 5 again it works from feet. Ore twelve rows up is unoffered at reach 1 and must be offered on the very next preparation at reach 11. A trunk refused at reach 0 must be offered on next preparation at reach 5, and a trunk worked from feet must be walked to from a reaching stand after reach shrinks. Lighting must never walk to a stand the current reach cannot swing from and must search again after reach grows. Every case lifts planning allowances.

## Courtesy

`VerifyCourtesy` (`--courtesy`, inside `--follow` and the default run) is A01 through the whole brain with only keeping company offered. The player four tiles away aims a dirt block at companion's feet-row tile without swinging: an empty hand must leave companion on that tile, the block must move it off and keep it off, and a wooden bow or torch aimed at the same tile must reproduce the empty hand's trajectory tick for tick.

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
