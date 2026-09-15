#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using FindToolAccess = AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>
/// Lights dark places: any tile the player's own smart cursor could put a torch on, inside the work area around the
/// player, whose own light is dark. The game's torch step decides which tiles take a torch, the light sense decides
/// which of those are dark, and the reach sense decides which the body can get to and come home from; the nearest such
/// tile to the companion is the site.
///
/// <para>The rule is the owner's own words for it: if his smart cursor can place a torch there and it is dark, the
/// companion can place one too. It replaced a search that began from the light field's lattice of dark regions and
/// refused any site whose neighbourhood mean was lit, which could not see a dark passage too narrow for the lattice or
/// three tiles of rock away from a lit room, and which never saw the tiles the player's cursor was offering him.</para>
///
/// <para>Region at a time is still the behaviour that matters. Lighting a dark area is not one interaction: a cave wing
/// takes several torches, so this keeps its job after a placement the way mining keeps a vein, re-searching from where
/// it now stands, and works its way through the dark until nothing dark is left in the work area or something outbids
/// it. The game's own spacing between torches is what stops it filling the place.</para>
/// </summary>
public sealed class LightUsefulArea : PerformNearbyWorldWork, ICandidateFunnelSource
{
    public override string Name => "place-torches";
    protected override float Utility => value;
    // A placed torch is observed; whether it lit anything useful is not measured by this method.
    protected override string CompletedEffect => "torch-placed-coverage-unmeasured";

    /// <summary>Lighting keeps its job after a placement instead of ending the attempt, so a region needing
    /// several torches is worked through rather than visited once per search cadence.</summary>
    protected override bool ContinueAfterInteraction => true;

    // Lighting's own candidate stages, in the order a tile meets them.
    private const string StageOutsideWorkArea = "outside-work-area";
    private const string StageOccupied = "occupied-or-protected";
    private const string StageUnread = "light-unread";
    private const string StageLit = "lit";
    private const string StagePlacerRefused = "placer-refused";

    /// <summary>What the last search did with each tile it looked at. Lit and unread tiles are recorded by the gathering
    /// scan, which is where they are refused; the rest by the executor.</summary>
    public CandidateFunnel Funnel { get; } = new(FunnelEntries,
        StageAllowance, StageOccupied, StageUnread, StageLit, StagePlacerRefused,
        StageSearchCut, StageStandNotYetKnown, StageStandBeyondKnownRadius, StageStandUnreachable, CandidateFunnel.Offered);
    private const int FunnelEntries = 6;
    protected override CandidateFunnel? SearchFunnel => Funnel;
    protected override string CandidateStagePassed => "placer-accepts";

    private float value = Weights.LightBaseValue;
    private bool playerCarriesLight;
    private LightSense.DarkRegion? region;
    private string refusal = NoDarkTile;
    // The engine's computed bounds for the search in progress, read once because reading them is reflection.
    private LightSense.Coverage? searchCoverage;

    private const string NoLightMeasured = "no-light-measured-in-range";
    private const string NoDarkTile = "no-dark-tile-in-range";
    private const string NoPlaceableTile = "no-tile-the-placer-accepts";
    private const string ScanCut = "dark-tile-scan-cut-by-planning-deadline";

    protected override bool Enabled(in ActionContext ctx)
    {
        MeasureArea(ctx);
        // No supply check: the companion's torches are its own and never run out (the owner's ruling, 15 September 2026).
        return PlayerIntegration.CompanionPreferences.Current.TorchPlacement;
    }

    private void MeasureArea(in ActionContext ctx)
    {
        // Whether the player has any light of his own. The companion's own torch is not asked about here:
        // the light field already has it subtracted, so a companion holding one cannot read the cave it is
        // standing in as lit. A torch in the player's hand is a reason for this to matter less, not a
        // reason to leave the place dark after he has walked out of it.
        Item held = ctx.Player.HeldItem;
        playerCarriesLight = !ctx.Player.dead && held != null && !held.IsAir && held.createTile > -1
            && held.createTile < TileID.Sets.Torch.Length && TileID.Sets.Torch[held.createTile];
    }

    public override void Prepare(in ActionContext ctx)
    {
        base.Prepare(ctx);
        UpdatePlayerReference(ctx);
        searchCoverage = null;
    }

    /// <summary>The work area, as a tile box: the player's intent region's centre, out to the work radius each way. A
    /// dark place outside it is somebody else's darkness: the companion is not a lamplighter sent out into the world, it
    /// lights where the two of them are. Anchored on the region rather than on his body, because a dark passage a few
    /// tiles ahead of a walking player is the one worth lighting and a box behind him drops it the moment he sets off.</summary>
    private static Rectangle WorkArea(in ActionContext ctx)
    {
        Point centre = MovementQueries.Tile(ctx.Senses.Intent.Region.Centre);
        int work = (int)(Weights.FollowWorkRadius / 16f);
        return new Rectangle(centre.X - work, centre.Y - work, 2 * work + 1, 2 * work + 1);
    }

    /// <summary>
    /// Every tile in the work area the engine has computed light for that could take a torch and is dark. The torch
    /// step's own attachment test is too dear to run on every tile of a screen, so a necessary condition of it filters
    /// here and the step itself runs in the executor, nearest first, on what survives; the light is read here because it
    /// is cheap and a lit tile is refused before it can take a place in the executor's order.
    /// </summary>
    protected override void GatherSearchTiles(in ActionContext ctx, List<(float Cost, int Order, Point Tile)> into)
    {
        var light = ctx.Senses.Light;
        LightSense.Coverage coverage = LightSense.Coverage.Current();
        searchCoverage = coverage;
        Rectangle area = WorkArea(ctx);
        // The engine computes light for the screen and answers nothing outside it, so the scan is clipped to what it
        // computed rather than paying for tiles that can only read unread.
        if (!coverage.Legacy)
            area = Rectangle.Intersect(area, coverage.Area);
        Point feet = MovementQueries.Tile(ctx.Npc.Center);
        Vector2 fromFeet = ctx.Npc.Center;
        NominateRegion(ctx, feet);
        int placeable = 0, lit = 0, unread = 0;
        bool cut = false;
        for (int x = area.Left; x < area.Right && !cut; x++)
        {
            // The scan reads the deadline the same way every route and reach query nested under it does, and for the
            // same reason: a screen of candidate tiles is not a search that answers in bounded time, and a share decides
            // whether the next child starts and cannot interrupt one already running.
            if (LimitPlanningWork.Expired) { cut = true; break; }
            for (int y = area.Top; y < area.Bottom; y++)
            {
                if (!WorldGen.InWorld(x, y, 10)) continue;
                Tile tile = Main.tile[x, y];
                if (tile.HasTile || tile.LiquidAmount > 0) continue;
                Point p = new(x, y);
                if (!RecommendTorchPlacement.MayAccept(p) || !PlaceTorches.Candidate(p) || SearchTileDeferred(p)) continue;
                placeable++;
                var reading = light.ReadForPlacement(p, coverage);
                float cost = CandidateCost(fromFeet, p);
                if (!reading.IsDark)
                {
                    if (reading.Light == LightSense.PlacementLight.Unread) unread++; else lit++;
                    Funnel.Add("tile", p, cost, StageOccupied,
                        reading.Light == LightSense.PlacementLight.Unread ? StageUnread : StageLit, Readings(reading));
                    continue;
                }
                into.Add((cost, into.Count, p));
            }
        }
        refusal = into.Count > 0 ? NoPlaceableTile
            : cut ? ScanCut
            : placeable == 0 && coverage.Area.Width <= 0 ? NoLightMeasured
            : placeable > 0 && lit == 0 && unread > 0 ? NoLightMeasured
            : placeable == 0 ? NoPlaceableTile
            : NoDarkTile;
    }

    /// <summary>The value this work is worth, from the nearest dark region of the light field inside the work area: a
    /// larger dark area is worth more, up to a cap, and a player with no light of his own makes it worth more again. The
    /// region is also what the telemetry names; with no region the base value stands, because a dark tile the lattice
    /// is too coarse to see is still worth lighting.</summary>
    private void NominateRegion(in ActionContext ctx, Point feet)
    {
        region = null;
        float player = playerCarriesLight ? 1f : Weights.LightPlayerUnlitFactor;
        value = Weights.LightBaseValue * player;
        Rectangle area = WorkArea(ctx);
        foreach (LightSense.DarkRegion found in ctx.Senses.Light.DarkRegionsNearest(feet, Weights.LightRegionSearchTiles))
        {
            if (!area.Contains(found.Centre)) continue;
            region = found;
            value = Math.Min(Weights.LightBaseValue + found.DarkSamples * Weights.LightRegionSampleValue,
                    Weights.LightBaseValue + Weights.LightRegionValueCap) * player;
            return;
        }
    }

    protected override (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead")
            : (OfferEligibility.PolicyForbidden, "torch-placement-disabled");

    /// <summary>
    /// Why the gathering found nothing to ask about. Nothing measured and a scan the deadline cut are unanswered
    /// searches; no dark tile and no tile the placer accepts are absences. Every exit about a site's stand belongs to
    /// the executor and carries its shared name.
    /// </summary>
    protected override (OfferEligibility Eligibility, string Reason)? SearchRefusal(in ActionContext ctx)
        => refusal switch
        {
            NoLightMeasured => (OfferEligibility.Unresolved, refusal),
            ScanCut => (OfferEligibility.Unresolved, refusal),
            _ => (OfferEligibility.NoOpportunity, refusal),
        };

    protected override bool Candidate(in ActionContext ctx, Point tile) => RefusingStage(ctx, tile) == null;

    /// <summary>
    /// The stage that refuses a tile, in the order the search meets them: a tile the companion may place into, light the
    /// engine computed, darkness there by the tile's own light, and the game's own torch step. There is no supply stage,
    /// because the companion's torches never run out. Unread is refused with lit: a place nobody has read is not a proven
    /// dark place, and a torch put there is put on a guess.
    /// </summary>
    protected override string? RefusingStage(in ActionContext ctx, Point tile)
    {
        Item torch = PlaceTorches.TorchToPlace(ctx.Companion.Bag.Items, ctx.Player.inventory);
        if (!PlaceTorches.Candidate(tile)) return StageOccupied;
        var reading = ctx.Senses.Light.ReadForPlacement(tile, searchCoverage ?? LightSense.Coverage.Current());
        if (reading.Light == LightSense.PlacementLight.Unread) return StageUnread;
        if (!reading.IsDark) return StageLit;
        return RecommendTorchPlacement.Accepts(tile, torch, ctx.Companion.StandIn.Player) ? null : StagePlacerRefused;
    }

    protected override string CandidateReadings(in ActionContext ctx, Point tile)
        => Readings(ctx.Senses.Light.ReadForPlacement(tile, searchCoverage ?? LightSense.Coverage.Current()));

    private static string Readings(LightSense.PlacementReading reading) => reading.Light switch
    {
        LightSense.PlacementLight.Unread => "light=unread",
        LightSense.PlacementLight.Carried => FormattableString.Invariant($"light=carried:{reading.Brightness:0.000}"),
        _ => FormattableString.Invariant($"light={reading.Brightness:0.000}"),
    };

    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        bool placed = PlaceTorches.Place(tile, ctx.Companion.Bag.Items, ctx.Player, out string source);
        Infrastructure.Diagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, placed ? "place-torch" : "placement-refused", PerformNote + source);
        return placed;
    }

    /// <summary>The region this activity is working, for the telemetry; absent when nothing is nominated.</summary>
    public LightSense.DarkRegion? NominatedRegion => region;

    // ---- the player's own cursor, as the reference this activity is judged against -------------------------------

    /// <summary>How often the reference is recomputed: the positioner's rescore interval, because the reference is read
    /// against the search, which re-runs on about that cadence, and a per-tick reference would cost the torch step's scan
    /// of his whole reach box every tick for an answer that moves when he does.</summary>
    private const int ReferenceTicks = 12;
    private ulong nextReference;

    /// <summary>The tile the player's own smart cursor would offer for the torch the companion would place, with the
    /// cursor at his centre; null when his reach holds none.</summary>
    public Point? PlayerReferenceTile { get; private set; }

    /// <summary>What that tile's own light reads to the placement question; null with no tile.</summary>
    public LightSense.PlacementReading? PlayerReferenceReading { get; private set; }

    /// <summary>The stage at which this activity refuses that tile, <see cref="CandidateFunnel.Offered"/> when it is the
    /// site, or the reason there is no tile.</summary>
    public string PlayerReferenceStage { get; private set; } = "-";

    private void UpdatePlayerReference(in ActionContext ctx)
    {
        if (Main.GameUpdateCount < nextReference && Main.GameUpdateCount + ReferenceTicks >= nextReference) return;
        nextReference = Main.GameUpdateCount + ReferenceTicks;
        PlayerReferenceTile = null;
        PlayerReferenceReading = null;
        Item torch = PlaceTorches.TorchToPlace(ctx.Companion.Bag.Items, ctx.Player.inventory);
        if (ctx.Player.dead) { PlayerReferenceStage = "player-dead"; return; }
        if (RecommendTorchPlacement.PlayerCursorTorch(ctx.Player, torch) is not Point tile)
        {
            PlayerReferenceStage = "cursor-offers-nothing";
            return;
        }
        PlayerReferenceTile = tile;
        PlayerReferenceReading = ctx.Senses.Light.ReadForPlacement(tile, LightSense.Coverage.Current());
        PlayerReferenceStage = ExplainTile(ctx, tile);
    }

    /// <summary>
    /// The stage at which this activity refuses one tile, asked the same way the search asks it and changing nothing:
    /// the work area, the activity allowance, a refusal remembered from an earlier search, the candidate stages and the
    /// reach sense's answer for its stand. A tile that passes every stage without being the site says that too, because
    /// then the answer is the search's order or its cadence rather than a rule.
    /// </summary>
    public string ExplainTile(in ActionContext ctx, Point tile)
    {
        if (target == tile) return CandidateFunnel.Offered;
        if (!WorkArea(ctx).Contains(tile)) return StageOutsideWorkArea;
        if (!AllowsTarget(ctx, tile.ToWorldCoordinates(), tile)) return StageAllowance;
        if (DeferredStage(tile) is string deferred) return deferred;
        if (RefusingStage(ctx, tile) is string refused) return refused;
        if (FindToolAccess.InReach(ctx.Npc.Center, tile)) return StagePassedEveryStage;
        return FindToolAccess.Approach(tile, ctx.Npc.Center, ctx.Senses.Reach, out _) switch
        {
            Reachability.Reach.Yes => StagePassedEveryStage,
            Reachability.Reach.No => StageStandUnreachable,
            _ => ctx.Senses.Reach.Complete ? StageStandBeyondKnownRadius : StageStandNotYetKnown,
        };
    }
}
