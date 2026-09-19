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
        const string useId = "plan:7/segment:0/use:0";
        var target = new CombatCourseFacts.Target(12, 3, 1, 40, 40, 64, 32, 0, 0);
        var weapon = new CombatCourseFacts.Weapon(0, 9, 2, 20, 0, 10, 1, 8, 5);
        // The target trace contains two landed six-damage hits.  `ExpectedTargetDamage` is their
        // per-use allocation, not two effects, so one native use can claim twelve once.
        var use = new CombatCourseFacts.Use(useId, 7, 0, 0, 12, 3, 0, 9, 2, 32, 32, 64, 32, 1, 0, 15, 20, 12, 4);
        DecisionFact Fact(FactKey key, long version, object value, double amount = 0)
            => new(key, version, new FactValue(amount, Text: JsonSerializer.Serialize(value)), FactEvidence.Observed);
        var snapshot = new DecisionFactSnapshot(44, 8, 15, 1, 0, new[]
        {
            Fact(CombatCourseFacts.TargetKey(12, 3), 3, target, 40),
            Fact(CombatCourseFacts.WeaponKey(0), 9, weapon),
            new DecisionFact(CombatCourseFacts.ManaCapacityKey(), 1, new FactValue(30), FactEvidence.Observed),
            Fact(CombatCourseFacts.UseKey(useId), 7, use),
            Fact(ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)), 1,
                new CapturedCourseTravel(new CoursePoint(32, 32), default, new CoursePoint(32, 32), new CoursePoint(3, 1), 0,
                    OpportunityAdmission.KnownUsable, "local", Array.Empty<CoursePoint>(), 1))
        });
        var source = new CombatOpportunitySource();
        var budget = new DecisionWorkBudget(double.PositiveInfinity, 1, () => 0, 1);
        var slice = source.Continue(snapshot, new DecisionWorkCursor(), budget);
        Require(slice.Examined.Count == 1 && slice.Examined[0].Admission == OpportunityAdmission.KnownUsable,
            "The captured use was not discovered as a concrete usable opportunity.");

        var state = new ProjectedCourseState(new CoursePoint(32, 32));
        var bound = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() }).Bind(slice.Examined[0], state,
            snapshot, new DecisionWorkCursor(), new DecisionWorkBudget(double.PositiveInfinity, 2, () => 0, 1));
        Require(bound.Binding != null && bound.Binding.Method == CombatCourseFacts.Method && bound.Binding.Tool == CombatCourseFacts.ToolId(0, 9, 2),
            "The binder did not retain the captured method and concrete tool.");
        Require(bound.Binding.ArrivalVelocity == new CoursePoint(3, 1) && bound.Binding.TravelTicks == 0,
            "A useful local shot did not use the captured route's arrival state.");
        Require(bound.Binding.Effects.Single().Evidence == EstimateStatus.Nominal && bound.Binding.Effects.Single().Amount == 12
            && bound.Binding.Effects.Single().NominalTick == 4,
            "The binder did not preserve the simulator's per-use target damage as a nominal effect.");
        Require(bound.Binding.Dependencies.Reads.Select(read => read.Key).SequenceEqual(new[]
            { CombatCourseFacts.ManaCapacityKey(), CombatCourseFacts.TargetKey(12, 3), CombatCourseFacts.UseKey(useId), CombatCourseFacts.WeaponKey(0),
              ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)) }.OrderBy(key => key)),
            "The binding omitted a captured input dependency.");
        Require(state.TryApply(bound.Binding, new System.Collections.Generic.Dictionary<string, double>(), out _),
            "the nominal combat prefix could not enter projected state");
        var dependent = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() }).Bind(slice.Examined[0],
            state, snapshot, new DecisionWorkCursor(), new DecisionWorkBudget(double.PositiveInfinity));
        Require(dependent.Binding == null && dependent.Reason == "combat-target-successor-unresolved",
            "a later shot treated the old observed target as the outcome of an uncertain earlier hit");
        var unsupported = use with { ExpectedTargetDamage = 0 };
        var unsupportedSnapshot = new DecisionFactSnapshot(45, 8, 16, 2, 0, new[]
        {
            Fact(CombatCourseFacts.TargetKey(12, 3), 3, target, 40), Fact(CombatCourseFacts.WeaponKey(0), 9, weapon),
            new DecisionFact(CombatCourseFacts.ManaCapacityKey(), 1, new FactValue(30), FactEvidence.Observed),
            Fact(CombatCourseFacts.UseKey(useId), 7, unsupported),
            Fact(ReadCourseTravel.Key(new CoursePoint(32, 32), default, new CoursePoint(32, 32)), 1,
                new CapturedCourseTravel(new CoursePoint(32, 32), default, new CoursePoint(32, 32), default, 0,
                    OpportunityAdmission.KnownUsable, "local", Array.Empty<CoursePoint>(), 1))
        });
        var unresolved = new BindOpportunity(new IOpportunityBinder[] { new CombatOpportunityBinder() }).Bind(slice.Examined[0],
            new ProjectedCourseState(new CoursePoint(32, 32)), unsupportedSnapshot, new DecisionWorkCursor(),
            new DecisionWorkBudget(double.PositiveInfinity, 2, () => 0, 1));
        Require(unresolved.Binding == null && unresolved.Admission == OpportunityAdmission.Unresolved
            && unresolved.Reason == "no-use-with-captured-travel-and-target-impact", "An unforecast use published a zero-damage combat effect.");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
