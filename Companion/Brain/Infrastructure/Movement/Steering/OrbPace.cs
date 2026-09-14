#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The body's pace as the game-free core reads it: the speed cap and the acceleration the motor
/// will actually apply this tick, in pixels per tick. The motor writes them from the live player
/// every tick, because the core cannot read the player; a headless tool writes the pace of the
/// player it is standing in for. The steering brakes and bends against these, and the flood's
/// travel estimates divide by the cap.
/// </summary>
public static class OrbPace
{
    public static float MaxSpeed { get; set; } = 6f;
    public static float Acceleration { get; set; } = 0.24f;

    /// <summary>How far the body travels while braking from the cap to rest: v² over 2a.</summary>
    public static float BrakingDistance => MaxSpeed * MaxSpeed / (2f * System.MathF.Max(0.01f, Acceleration));
}
