# Combat actions — going after enemies, or backing off

```
Combat/
├─ CLAUDE.md
├─ HuntAction.cs   a reachable target the player is safe enough to leave for, near enough to the player to be worth leaving him for, and not already killing the companion; forecasts the trip to a firing spot
└─ KiteAction.cs   a walker inside melee reach: retreat
```

**Neither of these fires a weapon, and the fact that they used to is the interesting thing about this folder.** Shooting lived inside hunt, kite and guard, which made the companion structurally incapable of shooting while it was following the player, looting, wandering or working — nine actions, three of which could reach the trigger. No amount of scoring could have fixed that, because the code that pulls the trigger was not reachable from the other six. It now runs every tick from `Brain.Engage`, after the navigator, on a target chosen independently of the one these actions walk toward. So a hunt is now only the decision to *walk toward* something, and kiting is only the decision to back away; both of them shoot exactly as much as following him does, which is to say whenever there is a live target and no tool in the arm.

Hunting remains opportunistic through the shared activity envelope and independent self-danger. `AllowsTarget` admits useful work farther away than ordinary following and retains an active job within its wider envelope; the selected distance profile owns that boundary rather than the screen edge. Its own skin, `1 − CompanionDanger` with a floor, stops it walking toward a fight it is already losing. Reading only player danger makes straying look safer precisely when the companion is exposed, so both bodies' danger remain separate inputs.

The weapon itself is picked and the shot solved in `../../../Weapons/Arsenal.cs` through `../../ProjectileAiming/`. The weapons are equipment, not behaviour, which is why they are not in the brain.

Kiting checks whether the hostile can reach the companion, independently of whether it can reach the player. Hunting considers threats to either actor; personal danger still discounts its score, so recognising a nearby hostile is not permission to abandon following.

Hunting observes actual engagement progress after the hands step. A target taking damage, a shot/reload cycle or meaningful body displacement renews the attempt. A stationary hunt producing neither movement nor attacks defers that target generation for a bounded retry interval. Changed body, target or terrain reopens it immediately. This avoids treating a selected behaviour as evidence that it is accomplishing anything while preserving productive travel and firing from a stationary position.

Kite anchors its retreat on the companion and selects its most urgent personal threat. Hunt additionally requires a currently attackable NPC: an invulnerable hostile is an avoidance problem, not a target worth pursuing. The chooser prices excursions against both regroup time and protection urgency, so a hunt cannot borrow safety from a player the companion would reach too late to help.
