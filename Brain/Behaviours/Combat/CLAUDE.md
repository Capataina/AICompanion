# Combat actions — going after enemies, or backing off

```
Combat/
├─ CLAUDE.md
├─ HuntAction.cs   a reachable target the player is safe enough to leave for, near enough to the player to be worth leaving him for, and not already killing the companion; forecasts the trip to a firing spot
└─ KiteAction.cs   a walker inside melee reach: retreat
```

**Neither of these fires a weapon, and the fact that they used to is the interesting thing about this folder.** Shooting lived inside hunt, kite and guard, which made the companion structurally incapable of shooting while it was following the player, looting, wandering or working — nine actions, three of which could reach the trigger. No amount of scoring could have fixed that, because the code that pulls the trigger was not reachable from the other six. It now runs every tick from `Brain.Engage`, after the navigator, on a target chosen independently of the one these actions walk toward. So a hunt is now only the decision to *walk toward* something, and kiting is only the decision to back away; both of them shoot exactly as much as following him does, which is to say whenever there is a live target and no tool in the arm.

What hunting scores on, past the target being worth hitting, is two terms that exist to keep it opportunistic rather than a mode. A leash falls to zero as the distance to the player passes what his screen can hold, because a hunt that walks off the screen has inverted the design's first priority. And its own skin, `1 − CompanionDanger` with a floor, stops it walking toward a fight it is already losing. Without the second term the score contained `1 − PlayerDanger`, so straying from the player made the world look safer and hunting score *higher* the further it went — a loop that ended with the companion dead eighty-four tiles away on 2026-09-09.

The weapon itself is picked and the shot solved in `../../../Combat/Weapons/Arsenal.cs` through `../../ProjectileAiming/`. The weapons are equipment, not behaviour, which is why they are not in the brain.
