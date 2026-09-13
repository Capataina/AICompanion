# Behaviour contract — preparation, execution and retained admission

`CompanionAction.cs` defines the shared contract used by activities in `../PurposeFamilies/`. Prepare captures an opportunity; Score and ForecastTicks expose its value and time estimate; Enter, Exit and Suspend manage execution transitions. Execute may operate an admitted tool and returns a PositionRequest. It never moves the NPC or owns independent weapon choice. Internal method state stays with the activity that uses it, while the common activity owner controls which executor receives the tick. A zero value means no selectable offer; protection's urgency may exceed the ordinary utility band.

```
Behaviours/
├─ CLAUDE.md
├─ CompanionAction.cs            the base and ActionContext (the companion, observations, player, and stranded fact)
├─ ClassifyOffersAndAttempts.cs  offer eligibility, attempt status and the immutable attempt outcome record
└─ Work/                         thin per-character work-policy readers
```

The old Combat, Companionship, Gathering and Survival directories under this folder are gone. The seven activities live in `../PurposeFamilies/`.

## An offer says what it is before it says what it is worth

Every `Prepare` classifies its offer as usable, unresolved, known-unusable, forbidden by policy, or no opportunity, with a short reason, beside the value it captures. The two questions are separate on purpose: a hunt can be worth a lot and have no reachable firing position, and a mine can be worth nothing this tick while its ore is perfectly usable. Only usable and unresolved offers may carry positive value, and the evaluator rejects any other combination as an evaluation error, so an adapter that forgets to classify reads as no opportunity and cannot win on a stale number. That strictness is deliberate and has already caught two test probes that scored without saying what they offered. Unresolved means a bounded investigation — an ore approach the search has not decided, a firing position whose reachable region has not settled, a guard destination that method admission has not yet queried — and never proven work.

A sixth state, deferred, is written only by the chooser's query scheduler for an activity it did not prepare because its family's preparation share was spent. An activity never classifies itself as deferred, and a deferred row says nothing about the world.

The reason strings are the activity's own account of why, not a second decision: mining reads its discovery status, hunting its last rejection, lighting whether the setting, the daylight or the torch supply is what refuses it. A policy prohibition is a player setting; a known-unusable method is the world or the kit refusing (no firing position, no torch supply, a pick too weak); no opportunity is simply nothing found.

## An attempt ends with the activity's own conclusion

`BeginAttempt` is called by the activity owner when an attempt opens, and `ConcludeAttempt(productiveEffects)` when selection replaces it, before Exit clears the method state it reads. An activity clears its conclusion evidence in `BeginAttempt` and records evidence at the moment it happens (a job ending, a jump losing its take-off, a strike removing the trunk), so a conclusion can only read what this attempt produced. Ticks cannot make that distinction: the coordinator prepares, selects and begins execution inside one engine tick, so a job ended by this tick's preparation and a job ended during this attempt carry the same tick.

A conclusion is a status, a cause and an attribution. Status says what happened to the purpose; attribution says who produced a completion, because the evidence an activity holds usually proves the companion contributed rather than that it finished. Companion means its own observed effect finished the job (every tracked ore site removed by its strikes, its own strike felling the trunk, its native pot or torch call). Shared means its effects contributed and something else finished it. Unattributed means the purpose ended and nothing observed names who did it — a pursued enemy dying, a guarded threat disappearing, a drop leaving the world. The same cleared coordinates with no effect from this attempt are invalid, an expired progress window or an abandoned approach is failed, and a revoked permission or changed material is invalid. Keeping company concludes executed and never complete. The base implementation claims nothing it cannot see: partial with effects, attempted without. Interruption never reaches this method — the owner records it directly — so no activity can reinterpret a projectile dodge or a downing as its own failure.

The chooser resets every activity's classification to no opportunity immediately before preparing it, and both prepared records default to no opportunity, so a preparation path that forgets to classify fails closed everywhere rather than keeping the previous tick's eligibility.

## Adding an action, or a family

The chooser invokes `Prepare` before evaluating each adapter. Preparation observes the world, updates discovery caches and captures candidate values. `Score()` and `ForecastTicks()` accept no world context and read only prepared values; comparing candidates cannot advance retry timers, prune jobs or rescan terrain. Execution must revalidate native availability before acting on a captured candidate. This separation does not make discovery itself pure or remove the need for a full target-bound offer contract.

`PreparedTargetRejection` lets an activity validate captured non-entity facts before activation without discovering a replacement. Mining and chopping use it for bound tile coordinates and materials; their execution methods enforce the same binding. A rejection names invalidated availability rather than changing the original utility estimate.

PreparedPositionRequest exposes an attack activity's captured positioning method without executing it. The chooser asks the shared positioner to admit that method before activation. Method availability is separate from raw utility: a threat can justify protection while no usable intervention destination has been established. Other activities retain their native access contracts rather than fabricating an attack-position request.

The shared activity owner invokes Enter/Exit and Suspend. Default suspension releases the physical method through Exit and then preserves any surviving purpose identity for continuation. A resumed adapter is prepared and entered again; it must not assume the old working pose or jump remains valid. Switching to a different executor releases the old admission independently of whether its Exit implementation retains discovery caches.

The owner invokes ObserveOutcome after control and hand resolution only for an executing ordinary activity. A suspended activity receives no such progress callback while safety or recovery acts. This separates an interrupted job's failure accounting from the shared body's continuing movement observations.

`IsExcursion` declares whether an action is optional time away from the player. It defaults to true, so newly added opportunistic actions automatically yield to regroup pressure. Keeping company and protection opt out because they maintain companionship or provide intervention. Keeping company lives in PurposeFamilies/NearbyAssistance and owns reunion, rest and nearby movement as one activity. Environmental escape and combat spacing belong to SharedSafety outside ordinary activity selection. Independent weapon firing remains outside the movement competition; a follow request can still shoot while travelling.

A behaviour declares its purpose family and is listed once in `../BehaviourSelection/ChooseBehaviour.cs`. Each family nominates a concrete positive-value child; the parent compares those nominations. Equal values use the same stable registration index at both levels. Behaviours are deliberately opportunistic: they follow the player loosely and act on what is already happening nearby. Player-directed missions were abandoned; mastery may later unlock movement abilities such as air jumps, dash, swimming and flight, but none is built. The tool a work behaviour drives lives in `../WorldInteractions/`, never inside the behaviour.
