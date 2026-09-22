# Recording — every flight watched, every use grouped

```
Recording/
├─ CLAUDE.md
├─ RecordProjectileFlights.cs   every watched projectile's trace: steps, wall contacts, hits, children, death cause
└─ GroupSpawnsIntoUses.cs       the spawn stream grouped into the player's and the companion's uses
```

This folder is the observation half of `../` — it watches, it does not decide and it does not learn. `RecordProjectileFlights` is where a projectile's whole life is filed as it happens, tick by tick, from the engine's own hooks; `GroupSpawnsIntoUses` is where the spawn stream those hooks also produce is cut into the *uses* — one press of the trigger, one swing — that the learners in `../Learning/` actually fit against. Everything here is upstream of learning: `FitFlightLaws`, `LearnWallResponses`, `LearnHitResponses`, `LearnChildSpawns` and `LearnVolleyShapes` all read closed traces and closed uses from this folder and write nothing back into it.

## Two objects, and the reason there are two

A **trace** (`FlightTrace`, in `RecordProjectileFlights.cs`) is one projectile's physical life: its steps, its wall contacts, its body hits, its children and how it died. A **use** (`ProjectileUse`, in `GroupSpawnsIntoUses.cs`) is one act of firing: the set of projectiles one trigger-pull or swing produced, which for a volley weapon is several traces sharing one use. The two are recorded by different rules because they answer different questions — a trace answers "how does this projectile type fly", which `../Learning/FitFlightLaws.cs` and `../Learning/LearnWallResponses.cs` need per-projectile; a use answers "what does one press of the trigger put into the world", which `../Learning/LearnVolleyShapes.cs` needs per-item. `GroupSpawnsIntoUses` reads the same spawn stream `RecordProjectileFlights` traces, but groups it by animation or by firing call rather than by projectile identity.

## `RecordProjectileFlights`: one trace per watched projectile

A trace opens on `NoteSpawn`, called from the engine's spawn hook, for the player's own shots (identified by the item-use source) and the companion's (identified by the hand's own call, which names no item and no aim — `NoteCompanionSpawn` supplies both explicitly). A child spawned from a watched flight is traced too, from its parent's source, so a bomb's fragments and a homing staff's re-fired bolt are watched exactly as the parent shot was.

Two kinds of sample land in `FlightTrace.Steps`, and a consumer has to be able to tell them apart, which is what `FlightStep.Settled` is for: **post-AI** samples arrive once per physics sub-step, before the engine's tile collision and movement run that tick, and are the velocities the arc learner and the flight-law fitter actually fit against; **settled** samples arrive once per tick from `PostUpdateProjectiles`, after collision and movement, and are collision-modified, so they anchor the overlay's drawn path and the audit's trace-error measurement rather than teaching a law. `FitFlightLaws.cs` states this directly: a settled sample entering a fit would teach the fitter the wall's reflection rather than the projectile's own flight.

`NoteWallContact` and `NoteHit` file into `Walls` and `Hits` as they happen, each carrying enough context for a learner three steps downstream to reconstruct what actually occurred — a `WallContact` carries the velocity going in (at the collide hook) and going out (filled from the next settled sample), and a `BodyHit` carries whether the NPC's box actually overlapped the projectile's own box, which is how `../Learning/LearnHitResponses.cs` tells a contact hit from area damage without being told which kind it is. `NoteDeath` closes the trace and reads `Steps`, `Walls` and `Hits` to name the `DeathCause` — expired, spent its pierce, hit a wall, or something else — from what the trace already holds, never from a flag set elsewhere.

## `GroupSpawnsIntoUses`: the spawn stream cut by animation and by call

A **player** use opens the tick `NotePlayerAnimation` sees `itemAnimation` reset to its max for the held item, and collects every spawn from his item-use source until the animation ends — one rule that catches a same-tick spread, a sky rain and a burst across the whole swing with nothing item-specific. A **companion** use is opened and closed explicitly by `ItemWeapon.Fire` itself (`OpenCompanionUse` / `CloseCompanionUse`), because the companion's spawns carry no item and no aim on the engine's own source and so cannot be grouped by animation at all. A use that spawned nothing — a sword swing, a tool stroke — is kept for inspection but taught to nothing: `LearnVolleyShapes` only has an opinion about what a weapon puts into the world, and a swing puts nothing into the world to measure.

A closed use is taught to `../Learning/LearnVolleyShapes.cs` the instant it closes (`Learn`), which is why volley shapes only ever learn from the *player's* uses even though both are recorded here: the companion's own volleys are reproductions of the medians the player already taught, and feeding them back would narrow every slot toward whatever the companion already fires, collapsing the distribution around its own output rather than the player's.

`Complete` on `ProjectileUse` matters for the same reason a trace's step kind matters: a use the grouping joined *mid-animation* — the recorder started listening after the animation was already running — still has its individual pellets taught correctly, because each pellet is a real spawn, but its total count is short and must never be read as "this weapon fires N projectiles per use." Reading `Complete` before trusting a use's count is a caller's obligation this file cannot enforce for them.

## What is deliberately not here

Neither file learns anything, corrects anything, or decides what a weapon *should* do — that is `../Learning/`'s job, reading `ClosedUses` and `ClosedFor(projectileType)` as its only inputs from this folder. Neither file simulates a hypothetical shot — that is `../Simulation/`'s job, which reads the learned models these two feed rather than these two directly. Both are pure observation of what the engine's own hooks actually produced, which is why they can watch the player's weapons as reliably as the companion's: nothing here assumes who fired the shot beyond the `Shooter` tag needed to route the sample to the right side of a learner.

## Traps

**A trace's post-AI series and its settled series answer different questions, and mixing them silently produces a plausible-looking wrong law.** `FitFlightLaws.cs`'s own docstring names this: only post-AI diffs teach a term, because a settled sample already carries the wall's own bounce baked in as if it were the projectile's dynamics.

**A companion use's source names neither an item nor an aim.** Any code that tries to identify a companion's own spawn by reading the engine's `IEntitySource` the way a player spawn is read will find nothing — `OpenCompanionUse` and `NoteCompanionAim` exist precisely because the companion side has to supply that context explicitly rather than have the engine hand it over.

**A use with `Complete == false` is real data with a short count.** Its pellets are legitimate samples for the arc and hit learners; its `Spawns.Count` is not a legitimate sample of "how many pellets this weapon fires."

**`MaxClosedUsesKept` (32) and `MaxTracesPerType` (8) are both small, bounded rings, not unbounded history.** A learner reading `ClosedFor(projectileType)` or `ClosedUses` is reading whatever is still in the ring, not everything ever fired; this is deliberate (a changed enemy or a changed loadout should be re-learned from what happens now, not diluted by history), and it is the same shape `../Learning/LearnWeaponEffectsOnEnemies.cs` uses for its own sample rings.

## Relations

`../Learning/FitFlightLaws.cs` and `../Learning/LearnWallResponses.cs` read `RecordProjectileFlights.ClosedFor`. `../Learning/LearnHitResponses.cs` and `../Learning/LearnChildSpawns.cs` read the same traces' `Hits` and `Children`. `../Learning/LearnVolleyShapes.cs` reads `GroupSpawnsIntoUses.ClosedUses` (the player's only). `../../Interactions/Firing/` calls `NoteCompanionSpawn`, `OpenCompanionUse` and `CloseCompanionUse` when the arsenal actually fires. The engine's own spawn, AI and collision hooks — decompiled and described in `../CLAUDE.md`'s "engine's hook order" section — are what call `NoteSpawn`, `NoteStep`, `NoteSettled`, `NoteWallContact`, `NoteHit` and `NoteDeath` in the first place; this folder does not install those hooks itself.

## Current state

Landed in the combat plan's phase B (`4272a52`, `7a35aa7`), alongside the rest of the learned-knowledge system, and untouched since — no commit in this repository's history touches either file after phase B closed. Both are session state: cleared on process restart and by the engine replay's per-case reset, exactly as `../CLAUDE.md` states for the folder as a whole.
