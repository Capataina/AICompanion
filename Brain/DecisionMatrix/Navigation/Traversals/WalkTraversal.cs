#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A walk to the next column, proven the way every other move is: the body driven from the
/// node's pose at the walk speed through its own tick rule until its centre crosses the next
/// column's, and the tile its feet are then in is the edge, the same row, one up (the kerb
/// rule lifted it, over a block or onto a platform at its knee) or one down (the kerb rule
/// stepped it down, or a slope lowered it). A body that meets a shape on the way, falls for
/// real, or ends off a node has no walk that way, which is what refuses the end of a platform
/// tread where the body is still held by the tread's last pixels and its head meets rock; the
/// slide test that proved walks before (a body that fits along the line between two poses)
/// accepted that walk and the body parked at it (run 5 block 12, 2026-09-08). Performing it is
/// walking toward the step's feet point. The one move this follower still derives on its own is
/// a jump at a real wall or a two-tile rise, which a walk edge never contains and only the
/// first step of a path can meet, when the plan began at the nearest node to a body that was
/// not on one; the follow harness is where that residue is measured before it is removed.
/// </summary>
public sealed class WalkTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.Walk;

    /// <summary>The longest a walk to the next column is followed: from rest, the body crosses a tile in about twenty ticks.</summary>
    private const int MaxTicks = 60;

    /// <summary>A walk from a tile with no pose, which only the start of a plan can be, takes a few ticks; it is offered untested, as the way onto the grid.</summary>
    private const int UntestedTicks = 6;

    private int blockedTicks;
    private int lastDir = 1;

    public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava)
    {
        Point t = node.Tile;
        foreach (int dir in new[] { -1, 1 })
        {
            if (here is BodyPhysics.Pose pose)
            {
                // Proven from rest, and checked at the walk speed: a walk the body makes the same
                // way however fast it arrives is a plain walk, and one whose landing changes with
                // speed (a ledge onto a lip that a body at speed flies over) starts from rest, so
                // the step before it coasts to a stop. The follow harness on the fourth run's
                // pocket (2026-09-08) flew a body at the walk speed off a one-row ledge, over
                // the two lip tiles the walk had been proven onto from rest, and into the pool
                // beyond them.
                if (Simulate(pose, 0f, dir, t, lava) is not (Point landing, int ticks))
                    continue;
                bool fromRest = Simulate(pose, dir * BodyPhysics.WalkSpeed, dir, t, lava) is not (Point atSpeed, _) || atSpeed.Y != landing.Y;
                int dy = landing.Y - t.Y;
                // A slope or a short ledge lowers the feet a row without a real fall, so the
                // step down is a walk like the others and not a drop; a step up costs its kerb.
                float cost = (landing.X - t.X) * dir + dy switch { 0 => 0f, < 0 => 0.5f, _ => 0.2f };
                yield return new NavEdge(new NavStep(landing, MoveKind.Walk, t, Ticks: ticks, FromRest: fromRest), cost, 0, false);
                continue;
            }
            // A body standing where no pose exists (the plan's start, from a body between
            // nodes) still needs a way onto the grid, so its walks are offered on the
            // neighbouring poses alone.
            foreach ((int dy, float cost) in new[] { (0, 1f), (-1, 1.5f), (1, 1.2f) })
                if (NavGrid.StandAt(t.X + dir, t.Y + dy, lava) != null)
                    yield return new NavEdge(new NavStep(new Point(t.X + dir, t.Y + dy), MoveKind.Walk, t, Ticks: UntestedTicks), cost, 0, false);
        }
    }

    /// <summary>
    /// Where the walk that way lands and how long it takes, or null: the body at the pose with
    /// <paramref name="startVx"/>, driven at the walk speed until it stands past the next
    /// column's centre, with any time in the air ridden out (the hop the kerb rule leaves to
    /// gravity, and the fall off a ledge less than a tile and more than the kerb rule's window,
    /// which is a walk down as the body makes it: a moment in the air and on again), and the
    /// tile it then stands in must be a node one row at most from the start's, as many columns
    /// over as the fall carried it. A shape met sideways, a body lost in a shape, or a landing
    /// two rows down or more (the drop's edge) ends it.
    /// </summary>
    private static (Point landing, int ticks)? Simulate(BodyPhysics.Pose pose, float startVx, int dir, Point t, bool lava)
    {
        ITileWorld world = NavGrid.World;
        BodyState state = BodyState.Standing(pose) with { Vx = startVx };
        var controls = new Controls(dir * BodyPhysics.WalkSpeed);
        for (int ticks = 1; ticks <= MaxTicks; ticks++)
        {
            state = BodyMotion.Step(world, state, controls);
            if (state.Stuck || state.CollideX)
                return null;
            // Standing with the centre anywhere in the next column is the walk made: a node's
            // pose sits off the column's centre where rock touches one side of it, and a body
            // driven to the centre itself meets that rock two pixels early; the follower's
            // slack covers the rest of the way to the pose.
            if (!state.OnGround || dir * (state.FeetTile.X - (t.X + dir)) < 0)
                continue;
            Point landing = state.FeetTile;
            int dx = (landing.X - t.X) * dir, dy = landing.Y - t.Y;
            if (dx < 1 || Math.Abs(dy) > 1 || NavGrid.StandAt(landing.X, landing.Y, lava) == null)
                return null;
            return (landing, ticks);
        }
        return null;
    }

    public override void Begin(NavStep step) => blockedTicks = 0;

    public override Controls Steer(BodyState live, NavStep step, NavStep? next)
    {
        Vector2 stepWorld = NavGrid.FeetWorld(step.Tile);
        float dx = stepWorld.X - live.CentreX;
        float dy = stepWorld.Y - live.Bottom; // negative = step is above
        int dir = MathF.Sign(dx) == 0 ? lastDir : MathF.Sign(dx);
        lastDir = dir;
        // Rises of one tile are steps, taken by the motor's StepUp without leaving the ground;
        // a jump is only for two tiles or more, at the height the rise needs, or for a real wall.
        int riseTiles = (int)MathF.Ceiling(-dy / 16f);
        bool jump = live.OnGround && (WallAhead(live, dir) || riseTiles >= 2);
        // The step before a move proven from rest coasts onto its point, because a body that
        // arrives at a lip at the walk speed leaves it at that speed and lands where the
        // simulation from rest never went (the follow harness on run 7, 2026-09-08); the step
        // before a running jump arrives at that jump's own speed, because its arc was proven at
        // that speed and a faster body flies a longer one (the harness on the fourth run's
        // pocket, where a half-speed jump taken at the walk speed reached the pool).
        float speed = next is NavStep n && StartsFromRest(n) ? BodyPhysics.SteerToward(stepWorld.X, live.CentreX, live.Vx)
            : next is NavStep j && j.Kind == MoveKind.Jump ? dir * MathF.Abs(j.StartVx)
            : dir * BodyPhysics.WalkSpeed;
        return new Controls(speed, jump, BodyPhysics.JumpScaleForTiles(Math.Max(2, riseTiles)));
    }

    /// <summary>
    /// Reached within the slack, standing or not, because a walk passes through its feet points
    /// and the kerb rules keep it grounded; and at rest when the next move was proven from rest,
    /// because a body handed to a descent while still coasting slid off the lip at speed and
    /// hopped from the slope beyond it far enough to read as a fall (the follow harness on the
    /// run-5 pool route, 2026-09-08).
    /// </summary>
    public override bool Done(BodyState live, NavStep step, NavStep? next)
        => Vector2.Distance(live.Feet, NavGrid.FeetWorld(step.Tile)) < ArriveSlack && SettledFor(live, next);

    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        blockedTicks = live.CollideX ? blockedTicks + 1 : 0;
        if (blockedTicks > 30)
            return TraversalFault.Blocked;
        return base.Check(live, step, ticksOnStep);
    }

    /// <summary>
    /// A real wall in the walking direction: solid at the feet row and the row above it, so the
    /// motor's StepUp cannot take it. A collision flag alone is not that test: the game sets
    /// collideX for a one-tile kerb on the tick it is met, before StepUp lifts the body over it,
    /// and jumping on the flag made every kerb a hop.
    /// </summary>
    public static bool WallAhead(BodyState live, int dir)
    {
        if (!live.CollideX)
            return false;
        Point feet = live.FeetTile;
        int ahead = feet.X + dir;
        return NavGrid.IsSolid(ahead, feet.Y) && NavGrid.IsSolid(ahead, feet.Y - 1);
    }
}
