extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using LightSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.LightSense;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using PerformNearbyWorldWork = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.PerformNearbyWorldWork;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
using RecommendTorchPlacement = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.RecommendTorchPlacement;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using TransientLights = live::AICompanion.Companion.Brain.Infrastructure.Observation.TransientLights;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Torches go wherever the player's own smart cursor could put one in the dark, and a search that cannot finish says
/// which of its exits it took.
///
/// <para>Every scene here presents light the way the colour engine does rather than painting brightness by hand: the
/// world's own colour per tile, each placed light merged in, each carried light merged by maximum, then the engine's
/// own <c>LightMap.Blur</c>, read at the game's default global brightness. The older lighting fixtures write
/// brightness straight into the map at a global brightness of one, which is exactly the one setting at which a light
/// stronger than one cannot exist, so no row there could see a defect that lives in the difference.</para>
///
/// <para>Every row here reads only surfaces that existed before this file did, so each is red against the code it was
/// written to replace without that code being checked out; the rows that read the funnel, the player's reference and
/// the recorded factors are in <c>VerifyCandidateFunnel</c>, because they read surfaces this lane added.</para>
/// </summary>
internal static class VerifyTorchPlacementRule
{
    private const BindingFlags InstanceField = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int StandRow = 59;
    private static readonly Rectangle Window = new(0, 0, 100, 100);

    /// <summary>The engine's own default, <c>Lighting.DEFAULT_GLOBAL_BRIGHTNESS</c>. A row that runs at one cannot tell a
    /// clamped light from an unclamped one, because at one no mean colour exceeds one.</summary>
    internal const float GameGlobalBrightness = 1.2f;

    // A room sealed in rock around the scene's player and body, whose floor is the scene's own floor row.
    internal const int RoomLeft = 12, RoomRight = 30, RoomTop = 52, RoomBottom = 59;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Preferences saved = Preferences.Current;
        LightMode mode = Lighting.Mode;
        float brightness = Lighting.GlobalBrightness;
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            Preferences.Current = new Preferences { TorchPlacement = true, PotBreaking = false };
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.End();
                LimitPlanningWork.Unbounded = false;
                Preferences.Current = saved;
                ForgetTransients();
                VerifyUsefulAssistance.ClearMeasuredLight();
                Lighting.Mode = mode;
                Lighting.GlobalBrightness = brightness;
            }
        }
        Each("light: a torch carried through a dark cave never makes the ground beside it read lit at the game's brightness",
            ACarriedTorchInADarkCaveReadsCarried);
        Each("light: the same carried torch in daylight never hides the daylight under it",
            ACarriedTorchInDaylightHidesNothing);
        Each("place: a dark room his cursor could put a torch in is offered and a torch is placed there",
            ADarkRoomHisCursorCouldLightIsLit);
        Each("place: the same room lit by a torch already placed offers nothing",
            TheSameRoomLitByAPlacedTorchOffersNothing);
        Each("place: the same room in daylight, with him holding a torch, offers nothing",
            TheSameRoomInDaylightOffersNothing);
        Each("search: a search the planning deadline cut says so rather than naming an unsettled stand",
            ACutSearchIsNamedAsCut);
        Each("search: a stand beyond a finished flood's known radius is named, set aside and not re-asked",
            AStandBeyondTheKnownRadiusIsNamedAndSetAside);
        Each("search: refusals proved past the store's pruning size are all still remembered on the next search",
            RefusalsSurviveTheStoresSize);
        if (red == 0) Console.WriteLine("torch placement rule: carried light is discounted at the game's own brightness, dark tiles his cursor accepts are lit, and every search exit is named");
        return red;
    }

    // ---- carried light, at the brightness the game actually runs at ------------------------------------------

    /// <summary>
    /// A player holding a torch in a dark cave. The engine lights the ground beside him to the torch's own colour decayed
    /// per tile and reports it at the game's global brightness, so the brightness it reports at his tile is above one.
    /// The sense's model of the same torch is compared against that reading to decide whether the light is his, and a
    /// model clamped to one before it decays is below the engine everywhere the torch reaches — so every tile he lights
    /// reads as the cave's own light, the cave reads lit, and nothing is ever placed. That is the unlit statue area of
    /// the 15 September capture: `dark_near` 0.00 on 3,938 rows beside five to eleven carried lights.
    /// </summary>
    private static void ACarriedTorchInADarkCaveReadsCarried()
    {
        var ctx = Scene();
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Point carrier = new(body.X + 4, body.Y - 1);
        PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: null, (carrier, TorchColour()));
        ForceRefresh(ctx);
        var light = ctx.Companion.Brain.Senses.Light;
        Require(TransientLights.Count > 0, $"premise: the carried torch must reach the sense; engine list {RawTransientList().Count}");
        Require(Lighting.Brightness(carrier.X, carrier.Y) > 1f,
            $"premise: at the game's brightness the engine reports more than one at a torch; read {Lighting.Brightness(carrier.X, carrier.Y):0.000}");

        // Every open-air tile the torch lights above the dark level is light somebody is carrying, and must never be
        // reported as the cave's: it is either unknown to the hold question or dark to the placement question.
        int asked = 0;
        var wrong = new List<string>();
        for (int dx = -12; dx <= 12; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                Point tile = new(carrier.X + dx, carrier.Y + dy);
                if (!LightSense.IsOpenAir(tile.X, tile.Y)) continue;
                if (Lighting.Brightness(tile.X, tile.Y) < Weights.LightDarkBelow) continue;
                asked++;
                if (light.MeasuredBrightnessAt(tile) is float kept)
                    wrong.Add($"{tile} engine {Lighting.Brightness(tile.X, tile.Y):0.000} kept as world light {kept:0.000}");
            }
        Require(asked > 10, $"premise: the torch must light a stretch of the cave above the dark level; lit tiles {asked}");
        Require(wrong.Count == 0,
            $"a torch somebody carries must never be read as the cave's own light at the game's global brightness; "
            + $"{wrong.Count} of {asked} lit tiles were: {string.Join("; ", wrong.Take(4))}");
    }

    /// <summary>
    /// The mirror, and the reason the fix compares in the engine's own units on both sides rather than only unclamping the
    /// model. In daylight the world is brighter than the torch everywhere, so no tile is the torch's; a model decayed from
    /// an unclamped strength but compared against a reading clamped to one calls the carrier's own tile and its
    /// neighbours his light, and a companion beside a player holding a torch at noon would place torches at his feet.
    /// </summary>
    private static void ACarriedTorchInDaylightHidesNothing()
    {
        var ctx = Scene();
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Point carrier = new(body.X + 4, body.Y - 1);
        PresentEngineLight((_, _) => new Vector3(1f), GameGlobalBrightness, placed: null, (carrier, TorchColour()));
        ForceRefresh(ctx);
        var light = ctx.Companion.Brain.Senses.Light;
        Require(TransientLights.Count > 0, "premise: the carried torch must reach the sense");
        var hidden = new List<string>();
        for (int dx = -3; dx <= 3; dx++)
        {
            Point tile = new(carrier.X + dx, carrier.Y);
            if (!LightSense.IsOpenAir(tile.X, tile.Y)) continue;
            if (light.MeasuredBrightnessAt(tile) is not float read || read < Weights.LightDarkBelow)
                hidden.Add($"{tile} engine {Lighting.Brightness(tile.X, tile.Y):0.000} read {(light.MeasuredBrightnessAt(tile)?.ToString("0.000") ?? "unknown")}");
        }
        Require(hidden.Count == 0,
            $"daylight brighter than a carried torch is the world's light and must be read as it is; {string.Join("; ", hidden)}");
    }

    // ---- a torch goes where his cursor would put one, in the dark, and nowhere else ---------------------------

    /// <summary>
    /// The owner's rule, on the scene that broke it: a small room sealed in rock, dark, with the player standing in it
    /// holding a torch. His own smart cursor offers him a floor tile beside him. The companion must offer a tile the same
    /// torch step accepts, inside the room, and then actually put a torch there.
    ///
    /// <para>Before, the room read lit: every tile of it is within the carried torch's reach, and the carried torch was
    /// modelled below the engine's own reading of it, so the whole room was kept as world light. The only dark air the
    /// field could see was outside the rock, where no stand exists, and the offer was a proven refusal while the player
    /// stood in the dark with his cursor offering a spot.</para>
    /// </summary>
    private static void ADarkRoomHisCursorCouldLightIsLit()
    {
        var ctx = Scene();
        BuildSealedRoom();
        Item torch = GiveTorches(ctx, held: true);
        Settle(ctx);
        PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: null,
            (ctx.Player.Center.ToTileCoordinates(), TorchColour()));
        ForceRefresh(ctx);
        Point? his = HisCursorTile(ctx.Player, torch);
        Require(his is Point h && InRoom(h),
            $"premise: the player's own cursor must offer him a tile in the dark room; offered {his}");

        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        string offer = $"score={score:0.000} offer={action.Eligibility}/{action.EligibilityReason} target={action.ActivityTarget}";
        Require(score > 0 && action.Eligibility == Offer.Usable && action.ActivityTarget is not null,
            $"a dark room the player's cursor could light must be a lighting job; {offer}");
        Point site = action.ActivityTarget!.Value.ToTileCoordinates();
        Require(InRoom(site) && RecommendTorchPlacement.Accepts(site, torch, ctx.Companion.StandIn.Player),
            $"the offered site must be a tile in the room the game's own torch step accepts; site={site} {offer}");

        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");
        Point? placed = null;
        for (int tick = 0; tick < 1200 && placed == null; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            placed = TorchTiles().Cast<Point?>().FirstOrDefault(InRoomOrNull);
        }
        Require(placed is Point p && RoomAir(p),
            $"lighting must put a torch in the dark room, not only offer to; placed={placed} last action={brain.LastAction?.Name} {offer}");
    }

    /// <summary>
    /// The same room with a torch already standing on its floor and the world's light around it as the engine computes
    /// it. Every tile of the room is within the torch's own light above the dark level, and the tiles far enough from it
    /// for the game's own spacing to allow another torch are among them, so a rule that placed wherever the torch step
    /// accepts, whatever the light, would put a second torch in a lit room; this rule offers nothing.
    /// </summary>
    private static void TheSameRoomLitByAPlacedTorchOffersNothing()
    {
        var ctx = Scene();
        BuildSealedRoom();
        Item torch = GiveTorches(ctx, held: false);
        Point standing = new((RoomLeft + RoomRight) / 2, RoomBottom);
        VerifyOreWork.Place(standing, TileID.Torches);
        Settle(ctx);
        PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: new[] { (standing, TorchColour()) });
        ForceRefresh(ctx);
        Point spaced = new(RoomLeft, RoomBottom);
        Require(RecommendTorchPlacement.Accepts(spaced, torch, ctx.Companion.StandIn.Player),
            $"premise: the room must hold a tile the game's spacing allows a second torch on, or the row tests the spacing and not the light; {spaced}");
        Require(Lighting.Brightness(spaced.X, spaced.Y) >= Weights.LightDarkBelow,
            $"premise: that tile must be lit by the torch already standing; engine {Lighting.Brightness(spaced.X, spaced.Y):0.000}");

        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(action.Eligibility != Offer.Usable && action.ActivityTarget is null && score == 0f,
            $"a room a torch already lights must not be offered another; score={score:0.000} offer={action.Eligibility}/{action.EligibilityReason} target={action.ActivityTarget}");
    }

    /// <summary>
    /// The same room in daylight, with the player holding a torch in it. Daylight is brighter than the torch, so none of
    /// the room is the torch's light; a sense that called the tiles beside him his light would count them dark and offer
    /// his feet a torch at noon.
    /// </summary>
    private static void TheSameRoomInDaylightOffersNothing()
    {
        var ctx = Scene();
        BuildSealedRoom();
        GiveTorches(ctx, held: true);
        Settle(ctx);
        PresentEngineLight((_, _) => new Vector3(1f), GameGlobalBrightness, placed: null,
            (ctx.Player.Center.ToTileCoordinates(), TorchColour()));
        ForceRefresh(ctx);
        Require(TransientLights.Count > 0, "premise: the torch he holds must reach the sense");
        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(action.Eligibility != Offer.Usable && action.ActivityTarget is null && score == 0f,
            $"a room in daylight needs no torch however many he is carrying; score={score:0.000} offer={action.Eligibility}/{action.EligibilityReason} target={action.ActivityTarget}");
    }

    // ---- every exit of the shared search is named for what it is ---------------------------------------------

    /// <summary>
    /// The deadline expiring in the middle of the search. It is an answer that has not arrived, and it is not the flood's
    /// answer: the 15 September capture reported it as an unsettled stand on 3,607 rows whose flood was complete and
    /// whose every asked site was proven unreachable. The executor is driven through a fixture method whose only site
    /// is out of reach and whose gathering expires the deadline, which is the one deterministic way to reach the cut
    /// inside the stand loop rather than inside a gathering scan.
    /// </summary>
    private static void ACutSearchIsNamedAsCut()
    {
        var ctx = Scene();
        var sites = new Sites { ExpireDeadlineAfterGathering = true };
        sites.Tiles.Add(new Point(40, StandRow));
        try { VerifyPreparedActivities.PrepareAndScore(sites, ctx); }
        finally { LimitPlanningWork.End(); LimitPlanningWork.Unbounded = true; }
        Require(sites.EligibilityReason != "interaction-stand-not-yet-known-reachable",
            $"a search the deadline cut must not be reported as a stand the flood has not settled; offer={sites.Eligibility}/{sites.EligibilityReason}");
        Require(sites.Eligibility == Offer.Unresolved && sites.EligibilityReason == "interaction-search-cut-by-planning-deadline",
            $"a search the deadline cut is unresolved under its own name; offer={sites.Eligibility}/{sites.EligibilityReason}");
    }

    /// <summary>
    /// A site sealed in rock at the far end of the world, beyond the finished flood's known radius from its root. A
    /// finished flood proves absence only inside that radius, so the stand is not proven unreachable, and nothing the
    /// flood will do can answer it either: before, it was reported as not yet known and re-asked every retry for as long
    /// as the body stayed where it was. It is named, set aside while that flood answers, and not re-asked.
    /// </summary>
    private static void AStandBeyondTheKnownRadiusIsNamedAndSetAside()
    {
        var ctx = Scene();
        for (int x = 76; x <= 94; x++)
            for (int y = 44; y <= 59; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        for (int x = 84; x <= 88; x++)
            for (int y = 50; y <= 52; y++)
                Main.tile[x, y].ClearEverything();
        ctx.Npc.position = new Vector2(8 * 16, 60 * 16 - ctx.Npc.height);
        ctx.Npc.velocity = Vector2.Zero;
        ctx.Player.position = new Vector2(47 * 16, 60 * 16 - ctx.Player.height);
        Settle(ctx);
        var reach = ctx.Companion.Brain.Senses.Reach;
        Point site = new(86, 52);
        Require(reach.Complete && !reach.WithinKnownRadius(site) && reach.Reachable(site) == ReachVerdict.NotYet,
            $"premise: a finished flood with the site outside its known radius; complete={reach.Complete} within={reach.WithinKnownRadius(site)} verdict={reach.Reachable(site)}");

        var sites = new Sites();
        sites.Tiles.Add(site);
        VerifyPreparedActivities.PrepareAndScore(sites, ctx);
        string first = $"offer={sites.Eligibility}/{sites.EligibilityReason} asked={sites.LastSearchAsked} ledger={sites.LastSearchSites}";
        Require(sites.LastSearchAsked == 1, $"premise: the site's stand must actually be asked; {first}");
        Require(sites.EligibilityReason != "interaction-stand-not-yet-known-reachable" && sites.Eligibility != Offer.Unresolved,
            $"a stand beyond a finished flood's known radius is not a question the flood has yet to answer; {first}");
        Require(sites.Eligibility == Offer.Deferred && sites.EligibilityReason == "interaction-stand-beyond-known-radius",
            $"a stand beyond a finished flood's known radius is deferred under its own name; {first}");

        typeof(PerformNearbyWorldWork).GetField("nextSearch", InstanceField)!.SetValue(sites, 0UL);
        VerifyPreparedActivities.PrepareAndScore(sites, ctx);
        Require(sites.LastSearchAsked == 0 && sites.Eligibility == Offer.Deferred,
            $"while the same flood answers, the site is not asked again; offer={sites.Eligibility}/{sites.EligibilityReason} asked={sites.LastSearchAsked} (first search: {first})");
    }

    /// <summary>
    /// Eighty sites sealed in a pocket under the floor, nearer than the one site the body can reach. The first search
    /// proves every pocket site unreachable on its way to the reachable one. Then the reachable site stops being a
    /// candidate and the search runs again: every pocket refusal still stands, so nothing is asked. Before, the store
    /// emptied itself whole past sixty-four entries, so the second search re-proved the pocket, and under a planning
    /// deadline a floor of sealed pockets was re-proved on every search and the reachable site behind them was never
    /// reached.
    /// </summary>
    private static void RefusalsSurviveTheStoresSize()
    {
        var ctx = Scene();
        const int PocketLeft = 26, PocketRight = 41, PocketTop = 64, PocketBottom = 69;
        for (int x = PocketLeft - 4; x <= PocketRight + 4; x++)
            for (int y = 61; y <= PocketBottom + 4; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        var sites = new Sites();
        for (int y = PocketTop; y <= PocketBottom; y++)
            for (int x = PocketLeft; x <= PocketRight; x++)
            {
                Main.tile[x, y].ClearEverything();
                if (sites.Tiles.Count < 80) sites.Tiles.Add(new Point(x, y));
            }
        Point reachable = new(60, StandRow);
        sites.Tiles.Add(reachable);
        Settle(ctx);
        var reach = ctx.Companion.Brain.Senses.Reach;
        Require(reach.Complete && reach.Reachable(sites.Tiles[0]) == ReachVerdict.Unreachable && reach.Reachable(reachable) == ReachVerdict.Reachable,
            $"premise: the pocket proven unreachable and the floor reachable; complete={reach.Complete} pocket={reach.Reachable(sites.Tiles[0])} floor={reach.Reachable(reachable)}");

        VerifyPreparedActivities.PrepareAndScore(sites, ctx);
        Require(sites.Eligibility == Offer.Usable && Equals(sites.ActivityIdentity, reachable) && sites.LastSearchAsked == 81,
            $"premise: the first search proves all eighty pocket sites on its way to the reachable one; offer={sites.Eligibility}/{sites.EligibilityReason} target={sites.ActivityIdentity} asked={sites.LastSearchAsked}");

        sites.Tiles.Remove(reachable);
        typeof(PerformNearbyWorldWork).GetField("nextSearch", InstanceField)!.SetValue(sites, 0UL);
        VerifyPreparedActivities.PrepareAndScore(sites, ctx);
        Require(sites.LastSearchAsked == 0,
            $"every refusal proven on the last search still stands while its flood answers, so none is asked again; asked={sites.LastSearchAsked} offer={sites.Eligibility}/{sites.EligibilityReason}");
        Require(sites.Eligibility == Offer.KnownUnusable && sites.EligibilityReason == "interaction-site-has-no-return",
            $"the search that asked nothing reports the proofs it is standing on; offer={sites.Eligibility}/{sites.EligibilityReason}");
    }

    /// <summary>
    /// The shared executor with nothing of its own: a fixed list of sites asked in list order, a candidate test that is
    /// membership of the list, and optionally a planning deadline that expires the moment gathering ends. It lets a row
    /// drive the executor's own exits without a subclass's gathering, value or placement getting in the way.
    /// </summary>
    private sealed class Sites : PerformNearbyWorldWork
    {
        public readonly List<Point> Tiles = new();
        public bool ExpireDeadlineAfterGathering;
        public override string Name => "fixture-sites";
        protected override float Utility => 1f;
        protected override bool Enabled(in ActionContext ctx) => true;
        protected override bool Candidate(in ActionContext ctx, Point tile) => Tiles.Contains(tile);
        protected override bool Perform(in ActionContext ctx, Point tile) => false;
        protected override void GatherSearchTiles(in ActionContext ctx, List<(float Cost, int Order, Point Tile)> into)
        {
            foreach (Point p in Tiles)
                if (!SearchTileDeferred(p)) into.Add((into.Count, into.Count, p));
            if (!ExpireDeadlineAfterGathering) return;
            LimitPlanningWork.Unbounded = false;
            LimitPlanningWork.Begin(0);
        }
    }

    // ---- scene -------------------------------------------------------------------------------------------------

    /// <summary>The ore-work floor with nothing in the hands and nothing in the bag, and no light presented yet.</summary>
    internal static ActionContext Scene()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(90, StandRow));
        Main.tile[90, StandRow].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        ctx.Player.selectedItem = 1;
        ForgetTransients();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        return ctx;
    }

    /// <summary>Rock from well above the room down through the floor, across the whole floor, with the room left open
    /// around the scene's player and body.</summary>
    internal static void BuildSealedRoom()
    {
        for (int x = 5; x < 95; x++)
            for (int y = 40; y <= 60; y++)
                if (!RoomAir(new Point(x, y)))
                    VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
    }

    internal static bool RoomAir(Point tile) => tile.X >= RoomLeft && tile.X <= RoomRight && tile.Y >= RoomTop && tile.Y <= RoomBottom;
    private static bool InRoom(Point tile) => RoomAir(tile);
    private static bool InRoomOrNull(Point? tile) => tile is Point t && RoomAir(t);

    /// <summary>Settles the scene after an edit: the edit log forgotten, the tile world rebuilt, the flood thrown away and
    /// run again from the body, and every sense observed.</summary>
    internal static void Settle(ActionContext ctx)
    {
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        VerifyOreWork.ResettleReach(ctx);
    }

    internal static Item GiveTorches(ActionContext ctx, bool held)
    {
        Item supply = new();
        supply.SetDefaults(ItemID.Torch);
        supply.stack = 20;
        ctx.Player.inventory[0] = supply;
        ctx.Player.selectedItem = held ? 0 : 1;
        return supply;
    }

    internal static Vector3 TorchColour()
    {
        TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
        return new Vector3(r, g, b);
    }

    /// <summary>The tile the player's own smart cursor would offer, worked out here rather than asked of the production
    /// code: every tile of his reach box as the game builds it, put to the one-tile torch step, and the accepted tile
    /// nearest his centre.</summary>
    internal static Point? HisCursorTile(Player player, Item torch)
    {
        int boost = torch.tileBoost;
        int startX = (int)(player.position.X / 16f) - Player.tileRangeX - boost + 1;
        int endX = (int)((player.position.X + player.width) / 16f) + Player.tileRangeX + boost - 1;
        int startY = (int)(player.position.Y / 16f) - Player.tileRangeY - boost + 1;
        int endY = (int)((player.position.Y + player.height) / 16f) + Player.tileRangeY + boost - 2;
        Point? best = null;
        float nearest = float.MaxValue;
        for (int x = Math.Max(10, startX); x <= Math.Min(Main.maxTilesX - 10, endX); x++)
            for (int y = Math.Max(10, startY); y <= Math.Min(Main.maxTilesY - 10, endY); y++)
            {
                Point tile = new(x, y);
                if (Main.tile[x, y].HasTile || !RecommendTorchPlacement.Accepts(tile, torch, player)) continue;
                float distance = Vector2.Distance(new Vector2(x * 16 + 8, y * 16 + 8), player.Center);
                if (distance >= nearest) continue;
                nearest = distance;
                best = tile;
            }
        return best;
    }

    internal static IEnumerable<Point> TorchTiles()
    {
        for (int x = Window.Left; x < Window.Right; x++)
            for (int y = Window.Top; y < Window.Bottom; y++)
                if (Main.tile[x, y].HasTile && TileID.Sets.Torch[Main.tile[x, y].TileType])
                    yield return new Point(x, y);
    }

    /// <summary>
    /// Presents light the way the colour engine's own blur state does: the world's colour on every tile of the window,
    /// solid tiles masked so light decays through them at the engine's solid rate, each placed light merged in the way
    /// the tile scan merges a torch standing in the world, each carried light merged by maximum the way
    /// <c>ApplyPerFrameLights</c> merges it, then the engine's <c>LightMap.Blur</c>, and the processed area swapped in.
    /// The carried lights also go into the engine's per-frame list through <c>Lighting.AddLight</c>, which is the list
    /// the sense reads, so the sense and the map describe one frame; placed lights do not, because the engine's scan
    /// never puts them there.
    /// </summary>
    internal static void PresentEngineLight(Func<int, int, Vector3> world, float globalBrightness,
        (Point Tile, Vector3 Colour)[]? placed, params (Point Tile, Vector3 Colour)[] carried)
    {
        Lighting.Mode = LightMode.Color;
        Lighting.GlobalBrightness = globalBrightness;
        object engine = typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var map = (LightMap)engine.GetType().GetField("_activeLightMap", InstanceField)!.GetValue(engine)!;
        map.SetSize(Window.Width, Window.Height);
        map.Clear();
        for (int x = 0; x < Window.Width; x++)
            for (int y = 0; y < Window.Height; y++)
            {
                int wx = Window.X + x, wy = Window.Y + y;
                Tile tile = Main.tile[wx, wy];
                bool solid = tile.HasUnactuatedTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
                map.SetMaskAt(x, y, solid ? LightMaskMode.Solid : LightMaskMode.None);
                map[x, y] = world(wx, wy);
            }
        foreach (var (tile, colour) in (placed ?? Array.Empty<(Point, Vector3)>()).Concat(carried))
            map[tile.X - Window.X, tile.Y - Window.Y] = Vector3.Max(map[tile.X - Window.X, tile.Y - Window.Y], colour);
        foreach (var (tile, colour) in carried)
            Lighting.AddLight(tile.ToWorldCoordinates(), colour.X, colour.Y, colour.Z);
        map.Blur();
        engine.GetType().GetField("_activeProcessedArea", InstanceField)!.SetValue(engine, Window);
    }

    internal static void ForgetTransients()
    {
        RawTransientList().Clear();
        TransientLights.Forget();
    }

    private static System.Collections.IList RawTransientList()
    {
        object engine = typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (System.Collections.IList)engine.GetType().GetField("_perFrameLights", InstanceField)!.GetValue(engine)!;
    }

    /// <summary>Makes the light field resample now rather than serve the frame it already holds; see the same helper in
    /// <c>VerifyLightAndReachSenses</c> for why the counter is set to a large value rather than to the maximum.</summary>
    internal static void ForceRefresh(ActionContext ctx)
    {
        var sense = ctx.Companion.Brain.Senses.Light;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        ulong? before = sense.ReadTick;
        typeof(LightSense).GetField("sinceRefresh", InstanceField)!.SetValue(sense, 1000);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        Require(sense.ReadTick != before, "forcing a refresh must actually resample the world");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
