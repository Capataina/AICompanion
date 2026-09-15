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
using TileDamageWatcher = live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageWatcher;
using CompanionTorches = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.CompanionTorches;
using PlaceTorches = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.PlaceTorches;
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
        Each("place: rooms the world lights to 0.30, 0.45 and 0.70 offer nothing, with or without a torch in his hand",
            MiddleBandRoomsOfferNothing);
        Each("place: a room a standing torch lights gets no second torch at the game's spacing while he holds a torch beside it",
            NoSecondTorchAtTheSpacingDistance);
        Each("place: a lamp-lit room with no bed, crossed by a player holding a torch, is offered nothing anywhere along his way",
            ALampLitRoomCrossedWithATorchOffersNothing);
        Each("place: a tile remembered dark is forgotten when the companion's own torch lights it, and is not offered a second",
            ATorchPlacedBesideARememberedDarkTileForgetsIt);
        Each("free: a companion torch drops nothing when broken, while the player's own torch beside it still drops its item",
            ACompanionTorchDropsNothing);
        Each("free: a spot the player cleared of a companion torch is not lit again within the game's spacing, across a save, until he places something there",
            ASpotHeClearedIsNotLitAgain);
        Each("sky: surface air at night reads below the dark level through the engine's own sky light, from dusk to the end of the night",
            SurfaceAirAtNightReadsDark);
        Each("sky: a dark surface room the sky lights is offered nothing, and the same room with background walls is lit",
            ARoomDaylightReachesIsNeverATorchSite);
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
    ///
    /// <para>The room is read dark once before his torch lights it, as it is in play, where the engine lights the screen
    /// ahead of him before his own light reaches it. Under his torch the world's light cannot be read at all, so a room
    /// the sense has never seen without his light is not known to be dark and is not lit; one it has seen dark stays dark
    /// while he stands in it.</para>
    /// </summary>
    private static void ADarkRoomHisCursorCouldLightIsLit()
    {
        var ctx = Scene();
        BuildSealedRoom();
        Item torch = GiveTorches(ctx, held: true);
        Settle(ctx);
        ReadTheRoomBeforeHisLightArrives(ctx, new Vector3(.02f), placed: null);
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

    /// <summary>
    /// The frame before his light arrives: the room presented with no carried light and a lighting search run over it, which
    /// is what reads each candidate tile's own light and lets the sense remember the dark ones.
    /// </summary>
    internal static void ReadTheRoomBeforeHisLightArrives(ActionContext ctx, Vector3 world, (Point Tile, Vector3 Colour)[]? placed)
    {
        ForgetTransients();
        PresentEngineLight((_, _) => world, GameGlobalBrightness, placed);
        ForceRefresh(ctx);
        VerifyPreparedActivities.PrepareAndScore(new LightUsefulArea(), ctx);
    }

    private static string OfferText(LightUsefulArea action, float score)
        => $"score={score:0.000} offer={action.Eligibility}/{action.EligibilityReason} target={action.ActivityTarget}";

    /// <summary>
    /// The sentinel's rooms: the world's own light at 0.30, 0.45 and 0.70 — every one above the dark level at the game's
    /// brightness — once with nothing in his hand and once with his torch lighting the room, both as a room he walked into
    /// already holding it and as one read before he lit it. Counting carried light as darkness offered a torch in every held
    /// case, because every tile his torch outshone was taken for a dark tile.
    /// </summary>
    private static void MiddleBandRoomsOfferNothing()
    {
        var offered = new List<string>();
        foreach (float world in new[] { .30f, .45f, .70f })
            foreach ((bool held, bool readFirst) in new[] { (false, false), (true, false), (true, true) })
            {
                var ctx = Scene();
                BuildSealedRoom();
                GiveTorches(ctx, held);
                Settle(ctx);
                if (readFirst) ReadTheRoomBeforeHisLightArrives(ctx, new Vector3(world), placed: null);
                var carried = held ? new[] { (ctx.Player.Center.ToTileCoordinates(), TorchColour()) } : Array.Empty<(Point, Vector3)>();
                PresentEngineLight((_, _) => new Vector3(world), GameGlobalBrightness, placed: null, carried);
                ForceRefresh(ctx);
                Point his = ctx.Player.Center.ToTileCoordinates();
                Require(Lighting.Brightness(his.X, his.Y + 1) >= Weights.LightDarkBelow || held,
                    $"premise: a room at world light {world} reads above the dark level; engine {Lighting.Brightness(his.X, his.Y + 1):0.000}");
                var action = new LightUsefulArea();
                float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
                if (score > 0 || action.ActivityTarget is not null)
                    offered.Add($"world {world} held={held} readFirst={readFirst}: {OfferText(action, score)}");
                ForgetTransients();
            }
        Require(offered.Count == 0, $"a room the world already lights must not be offered a torch, whatever he holds; {string.Join("; ", offered)}");
    }

    /// <summary>
    /// The room a standing torch lights, with the player holding a torch at the far wall beside the tile the game's spacing
    /// allows a second torch on. The engine reads that tile at about 0.47 of the world's own light, well above the dark
    /// level, and his torch outshines it: a rule that counts the light he carries as darkness offered the tile.
    /// </summary>
    private static void NoSecondTorchAtTheSpacingDistance()
    {
        var ctx = Scene();
        BuildSealedRoom();
        Item torch = GiveTorches(ctx, held: true);
        Point standing = new((RoomLeft + RoomRight) / 2, RoomBottom);
        VerifyOreWork.Place(standing, TileID.Torches);
        Point spaced = new(RoomLeft, RoomBottom);
        ctx.Player.position = new Vector2((RoomLeft + 1) * 16, (RoomBottom + 1) * 16 - ctx.Player.height);
        Settle(ctx);
        PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: new[] { (standing, TorchColour()) });
        float world = Lighting.Brightness(spaced.X, spaced.Y);
        PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: new[] { (standing, TorchColour()) },
            (ctx.Player.Center.ToTileCoordinates(), TorchColour()));
        ForceRefresh(ctx);
        Require(RecommendTorchPlacement.Accepts(spaced, torch, ctx.Companion.StandIn.Player) && world >= Weights.LightDarkBelow
            && Lighting.Brightness(spaced.X, spaced.Y) > world,
            $"premise: the spaced tile is one the game allows, lit by the standing torch, and outshone by his; world {world:0.000}, with his torch {Lighting.Brightness(spaced.X, spaced.Y):0.000}");
        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        var reading = ctx.Companion.Brain.Senses.Light.ReadForPlacement(spaced, LightSense.Coverage.Current());
        Require(score == 0f && action.ActivityTarget is null && !reading.IsDark,
            $"a room a standing torch lights must get no second torch at the spacing distance while he holds one there; world {world:0.000}, reading {reading}, {OfferText(action, score)}");
        Console.WriteLine($"        the spaced tile reads {world:0.000} by the standing torch alone and {reading.Light} beside his torch");
    }

    /// <summary>
    /// A room a lamp lights, with no bed in it and so no home protection, crossed wall to wall by a player holding a torch.
    /// The sense sees the room lit before he enters, and at every step his torch stands over part of it; none of it is
    /// ever a torch site. Counting carried light as darkness offered a torch at each step along the way.
    /// </summary>
    private static void ALampLitRoomCrossedWithATorchOffersNothing()
    {
        var ctx = Scene();
        BuildSealedRoom();
        GiveTorches(ctx, held: true);
        Settle(ctx);
        var lamp = new[] { (new Point((RoomLeft + RoomRight) / 2, RoomTop + 1), new Vector3(1f, .95f, .8f)) };
        ReadTheRoomBeforeHisLightArrives(ctx, new Vector3(.02f), lamp);
        var offered = new List<string>();
        for (int x = RoomLeft + 1; x <= RoomRight - 1; x += 4)
        {
            ctx.Player.position = new Vector2(x * 16, (RoomBottom + 1) * 16 - ctx.Player.height);
            PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, lamp, (ctx.Player.Center.ToTileCoordinates(), TorchColour()));
            ForceRefresh(ctx);
            var action = new LightUsefulArea();
            float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
            if (score > 0 || action.ActivityTarget is not null) offered.Add($"player at x={x}: {OfferText(action, score)}");
            ForgetTransients();
        }
        Require(offered.Count == 0, $"a lamp-lit room he walks through holding a torch must never be offered one; {string.Join("; ", offered)}");
    }

    /// <summary>
    /// The memory's one way of being wrong, closed. The room is read dark, so its tiles are remembered dark; then the
    /// companion's own torch goes in at the middle of the floor, which lights the far tile the game's spacing still allows
    /// to about 0.47, while the player's torch stands over that tile so its light cannot be read. A memory kept past the
    /// placement would call that tile dark and offer the second torch; the placement is an edit within a light's reach of
    /// the tile, so the tile is forgotten and unknown instead. The placement goes through the companion's own placer,
    /// because the game's placement runs no tile hook and the placer is what announces the edit.
    /// </summary>
    private static void ATorchPlacedBesideARememberedDarkTileForgetsIt()
    {
        var ctx = Scene();
        BuildSealedRoom();
        GiveTorches(ctx, held: true);
        Point standing = new((RoomLeft + RoomRight) / 2, RoomBottom);
        Point spaced = new(RoomLeft, RoomBottom);
        ctx.Player.position = new Vector2((RoomLeft + 1) * 16, (RoomBottom + 1) * 16 - ctx.Player.height);
        Settle(ctx);
        var light = ctx.Companion.Brain.Senses.Light;
        ReadTheRoomBeforeHisLightArrives(ctx, new Vector3(.02f), placed: null);
        Require(light.ReadForPlacement(spaced, LightSense.Coverage.Current()).IsDark && light.RememberedDarkTiles > 0,
            $"premise: the dark room's far tile is read and remembered dark; remembered {light.RememberedDarkTiles}");
        Require(PlaceTorches.Place(standing, ctx.Companion.Bag.Items, ctx.Player, out _),
            "premise: the companion's placer puts its torch in the middle of the floor");
        PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: new[] { (standing, TorchColour()) },
            (ctx.Player.Center.ToTileCoordinates(), TorchColour()));
        ForceRefresh(ctx);
        var reading = light.ReadForPlacement(spaced, LightSense.Coverage.Current());
        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(!reading.IsDark && score == 0f && action.ActivityTarget is null,
            $"a tile the companion's own torch now lights must not stay remembered dark under his torch; reading {reading}, {OfferText(action, score)}");
    }

    // ---- the companion's torches are free, and his removing one is his decision ---------------------------------

    /// <summary>The number of torch items lying in the world, for counting what a break dropped.</summary>
    private static int TorchItemsInTheWorld()
    {
        int count = 0;
        for (int i = 0; i < Main.maxItems; i++)
            if (Main.item[i] is { active: true, type: ItemID.Torch } item) count += item.stack;
        return count;
    }

    /// <summary>Breaks a torch tile the way the game does, with the torch memory's tile hook asked first as tModLoader asks
    /// it inside <c>WorldGen.KillTile</c> (the harness loads no hooks), and returns how many torch items the break dropped.</summary>
    private static int BreakTorch(Point tile, bool byCompanion = false)
    {
        int before = TorchItemsInTheWorld();
        bool hitting = TileDamageWatcher.CompanionIsHitting;
        TileDamageWatcher.CompanionIsHitting = byCompanion;
        try
        {
            bool noItem = CompanionTorches.Breaking(tile.X, tile.Y, Main.tile[tile.X, tile.Y].TileType, fail: false, effectOnly: false);
            WorldGen.KillTile(tile.X, tile.Y, noItem: noItem);
        }
        finally { TileDamageWatcher.CompanionIsHitting = hitting; }
        Require(!Main.tile[tile.X, tile.Y].HasTile, $"premise: the torch at {tile} must be broken");
        return TorchItemsInTheWorld() - before;
    }

    /// <summary>
    /// The companion's torches cost nothing, so breaking one must give nothing: before, the game dropped each broken torch's
    /// own item, which made a single handed-over rare torch an endless supply. The player's torch beside it is the control —
    /// the same break of the same tile type drops its item — so a drop count of zero is the rule and not a harness that
    /// drops nothing.
    /// </summary>
    private static void ACompanionTorchDropsNothing()
    {
        var ctx = Scene();
        BuildSealedRoom();
        GiveTorches(ctx, held: false);
        Settle(ctx);
        Point mine = new(RoomLeft + 2, RoomBottom), his = new(RoomRight - 2, RoomBottom);
        Require(PlaceTorches.Place(mine, ctx.Companion.Bag.Items, ctx.Player, out _), "premise: the companion's placer puts a torch on the floor");
        VerifyOreWork.Place(his, TileID.Torches);
        int hisDrop = BreakTorch(his);
        Require(hisDrop == 1, $"premise: a torch the player placed drops its item when broken, or the harness cannot see a drop; dropped {hisDrop}");
        int myDrop = BreakTorch(mine);
        Require(myDrop == 0, $"a companion torch must drop nothing when broken; dropped {myDrop}");
        Console.WriteLine($"        the player's torch dropped {hisDrop} item, the companion's {myDrop}");
    }

    /// <summary>
    /// He takes down the torch the companion put in a dark room. The room is still dark, so a rule that only remembered the
    /// tile would put the same torch one tile over at the next search; the refusal covers the game's own spacing around the
    /// spot, as a torch still standing there would. It survives the world being saved and loaded, and it ends when he places
    /// something on the spot, which is his next decision about it.
    /// </summary>
    private static void ASpotHeClearedIsNotLitAgain()
    {
        var ctx = Scene();
        BuildSealedRoom();
        GiveTorches(ctx, held: false);
        ctx.Player.position = new Vector2((RoomLeft + 1) * 16, (RoomBottom + 1) * 16 - ctx.Player.height);
        Settle(ctx);
        ReadTheRoomBeforeHisLightArrives(ctx, new Vector3(.02f), placed: null);
        Point spot = new((RoomLeft + RoomRight) / 2, RoomBottom);
        Require(PlaceTorches.Place(spot, ctx.Companion.Bag.Items, ctx.Player, out _),
            $"premise: the companion lights the middle of the dark room; candidate={PlaceTorches.Candidate(spot)} player at {ctx.Player.Center.ToTileCoordinates()}");
        BreakTorch(spot);
        bool Near(Point? tile) => tile is Point t && Math.Abs(t.X - spot.X) <= CompanionTorches.SpacingTiles && Math.Abs(t.Y - spot.Y) <= CompanionTorches.SpacingTiles;
        Point? Offered()
        {
            ForgetTransients();
            PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: null);
            ForceRefresh(ctx);
            var action = new LightUsefulArea();
            VerifyPreparedActivities.PrepareAndScore(action, ctx);
            return action.ActivityTarget?.ToTileCoordinates();
        }
        Point? afterRemoval = Offered();
        Require(!Near(afterRemoval) && !PlaceTorches.Candidate(spot) && !PlaceTorches.Candidate(new Point(spot.X + 1, spot.Y)),
            $"the spot he cleared and every tile within the game's spacing of it must not be lit again; offered {afterRemoval}, refusals {CompanionTorches.Refusals}");

        var saved = new Terraria.ModLoader.IO.TagCompound();
        CompanionTorches.Save(saved);
        CompanionTorches.Clear();
        CompanionTorches.Load(saved);
        Point? afterLoad = Offered();
        Require(!Near(afterLoad) && CompanionTorches.Refusals == 1, $"the refusal must survive the world being saved and loaded; offered {afterLoad}, refusals {CompanionTorches.Refusals}");

        VerifyOreWork.Place(spot, TileID.WoodBlock);
        CompanionTorches.TilePlaced(spot.X, spot.Y);                          // the placement hook, which the harness does not load
        Main.tile[spot.X, spot.Y].ClearEverything();
        Require(CompanionTorches.Refusals == 0 && PlaceTorches.Candidate(spot),
            $"something he placed on the spot ends his refusal of it; refusals {CompanionTorches.Refusals}");
        Console.WriteLine($"        after he cleared the torch the search offered {afterRemoval?.ToString() ?? "nothing"} outside the spacing, the same after a reload, and the spot again once he built on it");
    }

    // ---- the sky: daylight will light it, so the night never makes it a site -------------------------------------

    /// <summary>
    /// The measurement the ruling waited on: what a surface tile open to the sky reads at night. The game's own colour of
    /// the skies is set for the night hour (<c>Main.SetBackColor</c>), turned into the tile colour the way the game turns
    /// it (<c>ApplyColorOfTheSkiesToTiles</c>), and the colour engine's own tile scanner lights one wall-less surface air
    /// tile from it; the brightness is that colour's mean at the game's global brightness, which is what
    /// <c>Lighting.Brightness</c> reports. Every hour of the night is below the dark level, so a companion answering the
    /// light alone would light the surface every night.
    /// </summary>
    private static void SurfaceAirAtNightReadsDark()
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        double worldSurface = Main.worldSurface, time = Main.time;
        bool dayTime = Main.dayTime, sunThroughNoWall = Main.wallLight[0];
        Color tileColor = Main.tileColor;
        var readings = new List<string>();
        try
        {
            Scene();
            LetTheSunThroughNoWall();
            Main.worldSurface = 70;
            Point air = new(40, 50);
            Main.tile[air.X, air.Y].ClearEverything();
            Require(LightSense.DaylightReaches(air.X, air.Y), "premise: a wall-less air tile above the surface line is one the sky lights");
            object engine = typeof(Lighting).GetField("NewEngine", PrivateStatic)!.GetValue(null)!;
            object scanner = engine.GetType().GetField("_tileScanner", InstanceField)!.GetValue(engine)!;
            MethodInfo tileLight = scanner.GetType().GetMethod("GetTileLight")!;
            Type info = typeof(Main).GetNestedType("InfoToSetBackColor")!;
            MethodInfo setBackColor = typeof(Main).GetMethod("SetBackColor", PrivateStatic)!;
            MethodInfo applySkies = typeof(Main).GetMethod("ApplyColorOfTheSkiesToTiles", PrivateStatic)!;
            // The scanner asks the wall loader's light hooks after the sky's colour, and only mod loading creates their arrays;
            // empty is what a world with no modded walls holds, as the ore fixture does for the tile loader's.
            foreach (FieldInfo field in typeof(Terraria.ModLoader.WallLoader).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (field.Name.StartsWith("Hook") && field.FieldType.IsArray && field.GetValue(null) == null)
                    field.SetValue(null, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
            static void Call(MethodInfo method, object? target, object[]? args)
            {
                try { method.Invoke(target, args); }
                catch (TargetInvocationException e) { throw new InvalidOperationException($"{method.Name} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message} at {e.InnerException?.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
            }
            float brightest = 0f;
            foreach (double hour in new[] { 0.0, 8100.0, 16200.0, 24300.0, 32399.0 })
            {
                Main.dayTime = false;
                Main.time = hour;
                object[] args = { Activator.CreateInstance(info)!, null!, null! };
                Call(setBackColor, null, args);
                Call(applySkies, null, null);
                object[] light = { air.X, air.Y, null! };
                Call(tileLight, scanner, light);
                var colour = (Vector3)light[2];
                float brightness = (colour.X + colour.Y + colour.Z) / 3f * GameGlobalBrightness;
                brightest = Math.Max(brightest, brightness);
                readings.Add($"night {hour:0}: {brightness:0.000}");
            }
            Console.WriteLine($"        MEASURE surface air at night, at the game's brightness, against a dark level of {Weights.LightDarkBelow}: {string.Join(", ", readings)}");
            Require(brightest < Weights.LightDarkBelow,
                $"surface air at night must read below the dark level for the ruling to have a case to answer; {string.Join(", ", readings)}");
        }
        finally
        {
            Main.worldSurface = worldSurface;
            Main.time = time;
            Main.dayTime = dayTime;
            Main.tileColor = tileColor;
            Main.wallLight[0] = sunThroughNoWall;
        }
    }

    /// <summary>
    /// The one entry of the game's wall-light table these rows need, set as the game sets it: <c>Main.wallLight[0] = true</c>
    /// in <c>Main.Initialize_TileAndNPCData1_Part2</c> (Main.cs:10593 as decompiled), which a headless process never runs.
    /// Running the whole initialiser would rewrite tile tables the other suites stand on; the wood wall these rows use as the
    /// wall the sun does not pass is not in that method's list at all.
    /// </summary>
    private static void LetTheSunThroughNoWall()
    {
        Main.wallLight[0] = true;
        Require(WallID.Wood == 4 && !Main.wallLight[WallID.Wood], "premise: the game's list lets the sun through no wall and never through wood (wall 4)");
    }

    /// <summary>
    /// The dark room, moved above the surface line. With no background wall the sky lights it — the engine's own rule, which
    /// lights a wall-less cave mouth above the surface by day — so however dark it reads now it is not a torch site. The same
    /// room with a wood background wall, which the sun does not pass, is a surface house the morning does not light, and is
    /// offered as any dark room is.
    /// </summary>
    private static void ARoomDaylightReachesIsNeverATorchSite()
    {
        double worldSurface = Main.worldSurface;
        bool sunThroughNoWall = Main.wallLight[0];
        try
        {
            foreach (bool walled in new[] { false, true })
            {
                var ctx = Scene();
                LetTheSunThroughNoWall();
                BuildSealedRoom();
                Main.worldSurface = RoomBottom + 5;
                if (walled)
                    for (int x = RoomLeft; x <= RoomRight; x++)
                        for (int y = RoomTop; y <= RoomBottom; y++)
                        {
                            Tile tile = Main.tile[x, y];
                            tile.WallType = WallID.Wood;
                        }
                Point floor = new(RoomLeft + 2, RoomBottom);
                Require(LightSense.DaylightReaches(floor.X, floor.Y) != walled && !Main.wallLight[WallID.Wood],
                    $"premise: the sky reaches the room only without its wood wall; walled={walled} daylight={LightSense.DaylightReaches(floor.X, floor.Y)}");
                GiveTorches(ctx, held: false);
                Settle(ctx);
                PresentEngineLight((_, _) => new Vector3(.02f), GameGlobalBrightness, placed: null);
                ForceRefresh(ctx);
                var action = new LightUsefulArea();
                float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
                var reading = ctx.Companion.Brain.Senses.Light.ReadForPlacement(floor, LightSense.Coverage.Current());
                if (walled)
                    Require(score > 0 && action.ActivityTarget is not null && reading.IsDark,
                        $"a dark surface room behind background walls the sun does not pass is a torch site; reading {reading}, {OfferText(action, score)}");
                else
                    Require(score == 0f && action.ActivityTarget is null && reading.Light == LightSense.PlacementLight.Sky,
                        $"a room the sky lights must never be a torch site at night; reading {reading}, {OfferText(action, score)}");
            }
        }
        finally { Main.worldSurface = worldSurface; Main.wallLight[0] = sunThroughNoWall; }
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
        // A torch a row broke is a spot refused for the rows after it, which is the rule and not the next row's scene.
        CompanionTorches.Clear();
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        Require(sense.ReadTick != before, "forcing a refresh must actually resample the world");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
