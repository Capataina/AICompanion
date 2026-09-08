#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A drop off a lip: the body lowered down the open span beside it, against either wall or down
/// the middle, to the first surface that holds it, each distinct landing its own edge carrying
/// the line the body fell along. Performing it is steering to that line at reduced speed and
/// letting gravity work. The landing is found by lowering the body a row at a time with a fixed
/// drift toward the wall it hugs, which is the analytical scan the planner has used since run 5
/// (2026-09-08); the traversal work replaces it with the body's own tick rule so the line the
/// step carries is the one the follower steers to from the lip and not the one the body ended
/// on after drifting, which is the parked fall-through of run 7 (AIC-180).
/// </summary>
public sealed class DropTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.Drop;

    /// <summary>
    /// A falling body pressed toward a wall moves this far sideways per row of fall, at most:
    /// under NPC gravity the body clears a row in a couple of ticks near its top speed and in
    /// many at the start, and the motor moves it a few pixels a tick, so this is the fast end.
    /// </summary>
    public const float DriftPerRow = 6f;

    public override IEnumerable<NavEdge> Candidates(Point t, BodyPhysics.Pose? here, bool lava)
    {
        foreach (int dir in new[] { -1, 1 })
        {
            int nx = t.X + dir;
            // Edge: step off the lip and fall to the first surface that holds the body where the
            // follower steers it. The body is put there and asked what it rests on, row by row,
            // rather than the grid being asked whether a tile is standable: a tile is standable by
            // a two-pixel overhang, which the body in the open span is not on, and a platform one
            // row down is not a block, which is how a real support one row down was scanned past
            // and a drop offered that the body at the lip never made (run 5, 2026-09-08). A rest
            // one row down is the walk step-down's job and yields no edge; two or more rows is the
            // drop. The column beside the lip must be open underneath as well as clear: a platform
            // there is a floor the body walks onto and stands on, and the way down through it is
            // the fall-through edge from that tile, never a drop from this one (run 6, 2026-09-08).
            if (!NavGrid.IsBodyClear(nx, t.Y) || NavGrid.IsSupport(nx, t.Y + 1))
                continue;
            foreach ((Point drop, int fall, float steerX) in Landings(nx, t.Y, throughPlatform: false, lava))
                if (fall >= 2)
                    yield return new NavEdge(new NavStep(drop, MoveKind.Drop, t, SteerX: steerX, Ticks: FallTicks(fall)), 1f + fall * 0.2f, fall, true);
        }
    }

    /// <summary>
    /// Every place a body stepping off the lip of feet tile (<paramref name="column"/>, <paramref name="feetRow"/>)
    /// can come to rest, each with how many rows it fell and the X its centre fell along. The
    /// open span beside the lip is wider than the body in most shafts, and what the body lands on
    /// depends on where in it the body falls: hugging one wall it rests on a lip that the other
    /// wall's side falls past, which is how a zigzag shaft is descended a lip at a time. So the
    /// body is dropped against the span's left wall, down its middle and against its right wall,
    /// and each distinct landing is an edge whose step tells the follower where to steer. Nothing
    /// is yielded for a way down the body cannot pass or a fall past the limit.
    /// </summary>
    internal static IEnumerable<(Point tile, int fall, float steerX)> Landings(int column, int feetRow, bool throughPlatform, bool lava)
    {
        (int spanLeft, int spanRight) = NavGrid.OpenSpan(column, feetRow, throughPlatform);
        float hugLeft = spanLeft * 16f + 1f;
        float hugRight = (spanRight + 1) * 16f - BodyPhysics.Width - 1f;
        float centre = (spanLeft * 16f + (spanRight + 1) * 16f) / 2f - BodyPhysics.Width / 2f;
        var seen = new HashSet<Point>();
        foreach ((float left, int drift) in new[] { (centre, 0), (hugLeft, -1), (hugRight, 1) })
        {
            if (Landing(left, feetRow, drift, lava) is (Point tile, int fall, float restLeft) && seen.Add(tile))
                yield return (tile, fall, restLeft + BodyPhysics.Width / 2f);
        }
    }

    /// <summary>
    /// Where a body whose left edge is <paramref name="left"/>, feet in <paramref name="feetRow"/>,
    /// comes to rest when lowered a row at a time, how many rows it fell and where its left edge
    /// ended up: the first row whose surface holds it there is the landing, a row it does not fit
    /// in is a ceiling it cannot pass, and null is either of those failing inside the drop limit.
    /// With <paramref name="drift"/> set the body presses that way as it falls, sliding across each
    /// row as far as it fits, which is how it follows a shaft's wall into a wider segment and rests
    /// on a lip the wall above stood over. The landing must also be a node with lava as the
    /// caller allows it, so the path can continue from it.
    /// </summary>
    private static (Point, int, float)? Landing(float left, int feetRow, int drift, bool lava)
    {
        ITileWorld world = NavGrid.World;
        if (!BodyPhysics.Fits(world, left, (feetRow + 1) * 16f))
            return null;
        for (int dy = 1; dy <= NavGrid.MaxDropTiles; dy++)
        {
            int row = feetRow + dy;
            float floor = (row + 1) * 16f;
            if (drift != 0)
            {
                for (float slid = left + drift * DriftPerRow; drift * (slid - left) > 0f; slid -= drift * 2f)
                {
                    if (BodyPhysics.Fits(world, slid, floor))
                    {
                        left = slid;
                        break;
                    }
                }
            }
            if (BodyPhysics.RestBottom(world, left, row) is float rest && BodyPhysics.FeetRow(rest) == row && BodyPhysics.Fits(world, left, rest))
            {
                // The node the resting body is filed under: the column of its centre when that
                // tile is a node, else either column it covers, because a body two pixels over a
                // lip rests on the lip's tile and stands there in the game whichever column its
                // centre is in.
                int centreColumn = (int)Math.Floor((left + BodyPhysics.Width / 2f) / 16f);
                int leftColumn = (int)Math.Floor(left / 16f), rightColumn = (int)Math.Floor((left + BodyPhysics.Width - 0.02f) / 16f);
                foreach (int node in new[] { centreColumn, leftColumn, rightColumn })
                    if (NavGrid.StandAt(node, row, lava) != null)
                        return (new Point(node, row), dy, left);
                return null;
            }
            if (!BodyPhysics.Fits(world, left, floor))
                return null;
        }
        return null;
    }

    /// <summary>The X a descending step falls along: the plan's own steer point, or the middle of the opening for a path made without one.</summary>
    internal static float SteerX(NavStep step, BodyState live, bool throughPlatform)
        => step.SteerX > 0f ? step.SteerX : NavGrid.OpenSpanCentreX(step.Tile.X, live.FeetTile.Y, throughPlatform);

    private bool airborne;
    private int lastDir = 1;

    public override void Begin(NavStep step) => airborne = false;

    public override Controls Steer(BodyState live, NavStep step)
    {
        airborne |= !live.OnGround;
        // Steer to the line the planner dropped the body along (a wall of the shaft or its
        // middle, whichever lands on this step's tile), not the tile: centred on one column
        // of a two-wide shaft the body still overhangs the lip and never falls, and centred
        // in a three-wide one it falls past the lip that only a wall-hugging body lands on.
        float gap = SteerX(step, live, throughPlatform: false) - live.CentreX;
        int toGap = MathF.Sign(gap) == 0 ? lastDir : MathF.Sign(gap);
        lastDir = toGap;
        return new Controls(live.OnGround || MathF.Abs(gap) > 2f ? toGap * BodyPhysics.WalkSpeed * 0.8f : 0f);
    }

    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if (airborne && LandedElsewhere(live, step))
            return TraversalFault.Misland;
        return base.Check(live, step, ticksOnStep);
    }
}
