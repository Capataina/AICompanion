# Multi... Player?

An AI companion for Terraria, as a tModLoader mod. It follows you, and over time will fight beside you, do what you are doing, take orders like "go mine iron", and grow through its own upgrade tree that scales with your progression.

Early development. Today: `/companion` in chat spawns a companion that looks like the female player, follows you, wanders when idle, shares your max life and defence, gets downed instead of dying (stand beside it for three seconds to revive), chops the tree next to yours when you chop, shoots a bow at whatever is on screen, and comes back with you every time you enter a world. Its health bar sits top-centre; drag it anywhere, right-click to reset.

## Building

Open tModLoader → Workshop → Develop Mods → AICompanion → Build + Reload. Or from a shell, `dotnet build` in this folder compiles against the installed tModLoader.
