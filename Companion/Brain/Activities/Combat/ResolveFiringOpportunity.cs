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

    private readonly Dictionary<(int slot, int generation), (int at, Point origin, int terrain, Vector2 target, FiringAccess verdict, float access, float value)> cache = new();

    /// <summary>
    /// How far a target may have moved before an answer about it stops being an answer. It is the positioner's own
    /// firing-hold slack, read rather than copied, because the two are the same question — whether the thing is
    /// still where the verdict was taken about it — and a second number here would drift from that one silently.
    /// </summary>
    private static float TargetSlackPx => Infrastructure.Selection.Weights.FiringHoldTargetSlackPx;

    private static bool StillAbout(Vector2 remembered, NPC enemy)
        => Vector2.DistanceSquared(remembered, enemy.Center) <= TargetSlackPx * TargetSlackPx;

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
        Point feet = Infrastructure.Movement.MovementQueries.Tile(ctx.Npc.Center);
        var origin = new Point(feet.X >> 2, feet.Y >> 2);
        int terrain = Infrastructure.Movement.TerrainChanges.Revision;
        // Where the target stood when the answer was taken is part of the key, for the same reason the body's own
        // position is. The verdict is about a line between two places, so it goes stale when either end moves; the
        // key carried only the companion's end, so a target that walked out from behind its rock kept the answer
        // taken while it was behind it for the whole of the tick window.
        if (cache.TryGetValue(key, out var cached) && cached.origin == origin && cached.terrain == terrain
            && StillAbout(cached.target, enemy)
            && unchecked(ctx.Senses.Tick - cached.at) < FiringCacheTicks)
        {
            LastShotValue = cached.value;
            return (cached.verdict, cached.access);
        }

        var answer = Scan(ctx, enemy);
        cache[key] = (ctx.Senses.Tick, origin, terrain, enemy.Center, answer.Verdict, answer.AccessTicks, answer.Value);
        LastShotValue = answer.Value;
        return (answer.Verdict, answer.AccessTicks);
    }

    /// <summary>
    /// The whole sample is scanned rather than stopping at the first reachable tile, because a reposition
    /// is priced by its duration and scan order says nothing about that.
    /// </summary>
    private const int MaxStandSolvesPerTarget = 8;

    /// <summary>
    /// Where the next scan of a target's stands starts. The sample is sorted by distance and only a handful are
    /// solved, so a scan that always began at the nearest re-asked the same eight stands for ever: a target whose
    /// eight nearest stands are blocked and whose ninth works could never be found, and calling that a proven
    /// absence was the same exhausted-bound-as-negative mistake the positioner makes one layer up. The cursor
    /// advances a scan's worth each time the answer stays unsettled and wraps, so a sweep completes across
    /// rescores — and only a completed sweep with nothing solved earns <see cref="FiringAccess.None"/>.
    /// </summary>
    /// <summary>
    /// The sweep's progress per target, scoped to where that target was standing while the progress was made.
    ///
    /// <para>The stands are rebuilt around the enemy's own feet on every scan, so a target that has walked is a
    /// different set of stands — while the examined count accumulated across scans with nothing tying it to the
    /// target's position. So the count could reach the set's size having asked only about stands around places the
    /// enemy has left, and the answer was <see cref="FiringAccess.None"/>: a proven absence of any firing position,
    /// which protection reads as a threat it cannot shoot and hunting as a target not worth approaching. That is the
    /// same exhausted-bound-reported-as-a-fact defect this class already closed one way, surviving through the
    /// target's motion instead of through the budget. The positioner's own refusal memory scopes on the target's
    /// centre for exactly this; this sibling did not.</para>
    /// </summary>
    private readonly Dictionary<(int slot, int generation), (int cursor, int terrain, Vector2 target, int examined)> sweep = new();

    private (FiringAccess Verdict, float AccessTicks, float Value) Scan(in ActionContext ctx, NPC enemy)
    {
        var arsenal = ctx.Companion.Arsenal;
        Vector2 here = Arsenal.Muzzle(ctx.Npc);
        if (arsenal.ShotSolves(ctx, here, enemy))
            return (FiringAccess.FromHere, 0f, arsenal.BestShotValueFrom(ctx, here, enemy));

        Point feet = Infrastructure.Movement.MovementQueries.Tile(ctx.Npc.Center);
        var positioner = ctx.Companion.Brain.Positioner;
        float reach = arsenal.MaxReach;
        int radius = Math.Min(FiringSampleRadiusTiles, (int)(reach / 16f));
        Point centre = Infrastructure.Movement.MovementQueries.Tile(enemy.Center);
        var stands = new List<(float Distance, Point Tile, Vector2 Eye)>();
        for (int dx = -radius; dx <= radius; dx += FiringSampleStride)
        {
            for (int dy = -radius; dy <= radius; dy += FiringSampleStride)
            {
                var tile = new Point(centre.X + dx, centre.Y + dy);
                if (!Infrastructure.Movement.MovementQueries.IsHoverable(tile))
                    continue;
                // The arsenal's muzzle is expressed from a feet point; the orb's feet are its centre plus its radius.
                Vector2 hover = Infrastructure.Movement.MovementQueries.HoverPoint(tile);
                Vector2 eye = Arsenal.MuzzleAt(hover);
                if (Vector2.Distance(eye, enemy.Center) > reach)
                    continue;
                stands.Add((Vector2.DistanceSquared(ctx.Npc.Center, hover), tile, eye));
            }
        }
        stands.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        float nearestReachable = float.PositiveInfinity;
        float bestValue = 0f;
        bool solvedUnreachable = false;
        int solves = 0;
        int terrain = Infrastructure.Movement.TerrainChanges.Revision;
        var sweepKey = (enemy.whoAmI, HostileAttackSources.Generation(enemy));
        bool resumed = sweep.TryGetValue(sweepKey, out var mark) && mark.terrain == terrain
            && StillAbout(mark.target, enemy);
        int start = resumed && mark.cursor < stands.Count ? mark.cursor : 0;
        int examined = resumed ? mark.examined : 0;
        // The anchor is where the target stood when this sweep began, carried forward unchanged, never where it
        // stands on the scan writing the mark. Re-anchoring each scan would let a walking enemy drift as far as it
        // liked in slack-sized steps while every step stayed inside the slack, and the sweep would report having
        // asked about stands around a place the enemy left long ago — which is the defect, reassembled out of
        // several small moves instead of one large one.
        Vector2 anchor = resumed ? mark.target : enemy.Center;
        if (!resumed) { start = 0; examined = 0; }
        for (int offset = 0; offset < stands.Count && solves < MaxStandSolvesPerTarget; offset++)
        {
            int index = (start + offset) % stands.Count;
            var stand = stands[index];
            solves++;
            examined++;
            sweep[sweepKey] = ((index + 1) % stands.Count, terrain, anchor, examined);
            if (!arsenal.ShotSolves(ctx, stand.Eye, enemy)) continue;
            float value = arsenal.BestShotValueFrom(ctx, stand.Eye, enemy);
            if (value > bestValue) bestValue = value;
            if (positioner.Reaches(stand.Tile))
            {
                float ticks = positioner.EstimatedTravelTicks(feet, stand.Tile)
                    ?? Vector2.Distance(ctx.Npc.Center, Infrastructure.Movement.MovementQueries.HoverPoint(stand.Tile)) / Infrastructure.Movement.OrbPace.MaxSpeed;
                nearestReachable = MathF.Min(nearestReachable, ticks);
            }
            // A stand the flood has not claimed is only a refusal when the sense has proven it absent; beyond
            // the sense's known radius, or while the flood is unfinished, it is a stand hunt may still walk toward.
            else if (!positioner.ProvenUnreachableTile(stand.Tile))
                solvedUnreachable = true;
        }

        if (float.IsFinite(nearestReachable))
        {
            sweep.Remove(sweepKey);
            return (FiringAccess.AfterMoving, nearestReachable, bestValue);
        }
        // A real arc exists at a stand the flood has not yet claimed: hunt may start walking toward
        // that region without freezing the tile. A sealed chamber with no arc stays Unknown or None.
        if (solvedUnreachable)
        {
            sweep.Remove(sweepKey);
            return (FiringAccess.AfterMoving, Vector2.Distance(ctx.Npc.Center, enemy.Center) / Infrastructure.Movement.OrbPace.MaxSpeed, bestValue);
        }
        if (!positioner.ReachComplete)
            return (FiringAccess.Unknown, Vector2.Distance(ctx.Npc.Center, enemy.Center) / Infrastructure.Movement.OrbPace.MaxSpeed, 0f);
        // Only a completed sweep of every sampled stand is a proven absence. Stopping at the solve cap and calling
        // it None is an exhausted bound reported as a fact about the world, which is what put hunt at KnownUnusable
        // on 303 of the 13:27 capture's 624 decisions and let keeping company take the body by default.
        if (examined < stands.Count)
            return (FiringAccess.Unknown, Vector2.Distance(ctx.Npc.Center, enemy.Center) / Infrastructure.Movement.OrbPace.MaxSpeed, 0f);
        return (FiringAccess.None, float.PositiveInfinity, 0f);
    }
}
