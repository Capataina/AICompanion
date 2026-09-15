#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// What a weapon's attacks actually achieve against what the arsenal's arithmetic predicted, learned online from the
/// companion's own shots, so that weapon choice, target choice, firing position and aim are one decision valued by
/// outcomes rather than by a hand-written proxy for them. The owner ruled on 15 September 2026 that decisions
/// evaluate every option and learn from what their own actions did; this is that ruling applied to the hands.
///
/// <para><b>The prior is today's arithmetic, and the learner only corrects it.</b> The arsenal forecasts a shot from
/// the flight model, the pierce trace, the weapon-effects table and the evaluator; that forecast is the prior mean,
/// and this class learns a multiplicative correction on each forecast hit's damage. With nothing observed the
/// correction is exactly one and no random number is drawn, which is what keeps every forecast bit-identical to the
/// arithmetic until the weapon has actually been used.</para>
///
/// <para><b>The quantity learned is the outcome ratio minus one.</b> After a shot's window closes (its projectile and
/// every descendant dead, or a bound) the realised yield — damage landed per second of use, plus a value per enemy
/// struck — is divided by the same yield the forecast predicted, clamped, and the learner regresses <c>ratio − 1</c>
/// on the shot's context. Three residual forms were weighed. A plain difference is in each weapon's damage units, so
/// a coefficient learned on a staff means something else on a sword and a new weapon cannot borrow any intuition from
/// the scale. A log-ratio is the natural multiplicative form, but a miss is the commonest outcome of an aim-sensitive
/// weapon and a miss has no logarithm, so every miss would need an invented floor that then decides how hard misses
/// are punished. The ratio has neither problem, a miss is simply zero, and its clamp is the same shape the sibling
/// weapon-effects table already uses; subtracting one makes "the forecast was right" the prior mean of zero.</para>
///
/// <para><b>The model is Bayesian linear regression per weapon item, sampled by Thompson sampling.</b> The context is
/// seven numbers, each scaled to roughly unit range: a bias, distance as a share of the weapon's reach, the aim
/// offset from the solver's intercept as a share of the widest candidate, the target's speed relative to the orb,
/// the other hostiles along the shot's lane, the orb's own speed, and whether the target carries a debuff the
/// companion's other weapon applied. Each observation is a rank-one update of the posterior mean and covariance (the
/// Kalman form of Sherman–Morrison, so nothing is ever inverted), the Cholesky factor of the covariance is rebuilt
/// only when the posterior changes, and a decision draws one coefficient vector per weapon per tick that every
/// target, stand and aim candidate in that tick shares — a draw per candidate would turn the comparison into noise.
/// Aim is an input rather than a rule, so a homing weapon whose outcome ignores its aim learns an aim coefficient
/// near zero and a straight arrow learns a negative one, from the same code.</para>
///
/// <para><b>Each enemy type carries a bias shrunk toward the weapon's own.</b> A type's bias starts at zero — the
/// weapon's average — with a prior variance, and is updated on the residual the weapon model leaves after its own
/// update, so a weapon fought against one enemy type puts almost everything into the weapon model and a second type
/// with a different outcome is carried by its bias. That is the usual two-stage approximation to a hierarchical
/// model rather than the exact joint posterior, chosen because the exact form needs a feature per enemy type.</para>
///
/// <para><b>Debuffs are learned as a rate and read as context.</b> For each weapon and enemy type the class counts
/// how many struck bodies came away carrying a buff they did not have, and for how long. The arsenal's evaluator uses
/// that rate to mark a target as debuffed for the rest of its horizon after the first weapon's hit, and prices the
/// second weapon's later hits with the debuffed context — which is how a debuff-then-burst pair is opened with the
/// debuff without an eligibility trace or a written combination rule.</para>
///
/// Session state, never saved; <see cref="Reset"/> exists for the engine replay's per-case reset.
/// </summary>
public static class AttackLearning
{
    public const int FeatureCount = 7;
    public const int Bias = 0, Distance = 1, AimOffset = 2, RelativeSpeed = 3, LaneHostiles = 4, OrbSpeed = 5, DebuffedByOther = 6;

    /// <summary>The widest a realised outcome may read against its forecast; a shot landing four times its prediction is already a different weapon, as the weapon-effects table says of damage.</summary>
    public const float MaxOutcomeRatio = 4f;

    /// <summary>
    /// The relative speed, px per tick, that reads as one on the context scale. A fast walking enemy moves a few pixels a
    /// tick and a flier or a charging boss several times that, so this keeps ordinary targets inside the unit range the
    /// prior variance was chosen for without saturating on the fast ones.
    /// </summary>
    public const float RelativeSpeedScale = 8f;

    private sealed class Model
    {
        public readonly double[] Mean = new double[FeatureCount];
        public readonly double[,] Covariance = new double[FeatureCount, FeatureCount];
        public readonly double[,] Cholesky = new double[FeatureCount, FeatureCount];
        public bool CholeskyValid;
        public int Evidence;
        public readonly Dictionary<int, TypeBias> Types = new();
        public int DrawTick = int.MinValue;
        public readonly double[] Draw = new double[FeatureCount];

        public Model()
        {
            for (int i = 0; i < FeatureCount; i++)
                Covariance[i, i] = Weights.WeaponLearningPriorVariance;
        }
    }

    private sealed class TypeBias
    {
        public double Precision = 1.0 / Weights.WeaponLearningEnemyTypeVariance;
        public double Weighted;
        public int DrawTick = int.MinValue;
        public double Draw;
        public double Mean => Weighted / Precision;
    }

    private sealed class DebuffRecord
    {
        public int Struck;
        public int Applied;
        public long AppliedTicks;
    }

    private static readonly Dictionary<int, Model> models = new();
    private static readonly Dictionary<(int Item, int Npc), DebuffRecord> debuffs = new();
    private static Random random = new(Environment.TickCount);

    /// <summary>Advances whenever anything learned changes.</summary>
    public static int Revision { get; private set; }

    /// <summary>The last factor a forecast took, sampled and at the posterior mean, for the record and the overlay.</summary>
    public static float LastSampledFactor { get; private set; } = 1f;
    public static float LastMeanFactor { get; private set; } = 1f;

    /// <summary>
    /// The context vector for one attack, each input scaled to roughly unit range. Distance and aim are shares of the
    /// weapon's own reach and of the widest aim candidate, so the same coefficient means the same thing on a sword and
    /// on a rifle.
    /// </summary>
    public static float[] Context(float distance, float reach, float aimOffset, float relativeSpeed, int laneHostiles, float orbSpeed, float orbMaxSpeed, bool debuffedByOther)
    {
        float widestAim = Weights.WeaponAimOffsetRadians * Math.Max(1, Weights.WeaponAimOffsetSteps);
        return new[]
        {
            1f,
            Math.Clamp(distance / MathF.Max(1f, reach), 0f, 1.5f),
            Math.Clamp(MathF.Abs(aimOffset) / widestAim, 0f, 1.5f),
            Math.Clamp(relativeSpeed / RelativeSpeedScale, 0f, 3f),
            Math.Clamp(laneHostiles / (float)Arsenal.MaxPierceCounted, 0f, 1f),
            Math.Clamp(orbSpeed / MathF.Max(.01f, orbMaxSpeed), 0f, 1.5f),
            debuffedByOther ? 1f : 0f,
        };
    }

    /// <summary>
    /// The multiplier on a forecast hit's damage for this weapon against this enemy type in this context. Exactly one,
    /// with no draw, while the weapon has no evidence. With <paramref name="explore"/> the coefficients are the tick's
    /// Thompson draw; without it they are the posterior mean, which is what a decision under real danger uses.
    /// </summary>
    public static float Factor(int itemType, int npcType, ReadOnlySpan<float> x, bool explore, int tick)
    {
        if (!models.TryGetValue(itemType, out Model? model) || model.Evidence == 0)
            return 1f;
        double[] theta = explore ? DrawFor(model, tick) : model.Mean;
        double linear = 1.0;
        for (int i = 0; i < FeatureCount; i++) linear += theta[i] * x[i];
        if (model.Types.TryGetValue(npcType, out TypeBias? bias))
            linear += explore ? DrawFor(bias, tick) : bias.Mean;
        else if (explore)
        {
            // An enemy type this weapon has never struck is drawn from the prior, and the draw is kept for the tick like
            // every other: a fresh number per call made the aim candidates of one forecast compete against different noise.
            // The entry holds exactly the prior, so creating it changes nothing Observe will later do with the type.
            model.Types[npcType] = bias = new TypeBias();
            linear += DrawFor(bias, tick);
        }
        float factor = (float)Math.Clamp(linear, 0.0, MaxOutcomeRatio);
        if (explore) LastSampledFactor = factor; else LastMeanFactor = factor;
        return factor;
    }

    /// <summary>
    /// One closed shot: the context it was fired in and its realised yield over the forecast's. The weapon model takes
    /// the outcome less the enemy type's current bias, then the type's bias takes what the updated weapon model still
    /// leaves unexplained, so a single enemy type is carried by the weapon model and a second type by its bias.
    /// </summary>
    public static void Observe(int itemType, int npcType, ReadOnlySpan<float> x, float ratio)
    {
        if (itemType <= 0) return;
        if (!models.TryGetValue(itemType, out Model? model))
            models[itemType] = model = new Model();
        double y = Math.Clamp(ratio, 0f, MaxOutcomeRatio) - 1.0;
        model.Types.TryGetValue(npcType, out TypeBias? bias);
        double noise = Weights.WeaponLearningNoiseVariance;

        Span<double> px = stackalloc double[FeatureCount];
        double s = noise, predicted = 0.0;
        for (int i = 0; i < FeatureCount; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < FeatureCount; j++) sum += model.Covariance[i, j] * x[j];
            px[i] = sum;
            s += x[i] * sum;
            predicted += model.Mean[i] * x[i];
        }
        double error = y - (bias?.Mean ?? 0.0) - predicted;
        for (int i = 0; i < FeatureCount; i++)
            model.Mean[i] += px[i] / s * error;
        for (int i = 0; i < FeatureCount; i++)
            for (int j = 0; j < FeatureCount; j++)
                model.Covariance[i, j] -= px[i] * px[j] / s;
        model.CholeskyValid = false;
        model.DrawTick = int.MinValue;
        model.Evidence++;

        double after = 0.0;
        for (int i = 0; i < FeatureCount; i++) after += model.Mean[i] * x[i];
        if (bias == null) model.Types[npcType] = bias = new TypeBias();
        bias.Precision += 1.0 / noise;
        bias.Weighted += (y - after) / noise;
        bias.DrawTick = int.MinValue;
        Revision++;
    }

    /// <summary>The posterior mean of one coefficient, zero with no evidence.</summary>
    public static float Coefficient(int itemType, int feature)
        => models.TryGetValue(itemType, out Model? model) ? (float)model.Mean[feature] : 0f;

    /// <summary>The posterior mean of an enemy type's bias for this weapon, zero where the type has not been struck.</summary>
    public static float TypeBiasMean(int itemType, int npcType)
        => models.TryGetValue(itemType, out Model? model) && model.Types.TryGetValue(npcType, out TypeBias? bias) ? (float)bias.Mean : 0f;

    public static int Evidence(int itemType) => models.TryGetValue(itemType, out Model? model) ? model.Evidence : 0;

    /// <summary>
    /// Take this posterior as learned for a weapon: the given mean and an isotropic variance, counted as evidence. A
    /// fixture uses it to plant a posterior whose draw and mean disagree, which a stream of outcomes cannot be relied on
    /// to produce.
    /// </summary>
    public static void Assume(int itemType, float[] mean, float variance)
    {
        var model = new Model();
        for (int i = 0; i < FeatureCount; i++)
        {
            model.Mean[i] = mean[i];
            for (int j = 0; j < FeatureCount; j++) model.Covariance[i, j] = i == j ? variance : 0.0;
        }
        model.Evidence = 1;
        models[itemType] = model;
        Revision++;
    }

    /// <summary>One struck body's buffs across a companion hit: whether it came away carrying one it did not have, and for how long.</summary>
    public static void ObserveDebuff(int itemType, int npcType, bool applied, int ticks)
    {
        if (itemType <= 0) return;
        if (!debuffs.TryGetValue((itemType, npcType), out DebuffRecord? record))
            debuffs[(itemType, npcType)] = record = new DebuffRecord();
        record.Struck++;
        if (applied) { record.Applied++; record.AppliedTicks += Math.Max(0, ticks); }
        Revision++;
    }

    /// <summary>
    /// The chance this weapon's hit leaves this enemy type debuffed, counted with one unobserved failure so a weapon
    /// seen to debuff once is not taken as certain to; zero until a hit has been observed.
    /// </summary>
    public static float DebuffChance(int itemType, int npcType)
        => debuffs.TryGetValue((itemType, npcType), out DebuffRecord? r) ? r.Applied / (r.Struck + 1f) : 0f;

    /// <summary>How long an observed debuff from this weapon on this enemy type lasted, ticks, averaged; zero where none was seen.</summary>
    public static int DebuffTicks(int itemType, int npcType)
        => debuffs.TryGetValue((itemType, npcType), out DebuffRecord? r) && r.Applied > 0 ? (int)(r.AppliedTicks / r.Applied) : 0;

    /// <summary>Seed the sampler and forget the tick's draws, so a fixture's draws are reproducible.</summary>
    public static void Seed(int seed)
    {
        random = new Random(seed);
        foreach (Model model in models.Values)
        {
            model.DrawTick = int.MinValue;
            foreach (TypeBias bias in model.Types.Values) bias.DrawTick = int.MinValue;
        }
    }

    /// <summary>Forget everything learned and seed the sampler, so a fixture measures the prior and its own learning rather than the previous case's.</summary>
    public static void Reset()
    {
        models.Clear();
        debuffs.Clear();
        random = new Random(0);
        LastSampledFactor = LastMeanFactor = 1f;
        Revision++;
    }

    private static double[] DrawFor(Model model, int tick)
    {
        if (model.DrawTick == tick) return model.Draw;
        if (!model.CholeskyValid) Factorise(model);
        Span<double> z = stackalloc double[FeatureCount];
        for (int i = 0; i < FeatureCount; i++) z[i] = Gaussian();
        for (int i = 0; i < FeatureCount; i++)
        {
            double sum = model.Mean[i];
            for (int j = 0; j <= i; j++) sum += model.Cholesky[i, j] * z[j];
            model.Draw[i] = sum;
        }
        model.DrawTick = tick;
        return model.Draw;
    }

    private static double DrawFor(TypeBias bias, int tick)
    {
        if (bias.DrawTick == tick) return bias.Draw;
        bias.Draw = bias.Mean + Gaussian() / Math.Sqrt(bias.Precision);
        bias.DrawTick = tick;
        return bias.Draw;
    }

    /// <summary>
    /// The lower-triangular factor of the covariance. Rank-one downdates accumulate rounding, so a diagonal that has gone
    /// non-positive is floored at a tiny variance rather than letting a square root of a negative poison every draw.
    /// </summary>
    private static void Factorise(Model model)
    {
        const double floor = 1e-9;
        for (int i = 0; i < FeatureCount; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = (model.Covariance[i, j] + model.Covariance[j, i]) / 2.0;
                for (int k = 0; k < j; k++) sum -= model.Cholesky[i, k] * model.Cholesky[j, k];
                if (i == j)
                    model.Cholesky[i, i] = Math.Sqrt(Math.Max(floor, sum));
                else
                    model.Cholesky[i, j] = sum / model.Cholesky[j, j];
            }
            for (int j = i + 1; j < FeatureCount; j++) model.Cholesky[i, j] = 0.0;
        }
        model.CholeskyValid = true;
    }

    private static double Gaussian()
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
