# Brain — observe, choose a useful place, and request movement

The coordinator records brain execution separately from completed choice evaluation. Early recovery and reflex paths can own a fresh control response while retaining an older ordinary action and score board. Every completed chooser comparison receives an identity and source tick; diagnostics must keep those meanings separate when attributing tool effects or skipped selection.

No valid evaluated candidate produces an explicit absent activity and an ordinary Hold request. It does not select a numerically invalid last entry. Independent hand resolution and the existing early safety/recovery paths still run; the absence is not a fourth behaviour.

The chooser delegates current-activity ownership to `BehaviourSelection/OwnCurrentActivity.cs`. Ordinary execution marks that owner executing; reflex and follow recovery suspend it before taking movement, and the NPC downed path suspends it even when the brain does not run. A later comparison decides whether that purpose remains useful. Suspension records interruption rather than failed work, and it does not itself certify the safety response's outcome.

The brain turns one shared world observation into a body intent each tick. It does not move the NPC: `CoordinateBrainTick.cs` asks `SharedMovementSystem/CoordinateMovement.cs` for controls and the motor in `SharedMovementSystem/TerrariaIntegration/` applies those controls. The separation gives every body-changing decision one route through the same movement interface, while behaviours remain independent of route planning and engine movement details.

```
Brain/
├─ CLAUDE.md                    this guide
├─ CoordinateBrainTick.cs       tick coordinator and the independent hands step
├─ BehaviourWeights.cs          player-visible behaviour tuning in one authority
├─ WorldObservation/            facts derived once from Terraria
├─ CombatReflexes/              imminent-collision assessment, before selection
├─ SharedSafety/                independent environmental escape and retained collision responses
├─ BehaviourSelection/          utility choice among the behaviour families
├─ ActivityCoordination/        final movement application, hand grants, incidental interactions and distant recovery
├─ Behaviours/                  shared activity contract and work-policy readers
├─ PurposeFamilies/             seven ordinary activities grouped by purpose
├─ PositionSelection/           turn a position request into a useful feet tile
├─ SharedMovementSystem/        core simulation, route planning, execution and Terraria adapter
├─ ProjectileAiming/            trajectory solve shared by weapons and position selection
├─ WorldInteractions/           chop, mine, doors, torch and crack rendering
└─ BehaviourDiagnostics/        overlay, telemetry, scenario capture and session map
```

## One tick has one direction of flow

Planning consumers share a soft deadline and retain unfinished work. Survival can supply a safe-state predicate directly to movement when the head needs air; the same controller searches legal controls for ordinary local clearance. Hands still resolve after that movement choice. The coordinator stamps the current engine tick when it runs, so diagnostics distinguish fresh decisions from the stale state intentionally left while the companion itself is downed. Player death no longer suspends its decisions.

```
WorldObservation ──► CombatReflexes ──► SharedMovementSystem ──► Companion motor
       │                     │                  ▲
       └─► BehaviourSelection ─► PositionSelection ┘
                              │
                              └─► WorldInteractions

hands: arsenal fires after movement whenever no work tool owns the arm
```

`WorldObservation.Senses` is rebuilt first. Combat reflex assessment supplies predicted unsafe body states. SharedSafety may then suspend ordinary selection for environmental escape or collision avoidance, using shared movement to prepare the controls and retaining its response through the relevant aftermath. Otherwise selection scores every behaviour from the same facts; the winner acts and returns a kind of place, position selection chooses a tile, and movement plans or holds. The motor is the only writer to the live NPC body. Movement outcomes return to the next tick only as observed facts such as a stranded body, never as a lower stage changing a higher stage’s decision.

Ordinary selection prepares candidates before comparison. The common evaluator supplies their values, each purpose family nominates its best positive-value child, and the parent chooses among those three nominations. An empty family nominates nothing; an entirely empty board has no ordinary activity. Environmental escape and combat spacing run independently through SharedSafety even with no ordinary offers. Keeping company combines reunion and relaxed nearby movement without changing purpose identity between methods. Collection compares known drops with uncertain pot contents as opportunities under one activity. The seven ordinary activities are mining, chopping, hunting, guarding, lighting, collecting and keeping company.

Every branch returns a movement request and hand permission to the common finaliser. Ordinary travel, reflex avoidance, survival escape and recovery flight therefore share one motor application and a retained grant describing its actual AI-phase output. The downed lifecycle enters that finaliser without running ordinary selection. The grant does not certify the subsequently integrated motion or a productive native effect.

Body-progress observation and ordinary-activity observation have separate ownership. The finaliser can observe safety movement while the ordinary activity is suspended; it asks the activity owner to deliver the ordinary outcome callback only when that activity is executing. A retained label cannot charge an interrupted hunt for time spent escaping.

Companionship observation also precedes the early recovery and safety branches. Accumulated observed separation belongs to the shared reunion assessment rather than the current activity, so a label change or skipped comparison cannot reset it. Ordinary comparison uses that evidence with departure and estimated return time to price additional optional work; protection and shared safety keep their separate purposes.

The hands are independent of the feet. The arsenal may fire while following, guarding, looting, wandering or avoiding a hit; hunting only asks the feet to approach a firing position. Chopping and mining reserve the hand through coherent work phases, including cooldown gaps, while approaching work leaves it available. The torch fills a free hand in darkness. Downed grants revoke weapon permission. Navigation timing measures control preparation; finalisation timing separately includes motor application, compatible arsenal use and outcome observation.

Distant-follow recovery is an explicit coordinator branch outside the route graph. A selected activity must issue a WithPlayer reunion request with an available hand; proximity of a work or combat destination to the player cannot authorise flight. Beyond the configured recovery distance, the coordinator interrupts the current route and asks the motor for continuous flight until a clear arrival near the live owner. Recovery owns the feet while independent weapon targeting continues; it cannot teach the archive a route. Ordinary following uses separate horizontal and vertical comfort limits; navigation reaching a waypoint alone does not establish that companionship has arrived.

## Choice is utility, not a priority chain

Each behaviour returns a score whose considerations multiply, so any zero vetoes it. The incumbent receives a commitment bonus and long trips are discounted by the observed threat horizon. Guard urgency can exceed a committed ordinary action; it lives in `BehaviourWeights.cs`, which is the source for player-feel tuning. Shared environmental escape does not need to win that comparison. Behaviours are opportunistic: the companion follows loosely and helps with nearby activities the player is already doing. Player-directed missions were abandoned. The unbuilt mastery tree may later give the movement system capabilities such as air jumps, dash, swimming and flight.

## Movement is one shared system with an engine adapter

`SharedMovementSystem/` owns simulation, movement abilities, path search, execution, cached terrain facts and the Terraria adapter. The planner and offline replay use the portable core; the live companion uses the Terraria adapter and motor. EngineReplay compares the native adapter against Terraria’s own NPC collision path, including liquid transitions. That is evidence about controlled collision fixtures, while portable replay remains an approximation and gameplay comfort still needs playtest evidence.

The shared public surface is `CoordinateMovement` for requests and `MovementQueries` for geometry and reachability. Reflexes, position selection and behaviours ask it questions or submit intent; none writes controls or reaches into grid, navigator or motor state. The motor applies one resolved control set and tracks what the engine actually did.

## Traps

- Do not put another movement writer in a behaviour, reflex or Terraria callback. A direct position or velocity write bypasses the motor and makes telemetry’s movement evidence incomplete.
- `PlayerDanger` and `SelfDanger` are not interchangeable. A player needing defence is different from the companion entering a losing fight.
- Scores multiply. Raising one term cannot recover a zero from reachability, sight or another veto.
- A route replay checks the portable system. It is diagnostic evidence, not live engine parity.
- A tool does work only after a behaviour selected it. Interactions never decide what the companion should do next.

## Current state — 2026-09-09

The responsibility migration has replaced the old `DecisionMatrix`, `Actions`, `Aiming`, `Work` and `Debug` folder boundaries. The new folders above are the current ownership model. The companion has no missions; loose following and opportunistic help are the product direction. Native adapter parity is exercised headlessly; comfortable travel through real play remains the playtest gate.
