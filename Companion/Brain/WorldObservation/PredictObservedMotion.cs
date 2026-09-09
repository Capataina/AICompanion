using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>Shared, terrain-constrained forecast of observed motion; it does not invent an enemy's next AI decision.</summary>
public static class PredictObservedMotion
{
    private sealed class Track
    {
        public NPC Subject = null!;
        public int Type;
        public ulong Tick;
        public Vector2 Position, Velocity, Acceleration;
        public float Gravity, MaxFallSpeed, WaterMovementSpeed, LavaMovementSpeed, HoneyMovementSpeed, ShimmerMovementSpeed;
        public bool NoGravity, NoTileCollide, Wet, LavaWet, HoneyWet, ShimmerWet;
        public readonly List<Vector2> Centres = new();
        public Vector2 ForecastPosition, ForecastVelocity;
    }

    private static readonly Dictionary<int, Track> tracks = new();

    public static void Forget(int slot) => tracks.Remove(slot);
    public static void Clear() => tracks.Clear();

    public static void Observe(NPC npc)
    {
        ulong tick = Main.GameUpdateCount;
        if (!tracks.TryGetValue(npc.whoAmI, out Track? track))
            tracks[npc.whoAmI] = track = new Track();
        if (track.Subject == npc && track.Type == npc.type && track.Tick == tick
            && track.Position == npc.position && track.Velocity == npc.velocity) return;

        bool consecutive = track.Subject == npc && track.Type == npc.type && tick == track.Tick + 1
            && Vector2.DistanceSquared(npc.position, track.Position + track.Velocity) < 64f * 64f;
        Vector2 delta = consecutive ? npc.velocity - track.Velocity : Vector2.Zero;
        // Collisions and jumps are impulses, not acceleration to extrapolate for an entire flight.
        if (npc.collideX || MathF.Abs(delta.X) > 1f) delta.X = 0f;
        if (npc.collideY || MathF.Abs(delta.Y) > 1f) delta.Y = 0f;
        track.Acceleration = delta;
        track.Subject = npc;
        track.Type = npc.type;
        track.Tick = tick;
        track.Position = track.ForecastPosition = npc.position;
        track.Velocity = track.ForecastVelocity = npc.velocity;
        track.Gravity = npc.gravity;
        track.MaxFallSpeed = npc.maxFallSpeed;
        track.WaterMovementSpeed = npc.waterMovementSpeed;
        track.LavaMovementSpeed = npc.lavaMovementSpeed;
        track.HoneyMovementSpeed = npc.honeyMovementSpeed;
        track.ShimmerMovementSpeed = npc.shimmerMovementSpeed;
        track.NoGravity = npc.noGravity;
        track.NoTileCollide = npc.noTileCollide;
        track.Wet = npc.wet;
        track.LavaWet = npc.lavaWet;
        track.HoneyWet = npc.honeyWet;
        track.ShimmerWet = npc.shimmerWet;
        track.Centres.Clear();
        track.Centres.Add(npc.Center);
    }

    public static Vector2 Predict(NPC npc, int ticks)
    {
        Observe(npc);
        Track track = tracks[npc.whoAmI];
        // Consumers only have evidence for a short forecast. Longer requests retain the last
        // bounded prediction instead of asserting an unobserved enemy policy indefinitely.
        ticks = Math.Clamp(ticks, 0, 180);
        bool up = Collision.up, down = Collision.down, stair = Collision.stair,
            fall = Collision.stairFall, honey = Collision.honey, shimmer = Collision.shimmer,
            sloping = Collision.sloping;
        try
        {
            while (track.Centres.Count <= ticks)
            {
                int horizon = track.Centres.Count;
                Vector2 velocity = track.ForecastVelocity;
                float confidence = MathF.Exp(-horizon / 12f);
                velocity.X += track.Acceleration.X * confidence;
                // NPC.UpdateNPC applies its current gravity before UpdateCollision. Those fields
                // already include type, world, liquid and mod multipliers; substituting defaults
                // here makes a valid observation diverge the moment an NPC changes any of them.
                velocity.Y = track.NoGravity
                    ? velocity.Y + track.Acceleration.Y * confidence
                    : MathF.Min(track.MaxFallSpeed, velocity.Y + (track.Acceleration.Y > 0f ? track.Acceleration.Y : track.Gravity));
                if (!track.NoTileCollide)
                {
                    Vector4 downSlope = Collision.WalkDownSlope(track.ForecastPosition, velocity, npc.width, npc.height, track.Gravity);
                    track.ForecastPosition = new Vector2(downSlope.X, downSlope.Y);
                    velocity = new Vector2(downSlope.Z, downSlope.W);
                }
                Vector2 oldDryVelocity = velocity;
                if (!track.NoTileCollide)
                {
                    velocity = Collision.TileCollision(track.ForecastPosition, velocity, npc.width, npc.height);
                    Vector2 movement = velocity;
                    if (track.Wet)
                    {
                        float slowdown = track.ShimmerWet ? track.ShimmerMovementSpeed
                            : track.HoneyWet ? track.HoneyMovementSpeed
                            : track.LavaWet ? track.LavaMovementSpeed
                            : track.WaterMovementSpeed;
                        movement *= slowdown;
                        if (velocity.X != oldDryVelocity.X) movement.X = velocity.X;
                        if (velocity.Y != oldDryVelocity.Y) movement.Y = velocity.Y;
                    }
                    track.ForecastPosition += movement;
                    Vector4 slope = Collision.SlopeCollision(track.ForecastPosition, velocity, npc.width, npc.height, track.Gravity);
                    track.ForecastPosition = new Vector2(slope.X, slope.Y);
                    track.ForecastVelocity = new Vector2(slope.Z, slope.W);
                }
                else
                {
                    track.ForecastPosition += velocity;
                    track.ForecastVelocity = velocity;
                }
                track.Centres.Add(track.ForecastPosition + new Vector2(npc.width * .5f, npc.height * .5f));
            }
        }
        finally
        {
            Collision.up = up; Collision.down = down; Collision.stair = stair;
            Collision.stairFall = fall; Collision.honey = honey; Collision.shimmer = shimmer;
            Collision.sloping = sloping;
        }
        return track.Centres[ticks];
    }
}
