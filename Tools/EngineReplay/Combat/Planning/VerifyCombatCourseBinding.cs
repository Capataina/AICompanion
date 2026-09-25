#nullable enable

extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using live::AICompanion.Companion.Brain.Activities.Combat;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>Headless G04/G11 coverage for combat's immutable candidate catalogue.  Program registration
/// belongs to the central retained-course integration, so this method remains callable without changing its dispatcher.</summary>
internal static class VerifyCombatCourseBinding
{
    public static int CapturedUseBindsWithoutReadingLiveTerraria()
    {
        // Minted by the production helper rather than written out: the binder's front reads every use whose
        // identity starts with the target's `npc:<slot>.<generation>/`, and the literal this row carried
        // (`plan:7/segment:0/use:0`) was the identity before `UseId` moved the target into it, so the front
        // found no use and the claim read 0 damage at tick 0 — a fixture nothing had run since the change.
        string useId = CombatCourseFacts.UseId(12, 3, 0, 32, 32, 0);
        var target = new CombatCourseFacts.Target(12, 3, 1, 40, 40, 64, 32, 0, 0);
        var weapon = new CombatCourseFacts.Weapon(0, 9, 2, 20, 0, 10, 1, 8, 5);
        // The target trace contains two landed six-damage hits.  `ExpectedTargetDamage` is their
        // per-use allocation, not two effects, so one native use can claim twelve once.
        var use = new CombatCourseFacts.Use(useId, 7, 0, 0, 12, 3, 0, 9, 2, 32, 32, 64, 32, 1, 0, 15, 20, 12, 4);
        DecisionFact Fact(FactKey key, long version, object value, double amount = 0)
            => new(key, version, new FactValue(amount, Text: JsonSerializer.Serialize(value)), FactEvidence.Observed);
        var snapshot = new DecisionFactSnapshot(44, 8, 15, 1, 0, WithFront(new[]
        {
            Fact(CombatCourseFacts.TargetKey(12, 3), 3, target, 40),
            Fact(CombatCourseFacts.WeaponKey(0), 9, weapon),
            new DecisionFact(CombatCourseFacts.ManaCapacityKey(), 1, new FactValue(30), FactEvidence.Observed),
            Fact(CombatCourseFacts.UseKey(useId), 7, use),
            Fact(ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)), 1,
                new CapturedCourseTravel(new CoursePoint(32, 32), default, new CoursePoint(32, 32), new CoursePoint(3, 1), 0,
                    OpportunityAdmission.KnownUsable, "local", Array.Empty<CoursePoint>(), 1,
                    new[] { new TimedCoursePose(0, new(32, 32), new(3, 1)) }))
        }));
        var source = new CombatOpportunitySource();
        var budget = new DecisionWorkBudget(double.PositiveInfinity, 1, () => 0, 1);
        var slice = source.Continue(snapshot, new DecisionWorkCursor(), budget);
        Require(slice.Examined.Count == 1 && slice.Examined[0].Admission == OpportunityAdmission.KnownUsable,
            "The captured use was not discovered as a concrete usable opportunity.");

        var state = new ProjectedCourseState(new CoursePoint(32, 32));
        var travelKey = ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32));
        var missing = new DecisionFactSnapshot(44, 8, 15, 1, 0, snapshot.Facts.Where(fact => fact.Key != travelKey));
        var retainedCursor = new DecisionWorkCursor();
        var gate = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() });
        var pending = gate.Bind(slice.Examined[0], state, missing, retainedCursor,
            new DecisionWorkBudget(double.PositiveInfinity));
        Require(pending.Pending && pending.RequiredTravel?.Single().Key == travelKey,
            "A missing route did not leave the exact combat candidate pending with its query.");
        var completed = new DecisionFactSnapshot(44, 8, 15, 1, 0, missing.Facts.Concat(new[]
        {
            new DecisionFact(travelKey, 1, snapshot.Facts.Single(fact => fact.Key == travelKey).Value, FactEvidence.Modelled)
        }));
        var resumed = gate.Bind(slice.Examined[0], state, completed, retainedCursor,
            new DecisionWorkBudget(double.PositiveInfinity));
        Require(resumed.Binding?.NativeUseId == useId,
            "Completing the requested route lost the combat candidate behind an advanced cursor.");
        var bound = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() }).Bind(slice.Examined[0], state,
            snapshot, new DecisionWorkCursor(), new DecisionWorkBudget(double.PositiveInfinity, 2, () => 0, 1));
        Require(bound.Binding != null && bound.Binding.Method == CombatCourseFacts.Method && bound.Binding.Tool == CombatCourseFacts.ToolId(0, 9, 2),
            "The binder did not retain the captured method and concrete tool.");
        Require(bound.Binding.ArrivalVelocity == new CoursePoint(3, 1) && bound.Binding.TravelTicks == 0,
            "A useful local shot did not use the captured route's arrival state.");
        PredictedEffect claimed = bound.Binding.Effects.Single();
        Require(claimed.Evidence == EstimateStatus.Nominal && claimed.Amount == 12 && claimed.NominalTick == 4,
            "The binder did not preserve the simulator's per-use target damage as a nominal effect: "
            + $"{claimed.Evidence} {claimed.Amount} at tick {claimed.NominalTick} over {claimed.EarliestTick}..{claimed.LatestTick}.");
        Require(bound.Binding.Dependencies.Reads.Select(read => read.Key).SequenceEqual(new[]
            { CombatCourseFacts.ManaCapacityKey(), CombatCourseFacts.FrontKey(12, 3), CombatCourseFacts.TargetKey(12, 3), CombatCourseFacts.UseKey(useId), CombatCourseFacts.WeaponKey(0),
              ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)) }.OrderBy(key => key)),
            "The binding omitted a captured input dependency.");
        Require(state.TryApply(bound.Binding, new System.Collections.Generic.Dictionary<string, double>(), out _),
            "the nominal combat prefix could not enter projected state");
        var dependent = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() }).Bind(slice.Examined[0],
            state, snapshot, new DecisionWorkCursor(), new DecisionWorkBudget(double.PositiveInfinity));
        Require(dependent.Binding == null && dependent.Reason == "combat-target-successor-unresolved",
            "a later shot treated the old observed target as the outcome of an uncertain earlier hit");
        var unsupported = use with { ExpectedTargetDamage = 0 };
        var unsupportedSnapshot = new DecisionFactSnapshot(45, 8, 16, 2, 0, WithFront(new[]
        {
            Fact(CombatCourseFacts.TargetKey(12, 3), 3, target, 40), Fact(CombatCourseFacts.WeaponKey(0), 9, weapon),
            new DecisionFact(CombatCourseFacts.ManaCapacityKey(), 1, new FactValue(30), FactEvidence.Observed),
            Fact(CombatCourseFacts.UseKey(useId), 7, unsupported),
            Fact(ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)), 1,
                new CapturedCourseTravel(new CoursePoint(32, 32), default, new CoursePoint(32, 32), default, 0,
                    OpportunityAdmission.KnownUsable, "local", Array.Empty<CoursePoint>(), 1,
                    new[] { new TimedCoursePose(0, new(32, 32), default) }))
        }));
        var unresolved = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() }).Bind(slice.Examined[0],
            new ProjectedCourseState(new CoursePoint(32, 32)), unsupportedSnapshot, new DecisionWorkCursor(),
            new DecisionWorkBudget(double.PositiveInfinity, 2, () => 0, 1));
        Require(unresolved.Binding == null && unresolved.Admission == OpportunityAdmission.Unresolved
            && unresolved.Reason == CombatOpportunityBinder.NoDamagingUse, "An unforecast use published a zero-damage combat effect.");
        Require(!CombatCourseFacts.Front(unsupportedSnapshot.Facts).Any(),
            "a use that lands nothing on its own target published front evidence for it");
        var unsupportedSlice = new CombatOpportunitySource().Continue(unsupportedSnapshot, new DecisionWorkCursor(),
            new DecisionWorkBudget(double.PositiveInfinity, 8, () => 0, 1));
        Require(unsupportedSlice.Examined.Count == 0,
            "the census admitted a target whose only use lands nothing on it, which its own binder refuses");
        return 0;
    }

    /// <summary>
    /// Binding a fight claims the fight, not its first arrow.
    ///
    /// The course is charged the companionship gap of the whole excursion — out to the reunion at the
    /// end of the fight — so if it is credited one use it is comparing a whole trip's cost against a
    /// sixth of its benefit, and the empty course wins every time. Measured through the world run on the
    /// play of 0.38.13 before the fix: a 45-life zombie with a six-use front totalling 48 damage priced
    /// at useful 0.0083 against gap 0.0203, total −0.0120, held for 513 consecutive ticks with nothing
    /// refused and a bow in hand.
    ///
    /// The scene is three uses against one 30-life target from one stand, firing 0, 30 and 80 ticks
    /// apart and landing 10 ticks after each. Twelve plus twelve plus twelve over-kills, so the claim is
    /// the life and no more, and the nominal tick is the damage-weighted mean of when that life is taken
    /// rather than the first impact or the last. Every number here is chosen so that each wrong answer
    /// is a different number: the first shot alone is 12 at tick 10, the front uncapped by what is there
    /// to kill is 36, the last impact is 90, and the unweighted mean of the three impacts is 46.67
    /// against the weighted 38, which is what makes the third shot being a partial hit observable.
    /// </summary>
    public static int TheBoundFightClaimsTheFrontRatherThanItsFirstShot()
    {
        StepBinding bound = BindAThreeShotFight();
        PredictedEffect effect = bound.Effects.Single();
        Require(effect.Amount == 30,
            $"binding this fight claims {effect.Amount} damage where the front takes the target's whole "
            + $"30 life: 12 is the first shot alone, which is what the course was charged a whole "
            + $"excursion's companionship gap against, and 36 is the front uncapped by what is there to kill");
        // 12 at 10, 12 at 40, 6 at 90 — the third shot is capped to the life that is left.
        Require(Math.Abs(effect.NominalTick - 38) < 1e-6,
            $"the claimed damage is timed at {effect.NominalTick} rather than the damage-weighted mean 38 "
            + $"of when it lands: 10 prices a long fight as instant, 90 discounts the opening shot as hard "
            + $"as the closing one, and 46.67 is the mean of the three impacts with no weight on the third "
            + $"being a partial hit");
        Require(effect.EarliestTick == 10 && effect.LatestTick == 90,
            $"the interval reads {effect.EarliestTick}..{effect.LatestTick} rather than the first impact 10 "
            + $"to the last 90, so it does not say when this damage begins or when it is complete");
        Require(effect.Evidence == EstimateStatus.Nominal,
            "claiming the whole front must not claim bounds it does not have; the evidence stays nominal");
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"  a bound fight claims {effect.Amount} of 30 life at a weighted mean tick "
            + $"{effect.NominalTick} over {effect.EarliestTick}..{effect.LatestTick}, against the 12 at tick 10 "
            + "one use alone would have claimed");
        return 0;
    }

    /// <summary>
    /// A step is occupied for as long as the effect it carries, and it declares every use that effect
    /// was computed from.
    ///
    /// Two halves of one review finding, on one scene, because they are two halves of one claim. **The
    /// occupancy**: `useTicks` and both `ResourcePhase`s used to end one use after arrival while the
    /// effect ran to the last impact, so the order search could schedule a second step inside a window
    /// the fight already owned and `ForecastCourseCompanionship` charged the gap for one use against a
    /// credit spanning the fight — the asymmetry that commit named and moved only half of. **The
    /// manifest**: `FightAhead` summed its uses out of the raw snapshot rather than through
    /// `TrackedFactReader.Read`, so the binding declared a dependency on one of the three uses it
    /// claimed, and every consumer that asks whether a retained course is still priced on facts the
    /// world still holds — `DependencyManifest.Changed`, `RetainCourse`, `BindCourseOrder`,
    /// `RepairCourse.Invalidate` — was blind to a change confined to the second or third.
    ///
    /// The manifest arm is written as "every claimed use, by key" rather than as a count, because a
    /// count passes against a binder that reads three arbitrary facts.
    /// </summary>
    public static int ABoundFightIsOccupiedAndDeclaredForItsWholeLength()
    {
        StepBinding bound = BindAThreeShotFight();
        PredictedEffect effect = bound.Effects.Single();
        double arrival = bound.TravelTicks;
        Require(Math.Abs(arrival + bound.UseTicks - effect.LatestTick) < 1e-6,
            $"the step declares {bound.UseTicks} ticks of use from an arrival at {arrival}, ending at "
            + $"{arrival + bound.UseTicks}, while the effect it carries runs to {effect.LatestTick}: one "
            + $"use's occupancy against a whole fight's credit is the asymmetry this was supposed to close, "
            + $"and a phase that ends early lets the search book a second step inside the fight's own window");
        foreach (var resource in new[] { CourseResource.Body, CourseResource.Hand })
        {
            ResourcePhase phase = bound.Resources.Single(r => r.Resource == resource);
            Require(Math.Abs(phase.EndTick - effect.LatestTick) < 1e-6,
                $"the {resource} is declared free at {phase.EndTick} while the fight it is holding runs to "
                + $"{effect.LatestTick}");
        }
        var declared = bound.Dependencies.Reads.Select(read => read.Key).ToHashSet();
        var claimed = ThreeShots().Select(shot => CombatCourseFacts.UseKey(shot.Id)).ToArray();
        var missing = claimed.Where(key => !declared.Contains(key)).ToArray();
        Require(missing.Length == 0,
            $"the effect claims {effect.Amount} damage computed from {claimed.Length} uses and the binding "
            + $"declares {claimed.Length - missing.Length} of them: {string.Join(", ", missing.Select(k => k.ToString()))} "
            + $"are absent from the manifest, so a change confined to one of them dirties nothing and the "
            + $"course stays published on a claim the world no longer supports");
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"  the step holds body and hand from {arrival} to "
            + $"{effect.LatestTick} and declares all {claimed.Length} uses it claimed, of "
            + $"{bound.Dependencies.Reads.Count} reads");
        return 0;
    }

    private static CombatCourseFacts.Use[] ThreeShots()
    {
        CombatCourseFacts.Use Shot(string id, int fireTick, float damage)
            => new(id, 7, 0, 0, 12, 3, 0, 9, 2, 32, 32, 64, 32, 1, 0, fireTick, 20, damage, 10);
        return new[]
        {
            Shot(CombatCourseFacts.UseId(12, 3, 0, 32, 32, 0), 100, 12),
            Shot(CombatCourseFacts.UseId(12, 3, 0, 32, 32, 1), 130, 12),
            Shot(CombatCourseFacts.UseId(12, 3, 0, 32, 32, 2), 180, 12),
        };
    }

    /// <summary>Three uses against one 30-life target from one stand, firing 0, 30 and 80 ticks apart and
    /// landing ten ticks after each. The ids come from `UseId` rather than being written by hand, because
    /// the binder filters the front by the target the identity encodes.</summary>
    private static StepBinding BindAThreeShotFight()
    {
        var target = new CombatCourseFacts.Target(12, 3, 1, 30, 30, 64, 32, 0, 0);
        var weapon = new CombatCourseFacts.Weapon(0, 9, 2, 20, 0, 10, 1, 8, 5);
        var shots = ThreeShots();
        DecisionFact Fact(FactKey key, long version, object value, double amount = 0)
            => new(key, version, new FactValue(amount, Text: JsonSerializer.Serialize(value)), FactEvidence.Observed);
        var facts = new[]
        {
            Fact(CombatCourseFacts.TargetKey(12, 3), 3, target, 30),
            Fact(CombatCourseFacts.WeaponKey(0), 9, weapon),
            new DecisionFact(CombatCourseFacts.ManaCapacityKey(), 1, new FactValue(30), FactEvidence.Observed),
            Fact(ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)), 1,
                new CapturedCourseTravel(new CoursePoint(32, 32), default, new CoursePoint(32, 32), new CoursePoint(3, 1), 0,
                    OpportunityAdmission.KnownUsable, "local", Array.Empty<CoursePoint>(), 1,
                    new[] { new TimedCoursePose(0, new(32, 32), new(3, 1)) }))
        }.Concat(shots.Select(shot => Fact(CombatCourseFacts.UseKey(shot.Id), 7, shot)));
        var snapshot = new DecisionFactSnapshot(46, 8, 15, 1, 0, WithFront(facts.ToArray()));
        var slice = new CombatOpportunitySource().Continue(snapshot, new DecisionWorkCursor(),
            new DecisionWorkBudget(double.PositiveInfinity, 8, () => 0, 1));
        Require(slice.Examined.Count == 1,
            $"premise: three uses against one target are one opportunity, not {slice.Examined.Count}");
        var bound = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() })
            .Bind(slice.Examined[0], new ProjectedCourseState(new CoursePoint(32, 32)), snapshot,
                new DecisionWorkCursor(), new DecisionWorkBudget(double.PositiveInfinity, 8, () => 0, 1));
        Require(bound.Binding != null, $"premise: the front must bind at all; {bound.Reason}");
        return bound.Binding!;
    }

    /// <summary>A hand-built snapshot with the front evidence the live capture adds, built by the capture's own function.</summary>
    private static DecisionFact[] WithFront(DecisionFact[] facts) => facts.Concat(CombatCourseFacts.Front(facts)).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
