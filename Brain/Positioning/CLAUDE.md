# Positioning — where to stand

`Positioner.cs` turns a `PositionRequest` into a feet position. `Exact` and `Hold` pass through; the rest sample standable tiles in a box around the anchor (radius 14 tiles, stride 2) and score each on: distance band to the player (tight under threat, loose when calm, from `Weights`), sight line to the player, line of fire to the target through the aimer with the chosen weapon's profile, danger from predicted threat paths over the next 40 ticks, openness (solid tiles around eye height), travel bias along the player's intent, and for line-of-fire requests a standoff band from the target. The weights per request kind are the `switch` in `ScoreSpot`. Re-scored every 12 ticks or when the request kind or target changes.

The jump-shot from the design (a jump apex as a candidate spot) is not sampled yet; candidates are ground tiles only. Add it as a second candidate set with the apex height from `NavGrid.JumpHeightTiles`.
