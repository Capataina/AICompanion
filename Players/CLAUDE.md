# Players — per-character state and input

`CompanionPlayer.cs` is the ModPlayer: the has-companion flag (auto-spawn in `OnEnterWorld`), the health bar position, the bag, the F6 overlay toggle in `ProcessTriggers`, and the right-click on the companion that opens the bag in `PostUpdate`. Singleplayer only: the one instance that matters is `Main.LocalPlayer`'s.
