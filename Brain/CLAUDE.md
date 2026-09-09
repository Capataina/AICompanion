# Brain — observe, choose a useful place, and request movement

The brain turns one shared world observation into a body intent each tick. It does not move the NPC: `CoordinateBrainTick.cs` asks `SharedMovementSystem/CoordinateMovement.cs` for controls and the motor in `SharedMovementSystem/TerrariaIntegration/` applies those controls. The separation gives every body-changing decision one route through the same movement interface, while behaviours remain independent of route planning and engine movement details.

```
Brain/
├─ CLAUDE.md                    this guide
├─ CoordinateBrainTick.cs       tick coordinator and the independent hands step
├─ BehaviourWeights.cs          player-visible behaviour tuning in one authority
├─ WorldObservation/            facts derived once from Terraria
├─ CombatReflexes/              imminent-collision assessment, before selection
├─ BehaviourSelection/          utility choice among the behaviour families
├─ Behaviours/                  follow, combat, gathering, survival and work behaviour
├─ PositionSelection/           turn a position request into a useful feet tile
├─ SharedMovementSystem/        core simulation, route planning, execution and Terraria adapter
├─ ProjectileAiming/            trajectory solve shared by weapons and position selection
├─ WorldInteractions/           chop, mine, doors, torch and crack rendering
└─ BehaviourDiagnostics/        overlay, telemetry, scenario capture and session map
```

## One tick has one direction of flow

```
WorldObservation ──► CombatReflexes ──► SharedMovementSystem ──► Companion motor
       │                     │                  ▲
       └─► BehaviourSelection ─► PositionSelection ┘
                              │
                              └─► WorldInteractions

hands: arsenal fires after movement whenever no work tool owns the arm
```

`WorldObservation.Senses` is rebuilt first. A combat reflex may then pre-empt selection: it supplies predicted unsafe body states, and shared movement picks avoidance controls. Otherwise selection scores every behaviour from the same facts; the winner acts and returns a kind of place, position selection chooses a tile, and movement plans or holds. The motor is the only writer to the live NPC body. Movement outcomes return to the next tick only as observed facts such as a stranded body, never as a lower stage changing a higher stage’s decision.

The hands are independent of the feet. The arsenal may fire while following, guarding, looting, wandering or avoiding a hit; hunting only asks the feet to approach a firing position. Chopping and mining are the exception because a swing and projectile cannot use the same arm. The torch fills a free hand in darkness, so it disappears during a tool swing and returns when the arm clears.

## Choice is utility, not a priority chain

Each behaviour returns a score whose considerations multiply, so any zero vetoes it. The incumbent receives a commitment bonus and long trips are discounted by the observed threat horizon. The urgency values for guard and survival form an ordered ladder because either must clear a committed lower rung; they live in `BehaviourWeights.cs`, which is the source for player-feel tuning. Behaviours are opportunistic: the companion follows loosely and helps with nearby activities the player is already doing. Player-directed missions were abandoned. The unbuilt mastery tree may later give the movement system capabilities such as air jumps, dash, swimming and flight.

## Movement is one shared system with an engine adapter

`SharedMovementSystem/` owns simulation, movement abilities, path search, execution, cached terrain facts and the Terraria adapter. The planner and offline replay use the portable core; the live companion uses the Terraria adapter and motor. This is deliberately an approximation boundary, not proof that offline physics and engine behaviour are identical. A live parity checker still needs playtest evidence; do not claim that a replay-proven route has been proven by the native body.

The shared public surface is `CoordinateMovement` for requests and `MovementQueries` for geometry and reachability. Reflexes, position selection and behaviours ask it questions or submit intent; none writes controls or reaches into grid, navigator or motor state. The motor applies one resolved control set and tracks what the engine actually did.

## Traps

- Do not put another movement writer in a behaviour, reflex or Terraria callback. A direct position or velocity write bypasses the motor and makes telemetry’s movement evidence incomplete.
- `PlayerDanger` and `SelfDanger` are not interchangeable. A player needing defence is different from the companion entering a losing fight.
- Scores multiply. Raising one term cannot recover a zero from reachability, sight or another veto.
- A route replay checks the portable system. It is diagnostic evidence, not live engine parity.
- A tool does work only after a behaviour selected it. Interactions never decide what the companion should do next.

## Current state — 2026-09-09

The responsibility migration has replaced the old `DecisionMatrix`, `Actions`, `Aiming`, `Work` and `Debug` folder boundaries. The new folders above are the current ownership model. The companion has no missions; loose following and opportunistic help are the product direction. Native adapter parity is exercised headlessly; comfortable travel through real play remains the playtest gate.
