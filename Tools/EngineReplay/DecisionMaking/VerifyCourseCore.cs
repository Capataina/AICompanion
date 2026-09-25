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
    // The second field is the *purpose* and it has to be one `ExecuteCourseBinding` can execute: the
    // search refuses an opportunity whose purpose names no activity, so "copper" — which read as a
    // material here — was dropped before enumeration and left every ordering row measuring an empty
    // search. The material belongs in the target, where it already is.
    private static readonly OpportunityKey Key = new("collect", "collect", "slot:1", 1);
    private static DecisionFactSnapshot Facts(long id = 1, long epoch = 1, params DecisionFact[] facts)
        => new(id, epoch, 100, 1, 0, facts);
    private static CourseComparisonEpisode Episode(params UsefulNeed[] needs)
        => new(1, 1, 100, needs.Length == 0 ? new[] { new UsefulNeed(Loot, 20, 20, 1) } : needs, true, 0, "fixture");
    private static PredictedEffect Effect(long id, double amount, double tick, EstimateStatus status = EstimateStatus.NativeBound)
        => new(id, Loot, amount, tick, tick, tick, status, Array.Empty<long>(), Array.Empty<EffectDelta>(), DependencyManifest.Empty);
    private static StepBinding Binding(long id, PredictedEffect[] effects, DependencyManifest? dependencies = null,
        ResourcePhase[]? resources = null, OpportunityKey? key = null)
        => new(id, key ?? Key, "pickup", default, "none", 1, 1, 10, 1, 0,
            resources ?? Array.Empty<ResourcePhase>(), effects, Array.Empty<long>(), dependencies ?? DependencyManifest.Empty, true);
    private static CourseProjection Projection(params StepBinding[] steps)
        => new(steps, Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(), 30, true);
    /// <summary>
    /// Survival-first is a rule *between fights*, and an empty course is not a fight.
    ///
    /// `NominalOrder` compares companion harm before anything else during an encounter, which is right
    /// between two courses that both take the fight: the one that survives it wins even when it kills
    /// less. Applied with no qualifier it also says that doing nothing beats any fight that costs a
    /// single point, because an empty course has no effects and therefore no harm — the plan's own G13
    /// mutation `idle artificially wins by zero damage`, which lane G found live in production on
    /// 22 September 2026 and left for this folder to rule on. For an idle course I and a fight F it
    /// reduced to `F.CompanionHarm.CompareTo(0)`, positive whenever the fight costs anything at all, and
    /// `SearchCourseOrders` runs exactly that comparison against its incumbent best on every priced order.
    ///
    /// Three arms, because the fix has two ways to be wrong and only one to be right. The first is the
    /// defect: a fight worth a great deal that takes a small hit must beat standing still. The second is
    /// the property the qualifier must not destroy, and it is lane G's survival row in miniature — two
    /// fights, the safer one *less* valuable, and the safer one still wins, which is the arm that goes
    /// red if the encounter key is deleted rather than qualified. The third is the over-correction: an
    /// idle course must still beat a fight that is worth less than nothing, because the qualifier removes
    /// a tie-break and does not hand combat a bonus.
    /// </summary>
    private static void SurvivalIsBetweenFights()
    {
        var zombie = new NeedKey(NeedKind.HostileLife, "npc:5", 3);
        var episode = new CourseComparisonEpisode(2, 1, 100,
            new[] { new UsefulNeed(zombie, 40, 40, 1) }, true, 1, "fixture");
        Require(episode.Encounter, "premise: the episode must read as an encounter, or no arm here is about G13");

        // A fight is a course that takes hostile life. `cost` is companion harm at a tick the projection
        // actually counts — harm before the projection's own start tick is skipped, which is why 50.
        CourseProjection Fight(long id, double lifeTaken, double cost)
            => new(new[] { Binding(id, new[] { new PredictedEffect(id + 100, zombie, lifeTaken, 40, 40, 40,
                    EstimateStatus.NativeBound, Array.Empty<long>(), Array.Empty<EffectDelta>(), DependencyManifest.Empty) }) },
                new[] { new PredictedHarm(HarmActor.Companion, cost, 100, 50, EstimateStatus.NativeBound) },
                Array.Empty<CompanionshipInterval>(), 30, true);
        var idle = new CourseProjection(Array.Empty<StepBinding>(), Array.Empty<PredictedHarm>(),
            Array.Empty<CompanionshipInterval>(), 30, true);

        CourseValue Value(CourseProjection course) => CompareCourseOutcomes.Evaluate(course, episode);
        CourseValue nothing = Value(idle), winnable = Value(Fight(1, 40, 5));
        Require(nothing.CompanionHarm == 0 && winnable.CompanionHarm > 0,
            $"premise: idle must cost nothing and the fight must cost something, or the arm is vacuous; "
            + $"idle {nothing.CompanionHarm}, fight {winnable.CompanionHarm}");
        Require(winnable.Total.Nominal > nothing.Total.Nominal,
            $"premise: the fight must be worth more than standing still on totals, or the row is about "
            + $"pricing rather than ordering; fight {winnable.Total.Nominal}, idle {nothing.Total.Nominal}");

        Require(CompareCourseOutcomes.NominalOrder(nothing, winnable, encounter: true) < 0,
            $"standing still outranks a fight worth {winnable.Total.Nominal:0.0000} because the fight costs "
            + $"{winnable.CompanionHarm:0.0000} and doing nothing costs zero: survival-first is being applied "
            + $"to a course that is not a fight, so the companion wins the encounter comparison by having "
            + $"nothing to lose");
        Require(CompareCourseOutcomes.NominalOrder(winnable, nothing, encounter: true) > 0,
            "the order is not antisymmetric between a fight and standing still");

        // Two fights: the safer one kills less and is worth less, so only a survival key can pick it.
        CourseValue safer = Value(Fight(2, 20, 1)), deadlier = Value(Fight(3, 40, 30));
        Require(safer.Total.Nominal < deadlier.Total.Nominal,
            $"premise: the safer fight must be the less valuable one, or the total decides it and the "
            + $"survival key is untested; safer {safer.Total.Nominal}, deadlier {deadlier.Total.Nominal}");
        Require(CompareCourseOutcomes.NominalOrder(safer, deadlier, encounter: true) > 0,
            $"the survivable fight lost to the deadlier one during an encounter: safer costs "
            + $"{safer.CompanionHarm:0.0000} against {deadlier.CompanionHarm:0.0000}, and survival-first "
            + $"between two fights is the rule the qualifier must preserve rather than remove");

        // Against standing still there is no break-even any more, by the owner's ruling of 25 September 2026:
        // keeping the player company is chosen only when no course does any work, so a fight that claims any
        // life beats idle whatever it is worth on the total. Until then idle won against a fight priced below
        // it, and on the 25 September play that is how three zombies below the player were declined. The
        // hopeless fight is the edge of the ruling and is asserted as it stands so moving it is a decision:
        // the safety that remains is the evade layer, downing and recovery, never the choice of job.
        CourseValue hopeless = Value(Fight(4, 0.0001, 90));
        Require(hopeless.Total.Nominal < nothing.Total.Nominal,
            $"premise: the hopeless fight must be worth less than nothing on the total, or this arm says nothing "
            + $"about the ruling; {hopeless.Total.Nominal}");
        Require(CompareCourseOutcomes.NominalOrder(hopeless, nothing, encounter: true) > 0,
            $"standing still beat a fight worth {hopeless.Total.Nominal:0.0000}, so keeping company is competing "
            + "with work again rather than being what the companion does when there is none");
        CourseValue fatal = Value(Fight(5, 40, 100)), overkill = Value(Fight(6, 40, 130));
        Require(CompareCourseOutcomes.NominalOrder(overkill, nothing, encounter: true) > 0,
            $"a fight costing {overkill.CompanionHarm:0.0000} of the companion's life lost to standing still, so "
                + "keeping company is competing with work again");
        Require(CompareCourseOutcomes.NominalOrder(fatal, overkill, encounter: true) > 0,
            $"between two fights the one costing {fatal.CompanionHarm:0.0000} lost to the one costing "
                + $"{overkill.CompanionHarm:0.0000}, so survival-first between fights is gone");

        string detail = System.FormattableString.Invariant(
            $"  encounter ordering: a fight worth {winnable.Total.Nominal:0.0000} at a cost of {winnable.CompanionHarm:0.0000} beats idle at {nothing.Total.Nominal:0.0000}; between two fights the safer ({safer.CompanionHarm:0.0000}) still beats the deadlier ({deadlier.CompanionHarm:0.0000}) though it is worth less; against idle any fight wins by the ruling, the hopeless one at {hopeless.Total.Nominal:0.0000} included");
        AICompanion.Tools.Ledger.EmitLedgerRows.Detail(detail);
    }

    /// <summary>
    /// Being away from the player prices no work, by the owner's ruling of 25 September 2026. Two courses doing the
    /// same job at the same time, one of them far from him for its whole length, must total the same and carry the
    /// same bounds: the gap is still integrated and reported as <see cref="CourseValue.Companionship"/>, and enters
    /// nothing a decision compares. The work-first key alone cannot witness this, because it only separates work from
    /// idleness; a gap charged into the total would still rank a near job above an identical far one.
    /// </summary>
    private static void DistancePricesNoWork()
    {
        var zombie = new NeedKey(NeedKind.HostileLife, "npc:5", 3);
        var episode = new CourseComparisonEpisode(2, 1, 100,
            new[] { new UsefulNeed(zombie, 40, 40, 1) }, true, 0, "fixture");
        CourseProjection Job(long id, double gap)
            => new(new[] { Binding(id, new[] { new PredictedEffect(id + 100, zombie, 20, 40, 40, 40,
                    EstimateStatus.NativeBound, Array.Empty<long>(), Array.Empty<EffectDelta>(), DependencyManifest.Empty) }) },
                Array.Empty<PredictedHarm>(),
                new[] { new CompanionshipInterval(0, 30, gap, gap, EstimateStatus.ModelBound, new(0, 2), new(0, 2)) }, 30, true);
        CourseValue near = CompareCourseOutcomes.Evaluate(Job(1, 0), episode);
        CourseValue far = CompareCourseOutcomes.Evaluate(Job(2, 1), episode);
        Require(far.Companionship > near.Companionship,
            $"premise: the far job must report more companionship cost than the near one, or the row compares nothing; "
            + $"near {near.Companionship}, far {far.Companionship}");
        Close(far.Total.Nominal, near.Total.Nominal,
            $"a job {far.Companionship:0.0000} further from the player totals differently from the same job beside him, "
            + "so distance from the player is pricing work again");
        Close(far.Total.Lower, near.Total.Lower, "distance from the player moved the lower bound of an otherwise identical job");
        Close(far.Total.Upper, near.Total.Upper, "distance from the player moved the upper bound of an otherwise identical job");
    }

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
        + RunOneRow.Case("G13 doing nothing cannot win an encounter by having nothing to lose", SurvivalIsBetweenFights)
        + RunOneRow.Case("distance from the player prices no work", DistancePricesNoWork)
        + RunOneRow.Case("G08 changed facts dirty only dependent descendants", SpatialDependencies)
        + RunOneRow.Case("G08 refreshed reads preserve descendants and reject cycles atomically", DependencyRefresh)
        + RunOneRow.Case("G08 receipts allocate physical amount once", ReceiptConservation)
        + RunOneRow.Case("G09 resource conflicts include interior interval endpoints", Resources)
        + RunOneRow.Case("G15 in-flight effects cannot alter a successor before impact", DelayedEffects)
        + RunOneRow.Case("G11 one budget cuts all borrowers", SharedBudget)
        + RunOneRow.Case("G11 native route search borrows the same operation allowance", SharedRouteBudget)
        + RunOneRow.Case("G02 travel capture models terrain and preserves arrival momentum", CapturedTravel)
        + RunOneRow.Case("G11 cursor survives ticks and cache eviction stays visible", CursorAndStorage)
        + RunOneRow.Case("G03 a prolific discovery source cannot starve a quiet one out of the store", AProlificSourceCannotStarveAQuietOne)
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
        // The two reads differ by key rather than by value, which is what this row varies, so both
        // carry a default value; a read records the fields it saw since 22 September 2026, not a hash.
        index.Register(1, new(new[] { new FactRead(local, 1, default, FactEvidence.Observed) }), Array.Empty<long>());
        index.Register(2, DependencyManifest.Empty, new long[] { 1 });
        index.Register(3, new(new[] { new FactRead(remote, 1, default, FactEvidence.Observed) }), Array.Empty<long>());
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
        index.Register(1, new(new[] { new FactRead(fact, 2, new FactValue(Amount: 1), FactEvidence.Observed) }), Array.Empty<long>());
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
    /// <summary>
    /// A prolific source must not be able to empty a quiet one out of the shared candidate store.
    ///
    /// Measured on 21 September 2026 in a real scene before this held: six opportunity domains shared one
    /// store of sixty-four, lighting minted a hundred and thirty-eight sites in a dark area, and mining,
    /// chopping and collection each minted exactly one candidate and each had it evicted. The companion
    /// could not discover a vein with the ore in front of it, a pickaxe in its slot and the policy
    /// permitting; it lit instead, and nothing anywhere went red, because a domain left with no surviving
    /// candidate reports no admission group at all rather than a refusal.
    ///
    /// The row is written at the store rather than at that scene on purpose. The scene proves it happened
    /// once with one set of numbers; this proves the property for any producer that outproduces another,
    /// which is what a future domain — a fishing spot, an NPC to escort — inherits without anyone
    /// remembering this afternoon. `FairDiscovery` beside it covers the neighbouring half, that a tiny
    /// slice still *examines* every source; being examined and surviving are different guarantees, and
    /// the defect lived precisely in the gap between them.
    /// </summary>
    private static void AProlificSourceCannotStarveAQuietOne()
    {
        var loud = new FixtureSource("loud", 200); var quiet = new FixtureSource("quiet", 2);
        var discovery = new DiscoverOpportunities(new[] { loud, quiet }, 64);
        for (int i = 0; i < 400; i++) discovery.Continue(Facts(), new(double.PositiveInfinity, 8), Array.Empty<OpportunityKey>());
        int loudHeld = discovery.Candidates.Count(c => c.Key.Domain == "loud");
        int quietHeld = discovery.Candidates.Count(c => c.Key.Domain == "quiet");
        Require(loudHeld > 0, $"premise: the prolific source must actually fill the store; loud={loudHeld}");
        Require(quietHeld == 2,
            $"a source that minted two candidates against two hundred must keep both, or the store is a "
            + $"preference for the loudest producer rather than a capacity; loud={loudHeld} quiet={quietHeld}");
        Require(discovery.Coverage.Single(c => c.Source == "quiet").Evicted == 0,
            $"the quiet source's candidates must never be evicted while it is under its floor; "
            + $"evicted={discovery.Coverage.Single(c => c.Source == "quiet").Evicted}");
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
        // A declared domain, because the search refuses one no registered activity performs.
        var source = new FixtureSource("collect-target", 7);
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
        var absent = new DependencyManifest(new[] { new FactRead(new("tile", "uncaptured"), 1, default, FactEvidence.Missing) });
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
        var census = new CourseComparisonEpisode(1, 1, 100, Episode().Needs, false, 0, "partial");
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
                // "collect" rather than a synthetic purpose, because the search refuses an opportunity
                // whose purpose names no activity in `ExecuteCourseBinding`'s map: a made-up one is
                // dropped before enumeration and the row measures an empty search rather than a deep one.
                result.Add(new(new(name, "collect", index.ToString(), 1), 1, new(index, 0), OpportunityAdmission.KnownUsable,
                    "observed", new[] { new UsefulNeed(Loot, 20, 20, 1) }, new[] { "pickup" }, DependencyManifest.Empty, default));
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
