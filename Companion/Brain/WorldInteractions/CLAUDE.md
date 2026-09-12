# World interactions — game abilities chosen elsewhere

This folder performs reliable closed-set abilities using Terraria’s own mechanics where possible. It never selects a behaviour, chooses a place, plans movement or writes the NPC body; `Behaviours/Work` decides when it is useful and the brain gives it a chosen position.

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

Mining, chopping, pot collection and torch work use one tool-access query. It first tests the actual feet against the current player-derived reach box and native sight to an exposed target face. Current access needs no route. Otherwise it enumerates standable working poses with an arrival margin and asks shared movement for a Yes, No or Unknown approach. This query proves access under the observed geometry, not native permission or future completion. Execution checks actual access again because terrain, range or the body can change after preparation.

Prepared mining and chopping also bind their tile's coordinate and material. Availability validation detects disappearance or material replacement without discovering another target. This is distinct from tool access and native use permission; a replacement may be perfectly usable yet require a fresh offer. An unobserved remove-and-replace cycle with identical material is not a new generation this binding can identify.

Tool execution preserves immutable before/after observations around the native call. A requested swing, increased damage in that tool's hit table, tile removal and changed material/frame are separate outcomes. The hit table belongs to the hitter, so its damage is not shared world health; removal alone does not establish item yield. Tool instances retain their latest observation with its tick and monotonically increasing attempt number, and cooldown rejection creates no observation. Consumers join attempt identity with actor, tool and session rather than treating a retained result as fresh each tick.

RemainingToolWork estimates the next useful tile completion under unchanged native permission, damage and cadence. It includes the outstanding tool cooldown once, then the intervals between remaining hits. It excludes travel, subsequent targets and drops. Incapable or unavailable work returns no estimate rather than a fabricated finite duration. Partial damage from another hitter is not transferred between native tables; removed world tiles are shared evidence and disappear from subsequent opportunity preparation regardless of who removed them.
