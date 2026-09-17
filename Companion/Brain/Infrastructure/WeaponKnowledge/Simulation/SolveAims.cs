#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

/// <summary>One aim the simulator prices: the point aimed at and the direction the use leaves along. The two differ
/// for anything that arcs — the hand fires along the launch and the volley anchors on the aim.</summary>
public readonly record struct AimCandidate(Vector2 AimPoint, Vector2 LaunchDirection);

/// <summary>
/// Aim points from the muzzle at the target: the intercept, offsets over the volley spread, single banks off
/// nearby walls, and lines through two or more bodies. Every candidate is priced by the simulator, which is what
/// admits or rejects it — a bank whose bounce the wall response does not predict scores nothing and is never
/// chosen. This replaces the trajectory solver: where that traced one ray under a four-number arc, this proposes
/// aims and lets the simulated use decide among them.
/// </summary>
public static class SolveAims
{
    /// <summary>How many aims one call proposes at most: the intercept, the spread, the banks, the pierce lines.</summary>
    public const int MaxAims = 10;

    private const float SweepStepRadians = MathHelper.Pi / 60f;
    private const float SweepElevationRadians = MathHelper.Pi * 0.45f;
    private const float SweepDepressionRadians = MathHelper.Pi / 3f;

    /// <summary>
    /// The positioner's cheap true/false: sweep launch angles flattest-first from the direct line, simulating each
    /// until one lands on the target. The sweep proposes angles the way the old solver did; every verdict comes from
    /// a full use simulated against the enemy forecast, so walls, arcs and volleys are priced truly. Null when no
    /// angle lands. Banks are not swept — a stand proved only by a bank waits for the planner's own generator.
    /// </summary>
    public static (AimCandidate Aim, SimulatedUse Use)? FirstLanding(WeaponId weapon, Vector2 muzzle, EnemyForecast target,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, int fireTick, ref PlanningBudget budget)
    {
        int aimTick = Math.Max(1, fireTick);
        Vector2 toTarget = target.PredictedCentre(aimTick) - muzzle;
        if (toTarget == Vector2.Zero) return null;
        float direct = toTarget.ToRotation();
        float distance = toTarget.Length();
        for (float offset = 0f; offset <= SweepElevationRadians; offset += SweepStepRadians)
        {
            if (TryAngle(weapon, muzzle, direct - offset, distance, target, world, enemies, fireTick, ref budget)
                is { } landed)
                return landed;
            if (offset > 0f && offset <= SweepDepressionRadians
                && TryAngle(weapon, muzzle, direct + offset, distance, target, world, enemies, fireTick, ref budget)
                is { } lower)
                return lower;
            if (!budget.Check()) return null;
        }
        return null;
    }

    private static (AimCandidate Aim, SimulatedUse Use)? TryAngle(WeaponId weapon, Vector2 muzzle, float angle,
        float distance, EnemyForecast target, CombatWorld world, IReadOnlyList<EnemyForecast> enemies, int fireTick,
        ref PlanningBudget budget)
    {
        Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
        var aim = new AimCandidate(muzzle + direction * distance, direction);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        int knowledge = KnowledgeRevision.Current;
        if (!CacheSimulatedUses.TryGet(weapon, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, out SimulatedUse? use) || use == null)
        {
            use = SimulateUse.Simulate(weapon, muzzle, aim.AimPoint, direction, world, enemies, modifiers, fireTick, ref budget);
            CacheSimulatedUses.Store(weapon, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, use);
        }
        foreach (SimHit hit in use.Hits)
            if (hit.Slot == target.Slot)
                return (aim, use);
        return null;
    }

    /// <summary>How far around the direct angle the intercept search looks, radians each side.</summary>
    public const float InterceptSearchHalfAngle = 0.6f;

    /// <summary>How far from the muzzle-target segment a bank face may lie and still be candidated, tiles.</summary>
    public const int BankSearchTiles = 12;

    public static IReadOnlyList<AimCandidate> For(WeaponId weapon, Vector2 muzzle, EnemyForecast target,
        CombatWorld world, IReadOnlyList<EnemyForecast> enemies, int fireTick, ref PlanningBudget budget)
    {
        int aimTick = Math.Max(1, fireTick);
        Vector2 toTarget = target.PredictedCentre(aimTick) - muzzle;
        if (weapon.IsSwing || toTarget == Vector2.Zero)
            return new[] { new AimCandidate(target.PredictedCentre(aimTick), toTarget == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(toTarget)) };
        var aims = new List<AimCandidate>();
        int repType = RepresentativeType(weapon);
        FlightLaw law = FitFlightLaws.LawFor(repType);
        AimCandidate intercept = Intercept(muzzle, toTarget, weapon.Speed, law, target, world, fireTick);
        aims.Add(intercept);
        if (!budget.Check()) return aims;
        foreach (AimCandidate spread in SpreadAims(weapon, muzzle, intercept))
        {
            if (aims.Count >= MaxAims) break;
            aims.Add(spread);
        }
        if (law.Wall.Kind == WallKind.Reflects)
        {
            foreach (AimCandidate bank in BankAims(muzzle, target, world, aimTick))
            {
                if (aims.Count >= MaxAims || !budget.Check()) break;
                aims.Add(bank);
            }
        }
        foreach (AimCandidate line in PierceLines(muzzle, intercept, target, enemies, aimTick))
        {
            if (aims.Count >= MaxAims || !budget.Check()) break;
            aims.Add(line);
        }
        return aims;
    }

    private static int RepresentativeType(WeaponId weapon)
    {
        VolleyShape shape = LearnVolleyShapes.ShapeFor(weapon.ItemType);
        if (!LearnVolleyShapes.HasShape(weapon.ItemType) || shape.Slots.Count == 0)
            return weapon.ProjectileFallback;
        var counts = new Dictionary<int, int>();
        foreach (VolleySlot slot in shape.Slots)
        {
            int type = slot.ProjectileType ?? weapon.ProjectileFallback;
            counts[type] = counts.TryGetValue(type, out int n) ? n + 1 : 1;
        }
        int best = weapon.ProjectileFallback, bestCount = 0;
        foreach ((int type, int count) in counts)
        {
            if (count > bestCount) { best = type; bestCount = count; }
        }
        return best;
    }

    /// <summary>
    /// The intercept: the launch direction whose flight passes closest to the target's predicted centre, found by
    /// sampling around the direct angle and refining twice. Law-agnostic — gravity bends the search's flights the
    /// way it bends the shot, and homing corrects inside them — so one search aims every term in the library.
    /// </summary>
    private static AimCandidate Intercept(Vector2 muzzle, Vector2 toTarget, float speed, FlightLaw law,
        EnemyForecast target, CombatWorld world, int fireTick)
    {
        float direct = toTarget.ToRotation();
        float step = InterceptSearchHalfAngle / 4f;
        float bestAngle = direct;
        int bestTick = Math.Max(1, fireTick);
        float bestMiss = float.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            for (int i = -4; i <= 4; i++)
            {
                float angle = (round == 0 ? direct : bestAngle) + i * step;
                (int tick, float miss) = FlyToTarget(muzzle, angle, speed, law, target, world, fireTick);
                if (miss < bestMiss - 1e-6f || (MathF.Abs(miss - bestMiss) <= 1e-6f && tick < bestTick))
                {
                    bestMiss = miss;
                    bestTick = tick;
                    bestAngle = angle;
                }
            }
            step /= 3f;
        }
        return new AimCandidate(target.PredictedCentre(bestTick), new Vector2(MathF.Cos(bestAngle), MathF.Sin(bestAngle)));
    }

    private static (int Tick, float Miss) FlyToTarget(Vector2 muzzle, float angle, float speed, FlightLaw law,
        EnemyForecast target, CombatWorld world, int fireTick)
    {
        ContentSamples.ProjectilesByType.TryGetValue(law.ProjectileType, out Projectile? sample);
        int width = sample?.width ?? 10, height = sample?.height ?? 10;
        Vector2 position = muzzle - new Vector2(width, height) / 2f;
        Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
        int update = 0, bounces = 0;
        int firstTick = Math.Max(1, fireTick);
        int bestTick = firstTick;
        float bestMiss = Vector2.Distance(muzzle, target.PredictedCentre(firstTick));
        for (int tick = firstTick; tick <= 180; tick++)
        {
            for (int sub = 0; sub < law.UpdatesPerTick; sub++, update++)
            {
                Vector2 centre = position + new Vector2(width, height) / 2f;
                velocity = FlightLaw.AIVelocity(in law, velocity, update,
                    target.PredictedCentre(tick) - centre, target.PredictedCentre(tick) - centre,
                    world.CompanionCentre - centre, wet: false);
                Vector2 collided = sample != null && !sample.tileCollide ? velocity
                    : Collision.TileCollision(position, velocity, width, height, false, false, 1);
                if (collided.X != velocity.X || collided.Y != velocity.Y)
                {
                    WallKind kind = LearnWallResponses.ResponseFor(law.ProjectileType).Kind;
                    if (kind == WallKind.Unknown)
                        kind = sample != null && !sample.tileCollide ? WallKind.Passes : WallKind.Dies;
                    if (kind != WallKind.Reflects && kind != WallKind.Passes)
                        return (bestTick, bestMiss);
                    if (kind == WallKind.Reflects)
                    {
                        WallResponse wall = LearnWallResponses.ResponseFor(law.ProjectileType);
                        if (collided.X != velocity.X) collided.X = velocity.X * wall.RestitutionNormal;
                        if (collided.Y != velocity.Y) collided.Y = velocity.Y * wall.RestitutionNormal;
                        collided *= wall.SpeedFactorAfterBounce;
                        if (++bounces > wall.BounceCount)
                            return (bestTick, bestMiss);
                    }
                    else
                    {
                        collided = velocity;
                    }
                }
                position += collided;
                velocity = collided;
            }
            float miss = Vector2.Distance(position + new Vector2(width, height) / 2f, target.PredictedCentre(tick));
            if (miss < bestMiss)
            {
                bestMiss = miss;
                bestTick = tick;
            }
        }
        return (bestTick, bestMiss);
    }

    private static IEnumerable<AimCandidate> SpreadAims(WeaponId weapon, Vector2 muzzle, AimCandidate intercept)
    {
        if (!LearnVolleyShapes.HasShape(weapon.ItemType)) yield break;
        float cone = 0f;
        foreach (VolleySlot slot in LearnVolleyShapes.ShapeFor(weapon.ItemType).Slots)
            cone = MathF.Max(cone, MathF.Abs(slot.AngleFromAimLine.Median()));
        if (cone < 0.01f) yield break;
        foreach (float fraction in new[] { -1f, 1f, -0.5f, 0.5f })
        {
            float offset = fraction * cone;
            yield return new AimCandidate(
                muzzle + (intercept.AimPoint - muzzle).RotatedBy(offset),
                intercept.LaunchDirection.RotatedBy(offset));
        }
    }

    /// <summary>
    /// The bank sweep on its own: aims off wall faces near the muzzle-target midpoint, for the
    /// planner's BankShots generator, which asks from stands the intercept search never stands on.
    /// Unsimulated, like every other aim this class offers: the caller flies each and keeps the ones
    /// that land.
    /// </summary>
    public static IEnumerable<AimCandidate> BankAims(Vector2 muzzle, EnemyForecast target, CombatWorld world, int aimTick)
    {
        Vector2 centre = target.PredictedCentre(aimTick);
        Vector2 mid = (muzzle + centre) / 2f;
        int yielded = 0;
        int x0 = Math.Max(0, (int)(mid.X / 16f) - BankSearchTiles);
        int x1 = Math.Min(Main.maxTilesX - 1, (int)(mid.X / 16f) + BankSearchTiles);
        int y0 = Math.Max(0, (int)(mid.Y / 16f) - BankSearchTiles);
        int y1 = Math.Min(Main.maxTilesY - 1, (int)(mid.Y / 16f) + BankSearchTiles);
        for (int x = x0; x <= x1 && yielded < 6; x += 2)
        {
            for (int y = y0; y <= y1 && yielded < 6; y += 2)
            {
                Tile tile = Main.tile[x, y];
                if (tile == null || !tile.HasTile || !Main.tileSolid[tile.TileType]) continue;
                bool faceX = x > 0 && x < Main.maxTilesX - 1
                    && (!Main.tile[x - 1, y].HasTile || !Main.tile[x + 1, y].HasTile);
                bool faceY = y > 0 && y < Main.maxTilesY - 1
                    && (!Main.tile[x, y - 1].HasTile || !Main.tile[x, y + 1].HasTile);
                if (!faceX && !faceY) continue;
                Vector2 face = new(x * 16f + 8f, y * 16f + 8f);
                // Fire at the mirror image: a perfect reflection off this face reaches the target, and the
                // simulator — which flies the learned restitution, not a perfect one — says whether it does.
                Vector2 mirror = faceX && !faceY ? new Vector2(2f * face.X - centre.X, centre.Y)
                    : !faceX && faceY ? new Vector2(centre.X, 2f * face.Y - centre.Y)
                    : 2f * face - centre;
                Vector2 launch = mirror - muzzle;
                if (launch == Vector2.Zero) continue;
                yielded++;
                yield return new AimCandidate(mirror, Vector2.Normalize(launch));
            }
        }
    }

    private static IEnumerable<AimCandidate> PierceLines(Vector2 muzzle, AimCandidate intercept, EnemyForecast target,
        IReadOnlyList<EnemyForecast> enemies, int aimTick)
    {
        float direct = intercept.LaunchDirection.ToRotation();
        int yielded = 0;
        foreach (EnemyForecast other in enemies)
        {
            if (yielded >= 3 || other.Slot == target.Slot) continue;
            Vector2 toOther = other.PredictedCentre(aimTick) - muzzle;
            if (toOther == Vector2.Zero) continue;
            float off = MathF.Abs(MathHelper.WrapAngle(toOther.ToRotation() - direct));
            if (off > 0.18f) continue;
            // Aim at the farther body along the near line, so the flight crosses both.
            Vector2 aim = toOther.LengthSquared() > (intercept.AimPoint - muzzle).LengthSquared()
                ? other.PredictedCentre(aimTick) : intercept.AimPoint;
            Vector2 launch = aim - muzzle;
            if (launch == Vector2.Zero) continue;
            yielded++;
            yield return new AimCandidate(aim, Vector2.Normalize(launch));
        }
    }
}
