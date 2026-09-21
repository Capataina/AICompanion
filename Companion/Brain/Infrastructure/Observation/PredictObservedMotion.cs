using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Shared, terrain-constrained forecast of observed motion; it does not invent an enemy's next AI decision.</summary>
public static class PredictObservedMotion
{
    public const int MaximumForecastTicks = 180;
    /// <summary>
    /// The key the player's own track lives under. Negative on purpose: <see cref="tracks"/> is keyed by
    /// <c>NPC.whoAmI</c>, which is never negative, so the player cannot collide with a hostile however
    /// many slots the world has, and nothing that iterates <see cref="TrackedSlots"/> for enemies can
    /// pick him up by accident.
    ///
    /// He is tracked at all because a course that defends him has to be priced against where he is
    /// going, not where he stands: the contact forecast takes a victim's box per tick, and until this
    /// existed the player's boxes were an explicit unsupported empty and every course in existence
    /// priced his harm at exactly zero.
    /// </summary>
    public const int PlayerSlot = -1;

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

    /// <summary>
    /// The player's own motion history, kept exactly the way a hostile's is so his continuation is
    /// forecast by the same law rather than by a second model that could disagree with it.
    ///
    /// Identity is the <see cref="Player.whoAmI"/> and the slot, not a reference, because this is
    /// singleplayer and <c>Main.LocalPlayer</c> is one long-lived object; what has to be detected is a
    /// gap in observation, which the consecutive-tick test already does. A non-consecutive tick resets
    /// the error history, so a track picked up after a pause reports low confidence rather than a
    /// confident extrapolation across the gap.
    /// </summary>
    public static void Observe(Player player)
    {
        ulong tick = Main.GameUpdateCount;
        if (!tracks.TryGetValue(PlayerSlot, out Track? track))
            tracks[PlayerSlot] = track = new Track();
        if (track.Type == player.whoAmI && track.Tick == tick
            && track.Position == player.position && track.Velocity == player.velocity) return;

        bool consecutive = track.Type == player.whoAmI && tick == track.Tick + 1
            && Vector2.DistanceSquared(player.position, track.Position + track.Velocity) < 64f * 64f;
        if (!consecutive) { track.MeanError = 0f; track.ErrorSamples = 0; }
        if (consecutive && track.Centres.Count > 1)
        {
            float error = Vector2.Distance(player.Center, track.Centres[1]);
            track.MeanError = track.ErrorSamples == 0 ? error : track.MeanError * .8f + error * .2f;
            track.ErrorSamples++;
        }
        Vector2 delta = consecutive ? player.velocity - track.Velocity : Vector2.Zero;
        // A landing, a jump or a grapple is an impulse rather than acceleration to extrapolate for a
        // whole flight, exactly as for a hostile.
        if (MathF.Abs(delta.X) > 1f) delta.X = 0f;
        if (MathF.Abs(delta.Y) > 1f) delta.Y = 0f;
        track.Acceleration = delta;
        // `Subject` stays null: it is the NPC identity, and a player is not one. Every read of it is
        // guarded by a `Subject == npc` test that a null can only fail, which is the correct answer to
        // "is this track the one for that NPC".
        track.Type = player.whoAmI;
        track.Tick = tick;
        track.Position = track.ForecastPosition = player.position;
        track.Velocity = track.ForecastVelocity = player.velocity;
        // Signed by the player's own gravity direction rather than fixed downward. A Gravitation Potion
        // or a Gravity Globe sets `gravDir` to -1 and he falls upward; a track that hard-codes the
        // constant then predicts his path the wrong way for the whole horizon, and the harm forecast
        // prices contact against it at nominal evidence with no discount. This codebase already knows
        // the state is real — `SpoofOwnerInputForShots` branches on exactly this field — so reading it
        // is not speculation about a case nobody hits. Raised by a review of `e63375d`.
        track.Gravity = Player.defaultGravity * player.gravDir;
        track.MaxFallSpeed = player.maxFallSpeed;
        track.WaterMovementSpeed = track.LavaMovementSpeed = track.HoneyMovementSpeed = track.ShimmerMovementSpeed = 1f;
        track.NoGravity = false;
        track.NoTileCollide = false;
        track.Wet = player.wet;
        track.LavaWet = player.lavaWet;
        track.HoneyWet = player.honeyWet;
        track.ShimmerWet = player.shimmerWet;
        track.Centres.Clear();
        track.Centres.Add(player.Center);
        _ = PredictTrack(track, 1, player.width, player.height);
    }

    public static Vector2 Predict(NPC npc, int ticks)
    {
        Observe(npc);
        Track track = tracks[npc.whoAmI];
        return PredictTrack(track, ticks, npc.width, npc.height);
    }

    private static Vector2 PredictTrack(Track track, int ticks, int width, int height,
        Action<Vector2, int, int, bool>? terrainRead = null)
    {
        // Consumers only have evidence for a short forecast. Longer requests retain the last
        // bounded prediction instead of asserting an unobserved enemy policy indefinitely.
        ticks = Math.Clamp(ticks, 0, MaximumForecastTicks);
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
                    if (velocity.Y == track.Gravity) terrainRead?.Invoke(track.ForecastPosition, width, height, true);
                    Vector4 downSlope = Collision.WalkDownSlope(track.ForecastPosition, velocity, width, height, track.Gravity);
                    track.ForecastPosition = new Vector2(downSlope.X, downSlope.Y);
                    velocity = new Vector2(downSlope.Z, downSlope.W);
                }
                Vector2 oldDryVelocity = velocity;
                if (!track.NoTileCollide)
                {
                    terrainRead?.Invoke(track.ForecastPosition, width, height, false);
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
                    terrainRead?.Invoke(track.ForecastPosition, width, height, false);
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
        return new CapturedMotion(ExportTrack(npc.whoAmI)!, npc.width, npc.height);
    }

    public static CapturedMotion RestoreCaptured(ExportedTrack source, int width, int height)
        => new(source, width, height);

    public sealed class CapturedMotion
    {
        private readonly Track track;
        private readonly int width, height;
        private int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        internal CapturedMotion(ExportedTrack source, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            this.width = width; this.height = height;
            track = new Track
            {
                Type = source.Type, Tick = source.Tick, Position = source.Position, Velocity = source.Velocity,
                Acceleration = source.Acceleration, Gravity = source.Gravity, MaxFallSpeed = source.MaxFallSpeed,
                WaterMovementSpeed = source.WaterSpeed, LavaMovementSpeed = source.LavaSpeed,
                HoneyMovementSpeed = source.HoneySpeed, ShimmerMovementSpeed = source.ShimmerSpeed,
                NoGravity = source.NoGravity, NoTileCollide = source.NoTileCollide, Wet = source.Wet,
                LavaWet = source.LavaWet, HoneyWet = source.HoneyWet, ShimmerWet = source.ShimmerWet,
                MeanError = source.MeanError, ErrorSamples = source.ErrorSamples,
                ForecastPosition = source.Position, ForecastVelocity = source.Velocity
            };
            track.Centres.Add(source.Position + new Vector2(width * .5f, height * .5f));
        }
        public int CoveredTicks => track.Centres.Count - 1;
        public IReadOnlyList<Vector2> Samples => track.Centres.AsReadOnly();
        public bool ReadContains(int x, int y) => x >= left && x <= right && y >= top && y <= bottom;
        private void RecordTerrainRead(Vector2 position, int bodyWidth, int bodyHeight, bool downSlope)
        {
            // Terraria.Collision: WalkDownSlope scans the foot row (+4 px) and its
            // successor. TileCollision/SlopeCollision scan a one-tile skirt; tile
            // collision additionally reads the horizontal neighbours of those tiles.
            int l = downSlope ? (int)(position.X / 16f) : (int)(position.X / 16f) - 2;
            int r = (int)((position.X + bodyWidth) / 16f) + (downSlope ? 0 : 2);
            int t = downSlope ? (int)((position.Y + bodyHeight + 4f) / 16f) : (int)(position.Y / 16f) - 1;
            if (downSlope) t = Math.Clamp(t, 0, Main.maxTilesY - 3);
            int b = downSlope ? Math.Clamp(t, 0, Main.maxTilesY - 3) + 1 : (int)((position.Y + bodyHeight) / 16f) + 1;
            left = Math.Min(left, Math.Clamp(l, 0, Main.maxTilesX - 1));
            right = Math.Max(right, Math.Clamp(r, 0, Main.maxTilesX - 1));
            top = Math.Min(top, Math.Clamp(t, 0, Main.maxTilesY - 1));
            bottom = Math.Max(bottom, Math.Clamp(b, 0, Main.maxTilesY - 1));
        }
        public bool Continue(int ticks, DecisionWorkBudget budget)
        {
            if (ticks < 0 || ticks > MaximumForecastTicks) throw new ArgumentOutOfRangeException(nameof(ticks));
            while (CoveredTicks < ticks)
            {
                if (!budget.TrySpend("captured-enemy-motion")) return false;
                _ = PredictTrack(track, CoveredTicks + 1, width, height, RecordTerrainRead);
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
        return MathHelper.Clamp(measured * MathF.Exp(-Math.Clamp(ticks, 0, MaximumForecastTicks) / 90f), .1f, 1f);
    }

    public static int ErrorSamples(NPC npc) { Observe(npc); return tracks[npc.whoAmI].ErrorSamples; }

    /// <summary>
    /// One body's motion track in plain values, for the combat snapshot: the audit restores these so its
    /// forecasts extend the same history the live decision read. The forecast centres are recomputed, never
    /// stored — they follow from the position, velocity, acceleration and physics below.
    /// </summary>
    public sealed record ExportedTrack(int Slot, int Type, ulong Tick,
        [property: JsonConverter(typeof(MotionVectorConverter))] Vector2 Position,
        [property: JsonConverter(typeof(MotionVectorConverter))] Vector2 Velocity,
        [property: JsonConverter(typeof(MotionVectorConverter))] Vector2 Acceleration,
        float Gravity, float MaxFallSpeed, float WaterSpeed, float LavaSpeed,
        float HoneySpeed, float ShimmerSpeed, bool NoGravity, bool NoTileCollide, bool Wet, bool LavaWet,
        bool HoneyWet, bool ShimmerWet, float MeanError, int ErrorSamples);

    /// <summary>Native Vector2 coordinates are fields. The track's wire contract must
    /// preserve them even when its enclosing snapshot uses default serializer options.</summary>
    public sealed class MotionVectorConverter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var value = JsonDocument.ParseValue(ref reader);
            if (!value.RootElement.TryGetProperty("X", out var x) || !value.RootElement.TryGetProperty("Y", out var y)
                || !x.TryGetSingle(out float px) || !y.TryGetSingle(out float py)
                || !float.IsFinite(px) || !float.IsFinite(py))
                throw new JsonException("Motion vectors require finite X and Y coordinates.");
            return new(px, py);
        }
        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject(); writer.WriteNumber("X", value.X); writer.WriteNumber("Y", value.Y); writer.WriteEndObject();
        }
    }

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
