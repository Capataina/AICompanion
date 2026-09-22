#nullable enable

extern alias live;

using System;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// Gate G13 — how a course behaves while the world is about one thing.
///
/// The plan's row asks for four properties: a boss or event suppresses optional work, the survivable
/// continuation wins among feasible fight continuations, the player's death preserves the encounter, and an
/// unarmed or unsupported target gives safe conduct rather than a fake attack. Two of the four already have
/// rows under other names and are **referenced rather than rebuilt**, because a second row asserting the same
/// property is two things to keep in step and one of them will drift:
///
/// <list type="bullet">
/// <item><c>an unarmed companion offers no combat</c> (<c>Combat/VerifyCombatActivity</c>) and
/// <c>combat is admitted only where it can be executed</c> (<c>Combat/VerifyHuntAdmissibility</c>) are the
/// safe-conduct half — no weapon and no executable stand each produce no offer rather than an attempt.</item>
/// <item><c>the live brain stops mining only where the event reaches it</c>
/// (<c>Combat/VerifyEncounterContext</c>) is the suppression half at the level of the whole brain, with its own
/// paired control: the same blood moon with the player underground, where the game does not run it, must leave
/// mining valued to within a ten-thousandth of the quiet scene.</item>
/// </list>
///
/// What this file adds is the two properties nothing asserts, both at the level of the comparison rather than
/// of one multiplier — <c>the course charges an encounter once and only to optional non-combat work</c> already
/// pins <c>RelevanceFor</c>'s arithmetic, and a row repeating it would not have caught either of these.
/// </summary>
internal static class VerifyEncounterConduct
{
    private static readonly NeedKey Loot = new(NeedKind.Loot, "drop:1");
    private static readonly NeedKey Life = new(NeedKind.HostileLife, "hostile:1");

    public static int Run()
        => RunOneRow.Case("G13 an encounter suppresses a whole optional course while leaving a fight worth what it was",
               SuppressionIsOnWorkRatherThanABonusOnCombat)
         + RunOneRow.Case("G13 among two feasible fights the survivable one wins, and only while the encounter is on",
               SurvivalOrdersFeasibleFightsAndOnlyUnderAnEncounter);

    /// <summary>
    /// The plan's §2 wording is that optional work is *inadmissible* during a boss or event, and the tree
    /// discounts it by <c>1 − max(urgency, intensity)</c> instead, which is a multiplier where the plan wrote
    /// an admission gate. At full intensity the two agree — the multiplier is zero — and that is the case this
    /// row pins, because it is the one where a reader could not otherwise tell a suppression from a bonus.
    ///
    /// <para>Both directions are asserted, and the second is the one that makes it a check. A course whose
    /// effects are all loot must lose its whole value; a course whose effects are all a hostile's life must be
    /// worth *exactly* what it was in the calm world, to the last bit. An implementation that answered the
    /// first by scaling everything down, or that answered "combat wins" by scaling combat up, satisfies a
    /// one-sided row and fails this one — and the difference is not cosmetic, because a bonus on combat needs
    /// a magnitude nobody can derive and moves work's value at the same time.</para>
    ///
    /// <para>Mutation from the plan's column, "ordinary utility purchases forbidden optional work": make
    /// <c>CourseComparisonEpisode.RelevanceFor</c> answer 1 for every kind. The loot arm then reports the same
    /// number in both worlds and this row is red.</para>
    /// </summary>
    private static void SuppressionIsOnWorkRatherThanABonusOnCombat()
    {
        CourseProjection loot = Course(Effect(1, Loot, 20, tick: 10));
        CourseProjection fight = Course(Effect(2, Life, 20, tick: 10));

        double calmLoot = Value(loot, Calm()), bossLoot = Value(loot, Boss());
        double calmFight = Value(fight, Calm()), bossFight = Value(fight, Boss());

        Require(calmLoot > 0, $"the calm world must value the optional course at all, or the pair proves nothing; calm loot={calmLoot}");
        Require(bossLoot == 0,
            $"a boss left an optional course worth {bossLoot} where a calm world valued it {calmLoot}; "
            + "optional work is supposed to be worth nothing while the world is about one thing");
        Require(calmFight == bossFight,
            $"the fight was repriced by the encounter, {calmFight} calm against {bossFight} under a boss; "
            + "danger suppresses work rather than inflating combat, and a bonus on combat is a magnitude nobody can derive");
        Require(bossFight > bossLoot,
            $"under a boss the fight ({bossFight}) did not outrank the optional course ({bossLoot})");
    }

    /// <summary>
    /// <c>CompareCourseOutcomes.NominalOrder</c> compares companion harm *before* total value while an
    /// encounter is on, and falls back to total value when it is off. That is the plan's "survival-first among
    /// feasible meaningful fight continuations", it has been in the tree since the encounter term landed, and
    /// no row anywhere reaches it: every G03 ordering row runs with <c>encounter: false</c>.
    ///
    /// <para>The pair here is the encounter being on and off over one pair of courses, so the row cannot pass
    /// by the two fights differing in some other way. Both are feasible fights with identical useful effects at
    /// identical times; they differ only in the companion harm their own forecast carries. Under the encounter
    /// the safer one must win however the totals fall, and with the encounter off the more valuable one must
    /// win — which here is the *riskier* one, because its harm is the only thing making its total smaller, so a
    /// survival rule left switched on everywhere is caught rather than merely unexercised.</para>
    ///
    /// <para>Mutation from the plan's column, "idle artificially wins by zero damage": the third assertion is
    /// that a course doing nothing does not outrank a feasible fight under an encounter. <b>This is the arm
    /// that fails on the tree as it stands</b> — see the note on the assertion itself.</para>
    /// </summary>
    private static void SurvivalOrdersFeasibleFightsAndOnlyUnderAnEncounter()
    {
        // One scene, read twice. The safer fight is deliberately the *less valuable* one — it kills less and
        // costs the companion almost nothing — because a scene where the safer course also has the higher
        // total is decided by the total whichever rule is in force, and a row built that way passes with the
        // survival key deleted. It was built that way first and the mutation below found it green.
        CourseProjection safe = Course(new[] { Effect(1, Life, 6, tick: 10) },
            Harm(HarmActor.Companion, damage: 1, life: 100, tick: 5));
        CourseProjection risky = Course(new[] { Effect(2, Life, 20, tick: 10) },
            Harm(HarmActor.Companion, damage: 40, life: 100, tick: 5));

        CourseValue safeUnderBoss = Evaluate(safe, Boss()), riskyUnderBoss = Evaluate(risky, Boss());
        Require(safeUnderBoss.CompanionHarm < riskyUnderBoss.CompanionHarm,
            $"the scene must differ in companion harm, or the row is about nothing; safe={safeUnderBoss.CompanionHarm} risky={riskyUnderBoss.CompanionHarm}");
        Require(safeUnderBoss.Total.Nominal < riskyUnderBoss.Total.Nominal,
            $"the safer fight must be the *lower*-valued one, or the survival key is not what decides this row; "
            + $"safe={safeUnderBoss.Total.Nominal} risky={riskyUnderBoss.Total.Nominal}");

        Require(CompareCourseOutcomes.NominalOrder(safeUnderBoss, riskyUnderBoss, encounter: true) > 0,
            $"under an encounter the survivable fight lost to the one that costs the companion forty times as much life; "
            + $"safe harm={safeUnderBoss.CompanionHarm} total={safeUnderBoss.Total.Nominal}, "
            + $"risky harm={riskyUnderBoss.CompanionHarm} total={riskyUnderBoss.Total.Nominal}");

        // The control, on the same two courses. With no encounter the ordinary ordering is total value first,
        // so the riskier and more valuable fight must win — which is the opposite answer from the same inputs,
        // and is what says the survival key is switched on by the encounter rather than always.
        CourseValue safeCalm = Evaluate(safe, Calm()), riskyCalm = Evaluate(risky, Calm());
        Require(CompareCourseOutcomes.NominalOrder(riskyCalm, safeCalm, encounter: false) > 0,
            $"with no encounter the ordering must be total value first, and the safer-but-emptier course won anyway, "
            + $"which is a survival rule left switched on in a calm world; safe={safeCalm.Total.Nominal} risky={riskyCalm.Total.Nominal}");
    }

    private static CourseComparisonEpisode Calm()
        => new(1, 1, 100, Needs(), true, 0, "encounter-conduct-fixture");

    private static CourseComparisonEpisode Boss()
        => new(2, 1, 100, Needs(), true, 1, "encounter-conduct-fixture");

    private static UsefulNeed[] Needs()
        => new[] { new UsefulNeed(Loot, 20, 20, 1), new UsefulNeed(Life, 20, 20, 1) };

    private static PredictedEffect Effect(long id, NeedKey need, double amount, double tick)
        => new(id, need, amount, tick, tick, tick, EstimateStatus.NativeBound,
            Array.Empty<long>(), Array.Empty<EffectDelta>(), DependencyManifest.Empty);

    private static PredictedHarm Harm(HarmActor actor, double damage, double life, double tick)
        => new(actor, damage, life, tick, EstimateStatus.NativeBound);

    private static CourseProjection Course(params PredictedEffect[] effects) => Course(effects, Array.Empty<PredictedHarm>());

    private static CourseProjection Course(PredictedEffect[] effects, params PredictedHarm[] harm)
        => new(new[] { new StepBinding(effects[0].Id, new OpportunityKey("fixture", "conduct", "target", 1), "pickup",
                   default, "none", 1, 1, 10, 1, 0, Array.Empty<ResourcePhase>(), effects, Array.Empty<long>(),
                   DependencyManifest.Empty, true) },
            harm, Array.Empty<CompanionshipInterval>(), 30, true);

    private static double Value(CourseProjection course, CourseComparisonEpisode episode)
        => CompareCourseOutcomes.Evaluate(course, episode).UsefulEffects;

    private static CourseValue Evaluate(CourseProjection course, CourseComparisonEpisode episode)
        => CompareCourseOutcomes.Evaluate(course, episode);

    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
