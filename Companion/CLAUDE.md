# Companion — one gameplay subsystem around the NPC

`Companion/` contains everything that belongs to the companion as a game entity: its body and engine bridge, decision-making brain, equipment, bag, player-facing integrations, map/HUD presentation and enemy integration. The root contains the mod shell and tools; it no longer owns a parallel set of companion feature folders.

```
Companion/
├─ CLAUDE.md                 this guide
├─ CharacterBody/            the orb NPC's lifecycle, its drawing, its immunity to every liquid, the stand-in hostiles aim at and the mana pool
├─ EnemyIntegration/         temporary targeting stand-in and spawn-rate changes
├─ Brain/                    Activities, SharedBehaviours (Safety, Recovery) and Infrastructure; a course decides and the activities perform
├─ Progression/              the level priced by the game's own numbers — kills, boss fights and work credited to the companion or the player
├─ Inventory/                the cargo bag, the four gear slots and their page
├─ PlayerIntegration/        persistence, input, player events and /companion
├─ ProfileCard/              native per-character behaviour controls and inventory access
├─ DiagnosticsConfiguration/ native mod settings for inspector and local recording
├─ MapIntegration/           map head and torch-driven reveal
└─ HeadsUpDisplay/           the player-facing health notch
```

The brain requests a control set through its shared movement boundary. `CharacterBody` applies it to the live NPC; no other subsystem writes the NPC’s movement. Enemy integration’s temporary stand-in player is a targeting device only, never the companion’s owner of life or position.

The companion remains an opportunistic presence: it follows loosely, helps with nearby work already in progress, and fights while moving. It has no player-directed mission system. Future mastery may add movement capabilities but none is documented as current unless its implementation appears in the active capability surface.

One brain decides, and only one. Since `0bb2c8a` on 21 September 2026 the tick asks a retained course what to do — one frozen observation, discovery across all six domains, a bounded search over orders priced by the consequences each would have — and `ExecuteCourseBinding` translates the bound step into a position request rather than letting the activity choose its own site a second time. The multiplied-score family chooser that preceded it was deleted on 22 September 2026 with `f73bd24`, so any sentence anywhere in this subtree describing a per-tick competition between activity families is describing a brain that no longer exists.

## Cross-subsystem rules

- The bag carries cargo, not equipment. The gear's four slots carry equipment, not cargo: two weapons, a pickaxe and an axe the player hands over, read for their numbers and never run, with the arsenal choosing which weapon to use by what it would land.
- HUD and map surfaces show companion state; they do not decide behaviour or movement.
- Player integration owns character-persistent state and input. The body owns spawning an actual NPC from that state.
- The companion does not teleport under autonomous behaviour. The `/companion` command is the explicit player recovery exception.
