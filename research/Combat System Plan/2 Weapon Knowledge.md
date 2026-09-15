# 2 Weapon Knowledge — what is learned about a weapon, from what, stored how

Layer 1 answers one question for the layers above: if this item is used from here, at this angle, with these modifiers, what goes into the world and what does each piece do. It decides nothing. It is fed by every projectile use it can observe, the player's as well as the companion's, and it is read by the simulator (file 3) and nothing else.

## The rule that shapes every table here

Every property in the survey's taxonomy is either **read from the game**, **learned from observation**, or **out of scope with a revival condition**. Nothing is keyed by a weapon category, and no learned value may absorb something the companion itself changed (a mastery modifier), because that value would then be wrong the moment the modifier changes.

| Property | Kind | Key | Where it comes from |
|---|---|---|---|
| Base damage, use time, knockback, mana cost, shoot speed, default ammo | read | item (+ ammo) | `ItemWeapon`'s existing composition, unchanged |
| Declared pierce, lifetime, extra updates per tick, hitbox, NPC hit cooldowns, tile collision flag | read | projectile type | `ContentSamples.ProjectilesByType`: `penetrate`, `timeLeft`, `extraUpdates`, `width`/`height`, `usesLocalNPCImmunity`, `localNPCHitCooldown`, `usesIDStaticNPCImmunity`, `idStaticNPCHitCooldown`, `tileCollide` |
| Volley shape | learned | item, per volley slot | spawns grouped into uses, below |
| Flight law | learned | projectile type | traces, below |
| Wall response | learned | projectile type | tile contacts, below |
| Enemy response | learned | projectile type | the landed-hit ledger, below |
| Children | learned | parent projectile type → child type | parent-source attribution, below |
| Area | learned | projectile type | bodies struck off the trace, below |
| Push per enemy type | learned | item × NPC type | `LearnWeaponEffectsOnEnemies`, unchanged |
| Debuff chance and length | learned | item × NPC type | `LearnAttackOutcomes`' debuff table, unchanged |
| Residual value | learned | item × context | `LearnAttackOutcomes`' regression, now the correction on the simulator |

## Observation: one pipeline for every projectile

Today only registered companion shots are watched, for 45 post-AI velocities. The replacement watches every friendly damaging projectile whose source is a use by the local player or a companion shot, to its death, because the player fires most weapons many times before handing one over.

```
spawn  ──► GlobalProjectile.OnSpawn(source)
             ├─ EntitySource_ItemUse_WithAmmo from Main.LocalPlayer → a player use sample
             ├─ the companion NPC's source (ItemWeapon.Fire)          → a companion use sample
             └─ EntitySource_Parent naming a watched projectile       → a child sample
per update ─► GlobalProjectile.PostAI              velocity after the AI, before movement
wall      ──► GlobalProjectile.OnTileCollide        oldVelocity in, whether the projectile dies
per tick  ──► ModSystem.PostUpdateProjectiles      position and velocity after movement
hit       ──► the existing NPC modify and on-hit hooks in TrackLandedHits
death     ──► GlobalProjectile.OnKill               position, and why (timeLeft, pierce spent, wall, other)
```

**The hook order is a claim to verify before the learners are written**, and phase 0 does it against the decompiled `Projectile.Update` and `Projectile.HandleMovement` (`sh Tools/decompile.sh Terraria.Projectile`): whether `PostAI` runs once per extra update, whether `OnTileCollide` sees the pre-reflection velocity, and where `extraUpdates` sub-steps sit relative to `PostUpdateProjectiles`. The learners below are written against whatever that probe establishes, and the probe's findings go into `Companion/Brain/Infrastructure/WeaponKnowledge/CLAUDE.md` at the seam.

Each observed projectile becomes a `FlightTrace` in `RecordProjectileFlights.cs`:

```csharp
public sealed class FlightTrace
{
    public int ProjectileType;            // the full name is resolved at save time, below
    public Shooter Shooter;              // Player or Companion
    public ModifierState Modifiers;      // what the companion applied at spawn; empty for the player
    public int DeclaredPenetrate;        // as spawned, after modifiers
    public List<FlightStep> Steps;       // one per update, sub-steps included
    public List<WallContact> Walls;
    public List<BodyHit> Hits;
    public List<ChildSpawn> Children;
    public DeathCause Death;
}
public readonly record struct FlightStep(int Update, Vector2 Position, Vector2 Velocity,
    Vector2 ToNearestEligibleNpc, Vector2 ToAimPoint, Vector2 ToOwner, bool Wet);
public readonly record struct WallContact(int Update, Vector2 Position, Vector2 VelocityIn, Vector2 VelocityOut, Vector2 Normal, bool Died);
public readonly record struct BodyHit(int Update, int NpcType, Vector2 ProjectileCentre, Rectangle NpcBox, bool BoxOverlapsProjectile, int Damage, int HitIndex);
public readonly record struct ChildSpawn(int Update, int ChildType, Vector2 OffsetInParentFrame, Vector2 VelocityInParentFrame, ChildTrigger Trigger);
```

`ToAimPoint` is the player's cursor in world coordinates for his shots and the companion's aim point for its own; `ToOwner` is the player for both, because both are owned by the player (file 5 on why). Traces are bounded per type, and a new trace replaces the one that reduces parameter uncertainty least rather than the oldest.

## Volley shape: what one use puts into the world

`GroupSpawnsIntoUses.cs` turns spawn samples into uses. A player use begins on the tick his `itemAnimation` resets to `itemAnimationMax` for the item and collects every projectile his `EntitySource_ItemUse_WithAmmo` spawns until the animation ends, which captures same-tick spreads (shotguns), rains from the sky (Daedalus Stormbow) and bursts across the animation (burst rifles) with one rule. `LearnVolleyShapes.cs` keeps, per item:

```csharp
public sealed class VolleyShape
{
    public Histogram Count;                          // projectiles per use
    public List<VolleySlot> Slots;                   // ordered by spawn delay, then angle
    public MultishotState LearnedUnder;              // archery potion, quivers, other count-changing buffs seen
}
public sealed class VolleySlot
{
    public int? ProjectileType;                      // null where the item uses ammo: substituted from the companion's ammo at fire time
    public Distribution AngleFromAimLine;            // radians
    public Distribution SpeedRatio;                  // against the item's composed shoot speed
    public Distribution DamageShare;                 // against the item's composed damage at that moment
    public OriginKind Origin;                        // Shooter | AboveAim | AtAim | AroundShooter
    public Distribution OriginOffset;                // in the frame of the aim line, from the origin kind's anchor
    public Distribution DelayTicks;                  // from the start of the use
}
```

The origin kind is chosen per slot by which anchor — the player's centre, the cursor, a fixed height above the cursor — leaves the smallest variance in the offset, so the sky-rain case is found without being named. Samples taken under a count-changing effect the companion lacks are kept apart and not used for its own volley. An item never seen used has a default shape: one slot, the item's own projectile, angle zero, speed ratio one, damage share one, which is exactly what `ItemWeapon.Fire` does today.

## The flight law: a closed library of terms

`FlightLaw.cs` holds the law's terms and one update of flight under them; `FitFlightLaws.cs` chooses and fits the terms.

```csharp
public sealed record FlightLaw(int ProjectileType, int Revision, int UpdatesPerTick, int LifetimeUpdates,
    GravityTerm? Gravity, DragTerm Drag, SpeedChangeTerm? SpeedChange, HomingTerm? Homing,
    SteerToAimTerm? SteerToAim, ReturnToOwnerTerm? Return, WallResponse Wall,
    float ResidualPerUpdate, int Evidence, bool Predictable);

public readonly record struct GravityTerm(int OnsetUpdate, float Acceleration, float MaxFallSpeed);
public readonly record struct DragTerm(float Horizontal, float Vertical);
public readonly record struct SpeedChangeTerm(int OnsetUpdate, float RatioPerUpdate, float MinSpeed, float MaxSpeed);
public readonly record struct HomingTerm(int OnsetUpdate, float Radius, HomingAnchor Anchor, float Blend, float Speed);
public readonly record struct SteerToAimTerm(int OnsetUpdate, float MaxTurnPerUpdate);
public readonly record struct ReturnToOwnerTerm(int TurnUpdate, float Acceleration, float MaxSpeed);
```

One update under a law, in order: apply speed change, drag, gravity with its cap, then homing (`v ← (1 − Blend)·v + Blend·Speed·dir(anchor → nearest eligible NPC within Radius)`), then steering toward the aim point within the turn cap, then return. The order is fixed so that fitting and flying agree.

**Fitting is forward stagewise selection with a penalty.** Start from drag alone and fit it by least squares on per-update velocity differences. For each unused term, fit its parameters against the residual — linear terms by least squares, onset and radius by a coarse grid then a local refinement — and add the term whose inclusion most improves a penalised score (residual sum of squares plus a fixed cost per parameter times the log of the sample count, the Bayesian information criterion). Stop when no term improves the score. This is sparse identification of dynamics in its simplest form: propose many terms, keep the few the data needs. The steer-to-aim and homing terms compete honestly, because a trace carries both the nearest enemy and the aim point, and for the player's shots the cursor and the enemy are rarely in the same place.

A term is used by the simulator only once its parameters' standard error is below a stated share of their value; until then the law flies with the terms that are confident and marks itself less predictable. A type whose residual stays above the unpredictable bound with every term available is `Predictable = false`: it is still fired, at the intercept, and valued by the residual learner alone. **The gear stops refusing weapons for being unfittable**; `CompanionGear.Accepts`' unfittable branch is deleted in phase B.

Before any evidence, a type's law is today's prior: `ProjectileArcs.Prior`'s arrow and thrown-object numbers for those AI styles, straight flight otherwise, which `VerifyArcLearning` already holds against native AI.

## Walls, enemies, children and area

**Wall response** (`LearnWallResponses.cs`) classifies each type from its contacts: dies, passes (a `tileCollide = false` sample never contacts), reflects, or stops. A reflecting type learns a restitution per axis from `VelocityOut·n / VelocityIn·n` and `VelocityOut·t / VelocityIn·t`, a speed factor after a bounce, a bounce count (contacts before death when the death cause is a wall), and whether the last bounce departs from reflection by more than the residual bound, which marks a retargeting bounce such as Calamity's shotgun pellet; a retargeting bounce is flown as a homing term that switches on at that bounce.

**Enemy response** (`LearnHitResponses.cs`) learns, per type, distinct bodies struck before a death by pierce against the declared `penetrate` at spawn (so a companion-modified pierce is predicted by reading the instance, and nothing is relearned), the damage ratio of the k-th hit to the first (whip-style falloff), and repeat hits on one body against the read hit cooldowns.

**Children** (`LearnChildSpawns.cs`) are classified by what coincided with the spawn in the same update: a body hit, a wall contact, the parent's death, or none, in which case the trigger is a timer and its period is learned. Offsets and velocities are stored in the parent's velocity frame so they rotate with the shot. Each child flies its own learned law, to the depth of chain observed.

**Area** is learned from hits whose NPC box did not overlap the projectile box (`BoxOverlapsProjectile = false`): the radius is the largest distance from the projectile's centre at which such a hit landed, attached to the event that coincided (a death, a wall contact).

## Owner and cursor reads

Companion projectiles stay owned by the local player, because kills, drops, on-hit effects and experience attribution all rely on it. `SpoofOwnerInputForShots.cs` sets `Main.mouseX` and `Main.mouseY` so `Main.MouseWorld` is the companion's aim point during `PreAI` of a registered companion projectile and restores them in `PostAI`, which is TerraGuardians' shipped mechanism. An aim point off screen is written as the true value; AI reading `ClampedMouseWorld` sees the clamp, and the law learns it.

Owner-position reads are not spoofed. A projectile whose learned law has a return term, or whose player-shot traces steer toward the player's body rather than the cursor, is refused at the slot by that observed property (question 4 in file 1), which replaces today's refusal by a list of AI styles.

## Modifiers enter at one seam and are recorded everywhere

`ApplyCompanionModifiers.cs` (in Simulation, file 3) is the only place a companion-side change to a use is applied: added projectiles from Extra projectile, pierce from Piercing, and anything mastery adds later, read from the mastery bonuses record when that exists and from nothing until then. The spawned instance carries the result, and every trace records `ModifierState`. Learners that could absorb a modifier exclude modified traces or normalise by the recorded state: volley shapes learn only from unmodified player uses; flight laws are indifferent to count; enemy responses read the instance's declared pierce.

## Keeping what was learned

Knowledge is saved per character on `CompanionPlayer`, beside the gear, so a companion does not relearn a bow each session. Keys are **full names** (`Mod/ItemName`, `Mod/ProjectileName`) resolved through `ItemID.Search` and `ProjectileID.Search` and their mod-loader equivalents, never numeric ids, because modded ids are assigned at load and differ between mod sets. A saved law for a name that no longer resolves is dropped on load. The save carries a knowledge schema version; a mismatch drops the knowledge rather than migrating it, because relearning costs a few minutes of play and a migration is a second system. The residual learner's posteriors are saved the same way. This is question 6 in file 1; the default is to save.

## Out of scope in the first build, and what brings each back

| Out | Why | Revives when |
|---|---|---|
| Channelled beams, charge-then-release, yoyos, flails, whips, spears | behaviour depends on a held button or live owner state (`player.channel`, `itemAnimation`) the companion does not produce; spoofing `player.channel` risks the player's own item use | a companion-side hold counter is designed and a fixture shows a vanilla channel beam living exactly as long as the companion holds |
| Summons, sentries, minions | a second actor with its own targeting | the owner rules the companion may own minions |
| Class-resource gates (Calamity stealth, Thorium empowerment, Metroid overheat) | the alternate projectile is chosen in item-use code the companion never runs | the owner rules whether the companion mirrors a class resource as it mirrors mana |
| Items whose behaviour is not fixed per item (Stars Above aspects, Metroid addons) | one key sees several weapons | detected rather than solved: projectile types are keys, so differing spawned types already separate |
| Whip tags exploited by minions | the effect is in other actors | with minions |
| Damage over time | a burn's later life loss arrives through no hit hook and would credit the player's own attacks | a per-body buff-tick attribution is designed |
