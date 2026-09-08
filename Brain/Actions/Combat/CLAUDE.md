# Combat actions — going after enemies, or backing off

```
Combat/
├─ CLAUDE.md
├─ HuntAction.cs   a reachable target the player is safe enough to leave for; forecasts the trip to a firing spot
└─ KiteAction.cs   a walker inside melee reach: retreat while firing
```

Both fire through `../../../Combat/Weapons/Arsenal.cs`, which picks the weapon and solves the shot through `../../Aiming/`. The weapons are equipment, not behaviour, which is why they are not in the brain.
