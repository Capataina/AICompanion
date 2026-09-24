extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>
/// The three assistance domains becoming steps a course can contain.
///
/// Until `AssistanceOpportunityBinder` existed, collect, light and pot had sources and no binder: they
/// were discovered, enumerated and priced, and could never produce a `StepBinding`. That failure is
/// invisible from either side — a course with no torch in it is not an error — so these rows exist to
/// make "this domain can enter a course" a checkable claim rather than an assumption.
/// </summary>
internal static class VerifyAssistanceCourseBindings
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            red += RunOneRow.GreenOrRed(name, test);
        }
        Row("G03 each assistance domain binds a step a course can hold", EachDomainBinds);
        Row("G03 an incomplete census refuses to bind", CensusGatesTheBind);
        Row("G03 missing travel is a pending request, not a refusal", MissingTravelIsPending);
        Row("G03 a hand is reserved for a use and not for a pickup", HandIsReservedOnlyForAUse);
        Row("G03 a moved drop retires its application, not its opportunity", MovedDropRetiresTheApplication);
        Row("G03 a changed torch item retires the binding that named the old one", ChangedTorchItemRetiresTheBinding);
        return red;
    }

    private const double Amount = 3;

    /// <summary>A usable site. The purpose is written out per shape the census mints — a torch site under
    /// `light-target`, a drop slot or a pot tile under `collect-target` — because the census writes it and a fixture
    /// that left it for the reader to infer would be testing an inference production never makes.</summary>
    private static AssistanceOpportunityFact Site(string domain, string target, double x = 40, double y = 30)
        => new(domain, domain == "light-target" ? OpportunityPurposes.Light
                : target.StartsWith("tile:", StringComparison.Ordinal) ? OpportunityPurposes.BreakPot : OpportunityPurposes.Collect,
            target, 1, x, y, Amount, Amount, "usable", "fixture", "fixture-detail");

    /// <param name="coverage">Whether the census fact reads Observed. A site captured under an
    /// unfinished census is work chosen against a world nobody finished looking at.</param>
    private static DecisionFactSnapshot Snapshot(AssistanceOpportunityFact site, double travel = 0, bool coverage = true)
    {
        string coverageKind = site.Domain.Replace("-target", "-coverage", StringComparison.Ordinal);
        var facts = new List<DecisionFact>
        {
            new(new(site.Domain, site.Target, site.Generation), 1, new(Amount: site.Amount, Text: JsonSerializer.Serialize(site)), FactEvidence.Observed),
            new(new(coverageKind, "native-census"), 1, new(Text: "exhaustive;fixture"),
                coverage ? FactEvidence.Observed : FactEvidence.Unresolved),
        };
        if (travel >= 0) facts.Add(Travel(new(20, 30), new(site.ContactX ?? site.X, site.ContactY ?? site.Y), travel));
        return new(1, 1, 100, 1, 0, facts);
    }

    private static DecisionFact Travel(CoursePoint from, CoursePoint to, double ticks) => new(ReadCourseTravel.Key(from, default, to), 1,
        new(Text: JsonSerializer.Serialize(new CapturedCourseTravel(from, default, to, default, ticks,
            OpportunityAdmission.KnownUsable, "fixture", new[] { from, to }, 1,
            ticks == 0 ? new[] { new TimedCoursePose(0, from, default) }
                : new[] { new TimedCoursePose(0, from, default), new TimedCoursePose(ticks, to, default) }))), FactEvidence.Modelled);

    private static Opportunity Offer(DecisionFactSnapshot snapshot, string domain)
        => new DiscoverAssistanceOpportunities(domain).Continue(snapshot, new(), new(double.PositiveInfinity)).Examined.Single();

    private static BindingResult Bind(DecisionFactSnapshot snapshot, string domain, ProjectedCourseState? state = null)
        => new BindOpportunity(new[] { new AssistanceOpportunityBinder(domain) })
            .Bind(Offer(snapshot, domain), state ?? new ProjectedCourseState(new(20, 30)), snapshot, new(), new(double.PositiveInfinity));

    /// <summary>The load-bearing row: all three domains produce a step, through their own real source
    /// and their own real binder.</summary>
    private static void EachDomainBinds()
    {
        foreach ((string domain, string target) in new[]
                 { ("collect-target", "item:7"), ("light-target", "tile:5,6"), ("collect-target", "tile:7,6") })
        {
            DecisionFactSnapshot snapshot = Snapshot(Site(domain, target));
            BindingResult result = Bind(snapshot, domain);
            StepBinding binding = result.Binding
                ?? throw new InvalidOperationException($"{domain} produced no step: {result.Reason}");
            Require(binding.Opportunity.Domain == domain && binding.Opportunity.Target == target,
                $"{domain} bound a step that names another opportunity");
            Require(binding.Effects.Count == 1 && binding.Effects.Single().Amount > 0,
                $"{domain} bound a step that predicts no useful effect, so it could never be worth choosing");
            Require(binding.UseProven, $"{domain} bound an unproven use");
        }
    }

    /// <summary>A binder checks the census too. Discovery deciding its own enumeration finished says
    /// nothing about whether this site was observed under a complete one.</summary>
    private static void CensusGatesTheBind()
    {
        BindingResult refused = Bind(Snapshot(Site("light-target", "tile:5,6"), coverage: false), "light-target");
        Require(refused.Binding == null && refused.Reason == "assistance-census-unresolved",
            $"a site captured under an unfinished census bound anyway; reason={refused.Reason}");
    }

    /// <summary>An absent travel fact is a question, not an answer. Refusing it outright would write the
    /// site off on evidence that does not exist; the typed request is what lets the owner produce it.</summary>
    private static void MissingTravelIsPending()
    {
        DecisionFactSnapshot snapshot = Snapshot(Site("collect-target", "item:7"), travel: -1);
        BindingResult pending = Bind(snapshot, "collect-target");
        Require(pending.Binding == null && pending.Admission == OpportunityAdmission.Unresolved,
            $"a missing travel answer produced a verdict rather than a question; admission={pending.Admission}");
        Require(pending.RequiredTravel is { Count: > 0 },
            "the pending bind named no travel request, so nothing could ever answer it");
    }

    /// <summary>A drop is taken by contact as the body arrives; a torch and a pot are a use the hand
    /// performs. Reserving a hand for a pickup would refuse a shot the arsenal could have taken.</summary>
    private static void HandIsReservedOnlyForAUse()
    {
        StepBinding drop = Bind(Snapshot(Site("collect-target", "item:7")), "collect-target").Binding!;
        StepBinding torch = Bind(Snapshot(Site("light-target", "tile:5,6")), "light-target").Binding!;
        StepBinding pot = Bind(Snapshot(Site("collect-target", "tile:7,6")), "collect-target").Binding!;
        Require(drop.Resources.All(phase => phase.Resource != CourseResource.Hand),
            "a pickup reserved the hand, which would refuse an opportunistic shot on the way past");
        // A pot shares collection's domain with a drop and is still a use the hand performs, so the hand follows the
        // site's purpose rather than its domain.
        Require(pot.Resources.Any(phase => phase.Resource == CourseResource.Hand),
            "breaking a pot reserved no hand although it shares collection's domain with a pickup; the hand must follow the act, not the domain");
        Require(torch.Resources.Any(phase => phase.Resource == CourseResource.Hand),
            "placing a torch reserved no hand, so the course believes it can shoot and place at once");
    }

    /// <summary>A drop that rolls is the same opportunity carrying new evidence. Its contact pose is part
    /// of the binding, so the *application* retires and the opportunity survives to be bound again.</summary>
    private static void MovedDropRetiresTheApplication()
    {
        AssistanceOpportunityFact original = Site("collect-target", "item:7");
        StepBinding binding = Bind(Snapshot(original), "collect-target").Binding!;
        var gate = new BindOpportunity(new[] { new AssistanceOpportunityBinder("collect-target") });
        Require(gate.ValidateNextUse(binding, Snapshot(original)).CanUse,
            "an unchanged drop did not retain its binding");
        Require(!gate.ValidateNextUse(binding, Snapshot(original with { X = 200 })).CanUse,
            "a drop that rolled kept a binding pointing at where it used to be");
    }

    /// <summary>
    /// The tool a binding names must be able to change, or naming it proves nothing.
    ///
    /// `ValidateNextUse` compares `binding.Tool` against the site's current tool. That comparison was
    /// unable to fail while `Tool()` returned a bare per-domain constant — the constant is implied by
    /// the domain equality checked two terms earlier, so deleting the whole comparison left every row
    /// green. A torch is the assistance case where the tool genuinely varies, because the item type
    /// decides what gets placed, so the binding names it and a swap retires the application.
    /// </summary>
    private static void ChangedTorchItemRetiresTheBinding()
    {
        AssistanceOpportunityFact original = Site("light-target", "tile:5,6") with { ItemType = 8 };
        StepBinding binding = Bind(Snapshot(original), "light-target").Binding
            ?? throw new InvalidOperationException("the torch scene did not bind at all");
        var gate = new BindOpportunity(new[] { new AssistanceOpportunityBinder("light-target") });
        Require(gate.ValidateNextUse(binding, Snapshot(original)).CanUse,
            "an unchanged torch did not retain its binding");
        Require(!gate.ValidateNextUse(binding, Snapshot(original with { ItemType = 974 })).CanUse,
            "a binding that named one torch item stayed valid against a different one, so the tool it names is decoration");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
