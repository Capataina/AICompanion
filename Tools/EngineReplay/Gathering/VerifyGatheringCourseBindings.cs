extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Gathering;
using live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

internal static class VerifyGatheringCourseBindings
{
    public static int Run() => RunOneRow.Case("G09 tool binding preserves readiness between native uses", CooldownAndSuccessor)
        + RunOneRow.Case("G15 nominal tool travel cannot certify an enabling successor", NominalTravel)
        + RunOneRow.Case("G15 captured pick binding predicts one actual native strike", NativePick)
        + RunOneRow.Case("G03 partial reward cannot shrink a physical tool strike", PartialReward)
        + RunOneRow.Case("G09 missing tool travel requests resume without retrying concluded uncertainty", MissingTravel);

    private static void MissingTravel()
    {
        var site = Site() with { StandX = 40 };
        var complete = Snapshot(site, 5, 0);
        var key = ReadCourseTravel.Key(new(20, 30), default, new(40, 30));
        var missing = new DecisionFactSnapshot(1, 1, 100, 1, 0, complete.Facts.Where(fact => fact.Key != key));
        var gate = new BindOpportunity(new[] { new GatheringOpportunityBinder("mine-target") });
        var opportunity = Opportunity(missing);
        var state = new ProjectedCourseState(new(20, 30));
        var cursor = new DecisionWorkCursor();
        var pending = gate.Bind(opportunity, state, missing, cursor, new(double.PositiveInfinity));
        Require(pending.Pending && pending.RequiredTravel?.Single().Key == key,
            "missing native travel did not supply its exact physical query");
        Require(gate.Bind(opportunity, state, complete, cursor, new(double.PositiveInfinity)).Binding != null,
            "a completed travel extension failed to resume the native tool binding");
        var unresolved = new DecisionFactSnapshot(1, 1, 100, 1, 0, complete.Facts.Select(fact => fact.Key == key
            ? new DecisionFact(fact.Key, fact.Version, fact.Value, FactEvidence.Unresolved) : fact));
        var refused = gate.Bind(opportunity, state, unresolved, new(), new(double.PositiveInfinity));
        Require(!refused.Pending && refused.Binding == null && refused.RequiredTravel == null,
            "a concluded unresolved route was requeued as an unfinished query");
    }

    private static void PartialReward()
    {
        var site = Site();
        var snapshot = Snapshot(site, 0, 0);
        var original = Opportunity(snapshot);
        var opportunity = new Opportunity(original.Key, original.Revision, original.Target, original.Admission,
            original.Reason, original.Needs.Select(need => need with { RemainingAmount = 7.5 }),
            original.Methods, original.Dependencies);
        var state = new ProjectedCourseState(new(20, 30));
        var gate = new BindOpportunity(new[] { new GatheringOpportunityBinder("mine-target") });
        var binding = gate.Bind(opportunity, state, snapshot, new(), new(double.PositiveInfinity)).Binding
            ?? throw new InvalidOperationException("partial useful work failed to bind");
        var effect = binding.Effects.Single();
        Require(effect.Amount == 20 && opportunity.Needs.Single().Worth(effect.Amount) == 7.5 / 200,
            "physical damage and credited useful work were conflated");
        Require(state.TryApply(binding, new Dictionary<string, double>(), out _), "partial reward projection failed");
        var after = JsonSerializer.Deserialize<GatheringOpportunityFact>(state.Read(
            new("mine-target", site.Target, site.Generation), snapshot.Track()).Text)!;
        Require(after.Work!.DamageRemaining == 80 && after.RemainingAmount == 180,
            "a fractional reward claim changed the native successor damage");
    }

    private static void CooldownAndSuccessor()
    {
        var site = Site();
        DecisionFactSnapshot snapshot = Snapshot(site, 0, 103);
        var state = new ProjectedCourseState(new(20, 30));
        var binder = new GatheringOpportunityBinder("mine-target");
        var gate = new BindOpportunity(new[] { binder });
        Opportunity opportunity = Opportunity(snapshot);
        StepBinding first = gate.Bind(opportunity, state, snapshot, new(), new(double.PositiveInfinity)).Binding
            ?? throw new InvalidOperationException("ready native input failed to bind");
        Require(first.UseTicks == 4 && first.Effects.Single().Amount == 20,
            "binding must wait three ticks then apply one swing, not credit a whole vein");
        Require(first.Resources.Single(resource => resource.Resource == CourseResource.Hand).StartTick == 3,
            "waiting for a native tool must not occupy the hand before its actual use");
        Require(state.TryApply(first, new Dictionary<string, double>(), out _), "native successor failed projection");
        StepBinding second = gate.Bind(opportunity, state, snapshot, new(), new(double.PositiveInfinity)).Binding
            ?? throw new InvalidOperationException("closed native successor failed to bind");
        Require(second.UseTicks == 10 && second.Parents.Contains(first.Effects.Single().Id),
            "the next tool application ignored cooldown or omitted its causal predecessor");
        Require(gate.ValidateNextUse(first, snapshot).CanUse, "unchanged native binding did not retain identity");
        var replaced = site with { Work = site.Work! with { Power = 999 } };
        Require(!gate.ValidateNextUse(first, Snapshot(replaced, 0, 103)).CanUse,
            "an accepted native use silently substituted a changed tool");
    }

    private static void NominalTravel()
    {
        var site = Site() with { StandX = 40 };
        var snapshot = Snapshot(site, 5, 0);
        var state = new ProjectedCourseState(new(20, 30));
        var gate = new BindOpportunity(new[] { new GatheringOpportunityBinder("mine-target") });
        Opportunity opportunity = Opportunity(snapshot);
        StepBinding first = gate.Bind(opportunity, state, snapshot, new(), new(double.PositiveInfinity)).Binding
            ?? throw new InvalidOperationException("nominal travel lost a legal useful prefix");
        Require(first.Effects.Single().Evidence == EstimateStatus.Nominal,
            "a nominal arrival estimate acquired an exact effect-time bound");
        Require(state.TryApply(first, new Dictionary<string, double>(), out _), "nominal prefix could not project");
        BindingResult next = gate.Bind(opportunity, state, snapshot, new(), new(double.PositiveInfinity));
        Require(next.Binding == null && next.Reason == "native-tool-successor-unresolved",
            "a speculative predecessor was replaced by stale observed tool readiness");
    }

    private static void NativePick()
    {
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        var capture = new CaptureGatheringOpportunities();
        var facts = capture.Capture(context, new(double.PositiveInfinity)).ToList();
        GatheringOpportunityFact site = JsonSerializer.Deserialize<GatheringOpportunityFact>(facts.Single(fact => fact.Key.Kind == "mine-target").Value.Text)!;
        Require(site.Work != null && site.Admission == "usable", "native pick fixture needs a fully observed working tile");
        context.Npc.Center = new Vector2((float)site.StandX, (float)site.StandY);
        var pose = new CoursePoint(site.StandX, site.StandY);
        facts.Add(Travel(pose, pose, 0));
        var snapshot = new DecisionFactSnapshot(1, 1, (long)Main.GameUpdateCount, 1, 0, facts);
        var gate = new BindOpportunity(new[] { new GatheringOpportunityBinder("mine-target") });
        var binding = gate.Bind(Opportunity(snapshot), new ProjectedCourseState(pose), snapshot, new(), new(double.PositiveInfinity)).Binding
            ?? throw new InvalidOperationException("native captured pick did not bind");
        Item pick = TileMiner.PickaxeFor(context.Player);
        Require(context.Companion.Miner.Swing(new(25, 59), pick), "the accepted native mechanism did not swing");
        int remaining = context.Companion.Miner.EstimateRemaining(new(25, 59), pick)?.DamageRemaining ?? 0;
        Require(binding.Effects.Single().Amount == site.Work!.DamageRemaining - remaining,
            "predicted native work differs from the actual pick damage/removal");
    }

    private static GatheringOpportunityFact Site() => new("mine-target", "ore:7:1,1", 1, 1, 1, 7, "mine",
        200, 200, "usable", "native", "fixture", 20, 30, new(10, 0, 35, 10, 20, 100));
    private static DecisionFactSnapshot Snapshot(GatheringOpportunityFact site, double travel, double ready)
        => new(1, 1, 100, 1, 0, new[]
        {
            new DecisionFact(new("mine-target", site.Target, site.Generation), 1, new(Text: JsonSerializer.Serialize(site)), FactEvidence.Observed),
            new DecisionFact(new("mine-coverage", "native-census"), 1,
                new(Text: JsonSerializer.Serialize(new GatheringCoverageFact("mine-coverage", 0, 1, true, "fixture"))), FactEvidence.Observed),
            new DecisionFact(GatheringOpportunityBinder.ReadyKey("mine-target"), 1, new(Amount: ready), FactEvidence.Observed),
            Travel(new(20, 30), new(site.StandX, site.StandY), travel),
        });
    private static DecisionFact Travel(CoursePoint from, CoursePoint to, double ticks) => new(ReadCourseTravel.Key(from, default, to), 1,
        new(Text: JsonSerializer.Serialize(new CapturedCourseTravel(from, default, to, default, ticks,
            OpportunityAdmission.KnownUsable, "fixture", new[] { from, to }, 1,
            ticks == 0 ? new[] { new TimedCoursePose(0, from, default) }
                : new[] { new TimedCoursePose(0, from, default), new TimedCoursePose(ticks, to, default) }))), FactEvidence.Modelled);
    private static Opportunity Opportunity(DecisionFactSnapshot snapshot) => new GatheringOpportunitySource("mine-target")
        .Continue(snapshot, new(), new(double.PositiveInfinity)).Examined.Single();
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
