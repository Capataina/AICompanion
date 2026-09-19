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
using live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

internal static class VerifyTreeOpportunityCapture
{
    public static int Run() => RunOneRow.Case("G08 tree census retains native work and observes axe effects", NativeTreeCensus)
        + RunOneRow.Case("G08 missing gathering coverage remains unresolved", MissingCoverage);

    private static void NativeTreeCensus()
    {
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, new Point(25, 59));
        Main.tile[25, 59].ClearEverything();
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        Main.tileAxe[TileID.Trees] = true;
        Main.tileSolid[TileID.Trees] = false;
        TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
        // GetTreeBottom stops fifty tiles above the world's lower boundary. The replay world
        // is only one hundred tiles high, so a multi-cell trunk must sit above that native guard.
        foreach (int x in new[] { 25, 45 })
        {
            VerifyOreWork.Place(new Point(x, 40), TileID.Dirt);
            for (int y = 37; y <= 39; y++) VerifyOreWork.Place(new Point(x, y), TileID.Trees);
        }
        var capture = new CaptureTreeOpportunities();
        var cut = capture.Capture(context, new(double.PositiveInfinity, 1));
        Require(cut.All(fact => fact.Key.Kind != "chop-target"), "a partial rectangle published a tree denominator");
        var facts = Complete(capture, context);
        var trees = Values(facts);
        Require(trees.Length == 2 && trees.All(tree => tree.TileY == 39), "trunk branches became separate jobs or lost bottom identity");
        double total = trees.Sum(tree => tree.RemainingAmount);
        Require(total > 0 && trees.All(tree => tree.CensusAmount == total), "native axe work did not share a physical census");
        var same = Complete(capture, context);
        Require(facts.Where(fact => fact.Key.Kind == "chop-target").All(before => same.Any(after => after.Key == before.Key && after.Version == before.Version)),
            "unchanged tree capture churned semantic versions");
        Item axe = TileChopper.AxeFor(context.Player);
        var bottom = new Point(25, 39);
        Require(context.Companion.Chopper.Swing(bottom, axe), "native tree fixture needs a real axe strike");
        var damaged = Values(Complete(capture, context));
        Require(damaged.Sum(tree => tree.RemainingAmount) < total && damaged.Any(tree => tree.TileX == 45),
            "native axe progress erased the other tree or failed to reduce remaining work");
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        Require(Values(Complete(capture, context)).All(tree => tree.Admission == "unusable" && tree.Reason == "chopping-disabled"),
            "a disabled policy retained executable tree facts");
    }

    private static void MissingCoverage()
    {
        var snapshot = new DecisionFactSnapshot(1, 1, 1, 1, 0, Array.Empty<DecisionFact>());
        var slice = new GatheringOpportunitySource("chop-target").Continue(snapshot, new(), new(double.PositiveInfinity));
        Require(!slice.Coverage.Exhausted && slice.Examined.Count == 0, "missing census coverage became an empty completed census");
    }

    private static IReadOnlyList<DecisionFact> Complete(CaptureTreeOpportunities capture, ActionContext context)
    {
        for (int slice = 0; slice < 10000; slice++)
        {
            IReadOnlyList<DecisionFact> facts = capture.Capture(context, new(double.PositiveInfinity, 1));
            if (facts.Single(fact => fact.Key.Kind == "chop-coverage").Evidence == FactEvidence.Observed) return facts;
        }
        throw new InvalidOperationException("finite native tree census did not finish under one-operation slices");
    }

    private static GatheringOpportunityFact[] Values(IReadOnlyList<DecisionFact> facts) => facts.Where(fact => fact.Key.Kind == "chop-target")
        .Select(fact => JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text)!).ToArray();
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
