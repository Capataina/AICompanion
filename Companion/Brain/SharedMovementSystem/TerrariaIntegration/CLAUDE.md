# Terraria integration — live terrain, controls and native collision prediction

This is the only shared-movement folder allowed to name Terraria types. It adapts the real NPC and tilemap to the core’s body-state, control and terrain contracts. Behaviours never call collision helpers or write velocity themselves.

```
TerrariaIntegration/
├─ CLAUDE.md                       this boundary and its verification
├─ ReadGameTerrain.cs              GameTileWorld reads shape and pass-through separately
├─ SimulateTerrariaBody.cs          predicts one controlled tick with native collision helpers
├─ ApplyControlsToCompanion.cs      CompanionMotor applies the resolved packet and measures the preceding tick
└─ TrackTerrainChanges.cs          invalidates route facts on placement, breaking, hammering, wiring and world changes
```

The motor observes the NPC at AI entry, before stepping helpers can change its position. Applying controls first predicts the next engine result, then calls the shared ability transition and the real step helpers. Terraria subsequently adds gravity and performs collision. At the next AI entry the motor compares the previous prediction with the actual body. Hits, life changes and announced terrain changes invalidate that comparison explicitly; their displacement must not be blamed on the planner. Arbitrary velocity writes by other mods are not classified automatically.

Distant-follow recovery is the one motor mode outside ordinary collision prediction. It phases continuously toward the owner only while the coordinator keeps it active; it never writes a route or an executed traversal. Cancellation for a downed companion or dead owner does not leave a body inside terrain: while the recovery body is solid, the motor moves it continuously toward the last known clear position, then restores gravity and tile collision once clear. That clearance is ejection, not a second owner-seeking path.

The prediction backend follows ordinary NPC movement: controls, step helpers, gravity, walking down slopes, liquid detection, tile collision, wet or dry displacement, and slope/stair resolution. It restores every Collision scratch flag in a finally block. It does not run AI, damage, sound, item use or the NPC’s full update. Dimensions and ordinary liquid multipliers are explicit in CompanionNPC.SetDefaults; changing those values requires changing this contract and the engine tests together.

Liquid flags persist until the body is fully dry. Entering water directly from honey retains honey motion; shimmer has priority over honey. A wet state is not inferred solely from the tile under the feet in the native backend.

Run `dotnet run --project Tools/EngineReplay` from the repository root. That tool calls Terraria’s own private NPC collision wrapper as the independent comparison. `Tools/NavReplay` runs a portable approximation and cannot prove native parity. Live telemetry adds the effects and interruptions that a pure collision test deliberately excludes.

## Traps

`TrackTerrainChanges` also owns the lifecycle of executed-route memory. `OnWorldLoad` clears the static store before `LoadWorldData` restores that world's archive; `SaveWorldData` writes the UTF-8 JSON as a bounded `byte[]`, because TagIO strings have a signed-short byte length while byte arrays have a signed-Int32 length. The loader accepts a small legacy string only for migration, strictly decodes byte archives, and discards missing, malformed, oversized or unsupported optional route data without blocking the world. Unload clears the store again. This callback order is specified by the installed tModLoader ModSystem reference. `ReadGameTerrain` exposes exact liquid kind and amount for compatibility fingerprints, because a liquid change can alter movement without a tile placement event.

- A StepUp call can change position without changing vertical velocity. Recording only the engine’s later displacement misses that write; keep the AI-entry observation separate from the post-helper pose.
- Sloped platforms have both shape and pass-through semantics. Dropping either property recreates a stair the graph cannot descend.
- Terraria’s shimmer multiplier is non-zero. Treating shimmer as immobility passed same-simulator replay and failed the independent source review.
- Doors can bypass ordinary placement hooks. Their interaction adapter explicitly calls TerrainChanges.
- Predicting a full movement can call the native helpers many times. Retained control sequences avoid repeating the same complete proof every frame; brain phase timings expose remaining cost during play.
