extern alias live;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using live::AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>Exercises the census-to-source seam without Terraria state. Native capture is represented
/// by the exact immutable values sources receive, so a source that reads Main cannot make these rows pass.</summary>
internal static class VerifyAssistanceOpportunityDiscovery
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            red += RunOneRow.GreenOrRed(name, test);
        }
        Row("G08 assistance census retains every drop and site", AllCapturedSitesAppear());
        Row("G11 assistance source resumes after a cut", CutDoesNotResetCursor());
        Row("G08 replacement generation is a new opportunity", ReplacementDoesNotReuseIdentity());
        Row("G02 immutable capture ignores later caller mutation", CaptureIsImmutable());
        Row("G08 native drop census survives slicing and live item mutation", NativeDropCensus());
        Row("G03 every assistance domain's real capture feeds its own discovery", CaptureFeedsDiscovery());
        Row("G02 discovery walks the census's rank, so a cut slice is the sites the census ranked highest", DiscoveryFollowsTheCensusRank());
        return red;
    }

    /// <summary>
    /// The half of the census bound nobody had asked about until a sentinel did: whether the published
    /// rank ever reaches the search at all.
    ///
    /// It did not. This source re-sorted the published facts with `OrderBy(f => f.Key)` — tile-identity
    /// order, the very thing `RankCensusSitesByWorth` exists to stop deciding what the course sees — and
    /// walked *that* into a store of 64 candidates across six domains with a per-domain floor of ten.
    /// So the bound decided membership of the snapshot while tile order still decided which members were
    /// priced, and three documents said otherwise: the class docstring's "the set it publishes is the
    /// nearest usable sites rather than the first tiles in key order", the commit body's "the census was
    /// choosing what the course could see, by coordinate" read as closed, and the guide's "what survives
    /// the cut is the nearest sites". All three were true of the snapshot and false of the pricing set.
    ///
    /// The row builds the disagreement deliberately: twelve sites whose rank is the **reverse** of their
    /// key order, published in rank order the way the census publishes them, and an allowance that pays
    /// for four. What the slice examines must be the four the census ranked highest, which here are the
    /// four *highest* keys. Restoring `OrderBy(f => f.Key)` gives the four lowest and reds it.
    ///
    /// Keys are `tile:04,06` rather than `tile:4,6` on purpose: ordinal string order and numeric order
    /// disagree above nine, so an unpadded scene would make the mutation pass on some of its rows by
    /// accident rather than fail on all of them.
    /// </summary>
    private static Action DiscoveryFollowsTheCensusRank() => () =>
    {
        // Published in rank order — nearest first — and the nearest is the highest key here.
        DecisionFact[] ranked = Enumerable.Range(0, 12)
            .Select(i => Light($"tile:{11 - i:00},06"))
            .ToArray();
        string[] byRank = ranked.Select(f => f.Key.Identity).ToArray();
        string[] byKey = byRank.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Require(!byRank.Take(4).SequenceEqual(byKey.Take(4)),
            "premise: rank order and key order must disagree over the examined prefix, or this row cannot fail");

        var source = new DiscoverAssistanceOpportunities("light-target");
        var slice = source.Continue(Snapshot(1, ranked), new DecisionWorkCursor(), new(double.PositiveInfinity, 4));
        string[] examined = slice.Examined.Select(o => o.Key.Target).ToArray();
        Require(examined.Length == 4,
            $"premise: the allowance must cut the walk at four sites, or nothing is being selected; examined {examined.Length}");
        Require(examined.SequenceEqual(byRank.Take(4)),
            $"a cut slice must be the sites the census ranked highest and was [{string.Join(" ", examined)}]; "
            + $"by rank [{string.Join(" ", byRank.Take(4))}], by key [{string.Join(" ", byKey.Take(4))}]");
    };

    /// <summary>
    /// The seam, driven end to end: the real native capture produces the facts, and the three real
    /// sources read them. Every other row in this file hands a source facts written by hand, which is
    /// right for testing the source and is exactly why this gap survived — each half was correct
    /// against a fixture and the two halves did not agree with each other.
    ///
    /// What they disagreed about: a source calls its census exhausted only when it reads a
    /// `&lt;domain&gt;-coverage` fact as Observed, and the capture emitted that fact for drops alone. Light
    /// and pot sites were published with no coverage, so their census could never read complete, and
    /// by this tree's rule that optional work does not start on an unanswered search, a wired brain
    /// would never have placed a torch or broken a pot. Nothing caught it because nothing ran both
    /// halves together until the live tick was being wired.
    ///
    /// The row asserts exhaustion rather than a site count on purpose. An empty floor has no drops,
    /// no pots and no dark tiles, so the counts are legitimately zero and only the coverage answer
    /// distinguishes "looked and found nothing" from "never finished looking" — which is the whole
    /// three-valued rule this codebase keeps everywhere else.
    /// </summary>
    private static Action CaptureFeedsDiscovery() => () =>
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        ctx.Senses.Loot.Pickups.Clear();
        var capture = new CaptureAssistanceOpportunities();
        IReadOnlyList<DecisionFact> captured = capture.Capture(ctx.Senses, ctx);
        var facts = new DecisionFactSnapshot(91, 1, ctx.Senses.Tick, 1, 0, captured);

        // Each domain must publish a coverage fact, and the discovery that reads it must agree with what
        // it says. Asserting Observed for all three was wrong: in colour mode the light window is
        // intersected with the engine's own processed area, which is empty until the engine scans near
        // the companion and never does in a headless scene — so Unresolved is the honest answer there,
        // and the contract worth guarding is that capture and discovery reach the *same* verdict rather
        // than that the verdict is always the optimistic one.
        // Two assistance domains since pots became collection work on 23 September 2026. Collection's one coverage fact
        // covers both of its sweeps, drops and pots, and there is no pot coverage left to publish.
        Require(!facts.TryRead(new FactKey("pot-coverage", "native-census"), out _),
            "the capture still publishes a pot coverage fact, a second completeness for collection a reader could consult for the wrong half");
        foreach (string domain in new[] { "collect-target", "light-target" })
        {
            string coverage = domain.Replace("-target", "-coverage", StringComparison.Ordinal);
            Require(facts.TryRead(new FactKey(coverage, "native-census"), out DecisionFact fact),
                $"the real capture published no {coverage} at all, so {domain} discovery can never report a finished census");
            bool swept = fact.Evidence == FactEvidence.Observed;
            var slice = new DiscoverAssistanceOpportunities(domain)
                .Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
            Require(slice.Coverage.Exhausted == swept,
                $"{domain} discovery reported exhausted={slice.Coverage.Exhausted} while its capture published {fact.Evidence}; the two halves of the seam disagree");
        }

        // The same question asked of the other half of the class. Gathering publishes its two coverage
        // facts already, so these two arms are a guard rather than a repair — and they are here because
        // a defect found in one member of a class is checked across the class, not fixed where it
        // happened to surface. The gathering capture is a separate object with its own cursor, so it
        // gets its own snapshot rather than sharing the assistance one.
        var gathered = new CaptureGatheringOpportunities().Capture(ctx, new(double.PositiveInfinity));
        var gatheringFacts = new DecisionFactSnapshot(92, 1, ctx.Senses.Tick, 1, 0, gathered);
        foreach (string domain in new[] { "mine-target", "chop-target" })
        {
            string coverage = domain.Replace("-target", "-coverage", StringComparison.Ordinal);
            Require(gatheringFacts.TryRead(new FactKey(coverage, "native-census"), out DecisionFact fact)
                && fact.Evidence == FactEvidence.Observed,
                $"the real gathering capture published no observed {coverage}, so {domain} discovery can never report a finished census");
            var slice = new GatheringOpportunitySource(domain)
                .Continue(gatheringFacts, new DecisionWorkCursor(), new(double.PositiveInfinity));
            Require(slice.Coverage.Exhausted,
                $"{domain} discovery read the real capture and still could not call its census exhausted");
        }
    };

    private static Action AllCapturedSitesAppear() => () =>
    {
        var facts = Snapshot(1, Drop("item:7", 1, 11, 5), Drop("item:8", 1, 12, 6), Light("tile:5,6"), Pot("tile:7,6"));
        var collection = new DiscoverAssistanceOpportunities("collect-target");
        var lights = new DiscoverAssistanceOpportunities("light-target");
        var collected = collection.Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
        var lightResult = lights.Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
        Opportunity[] drops = collected.Examined.Where(o => o.Key.Purpose == OpportunityPurposes.Collect).ToArray();
        Opportunity[] pots = collected.Examined.Where(o => o.Key.Purpose == OpportunityPurposes.BreakPot).ToArray();
        Require(drops.Length == 2 && collected.Coverage.Exhausted && drops.Select(o => o.Key.Target).SequenceEqual(new[] { "item:7", "item:8" }),
            "two observed drops did not survive as two deterministic opportunities");
        // A pot is collection work: one opportunity under collect-target, whose need is a container rather than loot,
        // because its contents are unknown until the native break produces them.
        Require(pots.Length == 1 && pots[0].Key.Domain == "collect-target"
            && pots[0].Needs.Single().Key.Kind == NeedKind.Container && drops.All(d => d.Needs.Single().Key.Kind == NeedKind.Loot),
            $"the pot did not become one collection opportunity with a container need; pots={pots.Length}");
        Require(lightResult.Examined.Count == 1,
            "one captured dark site did not become its own opportunity");
    };

    private static Action CutDoesNotResetCursor() => () =>
    {
        var facts = Snapshot(2, Drop("item:7", 1, 1, 1), Drop("item:8", 1, 2, 1), Drop("item:9", 1, 3, 1));
        var source = new DiscoverAssistanceOpportunities("collect-target");
        var cursor = new DecisionWorkCursor();
        var first = source.Continue(facts, cursor, new(double.PositiveInfinity, 1));
        Require(first.Examined.Single().Key.Target == "item:7" && first.Coverage.BudgetCut && cursor.Offset == 1,
            "the first one-operation cut did not preserve the next drop");
        var second = source.Continue(new DecisionFactSnapshot(20, 1, 3, 3, 0, facts.Facts), cursor, new(double.PositiveInfinity, 2));
        Require(second.Examined.Select(o => o.Key.Target).SequenceEqual(new[] { "item:8", "item:9" }) && second.Coverage.Exhausted,
            "the continuation restarted or skipped sites after the cut");
    };

    private static Action ReplacementDoesNotReuseIdentity() => () =>
    {
        var oldFacts = Snapshot(3, Drop("item:7", 1, 1, 1));
        var replacement = Snapshot(4, Drop("item:7", 2, 1, 1));
        var source = new DiscoverAssistanceOpportunities("collect-target");
        var cursor = new DecisionWorkCursor();
        long oldGeneration = source.Continue(oldFacts, cursor, new(double.PositiveInfinity)).Examined.Single().Key.Generation;
        long newGeneration = source.Continue(replacement, cursor, new(double.PositiveInfinity)).Examined.Single().Key.Generation;
        Require(oldGeneration == 1 && newGeneration == 2, "a replacement slot reused the old opportunity generation");
    };

    private static Action CaptureIsImmutable() => () =>
    {
        var value = new AssistanceOpportunityFact("collect-target", OpportunityPurposes.Collect, "item:7", 1, 10, 20, 4, 4, "usable", "observed-drop", "stack=4");
        var fact = Fact(value);
        string serialized = fact.Value.Text;
        value = value with { Amount = 99, Detail = "mutated-after-capture" };
        var source = new DiscoverAssistanceOpportunities("collect-target");
        Opportunity opportunity = source.Continue(Snapshot(5, fact), new DecisionWorkCursor(), new(double.PositiveInfinity)).Examined.Single();
        Require(opportunity.Needs.Single().RemainingAmount == 4 && fact.Value.Text == serialized,
            "a caller-side mutation changed the frozen captured fact");
    };

    private static Action NativeDropCensus() => () =>
    {
        Item old7 = Main.item[7], old8 = Main.item[8];
        try
        {
            var ctx = VerifyCollectionContracts.SetUpFloor();
            Item a = VerifyCollectionContracts.Drop(ItemID.CopperOre, 3, new Vector2(25 * 16 + 8, 60 * 16), 7);
            Item b = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, new Vector2(27 * 16 + 8, 60 * 16), 8);
            ctx.Senses.Loot.Pickups.Clear();
            foreach (var item in new[] { a, b }) ctx.Senses.Loot.Pickups.Add(new(item, 1, 1));
            var capture = new CaptureAssistanceOpportunities();
            // The drop census alone is under test; its coverage also answers for the pot sweep, which on this floor is
            // a complete sweep of nothing.
            PotSweep pots = capture.CapturePots(ctx.Senses, ctx);
            Require(pots.Complete && pots.Facts.Count == 0, "premise: the floor has no pots and the pot sweep completes");
            var first = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1), pots);
            Require(!first.Coverage.Exhausted && first.Facts.All(f => f.Key.Kind != "collect-target"),
                "an unfinished native census published a partial denominator");
            // The second live object changes while the expensive contact work is paused.
            // This completed observation must still describe the eight units it captured.
            b.stack = 50;
            var second = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1), pots);
            var targets = second.Facts.Where(f => f.Key.Kind == "collect-target").ToArray();
            var values = targets.Select(f => JsonSerializer.Deserialize<AssistanceOpportunityFact>(f.Value.Text)!).ToArray();
            Require(second.Coverage.Exhausted && targets.Length == 2 && values.Sum(v => v.Stack) == 8
                && values.All(v => v.CensusAmount == 8), "sliced native capture read mutable stacks or normalised each stack independently");
            Require(values.All(v => v.ContactX is null || v.ContactX > 100), "contact coordinates are tile indices rather than native hover pixels");
            b.stack = 5;
            capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1), pots);
            var repeated = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1), pots);
            var again = repeated.Facts.Where(f => f.Key.Kind == "collect-target").ToArray();
            Require(targets.Select(f => (f.Key, f.Version)).SequenceEqual(again.Select(f => (f.Key, f.Version))),
                "unchanged native drops changed generation/version across slices");
            Item replacement = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, b.Bottom, 8);
            ctx.Senses.Loot.Pickups[1] = new(replacement, 1, 1);
            var replaced = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity), pots);
            Require(replaced.Facts.Single(f => f.Key.Identity == "item:8").Key.Generation != targets.Single(f => f.Key.Identity == "item:8").Key.Generation,
                "a replaced native item slot inherited its predecessor's identity");
            var unknown = new AssistanceOpportunityFact("collect-target", OpportunityPurposes.Collect, "item:99", 1, 0, 0, 0, 1, "unknown", "landing-undecided", "fixture");
            Require(JsonSerializer.Deserialize<AssistanceOpportunityFact>(JsonSerializer.Serialize(unknown))!.LandingX is null,
                "unknown landing did not survive JSON as unknown");
        }
        finally { Main.item[7] = old7; Main.item[8] = old8; }
    };

    private static DecisionFactSnapshot Snapshot(long id, params DecisionFact[] facts) => new(id, 1, id, id, 0,
        facts.Concat(facts.Select(f => f.Key.Kind).Distinct().Select(kind => new DecisionFact(
            new FactKey(kind.Replace("-target", "-coverage"), "native-census"), 1, new(Text: "complete"), FactEvidence.Observed))));
    private static DecisionFact Drop(string target, long generation, double x, double amount)
        => Fact(new("collect-target", OpportunityPurposes.Collect, target, generation, x, 0, amount, amount, "usable", "observed-drop", "fixture"));
    private static DecisionFact Light(string target) => Fact(new("light-target", OpportunityPurposes.Light, target, 0, 5, 6, 1, 1, "usable", "observed-persistent-darkness", "fixture"));
    private static DecisionFact Pot(string target) => Fact(new("collect-target", OpportunityPurposes.BreakPot, target, 0, 7, 6, 1, 1, "usable", "observed-pot-contents-unknown", "fixture"));
    private static DecisionFact Fact(AssistanceOpportunityFact value) => new(new FactKey(value.Domain, value.Target, value.Generation), 1,
        new FactValue(value.Amount, value.X, value.Y, JsonSerializer.Serialize(value)), FactEvidence.Observed);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
