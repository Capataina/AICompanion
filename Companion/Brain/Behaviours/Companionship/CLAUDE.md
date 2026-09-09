# Companionship — being with the player

```
Companionship/
├─ CLAUDE.md
├─ WalkWithPlayerAction.cs   follow the player's predicted position while they travel; scores zero when they stand inside the calm band so wander can win; a hard leash past which the score is full whatever else is happening (`Weights.LeashHard`); discounted while the brain says the body is stranded
├─ GuardAction.cs            danger high: stand within a wide band of the player, in sight of him and at a standoff from the most urgent threat
└─ WanderAction.cs           the floor score; stand, stroll, hop inside the calm band; stranded, the action that walks the pocket with a Roam request
```

These three are what the companion does when nothing else scores, and the torch (in `../../WorldInteractions/Torch/`) rides on them: it takes the hand whenever nothing else has claimed it in the dark.

Guard is scaled past the ordinary 0..1 band by `BehaviourWeights.GuardUrgency`, and that is arithmetic rather than tuning: the running behaviour keeps the commitment bonus, so an action topping out at 1 cannot displace a following body already at 1 × that bonus. `BehaviourWeights.cs` owns the ladder and why survive sits above this in turn.

None of the three fires a weapon and neither does guard, which used to. Shooting is the hands, run every tick from `Brain.Engage` whichever action won the feet, so a companion walking with the player shoots as readily as one guarding him — which is the whole point, because a player does not choose between travelling and attacking. Guarding is therefore a statement about *where to stand*, and the band it asks for is deliberately wide rather than tight: protecting someone means being able to hit what is attacking him, not standing where he stands, and a band that narrowed as danger rose was walking a ranged companion into the melee that was already hitting him.

## A stranded companion walks its pocket

A companion in a pocket the world seals (every plan to the player returns nothing and the flood from its feet closes under its budget) used to press at the wall nearest the player for as long as that held, because the follow scores a flat one past the leash and with no path the navigator walks straight. The brain counts those ticks and, after `Weights.StrandedAfterTicks`, says the body is stranded on the action context; walk-with multiplies its score by the stranded discount and wander scores the stranded value instead of its floor, which sits under every combat action's ceiling so a threat in the pocket still wins. Stranded, wander asks the positioner for a `Roam`, a far reachable tile, and the body walks the pocket. The brain cycles roam and retry (`RoamTicks`, then `RoamRetryTicks` of the follow winning again, whose plan to the player runs at once because the goal moved) so a way out that opens, or one the first start tile could not see, is found, and the count clears on the first plan to the player that finds anything. A player who is merely far, beyond the plan's budget, is a partial path and never stranded. Not yet watched in play (2026-09-08).
