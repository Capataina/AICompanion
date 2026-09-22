#nullable enable

extern alias live;

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using ThreatSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatSense;
using EncounterSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.EncounterSense;

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
/// <item>The whole-brain suppression half is <c>VerifyEncounterContext.TheLiveBrainStopsMiningOnlyWhereTheEventReachesIt</c>,
/// with its own paired control: the same blood moon with the player underground, where the game does not run
/// it, must leave mining valued to within a ten-thousandth of the quiet scene. **Cite it by its ledger case
/// name, which is `combat keeps its purpose across a substituted enemy`** — `VerifyEncounterContext` is not
/// registered in `DefaultCases` at all and runs inside `VerifyCombatPurpose.Run()`, so a reader who greps for
/// the method's own sentence finds nothing. An earlier draft of this summary cited that sentence as a case
/// name and it exists in no fixture.</item>
/// </list>
///
/// The other two clauses are added here, at the level of the comparison and the sense rather than of one
/// multiplier — <c>the course charges an encounter once and only to optional non-combat work</c> already pins
/// <c>RelevanceFor</c>'s arithmetic, and a row repeating it would not have caught any of these.
/// </summary>
internal static class VerifyEncounterConduct
{
    private static readonly NeedKey Loot = new(NeedKind.Loot, "drop:1");
    private static readonly NeedKey Life = new(NeedKind.HostileLife, "hostile:1");

    public static int Run()
        => RunOneRow.Case("G13 an encounter suppresses a whole optional course while leaving a fight worth what it was",
               SuppressionIsOnWorkRatherThanABonusOnCombat)
         + RunOneRow.Case("G13 among two feasible fights the survivable one wins, and only while the encounter is on",
               SurvivalOrdersFeasibleFightsAndOnlyUnderAnEncounter)
         + RunOneRow.Case("G13 the player's death leaves the encounter standing",
               ThePlayersDeathPreservesTheEncounter);

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
    /// <para><b>The plan's other column for this gate, "idle artificially wins by zero damage", was the
    /// tree's behaviour when this fixture was written and is closed; the arm for it is elsewhere.</b>
    /// `NominalOrder` opened with the harm comparison under no qualifier, so for an empty course `I` and
    /// any feasible fight `F` it reduced to `F.CompanionHarm.CompareTo(0)` — positive whenever the fight
    /// cost anything at all. This fixture routed the finding rather than asserting it, because a red row
    /// is a stop for every lane; `edcb3ab` then qualified the comparison on both sides taking hostile
    /// life, so survival-first is a rule between fights and an idle course cannot win an encounter by
    /// having nothing to lose. The arm landed as the third arm of
    /// `retained courses compare conserved futures` in `VerifyCourseCore.SurvivalIsBetweenFights`,
    /// beside the two it needs to sit with — a winnable fight beating idle, and the safer of two fights
    /// still winning — and not here, because those three are one rule read three ways. What this row
    /// still owns is the encounter *gate*: that the ordering applies during an encounter and not
    /// outside one.</para>
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

    /// <summary>
    /// The plan's third G13 clause, and the one a sentinel proved had no detector anywhere: planting
    /// <c>if (player.dead) { Intensity = 0f; Source = "none"; Recognised = false; return; }</c> at the top of
    /// <c>EncounterSense.Update</c> left both rows above green and `combat keeps its purpose across a
    /// substituted enemy` green too.
    ///
    /// <para>It matters because a player's death is the one moment an encounter is most certainly still on —
    /// the boss that killed him is still in the room — and because this tree deliberately stopped suspending
    /// decisions on player death. A sense that reads the world as calm the instant he goes down hands every
    /// optional need its full relevance back mid-boss.</para>
    ///
    /// <para>It is a live sense over live NPC slots rather than an arithmetic row, because the clause is about
    /// what the sense reads rather than about what the objective does with it. The scene is deliberately one
    /// boss and nothing else: `boss |= threat.IsBoss` is taken before the reach filter, so the recognition
    /// does not depend on the body being reachable, and one hostile keeps the scene under the headless spawn
    /// cap of five where a crowd would read an inferred encounter and prove the wrong thing.</para>
    ///
    /// <para>The pair is alive-then-dead over the same scene, so a sense that answered `boss` unconditionally
    /// would satisfy neither half: the first assertion is that killing the player changes nothing, and the
    /// last is that removing the boss does.</para>
    /// </summary>
    private static void ThePlayersDeathPreservesTheEncounter()
    {
        Player player = Main.player[0];
        player.active = true;
        player.dead = false;
        player.statLife = player.statLifeMax = player.statLifeMax2 = 100;
        player.position = new Vector2(40 * 16, 60 * 16);
        Main.worldSurface = 200;

        NPC companion = Main.npc[1];
        companion.SetDefaults(NPCID.Zombie);
        companion.active = true;
        companion.position = new Vector2(41 * 16, 60 * 16);

        NPC boss = Main.npc[2];
        boss.SetDefaults(NPCID.KingSlime);
        boss.active = true;
        boss.life = boss.lifeMax = 2000;
        boss.position = new Vector2(48 * 16, 60 * 16);
        Require(boss.boss, "premise: the scene's hostile must carry the game's own boss flag, or it tests nothing");

        var threats = new ThreatSense();
        var sense = new EncounterSense();

        Observe(threats, sense, player, companion);
        Require(sense.Recognised && sense.Source == "boss" && sense.Intensity == 1f,
            $"premise: a live boss beside a living player must read as a recognised encounter; "
            + $"got source={sense.Source} intensity={sense.Intensity} recognised={sense.Recognised}");

        player.dead = true;
        player.statLife = 0;
        Observe(threats, sense, player, companion);
        Require(sense.Recognised && sense.Source == "boss" && sense.Intensity == 1f,
            $"the player's death cleared the encounter while the boss that killed him is still standing; "
            + $"got source={sense.Source} intensity={sense.Intensity} recognised={sense.Recognised}. "
            + "Optional work gets its whole relevance back the instant he goes down");

        // The other half of the pair: the sense must still be able to say no. Without this, a boss flag read
        // unconditionally — or a sense that never updates at all after the first observation — passes above.
        boss.active = false;
        Observe(threats, sense, player, companion);
        Require(!sense.Recognised && sense.Source == "none" && sense.Intensity == 0f,
            $"with the boss gone the sense must read a calm world, dead player or not; "
            + $"got source={sense.Source} intensity={sense.Intensity} recognised={sense.Recognised}");
    }

    /// <summary>One observation per engine tick, because the admission ceiling is measured from when each
    /// counted hostile arrived and observations sharing a tick collapse every arrival into now.</summary>
    private static void Observe(ThreatSense threats, EncounterSense sense, Player player, NPC companion)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        threats.Update(player, companion);
        sense.Update(player, threats);
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
