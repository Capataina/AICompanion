# Companionship — being with the player

```
Companionship/
├─ CLAUDE.md
├─ WalkWithPlayerAction.cs   follow the player's predicted position while they travel; scores zero when they stand inside the calm band so wander can win; hard leash at 1400 px; discounted while the brain says the body is stranded
├─ GuardAction.cs            danger high: stand by the player with sight lines and shoot the most urgent threat
└─ WanderAction.cs           the floor score; stand, stroll, hop inside the calm band; stranded, the action that walks the pocket with a Roam request
```

These three are what the companion does when nothing else scores, and the torch (in `../../Work/Torch/`) rides on them: it takes the hand whenever an action here leaves it empty in the dark.

## A stranded companion walks its pocket

A companion in a pocket the world seals (every plan to the player returns nothing and the flood from its feet closes under its budget) used to press at the wall nearest the player for as long as that held, because the follow scores a flat one past the leash and with no path the navigator walks straight. The brain counts those ticks and, after `Weights.StrandedAfterTicks`, says the body is stranded on the action context; walk-with multiplies its score by the stranded discount and wander scores the stranded value instead of its floor, which sits under every combat action's ceiling so a threat in the pocket still wins. Stranded, wander asks the positioner for a `Roam`, a far reachable tile, and the body walks the pocket. The brain cycles roam and retry (`RoamTicks`, then `RoamRetryTicks` of the follow winning again, whose plan to the player runs at once because the goal moved) so a way out that opens, or one the first start tile could not see, is found, and the count clears on the first plan to the player that finds anything. A player who is merely far, beyond the plan's budget, is a partial path and never stranded. Not yet watched in play (2026-09-08).
