# Assistance fixtures

Lighting, collection, keeping-company strolls, courtesy, capability revision and the shared activity contracts. `--follow` includes company motion and courtesy; `--capability` is the reach/power group.

## Assistance trips

`VerifyAssistanceTrips` (in `--ore-work` and the default run) drives the real lighting and collection activities through the shared nearby-interaction executor on an island scene. The companion and player stand on a short floor whose torch sites sit inside measured lit disc; the only dark sites and pot are on the floor of a pit twelve rows below. Without a staircase the pit is a drop with no way back, and neither lighting site nor pot may be offered; with one-tile steps the pit must offer both. A second scene puts a two-tile shelf eleven columns from the companion, with pot on it as the only dark torch sites. At row 51 its sites are out of standing reach and out of a jump from start, and `HopApproach` finds a take-off; at row 30 it finds none. Each case lifts the planning allowances.

## Capability revision

`VerifyCapabilityRevision` (`--capability`, inside `--ore-work` and the default run) changes one player-derived capability between preparations. Mining at reach 5 swings from feet; at reach 2 the next preparation must send it to a stand that reaches, never stale feet, and at reach 5 again it works from feet. Ore twelve rows up is unoffered at reach 1 and must be offered on the very next preparation at reach 11. A trunk refused at reach 0 must be offered on next preparation at reach 5, and a trunk worked from feet must be walked to from a reaching stand after reach shrinks. Lighting must never walk to a stand the current reach cannot swing from and must search again after reach grows. Every case lifts planning allowances.

## Courtesy

`VerifyCourtesy` (`--courtesy`, inside `--follow` and the default run) is A01 through the whole brain with only keeping company offered. The player four tiles away aims a dirt block at companion's feet-row tile without swinging: an empty hand must leave companion on that tile, the block must move it off and keep it off, and a wooden bow or torch aimed at the same tile must reproduce the empty hand's trajectory tick for tick.

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
