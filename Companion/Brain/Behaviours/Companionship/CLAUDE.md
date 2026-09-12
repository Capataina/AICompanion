# Companionship — being with the player

```
Companionship/
├─ CLAUDE.md
└─ GuardAction.cs            danger high: stand within a wide band of the player, in sight of him and at a standoff from the most urgent threat
```

This folder retains player protection during the purpose-family migration. Ordinary reunion, rest and local movement belong to `../../PurposeFamilies/NearbyAssistance/KeepCompany.cs`; distant recovery belongs to `../../ActivityCoordination/`. The torch takes an otherwise free hand independently of those movement purposes.

Guard preparation refreshes commitment and clearance evidence, then captures its utility, enemy generation, target position and destination anchor together. Score receives no world context and returns that value without changing commitment or observing the player again. Entry commits the prepared enemy; execution refuses a dead, missing or replaced generation instead of substituting a newly urgent enemy. The next preparation can offer a different threat, which receives a distinct activity identity through the shared owner. This binding establishes which protection task was chosen, not whether a useful firing position or intervention exists.

Guard is scaled past the ordinary 0..1 band by `BehaviourWeights.GuardUrgency`: the running behaviour keeps the commitment bonus, so an action topping out at 1 cannot displace a following body already at 1 × that bonus. SharedSafety can suspend protection independently of this comparison when the companion needs an environmental escape or collision response.

Guard scores the observation layer's protection urgency, which compares enemy arrival with the time until the companion can actually intervene. Entry retains the relevant NPC's spawn identity and pressure; a small retreat does not erase an unfinished protection task. Death, disappearance, slot reuse or sustained loss of relevance releases that commitment. Personal survival can interrupt it. It is zero for a dead player; personal defence remains independent.

Guard does not fire a weapon. Shooting is the hands, run every tick from `Brain.Engage` whichever action won the feet, so a companion walking with the player shoots as readily as one guarding him. Guarding asks where to stand, with a deliberately wide band: protecting someone means being able to hit what is attacking them. Narrowing that band as danger rose previously walked a ranged companion into the melee it was trying to address.

## A stranded companion walks its pocket

A companion in a proven sealed pocket needs an available local movement method without forgetting reunion. The coordinator exposes that state on the action context and cycles local roaming with reunion retries using BehaviourWeights. Keeping company consumes the same evidence within one activity: its local method requests Roam, and its reunion method requests WithPlayer. A merely incomplete path is not proof of a sealed pocket. This policy retains the existing reachability evidence and does not prove that every declared sealed region is physically inescapable.
