#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A drop off a lip: the body walks to a line in the open span beside the lip (against either
/// wall or down the middle, because in a shaft wider than the body what it lands on depends on
/// which wall it hugs), falls, and comes to rest on the first surface that holds it. The edge
/// is proven by driving the body's own tick rule with the descent's steering from the lip's
/// pose until it lands, and performing it is the same steering from wherever the body is, so
/// the line the step carries is the line the body steers to from the lip and the landing is
/// wherever that line took the simulated body. The row-by-row lowering scan with a fixed drift
/// that proved drops before this (run 5 to run 7, 2026-09-08) carried the X the body had drifted
/// to by the landing as the line to steer to at the top, which parked the run-7 body four pixels
/// onto the block beside its platform and pressing down (AIC-180), and mislanded every diagonal
/// drop in the follow harness's first baseline.
/// </summary>
public sealed class DropTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.Drop;
    public override bool EntryDependsOnNext => false;

    public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava)
    {
        Point t = node.Tile;
        if (here is not BodyPhysics.Pose pose)
            yield break;
        foreach (int dir in new[] { -1, 1 })
        {
            int nx = t.X + dir;
            // The column beside the lip must be clear for the body and open underneath: a support
            // one row down is the walk step-down's job, and a platform there is a floor the body
            // walks onto, whose way down is the fall-through edge from that tile (run 6, 2026-09-08).
            if (!NavGrid.IsBodyClear(nx, t.Y) || NavGrid.IsSupport(nx, t.Y + 1))
                continue;
            foreach (NavEdge edge in Descents(t, pose, nx, throughPlatform: false, lava, MoveKind.Drop))
                yield return edge;
        }
    }

    /// <summary>
    /// Every place the body comes to rest when it descends from <paramref name="t"/> through the
    /// open span around <paramref name="column"/>: the span's middle and its two walls are the
    /// lines tried, and for a fall-through the place the body already stands as well, because
    /// a platform staircase is descended by pressing down where the tread ends, with the body
    /// hanging over the next tread, and no line in the span puts it there. Each line is
    /// simulated with <see cref="DescentControls"/> over the body's tick rule, and each distinct
    /// landing is an edge carrying its line, its fall and its ticks.
    /// </summary>
    internal static IEnumerable<NavEdge> Descents(Point t, BodyPhysics.Pose pose, int column, bool throughPlatform, bool lava, MoveKind kind)
    {
        (int spanLeft, int spanRight) = NavGrid.OpenSpan(column, t.Y, throughPlatform);
        // A hugged wall is held a few pixels off, wider than the ground steering's tolerance, so
        // the body can never come to rest with an edge still on the lip it is leaving.
        float hugLeft = spanLeft * 16f + WallGap + BodyPhysics.Width / 2f;
        float hugRight = (spanRight + 1) * 16f - WallGap - BodyPhysics.Width / 2f;
        float centre = (spanLeft * 16f + (spanRight + 1) * 16f) / 2f;
        var seen = new HashSet<Point>();
        IEnumerable<float> lines = throughPlatform ? new[] { pose.CentreX, centre, hugLeft, hugRight } : new[] { centre, hugLeft, hugRight };
        foreach (float line in lines)
        {
            if (Simulate(pose, line, throughPlatform, lava, t.Y, out Point landing, out int ticks, out int fall) && seen.Add(landing))
            {
                var step = new NavStep(landing, kind, t, SteerX: line, Ticks: ticks);
                yield return new NavEdge(step, MovementCost(step), fall, true);
            }
        }
    }

    /// <summary>The longest descent followed: the deepest drop's fall plus the walk to the line.</summary>
    private static readonly int MaxTicks = FallTicks(NavGrid.MaxDropTiles) + 120;

    /// <summary>
    /// The body driven from <paramref name="pose"/> toward <paramref name="line"/> until it has
    /// left the ground and come to rest again: the landing is a node it is filed under (the
    /// column of its centre when that is a node, else either column it covers, because a body
    /// two pixels over a lip rests on the lip's tile and stands there in the game whichever
    /// column its centre is in), and the fall is in rows from the lip. A rest less than two
    /// rows down before any press is a step or a hop on the way to the line, not the descent,
    /// and the body walks on; once it has pressed through its platform, the first rest below is
    /// the landing however shallow, because the next tread of a staircase is one row down. A
    /// body that cannot reach its line (a wall on the way), presses on a platform something
    /// else holds it above, gets stuck in a shape, or falls past the limit proves nothing.
    /// </summary>
    private static bool Simulate(BodyPhysics.Pose pose, float line, bool throughPlatform, bool lava, int lipRow, out Point landing, out int ticks, out int fall)
    {
        ITileWorld world = NavGrid.World;
        BodyState state = BodyState.Standing(pose);
        bool airborne = false, pressed = false;
        float giveUpBelow = (lipRow + NavGrid.MaxDropTiles + 2) * 16f;
        landing = default;
        fall = 0;
        for (ticks = 1; ticks <= MaxTicks; ticks++)
        {
            Controls controls = DescentControls(line, state, throughPlatform, lipRow, pressed);
            float before = state.Bottom;
            state = BodyMotion.Step(world, state, controls);
            if (state.Stuck || state.Bottom > giveUpBelow)
                return false;
            if (state.OnGround && state.CollideX)
                return false;
            if (controls.FallThrough && state.OnGround && state.Bottom <= before)
                return false;
            pressed |= controls.FallThrough;
            airborne |= Falling(state) || (pressed && !state.OnGround);
            if (!airborne || !state.OnGround)
                continue;
            fall = state.FeetTile.Y - lipRow;
            if (fall < (pressed ? 1 : 2))
            {
                airborne = false;
                continue;
            }
            int row = state.FeetTile.Y;
            foreach (int node in new[] { state.FeetTile.X, state.LeftColumn, state.RightColumn })
            {
                if (NavGrid.StandAt(node, row, lava) == null)
                    continue;
                landing = new Point(node, row);
                return true;
            }
            return false;
        }
        return false;
    }

    /// <summary>
    /// The descent's steering, used identically to prove and to perform it: the in-air steering
    /// rule toward the line (full speed, then coasting inside the stopping distance) on the
    /// ground and in the air alike, and for a fall-through a press that begins only once the body
    /// is on the line and still standing, so a stack of platforms is descended one edge at a time
    /// and a body pressing before it is on the line does not land beside it, then is held until
    /// the feet leave <paramref name="lipRow"/>. Holding it is what makes the move possible at
    /// all: the game asks once a tick whether the body may pass its platform and treats a
    /// platform as solid under any falling body whose feet are still inside its top band, so a
    /// press released on the first airborne tick is answered by the platform reappearing under
    /// feet that have travelled less than a pixel, and the body is put back on top of it.
    /// </summary>
    internal static Controls DescentControls(float line, BodyState live, bool throughPlatform, int lipRow, bool pressing)
    {
        // On the ground the body creeps to within half a pixel of the line, because the in-air
        // tolerance of two pixels is wider than the overhang that keeps a body standing on a lip.
        float steer = BodyPhysics.SteerToward(line, live.CentreX, live.Vx, live.OnGround ? GroundTolerance : 2f);
        bool onLine = MathF.Abs(line - live.CentreX) <= GroundTolerance;
        // The row that has to be cleared is the platform's, not the lip's. A body standing on a
        // platform has its feet in the row above it — NavGrid.IsPlatformUnder asks about y + 1 —
        // so releasing the press when the feet leave the lip row releases it after less than two
        // pixels of fall, while the body is still inside the platform tile, and the game makes the
        // platform solid again underneath it. That is a three-tick fall and a catch, for ever.
        int lastRow = lipRow + 1;
        bool press = throughPlatform && (live.OnGround ? onLine : pressing) && live.FeetTile.Y <= lastRow;
        // Every tick of a descent carries the descent's own vertical intent, whether or not this
        // is the tick that presses. That is what the kerb rules read to stop lifting the body onto
        // the platform it is on its way through, and it has to cover the whole move rather than the
        // press alone: the press is released once the feet clear the take-off row, and the ticks
        // after it are exactly the ticks the body is falling past platforms it must not catch.
        return new Controls(steer, FallThrough: press, Descend: true);
    }

    /// <summary>How close to its line a standing body gets before the descent counts it there.</summary>
    private const float GroundTolerance = 0.5f;

    /// <summary>How far off a hugged wall the body's edge is held; more than the ground tolerance, so a lip is always left.</summary>
    private const float WallGap = 3f;

    /// <summary>
    /// The body has left the ground for real: falling faster than a kerb hop ever falls. A body
    /// walking down a slope leaves the ground for a few ticks at a time under the game's own
    /// step-down window, and a descent must not read that as its fall.
    /// </summary>
    internal static bool Falling(BodyState live) => !live.OnGround && live.Vy > HopFallSpeed;

    /// <summary>The fastest a body falls closing a gap the game's StepDown leaves to gravity (seven pixels, seven ticks).</summary>
    private const float HopFallSpeed = 2.4f;

    private bool airborne;

    public override void Begin(NavStep step) => airborne = false;

    public override object CaptureExecutionState() => airborne;

    public override void RestoreExecutionState(object? state)
    {
        if (state is bool saved)
            airborne = saved;
    }

    public override Controls Steer(BodyState live, NavStep step, NavStep? next)
    {
        airborne |= Falling(live);
        return Perform(step, live, throughPlatform: false, begun: airborne);
    }

    /// <summary>Landed: standing in the promised row with the promised column under the body, where the descent filed it.</summary>
    public override bool Done(BodyState live, NavStep step, NavStep? next) => live.Covers(step.Tile);

    /// <summary>
    /// The same policy as the edge proof. Entry velocity and pose are validated by the complete
    /// live-state macro before any controls are emitted. Repeatedly braking while grounded
    /// changes the take-off trajectory and invalidates the planner's proof even from rest.
    ///
    /// A descent gets no entry phase of its own, and that is a decision with a measurement behind
    /// it rather than an omission. Two were built and both failed on the captured sub-tile cave
    /// entry (`Tools/Scenarios/actual-entry-drop-run-9.txt`, driven by `VerifyCapturedEntry`).
    /// Steering onto the lip's point at the half-pixel tolerance the line is judged by cannot
    /// terminate — `BodyPhysics.SteerToward`'s only non-zero output is the full walk speed, so the
    /// body accelerates at a point it cannot stop within, overshoots, reverses and hunts until the
    /// allowance runs out: 33 faults and no arrival, against one fault and an arrival with no
    /// entry phase at all. Creeping in at half the remaining distance per tick converges in
    /// principle and hunted in the same band in practice, for the same 33 faults.
    ///
    /// The reason neither works is that the alignment the prefix search provided was found by
    /// *searching* — it accepted whichever short prefix made the whole macro simulate clean, which
    /// is not a state any steering rule can be pointed at. The property to keep, because an entry
    /// phase for descents will be proposed again: **a sub-tile entry is a positioning problem, and
    /// a controller whose only outputs are full speed and nothing cannot solve one.** What answers
    /// a body that is not where the proof stood is re-proving the descent from where the body
    /// actually is, which the planner already does — the live navigator expands its first node
    /// from the body's own pose — so the refusal, its strike and the replan that follows are the
    /// designed path and not a fallback.
    /// </summary>
    internal static Controls Perform(NavStep step, BodyState live, bool throughPlatform, bool begun)
        => DescentControls(step.SteerX, live, throughPlatform, step.From.Y, begun);

    /// <summary>
    /// Mislanded: come to rest off the promised tile two or more rows below the lip, which is the
    /// rest the proof would have taken as the landing; a shallower rest on the way (a slope's
    /// point beside the lip) is one the proof walked on from, and the performer walks on too.
    /// </summary>
    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if (airborne && LandedElsewhere(live, step) && live.FeetTile.Y - step.From.Y >= 2)
            return TraversalFault.Misland;
        return base.Check(live, step, ticksOnStep);
    }
}
