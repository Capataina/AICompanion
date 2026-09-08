#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A fall through the platform underfoot to the first surface below, the way a player presses
/// down; a mine shaft capped with platforms is otherwise a ceiling. Proven and performed by the
/// drop's own descent (the body driven to a line on the platform, then asked to pass it), with
/// the platform columns as the span.
/// </summary>
public sealed class FallThroughTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.FallThrough;

    public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava)
    {
        Point t = node.Tile;
        if (here is not BodyPhysics.Pose pose || !NavGrid.IsPlatformUnder(pose, t.Y))
            yield break;
        foreach (NavEdge edge in DropTraversal.Descents(t, pose, t.X, throughPlatform: true, lava, MoveKind.FallThrough))
            yield return edge;
    }

    /// <summary>The body has been asked to pass its platform: the first rest after that is the landing, as it is in the proof.</summary>
    private bool pressed;

    public override void Begin(NavStep step) => pressed = false;

    /// <summary>Landed: standing in the promised row with the promised column under the body, where the descent filed it.</summary>
    public override bool Done(BodyState live, NavStep step, NavStep? next) => live.Covers(step.Tile);

    public override Controls Steer(BodyState live, NavStep step, NavStep? next)
    {
        Controls controls = DropTraversal.Perform(step, live, throughPlatform: true);
        pressed |= controls.FallThrough;
        return controls;
    }

    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if (pressed && LandedElsewhere(live, step))
            return TraversalFault.Misland;
        return base.Check(live, step, ticksOnStep);
    }
}
