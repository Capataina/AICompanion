extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using AStar = live::AICompanion.Companion.Brain.SharedMovementSystem.AStar;
using CollectNearbyItems = live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.CollectNearbyItems;
using LightUsefulArea = live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.LightUsefulArea;
using LimitPlanningWork = live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.SharedMovementSystem.MovementQueries;
using Offer = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Reach = live::AICompanion.Companion.Brain.SharedMovementSystem.Reachability.Reach;
using TerrainChanges = live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges;
using ActionContext = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using Breath = live::AICompanion.Companion.CharacterBody.CompanionBreath;
using BreathEnvelope = live::AICompanion.Companion.Brain.SharedMovementSystem.Reachability.BreathEnvelope;

/// <summary>
/// Lighting and pot trips through the shared nearby-interaction executor, on native tiles: a trip is offered only where the
/// companion can come back from it, a site reached only by a hop from a take-off elsewhere is offered and performed, and an
/// interaction passed on the way happens incidentally without a second destination or movement owner.
/// </summary>
internal static class VerifyAssistanceTrips
{
    public static int Run()
    {
        Preferences saved = Preferences.Current;
        LightMode mode = Lighting.Mode;
        float brightness = Lighting.GlobalBrightness;
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            bool oneWay = AStar.AllowOneWayDrops;
            Preferences.Current = new Preferences { TorchPlacement = false, PotBreaking = false };
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.Unbounded = false;
                AStar.AllowOneWayDrops = oneWay;
                Preferences.Current = saved;
                VerifyUsefulAssistance.ClearMeasuredLight();
                Lighting.Mode = mode;
                Lighting.GlobalBrightness = brightness;
            }
        }
        Each("L05 darkness below an unreturnable ledge is not offered; the same site with a staircase back is", DarknessBelowAnUnreturnableLedge);
        Each("I03 a pot below an unreturnable ledge is not offered; the same pot with a staircase back is", APotBelowAnUnreturnableLedge);
        if (red == 0) Console.WriteLine("assistance trips: lighting and pot trips require a way back");
        return red;
    }

    // The island the companion and the player stand on: floor row 60, columns 14 to 26, both actors at column 20.
    private const int FloorRow = 60, IslandLeft = 14, IslandRight = 26;
    // The pit to its right: open air from the island edge down to a floor at row 72, closed by a wall at column 41.
    private const int PitFloor = 72, PitLeft = 27, PitRight = 40;

    /// <summary>
    /// A measured dark area with a torch to place. The island's own sites sit inside a lit disc, so the only dark sites are down in
    /// the pit, twelve rows below the island edge: a drop the companion can take and never climb back. The one-way rule the
    /// walker search runs under is whatever the previous tick's request left, so both settings are asked. No setting may offer
    /// the pit. With a staircase from the pit floor up to the island, the pit is a round trip and a site there must be offered.
    /// </summary>
    private static void DarknessBelowAnUnreturnableLedge()
    {
        foreach (bool staircase in new[] { false, true })
            foreach (bool oneWay in new[] { true, false })
            {
                var ctx = BuildIsland(staircase);
                GiveTorches(ctx);
                Preferences.Current.TorchPlacement = true;
                LightIslandOnly();
                AStar.AllowOneWayDrops = oneWay;
                var light = new LightUsefulArea();
                float score = VerifyPreparedActivities.PrepareAndScore(light, ctx);
                string ledger = $"staircase={staircase} oneWay={oneWay}: score={score:0.000} offer={light.Eligibility}/{light.EligibilityReason} target={light.ActivityTarget}";
                if (!staircase)
                {
                    Require(score == 0 && light.ActivityTarget == null,
                        $"a dark site reached only by a one-way drop must not be offered; {ledger}");
                    // With drops allowed the walker reaches the pit, so the refusal must name the missing return rather than an absence.
                    Require(!oneWay || light.Eligibility == Offer.KnownUnusable && light.EligibilityReason == "interaction-site-has-no-return",
                        $"a site the companion could reach but not come back from must say so; {ledger}");
                    continue;
                }
                Require(score > 0 && light.Eligibility == Offer.Usable && light.ActivityTarget is Vector2,
                    $"the same dark pit with a staircase back must be a lighting opportunity; {ledger}");
                Point site = light.ActivityTarget!.Value.ToTileCoordinates();
                Require(site.X >= PitLeft && site.Y > FloorRow,
                    $"the premise needs the offered site down in the pit, not on the island; site={site}; {ledger}");
            }
    }

    /// <summary>The pot analogue of <see cref="DarknessBelowAnUnreturnableLedge"/>: a pot on the pit floor, pot breaking on.</summary>
    private static void APotBelowAnUnreturnableLedge()
    {
        foreach (bool staircase in new[] { false, true })
            foreach (bool oneWay in new[] { true, false })
            {
                var ctx = BuildIsland(staircase);
                Preferences.Current.PotBreaking = true;
                Point pot = PlacePot(new Point(38, PitFloor - 2));
                AStar.AllowOneWayDrops = oneWay;
                ctx.Senses.Loot.Pickups.Clear();
                var collect = new CollectNearbyItems();
                collect.Prepare(ctx);
                string ledger = $"staircase={staircase} oneWay={oneWay}: method={collect.Method} value={collect.Score():0.000} offer={collect.Eligibility}/{collect.EligibilityReason} target={collect.ActivityIdentity}";
                if (!staircase)
                {
                    Require(collect.Score() == 0 && collect.Method == "none",
                        $"a pot reached only by a one-way drop must not be offered; {ledger}");
                    continue;
                }
                Require(collect.Score() > 0 && collect.Method == "potential-pot-contents" && collect.ActivityIdentity is Point target
                    && Math.Abs(target.X - pot.X) <= 1 && Math.Abs(target.Y - pot.Y) <= 1,
                    $"the same pot with a staircase back must be offered; {ledger}");
            }
    }

    internal static ActionContext BuildIsland(bool staircase)
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
                Main.tile[x, y].ClearEverything();
        for (int x = IslandLeft; x <= IslandRight; x++) VerifyOreWork.Place(new Point(x, FloorRow), TileID.Dirt);
        for (int x = PitLeft - 1; x <= PitRight; x++) VerifyOreWork.Place(new Point(x, PitFloor), TileID.Dirt);
        for (int y = FloorRow - 6; y <= PitFloor; y++) VerifyOreWork.Place(new Point(PitRight + 1, y), TileID.Dirt);
        if (staircase)
            // One-tile steps rising leftward from the pit floor to one row below the island edge, so every step is a walk up or down.
            // The pit floor right of the steps, columns 38 to 40, stays open: that is where the dark sites and the pot are.
            for (int k = 1; k <= PitFloor - FloorRow - 1; k++)
                for (int y = PitFloor - k; y < PitFloor; y++)
                    VerifyOreWork.Place(new Point(38 - k, y), TileID.Dirt);
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        TerrainChanges.Reset();
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        Require(MovementQueries.IsStandable(20, FloorRow - 1), "the island must hold the companion");
        var pitTrip = MovementQueries.RoundTrip(new Point(20, FloorRow - 1), new Point(39, PitFloor - 1), EnvelopeOf(ctx));
        Require(pitTrip.Outward == Reach.Yes && pitTrip.Return == (staircase ? Reach.Yes : Reach.No),
            $"the island premise must be a one-way drop without the staircase and a round trip with it; staircase={staircase} trip={pitTrip}");
        return ctx;
    }

    internal static BreathEnvelope EnvelopeOf(ActionContext ctx)
        => new(ctx.Companion.Breath.TicksLeft, Breath.BreathMax * Breath.BreathCDMax, Breath.RecoverPerTick * Breath.BreathCDMax);

    internal static void GiveTorches(ActionContext ctx)
    {
        Item supply = new();
        supply.SetDefaults(ItemID.Torch);
        supply.stack = 20;
        ctx.Player.inventory[0] = supply;
        ctx.Player.selectedItem = 1;
    }

    /// <summary>Dark everywhere except a disc over the island, so every site on the island is vetoed as already lit while the area
    /// around the companion still measures dark.</summary>
    internal static void LightIslandOnly()
        => VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100),
            (x, y) => (x - 20) * (x - 20) + (y - 58) * (y - 58) <= 8 * 8 ? .9f : .02f);

    internal static Point PlacePot(Point origin)
    {
        Main.tileSolid[TileID.Pots] = false;
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                Tile tile = Main.tile[origin.X + x, origin.Y + y];
                tile.ClearEverything(); tile.HasTile = true; tile.TileType = TileID.Pots;
                tile.TileFrameX = (short)(x * 18); tile.TileFrameY = (short)(y * 18);
            }
        TerrainChanges.Reset();
        AStar.InvalidateEdges();
        return origin;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
