# Behaviours — what the companion can be doing, by family

One file per action, grouped by the kind of thing it is, so the list never becomes a flat pile. The contract is `CompanionAction.cs` here: `Score` (a product of considerations in 0..1, zero for "not now"), `ForecastTicks` (how long it keeps the companion away from the player), `Enter`/`Exit`, and `Execute`, which acts (fire, swing, set the held item) and returns a `PositionRequest`; an action never moves the NPC, the navigator does. Internal steps stay as private state on the action.

```
Behaviours/
├─ CLAUDE.md
├─ CompanionAction.cs   the base and ActionContext (the companion, observations, player, and stranded fact)
├─ Companionship/       player protection and distant-reunion recovery
├─ Combat/              pursuing a useful attack opportunity
└─ Work/                doing what the player is doing: chop, mine
```

## Adding an action, or a family

The chooser invokes `Prepare` before evaluating each adapter. Preparation observes the world, updates discovery caches and captures candidate values. `Score()` and `ForecastTicks()` accept no world context and read only prepared values; comparing candidates cannot advance retry timers, prune jobs or rescan terrain. Execution must revalidate native availability before acting on a captured candidate. This separation does not make discovery itself pure or remove the need for a full target-bound offer contract.

The shared activity owner invokes Enter/Exit and Suspend. Default suspension releases the physical method through Exit and then preserves any surviving purpose identity for continuation. A resumed adapter is prepared and entered again; it must not assume the old working pose or jump remains valid. Switching to a different executor releases the old admission independently of whether its Exit implementation retains discovery caches.

The owner invokes ObserveOutcome after control and hand resolution only for an executing ordinary activity. A suspended activity receives no such progress callback while safety or recovery acts. This separates an interrupted job's failure accounting from the shared body's continuing movement observations.

`IsExcursion` declares whether an action is optional time away from the player. It defaults to true, so newly added opportunistic actions automatically yield to regroup pressure. Keeping company and protection opt out because they maintain companionship or provide intervention. Keeping company lives in PurposeFamilies/NearbyAssistance and owns reunion, rest and nearby movement as one activity. Environmental escape and combat spacing belong to SharedSafety outside ordinary activity selection. Independent weapon firing remains outside the movement competition; a follow request can still shoot while travelling.

A behaviour declares its purpose family and is listed once in `../BehaviourSelection/ChooseBehaviour.cs`. Each family nominates a concrete positive-value child; the parent compares those nominations. Equal values use the same stable registration index at both levels. Behaviours are deliberately opportunistic: they follow the player loosely and act on what is already happening nearby. Player-directed missions were abandoned; mastery may later unlock movement abilities such as air jumps, dash, swimming and flight, but none is built. The tool a work behaviour drives lives in `../WorldInteractions/`, never inside the behaviour.
