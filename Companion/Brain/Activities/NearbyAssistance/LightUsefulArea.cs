#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>
/// Offer supplied lighting in measured dark air, through native placement and shared interaction access.
/// Darkness is open air the engine has computed, never packed tiles. The search is the visible screen
/// unioned with a neighbourhood around the companion, nearest proven site to the feet. A locally lit
/// bubble does not hide a dark pocket further on the screen.
/// </summary>
public sealed class LightUsefulArea : PerformNearbyWorldWork
{
    public override string Name => "place-torches";
    protected override float Utility => .60f;
    protected override bool AllowJump => true;
    // A placed torch is observed; whether it lit anything useful is not measured by this method.
    protected override string CompletedEffect => "torch-placed-coverage-unmeasured";

    private readonly List<(Vector2 Centre, float Radius)> carried = new(2);

    protected override bool Enabled(in ActionContext ctx)
    {
        MeasureArea(ctx);
        return PlayerIntegration.CompanionPreferences.Current.TorchPlacement
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
    }

    protected override void GatherSearchTiles(in ActionContext ctx, List<(float Cost, int Order, Point Tile)> into)
    {
        Point c = ctx.Npc.Center.ToTileCoordinates();
        int left = c.X - 48, right = c.X + 48, top = c.Y - 28, bottom = c.Y + 28;
        if (Main.screenWidth >= 64)
        {
            int sl = (int)(Main.screenPosition.X / 16f);
            int st = (int)(Main.screenPosition.Y / 16f);
            left = Math.Min(left, sl);
            top = Math.Min(top, st);
            right = Math.Max(right, sl + Main.screenWidth / 16);
            bottom = Math.Max(bottom, st + Main.screenHeight / 16);
        }
        int work = (int)(Weights.FollowWorkRadius / 16f);
        Point player = ctx.Player.Center.ToTileCoordinates();
        left = Math.Max(left, player.X - work);
        right = Math.Min(right, player.X + work);
        top = Math.Max(top, player.Y - work);
        bottom = Math.Min(bottom, player.Y + work);
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                Point p = new(x, y);
                if (SearchTileDeferred(p) || !PlaceSuppliedTorches.Candidate(p)) continue;
                into.Add((CandidateCost(ctx.Npc.Bottom, p), into.Count, p));
            }
    }

    protected override (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead")
            : !PlayerIntegration.CompanionPreferences.Current.TorchPlacement ? (OfferEligibility.PolicyForbidden, "torch-placement-disabled")
            : (OfferEligibility.KnownUnusable, "no-torch-supply");

    protected override bool Candidate(in ActionContext ctx, Point tile)
    {
        if (!PlaceSuppliedTorches.Candidate(tile)) return false;
        Item? torch = PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory);
        if (torch == null) return false;
        // Darkness first: Smart Cursor is the expensive half, and a lit bubble around the feet
        // is most of a cave screen. Packed tiles are not darkness, and a locally bright disc
        // does not hide a dark pocket elsewhere.
        var site = MeasureLightCoverage.Around(tile, Weights.LightSiteRadiusTiles, 2, carried);
        if (site.Measured == 0 || site.MeanBrightness >= Weights.LightDarkBelow) return false;
        return RecommendTorchPlacement.Accepts(tile, torch, ctx.Companion.Body.Player);
    }

    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        bool placed = PlaceSuppliedTorches.Place(tile, ctx.Companion.Bag.Items, ctx.Player, out string source);
        Infrastructure.Diagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, placed ? "place-torch" : "placement-refused", PerformNote + source);
        return placed;
    }
}
