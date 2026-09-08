#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A fall through the platform underfoot to the first surface below, the way a player presses
/// down; a mine shaft capped with platforms is otherwise a ceiling. Planned by the same lowering
/// scan as a drop with the platform passed, and performed by steering to the plan's line and
/// asking the body to pass its platform this tick.
/// </summary>
public sealed class FallThroughTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.FallThrough;

    public override IEnumerable<NavEdge> Candidates(Point t, BodyPhysics.Pose? here, bool lava)
    {
        if (!NavGrid.IsPlatformUnder(t.X, t.Y))
            yield break;
        foreach ((Point through, int depth, float steerX) in DropTraversal.Landings(t.X, t.Y, throughPlatform: true, lava))
            if (depth >= 2)
                yield return new NavEdge(new NavStep(through, MoveKind.FallThrough, t, SteerX: steerX, Ticks: FallTicks(depth)), 1f + depth * 0.2f, depth, true);
    }

    private bool airborne;

    public override void Begin(NavStep step) => airborne = false;

    public override Controls Steer(BodyState live, NavStep step)
    {
        airborne |= !live.OnGround;
        // Same steer as a drop, then let the body pass the platform this tick.
        float gap = DropTraversal.SteerX(step, live, throughPlatform: true) - live.CentreX;
        return new Controls(MathF.Abs(gap) > 2f ? MathF.Sign(gap) * BodyPhysics.WalkSpeed * 0.5f : 0f, FallThrough: true);
    }

    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if (airborne && LandedElsewhere(live, step))
            return TraversalFault.Misland;
        return base.Check(live, step, ticksOnStep);
    }
}
