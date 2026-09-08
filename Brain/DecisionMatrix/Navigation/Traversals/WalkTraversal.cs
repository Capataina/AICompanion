#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A walk to the next column: the same row, one up (a step the motor's kerb rule takes) or one
/// down (a slope or a kerb the motor steps down), wherever both poses exist and the body fits all
/// along the line between them. Performing it is walking toward the step's feet point. The one
/// move this follower still derives on its own is a jump at a real wall or a two-tile rise,
/// which a walk edge never contains and only the first step of a path can meet, when the plan
/// began at the nearest node to a body that was not on one; the follow harness is where that
/// residue is measured before it is removed.
/// </summary>
public sealed class WalkTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.Walk;

    /// <summary>A walk edge takes a few ticks; a step up under the kerb rule a few more.</summary>
    private const int WalkTicks = 6;

    private int blockedTicks;
    private int lastDir = 1;

    public override IEnumerable<NavEdge> Candidates(Point t, BodyPhysics.Pose? here, bool lava)
    {
        foreach (int dir in new[] { -1, 1 })
        {
            int nx = t.X + dir;
            // A slope lowers the feet a row without an edge to fall off, so the step down is a
            // walk like the others and not a drop.
            foreach ((int dy, float cost) in new[] { (0, 1f), (-1, 1.5f), (1, 1.2f) })
            {
                if (NavGrid.StandAt(nx, t.Y + dy, lava) is not BodyPhysics.Pose there)
                    continue;
                if (here is BodyPhysics.Pose h && !BodyPhysics.CanSlide(NavGrid.World, h, there))
                    continue;
                yield return new NavEdge(new NavStep(new Point(nx, t.Y + dy), MoveKind.Walk, t, Ticks: WalkTicks), cost, 0, false);
            }
        }
    }

    public override void Begin(NavStep step) => blockedTicks = 0;

    public override Controls Steer(BodyState live, NavStep step)
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
        return new Controls(dir * BodyPhysics.WalkSpeed, jump, BodyPhysics.JumpScaleForTiles(Math.Max(2, riseTiles)));
    }

    /// <summary>Reached within the slack, standing or not: a walk passes through its feet points and the kerb rules keep it grounded.</summary>
    public override bool Done(BodyState live, NavStep step)
        => Vector2.Distance(live.Feet, NavGrid.FeetWorld(step.Tile)) < ArriveSlack;

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
