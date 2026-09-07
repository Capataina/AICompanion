# AICompanion — "Multi... Player?"

A tModLoader mod that adds an AI companion to Terraria: an NPC that follows you, and will fight beside you, do what you are doing, take orders, and grow through its own upgrade tree. The internal mod name is `AICompanion`; the display name on the mod browser is `Multi... Player?`. This is the first step of a long-standing aspiration recorded in the LifeOS vault at `Profile/Personal/AI-Populated Games.md`, and its plan lives in the Slate project `ai-companion` (prefix AIC).

The companion is an NPC, deliberately, not a second `Player` slot. Abilities are the mod's own closed set, never real player items routed through NPC code, because that routing is the class of bug (bows that will not fire, potions that cannot be used) that keeps the existing companion mod, TerraGuardians, feeling like an NPC that does some stuff. The one requirement the NPC shape makes harder is keeping a boss fight alive after the human dies; AIC-8 on the board carries the two routes and the check.

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
│  └─ Companion.cs                 the companion ModNPC: aiStyle -1, follow AI (walk, jump, far-teleport),
│                                  Guide sprite borrowed until it has its own art, never despawns, no damage taken
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

Exit 0 and `bin/Debug/net8.0/AICompanion.dll` present is the pass. The shell build proves it compiles; it does not produce the `.tmod` tModLoader loads.

Build for the game (what Caner does to play it): tModLoader → Workshop → Develop Mods → AICompanion → Build + Reload. Then in a world, type `/companion` in chat. The companion appears at your feet and follows; walk away and it walks after you, jumps at ledges, and teleports to you past about 1,400 pixels.

## Traps

- **The shell build says "Build succeeded" in under two seconds.** That is real: the project is tiny and tMLMod.targets references the installed tModLoader.dll directly. Check the DLL exists before trusting it, as the first session did.
- **`Texture` borrows the Guide's sheet** (`Terraria/Images/NPC_22`). `FindFrame` assumes the town-NPC layout (frame 0 idle, 2–15 walking). Replacing the sprite means replacing `FindFrame` too.
- **`CheckActive` returns false**, so the companion is never culled for distance. If a companion ever needs removing, the command or a future despawn path has to do it explicitly.
- **`dontTakeDamage` is on** for the hello world. Combat (AIC-7) turns it off and gives the companion real health.

## Planned work

See the Slate project `ai-companion`. Next after the hello world: AIC-8 (boss persistence after the human dies) before any tech-tree work, because it decides whether the NPC shape meets the spec.
