# Commands — the one thing the player types

```
Commands/
└─ CompanionCommand.cs   /companion: summon one if there is none, or call the existing one to you
```

`/companion` is how the companion enters a save for the first time, and after that it is a recovery tool rather than a normal part of playing. Summoning marks the character (`../Players/`), so from then on the companion spawns with the player on every world enter and the command is never needed again. There is deliberately never more than one companion, so a second invocation cannot produce a second body.

## Calling it to you is a teleport, and it is the mod's only one

The project's standing ruling is that the companion never teleports — knocked off a platform it climbs back, left behind it walks back, and that is what makes it read as a person rather than a marker following the camera. This command breaks that rule on purpose, by setting the existing companion's position to the player's and zeroing its velocity.

The distinction it rests on is who acted. The ruling governs what the *companion* does on its own, because a body that resolves its own problems by vanishing has no travel to watch and no failures worth fixing. A player typing a command is an explicit intervention, and it is the escape hatch for the case the ruling itself creates: a companion sealed behind a sand fall stays sealed until something digs it out, rescue behaviours are unbuilt (AIC-65), and without this line the only remedy would be reloading the world.

Anything else in the mod that moves the companion instantly is a defect, whatever it is for. That is the boundary worth keeping in mind here, because this file is the one precedent for it and a precedent read without its reasoning becomes a licence.

## Where it is going

If orders arrive, they do not arrive here. A chat command is a poor surface for anything a player would use repeatedly, so a standing instruction belongs on the HUD or the map rather than typed; this stays the setup-and-rescue command. Whether the rescue should stop being a teleport once real rescue behaviour exists is an open question rather than a decided one.
