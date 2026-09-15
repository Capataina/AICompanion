# Brain — observe, choose a useful place, and request movement

The coordinator records brain execution separately from completed choice evaluation. Early recovery and downed paths can own a fresh control response while retaining an older ordinary action and score board. Every completed chooser comparison receives an identity and source tick; diagnostics must keep those meanings separate when attributing tool effects or skipped selection.

No valid evaluated candidate produces an explicit absent activity and an ordinary Hold request. It does not select a numerically invalid last entry. Independent hand resolution and the early recovery path still run; the absence is not a fourth behaviour.

The chooser delegates current-activity ownership to `Infrastructure/Selection/OwnCurrentActivity.cs`. Ordinary execution marks that owner executing; follow recovery suspends it before taking movement, and the NPC downed path suspends it even when the brain does not run. A later comparison decides whether that purpose remains useful. Suspension records interruption rather than failed work, and it does not itself certify the recovery's outcome.

The brain turns one shared world observation into a body intent each tick. It does not move the NPC: `CoordinateBrainTick.cs` asks `Infrastructure/Movement/CoordinateMovement.cs` for controls and the motor in `Infrastructure/Movement/TerrariaIntegration/` applies those controls. The separation gives every body-changing decision one route through the same movement interface, while activities remain independent of route planning and engine movement details.

```
Brain/
├─ CLAUDE.md
├─ CoordinateBrainTick.cs
├─ Activities/                 the seven competing jobs and the contract they implement
├─ SharedBehaviours/           can take the body without winning a family comparison
│  ├─ Safety/                  the hit prediction the evade layer reads
│  └─ Recovery/                distant flight home
└─ Infrastructure/             how those get done — observation, scoring, places, movement,
                              tools, aiming, grants and recording; its own file lists them
```

Weapons stay in `Companion/Weapons/`. The brain grants a free hand; the arsenal chooses target and weapon.

## One tick has one direction of flow

Planning consumers share a soft deadline and retain unfinished work. There is no Survival behaviour and no environmental escape: survival was folded into shared safety, and the escape went on 15 September 2026 when every liquid became air to the orb, so a folder file or comment still naming either is naming something that no longer exists. Avoiding a hit is not a behaviour either, and it takes nothing: it is the evade layer movement applies to whatever the tick's owner asked for, described below. Hands still resolve after that movement choice. The coordinator stamps the current engine tick when it runs, so diagnostics distinguish fresh decisions from the stale state intentionally left while the companion itself is downed. Player death no longer suspends its decisions.

```
Observation ──► Safety ──► Movement ──► Companion motor
     │              │            ▲
     └─► Selection ─► Position ──┘
              │
              └─► Interactions and the arsenal

hands: arsenal fires after movement whenever no work tool owns the arm
```

`Observation.Senses` is rebuilt first. Safety then predicts collisions and takes nothing, and selection scores every activity from the same facts; the winner acts and returns a kind of place, position selection chooses a point, and movement plans a route or hovers. Before the controls reach the motor they pass through the evade layer, which bends them away from a predicted hit and otherwise leaves them untouched, so avoiding damage sits on top of whatever the body is doing: the activity keeps its attempt and its hands, and the grant names the bent tick `evade`. The layer replaced a reflex and a combat-spacing search that each suspended the activity to take the body, which is the shape the owner ruled out on 15 September 2026; `SharedBehaviours/Safety/CLAUDE.md` carries why. The motor is the only writer to the live NPC body. Movement outcomes return to the next tick only as observed facts such as a stranded body, never as a lower stage changing a higher stage’s decision.

Ordinary selection prepares candidates before comparison. The common evaluator supplies their values, each purpose family nominates its best positive-value child, and the parent chooses among those three nominations. An empty family nominates nothing; an entirely empty board has no ordinary activity. Keeping company combines reunion and hovering beside the player without changing purpose identity between methods. Collection compares known drops with uncertain pot contents as opportunities under one activity. The seven ordinary activities are mining, chopping, hunting, guarding, lighting, collecting and keeping company.

Every branch returns a movement request and hand permission to the common finaliser. Ordinary travel with its evade bends and recovery flight therefore share one motor application and a retained grant describing its actual AI-phase output. The downed lifecycle enters that finaliser without running ordinary selection. The grant does not certify the subsequently integrated motion or a productive native effect.

Body-progress observation and ordinary-activity observation have separate ownership. The finaliser can observe recovery movement while the ordinary activity is suspended; it asks the activity owner to deliver the ordinary outcome callback only when that activity is executing. A retained label cannot charge an interrupted hunt for time spent flying home.

Companionship observation also precedes the early recovery branch. Accumulated observed separation belongs to the shared reunion assessment rather than the current activity, so a label change or skipped comparison cannot reset it. Ordinary comparison uses that evidence with departure and estimated return time to price additional optional work; protection and shared safety keep their separate purposes.

The hands are independent of the feet. The arsenal may fire while following, guarding, looting, wandering or avoiding a hit; hunting only asks the feet to approach a firing position. Chopping and mining reserve the hand through coherent work phases, including cooldown gaps, while approaching work leaves it available. The torch fills a free hand in darkness. Downed grants revoke weapon permission. Navigation timing measures control preparation; finalisation timing separately includes motor application, compatible arsenal use and outcome observation.

Distant-follow recovery is an explicit coordinator branch outside the contact. A selected activity must issue a WithPlayer reunion request with an available hand; proximity of a work or combat destination to the player cannot authorise flight. Beyond the configured recovery distance, the coordinator interrupts the current route and asks the motor for continuous flight until a clear arrival near the live owner. Recovery owns the feet while independent weapon targeting continues; it cannot teach the archive a route. Ordinary following keeps separate horizontal and vertical comfort limits, and they are no longer a box of its own: both are the intent region's two half-sizes, which is why they differ and why they grow with the player's lead. Navigation reaching a waypoint alone does not establish that companionship has arrived — arrival is a settled state, earned at rest over a rescore, so a tick coasting through the region is not one.

## Choice is utility, not a priority chain

Each activity returns a score whose considerations multiply, so any zero vetoes it. The incumbent receives a commitment bonus and long trips are discounted by the observed threat horizon. Guard urgency can exceed a committed ordinary action; it lives in `Infrastructure/Selection/BehaviourWeights.cs`, which is the source for player-feel tuning. Activities are opportunistic: the companion follows loosely and helps with nearby work the player is already doing. Player-directed missions were abandoned. The unbuilt mastery tree may later change what a move costs the body — a faster pace or a dash — and never which activity is available.

## Movement is one shared system with an engine adapter

`Infrastructure/Movement/` owns the orb's contact, the free-space graph and its search, the route and the steering law, the terrain record and the Terraria adapter with the motor. The core is game-free and compiles into `Tools/NavReplay` without Terraria; the live companion adds the adapter and motor. There is one body: the contact the motor runs is the contact every headless tool runs, so a property proved offline is a property of the body the game moves, and the native suite adds only that the same contact reads Terraria's own tiles correctly. Gameplay comfort still needs playtest evidence.

The shared public surface is `CoordinateMovement` for requests and `MovementQueries` for geometry. Reflexes and position selection ask it questions or submit intent; none writes controls or reaches into the clearance field, the navigator or the motor. Activities and interactions are narrower still: they may carry a `Reachability.Reach` verdict around but may not run a search to obtain one, because the reach sense already holds the answer, and `Tools/check-navigation-boundary.sh` refuses the search rather than trusting anyone to have read this. The motor applies one resolved control set and tracks what the engine actually did.

## Traps

- Do not put another movement writer in a behaviour, reflex or Terraria callback. A direct position or velocity write bypasses the motor and makes telemetry’s movement evidence incomplete.
- `PlayerDanger` and `SelfDanger` are not interchangeable. A player needing defence is different from the companion entering a losing fight.
- Scores multiply. Raising one term cannot recover a zero from reachability, sight or another veto.
- A route replay checks the portable system. It is diagnostic evidence, not live engine parity.
- A tool does work only after a behaviour selected it. Interactions never decide what the companion should do next.

## Senses: observation becomes a shared interface

`Observation.Senses` holds the facts every consumer reads rather than derives — the tile beneath the body, the player's position and threat level — and three of those facts are extracted senses that no consumer is allowed to compute for itself:

```
Senses.Light   how bright a place is          a field, queried about somewhere
Senses.Intent  where the player is going      a region with a gradient
Senses.Reach   where the body can get to      two floods, one region and one exception
```

`Senses.Intent` is the newest and it is the one that makes a whole class of question have a single answer: every "how far from the player" measure in the brain reads that one region, and reads to its **centre** rather than to its boundary, because each of those callers already subtracts a comfortable distance of its own and a distance that is zero inside the region subtracts that comfort twice. The region's centre is the player's feet plus a lead of his own observed pace, so the question it answers is where he is going rather than where he was.

Two of the three are rebuilt in `Senses.Update`, at the top of the tick. `Senses.Reach` is not: position selection's resolve refreshes it, because the flood's unit of time is the rescore and its rules are set per request by the brain tick, so a flood run at the top would answer every consumer under the previous tick's rule. That is why the reach sense's unit of time is the rescore rather than the game tick, and why a consumer asking during a hold gets a region nobody is growing.

Light and reach each answer in three values rather than two, and that middle answer is the reason those two extractions were worth making: a tile missing from an unfinished flood is *not yet known* rather than unreachable, and a place the engine has not lit is *unread* rather than bright. Each caller used to rebuild that distinction from a completeness flag and a membership test, and could get it wrong. The rule that optional work does not start on an unanswered search is expressible in the type instead of reconstructed per caller. `Infrastructure/Observation/CLAUDE.md` derives all three, including the two-floods geometry, the carried-light discount and the refresh circle the first flood of a companion's life arrives through.

## Current state — 2026-09-14

Three purpose families — Combat (hunting, guarding), Gathering (mining, chopping), NearbyAssistance (lighting, collecting, keeping company) — each nominate their best offer; the parent compares those three. Lighting is a dark-region job that works sites from the player's own darkness and chains them before returning. Light, the player's intent region and reach are the three senses every consumer reads; optional work does not start on an unanswered search (mining and hunting publish Unknown at zero). The intent region arrived last and replaced a shape rather than adding one: the symmetric follow box on the player's current feet is gone, and with it the missing quantity that made a moving player no reason at all to move. The reactive floor was deleted because the clearance search beside it had already replaced its output — its two guard conditions were exact complements, so every control it produced was discarded before it reached the motor. The walk no longer raises its own jump either, and **nothing replaced that jump**: a body meeting a shape on a proven walk is a divergence, and the navigator already answered a divergence by pricing the step, counting a strike and replanning from the live state. The jump was a second, private answer sitting in front of that path. One change from the same build crosses this whole tree rather than sitting in a folder: the world-global terrain revision counter is gone, and a terrain edit now invalidates a retained search only where that search actually looked. Every system that retains work across ticks — the reach sense's two floods, the route planner's queries — therefore keeps its own sensitivity set, and a player mining a screen away no longer restarts a search that never looked there. The portable movement system remains diagnostic while live play remains the gate.
