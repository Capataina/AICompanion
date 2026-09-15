# Progression — what the companion has earned, priced by the game's own numbers

```
Progression/
├─ CLAUDE.md                  this guide
├─ CompanionExperience.cs     the ledger: level, experience into it, the requirement, both anchors, the pricing, save and load
├─ CreditKillsAndFights.cs    which kills count, whose killing blow it was, when a boss fight ends, and the NPC hooks that feed it
└─ CreditWork.cs              ore, trees and torches: the companion's from its own native calls, the player's from the tile hooks
```

The companion levels so that a level takes a similar amount of play whatever the world is — Journey or Master, vanilla or a modpack of Calamity with Infernum and Fargo's hardest mode — because every number the pricing reads is one the game set for this world: an enemy's maximum life after the difficulty scaled it, and a boss fight's whole life. There are no flat values to tune per difficulty, and a difficulty change moves what a kill earns and what a bar costs by the same factor. That is the owner's ruling of 15 September 2026, which replaced a placeholder curve and an earlier ruling that only the companion's own completed attempts earned. The ledger is character state, saved with `../PlayerIntegration/`. Nothing in `../Brain/` reads a level: a level changes what the companion can do and survive through the mastery tree, never what it decides.

## What earns, and how much

A kill earns the enemy's maximum life, in whole display experience. The ruling's unit is 0.1% of that life; the ledger keeps its values in that unit and the display divides it back out, because the factor multiplies the earned amount and the bar alike and cancels in every fraction. The companion's killing blow earns in full, the player's in half, anything else's — a trap, lava, a town NPC — nothing. Work earns a fixed share of the bar the level currently needs, the companion's in full and the player's in half: one ore tile broken, one tree felled (once per tree, when its bottom trunk tile goes), one torch placed.

Never counted, and never an anchor: friendly and town NPCs, critters (`NPCID.Sets.CountsAsCritter`), statue spawns, the target dummy and anything immortal. A worm or any body sharing `realLife` is one enemy at its head's life.

## How the bar is priced

Two anchors decide it. The enemy anchor is the largest maximum life of any counting non-boss kill so far, by either killer, with the level at which it was set; with no kill yet it is the green slime's maximum life in this world, read from the game at the moment it is needed. The boss anchor is the largest whole-fight life of any boss fight ended so far, with its level; there is none before the first boss. The enemy bar is a fixed number of kills of the enemy anchor; the boss bar is the size at which killing the anchor boss again fills a fixed share; each grows by a fixed factor per level since its own anchor was set. The requirement is the larger of the bars that exist, and never below the requirement it replaces — at a level-up as much as at a new anchor. The kill count, the boss share, the growth and the work share are the constants at the top of `CompanionExperience.cs`, and that file is where they are read.

A new, stronger anchor restarts its own growth count. That restart is required rather than a choice of taste: a count from the first level grows without bound, so the strongest boss would soon fill almost nothing.

An anchor-setting credit fills first and anchors second. Every level its experience completes is priced at the anchors as they stood before it; then the anchor moves, and the level in progress is repriced to the larger of its old requirement and the new formula. A fill completes a level within a tiny relative tolerance of the bar, because a thousandth of a bar is not exact in binary and the thousandth ore would otherwise land a rounding error short.

## Who landed the killing blow

The ledger finds a killing blow the way the game finds its own (`NPCKillAttempt`): the target is read just before a strike and again just after, and the strike killed it when the NPC holding the target's life is no longer the same living NPC. The before half is the NPC modify hook, which runs before `StrikeNPC`; the after half is the on-hit hook, which runs after it, and the whole death — `checkDead`, loot, deactivation — happens inside the strike between them. A slot's spawn generation, bumped in `OnSpawn`, tells a dead NPC from a new one reusing its slot. `GlobalNPC.OnKill` is not the kill signal, because NPCLoot returns before it whenever any mod's PreKill refuses, and a ledger fed from it would lose kills silently in exactly the modpacks the ruling names.

The striker is decided at the before half. A companion projectile is a slot the arsenal registered in `../Weapons/TrackLandedHits.cs`, or a child the outcome windows joined to one; ownership cannot say this, because every companion shot is owned by the local player. The player's is any other friendly projectile he owns that no trap and no town NPC fired, and his own melee through the item hooks. The companion's swing reaches the NPC through `Player.ApplyDamageToNPC`, which runs no NPC hook at all, so `../Weapons/ItemWeapon.cs` brackets its own strike with the same two calls.

A death with no strike around it — a debuff ticking the last life away, lava, a fall, a despawn — is nobody's killing blow and earns nothing. That is a consequence of the rule, not a guard, and it means a debuff weapon's kill earns only when a hit lands the last point.

## Boss fights

A boss fight is every boss body alive at once. A body is an NPC holding its own life that is flagged `boss` or `NPCID.Sets.ShouldBeCountedAsBoss`: the Twins, Moon Lord's core, head and hands, the Destroyer's head (its segments hold no life of their own), the Eater of Worlds' heads, the pillars, and every modded boss that sets the flag. A tick sweep after NPCs update joins every living body; when a sweep finds every body gone, the fight ends. It is credited once, at the sum of its bodies' maximum life, if any body died rather than left, and dropped as a despawn if none did. It is full when the companion landed the last strike on the last body that died and half otherwise. A body's killing blow is its last strike rather than a strike that ended it, because a boss may pass through a death state first: Moon Lord's core restores its life and turns invulnerable before it actually dies, and its hands and head never die on their own.

A body left dead when the sweep finds it inactive with no life, or when the loot path's `OnKill` saw it; the second covers a slot the game reused inside the same tick.

Boss parts and minions without the boss flag are the fight's company: an NPC in `NPCID.Sets.BossBestiaryPriority` that is not a body, or one a body's or company NPC's own AI spawned (`EntitySource_Parent` naming it). Company earns nothing, sets no anchor and adds nothing to the fight. Both other homes were measured against the pricing and lose. As ordinary enemies, a Prime arm's thousands of life would pin the enemy anchor for the rest of a playthrough and every hardmode enemy after it would pay almost nothing. As bodies, a boss that spawns minions without end would grow its own whole life without end, and the servants would hold the fight open after the eye was dead.

## What reaches in, and what it reaches

`../Weapons/ItemWeapon.cs` brackets the swing; `../Weapons/TrackLandedHits.cs` answers whether a projectile is the companion's. The miner, the chopper and the torch placer in `../Brain/Infrastructure/Interactions/` call `CreditWork` after their native edit, and only when the edit is observed to have happened. The player's tile hooks use the cursor target, the swing and the held tool's power, and skip world generation and the companion's own hits through `TileDamageWatcher.CompanionIsHitting` in `../Brain/Infrastructure/Observation/`. Every credit writes an `experience-credit` occurrence through `../Brain/Infrastructure/Diagnostics/RecordGodsEyeEvents.cs`, naming the source, the earner, the amount, the level before and after, the bar and both anchors with the one it moved. `../HeadsUpDisplay/` and `../ProfileCard/` read `Level`, `IntoLevel`, `NeededNow` and `Fraction`; `NeededNow` saturates at the largest integer, which a late modpack bar can pass, while the ledger keeps the true value.

A character saved under the placeholder curve carries only an integer `total`; it loads as a fresh level 1 and the key is ignored, which the owner accepted because those totals bought nothing.

## What it deliberately does not do

It grants no mastery points and spends none; the card still learns without points. It does not guard against cheap repeatable work — placing and re-mining an ore, placing and picking up a torch, felling acorn trees, duplicating ore in Journey — because the owner has not ruled a guard, and one invented here would be a product decision made by a bookkeeping file. A player-killed enemy with no strike on the killing tick earns nothing, and a boss fight whose last body died to a debuff still credits, because a boss's blow is its last strike.

## Traps

- **The fixtures call the static entry points, not the hooks.** The headless harness loads no GlobalNPC or GlobalTile, so `Tools/EngineReplay/Lifecycle/VerifyCompanionExperience.cs` drives `BeforeStrike` and `AfterStrike` around the game's own `Player.ApplyDamageToNPC`, and `PlayerKilledTile` directly. The hooks are one-line delegations for that reason; logic added to a hook body is logic no row runs.
