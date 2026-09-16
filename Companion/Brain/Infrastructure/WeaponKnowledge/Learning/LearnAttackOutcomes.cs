#nullable enable

using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

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
/// <para><b>Whether a weapon lands on an enemy type is that type's own fact; how the context changes what lands is the
/// weapon's.</b> Each enemy type carries the intercept — the ratio at the contexts that type has been shot in — and the
/// six context coefficients are shared by every type but learned only from how an outcome moved as the context moved
/// <i>within</i> one type: each observation is regressed on its context less that type's running mean context, the
/// fixed-effects (within) form of a panel regression, so the weapon model's bias coefficient is never learned. A type
/// never shot reads its intercept's prior — the arithmetic, a ratio of one — at the weapon's own mean context. The
/// first shape of this learner put the intercept in the weapon model and handed a type only the residual, so three
/// misses against demon eyes left a zombie the bow had never shot at a mean factor of 0.36 and the arsenal found no
/// target on 37 of 200 decisions where the same amount of evidence as hits found none on 12
/// (<c>VerifyWeaponLearning.MissesAgainstOneEnemyTypeStayWithThatType</c>). Letting the type absorb the error first, or
/// giving each type its intercept while the coefficients still learned from raw contexts, left the zombie at about 0.66
/// and 0.80 on the same evidence when replayed on paper: a type's misses at one context cannot tell "this type is
/// missed" from "shots here are missed", so any coefficient that learns from them carries them to every type, and only
/// a deviation within one type carries no type in it. The cost is that a new enemy type borrows nothing from how the
/// other types went; it pays one shot.</para>
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

        /// <summary>The mean context of every observation of this weapon, which an enemy type never shot is read against; the zero context until one arrives.</summary>
        public readonly double[] Centre = new double[FeatureCount];
        public int Seen;

        /// <summary>Where a new enemy type's intercept starts, and how sure of it; the arithmetic unless a fixture planted otherwise.</summary>
        public double TypePriorMean;
        public double TypePriorVariance = Weights.WeaponLearningEnemyTypeVariance;

        public Model()
        {
            for (int i = 0; i < FeatureCount; i++)
                Covariance[i, i] = Weights.WeaponLearningPriorVariance;
        }
    }

    private sealed class TypeBias
    {
        public double Precision;
        public double Weighted;
        public int DrawTick = int.MinValue;
        public double Draw;
        public double Mean => Weighted / Precision;

        /// <summary>The mean context this type has been shot in, and the mean outcome there, which its intercept estimates.</summary>
        public readonly double[] Centre = new double[FeatureCount];
        public double MeanOutcome;
        public int Seen;

        public TypeBias(Model model)
        {
            Precision = 1.0 / model.TypePriorVariance;
            Weighted = model.TypePriorMean * Precision;
        }
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
    /// weapon's own reach and of its volley cone, so the same coefficient means the same thing on a sword and on a
    /// rifle; an aim further off than the cone — a bank, a pierce line — reads past one, clamped. The lane counts the
    /// other bodies the use is predicted to strike as a diminishing share, uncapped, because the sim that counts them
    /// has no pierce cap to scale by.
    /// </summary>
    public static float[] Context(float distance, float reach, float aimOffset, float widestAim, float relativeSpeed, int laneHostiles, float orbSpeed, float orbMaxSpeed, bool debuffedByOther)
    {
        return new[]
        {
            1f,
            Math.Clamp(distance / MathF.Max(1f, reach), 0f, 1.5f),
            Math.Clamp(MathF.Abs(aimOffset) / MathF.Max(0.01f, widestAim), 0f, 1.5f),
            Math.Clamp(relativeSpeed / RelativeSpeedScale, 0f, 3f),
            Math.Clamp(laneHostiles / (laneHostiles + 4f), 0f, 1f),
            Math.Clamp(orbSpeed / MathF.Max(.01f, orbMaxSpeed), 0f, 1.5f),
            debuffedByOther ? 1f : 0f,
        };
    }

    /// <summary>
    /// The multiplier on a forecast hit's damage for this weapon against this enemy type in this context. Exactly one,
    /// with no draw, while the weapon has no evidence. With <paramref name="explore"/> the coefficients are the tick's
    /// Thompson draw; without it they are the posterior mean, which the arsenal uses while its exploration gate is closed: a boss, a
    /// hit that would take a large share of a hurt player's or companion's remaining life, or a close shooter with a clear
    /// line, and never merely ordinary enemies against a healthy player (Companion/Brain/Infrastructure/WeaponKnowledge/CLAUDE.md).
    /// </summary>
    public static float Factor(int itemType, int npcType, ReadOnlySpan<float> x, bool explore, int tick)
    {
        if (!models.TryGetValue(itemType, out Model? model) || model.Evidence == 0)
            return 1f;
        model.Types.TryGetValue(npcType, out TypeBias? bias);
        if (bias == null && explore)
        {
            // An enemy type this weapon has never struck is drawn from the prior, and the draw is kept for the tick like
            // every other: a fresh number per call made the aim candidates of one forecast compete against different noise.
            // The entry holds exactly the prior and no context, so creating it changes nothing Observe will later do with it.
            model.Types[npcType] = bias = new TypeBias(model);
        }
        double[] theta = explore ? DrawFor(model, tick) : model.Mean;
        double[] centre = bias is { Seen: > 0 } ? bias.Centre : model.Centre;
        double linear = 1.0;
        for (int i = 1; i < FeatureCount; i++) linear += theta[i] * (x[i] - centre[i]);
        linear += bias == null ? model.TypePriorMean : explore ? DrawFor(bias, tick) : bias.Mean;
        float factor = (float)Math.Clamp(linear, 0.0, MaxOutcomeRatio);
        if (explore) LastSampledFactor = factor; else LastMeanFactor = factor;
        return factor;
    }

    /// <summary>
    /// One closed shot: the context it was fired in and its realised yield over the forecast's. The context coefficients
    /// are updated on how far this context and this outcome sit from the enemy type's means so far, scaled by
    /// <c>√(n/(n+1))</c> after <c>n</c> earlier observations of the type. That scaling is what makes an online update exact
    /// rather than approximate: summed over a type's observations, the scaled deviations from each running mean give
    /// exactly the scatter about the final means (the recursion Welford's running variance rests on), so the coefficients
    /// are the posterior a regression on fully centred data would reach, and the scaled outcome's noise has the unscaled
    /// variance. On the fixtures the scaling moves the burst bow's learned plain factor by a thousandth (0.626 without it,
    /// 0.627 with it), so no row proves it matters; it stays because it is the exact form and costs nothing. What did matter
    /// is centring the outcome as well as the context: a first version regressed the raw outcome less the intercept on
    /// centred contexts and taught the intercept the residual, which read the burst bow's plain factor at 0.685 against a
    /// true 0.6 and flipped the debuff-then-burst opening in <c>VerifyWeaponLearning</c>. The type's intercept is the
    /// precision-weighted mean of its raw outcomes, because the deviations the coefficients explain sum to zero over the
    /// type. The first observation of a type deviates by
    /// nothing, so it moves the coefficients not at all and is the type's alone.
    /// </summary>
    public static void Observe(int itemType, int npcType, ReadOnlySpan<float> x, float ratio)
    {
        if (itemType <= 0) return;
        if (!models.TryGetValue(itemType, out Model? model))
            models[itemType] = model = new Model();
        if (!model.Types.TryGetValue(npcType, out TypeBias? bias))
            model.Types[npcType] = bias = new TypeBias(model);
        double y = Math.Clamp(ratio, 0f, MaxOutcomeRatio) - 1.0;
        double noise = Weights.WeaponLearningNoiseVariance;

        double scale = Math.Sqrt(bias.Seen / (bias.Seen + 1.0));
        Span<double> z = stackalloc double[FeatureCount];
        for (int i = 1; i < FeatureCount; i++) z[i] = scale * (x[i] - bias.Centre[i]);
        double w = scale * (y - bias.MeanOutcome);

        Span<double> pz = stackalloc double[FeatureCount];
        double s = noise, predicted = 0.0;
        for (int i = 0; i < FeatureCount; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < FeatureCount; j++) sum += model.Covariance[i, j] * z[j];
            pz[i] = sum;
            s += z[i] * sum;
            predicted += model.Mean[i] * z[i];
        }
        double error = w - predicted;
        for (int i = 0; i < FeatureCount; i++)
            model.Mean[i] += pz[i] / s * error;
        for (int i = 0; i < FeatureCount; i++)
            for (int j = 0; j < FeatureCount; j++)
                model.Covariance[i, j] -= pz[i] * pz[j] / s;
        model.CholeskyValid = false;
        model.DrawTick = int.MinValue;
        model.Evidence++;

        bias.Precision += 1.0 / noise;
        bias.Weighted += y / noise;
        bias.DrawTick = int.MinValue;

        bias.Seen++;
        model.Seen++;
        bias.MeanOutcome += (y - bias.MeanOutcome) / bias.Seen;
        for (int i = 1; i < FeatureCount; i++)
        {
            bias.Centre[i] += (x[i] - bias.Centre[i]) / bias.Seen;
            model.Centre[i] += (x[i] - model.Centre[i]) / model.Seen;
        }
        Revision++;
    }

    /// <summary>The posterior mean of one coefficient, zero with no evidence.</summary>
    public static float Coefficient(int itemType, int feature)
        => models.TryGetValue(itemType, out Model? model) ? (float)model.Mean[feature] : 0f;

    /// <summary>The posterior mean of an enemy type's intercept for this weapon, zero where the type has not been struck.</summary>
    public static float TypeBiasMean(int itemType, int npcType)
        => models.TryGetValue(itemType, out Model? model) && model.Types.TryGetValue(npcType, out TypeBias? bias) ? (float)bias.Mean : 0f;

    public static int Evidence(int itemType) => models.TryGetValue(itemType, out Model? model) ? model.Evidence : 0;

    /// <summary>
    /// Take this posterior as learned for a weapon, counted as evidence: the context coefficients are the given mean with an
    /// isotropic variance, read against the zero context, and every enemy type's intercept starts at the given bias with the
    /// same variance. A fixture uses it to plant a posterior whose draw and mean disagree, which a stream of outcomes cannot
    /// be relied on to produce.
    /// </summary>
    public static void Assume(int itemType, float[] mean, float variance)
    {
        var model = new Model { TypePriorMean = mean[Bias], TypePriorVariance = variance };
        for (int i = 0; i < FeatureCount; i++)
        {
            model.Mean[i] = i == Bias ? 0.0 : mean[i];
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
