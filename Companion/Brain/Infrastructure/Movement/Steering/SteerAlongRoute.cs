#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The steering law, pure: given the body and its route, the velocity to accelerate toward this
/// tick. The body is steered at a point a fixed distance ahead of its projection on the route,
/// which is what makes it lean into a turn and cut the inside of it rather than tracking every
/// waypoint; the speed is the body's cap, lowered into a bend in proportion to how sharply the
/// route turns at the next waypoint within braking distance, and lowered toward the goal so the
/// body arrives rather than overshoots. The momentum this asks for is the motor's to apply; the
/// law never writes the body.
/// </summary>
public static class SteerAlongRoute
{
    /// <summary>
    /// Advance the route's segment index past segments the body has passed, and return the
    /// desired velocity and the point it aims at. A body that has drifted off the route is still
    /// steered at the lookahead from its projection, which pulls it back onto the route rather
    /// than back to the point it left it at.
    /// </summary>
    public static Controls Steer(OrbState live, Route route, float maxSpeed, float speedChange, out Vector2 lookahead)
    {
        Vector2 centre = live.Centre;
        // Project onto the current and the next few segments; take the nearest, and advance.
        int best = route.Index;
        float bestDistance = float.PositiveInfinity;
        Vector2 bestProjection = centre;
        int last = Math.Min(route.Points.Count - 2, route.Index + 3);
        for (int i = Math.Max(0, route.Index); i <= last; i++)
        {
            Vector2 projection = Route.Project(centre, route.Points[i], route.Points[i + 1]);
            float distance = Vector2.DistanceSquared(centre, projection);
            // A later segment wins only when it is genuinely nearer, so a route that doubles back
            // past itself does not skip its own middle.
            if (distance < bestDistance - 1f) { best = i; bestDistance = distance; bestProjection = projection; }
        }
        route.Index = best;

        // The lookahead point: a distance along the route from the projection that grows with the body's
        // speed, or the goal. A slow body tracks a winding route closely; a fast one looks far enough
        // ahead to lean into the bend it is about to meet rather than reacting to it at the corner.
        float remaining = Math.Clamp(live.Velocity.Length() * Weights.OrbLookaheadTicks,
            Weights.OrbLookaheadMinimumPixels, Weights.OrbLookaheadMaximumPixels);
        Vector2 cursor = bestProjection;
        int segment = best;
        while (segment < route.Points.Count - 1)
        {
            Vector2 end = route.Points[segment + 1];
            float toEnd = Vector2.Distance(cursor, end);
            if (toEnd >= remaining) { cursor += (end - cursor) * (remaining / MathF.Max(toEnd, 1e-3f)); remaining = 0f; break; }
            remaining -= toEnd;
            cursor = end;
            segment++;
        }
        lookahead = cursor;

        Vector2 toLookahead = lookahead - centre;
        float distanceToLookahead = toLookahead.Length();
        if (distanceToLookahead < 1e-3f) return Controls.None;
        Vector2 direction = toLookahead / distanceToLookahead;

        // Brake for the goal: the speed from which the body can stop over the remaining route. The
        // straight distance to the goal is the floor, because a body that has overshot the goal
        // projects onto the goal itself, reads no route left, and would otherwise be told to stop
        // where it is rather than come back the few pixels it sailed past.
        float remainingRoute = MathF.Max(route.RemainingLength(centre), Vector2.Distance(centre, route.Goal));
        float brake = OrbPace.ArrivalSpeed(remainingRoute);
        // Bend: at the next waypoint within braking distance, the turn between the segment
        // arriving and the one leaving; the cap through it falls with the turn's sharpness, and
        // the speed now is what lets the body slow to that cap by the time it gets there.
        float bend = maxSpeed;
        float distanceToBend = 0f;
        for (int i = best; i < route.Points.Count - 2; i++)
        {
            Vector2 a = i == best ? bestProjection : route.Points[i];
            distanceToBend += Vector2.Distance(a, route.Points[i + 1]);
            if (distanceToBend > OrbPace.BrakingDistance) break;
            Vector2 arriving = route.Points[i + 1] - route.Points[i];
            Vector2 leaving = route.Points[i + 2] - route.Points[i + 1];
            if (arriving.LengthSquared() < 1e-6f || leaving.LengthSquared() < 1e-6f) continue;
            float turn = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(arriving), Vector2.Normalize(leaving)), -1f, 1f));
            float capThrough = maxSpeed * MathF.Max(Weights.OrbBendMinimumShare, 1f - Weights.OrbBendSlowdown * turn / MathF.PI);
            float allowedNow = MathF.Sqrt(capThrough * capThrough + 2f * speedChange * distanceToBend);
            bend = MathF.Min(bend, allowedNow);
        }
        float speed = MathF.Min(maxSpeed, MathF.Min(brake, bend));
        return new Controls(direction * speed);
    }
}
