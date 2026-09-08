#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion;

/// <summary>
/// Turns intentions into NPC velocity. The only place the companion's physics
/// constants live, so a change to how it moves is one edit. It knows nothing about
/// why it is moving.
///
/// Horizontal movement has the player's own shape: speed builds by a fixed amount per
/// tick up to the walk speed, and bleeds by a larger fixed amount when stopping or
/// reversing, so a turn costs the ticks it costs a player and cannot happen inside one
/// jump. The first version lerped toward the target speed, which reached full speed in
/// a handful of ticks and reversed in the air in the same handful; that is the
/// "no momentum, turns instantly mid-air" the first playtest saw.
/// </summary>
public sealed class CompanionMotor
{
    public const float WalkSpeed = 3.5f;
    public const float JumpVelocity = -8.5f;

    /// <summary>Speed gained per tick toward the target; the player's runAcceleration scaled to this walk speed.</summary>
    public const float Acceleration = 0.08f * WalkSpeed / 3f;

    /// <summary>Speed lost per tick when stopping or reversing; the player's runSlowdown.</summary>
    public const float Slowdown = 0.2f;

    private readonly NPC npc;

    public CompanionMotor(NPC npc) => this.npc = npc;

    public bool OnGround => npc.velocity.Y == 0f;

    /// <summary>
    /// Set by the navigator for the tick it wants the body to fall through the platform it
    /// stands on; read by the NPC's fall-through hook and cleared every tick.
    /// </summary>
    public bool WantsFallThrough { get; set; }

    /// <summary>Accelerate toward a horizontal speed; sign is direction, magnitude is pace.</summary>
    public void MoveX(float speedX)
    {
        npc.velocity.X = StepVelocity(npc.velocity.X, speedX);
        if (speedX != 0f)
            npc.direction = npc.spriteDirection = speedX > 0f ? 1 : -1;
    }

    public void Stop()
    {
        npc.velocity.X = StepVelocity(npc.velocity.X, 0f);
    }

    public void Face(float worldX)
    {
        npc.direction = npc.spriteDirection = worldX >= npc.Center.X ? 1 : -1;
    }

    /// <summary>
    /// One tick of horizontal physics: toward <paramref name="target"/> by the acceleration when
    /// the current speed is on the target's side, by the slowdown when it is against it or the
    /// target is zero, never overshooting. Shared with the reflex simulation so a simulated
    /// step-back moves exactly as the real one does.
    /// </summary>
    public static float StepVelocity(float v, float target)
    {
        if (target == 0f || MathF.Sign(v) == -MathF.Sign(target))
        {
            float slowed = v - MathF.Sign(v) * Slowdown;
            v = MathF.Sign(slowed) != MathF.Sign(v) ? 0f : slowed;
            if (target == 0f)
                return v;
        }
        float next = v + MathF.Sign(target) * Acceleration;
        // Clamp to the target only once the speed is on the target's side: a magnitude test
        // alone snapped -3.2 straight to +1.75 on a reversal, which is the instant turn the
        // slowdown above exists to prevent.
        return MathF.Sign(next) == MathF.Sign(target) && MathF.Abs(next) > MathF.Abs(target) ? target : next;
    }

    /// <summary>Jump if standing; returns whether it happened.</summary>
    public bool Jump(float scale = 1f)
    {
        if (!OnGround)
            return false;
        npc.velocity.Y = JumpVelocity * scale;
        return true;
    }

    /// <summary>
    /// Jump velocity for a rise of so many tiles, the heights the fighter AI uses (-6 for two
    /// tiles, -7 for three, -8 for four) and the full jump above that; one tile is a step, not a jump.
    /// </summary>
    public static float JumpScaleForTiles(int tiles) => tiles switch
    {
        <= 2 => 6f / -JumpVelocity,
        3 => 7f / -JumpVelocity,
        4 => 8f / -JumpVelocity,
        _ => 1f,
    };

    /// <summary>
    /// Walk one-tile steps and slopes the way every vanilla walker does: Collision.StepUp lifts
    /// the body over a one-tile rise ahead of it and StepDown keeps its feet on a one-tile fall,
    /// so neither ever registers as a wall. Custom-AI NPCs get none of this unless they call it,
    /// which is why the companion used to jump at every kerb. Called once per tick after the
    /// brain has set the velocity, in the same place the fighter AI calls it.
    /// </summary>
    public void ApplySteps()
    {
        if (npc.velocity.Y == 0f)
            Collision.StepDown(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
        if (npc.velocity.Y >= 0f)
            Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY, 1, false, 1);
    }

    /// <summary>Vertical offset of a jump from standing after so many ticks, from the jump velocity and NPC gravity 0.3 (negative is up).</summary>
    public static float JumpOffsetAt(int ticks) => JumpVelocity * ticks + 0.15f * ticks * ticks;
}
