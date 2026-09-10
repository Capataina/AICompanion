# Companion — one gameplay subsystem around the NPC

`Companion/` contains everything that belongs to the companion as a game entity: its body and engine bridge, decision-making brain, equipment, bag, player-facing integrations, map/HUD presentation and enemy integration. The root contains the mod shell and tools; it no longer owns a parallel set of companion feature folders.

```
Companion/
├─ CLAUDE.md                 this guide
├─ CharacterBody/            NPC lifecycle, player-shaped rendering and breath
├─ EnemyIntegration/         temporary targeting stand-in and spawn-rate changes
├─ Brain/                    observation, behaviour, positioning and movement requests
├─ Weapons/                  companion equipment and outcome-based arsenal selection
├─ Inventory/                bag storage and its UI
├─ PlayerIntegration/        persistence, input, player events and /companion
├─ ProfileCard/              native per-character behaviour controls and cargo access
├─ DiagnosticsConfiguration/ native mod settings for inspector and local recording
├─ MapIntegration/           map head and torch-driven reveal
└─ HeadsUpDisplay/           the player-facing health notch
```

The brain requests a control set through its shared movement boundary. `CharacterBody` applies it to the live NPC; no other subsystem writes the NPC’s movement. Enemy integration’s temporary stand-in player is a targeting device only, never the companion’s owner of life or position.

The companion remains an opportunistic presence: it follows loosely, helps with nearby work already in progress, and fights while moving. It has no player-directed mission system. Future mastery may add movement capabilities but none is documented as current unless its implementation appears in the active capability surface.

## Cross-subsystem rules

- The bag carries cargo, not equipment. Weapons are fixed companion capabilities selected by the arsenal.
- HUD and map surfaces show companion state; they do not decide behaviour or movement.
- Player integration owns character-persistent state and input. The body owns spawning an actual NPC from that state.
- The companion does not teleport under autonomous behaviour. The `/companion` command is the explicit player recovery exception.
