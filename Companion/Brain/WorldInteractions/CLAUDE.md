# World interactions — game abilities chosen elsewhere

This folder performs reliable closed-set abilities using Terraria’s own mechanics where possible. It never selects a behaviour, chooses a place, plans movement or writes the NPC body. Gathering activities in `../PurposeFamilies/Gathering/` and nearby interaction methods decide when to request these abilities; the brain supplies the position and hand permission.

```
WorldInteractions/
├─ CLAUDE.md             this guide
├─ RenderTileCracks.cs   companion-owned tile damage presentation
├─ ObserveTileToolEffect.cs native tool-call snapshots and observed damage/removal outcomes
├─ EstimateRemainingToolWork.cs next tile completion from native damage, owned progress and cooldown
├─ FindToolAccess.cs     shared tile reach, exposed-face access and reachable working poses
├─ BindTileTarget.cs     captured coordinate/material availability without discovery or mutation
├─ Chopping/             tree detection and axe damage
├─ Mining/               ore discovery and the game pickaxe path
├─ Doors/                open or close a route obstruction
├─ Torch/                free-hand light and supplied permanent torch placement
└─ WorldProtection/      bed-room protection for autonomous edits
```

Tools read the player’s held-item numbers but do not invoke the player item-use pipeline. That boundary is why a companion ability works consistently while preserving player upgrades that are explicitly read. A terrain-changing interaction invalidates shared movement’s cached terrain facts.

Mining, chopping, pot collection and torch work use one tool-access query. It first tests the actual feet against the current player-derived reach box and a native tile walk to an exposed target face. The walk is the game's `Collision.CanHit`, not its wider `CanHitLine` beam, because a swing reaches into a one-tile notch that a beam three tiles wide refuses; it still refuses any walk that enters a solid tile. Current access needs no route. Otherwise it enumerates standable working poses with an arrival margin, ranks them by eye distance to the target, and asks shared movement for a Yes, No or Unknown approach nearest first, stopping at the first Yes. Each of those questions is a fresh bounded A* search with no retained progress, so asking every pose to keep the nearest reachable one made a single approach recomputation cost the brain's whole planning allowance; the first Yes in ascending distance is that same nearest pose, with ties kept in the exhaustive loop's scan order. Only when no pose is reachable does every pose still get asked, because Unknown versus No needs all the answers. This query proves access under the observed geometry, not native permission or future completion. Execution checks actual access again because terrain, range or the body can change after preparation.

A tile no standing pose reaches has a second query, `HopApproach`: the same geometry and nearest-first order over take-off poses, each admitted only when `SharedMovementSystem`'s interaction-jump proof shows a dry ground jump from rest there brings the tile into reach and lands beside the take-off, and only then asked of the walker. The proof takes the companion's own body state, so the jump it simulates is the companion's. Its pose search is bounded by the apex of a ground jump under ordinary gravity; reduced sky gravity (`BodyMotion.GravityAt`) raises the true apex, so near the surface a take-off lower than that bound is valid and never searched. The proof starts from rest, but a body reaches its take-off by walking, and `BodyPhysics.Stand` accepts a pose overhanging an edge by two pixels, so a pose is admitted only where some column under the body is support at rest and at a walking-speed stopping slide either side (v²/2a from the body's walking speed and slowdown), and only where it is dry, since the proof is a dry jump. The body proof does not watch the planning deadline, so the scan checks it before each proof and answers Unknown, never No, when it runs out. Mining uses it for ceiling ore, and lighting and pot work use it for a site no standing pose reaches, asked in the same order: standing first, the hop only on a standing No.

Prepared mining and chopping also bind their tile's coordinate and material. Availability validation detects disappearance or material replacement without discovering another target. This is distinct from tool access and native use permission; a replacement may be perfectly usable yet require a fresh offer. An unobserved remove-and-replace cycle with identical material is not a new generation this binding can identify.

Tool execution preserves immutable before/after observations around the native call. A requested swing, increased damage in that tool's hit table, tile removal and changed material/frame are separate outcomes. The hit table belongs to the hitter, so its damage is not shared world health; removal alone does not establish item yield. Tool instances retain their latest observation with its tick and monotonically increasing attempt number, and cooldown rejection creates no observation. Consumers join attempt identity with actor, tool and session rather than treating a retained result as fresh each tick.

RemainingToolWork estimates the next useful tile completion under unchanged native permission, damage and cadence. It includes the outstanding tool cooldown once, then the intervals between remaining hits. It excludes travel, subsequent targets and drops. Incapable or unavailable work returns no estimate rather than a fabricated finite duration. Partial damage from another hitter is not transferred between native tables; removed world tiles are shared evidence and disappear from subsequent opportunity preparation regardless of who removed them.
