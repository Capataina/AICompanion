#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion;

/// <summary>
/// Turns intentions into NPC velocity. The only place the companion's physics
/// constants live, so a change to how it moves is one edit. It knows nothing about
/// why it is moving.
/// </summary>
public sealed class CompanionMotor
{
    public const float WalkSpeed = 3.5f;
    public const float Acceleration = 0.25f;
    public const float JumpVelocity = -8.5f;

    private readonly NPC npc;

    public CompanionMotor(NPC npc) => this.npc = npc;

    public bool OnGround => npc.velocity.Y == 0f;

    /// <summary>Accelerate toward a horizontal speed; sign is direction, magnitude is pace.</summary>
    public void MoveX(float speedX)
    {
        npc.velocity.X = MathHelper.Lerp(npc.velocity.X, speedX, Acceleration);
        if (speedX != 0f)
            npc.direction = npc.spriteDirection = speedX > 0f ? 1 : -1;
    }

    public void Stop()
    {
        npc.velocity.X *= 0.8f;
        if (MathF.Abs(npc.velocity.X) < 0.1f)
            npc.velocity.X = 0f;
    }

    public void Face(float worldX)
    {
        npc.direction = npc.spriteDirection = worldX >= npc.Center.X ? 1 : -1;
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
