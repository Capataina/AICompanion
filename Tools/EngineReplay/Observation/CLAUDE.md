# Observation fixtures

Recorder lifecycle, God's Eye events, prepared comparison, family-offer matching, evidence scenes and brain-cost measurement. `--observation` and `--evidence-scenes` select this group. `VerifyFamilyOffers.cs` is P03's named selection contract.

## Brain cost

`--brain-cost` runs one seeded full-brain scenario (flat four-tile copper vein, player walking away then back) for 600 ticks with native collision advancing the body. Two measured runs under production millisecond allowances report every brain phase and AI time the brain did not account for (where recording lands), as p50/p95/max/mean with the three costliest ticks named. With `LimitPlanningWork.Unbounded` set, it runs twice with recording off and once on; exits 1 unless all three agree tick for tick on activity, request, applied controls, grant owner, hand grant and body position.

## Family allowance

The family-allowance fixture registers two gathering probes, a winning combat probe and a non-excursion probe, sets the chooser's share to zero and compares three times. It requires exactly one gathering child prepared per comparison with the other reported Deferred at zero value, the next comparison to start from the deferred sibling, incumbent and non-excursion probes to prepare every time, and a deferred sibling's retained higher value to lose. With the cost harness also reporting each family's preparation milliseconds and deferred count.

## Offer and attempt contracts

Offer and attempt contracts are exercised at three depths. The pure fixture requires a positive value beside a no-opportunity, policy-forbidden or known-unusable offer to be rejected, a zero-valued one to stay truthful absence, and an unresolved one to keep bounded value. A probe activity then drives the real activity owner through selection without execution, reselection, suspension and repeated suspension, resumption, replacement and empty selection.

## Travel episodes

`VerifyTravelEpisodes.cs` (`--travel-episodes`, and in the default run) is the producer half of SessionReport's journey checks: it drives the whole brain into a follow journey, kills the body inside it, waits out the self-revival and reads the recorded journey back. It counts the downed ticks itself while driving them, so the producer's figure is checked against an independent count of the same quantity rather than against its own arithmetic, and it asserts that the reported duration and the mean speed exclude them. The death has to land inside the journey, which closes around tick 65 in this scene once the body is inside the comfort box — a death after that measures nothing, and the fixture says so as a premise failure rather than passing.

## Evidence scenes

`--evidence-scenes[=<folder>]` is an instrument rather than a suite: it asserts nothing about behaviour, and it keeps the captures it writes. It drives four stalls, each already built by an existing fixture, through the whole brain, the real recorder and the real event writer for twenty seconds apiece. Each stall sits on one family's path: a hop take-off drowned, a drop sealed inside a dirt box, a zombie sealed in rock, the player sealed inside a box. For each it prints the capture path, row and occurrence counts, and actions recorded, so `dotnet run --project Tools/SessionReport -- <capture>` can be run on each.

## God's Eye events

`VerifyGodsEyeEvents` compiles the actual sparse event writer and its native NPC, projectile and terrain hooks with only test-local telemetry and mod stubs. It writes and parses a temporary JSONL session, then deletes it. The fixture proves sequence/schema/timestamp validity, normal session closure, snapshot-at-occurrence behaviour, reused NPC/projectile slot generations, shot-to-terrain correlation and tile dirtiness becoming a changed local terrain snapshot.

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
