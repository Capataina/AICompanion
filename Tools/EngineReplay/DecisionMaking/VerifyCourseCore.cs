#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;

internal static class VerifyCourseCore
{
    private static readonly NeedKey Loot = new(NeedKind.Loot, "copper:0");
    private static readonly OpportunityKey Key = new("collect", "copper", "slot:1", 1);
    private static DecisionFactSnapshot Facts(long id = 1, long epoch = 1, params DecisionFact[] facts)
        => new(id, epoch, 100, 1, 0, facts);
    private static CourseComparisonEpisode Episode(params UsefulNeed[] needs)
        => new(1, 1, 100, needs.Length == 0 ? new[] { new UsefulNeed(Loot, 20, 20, 1) } : needs, true, false, "fixture");
    private static PredictedEffect Effect(long id, double amount, double tick, EstimateStatus status = EstimateStatus.NativeBound)
        => new(id, Loot, amount, tick, tick, tick, status, Array.Empty<long>(), Array.Empty<EffectDelta>(), DependencyManifest.Empty);
    private static StepBinding Binding(long id, PredictedEffect[] effects, DependencyManifest? dependencies = null,
        ResourcePhase[]? resources = null, OpportunityKey? key = null)
        => new(id, key ?? Key, "pickup", default, "none", 1, 1, 10, 1, 0,
            resources ?? Array.Empty<ResourcePhase>(), effects, Array.Empty<long>(), dependencies ?? DependencyManifest.Empty, true);
    private static CourseProjection Projection(params StepBinding[] steps)
        => new(steps, Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(), 30, true);
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
    private static void Close(double actual, double expected, string reason)
        => Require(Math.Abs(actual - expected) < 1e-10, reason + $" (actual {actual}, expected {expected})");

    public static int Run()
        => RunOneRow.Case("G03 finite completion beats omission", FiniteCompletion)
        + RunOneRow.Case("G03 partition and repeated forecasts conserve physical units", ConservedUnits)
        + RunOneRow.Case("G03 unrelated candidates cannot change harm or traversal scale", FixedScale)
        + RunOneRow.Case("G03 discount rebasing preserves the remaining future", Rebase)
        + RunOneRow.Case("G03 companionship integral uses dimensionless units", GapUnits)
        + RunOneRow.Case("G07 uncertain challengers wait for semantic boundaries", BoundaryRetention)
        + RunOneRow.Case("G08 changed facts dirty only dependent descendants", SpatialDependencies)
        + RunOneRow.Case("G08 refreshed reads preserve descendants and reject cycles atomically", DependencyRefresh)
        + RunOneRow.Case("G08 receipts allocate physical amount once", ReceiptConservation)
        + RunOneRow.Case("G09 resource conflicts include interior interval endpoints", Resources)
        + RunOneRow.Case("G15 in-flight effects cannot alter a successor before impact", DelayedEffects)
        + RunOneRow.Case("G11 one budget cuts all borrowers", SharedBudget)
        + RunOneRow.Case("G11 native route search borrows the same operation allowance", SharedRouteBudget)
        + RunOneRow.Case("G02 travel capture models terrain and preserves arrival momentum", CapturedTravel)
        + RunOneRow.Case("G11 cursor survives ticks and cache eviction stays visible", CursorAndStorage)
        + RunOneRow.Case("G11 finite source discovery survives tiny slices", FairDiscovery)
        + RunOneRow.Case("G14 missing captured facts remain missing", FactManifests)
        + RunOneRow.Case("G02 ordering search reaches sites beyond five", ConcreteOrders)
        + RunOneRow.Case("G11 repair publication retains course identity", AtomicPublication)
        + RunOneRow.Case("G07 re-observation cannot reorder accepted uses", ObservationCannotSelect)
        + RunOneRow.Case("G11 pending or unresolved repairs cannot publish", UnfinishedRepairCannotPublish)
        + RunOneRow.Case("G03 model labels cannot manufacture exact costs", CostUncertainty)
        + RunOneRow.Case("G15 completed uses release the executor while projectiles remain live", AdvanceWithOutstandingEffect)
        + RunOneRow.Case("G08 course replacement preserves in-flight receipt conservation", RetireAcrossCourseReplacement)
        + RunOneRow.Case("G05 external changes censor an issued forecast without deleting its projectile", ChangedOutstandingForecast)
        + RunOneRow.Case("G15 expired forecasts remain uncertain after time rebasing", ExpiredOutstandingForecast)
        + RunOneRow.Case("G08 re-observation cannot publish an invalid current use", ReobserveMustValidate)
        + RunOneRow.Case("G07 completed boundaries expire when the next use starts", BoundaryBelongsToOneUse)
        + RunOneRow.Case("G08 observed repair closes its replacement permission", ObservationClosesRepair)
        + RunOneRow.Case("G08 stale future steps preserve the valid executing prefix", SuffixFailurePreservesPrefix);

    private static void SuffixFailurePreservesPrefix()
    {
        var key = new FactKey("cargo", "free");
        var before = Facts(1, 1, new DecisionFact(key, 1, new(Amount: 5), FactEvidence.Observed));
        var reader = before.Track(); reader.Read(key);
        var prefix = Binding(1, Array.Empty<PredictedEffect>());
        var suffix = Binding(2, Array.Empty<PredictedEffect>(), reader.Manifest());
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        Require(owner.Consider(Projection(prefix, suffix), Episode(), before, usable, _ => usable), "Initial course refused.");
        owner.BeginExecution(1);
        var changed = Facts(1, 1, new DecisionFact(key, 2, new(Amount: 2), FactEvidence.Observed));
        var held = owner.Current;
        Require(!owner.ObserveRemaining(Projection(prefix, suffix), changed, _ => usable)
            && ReferenceEquals(held, owner.Current) && owner.Boundary == ExecutionBoundary.None
            && !owner.Repair.IsDirty(prefix.Id) && owner.Repair.IsDirty(suffix.Id),
            "A stale suffix revoked an unaffected executing prefix.");
        owner.Repair.Continue(owner.Dependencies, new(double.PositiveInfinity));
        var refreshed = changed.Track(); refreshed.Read(key);
        Require(owner.ObserveRemaining(Projection(prefix, Binding(2, Array.Empty<PredictedEffect>(), refreshed.Manifest())), changed, _ => usable)
            && owner.Repair.Dirty.Count == 0 && owner.Boundary == ExecutionBoundary.None,
            "Repaired suffix failed to close its repair without disturbing execution.");
        owner.InvalidNextUse("temporary-admission-change");
        owner.Repair.Continue(owner.Dependencies, new(double.PositiveInfinity));
        Require(owner.ObserveRemaining(owner.Current!.Projection, changed, _ => usable)
            && owner.Boundary == ExecutionBoundary.None, "A revalidated current use kept stale interruption authority.");
    }

    private static void AdvanceWithOutstandingEffect()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        var first = Binding(1, new[] { Effect(11, 8, 25), Effect(12, 4, 30) });
        var next = Binding(2, Array.Empty<PredictedEffect>());
        Require(owner.Consider(Projection(first, next), Episode(), Facts(), usable, _ => usable), "Initial course refused.");
        owner.ObserveExecution(new(1, 10, 1, "fire", "fire", "projectile-created", 100,
            ExecutionBoundary.NativeUseComplete, "released"), new long[] { 11 });
        Require(owner.Current!.Projection.Prefix?.Id == 2 && owner.Current.Projection.Steps.Count == 1,
            "A completed firing use remained executable.");
        Require(owner.Current.Projection.OutstandingEffects.Select(effect => effect.Id).SequenceEqual(new long[] { 11 })
            && owner.Current.Projection.ProjectionStartTick == 11,
            "Native projectile was forgotten or an unissued speculative effect became physical.");
        Require(owner.Dependencies.Children(1).Contains(11), "Detached impact lost its causal binding root.");
        Close(CompareCourseOutcomes.Evaluate(owner.Current.Projection, Episode()).UsefulEffects,
            .4 * Math.Exp(-.14), "Outstanding effect must be valued once from the remaining origin");
        owner.ObserveExecution(new(1, 10, 1, "fire", "fire", "projectile-created", 100,
            ExecutionBoundary.NativeUseComplete, "released"), new long[] { 11 });
        Require(owner.Current.Projection.Prefix?.Id == 2, "Duplicate completion advanced a second binding.");
        owner.ObserveEpoch(2);
        Require(owner.OutstandingEffects.Count == 0 && owner.Current == null, "Physical effects crossed a world epoch.");
    }

    private static void RetireAcrossCourseReplacement()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        owner.Consider(Projection(Binding(1, new[] { Effect(11, 8, 25) })), Episode(), Facts(), usable, _ => usable);
        owner.ObserveExecution(new(1, 10, 1, "fire", "fire", "projectile-created", 100,
            ExecutionBoundary.NativeUseComplete, "released"), new long[] { 11 });
        Require(owner.ApplyEffectReceipt(101, Loot, 3).Allocated.Sum(value => value.Amount) == 3, "First impact was not credited.");
        owner.Release("player-entered-cave");
        Require(owner.OutstandingEffects.Count == 1, "Cancelling travel erased a native projectile.");
        owner.RetireReceiptsThrough(101);
        Require(owner.Effects.Confirmed(11) == 3, "Retirement discarded a still-live effect's allocated amount.");
        var replacement = Projection(Binding(3, Array.Empty<PredictedEffect>()));
        Require(owner.Consider(replacement, Episode(), Facts(), usable, _ => usable)
            && owner.Current!.Projection.OutstandingEffects.Single().Amount == 5,
            "New course failed to inherit exactly the outstanding physical remainder.");
        var freshFacts = new DecisionFactSnapshot(2, 1, 120, 2, 101, Array.Empty<DecisionFact>());
        var refreshed = new CourseProjection(new[] { new StepBinding(3, Key, "pickup", default, "none", 2, 1,
            10, 1, 0, Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(), Array.Empty<long>(), DependencyManifest.Empty, true) },
            Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(), 30, true);
        owner.ObserveRemaining(refreshed, freshFacts, _ => usable);
        Require(owner.Current!.Projection.OutstandingEffects.Single().NominalTick == 5,
            "A later observation restarted the outstanding projectile's flight time.");
        var next = owner.ApplyEffectReceipt(102, Loot, 10);
        Require(next.Allocated.Sum(value => value.Amount) == 5 && next.Remainder == 5 && owner.Effects.Confirmed(11) == 8,
            "A later impact allocated more than the original conserved effect amount.");
        owner.ObserveEffectTerminal(11, true, "projectile-expired");
        owner.ObserveRemaining(refreshed, freshFacts, _ => usable);
        owner.RetireReceiptsThrough(102);
        Require(owner.OutstandingEffects.Count == 0 && owner.Effects.Confirmed(11) == 0,
            "Terminal native effect or its confirmation survived retirement without a live consumer.");
        Require(owner.ApplyEffectReceipt(101, Loot, 3).Duplicate, "A retired receipt was accepted again.");
    }

    private static void ExpiredOutstandingForecast()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        owner.Consider(Projection(Binding(1, new[] { Effect(11, 8, 25) })), Episode(), Facts(), usable, _ => usable);
        owner.ObserveExecution(new(1, 10, 1, "fire", "fire", "projectile-created", 100,
            ExecutionBoundary.NativeUseComplete, "released"), new long[] { 11 });
        owner.Release("rejoin");
        var later = new DecisionFactSnapshot(2, 1, 200, 2, 100, Array.Empty<DecisionFact>());
        var replacement = new StepBinding(3, Key, "pickup", default, "none", 2, 1, 10, 1, 0,
            Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(), Array.Empty<long>(), DependencyManifest.Empty, true);
        Require(owner.Consider(Projection(replacement), Episode(), later, usable, _ => usable), "Replacement refused.");
        var physical = owner.Current!.Projection.OutstandingEffects.Single();
        Require(physical.Id == 11 && physical.Evidence == EstimateStatus.Unresolved && physical.Delta.Count == 0,
            "Clamping an expired impact to tick zero turned it into a currently guaranteed effect.");
    }

    private static void ChangedOutstandingForecast()
    {
        var pose = new FactKey("enemy-pose", "slot:7", 1);
        var before = Facts(1, 1, new DecisionFact(pose, 1, new(X: 20), FactEvidence.Observed));
        var reads = before.Track(); reads.Read(pose);
        var forecast = new PredictedEffect(11, Loot, 8, 25, 25, 25, EstimateStatus.ModelBound,
            Array.Empty<long>(), new[] { new EffectDelta(pose, new(X: 40)) }, reads.Manifest());
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        owner.Consider(Projection(Binding(1, new[] { forecast })), Episode(), before, usable, _ => usable);
        owner.ObserveExecution(new(1, 1, 1, "fire", "fire", "projectile-created", 100,
            ExecutionBoundary.NativeUseComplete, "released"), new long[] { 11 });
        var after = Facts(2, 1, new DecisionFact(pose, 2, new(X: 80), FactEvidence.Observed));
        owner.Repair.Invalidate(owner.Dependencies, pose, "external-hit");
        owner.Repair.Continue(owner.Dependencies, new(double.PositiveInfinity));
        Require(owner.ObserveRemaining(Projection(), after, _ => usable), "Unrelated lawful continuation was frozen by an in-flight miss.");
        var outstanding = owner.Current!.Projection.OutstandingEffects.Single();
        Require(outstanding.Evidence == EstimateStatus.Unresolved && owner.OutstandingEffects.Count == 1
            && !CompareCourseOutcomes.Evaluate(owner.Current.Projection, Episode()).Total.HasJustifiedBounds,
            "A stale bounded impact survived external displacement, or its physical projectile was forgotten.");
        var state = new ProjectedCourseState(default, outstandingEffects: new[] { outstanding }, startTick: 30,
            completedCauses: owner.Current.Projection.CompletedCauses);
        Require(state.Read(pose, after.Track()).X == 80, "Stale hypothetical shove overwrote the realised enemy position.");
        Require(!owner.ReobserveOutstandingEffect(forecast, after), "A stale manifest refreshed the outstanding prediction.");
        var newReads = after.Track(); newReads.Read(pose);
        var revised = new PredictedEffect(11, Loot, 8, 35, 35, 35, EstimateStatus.ModelBound,
            Array.Empty<long>(), new[] { new EffectDelta(pose, new(X: 90)) }, newReads.Manifest());
        Require(owner.ReobserveOutstandingEffect(revised, after)
            && owner.ObserveRemaining(Projection(), after, _ => usable)
            && owner.Current!.Projection.OutstandingEffects.Single().Evidence == EstimateStatus.ModelBound,
            "A fresh native-state forecast could not replace the stale prediction under the same physical identity.");
    }

    private static void ReobserveMustValidate()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        var prefix = Binding(1, Array.Empty<PredictedEffect>());
        owner.Consider(Projection(prefix), Episode(), Facts(), usable, _ => usable);
        var before = owner.Current;
        Require(!owner.ObserveRemaining(Projection(prefix), Facts(),
                _ => new(OpportunityAdmission.KnownUnusable, "target-removed", true))
            && ReferenceEquals(before, owner.Current) && owner.Boundary == ExecutionBoundary.Invalidated,
            "The same-use check bypassed native validity and published an obsolete use.");
    }

    private static void BoundaryBelongsToOneUse()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        var first = Binding(1, Array.Empty<PredictedEffect>());
        var next = Binding(2, new[] { Effect(12, 10, 30, EstimateStatus.Nominal) });
        owner.Consider(Projection(first, next), Episode(), Facts(), usable, _ => usable);
        owner.ObserveExecution(new(1, 1, 1, "use", "use", "completed", 100, ExecutionBoundary.NativeUseComplete, "completed"));
        owner.ObserveRemaining(Projection(next), Facts(), _ => usable);
        var worse = Projection(Binding(3, new[] { Effect(13, 1, 40, EstimateStatus.Nominal) }));
        Require(!owner.Consider(worse, Episode(), Facts(), usable, _ => usable), "Worse boundary alternative won.");
        owner.BeginExecution(2);
        var uncertainBetter = Projection(Binding(4, new[] { Effect(14, 20, 10, EstimateStatus.Nominal) }));
        Require(owner.Boundary == ExecutionBoundary.None
            && !owner.Consider(uncertainBetter, Episode(), Facts(), usable, _ => usable),
            "An old completion authorised an uncertain interruption after the next use began.");
    }

    private static void ObservationClosesRepair()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        var next = Binding(2, Array.Empty<PredictedEffect>());
        owner.Consider(Projection(Binding(1, new[] { Effect(11, 8, 25) }), next), Episode(), Facts(), usable, _ => usable);
        owner.ObserveExecution(new(1, 1, 1, "fire", "fire", "projectile-created", 100,
            ExecutionBoundary.NativeUseComplete, "released"), new long[] { 11 });
        owner.ObserveEffectTerminal(11, false, "hit-wall");
        owner.Repair.Continue(owner.Dependencies, new(double.PositiveInfinity));
        long revision = owner.Current!.Revision;
        Require(owner.ObserveRemaining(Projection(next), Facts(), _ => usable)
            && owner.Repair.Dirty.Count == 0 && owner.Current!.Revision == revision + 1,
            "A realised repair left its old replacement permission open or failed to identify its revision.");
        var worse = new CourseProjection(new[] { next }, Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(),
            30, true, tailNominal: -10);
        Require(!owner.PublishTail(worse, Episode(), Facts(), _ => usable),
            "An already-resolved miss licensed an unrelated worse tail.");
    }

    private static void FiniteCompletion()
    {
        double done = CompareCourseOutcomes.Evaluate(Projection(Binding(1, new[] { Effect(1, 20, 100) })), Episode()).Total.Nominal;
        double absent = CompareCourseOutcomes.Evaluate(Projection(), Episode()).Total.Nominal;
        Require(done > absent, "A lone effect at its completion time must beat omitting it at equal costs.");
        Close(done, Math.Exp(-1), "Completion has the specified exponential value");
    }
    private static void ConservedUnits()
    {
        var whole = Projection(Binding(1, new[] { Effect(1, 20, 10) }));
        var split = Projection(Binding(2, new[] { Effect(2, 10, 10), Effect(3, 10, 10), Effect(4, 20, 10) }));
        Close(CompareCourseOutcomes.Evaluate(whole, Episode()).Total.Nominal,
            CompareCourseOutcomes.Evaluate(split, Episode()).Total.Nominal,
            "Stack partition or overlapping forecasts manufactured useful units");
    }
    private static void FixedScale()
    {
        var harm = new[] { new PredictedHarm(HarmActor.Companion, 10, 50, 10, EstimateStatus.NativeBound) };
        var course = new CourseProjection(Array.Empty<StepBinding>(), harm, Array.Empty<CompanionshipInterval>(), 20, true);
        var expanded = Episode(new(Loot, 20, 20, 1), new(new(NeedKind.Container, "pot"), 1, 1, 1));
        Close(CompareCourseOutcomes.Evaluate(course, Episode()).Harm,
            CompareCourseOutcomes.Evaluate(course, expanded).Harm, "Unrelated work repriced damage");
        Close(CourseComparisonEpisode.TimeScaleFor(60, 80, 2, 0), 50, "Time scale is base traversal geometry");
        Close(CourseComparisonEpisode.TimeScaleFor(60, 80, 0, 15), 15, "Immobile body uses its real local cycle");
    }
    private static void Rebase()
    {
        double before = Math.Exp(-40d / 100) + Math.Exp(-80d / 100) - .2 * Math.Exp(-60d / 100);
        double after = Math.Exp(-20d / 100) + Math.Exp(-60d / 100) - .2 * Math.Exp(-40d / 100);
        Close(before * Math.Exp(.2), after, "Remaining rewards and harm must share one time kernel");
        var earlier = CompareCourseOutcomes.Evaluate(Projection(Binding(1, new[] { Effect(1, 20, 20) })), Episode());
        var later = CompareCourseOutcomes.Evaluate(Projection(Binding(2, new[] { Effect(2, 20, 30) })), Episode());
        Require(earlier.Total.Nominal > later.Total.Nominal, "Earlier equal work cannot score worse.");
    }
    private static void GapUnits()
    {
        Close(CompareCourseOutcomes.GapIntegral(0, 100, 1, 1, 100), 1 - Math.Exp(-1), "Constant gap integrates once");
        double joined = CompareCourseOutcomes.GapIntegral(0, 100, 0, 1, 100);
        double divided = CompareCourseOutcomes.GapIntegral(0, 40, 0, .4, 100)
            + CompareCourseOutcomes.GapIntegral(40, 100, .4, 1, 100);
        Close(joined, divided, "Waypoint subdivision changed the cost");
    }
    private static void BoundaryRetention()
    {
        var held = Projection(Binding(1, new[] { Effect(1, 20, 30, EstimateStatus.Nominal) }));
        var rival = Projection(Binding(2, new[] { Effect(2, 20, 10, EstimateStatus.Nominal) }));
        Require(!CompareCourseOutcomes.Compare(1, Episode(), held, rival, ExecutionBoundary.None, true).Replace,
            "Uncalibrated nominal improvement interrupted a valid action.");
        Require(CompareCourseOutcomes.Compare(1, Episode(), held, rival, ExecutionBoundary.NativeEffect, true).Replace,
            "A completed native action must permit a better nominal continuation.");
        var provenHeld = Projection(Binding(1, new[] { Effect(1, 20, 30) }));
        var provenRival = Projection(Binding(2, new[] { Effect(2, 20, 10) }));
        Require(CompareCourseOutcomes.Compare(1, Episode(), provenHeld, provenRival, ExecutionBoundary.None, true).Replace,
            "A justified improvement must be allowed during travel.");
    }
    private static void SpatialDependencies()
    {
        var local = new FactKey("terrain", "chunk:1");
        var remote = new FactKey("terrain", "chunk:2");
        var index = new CourseDependencyIndex();
        index.Register(1, new(new[] { new FactRead(local, 1, "a", FactEvidence.Observed) }), Array.Empty<long>());
        index.Register(2, DependencyManifest.Empty, new long[] { 1 });
        index.Register(3, new(new[] { new FactRead(remote, 1, "b", FactEvidence.Observed) }), Array.Empty<long>());
        var repair = new RepairCourse(); repair.Invalidate(index, local, "local-edit");
        repair.Continue(index, new(double.PositiveInfinity, 1));
        Require(repair.Pending == 1 && repair.IsDirty(1) && repair.IsDirty(2) && !repair.IsDirty(3),
            "A cut must retain descendants without touching a remote binding.");
        repair.Continue(index, new(double.PositiveInfinity, 1));
        Require(repair.Pending == 0 && repair.Dirty.Count == 2, "Transitive repair did not resume.");
    }
    private static void ReceiptConservation()
    {
        var ledger = new CourseEffectLedger();
        var effects = new[] { Effect(1, 8, 10), Effect(2, 8, 10) };
        var first = ledger.Apply(7, Loot, 10, effects);
        Require(first.Allocated.Count == 2 && first.Allocated.Sum(a => a.Amount) == 10 && first.Remainder == 0,
            "One receipt must split physical amount without duplicating it.");
        Require(ledger.Apply(7, Loot, 10, effects).Duplicate && ledger.Confirmed(1) == 8 && ledger.Confirmed(2) == 2,
            "Duplicate receipt changed observed effect credit.");
    }
    private static void DependencyRefresh()
    {
        var index = new CourseDependencyIndex();
        var fact = new FactKey("tile", "1,2");
        index.Register(1, DependencyManifest.Empty, Array.Empty<long>());
        index.Register(2, DependencyManifest.Empty, new long[] { 1 });
        index.Register(1, new(new[] { new FactRead(fact, 2, "changed", FactEvidence.Observed) }), Array.Empty<long>());
        Require(index.Children(1).SequenceEqual(new long[] { 2 }), "Replacing reads erased effect descendants.");
        bool refused = false;
        try { index.Register(1, DependencyManifest.Empty, new long[] { 2 }); }
        catch (ArgumentException) { refused = true; }
        Require(refused && index.DirectUsers(fact).Contains(1) && index.Children(1).Contains(2),
            "A cycle was admitted or rejection changed the published dependency index.");
    }
    private static void Resources()
    {
        var state = new ProjectedCourseState(default);
        var crossing = Binding(2, Array.Empty<PredictedEffect>(), resources: new[]
        { new ResourcePhase(CourseResource.Hand, 0, 11, 1), new ResourcePhase(CourseResource.Hand, 5, 10, 1) });
        Require(!state.TryApply(crossing, new Dictionary<string, double>(), out var why) && why == "resource-conflict:Hand",
            "An interior overlapping hand use was not rejected.");
        Require(state.Tick == 0, "Rejected branch mutated hypothetical time.");
    }
    private static void DelayedEffects()
    {
        var target = new FactKey("enemy-position", "slot:4", 1);
        var facts = Facts(1, 1, new DecisionFact(target, 1, new(X: 10), FactEvidence.Observed));
        var effect = new PredictedEffect(1, Loot, 1, 20, 20, 20, EstimateStatus.ModelBound,
            Array.Empty<long>(), new[] { new EffectDelta(target, new(X: 40)) }, DependencyManifest.Empty);
        var state = new ProjectedCourseState(default);
        Require(state.TryApply(Binding(1, new[] { effect }), new Dictionary<string, double>(), out _), "Projectile prefix refused.");
        Require(state.Read(target, facts.Track()).X == 10 && state.ReadEffects.Count == 0,
            "A future hit changed the enemy before its impact.");
        var sibling = state.Fork(); state.AdvanceTo(20);
        Require(state.Read(target, facts.Track()).X == 40 && state.ReadEffects.SequenceEqual(new long[] { 1 }),
            "Due hypothetical effect lost its dependency provenance.");
        Require(sibling.Read(target, facts.Track()).X == 10, "Advancing one branch changed its sibling.");
    }
    private static void SharedBudget()
    {
        long clock = 0;
        var budget = new DecisionWorkBudget(4, 2, () => clock, 1000);
        Require(budget.TrySpend("combat") && budget.TrySpend("repair") && !budget.TrySpend("route"), "Borrowers received separate operation pools.");
        Require(budget.FirstCutSubsystem == "route" && budget.OperationsUsed == 2, "Budget cut attribution missing.");
        var timed = new DecisionWorkBudget(4, long.MaxValue, () => clock, 1000);
        clock = 4;
        Require(!timed.TrySpend("combat"), "Four milliseconds did not cut at four milliseconds.");
        clock = 7;
        Close(timed.OverrunMilliseconds, 3, "Atomic overrun must be visible");
    }
    private static void SharedRouteBudget()
    {
        var world = new TextTileWorld(0, 0, new[] { "##########", "#........#", "#........#", "#........#", "##########" });
        var search = new FreeSpaceSearch(world, new(2, 2), new(8, 2), priceClearance: false);
        var empty = new DecisionWorkBudget(double.PositiveInfinity, 0);
        LimitPlanningWork.Restart(empty);
        try
        {
            Require(!search.Advance(1000) && search.Expansions == 0 && empty.FirstCutSubsystem == "free-space",
                "A route search expanded after another borrower spent the allowance.");
        }
        finally { LimitPlanningWork.End(); }
        int slices = 0;
        while (!search.Finished && slices++ < 100)
        {
            var one = new DecisionWorkBudget(double.PositiveInfinity, 1);
            LimitPlanningWork.Restart(one);
            try
            {
                int before = search.Expansions;
                search.Advance(1000);
                Require(search.Expansions - before <= 1 && one.OperationsUsed <= 1,
                    "Route search escaped its borrowed single-operation slice.");
            }
            finally { LimitPlanningWork.End(); }
        }
        Require(search.Stop == FreeSpaceSearch.StopReason.Found && search.RouteCorners() is { Count: > 1 },
            "Budget cuts discarded the route frontier instead of resuming it.");
    }
    private static void CapturedTravel()
    {
        var world = new TextTileWorld(0, 0, new[]
        {
            "####################", "#..................#", "#..................#", "#........##........#",
            "#........##........#", "#........##........#", "#........##........#", "#..................#",
            "#..................#", "####################"
        });
        var from = new CoursePoint(48, 80);
        var to = new CoursePoint(272, 80);
        Require(!CircleContact.SweptClear(world, new(48, 80), new(272, 80)), "The route contrast needs a real intervening wall.");
        var capture = new CaptureCourseTravel(world, from, default, to, 1);
        DecisionFact? result = null;
        for (int slice = 0; slice < 10000 && result == null; slice++)
            result = capture.Continue(new(double.PositiveInfinity, 1));
        Require(result != null, "One-operation slices never completed the native travel model.");
        var state = new ProjectedCourseState(from);
        var reader = Facts(1, 1, result!).Track();
        var travel = ReadCourseTravel.Read(state, to, reader);
        Require(travel?.Admission == OpportunityAdmission.KnownUsable && travel.Ticks > from.DistanceTo(to) / OrbPace.MaxSpeed
            && travel.Route.Count > 2 && reader.Manifest().Reads.Single().Key == capture.Key,
            "Travel was priced as a straight line, lost route evidence, or bypassed tracked reads.");
        Require(travel!.TimedRouteSamples is { Count: > 2 } samples && samples.Count <= travel.Route.Count + 1
            && samples[0].Tick == 0 && samples[0].Position == from
            && samples[^1].Tick == travel.Ticks && samples[^1].Velocity == travel.ArrivalVelocity
            && samples[^1].Position.DistanceTo(to) <= Navigator.ArriveDistance
            && samples.Zip(samples.Skip(1)).All(pair => pair.First.Tick < pair.Second.Tick),
            "Native travel lost timed body states, invented exact arrival, or stored an unbounded per-tick trace.");
        var step = new StepBinding(1, Key, "pickup", to, "none", 1, 1, travel!.Ticks, 0, 0,
            Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(), Array.Empty<long>(), reader.Manifest(), true,
            travel.ArrivalVelocity);
        Require(state.TryApply(step, new Dictionary<string, double>(), out _) && state.Velocity == travel.ArrivalVelocity
            && state.Fork().Velocity == state.Velocity, "Projection silently restarted the next leg from rest.");
        var identical = capture.Continue(new(double.PositiveInfinity, 0));
        Require(ReferenceEquals(result, identical), "A completed unchanged route was recomputed.");
        world.Set(9, 2, '#');
        var changed = capture.Continue(new(double.PositiveInfinity, 0));
        Require(capture.Stale && changed!.Evidence == FactEvidence.Unresolved,
            "A geometry edit inside the route model's reads retained stale arrival evidence.");
    }
    private static void CursorAndStorage()
    {
        var cursor = new DecisionWorkCursor(); cursor.Bind(1, "start"); cursor.Advance(7);
        Require(!cursor.Bind(1, "next-tick") && cursor.Offset == 7, "Tick advance reset unfinished discovery.");
        var cache = new DecisionStorage<int, string>(2);
        cache.Put(1, "current", true); cache.Put(2, "optional"); cache.Put(3, "new");
        Require(cache.TryGet(1, out _) && !cache.TryGet(2, out _) && cache.Evictions == 1, "Eviction lost the live binding.");
        cache.Pin(3, true);
        Require(!cache.Put(4, "refused") && cache.Refused == 1, "A full pinned cache must expose lost coverage.");
    }
    private static void FairDiscovery()
    {
        var left = new FixtureSource("left", 7); var right = new FixtureSource("right", 7);
        var discovery = new DiscoverOpportunities(new[] { left, right }, 20);
        for (int i = 0; i < 14; i++) discovery.Continue(Facts(), new(double.PositiveInfinity, 2), Array.Empty<OpportunityKey>());
        Require(left.Examined >= 7 && right.Examined >= 7 && discovery.Candidates.Count == 14,
            "Tiny positive slices must examine both complete finite source sets.");
    }
    private static void FactManifests()
    {
        var key = new FactKey("entity", "npc:4", 2);
        var facts = Facts(1, 1, new DecisionFact(key, 4, new(30), FactEvidence.Observed));
        var reader = facts.Track(); reader.Read(key); reader.Read(new("terrain", "uncaptured"));
        Require(!reader.Manifest().Complete, "A missing read was certified complete.");
        var changed = Facts(2, 1, new DecisionFact(key, 5, new(29), FactEvidence.Observed));
        Require(reader.Manifest().Changed(changed).Contains(key), "Changed native state did not dirty its reader.");
    }
    private static void ConcreteOrders()
    {
        var source = new FixtureSource("collect", 7);
        var opportunities = source.Continue(Facts(), new(), new(double.PositiveInfinity, 20)).Examined;
        // This projector takes one operation per concrete step and retains its offset across
        // cuts. Search calls the production objective, not a fixture ranking function.
        var projector = new FixtureProjector(opportunities);
        var search = new SearchCourseOrders(7);
        search.Begin(Facts(), Episode(), opportunities, Array.Empty<OpportunityKey>(), projector);
        int ticks = 0;
        while (!search.Exhausted && ticks++ < 1000) search.Continue(new(double.PositiveInfinity, 1));
        Require(search.Exhausted && search.Best?.Steps.Count == 7 && projector.Seen.Count == 7,
            "Concrete search lost candidates beyond five or could not resume a suffix.");
    }
    private static void AtomicPublication()
    {
        var owner = new RetainCourse();
        var good = new BindingValidation(OpportunityAdmission.KnownUsable, "native-valid", false);
        var first = Binding(1, new[] { Effect(11, 10, 20) });
        Require(owner.Consider(Projection(first), Episode(), Facts(), good, _ => good), "Cold start rejected proven use.");
        long id = owner.Current!.Id;
        var tail = Binding(2, new[] { Effect(12, 10, 30) });
        Require(owner.PublishTail(Projection(first, tail), Episode(), Facts(), _ => good)
            && owner.Current!.Id == id && owner.Current.Revision == 2, "Same-purpose repair recreated the course.");
        Require(!owner.PublishTail(Projection(tail), Episode(), Facts(), _ => good), "Tail repair replaced the executor without comparison.");
        owner.ObserveEpoch(2);
        Require(owner.Current == null, "World reset retained a stale executable course.");
    }

    private static void ObservationCannotSelect()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        var first = Binding(1, new[] { Effect(11, 10, 20) });
        var second = Binding(2, new[] { Effect(12, 10, 30) });
        owner.Consider(Projection(first, second), Episode(), Facts(), usable, _ => usable);
        var before = owner.Current;
        foreach (var changed in new[] { Projection(second, first), Projection(second), Projection() })
        {
            bool rejected = false;
            try { owner.ObserveRemaining(changed, Facts(), _ => usable); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && ReferenceEquals(before, owner.Current), "Re-observation changed the accepted executable order.");
        }
        owner.ObserveRemaining(Projection(first, second), Facts(), _ => usable);
        Require(owner.Current!.Id == before!.Id && owner.Current.Revision == before.Revision
            && owner.Boundary == ExecutionBoundary.None, "Refreshing a future created a selection boundary.");
    }

    private static void UnfinishedRepairCannotPublish()
    {
        var owner = new RetainCourse();
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "usable", false);
        var first = Binding(1, new[] { Effect(11, 10, 20) });
        var tail = Binding(2, new[] { Effect(12, 10, 30) });
        owner.Consider(Projection(first), Episode(), Facts(), usable, _ => usable);
        var before = owner.Current;
        owner.InvalidNextUse("changed-fact");
        Require(!owner.PublishTail(Projection(first, tail), Episode(), Facts(), _ => usable)
            && !owner.Consider(Projection(tail), Episode(), Facts(), usable, _ => usable)
            && ReferenceEquals(before, owner.Current) && owner.Repair.Pending > 0,
            "A pending traversal was erased by publication.");
        owner.Repair.Continue(owner.Dependencies, new(double.PositiveInfinity));
        var absent = new DependencyManifest(new[] { new FactRead(new("tile", "uncaptured"), 1, "missing", FactEvidence.Missing) });
        var unresolved = Binding(2, new[] { Effect(12, 10, 30) }, absent);
        Require(!owner.PublishTail(Projection(first, unresolved), Episode(), Facts(), _ => usable)
            && ReferenceEquals(before, owner.Current) && owner.Repair.Dirty.Count > 0,
            "An unresolved suffix cleared the dirty repair.");
        Require(owner.PublishTail(Projection(first, tail), Episode(), Facts(), _ => usable)
            && owner.Repair.Dirty.Count == 0, "A fully validated repair could not publish.");
    }

    private static void CostUncertainty()
    {
        var plain = Projection(Binding(1, new[] { Effect(11, 20, 30) }));
        var nominalHarm = new CourseProjection(plain.Steps,
            new[] { new PredictedHarm(HarmActor.Companion, 1, 100, 10, EstimateStatus.ModelBound) },
            Array.Empty<CompanionshipInterval>(), 30, true);
        Require(!CompareCourseOutcomes.Evaluate(nominalHarm, Episode()).Total.HasJustifiedBounds,
            "A model label with no numerical damage bounds became exact.");
        var bounded = new CourseProjection(plain.Steps,
            new[] { new PredictedHarm(HarmActor.Companion, 10, 100, 20, EstimateStatus.ModelBound, new(5, 15), new(10, 30)) },
            new[] { new CompanionshipInterval(0, 30, 1, 1, EstimateStatus.ModelBound, new(0, 2), new(0, 2)) }, 30, true);
        var value = CompareCourseOutcomes.Evaluate(bounded, Episode());
        Require(value.Total.HasJustifiedBounds && value.Total.Lower < value.Total.Nominal && value.Total.Upper > value.Total.Nominal,
            "Numerical harm and gap bounds did not widen the objective interval.");
        Close(value.SelfHarm.Lower, .05 * Math.Exp(-.3), "Lower harm pairs minimum damage with latest impact");
        Close(value.SelfHarm.Upper, .15 * Math.Exp(-.1), "Upper harm pairs maximum damage with earliest impact");
        var census = new CourseComparisonEpisode(1, 1, 100, Episode().Needs, false, false, "partial");
        Require(!CompareCourseOutcomes.Evaluate(plain, census).Total.HasJustifiedBounds, "An unfinished census certified exact preference.");
    }

    private sealed class FixtureSource(string name, int count) : IOpportunitySource
    {
        public string Name => name;
        public int Examined { get; private set; }
        public OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            var result = new List<Opportunity>();
            while (cursor.Offset < count && budget.TrySpend("source"))
            {
                long index = cursor.Offset; cursor.Advance(); Examined++;
                result.Add(new(new(name, "group", index.ToString(), 1), 1, new(index, 0), OpportunityAdmission.KnownUsable,
                    "observed", new[] { new UsefulNeed(Loot, 20, 20, 1) }, new[] { "pickup" }, DependencyManifest.Empty));
            }
            if (cursor.Offset == count) cursor.Complete();
            return new(result, new(name, facts.WorldEpoch, cursor.Offset, count, cursor.Exhausted, budget.Cut, "fixture-line"));
        }
    }
    private sealed class FixtureProjector(IReadOnlyList<Opportunity> sites) : ICourseProjector
    {
        private readonly List<StepBinding> steps = new();
        private long epoch = -1;
        public HashSet<OpportunityKey> Seen { get; } = new();
        public CourseProjectionResult Continue(IReadOnlyList<OpportunityKey> order, DecisionFactSnapshot facts,
            CourseComparisonEpisode episode, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            if (epoch != cursor.Epoch) { epoch = cursor.Epoch; steps.Clear(); }
            while (cursor.Offset < order.Count)
            {
                if (!budget.TrySpend("projection")) return new(ProjectionStatus.Pending, null, "cut");
                int index = (int)cursor.Offset;
                var key = order[index]; Seen.Add(key);
                steps.Add(Binding(CourseIdentity.Next(), new[] { Effect(CourseIdentity.Next(), 20d / sites.Count, index + 1) }, key: key));
                cursor.Advance();
            }
            return new(ProjectionStatus.Complete, Projection(steps.ToArray()), "projected");
        }
    }
}
