#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.Aiming;

/// <summary>
/// The motion the aimer flies each projectile type with, learned from the companion's own shots.
/// An item says what it fires and how fast, and nothing about how that thing falls, so the motion
/// is not a stat to read: it is a prior for the two vanilla styles whose free flight the decompiled
/// AI states plainly, a straight line at launch speed for everything else, and then whatever the
/// recorded first ticks of the type's own flights fit. Session state, never saved: a new session
/// starts from the priors again, and the first shots of a new projectile are calibration shots
/// that may miss.
///
/// The model is the game's own shape of free flight — some ticks straight, then a constant gravity
/// on the vertical speed and a drag factor on the horizontal, capped — because every gravity style
/// the game has is written that way, and fitting a shape the game does not use would fit its
/// noise. The fit is by medians over consecutive velocity pairs, so one sample taken as the
/// projectile struck something, or a modded projectile's occasional steer, cannot drag the answer.
///
/// Every shot the arsenal spawns is registered here beside its landed-hit registration; the
/// observer in <c>../../../Weapons/TrackLandedHits.cs</c> feeds each post-AI velocity until the
/// watch is full or the projectile dies, and the same slot-reuse discipline applies: a native
/// spawn forgets the slot first, the companion's own shot registers after its spawn has cleared
/// it, and a slot whose type changed under the watch stops being watched. Samples taken in liquid
/// are skipped, because the game multiplies gravity and drag there and the aimer already applies
/// that multiplier itself.
/// </summary>
public static class ProjectileArcs
{
    /// <summary>How many post-launch velocities one shot contributes at most: past both vanilla onsets with room to fit.</summary>
    public const int SamplesPerShot = 45;

    /// <summary>How many consecutive pairs past the onset a fit needs before it replaces the prior.</summary>
    public const int MinPairsToFit = 4;

    /// <summary>Two velocities closer than this are the same velocity; the game's arithmetic is single precision.</summary>
    private const float SameVelocity = 1e-4f;

    private sealed class Watch
    {
        public int Type;
        public readonly List<Vector2> Velocities = new();
    }

    private sealed class Record
    {
        /// <summary>The earliest AI step at which any watched flight of this type changed velocity, or none yet.</summary>
        public int Onset = int.MaxValue;
        /// <summary>Consecutive post-onset velocities, (before, after), across every watched flight.</summary>
        public readonly List<(Vector2 Before, Vector2 After)> Pairs = new();
        /// <summary>Flights watched to the full sample count with no change at all.</summary>
        public int StraightFlights;
        /// <summary>The fastest fall seen, for the cap where a flight reached it.</summary>
        public float Plateau;
    }

    private static readonly Dictionary<int, Watch> watching = new();
    private static readonly Dictionary<int, Record> records = new();
    private static readonly Dictionary<int, LearnedMotion> learned = new();

    /// <summary>The motion the aimer should fly this type with now: learned if it has been, else the prior.</summary>
    public static LearnedMotion MotionFor(int projectileType)
        => learned.TryGetValue(projectileType, out LearnedMotion motion) ? motion : Prior(projectileType);

    /// <summary>What has been learned for this type, or null while the prior still stands.</summary>
    public static LearnedMotion? Learned(int projectileType)
        => learned.TryGetValue(projectileType, out LearnedMotion motion) ? motion : null;

    /// <summary>How many velocity pairs past the onset back this type's fit.</summary>
    public static int Evidence(int projectileType)
        => records.TryGetValue(projectileType, out Record? record) ? record.Pairs.Count : 0;

    /// <summary>
    /// What the game's own AI says about a vanilla style, so a wooden arrow's first shot need not
    /// miss. Both numbers below are read from <c>Terraria.Projectile</c> as decompiled by
    /// <c>Tools/decompile.sh</c>, and the native-match row in <c>Tools/EngineReplay/Combat/VerifyArcLearning.cs</c>
    /// holds them against <c>VanillaAI</c> tick for tick.
    ///
    /// Style 1 with the <c>arrow</c> flag (<c>AI_001</c>): <c>else if (ai[0] >= 15f) { ai[0] = 15f; velocity.Y += 0.1f; }</c>
    /// with no horizontal drag, capped by <c>if (velocity.Y > 16f) velocity.Y = 16f</c>. Style 1 without
    /// the flag — bullets, lasers — takes none of that branch and flies straight.
    ///
    /// Style 2 (<c>AI</c>, the thrown-object branch): <c>num29 = 20; ai[0]++; if (ai[0] >= num29) { velocity.Y += 0.4f; velocity.X *= 0.97f; }</c>
    /// then the same 16 cap.
    ///
    /// Every other style, vanilla or modded, starts straight. A style the companion has not seen fly
    /// is not assumed to fall, because a wrong gravity misses in a way the learner then has to unlearn,
    /// while a straight guess misses in the one way a first observation corrects outright.
    /// </summary>
    public static LearnedMotion Prior(int projectileType)
    {
        if (!ContentSamples.ProjectilesByType.TryGetValue(projectileType, out Projectile? sample))
            return LearnedMotion.Straight;
        if (sample.aiStyle == ProjAIStyleID.Arrow && sample.arrow)
            return new LearnedMotion(GravityStartsAtPhase: 15, Gravity: .1f, HorizontalDrag: 1f, MaxFallSpeed: 16f);
        if (sample.aiStyle == ProjAIStyleID.ThrownProjectile)
            return new LearnedMotion(GravityStartsAtPhase: 20, Gravity: .4f, HorizontalDrag: .97f, MaxFallSpeed: 16f);
        return LearnedMotion.Straight;
    }

    /// <summary>Take this motion as learned, as if the fit had produced it. A fixture uses it to start a type from a straight guess and measure what learning buys.</summary>
    public static void Assume(int projectileType, LearnedMotion motion) => learned[projectileType] = motion;

    /// <summary>A shot the companion just spawned: watch this slot's velocity from the launch on.</summary>
    public static void Register(int projectileSlot, int projectileType, Vector2 launch)
    {
        if (projectileSlot < 0) return;
        var watch = new Watch { Type = projectileType };
        watch.Velocities.Add(launch);
        watching[projectileSlot] = watch;
    }

    /// <summary>A native spawn in this slot: whatever was watched there is gone.</summary>
    public static void Forget(int projectileSlot) => watching.Remove(projectileSlot);

    /// <summary>
    /// One post-AI velocity of a watched projectile. A slot whose type changed under the watch, or
    /// that went inactive, ends the watch; a sample in liquid is skipped without ending it.
    /// </summary>
    public static void Observe(Projectile projectile)
    {
        if (!watching.TryGetValue(projectile.whoAmI, out Watch? watch)) return;
        if (!projectile.active || projectile.type != watch.Type)
        {
            Finish(projectile.whoAmI, watch);
            return;
        }
        if (projectile.wet) return;
        watch.Velocities.Add(projectile.velocity);
        if (watch.Velocities.Count > SamplesPerShot)
            Finish(projectile.whoAmI, watch);
    }

    /// <summary>The watched projectile died; fit what it showed.</summary>
    public static void Retire(int projectileSlot)
    {
        if (watching.TryGetValue(projectileSlot, out Watch? watch))
            Finish(projectileSlot, watch);
    }

    private static void Finish(int slot, Watch watch)
    {
        watching.Remove(slot);
        var v = watch.Velocities;
        if (v.Count < 2) return;
        if (!records.TryGetValue(watch.Type, out Record? record))
            records[watch.Type] = record = new Record();
        int onset = -1;
        for (int k = 1; k < v.Count; k++)
            if (Vector2.Distance(v[k], v[k - 1]) > SameVelocity) { onset = k; break; }
        if (onset < 0)
        {
            // Only a flight watched to its full length says "straight"; one that struck something
            // on its third tick says nothing about gravity that had not started yet.
            if (v.Count > SamplesPerShot) record.StraightFlights++;
            Fit(watch.Type, record);
            return;
        }
        record.Onset = Math.Min(record.Onset, onset);
        for (int k = onset; k < v.Count; k++)
        {
            // A pair whose fall did not change past a positive speed is at the cap; it names the cap
            // and is kept out of the gravity estimate, which would otherwise read a zero.
            if (v[k].Y > 0f && MathF.Abs(v[k].Y - v[k - 1].Y) <= SameVelocity)
            {
                record.Plateau = MathF.Max(record.Plateau, v[k].Y);
                continue;
            }
            record.Pairs.Add((v[k - 1], v[k]));
        }
        Fit(watch.Type, record);
    }

    /// <summary>
    /// Drag from the horizontal ratio, gravity from the vertical difference with the vertical left
    /// undamped, both as medians; the cap from an observed plateau or the game's universal 16.
    /// </summary>
    private static void Fit(int type, Record record)
    {
        if (record.Onset == int.MaxValue)
        {
            if (record.StraightFlights > 0) learned[type] = LearnedMotion.Straight;
            return;
        }
        if (record.Pairs.Count < MinPairsToFit) return;
        var ratios = new List<float>();
        var gravities = new List<float>();
        foreach (var (before, after) in record.Pairs)
        {
            if (MathF.Abs(before.X) > .05f) ratios.Add(after.X / before.X);
            gravities.Add(after.Y - before.Y);
        }
        float drag = ratios.Count == 0 ? 1f : Math.Clamp(Median(ratios), 0f, 1f);
        float gravity = Median(gravities);
        float cap = record.Plateau > 0f ? record.Plateau : LearnedMotion.Straight.MaxFallSpeed;
        learned[type] = new LearnedMotion(record.Onset, gravity, drag, cap);
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2f;
    }

    /// <summary>Forget everything learned and watched, so a fixture measures the priors and the learning rather than the previous case's.</summary>
    public static void Reset()
    {
        watching.Clear();
        records.Clear();
        learned.Clear();
    }
}
