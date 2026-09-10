# Player integration — persistent companion state, input and explicit recovery

```
PlayerIntegration/
├─ CLAUDE.md                 this guide
├─ PersistCompanionState.cs  `CompanionPlayer` fields plus stable save/load keys and world entry
├─ ConfigureCompanionPreferences.cs  per-character optional-work and follow-distance choices
├─ HandleCompanionInput.cs   saved inspector keybind and pre-item-use notch/card/bag input handling
├─ ObservePlayerEvents.cs    `CompanionPlayer` authoritative player-event observation
└─ CompanionCommand.cs       /companion setup and explicit recovery command
```

`CompanionPlayer` is one partial `ModPlayer` class split by responsibility. It owns state that belongs to the character across worlds: whether the companion has been introduced, bag storage and player-facing layout/input preferences. `PersistCompanionState.cs` retains the existing save keys while `HandleCompanionInput.cs` and `ObservePlayerEvents.cs` add no second player-state object. `CompanionPreferences.Current` is set to the entering character's saved instance before spawning, so existing static work readers cannot leak another character's choices between worlds.

`/companion` introduces a companion when none exists and is the explicit player recovery action when one does. Autonomous companion behaviour never teleports. The command is deliberately the only player-initiated exception for a body stranded where the player cannot yet rescue it.

This folder forwards player facts to the brain and HUD but does not own the NPC. `CharacterBody/` creates and controls the live body; `Inventory/` defines cargo behaviour; `HeadsUpDisplay/` draws the notch; diagnostics owns its own overlay input contract.

Distance preferences separate ordinary comfort, new activity admission and ongoing recovery allowance. Standard and Free admit work out to their recovery radius; Close deliberately tightens new work further while retaining a wider recovery threshold, matching the user's close-mode example. Ongoing allowance derives from recovery, so changing the preference moves both safety boundaries coherently. Values are defined in `ConfigureCompanionPreferences` and `BehaviourWeights`, never copied into behaviour-specific gates.

`ObservePlayerEvents.OnHurt` records Terraria's final hurt calculation in the continuous telemetry and the occurrence stream. The callback happens before health subtraction, so the occurrence carries observed pre-hit life and explicitly expected remaining life, not an observed after-hit value. PostHurt alone would miss fatal hits because it runs only for survivors. Subsequent continuous samples supply observed health. Recorder failures must not interrupt the player's hurt lifecycle.
