#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// What the brain asks of the body for one tick, in the body's own terms and nobody else's: the
/// velocity to accelerate toward. The motor turns it into a move by accelerating the momentum
/// it holds toward this, capped at the body's speed, and nothing else ever moves the body. A
/// zero desire is a brake: the momentum decays toward rest at the body's acceleration.
/// </summary>
public readonly record struct Controls(Vector2 Desired)
{
    /// <summary>Ask for nothing: the body brakes to rest.</summary>
    public static readonly Controls None = new(Vector2.Zero);

    public override string ToString() => $"desired={Desired.X:0.00},{Desired.Y:0.00}";
}
