#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// Turns one captured assistance site into a bound step: a drop to take, a dark tile to light, a pot
/// to break. One class taking its domain rather than three, because the three differ only in what the
/// hand does on arrival and in which need they satisfy — and a shared binder means the travel gate,
/// the census gate and the identity checks cannot drift apart between them.
///
/// Until this existed these three domains could be discovered, enumerated and priced and could never
/// become a <see cref="StepBinding"/>, so no course could contain them. That failed silently: a course
/// with no torch in it is not an error, it is a course with no torch in it.
/// </summary>
public sealed class AssistanceOpportunityBinder : IOpportunityBinder
{
    public AssistanceOpportunityBinder(string domain)
    {
        if (domain is not "collect-target" and not "light-target" and not "pot-target")
            throw new ArgumentOutOfRangeException(nameof(domain));
        Domain = domain;
    }

    public string Domain { get; }

    /// <summary>The census-completeness fact this domain's discovery reads. A binder checks it too:
    /// discovery deciding its enumeration finished says nothing about whether this particular site was
    /// observed under a complete census, and a site bound from a partial one would be work chosen
    /// against a world nobody finished looking at.</summary>
    private FactKey CoverageKey => new(Domain.Replace("-target", "-coverage", StringComparison.Ordinal), "native-census");

    private NeedKind Kind => Domain switch
    {
        "collect-target" => NeedKind.Loot,
        "light-target" => NeedKind.Illumination,
        _ => NeedKind.Container,
    };

    /// <summary>A drop is taken by contact as the body arrives; a torch and a pot are a use the hand
    /// performs once there. That single tick difference is why collect reserves no hand: reserving one
    /// it never uses would refuse a shot the arsenal could legitimately have taken on the way past.</summary>
    private bool UsesHand => Domain != "collect-target";

    public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts,
        DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        if (!budget.TrySpend("bind-assistance-site")) return Refuse("budget-cut", true);
        FactKey targetKey = new(Domain, opportunity.Key.Target, opportunity.Key.Generation);
        if (state.HasUnresolvedChange(targetKey))
            return Refuse("assistance-successor-unresolved");

        DecisionFact target = facts.Read(targetKey);
        if (target.Evidence != FactEvidence.Observed) return Refuse("assistance-target-unresolved");
        var site = JsonSerializer.Deserialize<AssistanceOpportunityFact>(state.Read(targetKey, facts).Text);
        if (site == null) return Refuse("assistance-fact-unreadable");
        if (site.Domain != Domain || site.Target != opportunity.Key.Target || site.Generation != opportunity.Key.Generation)
            throw new InvalidOperationException("Assistance fact disagrees with its opportunity identity.");

        // The captured admission is the domain's own answer and its three values are kept apart: an
        // unusable site is refused for good, an unknown one is unresolved and may be asked again.
        if (site.Admission == "unusable")
            return new(null, OpportunityAdmission.KnownUnusable, site.Reason, false);
        if (site.Admission != "usable") return Refuse(site.Reason);
        if (facts.Read(CoverageKey).Evidence != FactEvidence.Observed) return Refuse("assistance-census-unresolved");

        // Where the body must be. A drop names the contact pose its own capture proved; a tile names
        // the site itself, and the positioner's tool-reach proof admits the hover beside it.
        var pose = new CoursePoint(site.ContactX ?? site.X, site.ContactY ?? site.Y);
        if (!double.IsFinite(pose.X) || !double.IsFinite(pose.Y)) return Refuse("assistance-pose-invalid");

        CapturedCourseTravel? travel = ReadCourseTravel.Read(state, pose, facts);
        if (travel == null)
        {
            var request = new CourseTravelRequest(state.Pose, state.Velocity, pose);
            return facts.Read(request.Key).Evidence == FactEvidence.Missing
                ? new(null, OpportunityAdmission.Unresolved, "assistance-travel-pending", true, new[] { request })
                : Refuse("assistance-travel-unresolved");
        }
        if (travel.Admission != OpportunityAdmission.KnownUsable)
            return new(null, travel.Admission, travel.Reason, travel.Admission == OpportunityAdmission.Unresolved);
        if (travel.ArrivalPose is not { } arrivalPose) return Refuse("assistance-arrival-pose-unresolved");

        var needKey = new NeedKey(Kind, site.Target, site.Generation);
        UsefulNeed? need = opportunity.Needs.SingleOrDefault(n => n.Key == needKey);
        if (need == null) return Refuse("assistance-need-missing");
        if (state.Remaining(need) <= 0)
            return new(null, OpportunityAdmission.KnownUnusable, "assistance-already-projected", false);

        double arrival = state.Tick + travel.Ticks;
        double completedAt = UsesHand ? arrival + 1 : arrival;
        double amount = Math.Min(Math.Max(0, site.Amount), state.Remaining(need));
        if (amount <= 0) return new(null, OpportunityAdmission.KnownUnusable, "assistance-nothing-left", false);

        // The site after this step: satisfied, and no longer offering what it just gave.
        var after = site with
        {
            Amount = Math.Max(0, site.Amount - amount),
            Admission = site.Amount - amount <= 0 ? "unusable" : site.Admission,
            Reason = site.Amount - amount <= 0 ? "projected-assistance-completed" : site.Reason,
        };

        long bindingId = CourseIdentity.Next();
        DependencyManifest dependencies = facts.Manifest();
        long[] parents = state.ReadEffects.ToArray();
        bool immediateTravel = travel.Ticks == 0 && travel.From == travel.To;
        var effect = new PredictedEffect(CourseIdentity.Next(), needKey, amount, completedAt, completedAt,
            immediateTravel ? completedAt : double.PositiveInfinity,
            immediateTravel ? EstimateStatus.ModelBound : EstimateStatus.Nominal, parents.Append(bindingId),
            new[] { new EffectDelta(targetKey, new(Amount: after.Amount, Text: JsonSerializer.Serialize(after))) },
            dependencies);

        var resources = UsesHand
            ? new[]
            {
                new ResourcePhase(CourseResource.Body, state.Tick, completedAt, 1),
                new ResourcePhase(CourseResource.Hand, arrival, completedAt, 1),
            }
            : new[] { new ResourcePhase(CourseResource.Body, state.Tick, completedAt, 1) };

        var binding = new StepBinding(bindingId, opportunity.Key, opportunity.Key.Purpose, pose, Tool(site),
            facts.SnapshotId, facts.WorldEpoch, travel.Ticks, completedAt - arrival, 0, resources,
            new[] { effect }, parents, dependencies, true, travel.ArrivalVelocity, Use(site), arrivalPose);
        return new(binding, OpportunityAdmission.KnownUsable, "assistance-site-bound", false);
    }

    public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
    {
        if (binding.WorldEpoch != facts.WorldEpoch || binding.Opportunity.Domain != Domain)
            return new(OpportunityAdmission.KnownUnusable, "assistance-epoch-or-domain-changed", true);
        if (!facts.TryRead(new(Domain, binding.Opportunity.Target, binding.Opportunity.Generation), out var fact)
            || fact.Evidence != FactEvidence.Observed)
            return new(OpportunityAdmission.Unresolved, "assistance-target-not-observed", true);
        var site = JsonSerializer.Deserialize<AssistanceOpportunityFact>(fact.Value.Text);
        if (site == null || site.Admission != "usable" || site.Amount <= 0)
            return new(OpportunityAdmission.KnownUnusable, "assistance-target-no-longer-usable", true);
        // A moved drop is the same opportunity with new evidence, and its pose is part of the binding,
        // so a changed contact pose retires this application rather than the opportunity.
        bool same = site.Domain == Domain && site.Target == binding.Opportunity.Target
            && site.Generation == binding.Opportunity.Generation && binding.NativeUseId == Use(site)
            && binding.Tool == Tool(site)
            && binding.Pose == new CoursePoint(site.ContactX ?? site.X, site.ContactY ?? site.Y);
        return new(same ? OpportunityAdmission.KnownUsable : OpportunityAdmission.KnownUnusable,
            same ? "assistance-binding-retained" : "assistance-application-changed", !same);
    }

    /// <summary>What the hand holds for this step. A drop needs nothing; a torch and a pot are named by
    /// their domain rather than by an item, because the companion's own mechanism performs them and the
    /// supply is checked live at use time rather than reserved here.</summary>
    private string Tool(AssistanceOpportunityFact site) => Domain switch
    {
        "collect-target" => "",
        "light-target" => "torch",
        _ => "pot-breaker",
    };

    private string Use(AssistanceOpportunityFact site) => $"{site.Domain}:{site.Target}";

    private static BindingResult Refuse(string reason, bool pending = false)
        => new(null, OpportunityAdmission.Unresolved, reason, pending);
}
