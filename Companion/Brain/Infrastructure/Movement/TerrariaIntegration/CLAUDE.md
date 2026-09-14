# Terraria integration — live terrain, controls and native collision prediction

This is the only shared-movement folder allowed to name Terraria types. It adapts the real NPC and tilemap to the core’s body-state, control and terrain contracts. Behaviours never call collision helpers or write velocity themselves.

```
TerrariaIntegration/
├─ CLAUDE.md                       this boundary and its verification
├─ ReadGameTerrain.cs              GameTileWorld reads shape and pass-through separately
├─ SimulateTerrariaBody.cs          predicts one controlled tick with native collision helpers
├─ ApplyControlsToCompanion.cs      CompanionMotor applies the resolved packet and measures the preceding tick
└─ TrackTerrainChanges.cs          three types: `TerrainChanges`, the one announcement point, carrying the tile; `TrackTerrainChanges`, the GlobalTile hooks that announce placement, breaking, hammering and wiring; and `ResetTerrainChanges`, the ModSystem owning world load, unload and the executed-route archive's save and load
```

**This folder is where an edit enters the brain, and one announcement does three things.** `TerrainChanges.Changed(x, y)` records the tile in the world's `TerrainEditLog`, drops every cached edge whose scans could have read that tile, and clears the one-way return verdicts wholesale — the last of those is deliberately not boxed, for the reason `../RoutePlanning/CLAUDE.md` gives. `TerrainChanges.Reset()` raises the record's floor and drops the whole edge cache instead, because nothing can be named after a wholesale change.

What announces, and it is a closed list: the `GlobalTile` hooks for wiring, sloping, placement and a kill that actually succeeded (a pickaxe hit that only cracks a tile is not a change and is not announced); the door adapter in `../../Interactions/Doors/`, by hand, because the game's door helper bypasses those hooks; and the fixtures. An edit made by any other route is invisible to everything downstream, which is the failure mode to look for first when retained work survives an edit it should not have.

The motor observes the NPC at AI entry, before stepping helpers can change its position. Applying controls first predicts the next engine result, then calls the shared ability transition and the real step helpers. Terraria subsequently adds gravity and performs collision. At the next AI entry the motor compares the previous prediction with the actual body. Hits, life changes and announced terrain changes invalidate that comparison explicitly; their displacement must not be blamed on the planner. The brain hands that reason (`DivergenceInvalidReason`) to the navigator every tick, which is how a knockback mid-traversal is classified as pre-emption rather than a native mismatch. Arbitrary velocity writes by other mods are not classified automatically, and reach the navigator as an unattributed divergence.

The motor counts ordinary and recovery control applications independently of recording. Activity coordination snapshots the count delta with the requested and actual owner, making duplicate applications observable even when their final controls agree. Immediate lifecycle cancellation remains a separate event; the per-AI grant describes the following resolved packet.

Distant-follow recovery is the one motor mode outside ordinary collision prediction. It phases continuously toward the owner only while the coordinator keeps it active; it never writes a route or an executed traversal. Cancellation for a downed companion or dead owner does not leave a body inside terrain: while the recovery body is solid, the motor moves it continuously toward the last known clear position, then restores gravity and tile collision once clear. That clearance is ejection, not a second owner-seeking path.

The prediction backend follows ordinary NPC movement: controls, step helpers, gravity, walking down slopes, liquid detection, tile collision, wet or dry displacement, and slope/stair resolution. It restores every Collision scratch flag in a finally block. It does not run AI, damage, sound, item use or the NPC’s full update. Dimensions and ordinary liquid multipliers are explicit in CompanionNPC.SetDefaults; changing those values requires changing this contract and the engine tests together.

Liquid flags persist until the body is fully dry. Entering water directly from honey retains honey motion; shimmer has priority over honey. A wet state is not inferred solely from the tile under the feet in the native backend.

SimulateTerrariaBody owns the gravity query used by native prediction and jump proposals. It reads altitude, world dimensions and the prior liquid state, matching the engine's pre-AI gravity phase. The motor captures the engine's current gravity separately from the model's value at AI entry, with the source tick and whether gravity was enabled. Diagnostics consume those observations rather than querying a later post-helper pose. A disabled-gravity recovery body is not an ordinary gravity-parity sample.

Run `dotnet run --project Tools/EngineReplay` from the repository root. That tool calls Terraria’s own private NPC collision wrapper as the independent comparison. `Tools/NavReplay` runs a portable approximation and cannot prove native parity. Live telemetry adds the effects and interruptions that a pure collision test deliberately excludes.

## Traps

`ResetTerrainChanges`, the ModSystem in `TrackTerrainChanges.cs`, owns the lifecycle of executed-route memory — the `GlobalTile` of the same name as the file owns only the announcement hooks, so a reader searching for these callbacks on it will not find them. `OnWorldLoad` clears the static store before `LoadWorldData` restores that world's archive; `SaveWorldData` writes the UTF-8 JSON as a bounded `byte[]`, because TagIO strings have a signed-short byte length while byte arrays have a signed-Int32 length. The loader accepts a small legacy string only for migration, strictly decodes byte archives, and discards missing, malformed, oversized or unsupported optional route data without blocking the world. Unload clears the store again. This callback order is specified by the installed tModLoader ModSystem reference. `ReadGameTerrain` exposes exact liquid kind and amount for compatibility fingerprints, because a liquid change can alter movement without a tile placement event.

- A StepUp call can change position without changing vertical velocity. Recording only the engine’s later displacement misses that write; keep the AI-entry observation separate from the post-helper pose.
- Sloped platforms have both shape and pass-through semantics. Dropping either property recreates a stair the graph cannot descend.
- Terraria’s shimmer multiplier is non-zero. Treating shimmer as immobility passed same-simulator replay and failed the independent source review.
- Doors can bypass ordinary placement hooks. Their interaction adapter explicitly calls TerrainChanges.
- Predicting a full movement can call the native helpers many times. Retained control sequences avoid repeating the same complete proof every frame; brain phase timings expose remaining cost during play.
