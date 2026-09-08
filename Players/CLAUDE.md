# Players — per-character state and input

`CompanionPlayer.cs` is the ModPlayer: the has-companion flag (auto-spawn in `OnEnterWorld`), the health bar position, the bag, the overlay toggle in `ProcessTriggers` (logged), the right-click on the companion that opens the bag in `PreUpdate`, and a log line on world enter saying whether the companion spawned and how full the bag is. Singleplayer only: the one instance that matters is `Main.LocalPlayer`'s.
