#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Weapons;

namespace AICompanion.Companion.Brain.Activities.Combat;

/// <summary>
/// What the companion would have to do to land a shot on an enemy. Three-and-a-bit valued for
/// the same reason <see cref="Infrastructure.Movement.Reachability.Reach"/> is: a bounded flood
/// that has not yet grown as far as the target says nothing about whether a firing spot exists
/// there. Unknown is an unfinished search that has not found a solvable stand. A solvable stand
/// is AfterMoving even while the path is unfinished, so hunt can start walking toward that region.
/// Collapsing Unknown into None would still be wrong, because a later tick can settle a shot that
/// this tick could not finish asking.
/// </summary>
public enum FiringAccess
{
    /// <summary>A weapon already solves a shot from where the companion stands.</summary>
    FromHere,
    /// <summary>Somewhere the walker can reach has a line to the enemy; the access is the walk to it.</summary>
    AfterMoving,
    /// <summary>Sighted standing spots exist but the reachable region has not settled, so nothing is established.</summary>
    Unknown,
    /// <summary>No standing position with a line to the enemy exists within weapon reach at all.</summary>
    None,
}

/// <summary>
/// Whether a standing position exists that the walker can reach and from which a weapon has a line to an
/// enemy, and how long reaching it takes — the owner's rule, in his words: can I hit it, if not can I move
/// to hit it, and if neither then it is not worth going for. Hunting and guarding ask the same question
/// about the same enemy from the same body, so one instance per companion answers both through one cache:
/// the chooser builds it and hands it to both activities, and whichever prepares second on a tick reads
/// the first one's answer instead of scanning the terrain again. It is per companion rather than static
/// because its key is an NPC slot and spawn generation, which a second companion or a rebuilt world reuses.
///
/// Existence is the arsenal's real forecast — the same <c>ForecastAttack</c> the hands fire from —
/// not a straight ray. A ray can say yes while the bow hits the floor, which is how hunt stood still
/// on a retained stand with <c>no-clear-trajectory</c>. Hunt does not pick the pair the hands will
/// fire; it only asks whether a pose would give the chooser something to rank, and which nearby pose
/// ranks highest. A handful of nearest stands are solved per target so a crowd cannot spend the tick.
/// </summary>
public sealed class ResolveFiringOpportunity
{
    private const int FiringCacheTicks = 20;
    private const int FiringSampleRadiusTiles = 14;
    // Every other tile. A standable row is one tile high, so a stride this coarse can miss a narrow
    // ledge; that costs an Unknown or a missed opportunity, never a wrong refusal, because a missed
    // sighted tile can only make the answer more cautious.
    private const int FiringSampleStride = 2;

    private readonly Dictionary<(int slot, int generation), (int at, Point origin, int terrain, FiringAccess verdict, float access, float value)> cache = new();

    /// <summary>Arsenal outcome value at the best solved pose of the last <see cref="Resolve"/>.</summary>
    public float LastShotValue { get; private set; }

    /// <summary>
    /// The verdict, and how long the reposition it implies would take: zero from here, the travel estimate
    /// to the nearest reachable sighted tile after moving, a straight-line walk to the enemy while nothing is
    /// established, and infinity for a proven absence. The straight-line figure under Unknown is not a hunt
    /// price; hunting skips Unknown rather than walking at a guess.
    /// </summary>
    public (FiringAccess Verdict, float AccessTicks) Resolve(in ActionContext ctx, NPC enemy)
    {
        var key = (enemy.whoAmI, HostileAttackSources.Generation(enemy));
        // Coarse, because the exact feet tile changes on almost every tick the companion is walking
        // and an exact key would therefore miss continuously — re-sampling hundreds of tiles every
        // tick of every approach, which is how a previous addition on this path made a session
        // unplayable. Whether some reachable position can shoot an enemy does not change from one
        // tile of travel, so the key moves in strides and the tick window bounds the staleness.
        Point feet = Infrastructure.Movement.MovementQueries.FeetTile(ctx.Npc.Bottom);
        var origin = new Point(feet.X >> 2, feet.Y >> 2);
        int terrain = Infrastructure.Movement.TerrainChanges.Revision;
        if (cache.TryGetValue(key, out var cached) && cached.origin == origin && cached.terrain == terrain
            && unchecked(ctx.Senses.Tick - cached.at) < FiringCacheTicks)
        {
            LastShotValue = cached.value;
            return (cached.verdict, cached.access);
        }

        var answer = Scan(ctx, enemy);
        cache[key] = (ctx.Senses.Tick, origin, terrain, answer.Verdict, answer.AccessTicks, answer.Value);
        LastShotValue = answer.Value;
        return (answer.Verdict, answer.AccessTicks);
    }

    /// <summary>
    /// The whole sample is scanned rather than stopping at the first reachable tile, because a reposition
    /// is priced by its duration and scan order says nothing about that.
    /// </summary>
    private const int MaxStandSolvesPerTarget = 8;

    private static (FiringAccess Verdict, float AccessTicks, float Value) Scan(in ActionContext ctx, NPC enemy)
    {
        var arsenal = ctx.Companion.Arsenal;
        Vector2 here = Arsenal.Muzzle(ctx.Npc);
        if (arsenal.ShotSolves(ctx, here, enemy))
            return (FiringAccess.FromHere, 0f, arsenal.BestShotValueFrom(ctx, here, enemy));

        Point feet = Infrastructure.Movement.MovementQueries.FeetTile(ctx.Npc.Bottom);
        var positioner = ctx.Companion.Brain.Positioner;
        float reach = MathF.Max(arsenal.Primary.Profile.Reach, arsenal.Secondary.Profile.Reach);
        int radius = Math.Min(FiringSampleRadiusTiles, (int)(reach / 16f));
        Point centre = Infrastructure.Movement.MovementQueries.FeetTile(enemy.Bottom);
        var stands = new List<(float Distance, Point Tile, Vector2 Eye)>();
        for (int dx = -radius; dx <= radius; dx += FiringSampleStride)
        {
            for (int dy = -radius; dy <= radius; dy += FiringSampleStride)
            {
                var tile = new Point(centre.X + dx, centre.Y + dy);
                if (!Infrastructure.Movement.MovementQueries.IsStandable(tile.X, tile.Y))
                    continue;
                Vector2 eye = Arsenal.MuzzleAtFeet(Infrastructure.Movement.MovementQueries.FeetWorld(tile));
                if (Vector2.Distance(eye, enemy.Center) > reach)
                    continue;
                stands.Add((Vector2.DistanceSquared(ctx.Npc.Bottom, Infrastructure.Movement.MovementQueries.FeetWorld(tile)), tile, eye));
            }
        }
        stands.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        float nearestReachable = float.PositiveInfinity;
        float bestValue = 0f;
        bool solvedUnreachable = false;
        int solves = 0;
        foreach (var stand in stands)
        {
            if (solves >= MaxStandSolvesPerTarget) break;
            solves++;
            if (!arsenal.ShotSolves(ctx, stand.Eye, enemy)) continue;
            float value = arsenal.BestShotValueFrom(ctx, stand.Eye, enemy);
            if (value > bestValue) bestValue = value;
            if (positioner.Reaches(stand.Tile))
            {
                float ticks = positioner.EstimatedTravelTicks(feet, stand.Tile)
                    ?? Vector2.Distance(ctx.Npc.Bottom, Infrastructure.Movement.MovementQueries.FeetWorld(stand.Tile)) / Companion.CompanionMotor.WalkSpeed;
                nearestReachable = MathF.Min(nearestReachable, ticks);
            }
            else if (!positioner.ReachComplete)
                solvedUnreachable = true;
        }

        if (float.IsFinite(nearestReachable))
            return (FiringAccess.AfterMoving, nearestReachable, bestValue);
        // A real arc exists at a stand the flood has not yet claimed: hunt may start walking toward
        // that region without freezing the tile. A sealed chamber with no arc stays Unknown or None.
        if (solvedUnreachable)
            return (FiringAccess.AfterMoving, Vector2.Distance(ctx.Npc.Bottom, enemy.Bottom) / Companion.CompanionMotor.WalkSpeed, bestValue);
        if (!positioner.ReachComplete)
            return (FiringAccess.Unknown, Vector2.Distance(ctx.Npc.Bottom, enemy.Bottom) / Companion.CompanionMotor.WalkSpeed, 0f);
        return (FiringAccess.None, float.PositiveInfinity, 0f);
    }
}
