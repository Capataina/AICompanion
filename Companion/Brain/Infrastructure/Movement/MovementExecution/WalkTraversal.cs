#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

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
/// walking toward the step's feet point, and nothing else: this traversal derives no move of its
/// own. It used to raise a jump at a real wall or a two-tile rise, which is a move no walk edge
/// can contain — the edge is proven by driving the body, and the only lift in that simulation is
/// the motor's StepUp, which gains a tile at most — so the jump could only fire for a body that
/// had already left what the proof described, and it answered that divergence by inventing a move
/// instead of reporting it. <see cref="Check"/> reports it now, and the navigator replans from the
/// live state, which is the same answer every other divergence in this folder gets.
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
    private Vector2 lastProgress;
    private int noProgressTicks;

    /// <summary>A kerb: the game's StepUp lifts the body one row without leaving the ground.</summary>
    public override int ClimbTiles => 1;

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
                // The whole landing is compared and not its row alone: a walk that lands one
                // column further along at speed than it did from rest promises a tile the body
                // will not be standing on, so Done never fires there and the step faults or
                // walks back. Comparing Y only let that through (Codex review of 7525a1b).
                var atSpeedProof = Simulate(pose, dir * BodyPhysics.WalkSpeed, dir, t, lava);
                bool fromRest = atSpeedProof is not (Point atSpeed, _) || atSpeed != landing;
                int dy = landing.Y - t.Y;
                // The step records how long the walk the performer makes takes, from this pose to the landing's
                // pose, which is where the next step's proof starts. A plain walk is taken
                // at the walk speed, so it carries the at-speed proof's duration; recording the from-rest
                // duration priced every tile of a route as a stop and a restart, several times the body's
                // real travel, and inflated everything that sums step durations: return estimates, the
                // reunion charge and meeting places. Route cost does not read a walk's duration, so this
                // changes estimates and never which route wins. A walk that starts from rest keeps its
                // from-rest duration, because the step before it really does coast to a stop.
                int duration = !fromRest && atSpeedProof is (_, int atSpeedTicks) ? atSpeedTicks : ticks;
                // A slope or a short ledge lowers the feet a row without a real fall, so the
                // step down is a walk like the others and not a drop; a step up costs its kerb.
                var step = new NavStep(landing, MoveKind.Walk, t, Ticks: duration, FromRest: fromRest);
                yield return new NavEdge(step, MovementCost(step), 0, false);
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
    /// <paramref name="startVx"/>, driven at the walk speed until its centre stands in the next
    /// column, and timed on until it reaches the landing's pose, with any time in the air ridden out (the hop the kerb rule leaves to
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
            if (dx < 1 || Math.Abs(dy) > 1 || NavGrid.StandAt(landing.X, landing.Y, lava) is not BodyPhysics.Pose next)
                return null;
            // The walk is made on entering the next column, but it lasts until the body stands where the next step
            // starts, at the landing's pose. A pose sits near its column's centre, so timing the walk to the column's
            // edge counted about half a tile, and a chain of walks read as twice the body's real pace. Rock that stops
            // the body just short of an off-centre pose ends the count there.
            int arrived = ticks;
            for (BodyState walking = state; arrived < MaxTicks && dir * (walking.Left - next.Left) < 0; arrived++)
            {
                walking = BodyMotion.Step(world, walking, controls);
                if (walking.Stuck || walking.CollideX) break;
            }
            return (landing, arrived);
        }
        return null;
    }

    public override void Begin(NavStep step)
    {
        blockedTicks = 0;
        noProgressTicks = 0;
        lastProgress = default;
    }

    private readonly record struct ExecutionState(int BlockedTicks, int LastDirection, Vector2 LastProgress, int NoProgressTicks);

    public override object CaptureExecutionState() => new ExecutionState(blockedTicks, lastDir, lastProgress, noProgressTicks);

    public override void RestoreExecutionState(object? state)
    {
        if (state is ExecutionState saved)
        {
            blockedTicks = saved.BlockedTicks;
            lastDir = saved.LastDirection;
            lastProgress = saved.LastProgress;
            noProgressTicks = saved.NoProgressTicks;
        }
    }

    public override Controls Steer(BodyState live, NavStep step, NavStep? next)
    {
        Vector2 stepWorld = NavGrid.FeetWorld(step.Tile);
        float dx = stepWorld.X - live.CentreX;
        int dir = MathF.Sign(dx) == 0 ? lastDir : MathF.Sign(dx);
        lastDir = dir;
        // The walk raises no jump at all. Every rise a walk edge can contain is at most one tile,
        // because the edge was proven by driving the body and the only lift in that simulation is
        // the motor's StepUp, which never gains more than a tile; so a jump here could only ever
        // fire for a body that is not where the proof put it — off a node, below its own step, or
        // pressed against a shape the proof never met. That is a divergence, and the answer to a
        // divergence is a fault and a replan from the live state (Check already returns Blocked on
        // a sideways press and Stuck on no headway), not a move this traversal invented. The
        // walker deriving its own jump was the last place the planner and the follower were two
        // rules for one move, which is the defect this whole folder exists to remove; the live
        // cost of keeping it was 19 hops on one-tile walk steps in the 18:56 capture of 0.22.46,
        // each landing at 0.00 to 1.57 px/tick against a 3.5 walk speed, because the walker
        // re-aims at its one-tile step every airborne tick and reverses once the body overflies it.
        if (step.FromRest && live.OnGround && MathF.Abs(live.Vx) > RestSpeed && live.Covers(step.From))
            return Controls.None;
        // The step before a move proven from rest coasts onto its point, because a body that
        // arrives at a lip at the walk speed leaves it at that speed and lands where the
        // simulation from rest never went (the follow harness on run 7, 2026-09-08); the step
        // before a running jump arrives at that jump's own speed, because its arc was proven at
        // that speed and a faster body flies a longer one (the harness on the fourth run's
        // pocket, where a half-speed jump taken at the walk speed reached the pool).
        float speed = next is NavStep n && StartsFromRest(n) ? BodyPhysics.SteerToward(stepWorld.X, live.CentreX, live.Vx)
            : next is NavStep j && j.Kind == MoveKind.Jump ? dir * MathF.Abs(j.StartVx)
            : dir * BodyPhysics.WalkSpeed;
        return new Controls(speed);
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
        if (ticksOnStep == 0)
            lastProgress = live.Feet;
        else if (Vector2.DistanceSquared(lastProgress, live.Feet) < 1f)
            noProgressTicks++;
        else
        {
            lastProgress = live.Feet;
            noProgressTicks = 0;
        }
        blockedTicks = live.CollideX ? blockedTicks + 1 : 0;
        if (blockedTicks > 30)
            return TraversalFault.Blocked;
        if (noProgressTicks > 45)
            return TraversalFault.Stuck;
        return base.Check(live, step, ticksOnStep);
    }
}
