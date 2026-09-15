#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// What the brain asks of the body for one tick, in the body's own terms and nobody else's: the
/// velocity to accelerate toward. The motor turns it into a move through <see cref="OrbPace.Step"/>,
/// capped at the body's speed, and nothing else ever moves the body. A zero desire eases the momentum
/// toward rest.
///
/// <para><paramref name="Burst"/> lets the change along the velocity use the body's full turn authority
/// instead of its gentler easing rate. Only a move that has to get clear of a predicted hit in time asks
/// for it; ordinary travel and hovering never do, because easing into and out of a move is the point.</para>
/// </summary>
public readonly record struct Controls(Vector2 Desired, bool Burst = false)
{
    /// <summary>Ask for nothing: the body eases to rest.</summary>
    public static readonly Controls None = new(Vector2.Zero);

    public override string ToString() => Burst ? $"desired={Desired.X:0.00},{Desired.Y:0.00};burst" : $"desired={Desired.X:0.00},{Desired.Y:0.00}";
}
