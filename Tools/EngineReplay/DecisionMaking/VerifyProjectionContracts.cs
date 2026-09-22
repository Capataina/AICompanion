#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>Projection rows that exercise causal provenance, physical resources, fair
/// discovery and receipt retirement through the production pure-core contracts.</summary>
internal static class VerifyProjectionContracts
{
    private static readonly NeedKey Loot = new(NeedKind.Loot, "projection-contract");
    private static readonly OpportunityKey Key = new("projection", "contract", "target", 1);

    public static int Run()
        => RunOneRow.Case("G08 effect-only reads dirty their dependent binding", EffectFactDependencies)
        + RunOneRow.Case("G09 physical hands ignore capacity keys", KeyedHandConflict)
        + RunOneRow.Case("G09 past reservations refuse without mutating a branch", PastReservationIsAtomic)
        + RunOneRow.Case("G09 repeated bindings reserve and credit once", DuplicateBinding)
        + RunOneRow.Case("G14 undeclared hypothetical reads refuse a binding", UndeclaredHypotheticalRead)
        + RunOneRow.Case("G15 causal effects require due parents", DelayedParentOrdering)
        + RunOneRow.Case("G15 descendants wait for a parent's guaranteed latest outcome", LateBoundParentOrdering)
        + RunOneRow.Case("G15 a missed parent censors but does not erase issued descendants", MissedPhysicalParent)
        + RunOneRow.Case("G11 one-operation discovery reaches every finite source", OneOperationFairness)
        + RunOneRow.Case("G08 receipt retirement retains duplicate floor and live credit", ReceiptRetirement)
        + RunOneRow.Case("G08 consequence reads invalidate costs without revoking a legal use", ConsequenceReads)
        + RunOneRow.Case("G15 contact harm stops at an unsupported post-hit successor", ContactHarm)
        + RunOneRow.Case("G15 projected body retains native arrival instead of requested pose", ArrivalPose)
        + RunOneRow.Case("G07 recorded uncertain costs permit a legal start without certifying safety", RecordedUncertainty);

    private static void RecordedUncertainty()
    {
        var key = new FactKey("cost-model", "unresolved");
        var facts = Facts(new DecisionFact(key, 1, new(Text: "unsupported-future"), FactEvidence.Unresolved));
        var reader = facts.Track(); reader.Read(key);
        var episode = new CourseComparisonEpisode(1, 1, 10, Array.Empty<UsefulNeed>(), true, 0, "fixture");
        var binding = Binding(94001);
        BindingValidation Valid(StepBinding _) => new(OpportunityAdmission.KnownUsable, "fixture", false);
        CourseProjection Proposal(DependencyManifest reads, bool unknown) => new(new[] { binding }, Array.Empty<PredictedHarm>(),
            Array.Empty<CompanionshipInterval>(), 0, true, tailUnresolved: unknown, consequenceDependencies: reads);
        var uncertain = Proposal(reader.Manifest(), true);
        Require(new RetainCourse().Consider(uncertain, episode, facts, Valid(binding), Valid),
            "a recorded uncertain cost was treated as absent native admission evidence");
        var value = CompareCourseOutcomes.Evaluate(uncertain, episode);
        Require(value.Total.Status == EstimateStatus.Unresolved && value.SelfHarm.Status == EstimateStatus.Unresolved,
            "an unknown future became certified total value or zero companion harm");
        Require(!new RetainCourse().Consider(Proposal(reader.Manifest(), false), episode, facts, Valid(binding), Valid),
            "unresolved costs published without exposing their uncertainty");
        var missing = Facts().Track(); missing.Read(key);
        Require(!new RetainCourse().Consider(Proposal(missing.Manifest(), true), episode, Facts(), Valid(binding), Valid),
            "a missing input published as though its uncertainty had been captured");
        var unproven = new StepBinding(94002, Key, "fixture", default, "none", 1, 1, 0, 0, 0,
            Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(), Array.Empty<long>(), reader.Manifest(), true);
        var badUse = new CourseProjection(new[] { unproven }, Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(),
            0, true, tailUnresolved: true, consequenceDependencies: reader.Manifest());
        Require(!new RetainCourse().Consider(badUse, episode, facts, Valid(unproven), Valid),
            "the weaker cost contract leaked into native-use admission");
    }

    private static void ArrivalPose()
    {
        var binding = new StepBinding(70001, Key, "fixture", new(49, 80), "fixture", 1, 1,
            0, 1, 0, Array.Empty<ResourcePhase>(), Array.Empty<PredictedEffect>(), Array.Empty<long>(),
            DependencyManifest.Empty, true, arrivalPose: new CoursePoint(48, 80));
        var state = new ProjectedCourseState(new(48, 80));
        Require(state.TryApply(binding, new Dictionary<string, double>(), out _), "arrival fixture was refused");
        Require(state.Pose == new CoursePoint(48, 80) && binding.Pose == new CoursePoint(49, 80),
            "projection teleported the body to the requested use pose");
    }

    private static void ContactHarm()
    {
        var actorBoxes = Enumerable.Repeat(new ContactBox(0, 0, 20, 20), 5).ToArray();
        var enemyBoxes = new[] { new ContactBox(40, 0, 20, 20), new ContactBox(20, 0, 20, 20),
            new ContactBox(19, 0, 20, 20), new ContactBox(10, 0, 20, 20), new ContactBox(0, 0, 20, 20) };
        ContactGeometry Geometry(ContactBox[] boxes, double damage, int ready)
            => new(boxes.Select(box => new ContactSample(box, damage, ready)).ToArray(), true);
        // The game's own windows, so the fixture and the world agree about the cadence: `Player.Hurt`
        // gives 40 ticks for an ordinary contact hit and 20 for one that lands for a single point, and
        // `NPC.BeHurtByOtherNPC` gives 30 with no damage-sized branch.
        const int PlayerImmunity = 40, PlayerMinimalImmunity = 20, CompanionImmunity = 30;
        var forecast = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Companion, 100, 0,
                CompanionImmunity, CompanionImmunity, actorBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(enemyBoxes, 50, 0), Geometry(enemyBoxes, 25, 0)) }, 4, true);
        ContactHarmResult? result = null;
        for (int i = 0; i < 100 && result == null; i++)
        {
            var budget = new DecisionWorkBudget(double.PositiveInfinity, 1);
            result = forecast.Continue(budget);
            Require(budget.OperationsUsed <= 1, "contact forecasting exceeded its shared allowance");
        }
        // One hit inside this horizon, and — the half that changed on 21 September 2026 — a *resolved*
        // tail. The row asserted `TailUnresolved` here for as long as a hit ended the actor's scan,
        // and it was right to: everything after a hit was genuinely unknown while there was no
        // successor model. The immunity window is that model, and it carries 30 ticks past a horizon of
        // 4, so there is nothing left unknown inside the horizon and saying otherwise would be the
        // forecast refusing to answer a question it has just answered. The row's own message named
        // "a known successor" as a failure; that is now the feature, and the cadence row below is what
        // stops the change from being a licence to invent hits.
        Require(result != null && result.Harm.Count == 1 && result.Harm[0].Tick == 2
            && result.Harm[0].Damage == 25 && result.Harm[0].CurrentLife == 100 && !result.TailUnresolved,
            $"contact forecasting invented near-miss harm, or left a tail unresolved that the immunity "
            + $"window resolves; got {result?.Harm.Count} hit(s), tail unresolved {result?.TailUnresolved}");

        // The cadence itself: a hostile that keeps overlapping hits again every immunity window, and
        // that difference is the whole reason this forecast exists. Before it, a zombie walking into a
        // player was priced as one hit whatever anybody did, so every course carried identical harm and
        // defending him was worth exactly nothing — the measurement on `danger lifts combat over work`,
        // where mining, fighting and standing still all read a harm of 0.3500.
        //
        // The scene is deliberately long enough to hold several windows. A body of 100 life against 25
        // a hit takes four before it is empty, and the fifth must not appear: a forecast that kept
        // charging a course for harm to somebody it has already said is down would make a hopeless
        // fight look infinitely expensive.
        var longBoxes = Enumerable.Repeat(new ContactBox(0, 0, 20, 20), 200).ToArray();
        var persistent = new ForecastContactHarm(
            new[] { new ContactActor(HarmActor.Companion, 100, 0, CompanionImmunity, CompanionImmunity, longBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(longBoxes, 50, 0), Geometry(longBoxes, 25, 0)) },
            199, true).Continue(new(double.PositiveInfinity));
        Require(persistent!.Harm.Count == 4
                && persistent.Harm.Select(hit => (int)hit.Tick).SequenceEqual(new[] { 0, 30, 60, 90 })
                && persistent.Harm.Select(hit => hit.CurrentLife).SequenceEqual(new double[] { 100, 75, 50, 25 }),
            $"a hostile that keeps overlapping must hit again every immunity window and stop when the "
            + $"body is empty; got {persistent.Harm.Count} hit(s) at "
            + $"{string.Join(",", persistent.Harm.Select(hit => hit.Tick))} leaving life "
            + $"{string.Join(",", persistent.Harm.Select(hit => hit.CurrentLife))}");

        // The one-damage branch, which is the game's and not a rounding of ours: a hit that lands for a
        // single point buys half the window, so an armoured body whose defence floors a weak hostile is
        // hurt twice as often. Asserted as the pair rather than as one number, because the value of the
        // branch is the difference and a row on the short window alone would pass against a model that
        // had lost the ordinary one.
        var flooredHits = new ForecastContactHarm(
            new[] { new ContactActor(HarmActor.Player, 100, 0, PlayerImmunity, PlayerMinimalImmunity, longBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(longBoxes, 1, 0), Geometry(longBoxes, 1, 0)) },
            99, true).Continue(new(double.PositiveInfinity));
        var ordinaryHits = new ForecastContactHarm(
            new[] { new ContactActor(HarmActor.Player, 100, 0, PlayerImmunity, PlayerMinimalImmunity, longBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(longBoxes, 10, 0), Geometry(longBoxes, 10, 0)) },
            99, true).Continue(new(double.PositiveInfinity));
        Require(flooredHits!.Harm.Select(hit => (int)hit.Tick).SequenceEqual(new[] { 0, 20, 40, 60, 80 })
                && ordinaryHits!.Harm.Select(hit => (int)hit.Tick).SequenceEqual(new[] { 0, 40, 80 }),
            $"a one-damage hit must buy the shorter immunity window and a real one the longer; floored "
            + $"at {string.Join(",", flooredHits.Harm.Select(hit => hit.Tick))} against ordinary at "
            + $"{string.Join(",", ordinaryHits!.Harm.Select(hit => hit.Tick))}");

        var incomplete = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Player, 100, 0,
                PlayerImmunity, PlayerMinimalImmunity, actorBoxes) },
            Array.Empty<ContactThreat>(), 4, false).Continue(new(double.PositiveInfinity));
        Require(incomplete!.TailUnresolved && incomplete.Harm.Count == 0, "an incomplete enemy census became a safe empty world");
        var perVictim = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Player, 100, 0,
                    PlayerImmunity, PlayerMinimalImmunity, actorBoxes),
                new ContactActor(HarmActor.Companion, 100, 0, CompanionImmunity, CompanionImmunity, actorBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(actorBoxes, 50, 3), Geometry(enemyBoxes, 25, 0)) }, 4, true)
            .Continue(new(double.PositiveInfinity));
        Require(perVictim!.Harm.Count == 2
            && perVictim.Harm.Single(hit => hit.Actor == HarmActor.Player).Tick == 3
            && perVictim.Harm.Single(hit => hit.Actor == HarmActor.Companion).Tick == 2,
            "victim-dependent attack geometry or native cooldown readiness was shared between actors");

        // A hostile the evaluating course kills makes no contact from the tick it dies on, for either
        // body. The same scene is run twice against the same geometry, differing only in the kill tick,
        // so the difference is the truncation and nothing else. Without the second arm the first proves
        // only that a kill tick before contact suppresses it, which a forecast that ignored kills
        // entirely would also satisfy on an empty harm list.
        //
        // This row exists because `KilledAtTick` shipped with no fixture anywhere in the tree — three
        // occurrences, all in the mod, a field, one read and one write — so the mechanism could have
        // been deleted without a test noticing. A review of `e63375d` measured exactly that.
        var killedBefore = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Companion, 100, 0, CompanionImmunity, CompanionImmunity, actorBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(enemyBoxes, 50, 0), Geometry(enemyBoxes, 25, 0), KilledAtTick: 1) },
            4, true).Continue(new(double.PositiveInfinity));
        Require(killedBefore!.Harm.Count == 0,
            $"a hostile the course kills before it lands must make no contact after it dies; "
            + $"got {killedBefore.Harm.Count} hit(s) at {string.Join(",", killedBefore.Harm.Select(h => h.Tick))}");
        var killedAfter = new ForecastContactHarm(new[] { new ContactActor(HarmActor.Companion, 100, 0, CompanionImmunity, CompanionImmunity, actorBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(enemyBoxes, 50, 0), Geometry(enemyBoxes, 25, 0), KilledAtTick: 3) },
            4, true).Continue(new(double.PositiveInfinity));
        Require(killedAfter!.Harm.Count == 1 && killedAfter.Harm[0].Tick == 2,
            $"a kill after the contact cannot retract it, or the truncation is being applied to the whole "
            + $"threat rather than from its own tick; got {killedAfter.Harm.Count} hit(s) at "
            + $"{string.Join(",", killedAfter.Harm.Select(h => h.Tick))}");

        // A body whose own readiness runs past the horizon is never examined at all, and the forecast
        // must not report that as a clean sweep. It is true that immunity makes him un-hittable inside
        // that window; what must not follow is a course banking the silence as proof it is the safer
        // one. The caller's `harm-window-shorter-than-forecast` rule is the other half of this; here the
        // property is that a hit list can be empty for a reason that is not safety.
        var neverExamined = new ForecastContactHarm(
            new[] { new ContactActor(HarmActor.Player, 100, ReadyTick: 9, PlayerImmunity, PlayerMinimalImmunity, actorBoxes) },
            new[] { new ContactThreat(1, 1, Geometry(enemyBoxes, 50, 0), Geometry(enemyBoxes, 25, 0)) },
            4, true).Continue(new(double.PositiveInfinity));
        Require(neverExamined!.Harm.Count == 0,
            "a body immune past the horizon cannot be hit inside it");
    }

    private static void ConsequenceReads()
    {
        var key = new FactKey("companionship", "region");
        var facts = Facts(new DecisionFact(key, 1, new(1), FactEvidence.Observed));
        var reader = facts.Track(); reader.Read(key);
        var binding = Binding(93001);
        var projection = new CourseProjection(new[] { binding }, Array.Empty<PredictedHarm>(),
            Array.Empty<CompanionshipInterval>(), 0, true, consequenceDependencies: reader.Manifest());
        var index = CourseDependencyIndex.Build(projection);
        var repair = new RepairCourse();
        repair.Invalidate(index, key, "region-changed");
        repair.Continue(index, new(double.PositiveInfinity));
        Require(repair.IsDirty(projection.ConsequenceId) && !repair.IsDirty(binding.Id),
            "a cost-only dependency either disappeared or revoked a legal native use");
        var episode = new CourseComparisonEpisode(1, 1, 10, Array.Empty<UsefulNeed>(), true, 0, "fixture");
        var owner = new RetainCourse();
        BindingValidation Valid(StepBinding _) => new(OpportunityAdmission.KnownUsable, "fixture", false);
        var changed = Facts(new DecisionFact(key, 2, new(2), FactEvidence.Observed));
        Require(!owner.Consider(projection, episode, changed, Valid(binding), Valid),
            "publication accepted costs computed from an obsolete region");
        Require(owner.Consider(projection, episode, facts, Valid(binding), Valid),
            "a current consequence manifest could not publish");
        Require(owner.Current!.Projection.ConsequenceDependencies.Reads.Single().Key == key,
            "physical-effect reconciliation erased consequence provenance");
    }

    private static DecisionFactSnapshot Facts(params DecisionFact[] facts)
        => new(1, 1, 100, 1, 0, facts);
    private static PredictedEffect Effect(long id, double nominalTick, IEnumerable<long>? parents = null,
        DependencyManifest? dependencies = null, IEnumerable<EffectDelta>? delta = null,
        double? earliestTick = null, double? latestTick = null, EstimateStatus evidence = EstimateStatus.ModelBound,
        double amount = 1)
        => new(id, Loot, amount, nominalTick, earliestTick ?? nominalTick, latestTick ?? nominalTick, evidence, parents ?? Array.Empty<long>(),
            delta ?? Array.Empty<EffectDelta>(), dependencies ?? DependencyManifest.Empty);
    private static StepBinding Binding(long id, IEnumerable<PredictedEffect>? effects = null,
        IEnumerable<ResourcePhase>? resources = null, IEnumerable<long>? parents = null, double travel = 0)
        => new(id, Key, "fixture", default, "none", 1, 1, travel, 0, 0,
            resources ?? Array.Empty<ResourcePhase>(), effects ?? Array.Empty<PredictedEffect>(), parents ?? Array.Empty<long>(),
            DependencyManifest.Empty, true);
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }

    private static void KeyedHandConflict()
    {
        var state = new ProjectedCourseState(default);
        var phases = new[]
        {
            new ResourcePhase(CourseResource.Hand, 0, 10, 1, "left"),
            new ResourcePhase(CourseResource.Hand, 0, 10, 1, "right")
        };
        Require(!state.TryApply(Binding(101, resources: phases, travel: 10), new Dictionary<string, double>(), out var reason)
            && reason == "resource-conflict:Hand", "Distinct hand keys double-booked the one physical hand.");
    }

    private static void PastReservationIsAtomic()
    {
        var state = new ProjectedCourseState(default); state.AdvanceTo(5);
        Require(!state.TryApply(Binding(103, resources: new[] { new ResourcePhase(CourseResource.Body, 4, 6, 1) }), new Dictionary<string, double>(), out var reason)
            && reason == "resource-phase-in-past" && state.Tick == 5,
            "A past phase was accepted or changed the branch before refusal.");
    }

    private static void DuplicateBinding()
    {
        var state = new ProjectedCourseState(default);
        var first = Binding(104, resources: new[] { new ResourcePhase(CourseResource.Mana, 0, 10, .5, "pool") });
        Require(state.TryApply(first, new Dictionary<string, double> { ["pool"] = 1 }, out _)
            && state.TryApply(first, new Dictionary<string, double> { ["pool"] = 1 }, out _), "A repeated binding was not idempotent.");
        Require(state.TryApply(Binding(105, resources: new[] { new ResourcePhase(CourseResource.Mana, 0, 10, .5, "pool") }),
            new Dictionary<string, double> { ["pool"] = 1 }, out _), "Replaying a binding debited its mana reservation twice.");
        Require(!state.TryApply(Binding(104), new Dictionary<string, double>(), out var reused) && reused == "binding-id-reused",
            "A distinct binding silently reused an issued binding ID.");
    }

    private static void EffectFactDependencies()
    {
        var fact = new FactKey("effect-result", "target");
        var manifest = new DependencyManifest(new[] { new FactRead(fact, 1, default, FactEvidence.Observed) });
        var first = Binding(106, new[] { Effect(107, 1, dependencies: manifest) }, travel: 1);
        var later = Binding(108, parents: new[] { 107L });
        var index = CourseDependencyIndex.Build(new CourseProjection(new[] { first, later }, Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(), 0, true));
        var repair = new RepairCourse(); repair.Invalidate(index, fact, "effect-fact-changed");
        repair.Continue(index, new(double.PositiveInfinity, 10));
        Require(repair.IsDirty(107) && repair.IsDirty(108), "An effect-only fact did not dirty its causal dependent binding.");
    }

    private static void UndeclaredHypotheticalRead()
    {
        var fact = new FactKey("hypothetical", "position");
        var state = new ProjectedCourseState(default);
        Require(state.TryApply(Binding(109, new[] { Effect(110, 0, delta: new[] { new EffectDelta(fact, new FactValue(X: 4)) }) }),
            new Dictionary<string, double>(), out _), "The prerequisite effect did not project.");
        var binder = new ReadingBinder(fact);
        var service = new BindOpportunity(new[] { binder });
        bool refused = false;
        try { service.Bind(Opportunity(), state, Facts(new DecisionFact(fact, 1, new(X: 1), FactEvidence.Observed)), new(), new(double.PositiveInfinity, 4)); }
        catch (InvalidOperationException) { refused = true; }
        Require(refused, "A binding concealed a hypothetical effect it read.");
        var cleanService = new BindOpportunity(new IOpportunityBinder[] { new EmptyBinder() });
        Require(cleanService.Bind(Opportunity(), state, Facts(), new(), new(double.PositiveInfinity, 4)).Binding != null,
            "One binder's hypothetical read leaked into the next binder's causal journal.");
    }

    private static void DelayedParentOrdering()
    {
        var state = new ProjectedCourseState(default);
        Require(state.TryApply(Binding(111, new[] { Effect(112, 20) }), new Dictionary<string, double>(), out _), "The parent effect did not project.");
        Require(!state.TryApply(Binding(113, new[] { Effect(114, 10, new[] { 112L }) }, parents: new[] { 111L }), new Dictionary<string, double>(), out var reason)
            && reason == "effect-parent-not-due", "A child effect activated before its parent was due.");
    }

    private static void LateBoundParentOrdering()
    {
        var changed = new FactKey("effect-result", "late-parent");
        var manifest = new DependencyManifest(new[] { new FactRead(changed, 1, default, FactEvidence.Observed) });
        var parent = Effect(121, 10, parents: new[] { 120L }, dependencies: manifest,
            delta: new[] { new EffectDelta(changed, new FactValue(X: 1)) }, earliestTick: 5, latestTick: 20,
            evidence: EstimateStatus.NativeBound);
        var earlyChild = Binding(122, new[] { Effect(123, 13, parents: new[] { 121L }, earliestTick: 13) }, parents: new[] { 121L });
        var state = new ProjectedCourseState(default, outstandingEffects: new[] { parent }, startTick: 12,
            completedCauses: new[] { 120L });
        Require(!state.TryApply(earlyChild, new Dictionary<string, double>(), out var refused) && refused == "binding-parent-not-due",
            "A parent only possibly complete by tick 12 enabled its child binding.");
        bool earlyGraphRefused = false;
        try
        {
            _ = CourseDependencyIndex.Build(new CourseProjection(new[] { earlyChild }, Array.Empty<PredictedHarm>(),
                Array.Empty<CompanionshipInterval>(), 0, true, outstandingEffects: new[] { parent }, completedCauses: new[] { 120L }, projectionStartTick: 12));
        }
        catch (ArgumentException) { earlyGraphRefused = true; }
        Require(earlyGraphRefused, "The causal graph certified a descendant before the parent's latest bound.");

        state.AdvanceTo(20);
        var laterChild = Binding(124, new[] { Effect(125, 21, parents: new[] { 121L }, earliestTick: 20) }, parents: new[] { 121L });
        Require(state.TryApply(laterChild, new Dictionary<string, double>(), out _),
            "A descendant at the parent's guaranteed latest outcome was refused.");
        var index = CourseDependencyIndex.Build(new CourseProjection(new[] { laterChild }, Array.Empty<PredictedHarm>(),
            Array.Empty<CompanionshipInterval>(), 0, true, outstandingEffects: new[] { parent }, completedCauses: new[] { 120L }, projectionStartTick: 20));
        Require(index.DirectUsers(changed).SequenceEqual(new long[] { 121 }) && index.Children(121).Contains(124),
            "An outstanding parent effect lost its fact manifest or causal child edge.");

        var nominalOnly = new CourseProjection(new[] { Binding(126, new[] { Effect(127, 10, evidence: EstimateStatus.Nominal) }) },
            Array.Empty<PredictedHarm>(), Array.Empty<CompanionshipInterval>(), 0, true);
        _ = CourseDependencyIndex.Build(nominalOnly);
        var episode = new CourseComparisonEpisode(1, 1, 10, new[] { new UsefulNeed(Loot, 1, 1, 1) }, true, 0, "fixture");
        Require(CompareCourseOutcomes.Evaluate(nominalOnly, episode).Total.Nominal > 0,
            "A nominal effect without a causal descendant stopped contributing to nominal scoring.");
    }

    private static void MissedPhysicalParent()
    {
        var stale = new FactKey("effect-result", "missed-parent");
        var parent = Effect(211, 0, delta: new[] { new EffectDelta(stale, new FactValue(X: 8)) }, amount: 0);
        var child = Effect(212, 0, parents: new[] { 211L }, delta: new[] { new EffectDelta(stale, new FactValue(X: 16)) });
        var launch = Binding(201, new[] { parent, child });
        var unrelated = Binding(202);
        var usable = new BindingValidation(OpportunityAdmission.KnownUsable, "fixture", false);
        var owner = new RetainCourse();
        Require(owner.Consider(new CourseProjection(new[] { launch, unrelated }, Array.Empty<PredictedHarm>(),
            Array.Empty<CompanionshipInterval>(), 0, true), new CourseComparisonEpisode(1, 1, 10,
            new[] { new UsefulNeed(Loot, 1, 1, 1) }, true, 0, "fixture"), Facts(), usable, _ => usable),
            "The issued parent/child fixture did not publish.");
        owner.ObserveExecution(new(201, 1, 1, "fire", "fire", "projectile-created", 401,
            ExecutionBoundary.NativeUseComplete, "released"), new[] { 211L, 212L });
        var receipt = owner.ApplyEffectReceipt(501, Loot, 1);
        Require(receipt.Allocated.Single().EffectId == 212 && owner.Effects.Confirmed(212) == 1,
            "The active child did not retain receipt ownership.");

        owner.ObserveEffectTerminal(211, false, "external-hit");
        Require(owner.Current!.Projection.Prefix?.Id == unrelated.Id,
            "A missed physical parent invalidated an unrelated executable prefix.");
        var censored = owner.OutstandingEffects.Single();
        Require(censored.Id == 212 && censored.Evidence == EstimateStatus.Unresolved && censored.Delta.Count == 0,
            "A live child retained the missed parent's certified delta.");
        var state = new ProjectedCourseState(default, outstandingEffects: owner.OutstandingEffects, startTick: 100);
        Require(state.Read(stale, Facts(new DecisionFact(stale, 1, new FactValue(X: 0), FactEvidence.Observed)).Track()).X == 0
            && state.ReadEffects.Count == 0, "A censored physical child still projected the stale successor state.");

        bool reobservationThrew = false;
        owner.Repair.Continue(owner.Dependencies, new(double.PositiveInfinity));
        try { _ = owner.ObserveRemaining(owner.Current.Projection, Facts(), _ => usable); }
        catch (InvalidOperationException) { reobservationThrew = true; }
        Require(!reobservationThrew, "A physical miss made re-observation throw before repair could preserve the child.");
        owner.Release("fixture-release");
        Require(owner.OutstandingEffects.Single().Id == 212 && owner.Effects.Confirmed(212) == 1
            && owner.ApplyEffectReceipt(501, Loot, 1).Duplicate,
            "Release discarded the live child or changed its already-issued receipt.");

        var refreshed = Effect(212, 5, parents: Array.Empty<long>(), delta: new[] { new EffectDelta(stale, new FactValue(X: 4)) });
        Require(owner.ReobserveOutstandingEffect(refreshed, Facts()) && owner.OutstandingEffects.Single().Id == 212
            && owner.OutstandingEffects.Single().Evidence == EstimateStatus.ModelBound && owner.Effects.Confirmed(212) == 1,
            "A fresh same-ID observation could not restore the active child's evidence.");
        bool malformedRefused = false;
        try
        {
            _ = CourseDependencyIndex.Build(new CourseProjection(Array.Empty<StepBinding>(), Array.Empty<PredictedHarm>(),
                Array.Empty<CompanionshipInterval>(), 0, true, outstandingEffects: new[]
                { Effect(213, 0, parents: new[] { 999L }, evidence: EstimateStatus.Unresolved) }));
        }
        catch (ArgumentException) { malformedRefused = true; }
        Require(malformedRefused, "An unresolved effect admitted an arbitrary missing causal parent.");
    }

    private static void OneOperationFairness()
    {
        var left = new FiniteSource("left", 3); var right = new FiniteSource("right", 3);
        var discovery = new DiscoverOpportunities(new IOpportunitySource[] { left, right }, 10);
        for (int tick = 0; tick < 6; tick++) discovery.Continue(Facts(), new(double.PositiveInfinity, 1), Array.Empty<OpportunityKey>());
        Require(left.Examined == 3 && right.Examined == 3, "A positive one-operation slice starved a finite source.");
    }

    private static void ReceiptRetirement()
    {
        var ledger = new CourseEffectLedger(); var effects = new[] { Effect(115, 0), Effect(116, 0) };
        Require(ledger.Apply(117, Loot, 1, effects).Allocated.Single().EffectId == 115, "Initial receipt allocation failed.");
        ledger.RetireThroughReceipt(117, new HashSet<long> { 116 });
        Require(ledger.Apply(117, Loot, 1, effects).Duplicate && ledger.Confirmed(115) == 0,
            "Retired receipt replayed or its retired confirmation survived.");
        Require(ledger.Apply(118, Loot, 1, new[] { Effect(116, 0) }).Allocated.Single().EffectId == 116 && ledger.Confirmed(116) == 1,
            "Receipt retirement discarded live-effect confirmation capacity.");
    }

    private static Opportunity Opportunity()
        => new(Key, 1, default, OpportunityAdmission.KnownUsable, "fixture", Array.Empty<UsefulNeed>(), new[] { "fixture" }, DependencyManifest.Empty, default);

    private sealed class ReadingBinder(FactKey key) : IOpportunityBinder
    {
        public string Domain => "projection";
        public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            state.Read(key, facts);
            return new(Binding(119), OpportunityAdmission.KnownUsable, "fixture", false);
        }
        public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
            => new(OpportunityAdmission.KnownUsable, "fixture", false);
    }

    private sealed class EmptyBinder : IOpportunityBinder
    {
        public string Domain => "projection";
        public BindingResult Bind(Opportunity opportunity, ProjectedCourseState state, TrackedFactReader facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
            => new(Binding(120), OpportunityAdmission.KnownUsable, "fixture", false);
        public BindingValidation ValidateNextUse(StepBinding binding, DecisionFactSnapshot facts)
            => new(OpportunityAdmission.KnownUsable, "fixture", false);
    }

    private sealed class FiniteSource(string name, int count) : IOpportunitySource
    {
        public string Name => name;
        public int Examined { get; private set; }
        public OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
        {
            var found = new List<Opportunity>();
            if (cursor.Offset < count && budget.TrySpend("fixture-source"))
            {
                long position = cursor.Offset; cursor.Advance(); Examined++;
                found.Add(new(new(Name, "fixture", position.ToString(), 1), 1, new(position, 0), OpportunityAdmission.KnownUsable,
                    "fixture", Array.Empty<UsefulNeed>(), new[] { "fixture" }, DependencyManifest.Empty, default));
            }
            if (cursor.Offset == count) cursor.Complete();
            return new(found, new(Name, facts.WorldEpoch, cursor.Offset, count, cursor.Exhausted, budget.Cut, "fixture"));
        }
    }
}
