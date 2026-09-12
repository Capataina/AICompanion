#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.WorldInteractions.Torch;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

/// <summary>Offer useful supplied lighting through native placement and shared interaction access.</summary>
public sealed class LightUsefulArea : PerformNearbyWorldWork
{
    public override string Name => "place-torches";
    protected override float Utility => .60f;
    protected override bool AllowJump => true;
    // A placed torch is observed; whether it lit anything useful is not measured by this method.
    protected override string CompletedEffect => "torch-placed-coverage-unmeasured";
    // Elevated lights clear floor clutter and reach across nearby ledges. Candidate validation
    // still requires native attachment, useful spacing, interaction reach and a safe landing.
    protected override float CandidateCost(Vector2 feet, Point tile)
    {
        Vector2 above = feet - new Vector2(0, Player.tileRangeY * 16f + 80f);
        return Math.Min(Vector2.DistanceSquared(above + new Vector2(64, 0), tile.ToWorldCoordinates()),
            Vector2.DistanceSquared(above - new Vector2(64, 0), tile.ToWorldCoordinates()));
    }
    protected override bool Enabled(in ActionContext ctx) => PlayerIntegration.CompanionPreferences.Current.TorchPlacement
        && DarkContext(ctx) && PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory) != null;
    private static bool DarkContext(in ActionContext ctx) => ctx.Npc.Center.Y / 16f > Main.worldSurface || !Main.dayTime;
    protected override (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead")
            : !PlayerIntegration.CompanionPreferences.Current.TorchPlacement ? (OfferEligibility.PolicyForbidden, "torch-placement-disabled")
            : !DarkContext(ctx) ? (OfferEligibility.NoOpportunity, "surface-in-daylight")
            : (OfferEligibility.KnownUnusable, "no-torch-supply");
    protected override bool Candidate(in ActionContext ctx, Point tile)
    {
        if (!PlaceSuppliedTorches.Candidate(tile)) return false;
        Item? torch = PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory);
        return torch != null && RecommendTorchPlacement.Accepts(tile, torch, ctx.Companion.Body.Player);
    }
    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        bool placed = PlaceSuppliedTorches.Place(tile, ctx.Companion.Bag.Items, ctx.Player, out string source);
        BehaviourDiagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, placed ? "place-torch" : "placement-refused", source);
        return placed;
    }
}
