# Chopping — trees and the axe

```
Chopping/
├─ CLAUDE.md
├─ TreeFinder.cs   which tile types are trees (IsATreeTrunk plus palm and cactus, like the vanilla axe code), the trunk bottom (GetTreeBottom returns the ground tile; TrunkBottom takes the row above), and the nearest tree with a standing spot
└─ TileChopper.cs  the companion's own HitTile and the vanilla axe formula (axe power × 1.2, × 3 on cactus); raises TileDamageWatcher.CompanionIsHitting around its KillTile so its own hits never read as the player's
```

The axe owns a separate HitTile from `../Mining/TileMiner.cs`; one cracks renderer draws both tables. Each accepted swing snapshots that table and the trunk before and after native damage. A vanished trunk is rejected before mutation, and an accepted call with no observed damage or removal is not productive work.

Discovery accepts the work action's target-admission predicate. Radius and home protection classify the trunk bottom, while the standing spot remains a movement destination: choosing the other side of a trunk must not change whether the tree is allowed. Final swings recheck home protection before damaging any tile.

TreeFinder supplies geometric standing candidates, not proof that the companion can reach them. The work action validates that approach with shared movement reachability before competing, reusing the verdict while its origin, goal and terrain revision agree and refreshing on its bounded cadence. Unknown or sealed approaches yield to other work. Arriving means matching both coordinates of the standing point, so sharing a trunk's horizontal coordinate on another floor cannot start chopping.
