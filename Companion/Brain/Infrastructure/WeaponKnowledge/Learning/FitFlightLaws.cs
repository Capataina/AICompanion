#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

/// <summary>
/// Flight laws from closed traces: multi-start forward stagewise selection with a BIC penalty. Each start fits
/// one term first — drag, homing, steering or return — then adds whichever unused term most improves the penalised
/// score until none does, and the best start wins. The multiple starts are load-bearing, not belt and braces:
/// drag fitted first on a turning flight absorbs the turns into per-axis ratios that no later term can undo, so a
/// homing flight needs a homing-first start the way a falling one needs its gravity. Linear terms fit by medians
/// rather than the plan's least squares, because one sample taken as a projectile struck something must not drag
/// the answer, which is the arc learner's proven robustness kept.
///
/// <para>Only use samples teach, and only unmodified ones: children never teach a law (a retargeting child would
/// bend its type's gravity) and a trace spawned under a modifier is excluded whole (row K7), because a value that
/// absorbs what the companion itself changed is wrong the moment the modifier changes. Diffs spanning a wall
/// contact are excluded — they hold the reflection, not the flight — and settled samples never enter the fit:
/// their velocities are collision-modified, so they anchor the audit's trace error and the overlay's true path
/// instead of the dynamics.</para>
///
/// <para>Drag carries an onset the plan's struct does not name, because the throwing knife's drag starts with its
/// gravity on the twentieth update and a global drag cannot match it tick for tick (row K1): without the onset
/// the fit splits the difference and drifts a hundred pixels over a flight.</para>
/// </summary>
public static class FitFlightLaws
{
    /// <summary>Fewest velocity diffs that fit anything; below this the default law stands.</summary>
    public const int MinDiffsToFit = 6;

    /// <summary>Fewest diffs behind a term's parameters before the standard-error gate can pass it.</summary>
    public const int MinDiffsPerTerm = 8;

    /// <summary>A term whose interquartile spread exceeds this share of its median is not confident and stays out of the law.</summary>
    public const float MaxRelativeSpread = 0.5f;

    /// <summary>Mean error per update above which a law is unpredictable: still fired, at the intercept, valued by outcomes alone.</summary>
    public const float UnpredictableResidualBound = 0.5f;

    private sealed class Diff
    {
        public int Update;
        public Vector2 Before;
        public Vector2 After;
        public Vector2 ToNpc;
        public Vector2 ToAim;
        public Vector2 ToOwner;
        public bool Wet;
    }

    private sealed class Terms
    {
        public DragTerm Drag = new(1f, 1f, 0);
        public GravityTerm? Gravity;
        public SpeedChangeTerm? SpeedChange;
        public HomingTerm? Homing;
        public SteerToAimTerm? SteerToAim;
        public ReturnToOwnerTerm? Return;
    }

    private static readonly Dictionary<int, FlightLaw> laws = new();

    public static FlightLaw LawFor(int projectileType)
        => laws.TryGetValue(projectileType, out FlightLaw? law) ? law : FlightLaw.Default(projectileType);

    /// <summary>Every fitted law by projectile type, for the persistence codec to read.</summary>
    public static IReadOnlyDictionary<int, FlightLaw> Laws => laws;

    /// <summary>One trace closed: refit its type unless the trace is a child or spawned under a modifier.</summary>
    public static void Notice(Recording.FlightTrace trace)
    {
        // Player flights only (row K0): the companion's shots are aimed by the law, so fitting on them
        // would teach the law its own aim back. Modified traces are excluded beside it, as the plan's
        // "exclude or normalise" rule asks; every modified trace is the companion's, so the shooter
        // gate subsumes it, and it stays as the belt to the gate's braces.
        if (!trace.UseSample || trace.Shooter != Recording.Shooter.Player || !trace.Modifiers.IsNone) return;
        Refit(trace.ProjectileType);
    }

    /// <summary>Refit a type from its closed use samples. Public so a fixture can fit synthetic traces.</summary>
    public static void Refit(int projectileType)
    {
        List<Diff> diffs = Collect(projectileType);
        FlightLaw current = LawFor(projectileType);
        if (diffs.Count < MinDiffsToFit) return;
        Terms terms = new() { Drag = FitDrag(diffs, new Terms()) };
        double bestScore = double.MaxValue;
        foreach (Terms start in Starts(diffs))
        {
            Terms grown = start;
            double baseline = Score(diffs, grown, ParamCount(grown));
            bool improved;
            do
            {
                improved = false;
                Terms? best = null;
                double bestStep = baseline;
                foreach (Terms candidate in Candidates(diffs, grown))
                {
                    double score = Score(diffs, candidate, ParamCount(candidate));
                    if (score < bestStep) { bestStep = score; best = candidate; }
                }
                if (best != null) { grown = best; baseline = bestStep; improved = true; }
            } while (improved);
            if (baseline < bestScore) { bestScore = baseline; terms = grown; }
        }
        GateUncertainTerms(diffs, terms);
        float residual = (float)Math.Sqrt(RawSumSquares(diffs, terms) / Math.Max(1, diffs.Count));
        bool predictable = residual <= UnpredictableResidualBound;
        FlightLaw law = new(projectileType, current.Revision, current.UpdatesPerTick, current.LifetimeUpdates,
            terms.Gravity, terms.Drag, terms.SpeedChange, terms.Homing, terms.SteerToAim, terms.Return,
            LearnWallResponses.ResponseFor(projectileType), residual, diffs.Count, predictable);
        if (laws.TryGetValue(projectileType, out FlightLaw? had) && SameLaw(had, law))
        {
            laws[projectileType] = had with { ResidualPerUpdate = residual, Evidence = diffs.Count, Predictable = predictable };
            return;
        }
        laws[projectileType] = law with { Revision = current.Revision + 1 };
        KnowledgeRevision.Bump();
        Diagnostics.GodsEyeEvents.RecordFlightLaw(projectileType, current.Revision + 1, Describe(law), residual, diffs.Count, predictable);
    }

    private static List<Diff> Collect(int projectileType)
    {
        var diffs = new List<Diff>();
        foreach (Recording.FlightTrace trace in Recording.RecordProjectileFlights.ClosedFor(projectileType))
        {
            // The shooter gate lives here too, not only at Notice: a player's close refits over every
            // closed trace of the type, and the companion's flights among them must not join the diffs.
            if (!trace.UseSample || trace.Shooter != Recording.Shooter.Player || !trace.Modifiers.IsNone) continue;
            Recording.FlightStep? before = null;
            foreach (Recording.FlightStep step in trace.Steps)
            {
                if (step.Settled) continue;
                if (before is { } prior && !SpansWall(trace, prior.Update, step.Update))
                {
                    diffs.Add(new Diff
                    {
                        Update = step.Update,
                        Before = prior.Velocity,
                        After = step.Velocity,
                        ToNpc = step.ToNearestEligibleNpc,
                        ToAim = step.ToAimPoint,
                        ToOwner = step.ToOwner,
                        Wet = step.Wet,
                    });
                }
                before = step;
            }
        }
        return diffs;
    }

    private static bool SpansWall(Recording.FlightTrace trace, int beforeUpdate, int afterUpdate)
    {
        foreach (Recording.WallContact wall in trace.Walls)
            if (wall.Update > beforeUpdate && wall.Update <= afterUpdate) return true;
        return false;
    }

    private static Vector2 Predict(in Terms terms, Diff diff)
    {
        var law = new FlightLaw(0, 0, 1, 0, terms.Gravity, terms.Drag, terms.SpeedChange, terms.Homing,
            terms.SteerToAim, terms.Return, WallResponse.Unknown(0), 0f, 0, true);
        return FlightLaw.AIVelocity(in law, diff.Before, diff.Update, diff.ToNpc, diff.ToAim, diff.ToOwner, diff.Wet);
    }

    private static double RawSumSquares(List<Diff> diffs, Terms terms)
    {
        double sum = 0;
        foreach (Diff diff in diffs)
        {
            Vector2 error = Predict(terms, diff) - diff.After;
            sum += error.LengthSquared();
        }
        return sum;
    }

    private static double Score(List<Diff> diffs, Terms terms, int params_)
    {
        double rss = Math.Max(1e-9, RawSumSquares(diffs, terms));
        return diffs.Count * Math.Log(rss / diffs.Count) + params_ * Math.Log(diffs.Count);
    }

    private static int ParamCount(Terms terms)
    {
        int count = 3;
        if (terms.Gravity != null) count += 3;
        if (terms.SpeedChange != null) count += 4;
        if (terms.Homing != null) count += 4;
        if (terms.SteerToAim != null) count += 2;
        if (terms.Return != null) count += 3;
        return count;
    }

    private static IEnumerable<Terms> Starts(List<Diff> diffs)
    {
        var empty = new Terms();
        yield return new Terms { Drag = FitDrag(diffs, empty) };
        if (FitHoming(diffs, empty) is { } homing)
            yield return With(new Terms { Drag = FitDrag(diffs, With(empty, homing)) }, homing);
        if (FitSteerToAim(diffs, empty) is { } steer)
            yield return With(new Terms { Drag = FitDrag(diffs, With(empty, steer)) }, steer);
        if (FitReturn(diffs, empty) is { } ret)
            yield return With(new Terms { Drag = FitDrag(diffs, With(empty, ret)) }, ret);
    }

    private static IEnumerable<Terms> Candidates(List<Diff> diffs, Terms terms)
    {
        if (terms.Gravity == null && FitGravity(diffs, terms) is { } gravity)
            yield return With(terms, gravity);
        if (terms.SpeedChange == null && FitSpeedChange(diffs, terms) is { } speed)
            yield return With(terms, speed);
        if (terms.Homing == null && FitHoming(diffs, terms) is { } homing)
            yield return With(terms, homing);
        if (terms.SteerToAim == null && FitSteerToAim(diffs, terms) is { } steer)
            yield return With(terms, steer);
        if (terms.Return == null && FitReturn(diffs, terms) is { } ret)
            yield return With(terms, ret);
    }

    private static Terms With(Terms terms, GravityTerm gravity) { Terms copy = Copy(terms); copy.Gravity = gravity; return copy; }
    private static Terms With(Terms terms, SpeedChangeTerm speed) { Terms copy = Copy(terms); copy.SpeedChange = speed; return copy; }
    private static Terms With(Terms terms, HomingTerm homing) { Terms copy = Copy(terms); copy.Homing = homing; return copy; }
    private static Terms With(Terms terms, SteerToAimTerm steer) { Terms copy = Copy(terms); copy.SteerToAim = steer; return copy; }
    private static Terms With(Terms terms, ReturnToOwnerTerm ret) { Terms copy = Copy(terms); copy.Return = ret; return copy; }

    private static Terms Copy(Terms terms) => new()
    {
        Drag = terms.Drag,
        Gravity = terms.Gravity,
        SpeedChange = terms.SpeedChange,
        Homing = terms.Homing,
        SteerToAim = terms.SteerToAim,
        Return = terms.Return,
    };

    private static int MaxUpdate(List<Diff> diffs)
    {
        int max = 0;
        foreach (Diff diff in diffs)
            max = Math.Max(max, diff.Update);
        return max;
    }

    private static IEnumerable<int> OnsetGrid(int maxUpdate)
    {
        yield return 0;
        for (int onset = 4; onset <= Math.Min(60, maxUpdate); onset += 4)
            yield return onset;
    }

    private static DragTerm FitDrag(List<Diff> diffs, Terms baseline)
    {
        DragTerm best = new(1f, 1f, 0);
        double bestRss = double.MaxValue;
        foreach (int onset in OnsetGrid(MaxUpdate(diffs)))
        {
            var hx = new List<float>();
            var hy = new List<float>();
            foreach (Diff diff in diffs)
            {
                if (diff.Update < onset) continue;
                Vector2 predicted = Predict(baseline, diff);
                if (MathF.Abs(predicted.X) > 0.05f) hx.Add(diff.After.X / predicted.X);
                if (MathF.Abs(predicted.Y) > 0.05f) hy.Add(diff.After.Y / predicted.Y);
            }
            // Pre-onset diffs must already be still under the baseline: an onset that starts the drag where
            // the residual was already changing reads the change as drag, so only stillness before it admits it.
            bool stillBefore = true;
            foreach (Diff diff in diffs)
            {
                if (diff.Update < onset && Vector2.Distance(diff.After, Predict(baseline, diff)) > 1e-3f) { stillBefore = false; break; }
            }
            if (!stillBefore) continue;
            var candidate = new DragTerm(
                Math.Clamp(hx.Count > 0 ? Median(hx) : 1f, 0f, 1f),
                Math.Clamp(hy.Count > 0 ? Median(hy) : 1f, 0f, 1f), onset);
            Terms withDrag = Copy(baseline);
            withDrag.Drag = candidate;
            double rss = RawSumSquares(diffs, withDrag);
            if (rss < bestRss) { bestRss = rss; best = candidate; }
        }
        DragTerm coarse = best;
        return RefineOnset(diffs, baseline, coarse, (t, o) => { Terms copy = Copy(t); copy.Drag = coarse with { OnsetUpdate = o }; return copy; });
    }

    private static GravityTerm? FitGravity(List<Diff> diffs, Terms terms)
    {
        GravityTerm? best = null;
        double bestRss = double.MaxValue;
        foreach (int onset in OnsetGrid(MaxUpdate(diffs)))
        {
            var falls = new List<float>();
            float cap = 0f;
            foreach (Diff diff in diffs)
            {
                if (diff.Update < onset) continue;
                Vector2 predicted = Predict(With(terms, new GravityTerm(onset, 0f, float.MaxValue)), diff);
                float change = diff.After.Y - predicted.Y;
                if (diff.Before.Y > 0f && MathF.Abs(change) <= 1e-4f)
                    cap = MathF.Max(cap, diff.After.Y);
                else
                    falls.Add(diff.Wet ? change / 2.5f : change);
            }
            if (falls.Count < MinDiffsPerTerm) continue;
            float accel = Median(falls);
            if (MathF.Abs(accel) < 1e-4f) continue;
            var candidate = new GravityTerm(onset, accel, cap > 0f ? cap : 16f);
            double rss = RawSumSquares(diffs, With(terms, candidate));
            if (rss < bestRss) { bestRss = rss; best = candidate; }
        }
        return best == null ? null : RefineOnset(diffs, terms, best.Value, (t, o) => With(t, best.Value with { OnsetUpdate = o }));
    }

    private static SpeedChangeTerm? FitSpeedChange(List<Diff> diffs, Terms terms)
    {
        SpeedChangeTerm? best = null;
        double bestRss = double.MaxValue;
        foreach (int onset in OnsetGrid(MaxUpdate(diffs)))
        {
            var ratios = new List<float>();
            float min = float.MaxValue, max = 0f;
            foreach (Diff diff in diffs)
            {
                if (diff.Update < onset || diff.Before == Vector2.Zero) continue;
                Vector2 predicted = Predict(terms, diff);
                if (predicted == Vector2.Zero) continue;
                ratios.Add(diff.After.Length() / predicted.Length());
                min = MathF.Min(min, diff.After.Length());
                max = MathF.Max(max, diff.After.Length());
            }
            if (ratios.Count < MinDiffsPerTerm) continue;
            float ratio = Median(ratios);
            if (MathF.Abs(ratio - 1f) < 1e-3f) continue;
            var candidate = new SpeedChangeTerm(onset, ratio, min == float.MaxValue ? 0f : min, max);
            double rss = RawSumSquares(diffs, With(terms, candidate));
            if (rss < bestRss) { bestRss = rss; best = candidate; }
        }
        return best == null ? null : RefineOnset(diffs, terms, best.Value, (t, o) => With(t, best.Value with { OnsetUpdate = o }));
    }

    private static HomingTerm? FitHoming(List<Diff> diffs, Terms terms)
    {
        HomingTerm? best = null;
        double bestRss = double.MaxValue;
        foreach (int onset in OnsetGrid(MaxUpdate(diffs)))
        {
            foreach (float radius in new[] { 150f, 300f, 500f, 800f, 1200f, 2000f })
            {
                var blends = new List<float>();
                var speeds = new List<float>();
                var ordered = new List<Diff>();
                foreach (Diff diff in diffs)
                {
                    if (diff.Update < onset || diff.ToNpc == Vector2.Zero || diff.ToNpc.Length() > radius) continue;
                    ordered.Add(diff);
                }
                if (ordered.Count < MinDiffsPerTerm) continue;
                // The homing speed latches the way the game latches it: the launch speed, read off the first
                // active updates. A median over the whole flight reads the spiral instead — a bullet orbiting
                // its body decays toward zero, and a speed fitted there explains nothing.
                ordered.Sort((a, b) => a.Update.CompareTo(b.Update));
                for (int i = 0; i < Math.Min(8, ordered.Count); i++)
                    speeds.Add(ordered[i].Before.Length());
                float speed = Median(speeds);
                foreach (Diff diff in diffs)
                {
                    if (diff.Update < onset || diff.ToNpc == Vector2.Zero || diff.ToNpc.Length() > radius) continue;
                    Vector2 predicted = Predict(terms, diff);
                    Vector2 want = Vector2.Normalize(diff.ToNpc) * speed;
                    Vector2 span = want - predicted;
                    if (span.LengthSquared() < 1e-6f) continue;
                    Vector2 actual = diff.After - predicted;
                    blends.Add(Math.Clamp(Vector2.Dot(actual, span) / span.LengthSquared(), 0f, 1f));
                }
                if (blends.Count < MinDiffsPerTerm) continue;
                float blend = Median(blends);
                if (blend < 0.01f) continue;
                var candidate = new HomingTerm(onset, radius, HomingAnchor.NearestNpc, blend, speed);
                double rss = RawSumSquares(diffs, With(terms, candidate));
                if (rss < bestRss) { bestRss = rss; best = candidate; }
            }
        }
        return best == null ? null : RefineOnset(diffs, terms, best.Value, (t, o) => With(t, best.Value with { OnsetUpdate = o }));
    }

    private static SteerToAimTerm? FitSteerToAim(List<Diff> diffs, Terms terms)
    {
        SteerToAimTerm? best = null;
        double bestRss = double.MaxValue;
        foreach (int onset in OnsetGrid(MaxUpdate(diffs)))
        {
            var turns = new List<float>();
            int toward = 0, total = 0;
            foreach (Diff diff in diffs)
            {
                if (diff.Update < onset || diff.ToAim == Vector2.Zero || diff.Before == Vector2.Zero) continue;
                Vector2 predicted = Predict(terms, diff);
                if (predicted == Vector2.Zero || diff.After == Vector2.Zero) continue;
                total++;
                float need = MathF.Abs(MathHelper.WrapAngle(diff.ToAim.ToRotation() - predicted.ToRotation()));
                float left = MathF.Abs(MathHelper.WrapAngle(diff.ToAim.ToRotation() - diff.After.ToRotation()));
                if (left < need - 1e-4f)
                {
                    toward++;
                    turns.Add(MathF.Abs(MathHelper.WrapAngle(diff.After.ToRotation() - predicted.ToRotation())));
                }
            }
            if (total < MinDiffsPerTerm || toward < total * 0.6f || turns.Count == 0) continue;
            float cap = Median(turns);
            if (cap < 1e-3f) continue;
            var candidate = new SteerToAimTerm(onset, cap);
            double rss = RawSumSquares(diffs, With(terms, candidate));
            if (rss < bestRss) { bestRss = rss; best = candidate; }
        }
        return best == null ? null : RefineOnset(diffs, terms, best.Value, (t, o) => With(t, best.Value with { OnsetUpdate = o }));
    }

    private static ReturnToOwnerTerm? FitReturn(List<Diff> diffs, Terms terms)
    {
        ReturnToOwnerTerm? best = null;
        double bestRss = double.MaxValue;
        foreach (int onset in OnsetGrid(MaxUpdate(diffs)))
        {
            var pulls = new List<float>();
            float max = 0f;
            foreach (Diff diff in diffs)
            {
                if (diff.Update < onset || diff.ToOwner == Vector2.Zero) continue;
                Vector2 predicted = Predict(terms, diff);
                pulls.Add(Vector2.Dot(diff.After - predicted, Vector2.Normalize(diff.ToOwner)));
                max = MathF.Max(max, diff.After.Length());
            }
            if (pulls.Count < MinDiffsPerTerm) continue;
            float accel = Median(pulls);
            if (accel < 1e-3f) continue;
            var candidate = new ReturnToOwnerTerm(onset, accel, max);
            double rss = RawSumSquares(diffs, With(terms, candidate));
            if (rss < bestRss) { bestRss = rss; best = candidate; }
        }
        return best == null ? null : RefineOnset(diffs, terms, best.Value, (t, o) => With(t, best.Value with { TurnUpdate = o }));
    }

    private static T RefineOnset<T>(List<Diff> diffs, Terms terms, T best, Func<Terms, int, Terms> at) where T : struct
    {
        int onset = best switch
        {
            GravityTerm g => g.OnsetUpdate,
            DragTerm d => d.OnsetUpdate,
            SpeedChangeTerm s => s.OnsetUpdate,
            HomingTerm h => h.OnsetUpdate,
            SteerToAimTerm s => s.OnsetUpdate,
            ReturnToOwnerTerm r => r.TurnUpdate,
            _ => 0,
        };
        // The coarse grid steps in fours; the refinement walks the three updates each side, but the fit that
        // chose this onset compared full laws, so the refinement compares the same full laws, not the term alone.
        T refined = best;
        double bestRss = RawSumSquares(diffs, at(terms, onset));
        foreach (int near in new[] { -3, -2, -1, 1, 2, 3 })
        {
            int candidate = onset + near;
            if (candidate < 0 || candidate > MaxUpdate(diffs)) continue;
            double rss = RawSumSquares(diffs, at(terms, candidate));
            if (rss < bestRss - 1e-9)
            {
                bestRss = rss;
                refined = At<T>(best, candidate);
            }
        }
        return refined;
    }

    private static T At<T>(T best, int onset) where T : struct => best switch
    {
        DragTerm d => (T)(object)(d with { OnsetUpdate = onset }),
        GravityTerm g => (T)(object)(g with { OnsetUpdate = onset }),
        SpeedChangeTerm s => (T)(object)(s with { OnsetUpdate = onset }),
        HomingTerm h => (T)(object)(h with { OnsetUpdate = onset }),
        SteerToAimTerm s => (T)(object)(s with { OnsetUpdate = onset }),
        ReturnToOwnerTerm r => (T)(object)(r with { TurnUpdate = onset }),
        _ => best,
    };

    /// <summary>
    /// The standard-error gate: a term stays only when enough diffs back it and its values cluster. A term that
    /// fit noise — a wide spread around its median — leaves the law, which flies the confident terms and carries
    /// the unexplained motion in its residual instead.
    /// </summary>
    private static void GateUncertainTerms(List<Diff> diffs, Terms terms)
    {
        if (terms.Gravity is { } gravity && !Confident(GravityValues(diffs, terms, gravity)))
            terms.Gravity = null;
        if (terms.SpeedChange is { } speed && !Confident(SpeedValues(diffs, terms, speed)))
            terms.SpeedChange = null;
        if (terms.Homing is { } homing && !Confident(HomingValues(diffs, terms, homing)))
            terms.Homing = null;
        if (terms.SteerToAim is { } steer && !Confident(SteerValues(diffs, terms, steer)))
            terms.SteerToAim = null;
        if (terms.Return is { } ret && !Confident(ReturnValues(diffs, terms, ret)))
            terms.Return = null;
    }

    private static bool Confident(List<float> values)
    {
        if (values.Count < MinDiffsPerTerm) return false;
        float median = Median(values);
        values.Sort();
        float q1 = values[values.Count / 4];
        float q3 = values[values.Count * 3 / 4];
        float spread = q3 - q1;
        return MathF.Abs(median) < 1e-4f ? spread < 0.01f : spread / MathF.Abs(median) < MaxRelativeSpread;
    }

    private static List<float> GravityValues(List<Diff> diffs, Terms terms, GravityTerm gravity)
    {
        var bare = Copy(terms);
        bare.Gravity = null;
        var values = new List<float>();
        foreach (Diff diff in diffs)
        {
            if (diff.Update < gravity.OnsetUpdate) continue;
            Vector2 predicted = Predict(bare, diff);
            float change = diff.After.Y - predicted.Y;
            if (diff.Before.Y > 0f && MathF.Abs(change) <= 1e-4f) continue;
            values.Add(diff.Wet ? change / 2.5f : change);
        }
        return values;
    }

    private static List<float> SpeedValues(List<Diff> diffs, Terms terms, SpeedChangeTerm speed)
    {
        var bare = Copy(terms);
        bare.SpeedChange = null;
        var values = new List<float>();
        foreach (Diff diff in diffs)
        {
            if (diff.Update < speed.OnsetUpdate || diff.Before == Vector2.Zero) continue;
            Vector2 predicted = Predict(bare, diff);
            if (predicted == Vector2.Zero) continue;
            values.Add(diff.After.Length() / predicted.Length());
        }
        return values;
    }

    private static List<float> HomingValues(List<Diff> diffs, Terms terms, HomingTerm homing)
    {
        var bare = Copy(terms);
        bare.Homing = null;
        var values = new List<float>();
        foreach (Diff diff in diffs)
        {
            if (diff.Update < homing.OnsetUpdate || diff.ToNpc == Vector2.Zero || diff.ToNpc.Length() > homing.Radius) continue;
            Vector2 predicted = Predict(bare, diff);
            Vector2 want = Vector2.Normalize(diff.ToNpc) * homing.Speed;
            Vector2 span = want - predicted;
            if (span.LengthSquared() < 1e-6f) continue;
            values.Add(Vector2.Dot(diff.After - predicted, span) / span.LengthSquared());
        }
        return values;
    }

    private static List<float> SteerValues(List<Diff> diffs, Terms terms, SteerToAimTerm steer)
    {
        var bare = Copy(terms);
        bare.SteerToAim = null;
        var values = new List<float>();
        foreach (Diff diff in diffs)
        {
            if (diff.Update < steer.OnsetUpdate || diff.ToAim == Vector2.Zero || diff.Before == Vector2.Zero) continue;
            Vector2 predicted = Predict(bare, diff);
            if (predicted == Vector2.Zero || diff.After == Vector2.Zero) continue;
            values.Add(MathF.Abs(MathHelper.WrapAngle(diff.After.ToRotation() - predicted.ToRotation())));
        }
        return values;
    }

    private static List<float> ReturnValues(List<Diff> diffs, Terms terms, ReturnToOwnerTerm ret)
    {
        var bare = Copy(terms);
        bare.Return = null;
        var values = new List<float>();
        foreach (Diff diff in diffs)
        {
            if (diff.Update < ret.TurnUpdate || diff.ToOwner == Vector2.Zero) continue;
            Vector2 predicted = Predict(bare, diff);
            values.Add(Vector2.Dot(diff.After - predicted, Vector2.Normalize(diff.ToOwner)));
        }
        return values;
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2f;
    }

    private static bool SameLaw(FlightLaw a, FlightLaw b)
        => Close(a.Gravity?.Acceleration ?? 0f, b.Gravity?.Acceleration ?? 0f)
            && (a.Gravity?.OnsetUpdate ?? -1) == (b.Gravity?.OnsetUpdate ?? -1)
            && Close(a.Drag.Horizontal, b.Drag.Horizontal) && Close(a.Drag.Vertical, b.Drag.Vertical)
            && a.Drag.OnsetUpdate == b.Drag.OnsetUpdate
            && (a.SpeedChange?.OnsetUpdate ?? -1) == (b.SpeedChange?.OnsetUpdate ?? -1)
            && Close(a.SpeedChange?.RatioPerUpdate ?? 1f, b.SpeedChange?.RatioPerUpdate ?? 1f)
            && (a.Homing?.OnsetUpdate ?? -1) == (b.Homing?.OnsetUpdate ?? -1)
            && Close(a.Homing?.Blend ?? 0f, b.Homing?.Blend ?? 0f)
            && (a.SteerToAim?.OnsetUpdate ?? -1) == (b.SteerToAim?.OnsetUpdate ?? -1)
            && (a.Return?.TurnUpdate ?? -1) == (b.Return?.TurnUpdate ?? -1)
            && a.Wall.Kind == b.Wall.Kind;

    private static bool Close(float a, float b) => MathF.Abs(a - b) < 1e-4f;

    private static string Describe(FlightLaw law)
    {
        var parts = new List<string> { $"drag={law.Drag.Horizontal:0.000},{law.Drag.Vertical:0.000}@{law.Drag.OnsetUpdate}" };
        if (law.Gravity is { } gravity) parts.Add($"gravity={gravity.Acceleration:0.000}@{gravity.OnsetUpdate},cap={gravity.MaxFallSpeed:0.0}");
        if (law.SpeedChange is { } speed) parts.Add($"speedx{speed.RatioPerUpdate:0.000}@{speed.OnsetUpdate}");
        if (law.Homing is { } homing) parts.Add($"homing={homing.Blend:0.000},r={homing.Radius:0}@{homing.OnsetUpdate}");
        if (law.SteerToAim is { } steer) parts.Add($"steer={steer.MaxTurnPerUpdate:0.000}@{steer.OnsetUpdate}");
        if (law.Return is { } ret) parts.Add($"return={ret.Acceleration:0.000}@{ret.TurnUpdate}");
        parts.Add($"wall={law.Wall.Kind}");
        return string.Join(";", parts);
    }

    /// <summary>Plant a belief for a type, revision included. A fixture seam for the unknown-arc row's straight guess; the game only ever reads fitted laws and the default.</summary>
    public static void AssumeLaw(int projectileType, FlightLaw law)
    {
        laws[projectileType] = law;
        KnowledgeRevision.Bump();
    }

    public static void Reset()
    {
        laws.Clear();
        KnowledgeRevision.Bump();
    }
}
