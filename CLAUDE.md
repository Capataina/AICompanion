# AICompanion — "Multi... Player?"

A tModLoader mod that adds an AI companion to Terraria: an NPC that follows you, and will fight beside you, do what you are doing, take orders, and grow through its own upgrade tree. The internal mod name is `AICompanion`; the display name on the mod browser is `Multi... Player?`. This is the first step of a long-standing aspiration recorded in the LifeOS vault at `Profile/Personal/AI-Populated Games.md`, and its plan lives in the Slate project `ai-companion` (prefix AIC).

**This mod is singleplayer only, by Caner's ruling on 2026-09-07, and every line of code assumes it.** The player is always `Main.LocalPlayer`; there is no netcode, no `netUpdate`, no server/client branching, no iteration over `Main.player`. A change that adds any of those is wrong even if it works, because it spends effort on a case the mod refuses to support and makes every later feature carry the same cost.

The companion is an NPC, deliberately, not a second `Player` slot. Abilities are the mod's own closed set, never real player items routed through NPC code, because that routing is the class of bug (bows that will not fire, potions that cannot be used) that keeps the existing companion mod, TerraGuardians, feeling like an NPC that does some stuff. The one requirement the NPC shape makes harder is keeping a boss fight alive after the human dies; AIC-8 on the board carries the two routes and the check.

**Before implementing any mechanic, read the decompiled game for the path that already does it, and reuse it unless it is gated on the local player.** Caner's standing instruction on 2026-09-07: most of what the companion does already exists in the game for the player. Chopping reuses `HitTile` plus the vanilla axe formula and `Main.DrawTileCracks`; the body reuses the player renderer; arrows are vanilla projectiles owned by the player so the player's on-hit accessories and ranged stats apply. Decompile with `ilspycmd -t Terraria.<Type> "<Steam>/tModLoader/tModLoader.dll"` (installed under `~/.dotnet/tools`) and grep the result; the reflection scratch tool at `/tmp/tmlreflect` lists member signatures.

## The map

```
AICompanion/
├─ CLAUDE.md                       this file
├─ README.md                       outward-facing summary for the repository
├─ build.txt                       tModLoader manifest: display name, author, version, buildIgnore
├─ description.txt                 mod browser description
├─ AICompanion.csproj              imports ../tModLoader.targets, which imports tMLMod.targets from the Steam install
├─ AICompanion.cs                  the Mod subclass; logs on load, nothing else
├─ Content/
│  ├─ Companion.cs                 the companion ModNPC: mirrors the player's max life and defence, runs the
│  │                               behaviour priority Downed > Shoot > Chop > Wander > Follow, downs instead of
│  │                               dying, revives after 3 s beside the player, draws through CompanionAppearance
│  ├─ CompanionAppearance.cs       a drawing-only Player (female starter body) synced to the NPC each tick and
│  │                               drawn by Main.PlayerRenderer; falls back to the Guide sprite if the renderer throws
│  └─ Behaviours/
│     ├─ TreeFinder.cs             the tree under the player's axe, and the nearest other tree with a standing spot
│     ├─ TileChopper.cs            the companion's own HitTile and the vanilla axe formula against a trunk's bottom tile
│     ├─ TileCracksRenderer.cs     ModSystem: draws the companion's cracks with Main.DrawTileCracks after tiles
│     ├─ ArrowAimer.cs             WeaponProfile + arc simulation against solid tiles, leading the target's velocity
│     ├─ BowBehaviour.cs           picks an on-screen hostile, asks the aimer, fires a player-owned wooden arrow
│     └─ WanderBehaviour.cs        idle stroll/stand/hop inside a 160 px leash
├─ Players/
│  └─ CompanionPlayer.cs           ModPlayer: has-companion flag (auto-spawn on world enter) and health bar position
├─ UI/
│  └─ CompanionHealthBar.cs        ModSystem: HUD bar after "Vanilla: Resource Bars"; drag with left mouse, right-click resets
├─ Commands/
│  └─ CompanionCommand.cs          /companion: spawns one companion at the player, or calls the existing one over
└─ Localization/
   └─ en-US_Mods.AICompanion.hjson  NPC display name
```

## Operating manual

Build from the shell (what a session does to verify a change compiles):

```
cd "~/Library/Application Support/Terraria/tModLoader/ModSources/AICompanion"
dotnet build -nologo -v q
```

Zero `error CS` lines and a fresh `bin/Debug/net8.0/AICompanion.dll` is the pass. While the game is open the same command then fails at the `.tmod` packaging step with `TML003: Please close tModLoader or disable the mod in-game`; that is the packaging step, not the compile, and the DLL timestamp is the thing to check.

Build for the game (what Caner does to play it): tModLoader → Workshop → Develop Mods → AICompanion → Build + Reload. Then in a world, type `/companion` in chat. The companion appears at your feet and from then on spawns with you on every world enter. It follows past 64 px, wanders when close, teleports to you past about 1,400 px, chops the nearest other tree when you swing an axe at one, and shoots any hostile on screen with a wooden bow.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real: the project is tiny and tMLMod.targets references the installed tModLoader.dll directly. Check the DLL exists before trusting it, as the first session did.
- **`Texture` still borrows the Guide's sheet** (`Terraria/Images/NPC_22`) and `FindFrame` still assumes the town-NPC layout, but only as the fallback when `CompanionAppearance` reports the player renderer failed. The normal path draws the dummy Player in `PreDraw` and returns false.
- **The dummy Player is never placed in `Main.player`.** Its `whoAmI` is `Main.maxPlayers` so no draw layer treats it as the local player. `ItemCheck_ApplyHoldStyle` is private, so at rest the hands are empty; the held item only shows while an animation runs.
- **`CheckActive` returns false**, so the companion is never culled for distance. If a companion ever needs removing, the command or a future despawn path has to do it explicitly.
- **`dontTakeDamage` is toggled by the downed state**, off while alive and on while downed. `CheckDead` returns false and enters Downed; forgetting to set life to 1 there would fire CheckDead every tick.
- **The health bar draws in raw screen pixels** (`InterfaceScaleType.None`) and scales sizes by `Main.UIScale` by hand, because `Main.mouseX/Y` are screen pixels and comparing them against a UI-scaled layer misses at any scale other than 100%.
- **Nothing in this folder has been watched running yet as of 2026-09-07.** Every behaviour above compiles; the first in-game run is Caner's, and what it shows goes into the Slate record.

## Planned work

See the Slate project `ai-companion`. AIC-11 (this batch) is compiled and awaiting the in-game run. After that: AIC-8 (boss persistence after the human dies) before any tech-tree work, because it decides whether the NPC shape meets the spec.
