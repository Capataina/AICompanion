#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Interactions;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The inspector's Execution evidence, built only from state the brain has already retained: the chooser's last family
/// nominations and scored offers, the positioner's admitted success region, the last control grant, and the activity
/// owner's open attempt and latest conclusion. It calls no planner, positioner, aimer or reach test, so reading it can
/// neither change a decision nor pay for a solve; and it is plain data, so the offscreen renderer checks it with no game.
/// </summary>
public static class DescribeExecutionEvidence
{
    /// <summary>One line of evidence; a heading names the question the lines under it answer.</summary>
    public readonly record struct Line(string Text, bool Heading = false);

    /// <summary>A region's box in world pixels, in feet space: feet inside it satisfy the region's box test.</summary>
    public readonly record struct RegionBox(Vector2 Min, Vector2 Max);

    public static IReadOnlyList<Line> Of(Brain brain)
    {
        var lines = new List<Line>();
        var chooser = brain.Chooser;

        lines.Add(new("Offers by family", Heading: true));
        if (chooser.LastScores.Count == 0) lines.Add(new("no comparison has run yet"));
        foreach (var family in chooser.LastScores.GroupBy(score => score.Action.Family))
        {
            var nomination = chooser.LastNominations.FirstOrDefault(n => n.Family == family.Key);
            lines.Add(new(nomination.Activity is { } nominee
                ? $"{family.Key}: nominated {nominee.Name} at {F(nominee.Final)}"
                : $"{family.Key}: nominated nothing"));
            foreach (var offer in family)
                lines.Add(new($"  {offer.Action.Name} {offer.Eligibility}:{(offer.EligibilityReason.Length > 0 ? offer.EligibilityReason : "-")} raw {F(offer.Raw)} final {F(offer.Final)}"
                    + (ReferenceEquals(offer.Action, brain.LastAction) ? "  selected" : "")));
        }

        lines.Add(new("Where the purpose succeeds", Heading: true));
        SuccessRegion region = brain.Positioner.Region;
        lines.Add(new(region.Kind switch
        {
            SuccessRegionKind.None => "no destination is held",
            SuccessRegionKind.FollowComfort => $"follow comfort {F(region.Comfort.X)} by {F(region.Comfort.Y)} around player {P(region.PlayerFeet)} or anchor {P(region.Anchor)}",
            SuccessRegionKind.ToolReach => $"tool reach {region.ReachX}x{region.ReachY} tiles to tile {region.WorkTile?.X},{region.WorkTile?.Y} from stand {P(region.Anchor)}",
            SuccessRegionKind.FiringPosition => $"firing position near {P(region.Anchor)}; its arc belongs to a moving target, so it declares no box",
            SuccessRegionKind.MeetingPlace => $"meeting place {P(region.Anchor)}, walked to without a box",
            _ => $"destination {P(region.Anchor)} declares no purpose geometry",
        }));
        if (region.Kind != SuccessRegionKind.None)
            lines.Add(new($"  destination revision {brain.Positioner.ChosenRevision}, admitted at tick {region.AdmittedTick} under terrain revision {region.TerrainRevision}"));

        lines.Add(new("Control requested and granted", Heading: true));
        if (brain.ControlGrants.Last is { } grant)
        {
            lines.Add(new($"grant {grant.Id} at tick {grant.Tick}: requested {grant.RequestedOwner}, applied {grant.AppliedOwner}"
                + (grant.RequestedOwner == grant.AppliedOwner ? "" : "  (differs)")));
            lines.Add(new($"  hand {grant.Hand}, activity {grant.ActivityId} {grant.ActivityPhase}, attempt {grant.AttemptId}"));
            // The recorder's own compact form, so the panel, the rows and the grant occurrences spell one control the same way.
            lines.Add(new($"  requested {BrainTelemetry.DescribeControls(grant.RequestedMovement)}"));
            lines.Add(new($"  applied {BrainTelemetry.DescribeControls(grant.AppliedMovement)}"));
        }
        else lines.Add(new("no control has been granted yet"));

        lines.Add(new("Effects and how the attempt ended", Heading: true));
        var owner = chooser.Activity;
        lines.Add(new(owner.AttemptOpen
            ? $"attempt {owner.AttemptId} open under {owner.Current?.Name ?? "-"} ({owner.Phase}), {owner.AttemptEffects} productive effect(s) so far"
            : $"no attempt open; {owner.Current?.Name ?? "no activity"} is {owner.Phase}"));
        if (owner.LastAttempt is { } last)
        {
            lines.Add(new($"last attempt {last.AttemptId} {last.Activity}: {last.Status}"
                + (last.Attribution == Activities.AttemptAttribution.NotApplicable ? "" : ":" + last.Attribution)
                + $" by {last.Cause}, ticks {last.StartTick}..{last.EndTick}"));
            lines.Add(new($"  {last.ProductiveEffects} productive effect(s); "
                + (last.ClaimedYieldQuantity > 0 ? $"claimed {last.ClaimedYieldQuantity} of item {last.ClaimedYieldType}" : "no yield claimed")));
        }
        else lines.Add(new("no attempt has concluded yet"));
        return lines;
    }

    /// <summary>
    /// The boxes a region declares, in feet space. Follow admits a destination near either of two references, so it has two
    /// comfort boxes; a tool stand has the native reach box, which <see cref="FindToolAccess"/> tests at the eye above the feet
    /// against the tile centre, so in feet space the same box sits that eye height lower. Kinds with no geometry have none.
    /// </summary>
    public static IReadOnlyList<RegionBox> RegionBoxes(in SuccessRegion region)
    {
        if (region.Kind == SuccessRegionKind.FollowComfort)
        {
            Vector2 player = region.Comfort;
            Vector2 ahead = region.ReachX > 0 && region.ReachY > 0
                ? new Vector2(region.ReachX, region.ReachY)
                : player;
            return new[] { Around(region.PlayerFeet, player), Around(region.Anchor, ahead) };
        }
        if (region.Kind == SuccessRegionKind.ToolReach && region.WorkTile is Point tile)
        {
            Vector2 centre = tile.ToWorldCoordinates(8f, 8f) + new Vector2(0f, FindToolAccess.EyeHeight);
            return new[] { Around(centre, new Vector2(region.ReachX * 16f + 8f, region.ReachY * 16f + 8f)) };
        }
        return Array.Empty<RegionBox>();
    }

    private static RegionBox Around(Vector2 centre, Vector2 half) => new(centre - half, centre + half);
    private static string F(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string P(Vector2 value) => $"{F(value.X)},{F(value.Y)}";
}
