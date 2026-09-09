# Multi... Player?

An AI companion for Terraria, as a singleplayer tModLoader mod. It travels with you, fights beside you and joins nearby work. It is intended to feel like another player: free to stop for something useful, close enough to help when you need it. Independent gathering missions are outside that design; a mastery tree will expand its abilities over time.

Early development. Today: `/companion` in chat spawns a companion that looks like the female player, follows you, wanders when idle, shares your max life and defence, gets downed instead of dying (stand beside it for three seconds to revive), chops the tree next to yours when you chop, shoots a bow at whatever is on screen, and comes back with you every time you enter a world. Its health bar sits top-centre; drag it anywhere, right-click to reset. While a companion is up, enemies spawn twice as often, because two of you clear a screen twice as fast.

## Building

From this folder, `sh Tools/verify.sh` compiles against the installed tModLoader and checks the movement boundary. With the game closed, `dotnet build` also packages the mod for the next launch. A fresh launch avoids the unresolved in-game reload hang.

The complete feature lives under `Companion/`, grouped into its brain, character body, weapons, inventory, player and enemy integration, map integration and HUD. Movement is shared by travel, work and combat through `Companion/Brain/SharedMovementSystem`. Its Terraria integration predicts using the game's collision helpers; `Tools/EngineReplay` compares those predictions with the engine's own NPC collision routine without opening a window. `Tools/NavReplay` exercises recorded terrain scenarios and the movement controller.

Every companion session writes telemetry automatically. After leaving the world, `dotnet run --project Tools/SessionReport -- --timeline Telemetry` prints the chronological player and companion record, followed by diagnostic findings. The report distinguishes measured movement and hits from inferred hesitation; activity outside the recorded game state is not a video replay.
