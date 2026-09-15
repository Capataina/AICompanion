#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// How many flights a type keeps, the newest kept, and every fact the fit reads comes from them —
    /// onset, pairs and plateau alike — so all of it ages together. Unbounded, the pairs grew for the
    /// life of the session and the fit, a median over every pair, was measured at twelve times its early
    /// cost after a few thousand shots, paid inside the projectile hooks on the main thread; and an onset
    /// kept as the earliest ever seen, or a cap as the fastest fall ever seen, would have pinned a type to
    /// what it did an hour ago however its mod changed it since. Eight flights is more evidence than a
    /// median needs and few enough that a changed projectile is re-learned from what it does now.
    /// </summary>
    public const int MaxFlightsKept = 8;

    /// <summary>Two velocities closer than this are the same velocity; the game's arithmetic is single precision.</summary>
    private const float SameVelocity = 1e-4f;

    private sealed class Watch
    {
        public int Type;
        public readonly List<Vector2> Velocities = new();
    }

    /// <summary>One watched flight, reduced to what the fit reads.</summary>
    private sealed class Flight
    {
        /// <summary>The AI step at which the velocity first changed, or -1 for a flight watched to its full length with no change.</summary>
        public int Onset = -1;
        /// <summary>Consecutive post-onset velocities, (before, after).</summary>
        public readonly List<(Vector2 Before, Vector2 After)> Pairs = new();
        /// <summary>The fastest fall this flight held steady at, or zero if it never reached a cap.</summary>
        public float Plateau;
    }

    /// <summary>The last <see cref="MaxFlightsKept"/> flights of a type; every derived fact is read across them and nothing outlives the ring.</summary>
    private sealed class Record
    {
        public readonly List<Flight> Flights = new();
        /// <summary>The earliest step at which any kept flight changed velocity, or none yet.</summary>
        public int Onset => Flights.Where(f => f.Onset >= 0).Select(f => f.Onset).DefaultIfEmpty(int.MaxValue).Min();
        public IEnumerable<(Vector2 Before, Vector2 After)> Pairs => Flights.SelectMany(f => f.Pairs);
        public int PairCount => Flights.Sum(f => f.Pairs.Count);
        /// <summary>Kept flights watched to the full sample count with no change at all.</summary>
        public int StraightFlights => Flights.Count(f => f.Onset < 0);
        /// <summary>The fastest fall any kept flight held steady at.</summary>
        public float Plateau => Flights.Select(f => f.Plateau).DefaultIfEmpty(0f).Max();
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
        => records.TryGetValue(projectileType, out Record? record) ? record.PairCount : 0;

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
        var flight = new Flight { Onset = onset };
        if (onset < 0)
        {
            // Only a flight watched to its full length says "straight"; one that struck something
            // on its third tick says nothing about gravity that had not started yet, and is not kept.
            if (v.Count <= SamplesPerShot) return;
        }
        else
        {
            for (int k = onset; k < v.Count; k++)
            {
                // A pair whose fall did not change past a positive speed is at the cap; it names the cap
                // and is kept out of the gravity estimate, which would otherwise read a zero.
                if (v[k].Y > 0f && MathF.Abs(v[k].Y - v[k - 1].Y) <= SameVelocity)
                {
                    flight.Plateau = MathF.Max(flight.Plateau, v[k].Y);
                    continue;
                }
                flight.Pairs.Add((v[k - 1], v[k]));
            }
        }
        record.Flights.Add(flight);
        if (record.Flights.Count > MaxFlightsKept)
            record.Flights.RemoveRange(0, record.Flights.Count - MaxFlightsKept);
        Fit(watch.Type, record);
    }

    /// <summary>
    /// Whether the aimer can fly this motion at all. The model falls under a non-negative gravity from an
    /// onset and damps the horizontal by a ratio in (0, 1]; a projectile that rises, homes, bounces or
    /// steers fits none of that, and a fit taken from it is a number the solver cannot fly. Such a fit
    /// must not become the type's motion: with it in place no shot solves, no shot is fired, no flight
    /// is watched, and the type is dead for the session with nothing left that could correct it.
    /// </summary>
    public static bool Flyable(LearnedMotion motion)
        // Of these, only the gravity sign and the finiteness can fail a motion the fit produces: the fit
        // clamps drag into [0, 1] and derives the cap from a plateau or the game's sixteen. The drag and
        // cap bounds are here for a motion handed in through Assume, which a fixture may make anything.
        => float.IsFinite(motion.Gravity) && motion.Gravity >= 0f
            && float.IsFinite(motion.HorizontalDrag) && motion.HorizontalDrag > 0f && motion.HorizontalDrag <= 1f
            && float.IsFinite(motion.MaxFallSpeed) && motion.MaxFallSpeed > 0f;

    /// <summary>Types whose observed flight fits nothing the aimer can fly, kept so a slot can say why the item is refused.</summary>
    private static readonly HashSet<int> unfittable = new();

    /// <summary>Whether this type's observed flight has been found to fit nothing the aimer can fly; its prior stands.</summary>
    public static bool Unfittable(int projectileType) => unfittable.Contains(projectileType);

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
        if (record.PairCount < MinPairsToFit) return;
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
        var motion = new LearnedMotion(record.Onset, gravity, drag, cap);
        if (!Flyable(motion))
        {
            // The flight fits nothing the aimer can fly: the prior stands, the type is named unfittable,
            // and the next flight is still watched, so a mod that changes the projectile is not held to
            // what it did before.
            learned.Remove(type);
            unfittable.Add(type);
            return;
        }
        unfittable.Remove(type);
        learned[type] = motion;
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
        unfittable.Clear();
    }
}
