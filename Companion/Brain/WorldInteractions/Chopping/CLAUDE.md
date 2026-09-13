# Chopping — trees and the axe

```
Chopping/
├─ CLAUDE.md
├─ TreeFinder.cs   native tree classification and trunk identity, with admitted trees ranked by distance and shared tool access from the actual companion
└─ TileChopper.cs  the companion's own HitTile and the vanilla axe formula (axe power × 1.2, × 3 on cactus); raises TileDamageWatcher.CompanionIsHitting around its KillTile so its own hits never read as the player's
```

The axe owns a separate HitTile from `../Mining/TileMiner.cs`; one cracks renderer draws both tables. Each accepted swing snapshots that table and the trunk before and after native damage. A vanished trunk is rejected before mutation, and an accepted call with no observed damage or removal is not productive work.

The live trunk check uses the same tree classification as discovery. Being axe-eligible alone does not make replacement furniture a tree. Prepared coordinate/material binding belongs to the action, while this native tool boundary enforces that its present target still belongs to the supported tree category.

The next-trunk completion estimate and the actual hit share one axe-damage function, including cactus scaling and native kill permission. Estimation reads existing damage and cooldown without performing a swing. The duration excludes approaching the trunk and retrieving its drops, and cannot promise removal if permission or material changes later.

Discovery accepts the work action's target-admission predicate. Radius and home protection classify the trunk bottom, while the standing spot remains a movement destination: choosing the other side of a trunk must not change whether the tree is allowed. Final swings recheck home protection before damaging any tile.

TreeFinder separates the search origin from the companion's actual feet. It deduplicates trunk bottoms and applies resource admission before asking the shared tool query for actual access or a reachable working pose. Searching near the player cannot assume the companion starts there. Unknown or sealed approaches yield to other candidates; distance ranks the resource rather than a preferred side of its trunk.

`TreeFinder.FindNearest` never searches within ten tiles of the world edge. A tree planted near the edge of a small test world is therefore invisible to discovery, and a fixture built that way reads as a valuation or cooperation failure rather than as a tree the finder never saw.

The work action refreshes retained access after changes to origin, target, terrain or effective reach, and checks actual access again before swinging. A usable current pose needs no approach to a representative node. The execution check uses the full shared tool reach and exposed-face rule, so a wall inserted after preparation or reduced reach cannot be bypassed by proximity to an old standing point. Route arrival alone is never axe permission.
