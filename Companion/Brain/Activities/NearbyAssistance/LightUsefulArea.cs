#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>
/// Lights dark places by walking to them, one region at a time. The light field nominates the nearest
/// region of dark air to the companion's own feet — not the player's, and not a disc around the body —
/// the reach sense says whether the body can get there and come home, and the game's own Smart Cursor
/// placer chooses which tile in that region actually takes a torch.
///
/// <para>Region at a time is the behaviour that matters. Lighting a dark area is not one interaction: a
/// cave wing takes several torches, and the earlier search picked the nearest dark site on the whole
/// screen, placed one torch and then went back to whatever else was on offer, so the companion trickled
/// torches into a cave a visit at a time. This keeps its job after a placement the way mining keeps a
/// vein, re-nominating from where it now stands, so it works its way through the dark and stops when
/// there is no dark left in range or something outbids it.</para>
/// </summary>
public sealed class LightUsefulArea : PerformNearbyWorldWork
{
    public override string Name => "place-torches";
    protected override float Utility => value;
    protected override bool AllowJump => true;
    // A placed torch is observed; whether it lit anything useful is not measured by this method.
    protected override string CompletedEffect => "torch-placed-coverage-unmeasured";

    /// <summary>Lighting keeps its job after a placement instead of ending the attempt, so a region needing
    /// several torches is worked through rather than visited once per search cadence.</summary>
    protected override bool ContinueAfterInteraction => true;

    private float value = Weights.LightBaseValue;
    private bool playerCarriesLight;
    private LightSense.DarkRegion? region;
    private string refusal = "no-dark-region-in-range";

    protected override bool Enabled(in ActionContext ctx)
    {
        MeasureArea(ctx);
        return PlayerIntegration.CompanionPreferences.Current.TorchPlacement
            && PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory) != null;
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

    /// <summary>
    /// The tiles worth asking the placer about: those in and around the nominated dark region, nearest the
    /// region's centre first. The search window used to be the whole visible screen scanned tile by tile,
    /// with the darkness test run per candidate; the field has already done that work, so this only has to
    /// turn one region into placement candidates.
    /// </summary>
    protected override void GatherSearchTiles(in ActionContext ctx, List<(float Cost, int Order, Point Tile)> into)
    {
        Point feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        var regions = ctx.Senses.Light.DarkRegionsNearest(feet, Weights.LightRegionSearchTiles);
        region = null;
        value = Weights.LightBaseValue;
        if (regions.Count == 0)
        {
            refusal = ctx.Senses.Light.MeasuredSamples == 0 ? "no-light-measured-in-range" : "no-dark-region-in-range";
            return;
        }
        // The work radius is measured from the player's intent region, not from his body. A dark
        // stretch of passage a few tiles ahead of a walking player sits at the far edge of a circle
        // centred behind him and drops out of range the moment he sets off towards it, which is the
        // one moment lighting it is worth anything; anchored on the region the radius leads him, so
        // the passage he is walking into is in range before he gets there. The nearest-first
        // ordering below still starts from the companion's own feet, because that is about which
        // darkness it can walk to rather than which darkness is worth lighting.
        Point player = MovementQueries.FeetTile(ctx.Senses.Intent.Region.Centre);
        int work = (int)(Weights.FollowWorkRadius / 16f);
        Vector2 fromFeet = ctx.Npc.Bottom;
        int span = Weights.LightPlacementSearchTiles;
        var offered = new HashSet<Point>();
        var nearest = new List<(float Cost, int Order, Point Tile)>();
        refusal = "dark-region-outside-work-radius";
        // Every dark region in range contributes its tiles, and the executor takes the first whose access it
        // can prove, scanning in cost order from the feet. Offering only the nearest region would let one
        // unreachable pocket hide every reachable one behind it — dark air under a floor is nearer than a
        // shelf across the room and sealed away from both of them — and "nearest dark region" is about where
        // the companion walks, not about which darkness it is allowed to know exists.
        bool cut = false;
        foreach (LightSense.DarkRegion found in regions)
        {
            // The scan reads the deadline the same way every route and reach query nested under it does, and
            // for the same reason: it is not a search that answers in bounded time. A screen dark everywhere
            // nominates one region holding most of the lattice, and the site test is a neighbourhood of engine
            // reads per candidate tile, so a whole-dark floor measured 37.6 ms of preparation in one tick
            // against a twelve-millisecond tick — the family's preparation share cannot hold that, because a
            // share decides whether the next child starts and cannot interrupt one already running. Cutting
            // here reports the refusal as Unresolved rather than as no opportunity, which is the difference
            // between an answer that has not arrived and an answer of "no", and it retries in a rescore.
            if (LimitPlanningWork.Expired) { cut = true; break; }
            // A region outside the work radius of the player is somebody else's darkness: the companion is
            // not a lamplighter sent out into the world, it lights where the two of them are.
            if (Math.Abs(found.Centre.X - player.X) > work || Math.Abs(found.Centre.Y - player.Y) > work)
                continue;
            // Every tile in and around the region. Around every member rather than around one representative
            // point: a torch needs something to attach to, and the wall a large dark region touches can be
            // at its far end. The span bridges the gaps the lattice leaves between its own samples, so the
            // scan sees whole tiles rather than only the sampled ones.
            foreach (Point member in found.Tiles)
            {
                // And inside the region too, because one region can hold most of the lattice and a probe
                // only between regions would never fire on the scene that costs the most.
                if (LimitPlanningWork.Expired) { cut = true; break; }
                for (int x = member.X - span; x <= member.X + span; x++)
                    for (int y = member.Y - span; y <= member.Y + span; y++)
                    {
                        Point p = new(x, y);
                        if (!offered.Add(p) || SearchTileDeferred(p) || !PlaceSuppliedTorches.Candidate(p)) continue;
                        // The darkness veto runs here rather than only in the executor's own candidate
                        // check, because the cap below keeps the nearest sites and a lit site that survives
                        // to be counted takes a place from a dark one further out. Inside a lit bubble every
                        // one of the nearest tiles is lit, so a cap applied before this test keeps a full
                        // list of sites that will all be refused and reports no opportunity.
                        if (!SiteIsDark(ctx, p)) continue;
                        nearest.Add((CandidateCost(fromFeet, p), nearest.Count, p));
                    }
            }
            if (region != null) continue;
            // The nearest region that contributed anywhere to put a torch is the one being worked, which is
            // what the value and the telemetry describe.
            region = found;
            value = Math.Min(Weights.LightBaseValue + found.DarkSamples * Weights.LightRegionSampleValue,
                    Weights.LightBaseValue + Weights.LightRegionValueCap)
                * (playerCarriesLight ? 1f : Weights.LightPlayerUnlitFactor);
        }
        // Every surviving site is handed on, ordered by the executor's own nearest-first scan. Capping the
        // list to the nearest few was tried and reverted: the placer's own attachment test runs inside the
        // executor, so a cap applied here fills with open air that has nothing to hold a torch and reports
        // no opportunity while a usable wall sits just past the cut. Bounding this list means running the
        // attachment test during the search, which is the expensive half, and that trade has not been
        // measured yet.
        foreach (var candidate in nearest)
            into.Add((candidate.Cost, into.Count, candidate.Tile));
        if (region != null) refusal = "no-tile-the-placer-accepts";
        // A cut scan that already found sites hands them on: they are real, and the ones it did not reach were
        // further away in the same cost order. A cut scan that found none has not answered, and saying "no
        // opportunity" there would be reporting the deadline as a fact about the world.
        if (cut && nearest.Count == 0) refusal = "dark-region-scan-cut-by-planning-deadline";
    }

    /// <summary>
    /// Whether this spot wants a torch. It is the site's whole neighbourhood rather than the one tile,
    /// because a torch lights an area: a dark tile pressed against a lit room needs no torch, and a point
    /// query says it does. Sampled from the engine at its own stride rather than off the field's lattice,
    /// whose stride is wider than the lit patches this has to resolve. Unmeasured is refused with lit: a
    /// place nobody has read is not a proven dark place, and a torch spent on it is spent on a guess.
    /// </summary>
    private static bool SiteIsDark(in ActionContext ctx, Point tile)
    {
        // The companion's own carried torch counts as darkness here, which is the opposite of how the torch's
        // own hold decision reads the same samples and is right for the same reason: a permanent torch exists
        // so the place stays lit once the carried one walks away. Without this a companion holding a torch
        // refuses every site within its own glow — it cannot see that the cave it is standing in needs
        // lighting, because it is the thing lighting it.
        var site = ctx.Senses.Light.MeasuredAround(tile, Weights.LightSiteRadiusTiles, Weights.LightSiteStrideTiles,
            carriedCountsAsDark: true);
        return !site.Unmeasured && site.MeanBrightness < Weights.LightDarkBelow;
    }

    protected override (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead")
            : !PlayerIntegration.CompanionPreferences.Current.TorchPlacement ? (OfferEligibility.PolicyForbidden, "torch-placement-disabled")
            : (OfferEligibility.KnownUnusable, "no-torch-supply");

    /// <summary>
    /// Why this search found nothing, split into the four exits that used to share one reason. They are
    /// not the same fact and they do not imply the same next move: no darkness anywhere is an absence of
    /// work, a region whose tile the flood has not claimed yet is an unanswered search that must not start
    /// a walk, a region proven unreachable is work that exists and cannot be had, and a region no placer
    /// tile accepts is a geometry problem in a place the companion can stand.
    /// </summary>
    protected override (OfferEligibility Eligibility, string Reason)? SearchRefusal(in ActionContext ctx)
        // The site budget is not tested here: it belongs to the executor that owns it, which reports it ahead
        // of every reason this method can give, so a copy of the test here would be a second home for one fact
        // and would answer differently the moment the executor's ordering changed.
        => refusal switch
        {
            "no-light-measured-in-range" => (OfferEligibility.Unresolved, refusal),
            "dark-region-tile-not-yet-known-reachable" => (OfferEligibility.Unresolved, refusal),
            "dark-region-scan-cut-by-planning-deadline" => (OfferEligibility.Unresolved, refusal),
            // The shared name for this condition, not a lighting-specific one: failing the two-way region
            // is exactly "the body cannot go there and come home", which is what the executor's round-trip
            // proof called by this name before the reach sense answered the same question more cheaply.
            "interaction-site-has-no-return" => (OfferEligibility.KnownUnusable, refusal),
            _ => (OfferEligibility.NoOpportunity, refusal),
        };

    protected override bool Candidate(in ActionContext ctx, Point tile)
    {
        Item? torch = PlaceSuppliedTorches.Supply(ctx.Companion.Bag.Items, ctx.Player.inventory);
        if (torch == null || !PlaceSuppliedTorches.Candidate(tile)) return false;
        // The region says where the dark is; this says the torch is not going into a lit corner of it. It
        // is the site's whole neighbourhood rather than the one tile, because a torch lights an area: a
        // dark tile pressed against a lit room needs no torch, and a point query says it does. Sampled from
        // the engine at its own stride, not off the field's lattice, whose stride is wider than the lit
        // patches this has to resolve. Unmeasured is refused with lit: a place nobody has read is not a
        // proven dark place, and a torch spent on it is a torch spent on a guess.
        if (!SiteIsDark(ctx, tile)) return false;
        return RecommendTorchPlacement.Accepts(tile, torch, ctx.Companion.Body.Player);
    }

    /// <summary>
    /// Lighting's two names for a site it walked past. The approach has already asked the reach sense, so
    /// this only has to say which of lighting's four exits the verdict was: an unsettled flood is an
    /// unanswered search that must not start a walk, and a flood that ran out of region is work that exists
    /// and cannot be had. Reporting both as one refusal is what the split of these exits undid.
    /// </summary>
    protected override void NoteApproach(in ActionContext ctx, Reachability.Reach verdict)
        => refusal = verdict == Reachability.Reach.Unknown
            ? "dark-region-tile-not-yet-known-reachable"
            : "interaction-site-has-no-return";

    protected override bool Perform(in ActionContext ctx, Point tile)
    {
        bool placed = PlaceSuppliedTorches.Place(tile, ctx.Companion.Bag.Items, ctx.Player, out string source);
        Infrastructure.Diagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, placed ? "place-torch" : "placement-refused", PerformNote + source);
        return placed;
    }

    /// <summary>The region this activity is working, for the telemetry; absent when nothing is nominated.</summary>
    public LightSense.DarkRegion? NominatedRegion => region;
}
