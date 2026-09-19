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

/// <summary>Exercises the census-to-source seam without Terraria state. Native capture is represented
/// by the exact immutable values sources receive, so a source that reads Main cannot make these rows pass.</summary>
internal static class VerifyAssistanceOpportunityDiscovery
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            try { test(); Console.WriteLine("GREEN " + name); }
            catch (Exception error) { red++; Console.WriteLine("RED " + name + ": " + error.Message); }
        }
        Row("G08 assistance census retains every drop and site", AllCapturedSitesAppear());
        Row("G11 assistance source resumes after a cut", CutDoesNotResetCursor());
        Row("G08 replacement generation is a new opportunity", ReplacementDoesNotReuseIdentity());
        Row("G02 immutable capture ignores later caller mutation", CaptureIsImmutable());
        Row("G08 native drop census survives slicing and live item mutation", NativeDropCensus());
        return red;
    }

    private static Action AllCapturedSitesAppear() => () =>
    {
        var facts = Snapshot(1, Drop("item:7", 1, 11, 5), Drop("item:8", 1, 12, 6), Light("tile:5,6"), Pot("tile:7,6"));
        var drops = new DiscoverAssistanceOpportunities("collect-target");
        var lights = new DiscoverAssistanceOpportunities("light-target");
        var pots = new DiscoverAssistanceOpportunities("pot-target");
        var dropResult = drops.Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
        var lightResult = lights.Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
        var potResult = pots.Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
        Require(dropResult.Examined.Count == 2 && dropResult.Coverage.Exhausted && dropResult.Examined.Select(o => o.Key.Target).SequenceEqual(new[] { "item:7", "item:8" }),
            "two observed drops did not survive as two deterministic opportunities");
        Require(lightResult.Examined.Count == 1 && potResult.Examined.Count == 1,
            "one captured dark site and one pot did not each become their own opportunity");
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
        var value = new AssistanceOpportunityFact("collect-target", "item:7", 1, 10, 20, 4, 4, "usable", "observed-drop", "stack=4");
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
            var first = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1));
            Require(!first.Coverage.Exhausted && first.Facts.All(f => f.Key.Kind != "collect-target"),
                "an unfinished native census published a partial denominator");
            // The second live object changes while the expensive contact work is paused.
            // This completed observation must still describe the eight units it captured.
            b.stack = 50;
            var second = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1));
            var targets = second.Facts.Where(f => f.Key.Kind == "collect-target").ToArray();
            var values = targets.Select(f => JsonSerializer.Deserialize<AssistanceOpportunityFact>(f.Value.Text)!).ToArray();
            Require(second.Coverage.Exhausted && targets.Length == 2 && values.Sum(v => v.Stack) == 8
                && values.All(v => v.CensusAmount == 8), "sliced native capture read mutable stacks or normalised each stack independently");
            Require(values.All(v => v.ContactX is null || v.ContactX > 100), "contact coordinates are tile indices rather than native hover pixels");
            b.stack = 5;
            capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1));
            var repeated = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity, 1));
            var again = repeated.Facts.Where(f => f.Key.Kind == "collect-target").ToArray();
            Require(targets.Select(f => (f.Key, f.Version)).SequenceEqual(again.Select(f => (f.Key, f.Version))),
                "unchanged native drops changed generation/version across slices");
            Item replacement = VerifyCollectionContracts.Drop(ItemID.CopperOre, 5, b.Bottom, 8);
            ctx.Senses.Loot.Pickups[1] = new(replacement, 1, 1);
            var replaced = capture.CaptureDrops(ctx.Senses, ctx, new(double.PositiveInfinity));
            Require(replaced.Facts.Single(f => f.Key.Identity == "item:8").Key.Generation != targets.Single(f => f.Key.Identity == "item:8").Key.Generation,
                "a replaced native item slot inherited its predecessor's identity");
            var unknown = new AssistanceOpportunityFact("collect-target", "item:99", 1, 0, 0, 0, 1, "unknown", "landing-undecided", "fixture");
            Require(JsonSerializer.Deserialize<AssistanceOpportunityFact>(JsonSerializer.Serialize(unknown))!.LandingX is null,
                "unknown landing did not survive JSON as unknown");
        }
        finally { Main.item[7] = old7; Main.item[8] = old8; }
    };

    private static DecisionFactSnapshot Snapshot(long id, params DecisionFact[] facts) => new(id, 1, id, id, 0,
        facts.Concat(facts.Select(f => f.Key.Kind).Distinct().Select(kind => new DecisionFact(
            new FactKey(kind.Replace("-target", "-coverage"), "native-census"), 1, new(Text: "complete"), FactEvidence.Observed))));
    private static DecisionFact Drop(string target, long generation, double x, double amount)
        => Fact(new("collect-target", target, generation, x, 0, amount, amount, "usable", "observed-drop", "fixture"));
    private static DecisionFact Light(string target) => Fact(new("light-target", target, 0, 5, 6, 1, 1, "usable", "observed-persistent-darkness", "fixture"));
    private static DecisionFact Pot(string target) => Fact(new("pot-target", target, 0, 7, 6, 1, 1, "usable", "observed-pot-contents-unknown", "fixture"));
    private static DecisionFact Fact(AssistanceOpportunityFact value) => new(new FactKey(value.Domain, value.Target, value.Generation), 1,
        new FactValue(value.Amount, value.X, value.Y, JsonSerializer.Serialize(value)), FactEvidence.Observed);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
