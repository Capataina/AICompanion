#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

/// <summary>
/// A weapon as the simulator sees it: the item, the gear slot, the composed numbers one use fires with, and the
/// swing sector for a weapon that swings. Identified from the hand's own weapon, so the sim prices the numbers the
/// use will actually fire rather than a second copy of them.
/// </summary>
public readonly record struct WeaponId(int ItemType, int Slot, bool IsSwing, int Damage, float Speed, float Knockback,
    int UseTime, int ManaCost, float SwingReach, int ProjectileFallback, bool PushesAwayFromOwner);

/// <summary>
/// The world as the simulator sees it: the two bodies a push is weighed against, and the terrain revision the
/// cache keys on. Tiles are read live through the engine's own collision, the way the observed-motion sense reads
/// them, so the sim and the engine cannot disagree about what is solid.
/// </summary>
public sealed class CombatWorld
{
    public Vector2 CompanionCentre;
    public Vector2 PlayerCentre;
    public int RefreshCount;

    public static CombatWorld Current(Vector2 companionCentre, Vector2 playerCentre, int refreshCount)
        => new() { CompanionCentre = companionCentre, PlayerCentre = playerCentre, RefreshCount = refreshCount };
}

/// <summary>One body a simulated use strikes: the slot, the tick of flight, the damage after armour and learning,
/// the push in x the weapon-effects table expects, whether the running tally has it dead, and where the strike lands.</summary>
public readonly record struct SimHit(int Slot, int Tick, float Damage, float DamageIfDebuffed, float PushX, bool Dead, Vector2 Position);

/// <summary>One wall contact a simulated flight survives: where, when, and the velocity in and out.</summary>
public readonly record struct SimBounce(Vector2 Position, int Tick, Vector2 In, Vector2 Out);

/// <summary>One simulated projectile's death: its type, where and when, and why.</summary>
public readonly record struct SimDeath(int ProjectileType, Vector2 Position, int Tick, DeathCause Cause);

/// <summary>
/// What one use is predicted to put into the world: the hits in the order they land, the bounces survived, the
/// deaths, the totals, and the two confidences — the flight laws' and the enemies' — the planner weighs the
/// prediction by. Paths carries one sampled path per spawn for the overlay's simulated-uses layer.
/// </summary>
public sealed class SimulatedUse
{
    public readonly List<SimHit> Hits = new();
    public readonly List<SimBounce> Bounces = new();
    public readonly List<SimDeath> Deaths = new();
    public readonly List<IReadOnlyList<Vector2>> Paths = new();
    public float TotalDamage;
    public int Struck;
    public int ManaCost;
    public float Predictability = 1f;
    public float EnemyConfidence = 1f;
    public bool Cut;

    /// <summary>First impact tick on any body, or -1 when the use hits nothing.</summary>
    public int ImpactTick => Hits.Count > 0 ? Hits[0].Tick : -1;
}

/// <summary>
/// One use, simulated: the learned volley expanded at its medians, each spawn flown sub-step by sub-step under
/// its learned law through the engine's own collision against the tick's enemy forecast, with learned wall, hit
/// and child responses at every event. What the sim predicts is what the hand fires — same expansion, same laws,
/// same responses — so a forecast that says a use kills is a claim about the use, checkable per shot against the
/// god's-eye trace it leaves.
///
/// <para>Damage runs the game's arithmetic for the body actually struck — defence off, the per-hit ratio on, the
/// weapon-effects correction on — and the push runs the same table's settled push. What the sim cannot see stays
/// with the residual learner, which now corrects the simulated totals rather than a straight-line prior.</para>
/// </summary>
public static class SimulateUse
{
    /// <summary>The profiler section one simulated use runs in. It is the hottest call a fight makes, entered once per
    /// use flown, which makes it the section whose own entry cost the profiler's overhead measurement is most
    /// sensitive to.</summary>
    private static readonly int SimulateSection = Diagnostics.BrainSections.Register("simulate");

    /// <summary>Deepest the child recursion goes; chains observed deeper than this are flown this far and their tail cut.</summary>
    public const int MaxChildDepth = 4;

    /// <summary>Record every Nth sub-step position for the overlay; the flight itself steps every update.</summary>
    public const int PathSampleEvery = 3;

    /// <summary>Default per-body hit cooldown in ticks where the sample names none: the game's own owner immunity.</summary>
    public const int DefaultHitCooldown = 10;

    public static WeaponId Identify(Interactions.Firing.CompanionWeapon weapon, in ActionContext ctx, int slot)
    {
        int damage = weapon.DamagePerHit(ctx);
        float speed = 0f, reach = 0f;
        if (weapon is Interactions.Firing.ItemWeapon item)
        {
            speed = item.LaunchSpeed;
            reach = item.SwingReach;
        }
        return new WeaponId(weapon.ItemType, slot, weapon.IsSwing, damage, speed, weapon.Knockback,
            weapon.UseTime, weapon.ManaCost, reach, weapon.ProjectileType, weapon.PushesAwayFromOwner);
    }

    /// <summary>
    /// The identity for a true/false question, where no context is priced: whether a use lands depends on the
    /// volley, the laws and the launch speed, never on the damage, mana or knockback numbers, so those travel as
    /// dummies. The positioner's stand proofs read this; anything that prices a use reads <see cref="Identify"/>.
    /// </summary>
    public static WeaponId IdentifyGeometry(Interactions.Firing.CompanionWeapon weapon, int slot)
    {
        float speed = 0f, reach = 0f;
        if (weapon is Interactions.Firing.ItemWeapon item)
        {
            speed = item.LaunchSpeed;
            reach = item.SwingReach;
        }
        return new WeaponId(weapon.ItemType, slot, weapon.IsSwing, 1, speed, 0f, 1, 0, reach,
            weapon.ProjectileType, false);
    }

    public static SimulatedUse Simulate(WeaponId weapon, Vector2 muzzle, Vector2 aimPoint, Vector2 launchDirection,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, ModifierState modifiers, int fireTick,
        ref DecisionWorkBudget budget)
    {
        using var section = Diagnostics.BrainSections.Enter(SimulateSection);
        budget.NoteSimulation();
        var use = new SimulatedUse { ManaCost = weapon.ManaCost };
        if (weapon.IsSwing)
        {
            SimulateSwing(weapon, muzzle, launchDirection, enemies, fireTick, use);
            return use;
        }
        var specs = new List<VolleySpawn>(LearnVolleyShapes.ShapeFor(weapon.ItemType)
            .Expand(muzzle, aimPoint, launchDirection == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(launchDirection),
                weapon.ProjectileFallback, weapon.Damage, weapon.Speed));
        // Extra projectiles copy the middle of the volley, offset a pellet-width across the aim line, so the
        // added spawn has its own spacing and the same damage. Mastery will name a real spacing; until then
        // this is the one extra the S5 plant asks for.
        for (int i = 0; i < modifiers.ExtraProjectiles && specs.Count > 0; i++)
        {
            VolleySpawn mid = specs[specs.Count / 2];
            Vector2 along = mid.Velocity == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(mid.Velocity);
            Vector2 across = new(-along.Y, along.X);
            specs.Add(mid with { Position = mid.Position + across * 8f * (i + 1) });
        }
        var life = new Dictionary<int, float>();
        foreach (EnemyForecast enemy in enemies)
            life[enemy.Slot] = enemy.Life;
        var confidences = new List<float> { 1f };
        foreach (VolleySpawn spec in specs)
        {
            if (!budget.Check()) { use.Cut = true; break; }
            FlySpawn(weapon, spec, depth: 0, fireTick + spec.DelayTicks, world, enemies, life, modifiers,
                aimPoint, use, confidences, ref budget);
            if (use.Cut) break;
        }
        float predictability = 1f;
        foreach (float confidence in confidences)
            predictability *= confidence;
        use.Predictability = predictability;
        use.EnemyConfidence = EnemyConfidenceOf(use, enemies);
        use.TotalDamage = 0f;
        var struck = new HashSet<int>();
        foreach (SimHit hit in use.Hits)
        {
            use.TotalDamage += hit.Damage;
            struck.Add(hit.Slot);
        }
        use.Struck = struck.Count;
        return use;
    }

    private static void SimulateSwing(WeaponId weapon, Vector2 muzzle, Vector2 launchDirection,
        IReadOnlyList<EnemyForecast> enemies, int fireTick, SimulatedUse use)
    {
        Vector2 direction = launchDirection == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(launchDirection);
        int tick = Math.Max(1, fireTick + 1);
        var life = new Dictionary<int, float>();
        foreach (EnemyForecast enemy in enemies)
            life[enemy.Slot] = enemy.Life;
        foreach (EnemyForecast enemy in enemies)
        {
            Rectangle box = enemy.PredictedBoxAtTick(tick);
            if (NearestDistance(muzzle, box) > weapon.SwingReach) continue;
            Vector2 toBody = new Vector2(box.Center.X, box.Center.Y) - muzzle;
            if (toBody != Vector2.Zero)
            {
                float angle = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(toBody), direction), -1f, 1f));
                if (angle > Interactions.Firing.ItemWeapon.SwingHalfAngle) continue;
            }
            if (!Collision.CanHit(muzzle, 1, 1, box.Location.ToVector2(), box.Width, box.Height)) continue;
            float damage = HitDamage(weapon, weapon.ProjectileFallback, enemy, weapon.Damage, 0);
            float remaining = life[enemy.Slot] - damage;
            life[enemy.Slot] = remaining;
            int pushDirection = WeaponEffects.PriorDirection(weapon.PushesAwayFromOwner, direction.X,
                box.Center.X, muzzle.X);
            float push = WeaponEffects.SettledPush(weapon.ItemType, enemy.NpcType, enemy.Velocity,
                enemy.KnockbackResist, enemy.OnFire2, (int)enemy.MaxLife, enemy.NoGravity,
                weapon.Knockback, damage, pushDirection);
            use.Hits.Add(new SimHit(enemy.Slot, tick, damage, float.NaN, push * pushDirection, remaining <= 0f,
                new Vector2(box.Center.X, box.Center.Y)));
        }
        use.TotalDamage = 0f;
        var struck = new HashSet<int>();
        foreach (SimHit hit in use.Hits)
        {
            use.TotalDamage += hit.Damage;
            struck.Add(hit.Slot);
        }
        use.Struck = struck.Count;
    }

    private static float NearestDistance(Vector2 point, Rectangle box)
    {
        float dx = MathF.Max(box.Left - point.X, MathF.Max(0f, point.X - box.Right));
        float dy = MathF.Max(box.Top - point.Y, MathF.Max(0f, point.Y - box.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void FlySpawn(WeaponId weapon, VolleySpawn spec, int depth, int startTick,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, Dictionary<int, float> life,
        ModifierState modifiers, Vector2 aimPoint, SimulatedUse use, List<float> confidences, ref DecisionWorkBudget budget)
    {
        FlightLaw law = FitFlightLaws.LawFor(spec.ProjectileType);
        confidences.Add(LawConfidence(law));
        ContentSamples.ProjectilesByType.TryGetValue(spec.ProjectileType, out Projectile? sample);
        int width = sample?.width ?? 10, height = sample?.height ?? 10;
        int declared = sample?.penetrate ?? 1;
        int bodiesAllowed = declared < 0 ? int.MaxValue
            : Math.Max(1, (int)MathF.Round((declared + modifiers.AddedPierce)
                * LearnHitResponses.ResponseFor(spec.ProjectileType).BodiesPerPenetrate));
        int cooldown = sample == null ? DefaultHitCooldown
            : sample.usesLocalNPCImmunity ? (sample.localNPCHitCooldown < 0 ? DefaultHitCooldown : Math.Max(1, sample.localNPCHitCooldown))
            : sample.usesIDStaticNPCImmunity ? (sample.idStaticNPCHitCooldown < 0 ? DefaultHitCooldown : Math.Max(1, sample.idStaticNPCHitCooldown))
            : DefaultHitCooldown;
        bool repeatsPossible = LearnHitResponses.ResponseFor(spec.ProjectileType).RepeatHitsObserved
            || (sample != null && (sample.usesLocalNPCImmunity || sample.usesIDStaticNPCImmunity));
        int updatesPerTick = law.UpdatesPerTick;
        int lifetime = law.LifetimeUpdates;
        var path = new List<Vector2> { spec.Position };
        use.Paths.Add(path);
        Vector2 position = spec.Position - new Vector2(width, height) / 2f;
        Vector2 velocity = spec.Velocity;
        int update = 0, bounces = 0, bodies = 0, hits = 0;
        var immuneUntil = new Dictionary<int, int>();
        var hitBefore = new HashSet<int>();
        int lastTimerSpawn = 0;
        var prospects = new List<EnemyForecast>();
        for (int tick = startTick; update < lifetime; tick++)
        {
            if (!budget.Check()) { use.Cut = true; return; }
            for (int sub = 0; sub < updatesPerTick && update < lifetime; sub++, update++)
            {
                Vector2 centre = position + new Vector2(width, height) / 2f;
                Vector2 toNpc = Vector2.Zero;
                float nearest = float.MaxValue;
                foreach (EnemyForecast enemy in enemies)
                {
                    Vector2 offset = enemy.PredictedCentre(tick) - centre;
                    float distance = offset.Length();
                    if (distance < nearest) { nearest = distance; toNpc = offset; }
                }
                bool wet = IsWet(centre);
                velocity = FlightLaw.AIVelocity(in law, velocity, update, toNpc, aimPoint - centre,
                    world.CompanionCentre - centre, wet);
                // The engine's order per sub-step: the NPC swoop at the pre-move box, the tile sweep, then the move.
                prospects.Clear();
                foreach (EnemyForecast enemy in enemies)
                {
                    Rectangle box = enemy.PredictedBoxAtTick(tick);
                    if (new Rectangle((int)position.X, (int)position.Y, width, height).Intersects(box))
                        prospects.Add(enemy);
                }
                prospects.Sort((a, b) => Vector2.DistanceSquared(a.PredictedCentre(tick), centre)
                    .CompareTo(Vector2.DistanceSquared(b.PredictedCentre(tick), centre)));
                foreach (EnemyForecast enemy in prospects)
                {
                    if (bodies >= bodiesAllowed) break;
                    if (immuneUntil.TryGetValue(enemy.Slot, out int until) && tick < until) continue;
                    if (!repeatsPossible && !hitBefore.Add(enemy.Slot)) continue;
                    hitBefore.Add(enemy.Slot);
                    float damage = HitDamage(weapon, spec.ProjectileType, enemy, spec.Damage, hits);
                    hits++;
                    float remaining = life[enemy.Slot] - damage;
                    life[enemy.Slot] = remaining;
                    bool away = WeaponEffects.PushesAwayFromOwner(spec.ProjectileType);
                    int pushDirection = WeaponEffects.PriorDirection(away, velocity.X,
                        enemy.PredictedCentre(tick).X, world.PlayerCentre.X);
                    float push = WeaponEffects.SettledPush(weapon.ItemType, enemy.NpcType, enemy.Velocity,
                        enemy.KnockbackResist, enemy.OnFire2, (int)enemy.MaxLife, enemy.NoGravity,
                        weapon.Knockback, damage, pushDirection);
                    use.Hits.Add(new SimHit(enemy.Slot, tick, damage, float.NaN, push * pushDirection, remaining <= 0f, centre));
                    immuneUntil[enemy.Slot] = tick + cooldown;
                    bodies++;
                    SpawnChildren(weapon, spec, model => model.Trigger == ChildTrigger.OnBodyHit, update, tick,
                        position, velocity, world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
                    if (bodies >= bodiesAllowed)
                    {
                        use.Deaths.Add(new SimDeath(spec.ProjectileType, centre, tick, DeathCause.PierceSpent));
                        SpawnChildren(weapon, spec, model => model.Trigger == ChildTrigger.OnParentDeath, update, tick,
                            position, velocity, world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
                        ApplyArea(weapon, spec, centre, tick, AreaTrigger.OnDeath, world, enemies, life, use);
                        return;
                    }
                }
                Vector2 before = velocity;
                Vector2 collided = sample != null && !sample.tileCollide ? velocity
                    : Collision.TileCollision(position, velocity, width, height, false, false, 1);
                bool hitWall = collided.X != velocity.X || collided.Y != velocity.Y;
                velocity = collided;
                if (hitWall)
                {
                    WallResponse wall = LearnWallResponses.ResponseFor(spec.ProjectileType);
                    WallKind kind = wall.Kind;
                    if (kind == WallKind.Unknown)
                        kind = sample != null && !sample.tileCollide ? WallKind.Passes : WallKind.Dies;
                    if (kind == WallKind.Dies || kind == WallKind.Stops)
                    {
                        use.Deaths.Add(new SimDeath(spec.ProjectileType, centre, tick, DeathCause.Wall));
                        SpawnChildren(weapon, spec, model => model.Trigger is ChildTrigger.OnWallContact or ChildTrigger.OnParentDeath,
                            update, tick, position, velocity, world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
                        ApplyArea(weapon, spec, centre, tick, AreaTrigger.OnWall, world, enemies, life, use);
                        ApplyArea(weapon, spec, centre, tick, AreaTrigger.OnDeath, world, enemies, life, use);
                        return;
                    }
                    if (kind == WallKind.Passes)
                    {
                        velocity = before;
                    }
                    else
                    {
                        bounces++;
                        Vector2 reflected = before;
                        if (collided.X != before.X) reflected.X = before.X * wall.RestitutionNormal;
                        else reflected.X = before.X * wall.RestitutionTangent;
                        if (collided.Y != before.Y) reflected.Y = before.Y * wall.RestitutionNormal;
                        else reflected.Y = before.Y * wall.RestitutionTangent;
                        reflected *= wall.SpeedFactorAfterBounce;
                        use.Bounces.Add(new SimBounce(centre, tick, before, reflected));
                        velocity = reflected;
                        if (wall.RetargetingBounce && wall.BounceHoming is { } bounceHoming)
                            law = law with { Homing = bounceHoming with { OnsetUpdate = update } };
                        SpawnChildren(weapon, spec, model => model.Trigger == ChildTrigger.OnWallContact, update, tick,
                            position, velocity, world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
                        ApplyArea(weapon, spec, centre, tick, AreaTrigger.OnWall, world, enemies, life, use);
                        if (bounces > wall.BounceCount)
                        {
                            use.Deaths.Add(new SimDeath(spec.ProjectileType, centre, tick, DeathCause.Wall));
                            return;
                        }
                    }
                }
                position += velocity;
                if (update % PathSampleEvery == 0)
                    path.Add(position + new Vector2(width, height) / 2f);
                foreach (ChildModel child in LearnChildSpawns.ChildrenFor(spec.ProjectileType))
                {
                    if (child.Trigger != ChildTrigger.Timer || child.Period.Count == 0) continue;
                    int period = Math.Max(1, (int)child.Period.Median(1f));
                    if (update - lastTimerSpawn >= period)
                    {
                        lastTimerSpawn = update;
                        SpawnChild(weapon, spec, child, update, tick, position, velocity,
                            world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
                    }
                }
            }
        }
        Vector2 expired = position + new Vector2(width, height) / 2f;
        int expiredTick = startTick + lifetime / Math.Max(1, updatesPerTick);
        use.Deaths.Add(new SimDeath(spec.ProjectileType, expired, expiredTick, DeathCause.Expired));
        SpawnChildren(weapon, spec, model => model.Trigger == ChildTrigger.OnParentDeath, update, expiredTick,
            position, velocity, world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
        ApplyArea(weapon, spec, expired, expiredTick, AreaTrigger.OnDeath, world, enemies, life, use);
    }

    private static void SpawnChildren(WeaponId weapon, VolleySpawn spec, Func<ChildModel, bool> trigger,
        int update, int tick, Vector2 position, Vector2 velocity,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, Dictionary<int, float> life,
        ModifierState modifiers, Vector2 aimPoint, SimulatedUse use, List<float> confidences,
        ref DecisionWorkBudget budget, int depth)
    {
        foreach (ChildModel child in LearnChildSpawns.ChildrenFor(spec.ProjectileType))
        {
            if (!trigger(child)) continue;
            SpawnChild(weapon, spec, child, update, tick, position, velocity,
                world, enemies, life, modifiers, aimPoint, use, confidences, ref budget, depth);
        }
    }

    private static void SpawnChild(WeaponId weapon, VolleySpawn spec, ChildModel child, int update, int tick,
        Vector2 position, Vector2 velocity,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, Dictionary<int, float> life,
        ModifierState modifiers, Vector2 aimPoint, SimulatedUse use, List<float> confidences,
        ref DecisionWorkBudget budget, int depth)
    {
        if (depth >= MaxChildDepth) return;
        if (!budget.Check()) { use.Cut = true; return; }
        int count = Math.Max(1, (int)MathF.Round(child.Count.Median(1f)));
        Vector2 direction = velocity == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(velocity);
        Vector2 across = new(-direction.Y, direction.X);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = direction * child.OffsetAlong.Median() + across * child.OffsetAcross.Median();
            Vector2 childVelocity = direction * child.VelocityAlong.Median() + across * child.VelocityAcross.Median();
            int damage = Math.Max(1, (int)(spec.Damage * child.DamageRatio.Median(1f)));
            var childSpec = new VolleySpawn(child.ChildType, position + offset, childVelocity, damage, tick);
            FlySpawn(weapon, childSpec, depth + 1, tick, world, enemies, life, modifiers, aimPoint,
                use, confidences, ref budget);
            if (use.Cut) return;
        }
    }

    private static void ApplyArea(WeaponId weapon, VolleySpawn spec, Vector2 centre, int tick, AreaTrigger trigger,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, Dictionary<int, float> life, SimulatedUse use)
    {
        AreaResponse area = LearnHitResponses.ResponseFor(spec.ProjectileType).Area;
        if (area.Trigger != trigger || area.Radius <= 0f) return;
        foreach (EnemyForecast enemy in enemies)
        {
            Rectangle box = enemy.PredictedBoxAtTick(tick);
            float dx = MathF.Max(box.Left - centre.X, MathF.Max(0f, centre.X - box.Right));
            float dy = MathF.Max(box.Top - centre.Y, MathF.Max(0f, centre.Y - box.Bottom));
            if (dx * dx + dy * dy > area.Radius * area.Radius) continue;
            float damage = HitDamage(weapon, spec.ProjectileType, enemy, spec.Damage, 0);
            float remaining = life[enemy.Slot] - damage;
            life[enemy.Slot] = remaining;
            use.Hits.Add(new SimHit(enemy.Slot, tick, damage, float.NaN, 0f, remaining <= 0f,
                new Vector2(box.Center.X, box.Center.Y)));
        }
    }

    private static float HitDamage(WeaponId weapon, int projectileType, EnemyForecast enemy, int baseDamage, int hitIndex)
    {
        float ratio = LearnHitResponses.ResponseFor(projectileType).RatioForHit(hitIndex);
        float afterArmour = WeaponEffects.PriorDamage(baseDamage * ratio, enemy.Defense);
        return MathF.Max(1f, afterArmour * WeaponEffects.DamageFactor(weapon.ItemType, enemy.NpcType));
    }

    private static float LawConfidence(FlightLaw law)
        => law.Predictable ? Math.Clamp(1f - law.ResidualPerUpdate / FitFlightLaws.UnpredictableResidualBound, 0f, 1f) : 0f;

    private static float EnemyConfidenceOf(SimulatedUse use, IReadOnlyList<EnemyForecast> enemies)
    {
        SimHit? first = null;
        foreach (SimHit hit in use.Hits)
        {
            if (hit.Tick > 1) { first = hit; break; }
        }
        first ??= use.Hits.Count > 0 ? use.Hits[0] : null;
        if (first == null) return 1f;
        foreach (EnemyForecast enemy in enemies)
            if (enemy.Slot == first.Value.Slot) return enemy.ConfidenceAtTick(first.Value.Tick);
        return 1f;
    }

    private static bool IsWet(Vector2 centre)
    {
        int x = (int)(centre.X / 16f), y = (int)(centre.Y / 16f);
        if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY)
            return false;
        return Main.tile[x, y].LiquidAmount > 0;
    }
}
