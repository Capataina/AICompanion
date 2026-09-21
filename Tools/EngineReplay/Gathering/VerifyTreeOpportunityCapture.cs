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
        + RunOneRow.Case("G08 missing gathering coverage remains unresolved", MissingCoverage)
        + RunOneRow.Case("G08 a moved body re-answers every held site and a one-operation sweep still advances", ReanswerAndStarvation);

    /// <summary>
    /// The two properties the re-answer replaced a full rescan with, neither of which had a row until an
    /// adversarial review pointed out that nothing anywhere asserted on the path at all — it ran only
    /// incidentally inside the 900-tick brain fixtures, and the one row that touched it exercised a census
    /// of a single site, where a cursor completes in one iteration and cannot mix or stall.
    ///
    /// **Two trunks, not one, and a starved allowance, because the defects only exist above N = 1.**
    /// A round over several sites can be cut mid-cursor, and the question is whether every held site is
    /// eventually re-answered against the current body rather than some of them being left on a stale
    /// verdict for ever. And a sweep that reaches a site with no allowance left must still make progress
    /// on the next call: charging the block on `offset % Block == 0` meant a site sitting exactly on a
    /// block boundary spent the block, failed the site, rewound onto the multiple and charged it again,
    /// which at an allowance of one operation is a livelock the harness reports as a hang.
    /// </summary>
    private static void ReanswerAndStarvation()
    {
        // The ore census rather than the trunk one, because the observable has to be a term that really
        // moves with the body and only ore has a clean one. Two earlier drafts of this row would each
        // have failed against correct code, which is the kind that gets "fixed" by loosening it: the
        // first asserted on a site's stand, which for a tree is a free cell beside it and the same point
        // wherever the companion is; the second threw the reach flood away and asserted the verdict
        // flipped to `approach-not-yet`, and it did not, correctly, because the flood resettles and the
        // trunks are genuinely still reachable from the new pose.
        //
        // `InNewActivityAllowance` reads the companion's own position, so a body carried further from
        // the player's heading than the new-work radius makes every site `outside-allowance-or-protected`
        // whatever the site is. That is unambiguous, it is exactly the admission term a re-answer exists
        // to refresh, and a site still reading `observed-native-ore` is one the round never reached.
        var (_, context) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, new Point(25, 59));
        VerifyOreWork.Place(new Point(45, 59), TileID.Copper);
        VerifyOreWork.ResettleReach(context);
        var capture = new CaptureGatheringOpportunities();

        // Driven one operation at a time throughout, which is the starvation half: every call must
        // either advance the cursor or take a site, and a call that does neither for ever is the
        // livelock a block charged on `offset % Block == 0` produced when a site sat exactly on a block
        // boundary — the block unit taken, the site spend refused, the offset rewound onto the multiple.
        // The bound is the window's own cell count, so the row says "finite" rather than restating a
        // literal that goes stale the next time the window moves.
        int radius = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.WorkCensusRadiusTiles;
        long cells = (long)(2 * radius + 1) * (2 * radius + 1);
        GatheringOpportunityFact[] Ores(IReadOnlyList<DecisionFact> facts)
            => facts.Where(fact => fact.Key.Kind == "mine-target")
                .Select(fact => JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text)!).ToArray();

        var before = Array.Empty<GatheringOpportunityFact>();
        for (long slice = 0; slice < 4 * cells && before.Length < 2; slice++)
            before = Ores(capture.Capture(context, new(double.PositiveInfinity, 1)));
        Require(before.Length == 2,
            $"the scene needs two veins for a cursor to be able to span them; got {before.Length}");
        Require(before.All(ore => ore.Reason == "observed-native-ore"),
            "both veins must start admitted, or the flip below proves nothing: "
            + string.Join("; ", before.Select(ore => $"{ore.TileX},{ore.TileY} {ore.Reason}")));

        float allowance = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        context.Npc.Center = context.Senses.Intent.Region.Heading + new Vector2(allowance * 2, 0);
        var after = Array.Empty<GatheringOpportunityFact>();
        for (long slice = 0; slice < 4 * cells; slice++)
        {
            after = Ores(capture.Capture(context, new(double.PositiveInfinity, 1)));
            if (after.Length == 2 && after.All(ore => ore.Reason == "outside-allowance-or-protected")) break;
        }
        Require(after.Length == 2, $"the re-answer lost a vein; got {after.Length}");
        Require(after.All(ore => ore.Reason == "outside-allowance-or-protected"),
            "a body carried beyond the new-work allowance left a held vein on the verdict it carried "
            + "before, so the round never reached it: "
            + string.Join("; ", after.Select(ore => $"{ore.TileX},{ore.TileY} {ore.Reason}")));
    }

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
        // One slice is one cell, so the bound is the census's own cell count with room for one reopening,
        // derived rather than written down: it stood at a literal 10,000 against a box of 81 x 81, and the
        // day the census window became the admission radius (127 x 127 = 16,129) the row failed as though
        // the census had hung. A bound on a finite scan says "finite", and it is only saying that while it
        // is the scan's own size.
        // Read from the allowance the census window is derived from, not from the derived tile count, so
        // this bound follows the radius through any later change of how the tiles are rounded.
        int radius = (int)Math.Ceiling(
            live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.NewActivityRadius / 16f);
        long cells = (long)(2 * radius + 1) * (2 * radius + 1);
        for (long slice = 0; slice < 2 * cells; slice++)
        {
            IReadOnlyList<DecisionFact> facts = capture.Capture(context, new(double.PositiveInfinity, 1));
            if (facts.Single(fact => fact.Key.Kind == "chop-coverage").Evidence == FactEvidence.Observed) return facts;
        }
        throw new InvalidOperationException(FormattableString.Invariant(
            $"finite native tree census did not finish in {2 * cells} one-operation slices over its own {cells}-cell window"));
    }

    private static GatheringOpportunityFact[] Values(IReadOnlyList<DecisionFact> facts) => facts.Where(fact => fact.Key.Kind == "chop-target")
        .Select(fact => JsonSerializer.Deserialize<GatheringOpportunityFact>(fact.Value.Text)!).ToArray();
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
