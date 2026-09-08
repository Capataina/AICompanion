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
}
