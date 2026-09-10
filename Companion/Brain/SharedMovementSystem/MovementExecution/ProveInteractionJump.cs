#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>Proves an interaction is reachable during a ground jump that lands safely nearby.</summary>
public static class ProveInteractionJump
{
    public static bool CanReach(ITileWorld world, BodyState start, Func<BodyState, bool> inReach)
    {
        if (!start.OnGround || start.Wet || start.CannotAct || start.Stuck) return false;
        BodyState body = start;
        bool reached = false;
        for (int tick = 0; tick < 90; tick++)
        {
            body = BodyMotion.Step(world, body, new Controls(0, Jump: tick == 0));
            if (body.Wet || body.Stuck || body.CannotAct) return false;
            reached |= inReach(body);
            if (body.OnGround)
                return reached && Vector2.DistanceSquared(body.Feet, start.Feet) <= BodyPhysics.Width * BodyPhysics.Width;
        }
        return false;
    }
}
