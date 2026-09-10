# Diagnostics configuration — optional player-facing observation

```
DiagnosticsConfiguration/
├─ CLAUDE.md                     this guide
└─ CompanionDiagnosticsConfig.cs native client-side Mod Configuration switches
```

This is the tModLoader settings surface for the brain inspector and local recording. It is separate from character behaviour preferences in `PlayerIntegration` and the companion profile card. Both capabilities are enabled by default for this development build; players can disable either independently. The inspector key is rebound through the game's Controls menu. Disabling the inspector hides it and clears retained traces immediately. Disabling recording closes the current stream immediately; enabling it takes effect on the next world entry so one file retains one clear session boundary.

Help text belongs to `Localization/en-US_Mods.AICompanion.hjson`. It names the local telemetry directory and tells the player to share the session files for a bug report; no data is uploaded automatically. Recording preferences never change the companion's gameplay decisions.
