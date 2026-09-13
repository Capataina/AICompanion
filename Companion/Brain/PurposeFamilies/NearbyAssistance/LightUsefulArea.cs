#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.WorldInteractions.Torch;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

/// <summary>
/// Offer supplied lighting where darkness has been measured, through native placement and shared interaction
/// access. Being underground or at night was the earlier test, and it spent torches in lit caves and treated a
/// place the lighting engine never computed as dark. The area around the companion must now be mostly measured
/// and dark, carried light left out; a site whose own measured neighbourhood is already lit is not worth a torch.
/// </summary>
public sealed class LightUsefulArea : PerformNearbyWorldWork
{
    public override string Name => "place-torches";
    protected override float Utility => .60f;
    protected override bool AllowJump => true;
    // A placed torch is observed; whether it lit anything useful is not measured by this method.
    protected override string CompletedEffect => "torch-placed-coverage-unmeasured";

    private MeasureLightCoverage.Darkness area;
    private readonly List<(Vector2 Centre, float Radius)> carried = new(2);

    // Elevated lights clear floor clutter and reach across nearby ledges. Candidate validation
    // still requires native attachment, useful spacing, interaction reach and a safe landing.
    protected override float CandidateCost(Vector2 feet, Point tile)
    {
        Vector2 above = feet - new Vector2(0, Player.tileRangeY * 16f + 80f);
        return Math.Min(Vector2.DistanceSquared(above + new Vector2(64, 0), tile.ToWorldCoordinates()),
            Vector2.DistanceSquared(above - new Vector2(64, 0), tile.ToWorldCoordinates()));
    }

    protected override bool Enabled(in ActionContext ctx)
    {
        MeasureArea(ctx);
        return PlayerIntegration.CompanionPreferences.Current.TorchPlacement && MeasuredDark(area)
            && PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory) != null;
    }

    private void MeasureArea(in ActionContext ctx)
    {
        carried.Clear();
        float radius = Weights.CarriedLightRadiusTiles * 16f;
        // A carried torch lights only where it is carried: the companion's own while it is out, and one the
        // player holds. Left in, either makes a dark cave read as lit for exactly as long as it stays there.
        if (ctx.Companion.Torch.Shown) carried.Add((ctx.Npc.Center, radius));
        Item held = ctx.Player.HeldItem;
        if (!ctx.Player.dead && held != null && !held.IsAir && held.createTile > -1
            && held.createTile < TileID.Sets.Torch.Length && TileID.Sets.Torch[held.createTile])
            carried.Add((ctx.Player.Center, radius));
        area = MeasureLightCoverage.Around(ctx.Npc.Center.ToTileCoordinates(), Weights.LightAreaRadiusTiles, Weights.LightAreaStrideTiles, carried);
    }

    private static bool MeasuredDark(MeasureLightCoverage.Darkness darkness)
        => darkness.MeasuredFraction >= Weights.LightMeasuredFractionRequired && darkness.MeanBrightness < Weights.LightDarkBelow;

    protected override (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead")
            : !PlayerIntegration.CompanionPreferences.Current.TorchPlacement ? (OfferEligibility.PolicyForbidden, "torch-placement-disabled")
            : area.MeasuredFraction < Weights.LightMeasuredFractionRequired ? (OfferEligibility.NoOpportunity, "darkness-unmeasured")
            : area.MeanBrightness >= Weights.LightDarkBelow ? (OfferEligibility.NoOpportunity, "area-already-lit")
            : (OfferEligibility.KnownUnusable, "no-torch-supply");

    protected override bool Candidate(in ActionContext ctx, Point tile)
    {
        if (!PlaceSuppliedTorches.Candidate(tile)) return false;
        Item? torch = PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory);
        if (torch == null || !RecommendTorchPlacement.Accepts(tile, torch, ctx.Companion.Body.Player)) return false;
        // Light already measured around the site means another torch there adds little. Carried light is left
        // out, and a neighbourhood with too few measured reads cannot veto a site the area reading found dark.
        var site = MeasureLightCoverage.Around(tile, Weights.LightSiteRadiusTiles, 2, carried);
        return site.Measured < Weights.LightSiteMinimumSamples || site.MeanBrightness < Weights.LightDarkBelow;
    }

    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        bool placed = PlaceSuppliedTorches.Place(tile, ctx.Companion.Bag.Items, ctx.Player, out string source);
        BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, placed ? "place-torch" : "placement-refused", source);
        return placed;
    }
}
