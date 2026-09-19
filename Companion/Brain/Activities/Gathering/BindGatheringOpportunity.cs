#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>Binds one native tool application from immutable native facts. The conditional successor
/// is the closed tool law under those facts; a live edit or changed tool requires revalidation before use.</summary>
public sealed class GatheringOpportunityBinder : IOpportunityBinder
{
    public GatheringOpportunityBinder(string domain)
    {
        if (domain is not "mine-target" and not "chop-target") throw new ArgumentOutOfRangeException(nameof(domain));
        Domain = domain;
    }
    public string Domain { get; }
    public static FactKey ReadyKey(string domain) => new("native-tool-ready", domain);
    public static NeedKey Need(GatheringOpportunityFact site) => new(NeedKind.NativeWork,
        site.Work == null ? site.Target : $"tile:{site.Material}:{site.TileX},{site.TileY}", site.Generation);
    public static string Tool(CapturedToolWork work) => $"tool:{work.ItemType}:{work.Prefix}:{work.Power}:{work.UseTime}";
    private static string Use(GatheringOpportunityFact site) => $"{site.Domain}:{site.TileX},{site.TileY}:{site.Material}";

    public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
        DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("bind-native-tool")) return Refuse("budget-cut", true);
        FactKey targetKey = new(Domain, opportunity.Key.Target, opportunity.Key.Generation);
        FactKey readyKey = ReadyKey(Domain);
        if (state.HasUnresolvedChange(targetKey) || state.HasUnresolvedChange(readyKey))
            return Refuse("native-tool-successor-unresolved");
        DecisionFact target = facts.Read(targetKey);
        if (target.Evidence != FactEvidence.Observed) return Refuse("native-target-unresolved");
        var site = JsonSerializer.Deserialize<GatheringOpportunityFact>(state.Read(targetKey, facts).Text);
        if (site?.Work is not { } work) return Refuse("native-tool-work-missing");
        if (site.Domain != Domain || site.Target != opportunity.Key.Target || site.Generation != opportunity.Key.Generation)
            throw new InvalidOperationException("Native tool fact disagrees with its opportunity identity.");
        if (site.Admission != "usable" || work.DamageRemaining <= 0)
            return new(null, OpportunityAdmission.KnownUnusable, "native-target-not-usable", false);
        if (work.DamagePerHit <= 0 || work.UseTime <= 0) return Refuse("native-tool-law-unresolved");
        string coverageKind = Domain == "mine-target" ? "mine-coverage" : "chop-coverage";
        if (facts.Read(new(coverageKind, "native-census")).Evidence != FactEvidence.Observed)
            return Refuse("native-census-unresolved");
        var pose = new CoursePoint(site.StandX, site.StandY);
        if (!double.IsFinite(pose.X) || !double.IsFinite(pose.Y)) return Refuse("native-working-pose-invalid");
        CapturedCourseTravel? travel = ReadCourseTravel.Read(state, pose, facts);
        if (travel == null)
        {
            var request = new CourseTravelRequest(state.Pose, state.Velocity, pose);
            return facts.Read(request.Key).Evidence == FactEvidence.Missing
                ? new(null, OpportunityAdmission.Unresolved, "native-travel-pending", true, new[] { request })
                : Refuse("native-travel-unresolved");
        }
        if (travel.Admission != OpportunityAdmission.KnownUsable)
            return new(null, travel.Admission, travel.Reason, travel.Admission == OpportunityAdmission.Unresolved);
        if (facts.Read(readyKey).Evidence != FactEvidence.Observed) return Refuse("native-tool-readiness-missing");
        double ready = state.Read(readyKey, facts).Amount;
        if (!double.IsFinite(ready) || ready < 0) return Refuse("native-tool-readiness-invalid");
        double readyAt = Math.Max(0, ready - facts.Tick);
        double arrival = state.Tick + travel.Ticks;
        double useAt = Math.Max(arrival, readyAt), completedAt = useAt + 1;
        double amount = Math.Min(work.DamagePerHit, work.DamageRemaining);
        UsefulNeed? need = opportunity.Needs.SingleOrDefault(n => n.Key == Need(site));
        if (need == null) return Refuse("native-work-need-missing");
        if (state.Remaining(need) <= 0) return new(null, OpportunityAdmission.KnownUnusable, "native-work-already-projected", false);
        // Reward allocation may have less left than a swing delivers. The native mechanism
        // still applies its whole hit; CompareCourseOutcomes caps credited useful work.
        var afterWork = work with { DamageRemaining = Math.Max(0, work.DamageRemaining - (int)amount) };
        var after = site with
        {
            Work = afterWork,
            RemainingAmount = Math.Max(0, site.RemainingAmount - amount),
            Admission = afterWork.DamageRemaining == 0 ? "unusable" : site.Admission,
            Reason = afterWork.DamageRemaining == 0 ? "projected-native-removal" : site.Reason,
        };
        long bindingId = CourseIdentity.Next();
        DependencyManifest dependencies = facts.Manifest();
        long[] parents = state.ReadEffects.ToArray();
        bool immediateTravel = travel.Ticks == 0 && travel.From == travel.To;
        var effect = new PredictedEffect(CourseIdentity.Next(), need.Key, amount, completedAt, completedAt,
            immediateTravel ? completedAt : double.PositiveInfinity,
            immediateTravel ? EstimateStatus.ModelBound : EstimateStatus.Nominal, parents.Append(bindingId), new[]
            {
                new EffectDelta(targetKey, new(Amount: after.RemainingAmount, Text: JsonSerializer.Serialize(after))),
                new EffectDelta(readyKey, new(Amount: facts.Tick + useAt + work.UseTime)),
            }, dependencies);
        var binding = new StepBinding(bindingId, opportunity.Key, site.Purpose, pose, Tool(work), facts.SnapshotId,
            facts.WorldEpoch, travel.Ticks, completedAt - arrival, 0, new[]
            {
                new ResourcePhase(CourseResource.Body, state.Tick, completedAt, 1),
                new ResourcePhase(CourseResource.Hand, useAt, completedAt, 1),
            }, new[] { effect }, parents, dependencies, true, travel.ArrivalVelocity, Use(site));
        return new(binding, OpportunityAdmission.KnownUsable, "native-tool-use-bound", false);
    }

    public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
    {
        if (binding.WorldEpoch != facts.WorldEpoch || binding.Opportunity.Domain != Domain)
            return new(OpportunityAdmission.KnownUnusable, "native-tool-epoch-or-domain-changed", true);
        if (!facts.TryRead(new(Domain, binding.Opportunity.Target, binding.Opportunity.Generation), out var fact)
            || fact.Evidence != FactEvidence.Observed)
            return new(OpportunityAdmission.Unresolved, "native-target-not-observed", true);
        var site = JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text);
        if (site?.Work is not { } work || site.Admission != "usable" || work.DamageRemaining <= 0)
            return new(OpportunityAdmission.KnownUnusable, "native-target-no-longer-usable", true);
        bool same = site.Domain == Domain && site.Target == binding.Opportunity.Target && site.Generation == binding.Opportunity.Generation
            && binding.Method == site.Purpose && binding.Tool == Tool(work) && binding.NativeUseId == Use(site)
            && binding.Pose == new CoursePoint(site.StandX, site.StandY);
        return new(same ? OpportunityAdmission.KnownUsable : OpportunityAdmission.KnownUnusable,
            same ? "native-tool-binding-retained" : "native-tool-application-changed", !same);
    }

    private static BindingResult Refuse(string reason, bool pending = false)
        => new(null, OpportunityAdmission.Unresolved, reason, pending);
}
