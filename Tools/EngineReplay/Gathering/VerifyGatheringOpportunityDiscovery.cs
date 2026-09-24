#nullable enable

extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using Terraria;
using live::AICompanion.Companion.Brain.Activities.Gathering;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>Headless contract rows for the frozen gathering census.  Program registration belongs
/// to the retained-course coordinator; these rows are intentionally callable without mutating it.</summary>
internal static class VerifyGatheringOpportunityDiscovery
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action action) => red += RunOneRow.GreenOrRed(name, action);
        Row("G08 gathering census retains every ore purpose", EveryCandidateSurvives());
        Row("G11 gathering source resumes at its exact cut", CutResumes());
        Row("G02 gathering facts cannot mutate after capture", SnapshotIsImmutable());
        Row("G08 incomplete ore census remains unresolved", IncompleteCensusRemainsVisible());
        Row("G08 native ore capture resumes and conserves work", NativeOreCaptureUsesFrozenNativeFacts());
        return red;
    }

    private static Action EveryCandidateSurvives() => () =>
    {
        var facts = Snapshot(1, Mine("ore:7:10,4", 1, 10, 4, 24), Mine("ore:7:13,4", 1, 13, 4, 12));
        var mine = new GatheringOpportunitySource("mine-target");
        var mineSlice = mine.Continue(facts, new DecisionWorkCursor(), new(double.PositiveInfinity));
        Require(mineSlice.Examined.Select(opportunity => opportunity.Key.Target).SequenceEqual(new[] { "ore:7:10,4", "ore:7:13,4" }),
            "ore discovery collapsed the frozen census to one nearest target");
    };

    private static Action CutResumes() => () =>
    {
        var source = new GatheringOpportunitySource("mine-target");
        var cursor = new DecisionWorkCursor();
        var facts = Snapshot(2, Mine("ore:1:1,1", 1, 1, 1, 1), Mine("ore:1:2,1", 1, 2, 1, 1), Mine("ore:1:3,1", 1, 3, 1, 1));
        OpportunitySlice first = source.Continue(facts, cursor, new(double.PositiveInfinity, 1));
        OpportunitySlice second = source.Continue(Snapshot(3, Mine("ore:1:1,1", 1, 1, 1, 1), Mine("ore:1:2,1", 1, 2, 1, 1), Mine("ore:1:3,1", 1, 3, 1, 1)), cursor, new(double.PositiveInfinity, 2));
        Require(first.Examined.Single().Key.Target == "ore:1:1,1" && first.Coverage.BudgetCut && cursor.Offset == 3,
            "the one-operation slice did not retain its cursor");
        Require(second.Examined.Select(opportunity => opportunity.Key.Target).SequenceEqual(new[] { "ore:1:2,1", "ore:1:3,1" }) && second.Coverage.Exhausted,
            "the next slice restarted or skipped frozen candidates");
    };

    private static Action SnapshotIsImmutable() => () =>
    {
        // With the pick's captured work, as the census publishes every usable ore: a site with no `Work` offers
        // no need by design (the source's own note, 22 September 2026), and this row read `Needs.Single()` of an
        // empty list — `Sequence contains no elements` — from the day that rule landed until it was registered.
        GatheringOpportunityFact captured = new("mine-target", "ore:1:1,1", 1, 1, 1, 1, "mine", 18, 18, "usable", "native", "fixture",
            Work: PickWork(18));
        DecisionFact fact = Fact(captured);
        string frozen = fact.Value.Text;
        captured = captured with { RemainingAmount = 99, Reason = "mutated-after-capture" };
        Opportunity opportunity = new GatheringOpportunitySource("mine-target").Continue(Snapshot(3, fact), new DecisionWorkCursor(), new(double.PositiveInfinity)).Examined.Single();
        Require(opportunity.Needs.Single().RemainingAmount == 18 && fact.Value.Text == frozen,
            "post-capture caller mutation changed a course fact");
    };

    private static Action IncompleteCensusRemainsVisible() => () =>
    {
        DecisionFact incomplete = Fact(new GatheringOpportunityFact("mine-target", "ore:1:1,1", 1, 1, 1, 1, "mine", 400, 400,
            "usable", "native;vein-complete=False", "fixture", Work: PickWork(400)), FactEvidence.Unresolved);
        OpportunitySlice slice = new GatheringOpportunitySource("mine-target").Continue(Snapshot(4, false, incomplete), new DecisionWorkCursor(), new(double.PositiveInfinity));
        Opportunity opportunity = slice.Examined.Single();
        Require(opportunity.Dependencies.Reads.Any(read => read.Key == incomplete.Key && read.Evidence == FactEvidence.Unresolved)
            && opportunity.Needs.Single().CensusAmount == 400 && !slice.Coverage.Exhausted,
            "a bound-hit vein became a complete census or the source claimed an incomplete capture exhausted");
    };

    private static Action NativeOreCaptureUsesFrozenNativeFacts() => () =>
    {
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, Terraria.ID.TileID.Copper,
            new Microsoft.Xna.Framework.Point(25, 59), new Microsoft.Xna.Framework.Point(45, 59));
        var capture = new CaptureGatheringOpportunities();
        DecisionFactSnapshot first = CaptureAll(capture, context, 1, 101);
        DecisionFact[] firstOres = first.Facts.Where(fact => fact.Key.Kind == "mine-target").ToArray();
        Require(firstOres.Length == 2 && firstOres.All(fact => fact.Evidence == FactEvidence.Observed),
            "one-operation native capture did not eventually observe both separated ore purposes");
        var firstValues = firstOres.Select(fact => JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text)!).ToArray();
        Require(firstValues.All(value => value.StandX != 0 || value.StandY != 0),
            "native capture recorded a solid target instead of FindToolAccess's proven working pose");
        DecisionFactSnapshot unchanged = CaptureAll(capture, context, 1, 102);
        Require(firstOres.All(before => unchanged.TryRead(before.Key, out DecisionFact after) && after.Version == before.Version),
            "an unchanged completed native census changed target fact versions");

        Item pick = TileMiner.PickaxeFor(context.Player);
        Microsoft.Xna.Framework.Point firstTile = firstValues[0].TileX == 25 ? new(25, 59) : new(45, 59);
        double beforeTotal = firstValues.Sum(value => value.RemainingAmount);
        Require(context.Companion.Miner.Swing(firstTile, pick), "native capture conservation needs one companion pick strike");
        DecisionFactSnapshot damaged = CaptureAll(capture, context, 1, 103);
        double damagedTotal = OreValues(damaged).Sum(value => value.RemainingAmount);
        Require(damagedTotal > 0 && damagedTotal < beforeTotal,
            $"native partial damage must reduce captured physical work without erasing the other vein: before={beforeTotal}; after={damagedTotal}");

        while (Terraria.Main.tile[firstTile.X, firstTile.Y].HasTile)
        {
            for (int tick = 0; tick < pick.useTime; tick++) context.Companion.Miner.Tick();
            Require(context.Companion.Miner.Swing(firstTile, pick), "native removal needs a ready companion pick strike");
        }
        DecisionFactSnapshot removed = CaptureAll(capture, context, 1, 104);
        GatheringOpportunityFact[] remaining = OreValues(removed).ToArray();
        Require(remaining.Length == 1 && remaining.Single().CensusAmount == remaining.Single().RemainingAmount,
            "after a native removal the completed material census must conserve only the surviving vein's work");
    };

    private static DecisionFactSnapshot CaptureAll(CaptureGatheringOpportunities capture, ActionContext context, long operations, long id)
    {
        IReadOnlyList<DecisionFact> facts = Array.Empty<DecisionFact>();
        for (int tick = 0; tick < 10000; tick++)
        {
            context.Companion.Brain.Senses.Update(context.Npc, context.Player);
            facts = capture.Capture(context, new DecisionWorkBudget(double.PositiveInfinity, operations));
            DecisionFact coverage = facts.Single(fact => fact.Key.Kind == "mine-coverage");
            if (coverage.Evidence == FactEvidence.Observed) return new DecisionFactSnapshot(id, 1, id, id, 0, facts);
        }
        throw new InvalidOperationException("one-operation native capture did not complete its established ore search area");
    }

    private static IEnumerable<GatheringOpportunityFact> OreValues(DecisionFactSnapshot facts)
        => facts.Facts.Where(fact => fact.Key.Kind == "mine-target")
            .Select(fact => JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text)!);

    private static DecisionFactSnapshot Snapshot(long id, params DecisionFact[] facts) => Snapshot(id, true, facts);
    private static DecisionFactSnapshot Snapshot(long id, bool complete, params DecisionFact[] facts)
        => new(id, 1, id, id, 0, facts.Concat(new[] { Coverage("mine-coverage", complete), Coverage("chop-coverage", true) }));
    private static DecisionFact Mine(string target, long generation, int x, int y, double amount) => Fact(new("mine-target", target, generation, x, y, 7, "mine", amount, amount, "usable", "native", "fixture"));
    private static DecisionFact Fact(GatheringOpportunityFact value, FactEvidence evidence = FactEvidence.Observed) => new(new FactKey(value.Domain, value.Target, value.Generation), 1,
        new FactValue(value.RemainingAmount, value.TileX * 16 + 8, value.TileY * 16 + 8, JsonSerializer.Serialize(value)), evidence);
    private static DecisionFact Coverage(string domain, bool complete) => new(new FactKey(domain, "native-census"), 1,
        new FactValue(Text: JsonSerializer.Serialize(new GatheringCoverageFact(domain, 0, complete ? 1 : 0, complete, "fixture"))),
        complete ? FactEvidence.Observed : FactEvidence.Unresolved);
    /// <summary>A copper pickaxe's captured work with the given damage left, so a hand-built site offers the
    /// need a native capture would. The pick's own numbers are not the subject of either row that uses it.</summary>
    private static CapturedToolWork PickWork(int damageRemaining)
        => new(Terraria.ID.ItemID.CopperPickaxe, 0, 35, 15, 35, damageRemaining);

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
