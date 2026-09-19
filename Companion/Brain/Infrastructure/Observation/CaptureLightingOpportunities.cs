#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>A native torch site with coverage modelled by the engine's blur over frozen pre-blur input.</summary>
public sealed record LightingOpportunityFact(string Target, double X, double Y, double ContactX, double ContactY,
    string Reach, string Admission, string Reason, float Brightness, string Light, string CoverageEvidence,
    IReadOnlyList<LightingCoverageFact> Coverage, string Policy, double? NativeUseTicks, double? EffectTicks, string TimingEvidence);

public sealed record LightingDeficitFact(string Target, float Deficit, float CensusAmount, float Brightness);
public sealed record LightingCoverageFact(string DeficitTarget, float Reduction);
public readonly record struct LightingCaptureCoverage(long Examined, long Total, bool Exhausted, bool BudgetCut, Rectangle Bounds);
public sealed record LightingCaptureSlice(IReadOnlyList<DecisionFact> Facts, LightingCaptureCoverage Coverage);

/// <summary>Captures every actual native torch site before an activity picks a winner.  It owns one
/// resumable scan of the current light-coverage/work-area intersection; a cut retains its cursor and
/// frozen observations.  Replacing the scan happens only after the finite spatial input completed, so
/// an unrelated world edit cannot discard the in-flight census.</summary>
public sealed class CaptureLightingOpportunities
{
    private readonly DecisionWorkCursor cursor = new();
    private readonly List<(LightingOpportunityFact Site, short? Style)> sites = new();
    private readonly List<LightingOpportunityFact> projectedSites = new();
    private readonly List<LightingDeficitFact> deficits = new();
    private WorldLight.ProjectionSnapshot? lightSnapshot;
    private WorldLight.ProjectedLight? baseline;
    private int projectedCount;
    private Item? capturedTorch;
    private bool capturedPolicy;
    private Rectangle area;
    private Point[] points = Array.Empty<Point>();
    private long epoch;
    private long factVersion;
    private readonly Dictionary<FactKey, (string Text, long Version)> versions = new();

    public LightingCaptureSlice Capture(Senses senses, ActionContext context, DecisionWorkBudget budget)
    {
        if (cursor.Exhausted || points.Length == 0)
        {
            if (!budget.TrySpend("capture-lighting-snapshot"))
            {
                var pending = new LightingCaptureCoverage(0, 0, false, true, Rectangle.Empty);
                return new(new[] { Fact("lighting-coverage", "native-census", pending, FactEvidence.Unresolved) }, pending);
            }
            Begin(senses, context);
        }
        Item torch = capturedTorch!;
        while (cursor.Offset < points.Length && budget.TrySpend("capture-lighting"))
        {
            Point point = points[(int)cursor.Offset];
            CapturePoint(senses, context, torch, point);
            cursor.Advance();
        }
        while (cursor.Offset == points.Length && projectedCount < sites.Count && budget.TrySpend("project-lighting"))
        {
            var candidate = sites[projectedCount];
            Point point = Parse(candidate.Site.Target);
            WorldLight.ProjectedLight projection = candidate.Style is short style
                ? lightSnapshot!.ProjectTorches(new[] { new WorldLight.PlannedTorch(point, style) })
                : WorldLight.ProjectedLight.Unavailable;
            var covered = deficits.Select(deficit => new LightingCoverageFact(deficit.Target,
                Math.Min(deficit.Deficit, Math.Max(0f, (projection.Brightness(Parse(deficit.Target).X, Parse(deficit.Target).Y) ?? deficit.Brightness) - deficit.Brightness))))
                .Where(coverage => coverage.Reduction > 0).ToArray();
            projectedSites.Add(candidate.Site with
            {
                Coverage = covered,
                CoverageEvidence = projection.Complete ? "modelled-captured-preblur-lightmap" : "unresolved-torch-emission-or-coverage",
            });
            projectedCount++;
        }
        if (cursor.Offset == points.Length && projectedCount == sites.Count) cursor.Complete();
        var capture = new List<DecisionFact>();
        if (cursor.Exhausted)
        {
            float denominator = deficits.Sum(deficit => deficit.Deficit);
            foreach (LightingDeficitFact deficit in deficits)
                capture.Add(Fact("lighting-deficit", deficit.Target, deficit with { CensusAmount = denominator }, FactEvidence.Observed));
            foreach (LightingOpportunityFact site in projectedSites)
            {
                capture.Add(Fact("lighting-site", site.Target, site,
                    site.CoverageEvidence == "modelled-captured-preblur-lightmap" ? FactEvidence.Modelled : FactEvidence.Unresolved));
            }
        }
        LightingCaptureCoverage status = new(cursor.Offset + projectedCount, points.Length + sites.Count, cursor.Exhausted,
            budget.Cut && !cursor.Exhausted, area);
        capture.Add(Fact("lighting-coverage", "native-census", status, cursor.Exhausted ? FactEvidence.Observed : FactEvidence.Unresolved));
        return new(capture.OrderBy(fact => fact.Key).ToArray(), status);
    }

    public void Reset()
    {
        points = Array.Empty<Point>(); sites.Clear(); projectedSites.Clear(); deficits.Clear(); versions.Clear();
        lightSnapshot = null; baseline = null; projectedCount = 0;
        capturedTorch = null;
        cursor.Bind(++epoch, "reset"); cursor.Complete(); factVersion = 0;
    }

    private void Begin(Senses senses, ActionContext context)
    {
        int radius = (int)(Weights.FollowWorkRadius / 16f);
        Point heading = senses.Intent.Region.Heading.ToTileCoordinates();
        area = new Rectangle(heading.X - radius, heading.Y - radius, radius * 2 + 1, radius * 2 + 1);
        LightSense.Coverage coverage = LightSense.Coverage.Current();
        if (!coverage.Legacy) area = Rectangle.Intersect(area, coverage.Area);
        lightSnapshot = WorldLight.CaptureProjectionSnapshot();
        baseline = lightSnapshot.ProjectTorches(Array.Empty<WorldLight.PlannedTorch>());
        capturedTorch = PlaceTorches.TorchToPlace(context.Companion.Bag.Items, context.Player.inventory).Clone();
        capturedPolicy = PlayerIntegration.CompanionPreferences.Current.TorchPlacement;
        points = Enumerable.Range(area.Left, Math.Max(0, area.Width))
            .SelectMany(x => Enumerable.Range(area.Top, Math.Max(0, area.Height)).Select(y => new Point(x, y)))
            .Where(point => WorldGen.InWorld(point.X, point.Y, 10)).ToArray();
        sites.Clear(); projectedSites.Clear(); deficits.Clear(); projectedCount = 0;
        cursor.Bind(++epoch, "spatial-light-coverage-or-intent-area");
    }

    private void CapturePoint(Senses senses, ActionContext context, Item torch, Point point)
    {
        float? brightness = baseline!.Brightness(point.X, point.Y);
        LightSense.PlacementReading reading = senses.Light.ReadForPlacement(point, brightness);
        if (reading.IsDark)
        {
            float deficit = Weights.LightDarkBelow - reading.Brightness;
            deficits.Add(new($"cell:{point.X},{point.Y}", Math.Max(0f, deficit), 0f, reading.Brightness));
        }
        bool policy = capturedPolicy;
        bool candidate = PlaceTorches.Candidate(point);
        bool filter = candidate && RecommendTorchPlacement.MayAccept(point);
        bool accepts = filter && reading.IsDark && RecommendTorchPlacement.Accepts(point, torch, context.Companion.StandIn.Player);
        ReachVerdict reach = senses.Reach.Reachable(point);
        string admission = !policy || !candidate || !filter || !accepts ? "unusable"
            : reading.Light == LightSense.PlacementLight.Unread || reach == ReachVerdict.NotYet ? "unknown"
            : reach == ReachVerdict.Unreachable ? "unusable" : "usable";
        string reason = !policy ? "torch-placement-disabled" : !candidate ? "placement-policy-or-contact"
            : !filter ? "attachment-impossible" : !reading.IsDark ? reading.Light == LightSense.PlacementLight.Unread ? "light-unread" : "not-persistently-dark"
            : !accepts ? "native-smart-cursor-refused" : reach == ReachVerdict.NotYet ? "reach-not-yet"
            : reach == ReachVerdict.Unreachable ? "reach-unreachable" : "observed-native-site";
        if (!accepts) return;
        Vector2 contact = MovementQueries.HoverPoint(point);
        bool knownEmission = torch.createTile == Terraria.ID.TileID.Torches && torch.placeStyle >= 0 && torch.placeStyle < Terraria.ID.TorchID.Count;
        sites.Add((new($"tile:{point.X},{point.Y}", point.X * 16 + 8, point.Y * 16 + 8, contact.X, contact.Y,
            reach.ToString(), admission, reason, reading.Brightness, reading.Light.ToString(), "projection-pending",
            Array.Empty<LightingCoverageFact>(), "PlaceTorches.Candidate+RecommendTorchPlacement.Accepts", 1, null,
            "native-placement-boundary=1;effect-arrival=unresolved"), knownEmission ? (short)torch.placeStyle : null));
    }

    private static Point Parse(string target)
    {
        string[] coordinates = target[5..].Split(',');
        return new(int.Parse(coordinates[0]), int.Parse(coordinates[1]));
    }

    private DecisionFact Fact(string kind, string identity, object value, FactEvidence evidence)
    {
        var key = new FactKey(kind, identity);
        string text = JsonSerializer.Serialize(value);
        long version = versions.TryGetValue(key, out var prior) && prior.Text == text ? prior.Version : ++factVersion;
        versions[key] = (text, version);
        return new(key, version, new(Text: text), evidence);
    }
}
