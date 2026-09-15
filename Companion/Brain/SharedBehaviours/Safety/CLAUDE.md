# Shared safety retains its response independently of ordinary work

Safety runs after current danger observation and before ordinary activity selection. It can suspend any activity or act with no ordinary offers. It submits one request to Grants; it never applies the motor itself or chooses a weapon. Three responses, one owner: environmental escape, collision avoidance and combat spacing, each with an identity, a kind and a reason the record carries.

```
Safety/
├─ CLAUDE.md
├─ AssessImmediateThreats.cs   the predicted-collision predicate: where incoming hostiles and projectiles will be, against the orb's coasting body
├─ ChooseSafetyResponse.cs     response identity, which of the three runs, and the movement request each makes
├─ ReachEnvironmentalSafety.cs leaving water or lava: a retained flood, through the liquid, to the nearest dry cell
└─ CreateCombatSpace.cs        a state search toward lower enemy exposure
```

## Environmental escape is leaving the liquid, and nothing else

The orb has no breath: water and lava hurt it on contact, so touching either is the exposure and leaving it is the whole response. The escape is a retained flood over the corner graph that is allowed to cross the liquid the body is already in — a flood that refused wet corners would refuse the one the body stands on — toward the nearest free, dry cell, and the target is kept stable while the search runs so it finishes instead of restarting at every nearer pocket. Dry means the contact circle touches no tile that is a wet wall under the body's immunities, so an immunity the mastery tree grants ends the exposure without any change here. A body sealed inside a pool with no dry cell in reach reports that failure and keeps its purpose; nothing here grants immunity or manufactures a successful escape. Downing and recovery explicitly cancel safety ownership, and a replacement safety kind clears the shared body's retained state search, because its frontier was proved for the previous response's terminal condition.

## Collision avoidance is a dodge that flies

The reflex predicts each incoming hostile and projectile against the orb's own coasting body — the contact circle integrated forward through the same contact the motor runs, so a body drifting toward a wall is predicted where the wall will leave it — and while a collision is imminent the movement request is `AvoidThreats`: a handful of headings and a stop run forward through the contact against that predicate, the one that stays safe longest taken, ties broken by progress toward the player. The response holds only while the collision is imminent; there is no landing to wait for, because there is no ground. A body that flies dodges up and down as readily as back, which the walker's dodge could not, and the projectile-on-dry-floor reproduction the walker left red is the scene that shows it.

## Combat spacing seeks lower exposure through the same search

Combat spacing starts from the companion's own threat assessment and geometric exposure (`Positioner.PredictedExposureAt`, the orb's box against forecast hitboxes), independent of player danger and ordinary family offers, and only for a threat that will actually connect — a predicted overlap or a proven reach arriving within a short horizon, never mere proximity. It searches through the shared state search for a dry cell with exposure under the accepted threshold, retains its identity through the flight, suspends ordinary work, and grants an available hand. Environmental escape and imminent collision can replace it. A completed search with no safe cell ends explicitly and yields ordinary activity for a bounded retry interval; it does not manufacture a successful retreat or failed ordinary work.

The admission threshold uses the companion-danger estimate, whose damage and time proxies are not a calibrated prediction of effective health loss. The exposure threshold and the search's heuristic scale live in `../../Infrastructure/Selection/BehaviourWeights.cs`. The three responses compose rather than replace each other's purpose, and `Tools/EngineReplay/Combat/VerifySafetyAftermath.cs` holds the evidence: a wounded companion taking combat spacing still fires from its granted hands on ticks the retreat owns the feet; a projectile reflex suspends a guarding activity and the same activity identity resumes when the shot is gone.

The escape does not price liquid damage against a hostile shot: every escape state is vetoed where a predicted projectile box meets the body, so a shot hanging over the only exit is impassable rather than a cost weighed against the hurt. A priced trade between hit damage, liquid contact and the aftermath is open, and it needs the movement system's veto to become a cost rather than a predicate.

## Traps

- `ReachEnvironmentalSafety` is a movement escape and is not the reach sense in `../../Infrastructure/Observation/`, which every consumer reads once per tick; the escape's flood runs through liquid and the sense's never does.
- A reactive floor — a fallback whose trigger is the absence of the ordinary path's precondition — fires hardest while the planner is still thinking. One was deleted from the walker for producing controls on exactly the ticks nothing consumed them; anything reviving one answers what fact it fires on, and whether that fact is "the planner failed" or only "the planner has not finished yet".
