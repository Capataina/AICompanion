# Proposal 1 implementation ledger

**13 September 2026.** `sh Tools/verify.sh` exits 0: 2536/2536 native NPC collision matches, family-offer fixtures, and the rest of the default native suite. Combat's telemetry-close fixture now passes `Close(string reason)` rather than the old zero-argument invoke. No recorded playtest of this build. Current Behaviour in the README is still the 11 September caves. A green fixture is not a live pass.

G1 (two-tile pillar top hop) remains a known limitation asserted as it stands. Mastery movement abilities, chaining several jobs (Proposal 3), and a projectile dodge that jumps are not built.

## Failure cases

| IDs | Owner | Evidence | Status |
|---|---|---|---|
| T01, T07 | P03 | `VerifyFamilyOffers.cs` — empty family, duplicate/reorder, matched flat maximum | fixture |
| T02 | P02/P06 | `VerifyPreparedActivities` attempt ownership; suspension is Interrupted not Failed | fixture |
| T03, T04 | P03/P08 | `VerifyCombatPurpose`, `VerifyCombatActorMatrix`, `VerifyPersonalDanger` | fixture |
| T05 | P02/P05 | `VerifyWorkAccounting`, reunion charge matrix in `VerifyOreWork` | fixture |
| T06 | P05 | reunion delay cost in `VerifyPreparedActivities` / `VerifyOreWork` | fixture |
| T08 | P03/P10 | deferred child in `VerifyFamilyOffers`; family share in `ScheduleOpportunityQueries` | fixture |
| T09 | P02/P11 | `VerifyCapabilityRevision` | fixture |
| T10 | P03/P06 | empty-world reunion in `VerifyCompanyLocalMotion` | fixture |
| G01 | P03/P04/P09 | `VerifyOreWork` ineligible vs usable; G1 hop still a known limitation | partial |
| G02, G03 | P05/P09 | `VerifyWorkAccounting` equal remaining work | fixture |
| C01–C03 | P08 | `VerifySafetyAftermath`, `VerifyCombatPurpose` | fixture |
| A01–A03 | P05/P10 | `VerifyUsefulAssistance`, `VerifyAssistanceTrips`, `VerifyCompanyLocalMotion` | fixture |
| M01–M04 | P04/P07 | `VerifyOreWork`, `VerifyMiningHops`; G1 not closed | partial |
| M05 | P02/P09 | attributed Complete in gathering fixtures | fixture |
| W01–W05 | P09/P05 | `VerifyGatheringCooperation` | fixture |
| H01–H05 | P08/P07 | `VerifyHuntAdmissibility`, `VerifyHuntProgress`, `VerifyFiringPosition` | fixture |
| P01–P05 | P08 | `VerifyCombatPurpose` guard access and removal | fixture |
| S01–S05 | P08/P06/P07 | `VerifySafetyAftermath`, `VerifyCapturedEscape`; S01 dodge-on-dry-floor still `--dodge-repro` exit 1 | partial |
| L01–L05 | P10/P07 | `VerifyUsefulAssistance`, `VerifyAssistanceTrips` | fixture |
| I01–I05 | P10/P09 | `VerifyCollectionContracts` | fixture |
| K01–K05 | P05/P06/P07/P10 | `VerifyResponsiveFollowing`, `VerifyCompanyLocalMotion`, recovery fixtures | fixture |
| X01–X05 | P10/P07 | hazard strolls and rim case in `VerifyCompanyLocalMotion` | fixture |

## Product responsibilities

| Case | Responsibility | Status |
|---|---|---|
| A01 | Courtesy | fixture (`VerifyCourtesy`); open-floor MEASURE not a pass |
| A02 | Boss and event | fixture (`VerifyEncounterContext`); live boss unplayed |
| A03 | Player direction | fixture (meeting-place activity pairs) |
| A04 | Enemy selection | fixture (`VerifyHuntAdmissibility`, arsenal outcomes) |
| A05 | Chain jobs | not built — Proposal 3; incidental pots/torches in reach only |
| A06 | Know unavailable | fixture (eligibility classes) |
| A07 | Commit coherently | fixture (attempt outcomes, remaining work) |
| A08 | Firing position | fixture (`VerifyFiringPosition`) |
| A09 | Self-preservation | fixture (`VerifyCapturedEscape`); breath-priced routing not claimed |
| A10 | Lighting | fixture (`VerifyUsefulAssistance`) |
| A11 | Recognise threats | fixture (unattackable harmful actors) |
| A12 | Traverse terrain | fixture (native collision, movement failures); historical corpus not green |
| A13 | Protect the player | fixture (`VerifyCombatPurpose`) |
| A14 | Incidental pots | fixture (grant-boundary incidental scan) |
| A15 | Dodge and kite | partial — spacing+fire fixture; dry-floor jump dodge still fails |
| A16 | Mine | fixture; G1 known limitation |
| A17 | Report activity | HUD offscreen fixtures; live look unreviewed |
| A18 | Find routes | fixture; portable corpus still partial |
| A19 | Keep company | fixture; slope walks are AIC-212 |
| A20 | Collect | fixture (`VerifyCollectionContracts`) |
| A21 | Weapons | fixture (`VerifyAttackOutcomes`) |
| A22 | Recover | fixture (recovery and sealed-pocket company) |
| A23 | World-edit bounds | fixture (home protection in gathering) |
| A24 | Movement abilities | not built — written dependency list only |
| A25 | Doors | fixture (`VerifyDoorPassage`); route search still treats a closed door as a wall |
| A26 | Downing | fixture (`VerifyDowningAndRevival`) |
| A27 | Chop | fixture (`VerifyGatheringCooperation`) |

## J01–J16

Held in the combat, safety, reunion and courtesy fixtures named above. Matched native scenes exist. Held-out live play does not.

## What P14 still needs from a person

A recorded playtest of this packaged build, read with SessionReport against a schema-0.30 capture. Owner look at the family/activity notch. Slope walking (AIC-212). G1 if ceiling hops on two-tile tops matter in play.
