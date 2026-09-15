#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The body's pace, and the one law that changes its velocity. The motor writes the three numbers from
/// the live player every tick, because the core cannot read the player; a headless tool writes the pace of
/// the player it is standing in for. The steering brakes and bends against them, the flood's travel
/// estimates divide by the cap, and every forward simulation of the body — the motor itself, the evade's
/// candidate headings, a fixture's loop — changes velocity through <see cref="Step"/>, so a simulated body
/// and the body the game moves obey one law rather than two copies of it.
///
/// <para>Two accelerations, not one. <see cref="Turn"/> caps the part of a tick's change that bends the
/// velocity sideways, and it is the body's turn authority: the turning radius at speed is the square of
/// the speed over it, which is why its multiple was raised until the corridor fixture's body kept
/// clearance through a bend. <see cref="SpeedChange"/> caps the part along the velocity — speeding up and
/// slowing down — and is deliberately softer, because the owner ruled on 15 September 2026 that the orb
/// eases into and out of every move with its momentum visible. One shared cap could not give both: kept
/// at the turn authority the body reached the cap in a fifth of a second and stopped like a snap, and
/// lowered it swung wide into walls. A burst request lets the change along the velocity use the turn
/// authority too, which is what a dodge that has to get clear in time asks for.</para>
/// </summary>
public static class OrbPace
{
    /// <summary>Under this speed a velocity has no direction worth bending, so all of a tick's change is speeding up.</summary>
    public const float RestSpeed = 0.5f;

    // The defaults are the fallbacks in BehaviourWeights, so a headless tool that never writes them
    // runs the body a plain player would produce.
    public static float MaxSpeed { get; set; } = Selection.Weights.OrbFallbackSpeed;
    public static float Turn { get; set; } = Selection.Weights.OrbFallbackTurn;
    public static float SpeedChange { get; set; } = Selection.Weights.OrbFallbackSpeedChange;

    /// <summary>How far the body travels while slowing from the cap to rest at its easing rate: v² over 2a.</summary>
    public static float BrakingDistance => MaxSpeed * MaxSpeed / (2f * MathF.Max(0.01f, SpeedChange));

    /// <summary>One tick of the body's velocity law at this tick's pace.</summary>
    public static Vector2 Step(Vector2 velocity, Vector2 desired, bool burst = false)
        => Step(velocity, desired, MaxSpeed, Turn, SpeedChange, burst);

    /// <summary>
    /// Move <paramref name="velocity"/> toward <paramref name="desired"/> by at most one tick's change: the
    /// part of the change across the current heading capped by the turn authority, the part along it by the
    /// easing rate (or the turn authority on a burst), then the whole capped at the speed.
    /// </summary>
    public static Vector2 Step(Vector2 velocity, Vector2 desired, float maxSpeed, float turn, float speedChange, bool burst)
    {
        if (desired.LengthSquared() > maxSpeed * maxSpeed) desired = Vector2.Normalize(desired) * maxSpeed;
        Vector2 change = desired - velocity;
        float alongCap = burst ? MathF.Max(turn, speedChange) : speedChange;
        float speed = velocity.Length();
        if (speed < RestSpeed)
        {
            if (change.LengthSquared() > alongCap * alongCap) change = Vector2.Normalize(change) * alongCap;
        }
        else
        {
            Vector2 heading = velocity / speed;
            float alongAmount = Vector2.Dot(change, heading);
            Vector2 across = change - heading * alongAmount;
            if (across.LengthSquared() > turn * turn) across = Vector2.Normalize(across) * turn;
            change = heading * Math.Clamp(alongAmount, -alongCap, alongCap) + across;
        }
        velocity += change;
        if (velocity.LengthSquared() > maxSpeed * maxSpeed) velocity = Vector2.Normalize(velocity) * maxSpeed;
        return velocity;
    }

    /// <summary>
    /// The speed to ask for with <paramref name="remaining"/> pixels still to go: the cap, eased down to what
    /// lets the body slow at a share of its easing rate and arrive gliding rather than braking at the last
    /// moment. The share is under one on purpose, so the motor is never asked for its whole easing rate and
    /// the slowdown reads as a glide.
    /// </summary>
    public static float ArrivalSpeed(float remaining)
    {
        float distance = MathF.Max(0f, remaining - Navigator.ArriveDistance * 0.5f);
        return MathF.Min(MaxSpeed, MathF.Sqrt(2f * SpeedChange * Selection.Weights.OrbArrivalEasingShare * distance));
    }
}
