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
            try { test(); Console.WriteLine("GREEN " + name); }
            catch (Exception error) { red++; Console.WriteLine("RED " + name + ": " + error.Message); }
        }
        Row("G01 one snapshot carries every domain's census", EveryDomainAppears);
        Row("G01 each tick is a new observation, not an extension of the last", EachTickIsItsOwnObservation);
        Row("G01 a world reset ends the snapshot sequence", ResetEndsTheSequence);
        return red;
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

        foreach (string coverage in new[] { "collect-coverage", "light-coverage", "pot-coverage", "mine-coverage", "chop-coverage" })
            Require(snapshot.TryRead(new FactKey(coverage, "native-census"), out DecisionFact fact)
                && fact.Evidence == FactEvidence.Observed,
                $"the assembled snapshot carries no observed {coverage}, so that domain can never finish a census");

        Require(snapshot.TryRead(CapturedContactCensus.Key, out _),
            "the assembled snapshot carries no contact census, so harm has no captured geometry to forecast from");
        Require(snapshot.TryRead(CaptureCompanionshipInputs.Key, out _),
            "the assembled snapshot carries no companionship inputs, so an empty order cannot be priced as company");

        Require(snapshot.Facts.Count == snapshot.Facts.Select(f => f.Key).Distinct().Count(),
            "two captures published the same fact key into one observation");
        Require(snapshot.Tick == ctx.Senses.Tick,
            $"the snapshot's clock is not the tick it observed; snapshot={snapshot.Tick} senses={ctx.Senses.Tick}");
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
