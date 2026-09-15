# Doors — invalidate movement facts when the game opens a passage

```
Doors/
├─ CLAUDE.md
└─ DoorOpener.cs   asks Terraria’s door helper to open or close the obstruction
```

The game door helper bypasses the player-work tile hooks, so a successful open or close announces every door row to the terrain record. Otherwise the retained searches, the route and the clearance field keep the closed door as a wall after the motor has made an opening.

Terraria decides whether a door opens, not this folder. `WorldGen.OpenDoor` swings the door toward the side the body is going when that column is clear over the door's three rows, and inward when it is not; it refuses only when both swing columns are blocked, and it always refuses the locked Lihzahrd temple style whatever the columns hold (measured headlessly on 2026-09-13; Plantera does not unlock it, a temple key converts the tile). `OpenDoor` announces nothing itself, which is why `DoorOpener` calls `TerrainChanges.Changed` after opening and after closing.

`Tools/EngineReplay/Lifecycle/VerifyDoorPassage.cs` holds the contract on native tiles: the helper's swing matrix; a reach question across a closed door that turns from no to yes the moment the real interaction opens it and back to no when it closes, with the terrain record's clock unmoved, so only the announcement can move it; a closed door that is the only passage opened and flown through by the whole brain; and a locked door that is never opened and is pressed against no longer than a stone tile in the same doorway, both with no detour and routed around through a passage under the wall. Removing the two announcements turns the reach question red while the whole-brain passage still passes, because the motor's contact reads the live tile whatever the retained search believes.

## What a reader will get wrong here

The planner does not route through doors. A closed door is solid to the orb's contact, the flood and the clearance field read the same rule, so to the planner a closed door is a wall. A door is passed only when following flies the body into it and this interaction opens it; the announcement then lets the next search find the route. So beside any other route the companion flies around a door it could have opened: with a three-row passage under the wall, the walker's whole brain went under and never opened the door (measured 2026-09-13), and nothing about the orb changes that. Making doors passages to the planner would change what shared movement's terrain representation means (an openable door read as free by the flood and the contact, against a helper whose rule lives in the game), and that belongs to shared movement rather than to this folder. It is worth doing when a playtest shows the companion taking long detours around the player's own doors.

Tall gates use the same interaction (`ShiftTallGate`) and have no native fixture.
