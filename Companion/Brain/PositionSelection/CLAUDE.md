# Position selection — turn an intent into a useful place

`ChooseUsefulPosition.cs` turns a behaviour’s `PositionRequest` into a standable feet position. It owns candidate scoring; shared movement owns whether a route can be planned and the companion motor owns applying the resulting controls.

```
PositionSelection/
├─ CLAUDE.md                 this guide
├─ PositionRequest.cs        request kinds and their anchor, target and jump intent
└─ ChooseUsefulPosition.cs   candidate flood, scoring, incumbent hold and temporary bans
```

The resolver samples standable tiles around the request anchor, prefers the shared movement region when one reaches the request, scores cheap factors first, and asks projectile aiming only about its strongest firing candidates. The score combines the request’s band, sight, danger, openness, travel bias and, where relevant, a feasible shot. A place outside the reachable region is absent rather than merely worse; a partial route remains useful only when the region was cut short rather than proved closed.

One-way drops are refused by default. The exception is for following the player down: it opens only when the raw region actually reaches the player, which separates a player below a drop from a player sealed behind a wall. A stuck route temporarily bans its chosen tile so the next answer is a different place rather than the same unwalkable claim.

## Traps

The returnable and unrestricted regions retain frontier work across rescores. A moved start reuses them only after generated connections establish travel in both directions to the previous root; a short drop cannot inherit the upper ledge's reachable region merely because it is close. World revision and lava-policy changes invalidate both. The incumbent's exact tile is reconsidered alongside newly sampled candidates, so shifting a sampling lattice does not silently remove a still-useful follow destination. Candidate counts and known travel ticks feed diagnostics and the chooser's excursion cost.

- Candidate danger and approach clearance test hostile geometry directly. An enemy unable to reach either actor's current position can still occupy the place the companion is considering entering.
- Do not resolve a route here. Position selection states where the companion would be useful; `CoordinateMovement` decides how its body gets there.
- The current chosen tile gets a small incumbent preference. Removing it turns equal candidates into repeated replans.
- A weapon trajectory solve is expensive; apply it only after the cheap scoring pass.
- CandidateEvidence retains the strongest evaluated alternatives, their final scores and whether each solved a shot, stamped with the rescore tick. It exposes the decision to the recorder without making diagnostics rerun spatial queries.
- A retained region's membership can transfer within a proven two-way component; its root-relative travel times cannot. EstimatedTravelTicks takes the live origin and returns unknown after that origin moves, leaving the chooser's current geometric and active-route estimates in charge.
