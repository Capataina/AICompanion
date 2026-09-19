using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

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
        public float MeanError;
        public int ErrorSamples;
    }

    private static readonly Dictionary<int, Track> tracks = new();

    public static void Forget(int slot) => tracks.Remove(slot);
    public static void Clear() => tracks.Clear();
    /// <summary>Read-only evidence already computed by consumers; inspecting never extends a forecast.</summary>
    public static IReadOnlyList<Vector2> ExistingForecast(NPC npc)
        => tracks.TryGetValue(npc.whoAmI, out Track? track) && track.Subject == npc && track.Type == npc.type
            && track.Tick == Main.GameUpdateCount ? track.Centres : Array.Empty<Vector2>();

    public static void Observe(NPC npc)
    {
        ulong tick = Main.GameUpdateCount;
        if (!tracks.TryGetValue(npc.whoAmI, out Track? track))
            tracks[npc.whoAmI] = track = new Track();
        if (track.Subject == npc && track.Type == npc.type && track.Tick == tick
            && track.Position == npc.position && track.Velocity == npc.velocity) return;

        bool consecutive = track.Subject == npc && track.Type == npc.type && tick == track.Tick + 1
            && Vector2.DistanceSquared(npc.position, track.Position + track.Velocity) < 64f * 64f;
        if (!consecutive) { track.MeanError = 0f; track.ErrorSamples = 0; }
        if (consecutive && track.Centres.Count > 1)
        {
            float error = Vector2.Distance(npc.Center, track.Centres[1]);
            track.MeanError = track.ErrorSamples == 0 ? error : track.MeanError * .8f + error * .2f;
            track.ErrorSamples++;
        }
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
        // Observation owns its next comparison. Relying on an aimer/reflex to request a
        // forecast made confidence depend on NPC iteration order and which behaviour ran.
        _ = Predict(npc, 1);
    }

    public static Vector2 Predict(NPC npc, int ticks)
    {
        Observe(npc);
        Track track = tracks[npc.whoAmI];
        return PredictTrack(track, ticks, npc.width, npc.height);
    }

    private static Vector2 PredictTrack(Track track, int ticks, int width, int height)
    {
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
                    Vector4 downSlope = Collision.WalkDownSlope(track.ForecastPosition, velocity, width, height, track.Gravity);
                    track.ForecastPosition = new Vector2(downSlope.X, downSlope.Y);
                    velocity = new Vector2(downSlope.Z, downSlope.W);
                }
                Vector2 oldDryVelocity = velocity;
                if (!track.NoTileCollide)
                {
                    velocity = Collision.TileCollision(track.ForecastPosition, velocity, width, height);
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
                    Vector4 slope = Collision.SlopeCollision(track.ForecastPosition, velocity, width, height, track.Gravity);
                    track.ForecastPosition = new Vector2(slope.X, slope.Y);
                    track.ForecastVelocity = new Vector2(slope.Z, slope.W);
                }
                else
                {
                    track.ForecastPosition += velocity;
                    track.ForecastVelocity = velocity;
                }
                track.Centres.Add(track.ForecastPosition + new Vector2(width * .5f, height * .5f));
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

    /// <summary>Captures enemy state for a resumable native motion query. Terrain remains
    /// native: the query owner must validate terrain dependencies before publishing samples.</summary>
    public static CapturedMotion Capture(NPC npc)
    {
        Observe(npc);
        return new CapturedMotion(npc);
    }

    public sealed class CapturedMotion
    {
        private readonly Track track;
        private readonly int width, height;
        internal CapturedMotion(NPC npc)
        {
            var source = tracks[npc.whoAmI];
            width = npc.width; height = npc.height;
            track = new Track
            {
                Type = source.Type, Tick = source.Tick, Position = source.Position, Velocity = source.Velocity,
                Acceleration = source.Acceleration, Gravity = source.Gravity, MaxFallSpeed = source.MaxFallSpeed,
                WaterMovementSpeed = source.WaterMovementSpeed, LavaMovementSpeed = source.LavaMovementSpeed,
                HoneyMovementSpeed = source.HoneyMovementSpeed, ShimmerMovementSpeed = source.ShimmerMovementSpeed,
                NoGravity = source.NoGravity, NoTileCollide = source.NoTileCollide, Wet = source.Wet,
                LavaWet = source.LavaWet, HoneyWet = source.HoneyWet, ShimmerWet = source.ShimmerWet,
                MeanError = source.MeanError, ErrorSamples = source.ErrorSamples,
                ForecastPosition = source.Position, ForecastVelocity = source.Velocity
            };
            track.Centres.Add(source.Position + new Vector2(width * .5f, height * .5f));
        }
        public int CoveredTicks => track.Centres.Count - 1;
        public IReadOnlyList<Vector2> Samples => track.Centres.AsReadOnly();
        public bool Continue(int ticks, DecisionWorkBudget budget)
        {
            if (ticks < 0 || ticks > 180) throw new ArgumentOutOfRangeException(nameof(ticks));
            while (CoveredTicks < ticks)
            {
                if (!budget.TrySpend("captured-enemy-motion")) return false;
                _ = PredictTrack(track, CoveredTicks + 1, width, height);
            }
            return true;
        }
    }

    /// <summary>Measured continuation confidence, not a claim to know the next enemy AI choice.</summary>
    public static float Confidence(NPC npc, int ticks)
    {
        Observe(npc);
        Track track = tracks[npc.whoAmI];
        float measured = track.ErrorSamples == 0 ? .5f : MathF.Exp(-track.MeanError / 32f);
        return MathHelper.Clamp(measured * MathF.Exp(-Math.Clamp(ticks, 0, 180) / 90f), .1f, 1f);
    }

    public static int ErrorSamples(NPC npc) { Observe(npc); return tracks[npc.whoAmI].ErrorSamples; }

    /// <summary>
    /// One body's motion track in plain values, for the combat snapshot: the audit restores these so its
    /// forecasts extend the same history the live decision read. The forecast centres are recomputed, never
    /// stored — they follow from the position, velocity, acceleration and physics below.
    /// </summary>
    public sealed record ExportedTrack(int Slot, int Type, ulong Tick, Vector2 Position, Vector2 Velocity,
        Vector2 Acceleration, float Gravity, float MaxFallSpeed, float WaterSpeed, float LavaSpeed,
        float HoneySpeed, float ShimmerSpeed, bool NoGravity, bool NoTileCollide, bool Wet, bool LavaWet,
        bool HoneyWet, bool ShimmerWet, float MeanError, int ErrorSamples);

    /// <summary>Every tracked body, for the snapshot to carry the forecast history with the forecast.</summary>
    public static IReadOnlyCollection<int> TrackedSlots => tracks.Keys;

    public static ExportedTrack? ExportTrack(int slot)
    {
        if (!tracks.TryGetValue(slot, out Track? track))
            return null;
        return new ExportedTrack(slot, track.Type, track.Tick, track.Position, track.Velocity, track.Acceleration,
            track.Gravity, track.MaxFallSpeed, track.WaterMovementSpeed, track.LavaMovementSpeed,
            track.HoneyMovementSpeed, track.ShimmerMovementSpeed, track.NoGravity, track.NoTileCollide,
            track.Wet, track.LavaWet, track.HoneyWet, track.ShimmerWet, track.MeanError, track.ErrorSamples);
    }

    /// <summary>
    /// Install a track read back from a snapshot. The live body it extends must already stand at the
    /// snapshot's position with the snapshot's velocity: the next <see cref="Predict"/> call re-observes,
    /// sees the same position and velocity at the restored game tick, and extends this history rather than
    /// starting a new one — which is what makes the audit's forecasts the live decision's forecasts.
    /// </summary>
    public static void AssumeTrack(NPC subject, ExportedTrack exported)
    {
        var track = new Track
        {
            Subject = subject,
            Type = exported.Type,
            Tick = exported.Tick,
            Position = exported.Position,
            Velocity = exported.Velocity,
            Acceleration = exported.Acceleration,
            Gravity = exported.Gravity,
            MaxFallSpeed = exported.MaxFallSpeed,
            WaterMovementSpeed = exported.WaterSpeed,
            LavaMovementSpeed = exported.LavaSpeed,
            HoneyMovementSpeed = exported.HoneySpeed,
            ShimmerMovementSpeed = exported.ShimmerSpeed,
            NoGravity = exported.NoGravity,
            NoTileCollide = exported.NoTileCollide,
            Wet = exported.Wet,
            LavaWet = exported.LavaWet,
            HoneyWet = exported.HoneyWet,
            ShimmerWet = exported.ShimmerWet,
            MeanError = exported.MeanError,
            ErrorSamples = exported.ErrorSamples,
        };
        track.ForecastPosition = exported.Position;
        track.ForecastVelocity = exported.Velocity;
        track.Centres.Add(subject.Center);
        tracks[exported.Slot] = track;
    }
}
