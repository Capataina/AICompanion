# Doors — the one obstacle the world opens for you if you ask

```
Doors/
├─ CLAUDE.md
└─ DoorOpener.cs   Tick(npc): a closed door or tall gate in the walking direction swings the way the body is going, or inward when that side is blocked; the tile is remembered and shut again once the body is two tiles past it, and forgotten once the body is four tiles away
```

## The game answers the hard question, so this folder asks it rather than measuring anything

`WorldGen.OpenDoor(x, y, direction)` takes a direction and returns whether the door could actually swing that way. That single return value is the whole clearance test: whether there is room on the far side, whether something is standing in the arc, whether the tiles beside it allow the open frame. So the rule is three calls and no geometry —

1. try the direction the body is travelling,
2. try the opposite, which is a door opening inward,
3. try `WorldGen.ShiftTallGate`, which is the same contract with a different verb for the tile a player builds when they want the door out of the way.

A door only stays shut when both sides are blocked, and nothing here had to be taught what "blocked" means. This is the standing instruction of this project working as intended: read the decompiled game for the path that already does it, and reuse it unless it is gated on the local player. The rule is lifted from `AI_007_TownEntities`, where a townsperson does exactly this around its `closeDoor` flag.

## Two things a townsperson does that a companion must not

The town NPC gates its attempt on `Main.rand.Next(10) == 0` and waits for `ai[2]` to reach 60 first. Both are character: a villager dithering at a doorway before pushing through reads as a person. A companion that fails to follow the player for a few tenths of a second reads as broken, so neither is copied. Character and reliability want opposite things at a doorway, and this is a follower.

## Opening a door is a world change the tile hooks never announce

`WorldGen.OpenDoor` neither kills nor places a tile through `KillTile` or `PlaceInWorld`, which are the two hooks `../../DecisionMatrix/Senses/TileDamageWatcher.cs` uses to tell the planner's edge cache that the world moved. So this folder calls `AStar.TileChanged` on the door's three rows itself, on the open and on the close alike. Without that the planner keeps the closed door's proven edges — a wall in the grid where the body has just made an opening — until they age out on the cache's own lifetime, and the companion would plan round a door it had already opened.

That is worth generalising rather than remembering: anything in this mod that changes a tile outside `KillTile` and `PlaceInWorld` owes the edge cache the same call. Liquids settling, sand landing and a boulder rolling are the same class, and the cache's timed expiry is the backstop for the ones nobody can hook.
