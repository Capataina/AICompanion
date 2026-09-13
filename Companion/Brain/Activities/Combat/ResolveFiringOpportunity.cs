#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.Combat;

/// <summary>
/// What the companion would have to do to land a shot on an enemy. Three-and-a-bit valued for
/// the same reason <see cref="Infrastructure.Movement.Reachability.Reach"/> is: a bounded flood
/// that has not yet grown as far as the target says nothing about whether a firing spot exists
/// there. Hunting does not walk at that Unknown; it waits until the flood proves FromHere or
/// AfterMoving. Collapsing Unknown into None would still be wrong, because a later tick can
/// settle a shot that this tick could not finish asking.
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
/// The sight test is the same straight ray the positioner ranks candidates with, not a full trajectory
/// solve, because this runs per target and a solve costs up to 48 arcs. It is a lower bound on an arcing
/// shot, so it under-reports opportunities and never invents one; the real solve still happens in
/// position selection, on the spot this admits.
///
/// Applying the from-here test alone would have been the obvious rule and the wrong one: it rejects every
/// enemy the companion would simply have had to walk toward, which is most of them.
/// </summary>
public sealed class ResolveFiringOpportunity
{
    private const int FiringCacheTicks = 20;
    private const int FiringSampleRadiusTiles = 14;
    // Every other tile. A standable row is one tile high, so a stride this coarse can miss a narrow
    // ledge; that costs an Unknown or a missed opportunity, never a wrong refusal, because a missed
    // sighted tile can only make the answer more cautious.
    private const int FiringSampleStride = 2;

    private readonly Dictionary<(int slot, int generation), (int at, Point origin, int terrain, FiringAccess verdict, float access)> cache = new();

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
            return (cached.verdict, cached.access);

        var answer = Scan(ctx, enemy);
        cache[key] = (ctx.Senses.Tick, origin, terrain, answer.Verdict, answer.AccessTicks);
        return answer;
    }

    /// <summary>
    /// The whole sample is scanned rather than stopping at the first reachable tile, because a reposition
    /// is priced by its duration and scan order says nothing about that.
    /// </summary>
    private static (FiringAccess Verdict, float AccessTicks) Scan(in ActionContext ctx, NPC enemy)
    {
        if (ctx.Companion.Arsenal.CanEngage(ctx, enemy))
            return (FiringAccess.FromHere, 0f);
        Point feet = Infrastructure.Movement.MovementQueries.FeetTile(ctx.Npc.Bottom);
        float nearest = float.PositiveInfinity;
        var positioner = ctx.Companion.Brain.Positioner;
        float reach = MathF.Max(ctx.Companion.Arsenal.Primary.Profile.Reach, ctx.Companion.Arsenal.Secondary.Profile.Reach);
        // Never wider than the box position selection itself samples: a spot it cannot propose is
        // not a spot the companion can be walked to, so admitting a target on one would promise a
        // position that never arrives.
        int radius = Math.Min(FiringSampleRadiusTiles, (int)(reach / 16f));
        Point centre = Infrastructure.Movement.MovementQueries.FeetTile(enemy.Bottom);
        bool sightedButUnsettled = false;
        for (int dx = -radius; dx <= radius; dx += FiringSampleStride)
        {
            for (int dy = -radius; dy <= radius; dy += FiringSampleStride)
            {
                var tile = new Point(centre.X + dx, centre.Y + dy);
                if (!Infrastructure.Movement.MovementQueries.IsStandable(tile.X, tile.Y))
                    continue;
                Vector2 eye = Infrastructure.Movement.MovementQueries.FeetWorld(tile) + new Vector2(0f, -30f);
                if (Vector2.Distance(eye, enemy.Center) > reach)
                    continue;
                if (!Terraria.Collision.CanHitLine(eye, 1, 1, enemy.position, enemy.width, enemy.height))
                    continue;
                if (positioner.Reaches(tile))
                {
                    float ticks = positioner.EstimatedTravelTicks(feet, tile)
                        ?? Vector2.Distance(ctx.Npc.Bottom, Infrastructure.Movement.MovementQueries.FeetWorld(tile)) / Companion.CompanionMotor.WalkSpeed;
                    nearest = MathF.Min(nearest, ticks);
                    continue;
                }
                // Sighted, and the flood has not proved it either way. Only an exhausted region
                // turns that into an absence; until then it is the search declining to answer.
                if (!positioner.ReachComplete)
                    sightedButUnsettled = true;
            }
        }
        if (float.IsFinite(nearest)) return (FiringAccess.AfterMoving, nearest);
        return sightedButUnsettled
            ? (FiringAccess.Unknown, Vector2.Distance(ctx.Npc.Bottom, enemy.Bottom) / Companion.CompanionMotor.WalkSpeed)
            : (FiringAccess.None, float.PositiveInfinity);
    }
}
