extern alias live;

using System;
using System.Linq;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>
/// The observation boundary the retained-course brain rests on: one frozen snapshot per tick,
/// assembled from every domain's own capture, after which nothing may read the live world again.
///
/// Until `AssembleCourseSnapshot` existed nothing in production built a `DecisionFactSnapshot` at
/// all — the type's only callers were its own definition and a model-extension helper — so every
/// capture in the tree was complete and none of them had ever been composed. These rows are what
/// make "the course machinery can be handed an observation" a checkable claim rather than a plan.
/// </summary>
internal static class VerifyCourseSnapshotAssembly
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            red += RunOneRow.GreenOrRed(name, test);
        }
        Row("G01 one snapshot carries every domain's census", EveryDomainAppears);
        Row("G01 each tick is a new observation, not an extension of the last", EachTickIsItsOwnObservation);
        Row("G01 a world reset ends the snapshot sequence", ResetEndsTheSequence);
        Row("G01 an assembled snapshot is accepted by the model owner that has to consume it", TheModelOwnerAcceptsIt);
        return red;
    }

    /// <summary>
    /// The seam nobody was standing on, and the class of defect this repository has now produced four
    /// times: two halves of one boundary, each only ever tested against a hand-written stand-in for the
    /// other, agreeing with their fixtures and not with each other.
    ///
    /// `RetainCourseModelQueries` is the consumer of an assembled snapshot in the wired brain, and it
    /// refuses one whose own tick disagrees with the contact census inside it — a census from one frame
    /// indexed under another frame's observation being exactly the mixed-observation defect the freeze
    /// exists to stop. This assembler stamped `Senses.Tick`, a per-companion counter starting at zero on
    /// every spawn, while the census, both victim captures and the model scheduler all stamp
    /// `Main.GameUpdateCount`. So the first live snapshot holding any hostile would have thrown the
    /// moment the coordinator built its model owner, and no existing row could see it: the owner's own
    /// fixtures all hand-build their snapshots, and this file had never handed one to the owner.
    ///
    /// The row deliberately constructs the owner rather than asserting the tick's value. A row reading
    /// `snapshot.Tick == Main.GameUpdateCount` would be this file agreeing with itself about a
    /// convention; constructing the consumer is the only form that fails when the two sides drift again.
    /// </summary>
    private static void TheModelOwnerAcceptsIt()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        DecisionFactSnapshot snapshot = Fresh().Capture(ctx, ctx.Companion.Combat, null, new(double.PositiveInfinity));
        Require(snapshot.TryRead(CapturedContactCensus.Key, out _),
            "the assembled snapshot carries no contact census, so this row would pass on a snapshot the owner never checks");
        _ = new RetainCourseModelQueries(snapshot, live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World, 1, 4);
    }

    private static AssembleCourseSnapshot Fresh() => new();

    /// <summary>
    /// Every domain the course brain can act on has to be visible in one observation, because a
    /// domain absent from the snapshot is not an empty world to a discovery source — it is a question
    /// nobody asked, and optional work does not start on an unanswered search. A snapshot missing one
    /// domain is therefore a companion that silently never does that job.
    ///
    /// The row asserts the completeness facts rather than site counts on purpose: the fixture floor
    /// has no ore, no trunks, no pots and no drops, so the counts are legitimately zero and only the
    /// coverage answer separates "looked and found nothing" from "never looked".
    /// </summary>
    private static void EveryDomainAppears()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Senses.Loot.Pickups.Clear();
        DecisionFactSnapshot snapshot = Fresh().Capture(ctx, ctx.Companion.Combat, null, new(double.PositiveInfinity));

        // Every domain must publish a coverage fact, and any domain claiming Observed must name a
        // non-empty area it actually swept. The second half is the one that matters: in colour mode the
        // light window is intersected with the engine's own processed area, which is empty until the
        // engine has scanned near the companion — and it never has in a headless scene. Asserting
        // Observed for all five passed only because the capture used to publish Observed unconditionally,
        // which is a finished census of a world nobody looked at. Unresolved here is the honest answer
        // and the row now demands that the two agree with each other.
        foreach (string coverage in new[] { "collect-coverage", "light-coverage", "mine-coverage", "chop-coverage" })
        {
            Require(snapshot.TryRead(new FactKey(coverage, "native-census"), out DecisionFact fact),
                $"the assembled snapshot carries no {coverage} at all, so that domain can never finish a census");
            Require(fact.Evidence is FactEvidence.Observed or FactEvidence.Unresolved,
                $"{coverage} published evidence {fact.Evidence}, which is neither a finished census nor an unfinished one");
            Require(fact.Evidence != FactEvidence.Observed || !fact.Value.Text.Contains("unscanned", StringComparison.Ordinal),
                $"{coverage} called an unscanned area a finished census");
        }

        Require(snapshot.TryRead(CapturedContactCensus.Key, out _),
            "the assembled snapshot carries no contact census, so harm has no captured geometry to forecast from");
        Require(snapshot.TryRead(CaptureCompanionshipInputs.Key, out _),
            "the assembled snapshot carries no companionship inputs, so an empty order cannot be priced as company");

        Require(snapshot.Facts.Count == snapshot.Facts.Select(f => f.Key).Distinct().Count(),
            "two captures published the same fact key into one observation");
        // The engine's frame counter, not `Senses.Tick`, which restarts at zero on every companion spawn
        // and is therefore not a clock anything outside the brain can be compared against. Every capture
        // inside this snapshot stamps the engine's counter, so a snapshot stamping a different one is a
        // frozen observation whose parts disagree about which frame they came from. `TheModelOwnerAcceptsIt`
        // is what actually fails when the two drift; this line only states which clock is the right one.
        Require(snapshot.Tick == (long)Terraria.Main.GameUpdateCount,
            $"the snapshot's clock is not the frame its captures observed; snapshot={snapshot.Tick} frame={Terraria.Main.GameUpdateCount}");
    }

    /// <summary>
    /// A later tick is a new observation and must never read as a model extension of the earlier one.
    /// `IsModelExtensionOf` is what admits a derived query that finished after the freeze, and it
    /// admits only additional modelled or unresolved answers against an unchanged identity. If a
    /// fresh tick satisfied it, a suspended projection could resume against a world that had moved
    /// underneath it and still believe its inputs were frozen.
    /// </summary>
    private static void EachTickIsItsOwnObservation()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Senses.Loot.Pickups.Clear();
        var assembler = Fresh();
        DecisionFactSnapshot first = assembler.Capture(ctx, ctx.Companion.Combat, null, new(double.PositiveInfinity));
        DecisionFactSnapshot second = assembler.Capture(ctx, ctx.Companion.Combat, null, new(double.PositiveInfinity));

        Require(second.Id > first.Id, $"a second observation reused an identity; first={first.Id} second={second.Id}");
        Require(second.ObservationOrdinal > first.ObservationOrdinal,
            "a second observation did not advance the receipt ordinal, so a strike between the two would be attributed to either");
        Require(!second.IsModelExtensionOf(first),
            "a fresh observation read as a model extension of the previous one, which would let a suspended projection resume against a moved world");
        Require(ReferenceEquals(assembler.Current, second), "the assembler did not publish its newest observation");
    }

    private static void ResetEndsTheSequence()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Senses.Loot.Pickups.Clear();
        var assembler = Fresh();
        assembler.Capture(ctx, ctx.Companion.Combat, null, new(double.PositiveInfinity));
        assembler.ResetWorld();
        Require(assembler.Current == null, "a world reset left the previous world's observation published");
        DecisionFactSnapshot after = assembler.Capture(ctx, ctx.Companion.Combat, null, new(double.PositiveInfinity));
        Require(after.Id == 1, $"the first observation of a new world continued the old world's identity sequence at {after.Id}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
