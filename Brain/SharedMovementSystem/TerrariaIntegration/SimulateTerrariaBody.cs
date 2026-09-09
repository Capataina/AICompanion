using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>
/// A side-effect-free preview of the companion's engine movement path. Calls the engine's
/// collision helpers in NPC.UpdateCollision order; does not execute NPC AI, damage, buffs,
/// sounds or item code. Collision's scratch flags are restored before returning. The next
/// real AI entry measures its prediction error, because engine hooks and external hits can
/// still change what happens after planning.
/// </summary>
public static class SimulateTerrariaBody
{
    public static BodyState Step(BodyState state, Controls controls, MovementCapabilities capabilities)
    {
        bool oldUp = Collision.up, oldDown = Collision.down, oldStair = Collision.stair,
            oldFall = Collision.stairFall, oldHoney = Collision.honey, oldShimmer = Collision.shimmer,
            oldSloping = Collision.sloping;
        try
        {
            BodyState driven = MovementAbilities.ApplyControls(state, controls, capabilities);
            Vector2 position = new(driven.Left, driven.Bottom - BodyPhysics.Height);
            Vector2 velocity = new(driven.Vx, driven.Vy);
            float stepSpeed = 0f, gfxOffset = 0f;
            int width = BodyPhysics.Width, height = BodyPhysics.Height;
            if (velocity.Y == 0f && !controls.FallThrough)
                Collision.StepDown(ref position, ref velocity, width, height, ref stepSpeed, ref gfxOffset);
            if (velocity.Y >= 0f)
                Collision.StepUp(ref position, ref velocity, width, height, ref stepSpeed, ref gfxOffset, 1, !controls.Descend, 1);

            // NPC.UpdateNPC_UpdateGravity runs before AI using the previous liquid state.
            // Space changes gravity continuously; preserve the engine rule rather than make
            // a surface-derived constant become a promise for sky travel.
            float gravity = 0.3f, cap = 10f;
            float size = Main.maxTilesX / 4200f;
            float altitude = (float)((state.Bottom - height) / 16f - (60f + 10f * size * size));
            gravity *= Math.Clamp(altitude / (float)(Main.worldSurface / 6.0), 0.25f, 1f);
            if (state.Wet)
            {
                (gravity, cap) = state.LiquidKind switch
                {
                    2 => (0.1f, 4f), 3 => (0.15f, 5.5f), _ => (0.2f, 7f)
                };
            }
            velocity.Y = MathF.Min(velocity.Y + gravity, cap);
            if (MathF.Abs(velocity.X) < 0.005f) velocity.X = 0;
            Vector4 downSlope = Collision.WalkDownSlope(position, velocity, width, height, gravity);
            position = new Vector2(downSlope.X, downSlope.Y);
            velocity = new Vector2(downSlope.Z, downSlope.W);
            bool lava = Collision.LavaCollision(position, width, height);
            bool wet = Collision.WetCollision(position, width, height);
            // NPC liquid flags persist until the body becomes completely dry. Crossing from
            // honey into water therefore retains honey drag and next tick's honey gravity.
            int detectedLiquid = Collision.shimmer ? 3 : Collision.honey ? 2 : lava ? 1 : 0;
            int liquid = wet ? Math.Max(state.Wet ? state.LiquidKind : 0, detectedLiquid) : 0;
            if (state.Wet && !wet) velocity.X *= 0.5f;

            Vector2 beforeCollision = velocity;
            velocity = Collision.TileCollision(position, velocity, width, height, controls.FallThrough, controls.FallThrough);
            if (Collision.up) velocity.Y = 0.01f;
            bool collideX = velocity.X != beforeCollision.X;
            bool collideY = velocity.Y != beforeCollision.Y;
            if (wet)
            {
                float slowdown = liquid == 2 ? 0.25f : liquid == 3 ? 0.375f : 0.5f;
                Vector2 movement = velocity * slowdown;
                if (collideX) movement.X = velocity.X;
                if (collideY) movement.Y = velocity.Y;
                position += movement;
            }
            else position += velocity;

            bool stairFall = state.StairFall || controls.FallThrough;
            Vector4 slope = Collision.SlopeCollision(position, velocity, width, height, gravity, stairFall);
            position = new Vector2(slope.X, slope.Y);
            velocity = new Vector2(slope.Z, slope.W);
            if (Collision.stairFall) stairFall = true;
            else if (!controls.FallThrough) stairFall = false;
            return driven with
            {
                Left = position.X, Bottom = position.Y + height, Vx = velocity.X, Vy = velocity.Y,
                OnGround = velocity.Y == 0f, CollideX = collideX, Pinned = false,
                Wet = wet, LiquidKind = liquid, StairFall = stairFall
            };
        }
        finally
        {
            Collision.up = oldUp; Collision.down = oldDown; Collision.stair = oldStair;
            Collision.stairFall = oldFall; Collision.honey = oldHoney; Collision.shimmer = oldShimmer;
            Collision.sloping = oldSloping;
        }
    }
}
