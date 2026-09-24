extern alias live;

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using PlaceTorches = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.PlaceTorches;

/// <summary>Drives the real colour-engine scan, Smart Cursor torch step, and reach sense through
/// the lighting-capture seam, including private projections using the engine's LightMap blur.</summary>
internal static class VerifyLightingOpportunityCapture
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            try { red += RunOneRow.GreenOrRed(name, test); }
            finally { VerifyUsefulAssistance.ClearMeasuredLight(); }
        }
        Row("G08 native lighting capture preserves cuts and shared deficits", NativeSitesShareOneObservedDeficitCensus);
        Row("G02 unmeasured lighting remains unresolved rather than dark", UnmeasuredIsNotDarkness);
        Row("G08 projected torch light matches a real placed torch after a native rescan", ProjectionMatchesNativePlacement);
        Row("G08 captured light survives later scans and reports uncovered placements", ProjectionSnapshotSurvivesLiveChanges);
        return red;
    }

    private static void NativeSitesShareOneObservedDeficitCensus()
    {
        var context = VerifyCollectionContracts.SetUpFloor();
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.05f), 1f, placed: null);
        context.Senses.Update(context.Npc, context.Player);
        SettleReach(context);
        var capture = new CaptureLightingOpportunities();
        LightingCaptureSlice first = capture.Capture(context.Senses, context, new(double.PositiveInfinity, 1));
        Require(!first.Coverage.Exhausted && first.Coverage.BudgetCut && first.Facts.All(f => f.Key.Kind != "lighting-site"),
            "a one-operation cut published a partial lighting census");
        LightingCaptureSlice complete = capture.Capture(context.Senses, context, new(double.PositiveInfinity));
        var sites = complete.Facts.Where(f => f.Key.Kind == "lighting-site")
            .Select(f => JsonSerializer.Deserialize<LightingOpportunityFact>(f.Value.Text)!).ToArray();
        var deficits = complete.Facts.Where(f => f.Key.Kind == "lighting-deficit")
            .Select(f => JsonSerializer.Deserialize<LightingDeficitFact>(f.Value.Text)!).ToArray();
        Require(complete.Coverage.Exhausted && sites.Length >= 2,
            $"the native scan did not retain two Smart Cursor sites; sites={sites.Length} coverage={complete.Coverage}");
        Require(sites.Count(site => site.Admission == "usable") >= 2 && sites.All(site => site.ContactX > 0 && site.ContactY > 0
            && site.CoverageEvidence == "modelled-captured-preblur-lightmap" && site.NativeUseTicks == 1 && site.EffectTicks is null),
            "a captured site omitted its reach/contact fact or its native-model timing boundary");
        Require(deficits.Length == deficits.Select(deficit => deficit.Target).Distinct().Count() && deficits.Length > 0,
            "persistent dark cells were not uniquely keyed");
        float denominator = deficits[0].CensusAmount;
        Require(deficits.All(deficit => Math.Abs(deficit.CensusAmount - denominator) < .0001f)
            && Math.Abs(denominator - deficits.Sum(deficit => deficit.Deficit)) < .0001f,
            "overlapping torch sites do not share one original brightness-deficit denominator");
        Require(sites.All(site => site.Coverage.Count > 0) && sites.SelectMany(site => site.Coverage.Select(coverage => coverage.DeficitTarget)).Distinct().Count() < sites.Sum(site => site.Coverage.Count),
            "two overlapping native projections did not point at shared deficit keys");
    }

    private static void UnmeasuredIsNotDarkness()
    {
        var context = VerifyCollectionContracts.SetUpFloor();
        VerifyUsefulAssistance.ClearMeasuredLight();
        context.Senses.Update(context.Npc, context.Player);
        SettleReach(context);
        LightingCaptureSlice complete = new CaptureLightingOpportunities().Capture(context.Senses, context, new(double.PositiveInfinity));
        Require(complete.Coverage.Exhausted && complete.Facts.All(f => f.Key.Kind != "lighting-site" && f.Key.Kind != "lighting-deficit"),
            "an unread engine-light area was captured as a dark lighting need");
    }

    private static void ProjectionMatchesNativePlacement()
    {
        var context = VerifyCollectionContracts.SetUpFloor();
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.05f), 1f, placed: null);
        context.Senses.Update(context.Npc, context.Player);
        SettleReach(context);
        var captured = new CaptureLightingOpportunities().Capture(context.Senses, context, new(double.PositiveInfinity));
        LightingOpportunityFact site = captured.Facts.Where(f => f.Key.Kind == "lighting-site")
            .Select(f => JsonSerializer.Deserialize<LightingOpportunityFact>(f.Value.Text)!).First();
        Point tile = new((int)((site.X - 8) / 16), (int)((site.Y - 8) / 16));
        var projected = WorldLight.ProjectTorches(new[] { new WorldLight.PlannedTorch(tile, 0) });
        Point[] probes = { tile, new(tile.X + 1, tile.Y), new(tile.X + 3, tile.Y) };
        float[] before = probes.Select(point => WorldLight.Brightness(point.X, point.Y)!.Value).ToArray();
        float[] expected = probes.Select(point => projected.Brightness(point.X, point.Y)!.Value).ToArray();
        Require(before.All(value => Math.Abs(value - .05f) < .001f), "projection changed the captured world light before placement");
        Require(PlaceTorches.Place(tile, context.Companion.Bag.Items, context.Player, out _), "fixture could not make the captured native site real");
        TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.05f), 1f,
            new[] { (tile, new Vector3(r, g, b)) });
        float[] actual = probes.Select(point => WorldLight.Brightness(point.X, point.Y)!.Value).ToArray();
        Require(expected.Zip(actual, (left, right) => Math.Abs(left - right) < .001f).All(equal => equal),
            $"private preblur projection diverged from native post-placement rescan; expected={string.Join(',', expected)} actual={string.Join(',', actual)}");
    }

    private static void ProjectionSnapshotSurvivesLiveChanges()
    {
        var context = VerifyCollectionContracts.SetUpFloor();
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.05f), 1f, placed: null);
        var snapshot = WorldLight.CaptureProjectionSnapshot();
        var torch = new WorldLight.PlannedTorch(new Point(40, 40), 0);
        float original = snapshot.ProjectTorches(new[] { torch }).Brightness(42, 40)!.Value;
        VerifyTorchPlacementRule.PresentEngineLight((_, _) => new Vector3(.9f), 1f, placed: null);
        float brightness = Lighting.GlobalBrightness;
        try
        {
            Lighting.GlobalBrightness = brightness * .5f;
            var repeated = snapshot.ProjectTorches(new[] { torch });
            Require(Math.Abs(repeated.Brightness(42, 40)!.Value - original) < .001f,
                "a later scan or global brightness changed the frozen light projection");
            Require(!snapshot.ProjectTorches(new[] { new WorldLight.PlannedTorch(new Point(-1, -1), 0) }).Complete,
                "a torch outside captured geometry was silently treated as modelled");
            Require(!snapshot.ProjectTorches(new[] { new WorldLight.PlannedTorch(new Point(40, 40), -1) }).Complete,
                "an unsupported torch style was silently treated as ordinary light");
        }
        finally { Lighting.GlobalBrightness = brightness; }
        var capture = new CaptureLightingOpportunities();
        LightingCaptureSlice cut = capture.Capture(context.Senses, context, new(double.PositiveInfinity, 0));
        Require(!cut.Coverage.Exhausted && cut.Coverage.BudgetCut && cut.Facts.Count == 1,
            "a zero allowance must retain unresolved coverage without starting native light projection");
    }

    private static void SettleReach(live::AICompanion.Companion.Brain.Activities.ActionContext context)
    {
        var brain = context.Companion.Brain;
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, context.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++) brain.Positioner.Resolve(home, brain.Senses);
        Require(brain.Positioner.ReachComplete, "fixture needs a settled native reach sense");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
