extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using FreeSpaceSearch = live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using TerrainEditLog = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainEditLog;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using LightSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.LightSense;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using SuccessRegionKind = live::AICompanion.Companion.Brain.Infrastructure.Position.SuccessRegionKind;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using TorchBearer = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.TorchBearer;
using CaptureWorldLight = live::AICompanion.Companion.Brain.Infrastructure.Observation.CaptureWorldLight;
using WorldLight = live::AICompanion.Companion.Brain.Infrastructure.Observation.WorldLight;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// The acceptance set for light and reachability as two senses every consumer reads. Each case is written to
/// fail on the code before them without needing that code checked out, by asserting the answer the old scalar
/// would have given beside the answer the field gives:
///
/// <list type="bullet">
/// <item>a lit room inside a dark world: one mean brightness over the window reads dark and would have raised
/// the torch, while the field reads the body's own neighbourhood as lit and puts it out — and the same field
/// raises it again for dark air on the heading alone, which no single scalar could express</item>
/// <item>an unmeasured neighbourhood casts no vote: a companion off the computed screen beside a player in
/// daylight keeps its torch, because a silent answer is not a bright one</item>
/// <item>light anybody carries, the companion's own torch included, cannot make a neighbourhood read lit, because
/// the field reads the world's own light from the engine's scan, taken before the blur merges what a body carries</item>
/// <item>a dark wing away from a lit body is an offered lighting job whose site the reach sense calls
/// Reachable, rather than a site proven by a round trip of its own</item>
/// <item>two placeable sites in one dark region are worked one after the other without the body going back to
/// the player in between</item>
/// <item>a player standing where no candidate is both standable and reachable still gets a destination that
/// closes the gap, declaring no success region it cannot meet</item>
/// </list>
/// </summary>
internal static class VerifyLightAndReachSenses
{
    private const BindingFlags InstanceField = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int FloorRow = 60, StandRow = 59;
    private static readonly Rectangle Window = new(0, 0, 100, 100);

    public static int Run()
    {
        // The --light-senses entry point reaches here without VerifyEngineMotion's setup, and Main's static
        // constructor needs a save path before Lighting's own static constructor can touch it.
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
                LimitPlanningWork.Unbounded = false;
                Preferences.Current = saved;
                VerifyUsefulAssistance.ClearMeasuredLight();
                Lighting.Mode = mode;
                Lighting.GlobalBrightness = brightness;
            }
        }
        Each("a: a lit room inside a dark world puts the torch out, where one mean over the window would have raised it", ALitRoomInsideADarkWorld);
        Each("a: dark air on the heading alone raises the torch over a lit body", DarkAheadRaisesOverALitBody);
        Each("a: one unmeasured neighbourhood beside one bright one holds the torch", AnUnmeasuredNeighbourhoodCastsNoVote);
        Each("a: the companion's own shown torch cannot light its own neighbourhood", ItsOwnTorchIsNotEvidenceOfLight);
        Each("a: the world's own light is taken once per scan whether the game draws once or twice per tick", TheWorldLightIsTakenOncePerScanAtEveryCadence);
        Each("a: the scan the sense reads holds none of the light the engine's own blur then merges in", TheScanHoldsNoneOfTheLightTheBlurMerges);
        Each("a: a carried torch does not blind the placement search that would replace it", ACarriedTorchDoesNotBlindThePlacementSearch);
        // A torch's own colour, and then a pet-like source that is a different colour and a different strength, so
        // the row cannot be passing on a constant that happens to match the torch.
        TorchID.TorchColor(TorchID.Torch, out float tr, out float tg, out float tb);
        Each("a: a player's held torch never makes a passage read lit",
            () => ACarriedLightNeverMakesAPassageReadLit("player torch", tr, tg, tb));
        Each("a: a light pet never makes a passage read lit",
            () => ACarriedLightNeverMakesAPassageReadLit("light pet", .45f, .75f, .95f));
        Each("a: taking and blurring a large screen's world light is measured per scan", MeasureTheWorldLightOnALargeScreen);
        Each("b: a dark wing away from a lit body is offered with a Reachable site", ADarkWingIsOfferedWithAReachableSite);
        Each("b: nearer darkness the body cannot reach does not hide the darkness it can", NearerUnreachableDarknessDoesNotHideAReachableSite);
        Each("c: two sites in one dark region are worked without going back to the player", TwoSitesAreWorkedWithoutReturning);
        Each("c: the same dark floor priced under the production allowances", MeasureTheRegionScanUnderProductionAllowances);
        Each("e: a retained search survives an edit it never read and dies on one it did", ARetainedSearchSurvivesAnEditItNeverRead);
        Each("e: the invalidation margin is the flood's own explored bounds, either side of it", TheMarginIsTheScansOwnReach);
        Each("e: a revision older than the record's window is treated as changed", ARevisionOlderThanTheRecordIsChanged);
        Each("e: the reach flood refloods for an edit it read and not for one it did not", TheReachFloodRefloodsOnlyForAnEditItRead);
        Each("e: a door toggled by anything but the companion announces its rows", ADoorToggledByAnybodyAnnouncesItsRows);
        if (red == 0) Console.WriteLine("light and reach senses: the torch reads a field around the body and on the heading, lighting works a region through the reach sense, and following always has somewhere to go");
        return red;
    }

    // ---- (a) the torch holds only for dark air near the body or on its heading -------------------------

    /// <summary>
    /// A lit chamber wide enough to fill the torch's own hold radius, inside a world that is dark everywhere
    /// else. The premise is the discriminator: the mean brightness over every open-air tile of the window —
    /// which is what the retired <c>Ambient</c> scalar was — sits below the dark level, so code reading one
    /// number for the whole window raises the torch standing in a lit room. The field is asked the question
    /// that actually decides the hand and answers the opposite.
    /// </summary>
    private static void ALitRoomInsideADarkWorld()
    {
        var ctx = Scene((x, y) => InChamber(x, y) ? .6f : .02f);
        var light = ctx.Companion.Brain.Senses.Light;
        Point body = ctx.Npc.Center.ToTileCoordinates();

        float old = OldWindowMean();
        Require(old < Weights.LightDarkBelow,
            $"the scene must be one the retired window mean calls dark, or it discriminates nothing; mean={old:0.000} against {Weights.LightDarkBelow}");

        var here = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(!here.Unmeasured, $"the body's neighbourhood must be measured for this row to mean anything; {Show(here)}");
        Require(here.DarkFraction < Weights.TorchLowerDarkShare,
            $"a body standing in a lit chamber must read almost no dark air around it; {Show(here)}, window mean {old:0.000}");

        // The hand itself, not only the reading behind it: a torch already up must come down, and the reason
        // must be the room rather than a hold running out.
        var (lit, why) = Settles(ctx, light, startLit: true, heading: ctx.Npc.Bottom);
        Require(!lit && why == "lit-room",
            $"the torch must go out in a lit room and say so; lit={lit} reason={why}, {Show(here)}, window mean {old:0.000}");
    }

    /// <summary>
    /// The README's 6:00 scene, which is the half a single number cannot hold: the body is in the lit chamber
    /// and the player's predicted feet are out in the dark, so the torch is up before the companion arrives
    /// rather than a second and a half afterwards.
    /// </summary>
    private static void DarkAheadRaisesOverALitBody()
    {
        var ctx = Scene((x, y) => InChamber(x, y) ? .6f : .02f);
        var light = ctx.Companion.Brain.Senses.Light;
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Vector2 ahead = new((body.X + 40) * 16f, body.Y * 16f);

        var here = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        var onHeading = light.DarkAirNear(ahead.ToTileCoordinates(), Weights.TorchHoldRadiusTiles);
        Require(here.DarkFraction < Weights.TorchLowerDarkShare && !onHeading.Unmeasured && onHeading.DarkFraction > Weights.TorchRaiseDarkShare,
            $"the premise is a lit body and a dark heading; here {Show(here)} ahead {Show(onHeading)}");

        var (lit, why) = Settles(ctx, light, startLit: false, heading: ahead);
        Require(lit && why == "dark-ahead",
            $"dark air on the heading must raise the torch over a lit body; lit={lit} reason={why}");
    }

    /// <summary>
    /// The failure the two-query read introduces and a both-unmeasured rule does not catch: the companion is
    /// deep in a cave the engine has not computed while the player's predicted feet are out in daylight. One
    /// query is silent and one is bright, and reading the silent one's dark fraction as a zero puts the torch
    /// out in the dark. A silent answer votes for nothing.
    /// </summary>
    private static void AnUnmeasuredNeighbourhoodCastsNoVote()
    {
        // The presented area covers the player's heading only. The body stands far outside it, which is what
        // the engine does to anything off the computed screen.
        var ctx = Scene(null);
        VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(60, 40, 30, 30), (_, _) => .9f);
        var senses = ctx.Companion.Brain.Senses;
        senses.Update(ctx.Npc, ctx.Player);
        var light = senses.Light;
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Vector2 ahead = new(75 * 16f, 50 * 16f);

        var here = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        var onHeading = light.DarkAirNear(ahead.ToTileCoordinates(), Weights.TorchHoldRadiusTiles);
        Require(here.Unmeasured, $"the body must sit outside the presented area for this row to mean anything; {Show(here)}");
        Require(!onHeading.Unmeasured && onHeading.DarkFraction < Weights.TorchLowerDarkShare,
            $"the heading must be measured and bright; {Show(onHeading)}");

        var (lit, why) = Settles(ctx, light, startLit: true, heading: ahead);
        Require(lit && why == "held-lit",
            $"a torch lit in an unread cave must not be put out by the one bright answer beside it; lit={lit} reason={why}, here {Show(here)} ahead {Show(onHeading)}");
    }

    /// <summary>
    /// The companion's own torch, shining in a cave dark everywhere else. The engine presents the torch's colour decayed by
    /// Manhattan distance over the cave, and the scan under it is the cave's own dark. Every sample the field keeps inside the
    /// hold radius must read dark, and every open-air tile the torch lights must still be measured and dark, because none of
    /// them is unknown: the world's light under a carried light is read rather than guessed at. A field built from the presented
    /// map reads the hand's glow as a lit room; a field that dropped what a carried light could account for keeps nothing near
    /// the hand, which is the state that hid every pocket beside a torch from placement.
    /// </summary>
    private static void ItsOwnTorchIsNotEvidenceOfLight()
    {
        var ctx = Scene(null);
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Point hand = (ctx.Npc.Center + new Vector2(ctx.Npc.direction * 10f, -6f)).ToTileCoordinates();
        VerifyUsefulAssistance.WriteMeasuredLight(Window, (_, _) => .02f, carried: CarriedFrom(hand, TorchStrength()));
        typeof(TorchBearer).GetProperty("Shown")!.GetSetMethod(true)!.Invoke(ctx.Companion.Torch, new object[] { true });
        ForceRefresh(ctx);
        var light = ctx.Companion.Brain.Senses.Light;
        Require(light.MeasuredSamples > 0,
            $"the scene must present a field for this row to test anything; samples={light.MeasuredSamples}");
        Require(Lighting.Brightness(hand.X, hand.Y) >= Weights.LightDarkBelow,
            $"premise: the torch lights its own hand as presented; engine {Lighting.Brightness(hand.X, hand.Y):0.000} at {hand}");

        var here = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(!here.Unmeasured && here.Dark == here.Measured,
            $"the companion's own torchlight is being kept as room light: {here.Measured - here.Dark} of "
            + $"{here.Measured} samples inside the hold radius read lit; {Show(here)} at hand {hand}");

        var unknownOrLit = new List<string>();
        int litByTheTorch = 0;
        for (int dx = -4; dx <= 4; dx++)
            for (int dy = -4; dy <= 4; dy++)
            {
                Point tile = new(hand.X + dx, hand.Y + dy);
                if (!LightSense.IsOpenAir(tile.X, tile.Y) || Lighting.Brightness(tile.X, tile.Y) < Weights.LightDarkBelow) continue;
                litByTheTorch++;
                if (light.MeasuredBrightnessAt(tile) is not float read || read >= Weights.LightDarkBelow)
                    unknownOrLit.Add($"{tile} read {(light.MeasuredBrightnessAt(tile)?.ToString("0.000") ?? "unknown")}");
            }
        Require(litByTheTorch > 10, $"premise: the torch lights the air around its hand; lit tiles {litByTheTorch}");
        Require(unknownOrLit.Count == 0,
            $"every tile the companion's own torch lights must be measured at the cave's own dark; {unknownOrLit.Count} of {litByTheTorch} were not: {string.Join("; ", unknownOrLit.Take(4))}");
        Require(light.NearestDarkRegion(body, Weights.LightRegionSearchTiles) is not null,
            "a dark cavern lit only by the companion's own torch must still hold a dark region");
    }

    /// <summary>
    /// Why the world light is observed from the draw and not only from the brain. Each draw's lighting call advances the engine
    /// one state, and a scan can be taken only while the state reads Blur. At one draw per tick every observer takes every scan.
    /// At two draws per tick, which is frame skip off on a 120 Hz display, an observer at the brain tick alone sees the engine at
    /// the same two phases every tick, so at some starting phases it never takes a scan at all; the draw's own observation still
    /// takes every one. And a scan the draw took is not taken again by the brain.
    /// </summary>
    private static void TheWorldLightIsTakenOncePerScanAtEveryCadence()
    {
        Scene((_, _) => .02f);
        object engine = VerifyUsefulAssistance.ColourEngine();
        string[] cycle = { "MinimapUpdate", "ExportMetrics", "Scan", "Blur" };
        var draw = new CaptureWorldLight();
        const int draws = 16, scans = draws / 4;
        int Run(int drawsPerTick, bool drawObserves, bool tickObserves, int startState)
        {
            VerifyUsefulAssistance.ForgetEngineLight();
            engine.GetType().GetField("_workingProcessedArea", InstanceField)!.SetValue(engine, Window);
            int state = startState;
            for (int i = 1; i <= draws; i++)
            {
                state = (state + 1) % cycle.Length;
                VerifyUsefulAssistance.SetEngineState(engine, cycle[state]);
                if (drawObserves) draw.PostDrawTiles();
                if (tickObserves && i % drawsPerTick == 0) WorldLight.Observe();
            }
            return WorldLight.Captures;
        }

        var ledger = new List<string>();
        int tickAloneMissed = 0;
        for (int start = 0; start < cycle.Length; start++)
        {
            int both1 = Run(1, true, true, start), both2 = Run(2, true, true, start), draw2 = Run(2, true, false, start);
            int tick1 = Run(1, false, true, start), tick2 = Run(2, false, true, start);
            ledger.Add($"start {cycle[start]}: both at 1/tick {both1}, both at 2/tick {both2}, draw alone at 2/tick {draw2}, tick alone at 1/tick {tick1}, at 2/tick {tick2}");
            Require(both1 == scans && both2 == scans && draw2 == scans && tick1 == scans,
                $"every scan must be taken exactly once whenever the draw observes, or at one draw per tick; {ledger[^1]}");
            if (tick2 == 0) tickAloneMissed++;
        }
        Require(tickAloneMissed > 0,
            $"premise: at two draws per tick a brain-tick observer alone misses every scan at some phase, or the draw's observation guards nothing; {string.Join("; ", ledger)}");
        Console.WriteLine($"        world light cadence over {draws} draws: {string.Join("; ", ledger)}");
    }

    /// <summary>
    /// The engine fact the world light rests on, asked of the engine rather than of its decompile. The scene's world light is
    /// left in the working map between a scan and its blur, a torch is announced through <c>Lighting.AddLight</c> the way
    /// anything carried is, and the draw's observation takes the scan. Then the engine's own <c>ProcessArea</c> runs its blur
    /// state: its decay update, its merge of the per-frame lights, its blur and the swap that presents the map. The presented
    /// map must show the torch above the dark level at its tile, and the world light taken before it must still read the scene's
    /// dark there. A game version that merged carried lights during the scan, or presented before the blur state, turns this red.
    /// </summary>
    private static void TheScanHoldsNoneOfTheLightTheBlurMerges()
    {
        var ctx = Scene((_, _) => .02f);
        var engine = (LightingEngine)VerifyUsefulAssistance.ColourEngine();
        var perFrame = (System.Collections.IList)engine.GetType().GetField("_perFrameLights", InstanceField)!.GetValue(engine)!;
        perFrame.Clear();
        try
        {
            LightMap scan = VerifyUsefulAssistance.EngineMap(engine, "_workingLightMap");
            scan.SetSize(Window.Width, Window.Height);
            scan.Clear();
            scan.NonVisiblePadding = 0;
            for (int x = 0; x < Window.Width; x++)
                for (int y = 0; y < Window.Height; y++)
                    scan[x, y] = new Vector3(.02f);
            VerifyUsefulAssistance.RestoreEngineDecay(VerifyUsefulAssistance.EngineMap(engine, "_activeLightMap"));
            Point torch = ctx.Npc.Center.ToTileCoordinates() + new Point(4, -1);
            TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
            Lighting.AddLight(torch.ToWorldCoordinates(), r, g, b);
            Require(perFrame.Count > 0, "premise: the announced torch must reach the engine's per-frame list");
            VerifyUsefulAssistance.TakeTheScan(engine, Window);
            float? taken = WorldLight.Brightness(torch.X, torch.Y);

            // ProcessArea times each phase through TimeLogger, whose buffers are allocated by a graphical launch and are null in
            // this shell: without this the call dies with a null reference inside TimeLogger.LightingTime before reaching any
            // lighting at all. Initialising it is allocation and nothing else.
            Terraria.TimeLogger.Initialize();
            engine.ProcessArea(Window);
            float presented = Lighting.Brightness(torch.X, torch.Y);
            Require(presented >= Weights.LightDarkBelow,
                $"premise: the engine's own blur state must merge the announced torch into the map it presents; presented {presented:0.000} at {torch}");
            Require(taken is float t && Math.Abs(t - .02f) < 1e-3f && WorldLight.Brightness(torch.X, torch.Y) is float still && Math.Abs(still - .02f) < 1e-3f,
                $"the world light taken between the scan and the blur must hold none of the torch the blur merged; taken {taken?.ToString("0.000") ?? "unknown"}, presented {presented:0.000} at {torch}");
            Console.WriteLine($"        engine order: the torch's tile reads {presented:0.000} as presented and {taken:0.000} in the world light taken before the blur");
        }
        finally { perFrame.Clear(); }
    }

    /// <summary>A carried light's colour as the engine presents it: its mean at the source, decayed per tile of Manhattan
    /// distance through air, at a global brightness of one.</summary>
    private static Func<int, int, float> CarriedFrom(Point source, float strength)
        => (x, y) => strength * MathF.Pow(.91f, Math.Abs(x - source.X) + Math.Abs(y - source.Y));

    private static float TorchStrength()
    {
        TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
        return (r + g + b) / 3f;
    }

    /// <summary>
    /// A companion already carrying a torch must still be able to see that a place needs a permanent one. Reading its own
    /// light as the room's is a deadlock rather than caution: the torch comes out because the cavern is dark, and every site
    /// then reads lit precisely because the torch is out, so nothing is ever placed.
    ///
    /// <para>The torch here is brought out by the real decision over a real dark scene, not set. Headless nothing lights the map
    /// from it, so the frame the engine would present next is presented by the row: the torch's colour decayed from the hand
    /// over the cave's own dark. Lighting must still offer a site, and a tile that light stands over, asked of the sense
    /// directly, must read dark.</para>
    /// </summary>
    private static void ACarriedTorchDoesNotBlindThePlacementSearch()
    {
        // Dark everywhere, so the assertions below hold wherever keeping company has walked the body by the
        // time the torch is up.
        var ctx = Scene((_, _) => .02f);
        GiveTorches(ctx);
        Settle(ctx);
        var brain = ctx.Companion.Brain;
        // Keeping company only, so the hand stays free and nothing places a torch while we wait for one to
        // come up; lighting is prepared directly afterwards.
        brain.Actions.RemoveAll(a => a.Name != "keep-company");
        for (int tick = 0; tick < 600 && !ctx.Companion.Torch.Shown; tick++)
            VerifyOreWork.AdvanceBrain(ctx);
        Require(ctx.Companion.Torch.Shown,
            $"the premise is a companion carrying light in a dark cavern; "
            + $"lit={ctx.Companion.Torch.Lit} shown={ctx.Companion.Torch.Shown} reason={ctx.Companion.Torch.Reason}");

        Point hand = (ctx.Npc.Center + new Vector2(ctx.Npc.direction * 10f, -6f)).ToTileCoordinates();
        VerifyUsefulAssistance.WriteMeasuredLight(Window, (_, _) => .02f, carried: CarriedFrom(hand, TorchStrength()));
        ForceRefresh(ctx);

        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(score > 0 && action.Eligibility == Offer.Usable && action.ActivityTarget is not null,
            $"a companion holding a torch must still offer to place one; its own carried light is not evidence that "
            + $"the cave is lit, and reading it as such leaves the ability unable to fire in the dark it exists for; "
            + $"score={score:0.000} {action.Eligibility}:{action.EligibilityReason} torch shown={ctx.Companion.Torch.Shown}");

        Point? under = null;
        for (int dx = -3; dx <= 3 && under is null; dx++)
            for (int dy = -3; dy <= 3 && under is null; dy++)
            {
                Point tile = new(hand.X + dx, hand.Y + dy);
                if (LightSense.IsOpenAir(tile.X, tile.Y) && Lighting.Brightness(tile.X, tile.Y) >= Weights.LightDarkBelow) under = tile;
            }
        Require(under is Point, $"premise: the torch lights open air around its hand {hand}");
        var reading = brain.Senses.Light.ReadForPlacement(under!.Value, LightSense.Coverage.Current());
        Require(reading.IsDark,
            $"a tile the companion's own torch lights must read dark to placing by the cave's own light; {reading} at {under}, "
            + $"engine {Lighting.Brightness(under.Value.X, under.Value.Y):0.000}");
    }

    // ---- every carried light leaves the world's light as it was, not only the companion's own ------------

    /// <summary>
    /// The behaviour this lane exists for, stated as a comparison of the same scene with and without a light
    /// somebody is carrying through it. The companion follows a player holding a torch into a dark passage;
    /// under a rule that read the presented light, everything around him read lit, nothing was placed, and the
    /// passage went dark the moment he walked on — the opposite of lighting a mine as you go. The sense never
    /// learns which source this is: whatever a body carries reaches the engine after the scan the sense reads, so a
    /// light pet, a mining helmet and a lantern from a mod nobody here has heard of are all absent from it alike.
    /// </summary>
    private static void ACarriedLightNeverMakesAPassageReadLit(string what, float r, float g, float b)
    {
        // Without the carrier first, to establish what the world's own light actually is.
        var ctx = Scene((_, _) => .02f);
        var light = ctx.Companion.Brain.Senses.Light;
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Point carrier = new(body.X + 4, body.Y - 1);
        var before = new Dictionary<Point, float>();
        for (int x = body.X - 10; x <= body.X + 20; x++)
            for (int y = body.Y - 6; y <= body.Y; y++)
                if (light.MeasuredBrightnessAt(new Point(x, y)) is float value) before[new Point(x, y)] = value;
        Require(before.Count > 0, $"{what}: the scene must measure something before the carrier arrives");
        var darkBefore = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(!darkBefore.Unmeasured && darkBefore.DarkFraction > Weights.TorchRaiseDarkShare,
            $"{what}: the premise is a passage that reads dark with nobody carrying anything; {Show(darkBefore)}");

        // Now the same world with somebody standing in it holding a light, as the engine presents it.
        VerifyUsefulAssistance.WriteMeasuredLight(Window, (_, _) => .02f, carried: CarriedFrom(carrier, (r + g + b) / 3f));
        ForceRefresh(ctx);
        Require(Lighting.Brightness(carrier.X, carrier.Y) >= Weights.LightDarkBelow,
            $"{what}: premise: the carried light must light the passage as presented, or nothing below is being tested; "
            + $"engine {Lighting.Brightness(carrier.X, carrier.Y):0.000} at {carrier}");

        // Every tile measured before must read exactly what it read before, and none may become unknown. A value that
        // moved means carried light is being reported as the room's; a tile that went unknown is one placement would refuse
        // to light, which is how every pocket beside a torch stayed dark.
        int litByTheCarrier = 0;
        foreach (var (tile, was) in before)
        {
            float? now = light.MeasuredBrightnessAt(tile);
            Require(now is float,
                $"{what}: the world-light reading at {tile} became unknown when somebody walked in carrying a light; it read {was:0.0000} before");
            Require(Math.Abs(now!.Value - was) < 1e-4f,
                $"{what}: the world-light reading at {tile} changed from {was:0.0000} to {now.Value:0.0000} when somebody "
                + $"walked in carrying a light; the sense is reporting their light as the room's");
            if (Lighting.Brightness(tile.X, tile.Y) >= Weights.LightDarkBelow) litByTheCarrier++;
        }
        Require(litByTheCarrier > 0,
            $"{what}: the carrier must light some of the measured tiles as presented, or the row would pass against a sense "
            + $"that read the presented map; {before.Count} tiles measured");

        // And the passage still reads dark around the body, which is the behaviour the objective names: a
        // companion beside a player holding a torch must not conclude the place is lit.
        var darkAfter = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(!darkAfter.Unmeasured && darkAfter.DarkFraction > Weights.TorchRaiseDarkShare,
            $"{what}: a passage lit only by a light somebody is carrying through it must still read dark; "
            + $"before {Show(darkBefore)} after {Show(darkAfter)}");
        Console.WriteLine($"        {what}: {before.Count} tiles unchanged, {litByTheCarrier} of them lit by the carrier as presented, dark share "
            + $"{darkBefore.DarkFraction:0.000} -> {darkAfter.DarkFraction:0.000}");
    }

    /// <summary>
    /// What reading the world's own light costs on the scan of a large screen: its tiles plus the engine's margin on every side,
    /// a few torches among dark air and rock. The sense pays the copy taken between scan and blur on every scan, and the engine's
    /// own blur of it once per scan that something asks about. The cost is filed as two measures, the take and the blur, because
    /// they grow for different reasons. Until 24 September 2026 their sum was held under the whole tick's planning allowance,
    /// which is a pass line on the machine; a read that has become a tick's work is a step of an order of magnitude against the
    /// row's own history, which is what the scoreboard's comparison now catches.
    /// </summary>
    private static void MeasureTheWorldLightOnALargeScreen()
    {
        Scene((_, _) => .02f);
        // A 3840 by 2160 screen in tiles, with ProcessScan's 28-tile inflation on each side.
        Rectangle area = new(0, 0, 3840 / 16 + 56, 2160 / 16 + 56);
        object engine = VerifyUsefulAssistance.ColourEngine();
        LightMap scan = VerifyUsefulAssistance.EngineMap(engine, "_workingLightMap");
        scan.SetSize(area.Width, area.Height);
        var random = new Random(20260916);
        for (int x = 0; x < area.Width; x++)
            for (int y = 0; y < area.Height; y++)
            {
                scan.SetMaskAt(x, y, random.Next(3) == 0 ? LightMaskMode.Solid : LightMaskMode.None);
                scan[x, y] = random.Next(100) == 0 ? new Vector3(1f, .95f, .8f) : new Vector3(.02f);
            }
        VerifyUsefulAssistance.RestoreEngineDecay(VerifyUsefulAssistance.EngineMap(engine, "_activeLightMap"));
        const int scans = 30;
        int capturedBefore = WorldLight.Captures;
        double take = 0, blur = 0;
        for (int i = 0; i < scans; i++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            VerifyUsefulAssistance.TakeTheScan(engine, area);
            take += (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Require(WorldLight.Brightness(area.Width / 2, area.Height / 2) is float, "premise: the scan covers its own middle");
            blur += WorldLight.LastBlurMs;
        }
        Require(WorldLight.Captures - capturedBefore == scans, $"premise: every scan is taken once; taken {WorldLight.Captures - capturedBefore} of {scans}");
        string scene = $"a {area.Width}x{area.Height}-tile scan (a 3840x2160 screen plus the engine's margin), a third rock and one tile in a hundred a torch, mean over {scans} scans";
        EmitTimingMeasures.Timing("world light: taking the scan, per scan on a large screen", take / scans, "the copy the sense takes between the engine's scan and its blur; " + scene);
        EmitTimingMeasures.Timing("world light: blurring the scan, per scan on a large screen", blur / scans, "the engine's own LightMap.Blur over that copy; " + scene);
        Console.WriteLine($"        world light on a {area.Width}x{area.Height} scan: {take / scans:0.000} ms to take and {blur / scans:0.000} ms to blur, per scan (this machine)");
    }

    // ---- (b) a dark wing is offered, and its site is Reachable rather than round-tripped ---------------

    private static void ADarkWingIsOfferedWithAReachableSite()
    {
        // Lit around the body, dark from twenty columns out — a wing, not a pocket, so the nearest dark region
        // is unambiguously away from the feet and well inside the work radius.
        var ctx = Scene((x, y) => x < 40 ? .8f : .02f);
        GiveTorches(ctx);
        var brain = ctx.Companion.Brain;
        Settle(ctx);

        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(score > 0 && action.Eligibility == Offer.Usable && action.ActivityTarget is not null,
            $"a dark wing inside the work radius must be a lighting job; score={score:0.000} {action.Eligibility}:{action.EligibilityReason}");

        Point site = action.ActivityTarget!.Value.ToTileCoordinates();
        Require(site.X >= 40, $"the offered site {site} is inside the lit half, so the field read the wrong half dark");
        Require(site.X * 16f - ctx.Npc.Center.X <= Weights.FollowWorkRadius,
            $"the offered site {site} sits outside the work radius the nomination is bounded to");

        // The site is admitted through the reach sense rather than through a round trip of its own, so the
        // destination the positioner resolves for it must be one the sense itself calls Reachable — not
        // NotYet, which is the answer a flood that has not settled gives and which lighting must refuse.
        // The destination is the course's: the census's site in the dark wing, bound by the real binder, and the
        // request `ExecuteCourseBinding` makes of that step — the activity's own `Execute` names no destination
        // since the course took the body.
        var step = VerifyAssistanceTrips.BindCensusSite(ctx, "light-target",
            // A light site's target is `tile:x,y`, the tile the torch goes on.
            o => int.Parse(o.Key.Target["tile:".Length..].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture) >= 40, "in the dark wing");
        var request = live::AICompanion.Companion.Brain.Infrastructure.Selection.ExecuteCourseBinding.RequestFor(step, ctx.Npc.Center);
        Vector2? destination = brain.Positioner.Resolve(request, brain.Senses);
        Require(destination is { } stand && brain.Senses.Reach.Reachable(MovementQueries.Tile(stand)) == ReachVerdict.Reachable,
            $"the lighting destination must be a tile the reach sense calls Reachable; destination={destination} "
            + $"verdict={(destination is { } d ? brain.Senses.Reach.Reachable(MovementQueries.Tile(d)).ToString() : "none")} "
            + $"complete={brain.Senses.Reach.Complete}");
    }

    // ---- (b) nearer darkness nobody can reach does not hide the darkness somebody can ------------------

    /// <summary>
    /// The acceptance row for the defect this whole lane exists to remove. A sealed chamber of dark air sits
    /// ten tiles from the companion, and the only darkness it can actually work is nearly thirty tiles further
    /// on. Lighting must offer the far one, on the first search, and it must do it without ever having been
    /// told how many sites it is allowed to ask about.
    ///
    /// <para>Before the reach sense answered the approach, each of those questions was a fresh bounded A* per
    /// working pose, so the search could afford three sites a rescore. Three sites nearest-first are three
    /// chamber sites; a bounded A* that runs out of expansions answers Unknown rather than No; and an Unknown
    /// is correctly not remembered, because remembering one writes a site off on the strength of a search that
    /// never finished. So the same three were asked every retry, the answer never arrived, and the offer read
    /// <c>site-budget-spent-before-an-answer</c> — on 11,880 of the 15,105 rows of the 2026-09-14 capture, with
    /// a settled flood sitting beside 7,003 of them holding the answer. The companion stood beside dark
    /// reachable passages and lit none of them.</para>
    ///
    /// <para>The row asserts the offer string rather than only the score, because "something was offered" is
    /// satisfied by any of the four exits and the distinction between them is the entire finding.</para>
    /// </summary>
    private static void NearerUnreachableDarknessDoesNotHideAReachableSite()
    {
        // A cavity carved inside a block of rock under the walking floor, and darkness again out past the far
        // column. Sealed rock rather than a pit in the floor, deliberately: a pit is also a hole in the only
        // path to the far darkness, and the row would then assert that an unreachable site is skipped while
        // quietly making the reachable one unreachable too — a premise that passes for the wrong reason.
        const int CaveLeft = 30, CaveRight = 41, CaveTop = 64, CaveBottom = 76, FarDark = 52;
        var ctx = Scene((x, y) =>
            (x >= CaveLeft && x <= CaveRight && y >= CaveTop && y <= CaveBottom) || x >= FarDark ? .02f : .8f);
        GiveTorches(ctx);
        // Solid ground from just under the floor down past the cavity, then the cavity carved out of it. The
        // rock around it is what makes the row work at all: a site is refused unless its whole neighbourhood
        // reads dark, and a neighbourhood is a mean over *open air* only, so rock is excluded while open lit
        // air a few tiles away is not. A first attempt hung a thin-shelled chamber in open air, and every
        // site inside it read lit through its own shell — the search asked one site, the far one, and the row
        // passed while proving nothing about hiding.
        for (int x = CaveLeft - 6; x <= CaveRight + 6; x++)
            for (int y = FloorRow + 1; y <= CaveBottom + 6; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        for (int x = CaveLeft; x <= CaveRight; x++)
            for (int y = CaveTop; y <= CaveBottom; y++)
                Main.tile[x, y].ClearEverything();
        TerrainChanges.Reset();
        // Thrown away and flooded again: the shell went up after the scene's own setup had flooded an open
        // floor, and a loop that runs while the region is incomplete does nothing when the stale region is
        // complete.
        VerifyOreWork.ResettleReach(ctx);
        ForceRefresh(ctx);

        // The premise, asserted rather than assumed, because every claim this row makes rests on it: the
        // chamber is nearer, the chamber is proven unreachable, and the far floor is proven reachable. A
        // scene that failed any of these would let the row pass while testing nothing.
        var reach = ctx.Companion.Brain.Senses.Reach;
        Point insideChamber = new((CaveLeft + CaveRight) / 2, CaveBottom - 1);
        Point farStand = new(FarDark + 10, StandRow);
        Require(reach.Complete, "this row needs a settled region, or unreachable and not-yet-known read alike");
        Require(reach.Reachable(insideChamber) == ReachVerdict.Unreachable,
            $"premise: the cavity must be proven unreachable, not merely unvisited; got {reach.Reachable(insideChamber)}");
        Require(reach.Reachable(farStand) == ReachVerdict.Reachable,
            $"premise: the far floor must be reachable, or there is no right answer to offer; got {reach.Reachable(farStand)}");
        float toChamber = Vector2.Distance(ctx.Npc.Bottom, insideChamber.ToWorldCoordinates());
        float toFar = Vector2.Distance(ctx.Npc.Bottom, farStand.ToWorldCoordinates());
        Require(toChamber < toFar,
            $"premise: the unreachable darkness must be the nearer one, or nothing is being hidden; chamber={toChamber:0} far={toFar:0}");

        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        string offer = $"score={score:0.000} offer={action.Eligibility}/{action.EligibilityReason} target={action.ActivityTarget}";
        Require(action.Eligibility == Offer.Usable && action.EligibilityReason == "reachable-interaction",
            $"the first search must reach a site it can work rather than stopping on an unfinished question; {offer}");
        Require(score > 0 && action.ActivityTarget is not null, $"a usable lighting offer must carry a value and a target; {offer}");
        Point site = action.ActivityTarget!.Value.ToTileCoordinates();
        Require(site.X >= FarDark,
            $"the offered site must be the reachable darkness, not a chamber tile the body cannot get to; site={site}; {offer}");
        Require(reach.Reachable(MovementQueries.Tile(ctx.Companion.Brain.Positioner.Resolve(action.Execute(ctx), ctx.Companion.Brain.Senses)
                ?? ctx.Npc.Center)) == ReachVerdict.Reachable,
            $"the destination lighting resolves for that site must itself be reachable; {offer}");
        // The ledger the recorder writes must name what was asked, or the next capture is as unreadable as the
        // last one. Every chamber site asked has to appear as unreachable: a ledger that only recorded the
        // site finally chosen would say nothing about the ones that were skipped, which is the whole question.
        Require(action.LastSearchAsked > 0 && action.LastSearchSites.Contains("unreachable"),
            $"the search ledger must record the refused sites it walked past; asked={action.LastSearchAsked} sites={action.LastSearchSites}");
    }

    // ---- (c) the region is worked, not visited ---------------------------------------------------------

    /// <summary>
    /// The other half of the objective: after a torch goes in, lighting re-nominates from where the body now
    /// stands and keeps going. The scene is a dark floor with only lighting and keeping company registered, so
    /// the body has exactly two things it can be doing, and the measurement is the number of ticks keeping
    /// company owns between the first torch landing and the second site being named. Placing a torch does not
    /// recompute the presented light map headlessly, so the region stays dark and a second site exists.
    /// </summary>
    private static void TwoSitesAreWorkedWithoutReturning()
    {
        var ctx = Scene((_, _) => .02f);
        GiveTorches(ctx);
        Settle(ctx);
        var brain = ctx.Companion.Brain;
        brain.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");

        int first = -1, second = -1, companyBetween = 0;
        var courseReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        string trace = "";
        var placed = new List<Point>();
        double decideMax = 0, decideTotal = 0;
        int ticks = 0;
        for (int tick = 0; tick < 1800 && second < 0; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            decideMax = Math.Max(decideMax, brain.DecideMs);
            decideTotal += brain.DecideMs;
            ticks++;
            foreach (Point t in TorchTiles())
                if (!placed.Contains(t))
                {
                    placed.Add(t);
                    if (first < 0) { first = tick; trace += $"torch 1 at {t} tick {tick}; "; }
                    else if (second < 0) { second = tick; trace += $"torch 2 at {t} tick {tick}; "; }
                }
            // Counted as "not still lighting" rather than as "keeping company": a broken continuation that
            // yields nothing at all publishes no action and would pass a test looking for the other name.
            if (first >= 0 && second < 0 && brain.LastAction?.Name != "place-torches") companyBetween++;
            AICompanion.Tools.EngineReplay.Observation.DescribeCourseFunnel.Count(courseReasons, brain.Course);
        }
        // Chaining a second site is the retained course's headline capability, so when it does not happen
        // the question is which of three things went wrong — the second site was never admitted, it was
        // admitted and the two-site order lost on price, or it won and the body never arrived — and the
        // funnel is the only thing that separates them. Printed before the first Require, because these
        // rows throw and a diagnosis after the throw is a diagnosis nobody sees.
        AICompanion.Tools.EngineReplay.Observation.DescribeCourseFunnel.Print("two-sites", brain.Course, courseReasons);
        // Where the body actually got to, against where the course is asking it to be. A step nothing
        // executes and a step the body is still flying towards look the same in the course's own state.
        Console.WriteLine($"  two-sites body: centre={ctx.Npc.Center} lastAction={brain.LastAction?.Name ?? "none"} request={brain.LastRequest.Kind}");
        Require(first >= 0, $"the lighting job must place a first torch on a dark floor; {trace}placed={placed.Count}");
        Require(second >= 0,
            $"lighting must keep the job after placing and place a second torch in the same region; {trace}placed={placed.Count}");
        Require(companyBetween == 0,
            $"the companion must not stop lighting between torches; {companyBetween} of the ticks between tick {first} and tick {second} were not place-torches; {trace}");
        // This is the one scene in the suite where the region scan actually runs — a floor that is dark
        // everywhere, so every rescore nominates a region and scans around each of its members. The brain-cost
        // harness cannot price it, because its scene presents no light map at all and lighting's search returns
        // before it looks at anything. These numbers describe this machine and are never asserted.
        Console.WriteLine($"        two torches at ticks {first} and {second}, {placed.Count} placed, no keeping-company tick between them; "
            + $"decide over a wholly dark floor max {decideMax:0.000} ms, mean {decideTotal / Math.Max(1, ticks):0.000} ms over {ticks} ticks");
        // With the allowances lifted this times the whole decision as an unbounded search over every usable
        // site, so it measures how much work the course finds as much as what each unit costs: while the
        // census published sites the placer would refuse the search priced three orders and read 37-39 ms,
        // and with the census fixed it prices about 205 and reads around 1830 ms. It was a 120 ms ceiling
        // until 21 September 2026 for that reason, and it is a measure now. The regime production enters is
        // the row below; this one says how far the search would run with nothing stopping it.
        Console.WriteLine($"        MEASURE deciding over a wholly dark floor with the planning allowances lifted: "
            + $"max {decideMax:0.000} ms, mean {decideTotal / Math.Max(1, ticks):0.000} ms over {ticks} ticks");
        EmitTimingMeasures.Timing("dark floor, allowances lifted: worst decide on one tick", decideMax,
            $"place-torches and keep-company only, {ticks} ticks until the second torch landed; an unbounded search over every usable site");
    }

    /// <summary>
    /// A measurement, not an assertion. A screen that is dark everywhere is the worst input the region scan
    /// has: every rescore nominates a region and scans a box around every one of its members. The case above
    /// runs with the allowances lifted, which prices the scan with nothing stopping it; this one runs the same
    /// scene under the allowances the live tick actually applies, which is the number that says whether the
    /// tick's planning allowance bounds the decision at all.
    /// </summary>
    private static void MeasureTheRegionScanUnderProductionAllowances()
    {
        bool lifted = LimitPlanningWork.Unbounded;
        LimitPlanningWork.Unbounded = false;
        try
        {
            var ctx = Scene((_, _) => .02f);
            GiveTorches(ctx);
            var brain = ctx.Companion.Brain;
            brain.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");
            double decideMax = 0, decideTotal = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                VerifyOreWork.AdvanceBrain(ctx);
                decideMax = Math.Max(decideMax, brain.DecideMs);
                decideTotal += brain.DecideMs;
            }
            Console.WriteLine($"        dark floor under production allowances: decide max {decideMax:0.000} ms, mean {decideTotal / 600:0.000} ms "
                + "over 600 ticks (this machine)");
            // The regression this exists to show was found by a person reading a printed number: a cheaper
            // proof deleted the loop's bound and one preparation reached 37.6 ms against a twelve-millisecond
            // allowance. It held twice the tick's planning allowance as a pass line until 24 September 2026,
            // and the same code measured 7.8 ms in the warmed suite and 13.0 ms alone, so the line sat between
            // two regimes of the harness. A lost bound is a step of several times against this figure's own
            // history, which is what the scoreboard's comparison now catches without a number written here.
            string scene = $"place-torches and keep-company only, 600 ticks on a wholly dark floor; the tick's planning allowance is {Weights.TotalPlanningMilliseconds:0.###} ms";
            EmitTimingMeasures.Timing("dark floor under production allowances: worst decide on one tick", decideMax, scene);
            EmitTimingMeasures.Timing("dark floor under production allowances: mean decide per tick", decideTotal / 600, scene);
        }
        finally { LimitPlanningWork.Unbounded = lifted; }
    }

    // ---- (d) following always has somewhere to go ------------------------------------------------------
    //
    // This section's one row is gone with the fallback it was about. A player on a shelf hanging in the air
    // left the walker's candidate list empty, because every candidate had to be a tile a body could stand on
    // and be flooded to, and the positioner answered with a partial-progress tile plus a success region of
    // its own. The orb's positioner has no such fallback — a follow request that accepts nothing answers
    // null, and the brain closes the gap through `SeekDestination` aiming at the anchor itself — so
    // `SuccessRegionKind.PartialProgress` and the `partial-progress-candidate` reason no longer exist to
    // assert against.
    //
    // NOT REPLACED, and worth saying rather than leaving as a silence: the orb-shaped version of this row is
    // a follow scene where every free cell near the player is above the positioner's ceiling, so `Best`
    // returns null and the body must still reach the anchor. It is not written here.

    // ---- (e) knowledge is invalidated where it happened -------------------------------------------------

    /// <summary>
    /// A retained route search keeps its frontier across an edit it never read, and loses it across one it
    /// did. Before this, the compare was one world-global counter, so a player breaking a tile anywhere in
    /// the loaded world discarded every frontier in the brain; the 2026-09-14 capture's navigator restarted
    /// one query 166 times in 3,000 ticks and never struck.
    ///
    /// <para>The edits are announced rather than made, which is the whole surface under test: invalidation
    /// keys on what the game announces, and announcing without editing keeps the scene identical for the
    /// rows that follow.</para>
    /// </summary>
    private static void ARetainedSearchSurvivesAnEditItNeverRead()
    {
        var ctx = Scene(null);
        Point body = MovementQueries.Tile(ctx.Npc.Center);
        var search = Flood(ctx);
        for (int i = 0; i < 40; i++) search.Advance(Weights.ReachFloodExpansions);
        Rectangle bounds = search.ExploredBounds;
        Require(search.Valid && search.Expansions > 0 && bounds.Height > 0,
            $"the row needs a live query with a region behind it; valid={search.Valid} expansions={search.Expansions} bounds={bounds}");

        // Below the floor, not above it. A flying body's flood fills the open sky up to the world margin, so
        // the rows above the floor are inside the region it read; the free space it never reads is under the
        // floor. Sideways is no good either, because this world is a hundred tiles wide and the flood runs the
        // whole open floor, so no column is far from it — which is the honest limit of this lane, and is why
        // the boundary row below pins the margin rather than assuming a wide one.
        int farOutside = bounds.Bottom + 5;
        Require(farOutside < Main.maxTilesY && farOutside > bounds.Bottom,
            $"the fixture world must leave room below the flood to place a distant edit; bottom={bounds.Bottom}");
        int reachedBefore = search.Reached.Count;
        TerrainChanges.Changed(body.X, farOutside);
        Require(search.Valid,
            $"an edit {farOutside - bounds.Bottom} rows below everything the query read must leave it valid; bounds={bounds} edit={body.X},{farOutside}");
        Require(search.Reached.Count == reachedBefore && search.Stop == FreeSpaceSearch.StopReason.Exhausted,
            $"the answer a surviving query already has must survive with it; reached {reachedBefore} -> {search.Reached.Count} stop={search.Stop}");

        // The same thing said about a query that has not finished, because "keeps its answer" and "goes on
        // working" are two claims and this room is small enough to exhaust in one advance at full budget.
        var unfinished = Flood(ctx);
        unfinished.Advance(1);
        Require(!unfinished.Finished, $"a one-unit advance must leave work to do, or the row below asserts nothing; stop={unfinished.Stop}");
        int expansionsBefore = unfinished.Expansions;
        TerrainChanges.Changed(body.X, farOutside);
        Require(unfinished.Valid, "an unfinished query must survive a distant edit too");
        for (int i = 0; i < 20; i++) unfinished.Advance(1);
        Require(unfinished.Expansions > expansionsBefore,
            $"a query that survived a distant edit must go on expanding; {expansionsBefore} -> {unfinished.Expansions}");

        // Inside: a tile in the middle of what the flood actually closed.
        TerrainChanges.Changed(bounds.Center.X, bounds.Center.Y);
        Require(!search.Valid, $"an edit inside the region the query flooded must invalidate it; bounds={bounds}");
        Require(!search.Valid, "invalidity must be remembered rather than re-derived against a moving counter");
    }

    /// <summary>A goal-less flood from the corner nearest the body, which is what the reach sense runs and what
    /// every row in this group retains across an edit.</summary>
    private static FreeSpaceSearch Flood(ActionContext ctx)
    {
        var world = MovementQueries.World;
        Point? root = CornerGraph.NearestUsable(world, ctx.Npc.Center, 2, requireSweep: false);
        Require(root != null, $"the scene must leave the body somewhere it fits; centre={ctx.Npc.Center}");
        return new FreeSpaceSearch(world, root!.Value, null);
    }

    /// <summary>
    /// The margin is the flood's own explored bounds — the corners it closed, inflated by the tile a swept
    /// edge of the body's radius can read into — and this row pins it to exactly that, one row inside and one
    /// row outside. Both halves are asserted so a later tidy-up cannot shrink the margin quietly, and the
    /// looser half matters as much, because a margin that never lets anything survive is this whole lane
    /// doing nothing while every row about invalidation still passes.
    /// </summary>
    private static void TheMarginIsTheScansOwnReach()
    {
        var ctx = Scene(null);
        Point body = MovementQueries.Tile(ctx.Npc.Center);

        var atTheMargin = Flood(ctx);
        for (int i = 0; i < 40; i++) atTheMargin.Advance(Weights.ReachFloodExpansions);
        Rectangle margin = atTheMargin.ExploredBounds;
        TerrainChanges.Changed(body.X, margin.Top);
        Require(!atTheMargin.Valid,
            $"an edit on the topmost row the flood's own bounds claim must invalidate it; bounds={margin}");

        var oneBeyond = Flood(ctx);
        for (int i = 0; i < 40; i++) oneBeyond.Advance(Weights.ReachFloodExpansions);
        Rectangle beyond = oneBeyond.ExploredBounds;
        Require(beyond.Top == margin.Top, $"both halves must flood the same region, or they pin different margins; {margin} vs {beyond}");
        TerrainChanges.Changed(body.X, beyond.Top - 1);
        Require(oneBeyond.Valid,
            $"an edit one row past the flood's own bounds touches nothing it read and must leave it valid; bounds={beyond}");
    }

    /// <summary>
    /// A revision older than the record reaches back is treated as changed. The record is a bounded ring, so
    /// a query nobody asks about while a window's worth of edits lands anywhere in the world can no longer be
    /// told whether any of them were its own — and the only safe answer there is that it was. The row drives
    /// the real query rather than the log alone, because the thing that must not regress is a caller being
    /// handed a silent Unchanged for a gap nothing was recorded over.
    /// </summary>
    private static void ARevisionOlderThanTheRecordIsChanged()
    {
        var ctx = Scene(null);
        Point body = MovementQueries.Tile(ctx.Npc.Center);
        var search = Flood(ctx);
        for (int i = 0; i < 40; i++) search.Advance(Weights.ReachFloodExpansions);
        Rectangle bounds = search.ExploredBounds;
        // Below the floor, for the same reason as the row above: a flying body's flood fills the sky, so the
        // free space it never read is under the ground rather than over it.
        int farOutside = bounds.Bottom + 5;
        Require(farOutside < Main.maxTilesY && farOutside > bounds.Bottom,
            $"the fixture world must leave room below the flood; bottom={bounds.Bottom}");

        // Not one call to Valid in between: the query's own revision only moves forward when it is asked, so
        // this is exactly the case the window exists to fail safely on.
        for (int i = 0; i <= TerrainEditLog.Window; i++) TerrainChanges.Changed(body.X, farOutside);
        Require(!search.Valid,
            $"a query unasked across {TerrainEditLog.Window + 1} edits has fallen off the record's window and must read invalid rather than clean");

        // And the asked query outlives the same run of distant edits, which is what makes the row above a
        // statement about the window rather than about distance.
        var asked = Flood(ctx);
        for (int i = 0; i < 40; i++) asked.Advance(Weights.ReachFloodExpansions);
        Rectangle askedBounds = asked.ExploredBounds;
        int died = -1;
        for (int i = 0; i <= TerrainEditLog.Window * 2; i++)
        {
            TerrainChanges.Changed(body.X, farOutside);
            if (!asked.Valid) { died = i; break; }
        }
        Require(asked.Valid,
            $"a query asked between edits advances its own revision and must survive any number of distant ones; "
            + $"window={TerrainEditLog.Window} died at edit {died} of {TerrainEditLog.Window * 2} "
            + $"edit={body.X},{farOutside} askedBounds={askedBounds} searchBounds={bounds}");
    }

    /// <summary>
    /// The reach flood itself: a distant edit costs no reflood, an edit it read costs one, and the region it
    /// grows afterwards holds the edit. The flood is the consumer the 13:27 capture measured — unfinished on
    /// 9,812 of 22,473 rows — and the world-global restart is one of the two causes named for it.
    /// </summary>
    private static void TheReachFloodRefloodsOnlyForAnEditItRead()
    {
        var ctx = Scene(null);
        var brain = ctx.Companion.Brain;
        var request = new PositionRequest(RequestKind.WithPlayer, ctx.Player.Bottom);
        // The scene's own setup floods before it resets the terrain, so the region standing here is complete
        // and was built under an earlier revision: settling on "resolve while incomplete" would return at
        // once and every reflood counted below would be a count against a flood from the previous world.
        // This is the trap this folder's own file names, and it is what the first version of this row hit.
        VerifyOreWork.ResettleReach(ctx);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++) brain.Positioner.Resolve(request, brain.Senses);
        Require(brain.Positioner.ReachComplete, "the flood must settle before a reflood can be counted against it");

        Point feet = MovementQueries.Tile(ctx.Npc.Center);
        Point beyond = new(feet.X + 30, StandRow);
        Require(brain.Senses.Reach.Reachable(beyond) == ReachVerdict.Reachable,
            $"the row needs a settled region holding a tile it can later lose; verdict={brain.Senses.Reach.Reachable(beyond)}");

        int refloodsBefore = brain.Senses.Reach.Refloods;
        // Outside the world's own margin, so it is a row no flood over free cells can have read.
        TerrainChanges.Changed(feet.X, 2);
        for (int i = 0; i < 40; i++) brain.Positioner.Resolve(request, brain.Senses);
        Require(brain.Senses.Reach.Refloods == refloodsBefore,
            $"an edit forty rows above everything the flood read must cost no reflood; {refloodsBefore} -> {brain.Senses.Reach.Refloods}");
        Require(brain.Positioner.ReachComplete && brain.Senses.Reach.Reachable(beyond) == ReachVerdict.Reachable,
            "a region that survived a distant edit keeps both its completeness and its membership");

        // A wall across the floor the flood walked, carried up to the world's own margin, because a body that
        // flies leaves the region only where the column is sealed to the top. The edit is real here because
        // this half is about membership rather than about the compare.
        int wall = feet.X + 12;
        for (int y = StandRow; y >= 5; y--)
        {
            VerifyOreWork.Place(new Point(wall, y), TileID.Dirt);
            TerrainChanges.Changed(wall, y);
        }
        // One resolve, so the sense is asked before the count is read: the discard happens inside Refresh and
        // Refresh runs on a resolve, which is the same clock the whole flood keeps.
        brain.Positioner.Resolve(request, brain.Senses);
        Require(brain.Senses.Reach.Refloods > refloodsBefore,
            $"an edit on the floor the flood walked must throw the flood away; refloods={brain.Senses.Reach.Refloods}");
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++) brain.Positioner.Resolve(request, brain.Senses);
        Require(brain.Positioner.ReachComplete, "the refloods region must settle again before its membership is read as final");
        Require(brain.Senses.Reach.Reachable(beyond) == ReachVerdict.Unreachable,
            $"the region grown after the wall must not still hold the floor behind it; verdict={brain.Senses.Reach.Reachable(beyond)}");
    }

    // Two rows in this group are gone with the machinery they were about, and both are worth naming rather
    // than leaving as a gap in the lettering.
    //
    // The adopted-suffix row held a window in the walker's route archive: a search could copy a remembered
    // route suffix out of `RememberExecutedRoutes`, and terrain under that suffix had to be tested from the
    // moment it was copied rather than from the moment it was adopted. There is no archive now — the orb
    // plans from the corner graph every time and remembers no route between searches — so a search depends
    // only on the corners it actually closed, which is exactly what `FreeSpaceSearch.ExploredBounds` covers
    // and what the three rows above already pin.
    //
    // The raw-flood row held the seam between two floods: a raw one that allowed edges with no way back and
    // a scored one that did not, diverging by construction so that an edit could invalidate one alone. The
    // reach sense runs one flood now, because for a body that flies the outward and return questions are the
    // same flood read in one direction, and there is no second search for a stale region to be folded in
    // from. `ReachSense.ReachableOneWay` is `Returnable` in the source, which is that collapse written down.


    /// <summary>
    /// A door toggled by anything at all announces its rows. The game's door helpers write tiles through
    /// neither the placement nor the destruction hook, so before the detours the companion's own toggles
    /// were exact and a player's, a town NPC's or a wire's were silent.
    ///
    /// <para>The row is built as its own mutation, because the two halves are the same scene and the same
    /// call: without the detour installed the door opens and the retained query that read the doorway stays
    /// valid, which is the defect stated rather than described; with it installed the same call invalidates
    /// it. A row asserting only the second half would pass against a query that invalidates on anything.</para>
    ///
    /// <para>The hooks are installed by the fixture rather than by the mod, because <c>ModSystem.Load</c>
    /// never runs in this host — the same absence <c>VerifyOreWork.InitialiseVanillaTileHooks</c> fills for
    /// the tile hook arrays — and they are removed in a finally, because a detour left installed changes the
    /// revision counts of every door fixture that runs after this one.</para>
    /// </summary>
    private static void ADoorToggledByAnybodyAnnouncesItsRows()
    {
        // Restored on the way out with the hooks. The headless tile table seeds almost nothing, so a row
        // that needs a door has to state the live game's values for these two — and a row that leaves them
        // stated is a static leaking into every row after it, which is the failure this folder's own file
        // names twice. Inert today only because the door suite sets its own.
        bool closedWasSolid = Main.tileSolid[TileID.ClosedDoor], openWasSolid = Main.tileSolid[TileID.OpenDoor];
        Main.tileSolid[TileID.ClosedDoor] = true;
        Main.tileSolid[TileID.OpenDoor] = false;

        (FreeSpaceSearch Search, int Revision, Point Door) Settled()
        {
            var ctx = Scene(null);
            Point feet = MovementQueries.Tile(ctx.Npc.Center);
            Point door = new(feet.X + 12, StandRow - 2);
            // From the top of the world, not from a comfortable height: a wall that stops short is a wall a
            // flying body goes over, and the premise below then reads the far side as reached for a reason
            // that has nothing to do with the door.
            for (int row = 0; row < door.Y; row++) VerifyOreWork.Place(new Point(door.X, row), TileID.Dirt);
            for (int row = 0; row < 3; row++)
            {
                Tile tile = Main.tile[door.X, door.Y + row];
                tile.ClearEverything();
                tile.HasTile = true;
                tile.TileType = TileID.ClosedDoor;
                tile.TileFrameX = 0;
                tile.TileFrameY = (short)(row * 18);
            }
            TerrainChanges.Reset();
            var search = Flood(ctx);
            for (int i = 0; i < 200; i++) search.Advance(Weights.ReachFloodExpansions);
            // The flood closes corners, so tile membership is "any of this tile's four corners was closed",
            // which is the same reading the reach sense publishes.
            bool near = CornerGraph.AnyCornerOf(new Point(door.X - 1, StandRow), search.Reached.Contains);
            bool far = CornerGraph.AnyCornerOf(new Point(door.X + 1, StandRow), search.Reached.Contains);
            Require(near && !far,
                $"premise: the closed door must be the wall this query's region stops at; door={door} near={near} far={far}");
            return (search, TerrainChanges.Revision, door);
        }

        try
        {
            TerrainChanges.RemoveDoorHooks();
            var silent = Settled();
            Require(WorldGen.OpenDoor(silent.Door.X, silent.Door.Y + 1, 1),
                "premise: the native helper must open this door");
            Require(TerrainChanges.Revision == silent.Revision && silent.Search.Valid,
                "the defect, stated: with nothing detouring the door helper the world changes under a "
                + "retained query and the query never hears about it");

            TerrainChanges.InstallDoorHooks();
            var opened = Settled();
            Require(WorldGen.OpenDoor(opened.Door.X, opened.Door.Y + 1, 1),
                "premise: the native helper must open this door with the detour in");
            Require(TerrainChanges.Revision != opened.Revision,
                "a door opened by anybody must reach the edit record");
            Require(!opened.Search.Valid,
                "a query whose region stopped at that doorway must be invalidated by the opening");

            var closed = Settled();
            Require(WorldGen.OpenDoor(closed.Door.X, closed.Door.Y + 1, 1), "premise: opened before closing");
            int afterOpen = TerrainChanges.Revision;
            Require(WorldGen.CloseDoor(closed.Door.X, closed.Door.Y + 1, true),
                "premise: the native helper must close it again");
            Require(TerrainChanges.Revision != afterOpen,
                "closing announces as well as opening: a door shut across the only corridor is the case a "
                + "retained route survives longest under a spatial rule");
        }
        finally
        {
            TerrainChanges.RemoveDoorHooks();
            Main.tileSolid[TileID.ClosedDoor] = closedWasSolid;
            Main.tileSolid[TileID.OpenDoor] = openWasSolid;
        }
    }

    // ---- scene ----------------------------------------------------------------------------------------

    private static int MaxColumn(IReadOnlyCollection<Point> tiles, int row)
    {
        int max = int.MinValue;
        foreach (Point tile in tiles)
            if (tile.Y == row && tile.X > max) max = tile.X;
        return max;
    }

    /// <summary>The ore-work floor with the ore far away and no supplies, presenting <paramref name="light"/>
    /// over the window, or nothing at all when it is null.</summary>
    private static ActionContext Scene(Func<int, int, float>? light)
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(90, StandRow));
        Main.tile[90, StandRow].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        ctx.Player.selectedItem = 1;
        if (light == null) VerifyUsefulAssistance.ClearMeasuredLight();
        else VerifyUsefulAssistance.WriteMeasuredLight(Window, light);
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
    }

    private static bool InChamber(int x, int y) => Math.Abs(x - 20) <= 14 && Math.Abs(y - StandRow) <= 14;

    /// <summary>Observes the world and makes the field resample, rather than serve the samples it already
    /// holds. The field refreshes on a cadence, so two `Senses.Update` calls in one scene read the world once;
    /// a row that changes the light between them and does not force this is comparing a scene with itself.</summary>
    private static void ForceRefresh(ActionContext ctx)
    {
        // Not int.MaxValue: the gate is `++sinceRefresh < RefreshTicks`, so the increment overflows to
        // int.MinValue and the sense returns early — the exact opposite of forcing a refresh, and silent.
        var sense = ctx.Companion.Brain.Senses.Light;
        // The engine clock advances too, so `ReadTick` can witness the resample. Without that the stamp is
        // the same game tick either way and the check below cannot tell a refresh from an early return.
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        ulong? before = sense.ReadTick;
        typeof(LightSense).GetField("sinceRefresh", InstanceField)!.SetValue(sense, 1000);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        Require(sense.ReadTick != before,
            "forcing a refresh must actually resample the world, or every row built on it compares a scene with itself");
    }

    /// <summary>The retired <c>Ambient</c> scalar, recomputed here from the engine rather than from our own
    /// code: one mean brightness over every open-air tile of the window. A scene where this disagrees with the
    /// field is a scene the old code gets wrong.</summary>
    private static float OldWindowMean()
    {
        float total = 0f;
        int n = 0;
        for (int x = Window.Left; x < Window.Right; x++)
            for (int y = Window.Top; y < Window.Bottom; y++)
            {
                if (!LightSense.IsOpenAir(x, y)) continue;
                if (!LightSense.Coverage.Current().Contains(x, y)) continue;
                total += Lighting.Brightness(x, y);
                n++;
            }
        return n == 0 ? 0f : total / n;
    }

    private static void Settle(ActionContext ctx)
    {
        var brain = ctx.Companion.Brain;
        var home = new PositionRequest(RequestKind.WithPlayer, ctx.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses);
        Require(brain.Positioner.ReachComplete, "these scenes need a settled reach region before preparing");
    }

    private static void GiveTorches(ActionContext ctx)
    {
        Item supply = new();
        supply.SetDefaults(ItemID.Torch);
        supply.stack = 20;
        ctx.Player.inventory[0] = supply;
    }

    private static IEnumerable<Point> TorchTiles()
    {
        for (int x = Window.Left; x < Window.Right; x++)
            for (int y = 30; y < FloorRow; y++)
                if (Main.tile[x, y].HasTile && TileID.Sets.Torch[Main.tile[x, y].TileType])
                    yield return new Point(x, y);
    }

    /// <summary>Drives the real torch past its minimum hold and answers with the hand it settled on and the
    /// reason given on the tick it settled. Reading <c>Reason</c> after the run instead would read whichever
    /// hold restarted afterwards, which is a fact about the damping rather than about the decision.</summary>
    private static (bool Lit, string Reason) Settles(ActionContext ctx, LightSense light, bool startLit, Vector2 heading)
    {
        var torch = ctx.Companion.Torch;
        typeof(TorchBearer).GetProperty("Lit")!.GetSetMethod(true)!.Invoke(torch, new object[] { startLit });
        typeof(TorchBearer).GetField("sinceChange", InstanceField)!.SetValue(torch, 0);
        string reason = "never-updated";
        for (int i = 0; i <= 181; i++)
        {
            bool was = torch.Lit;
            torch.Update(light, ctx.Npc, handFree: true, heading);
            if (torch.Lit != was || i == 181) reason = torch.Reason;
            if (torch.Lit != was) break;
        }
        return (torch.Lit, reason);
    }

    private static string Show(LightSense.DarkReading r)
        => r.Unmeasured ? "unmeasured" : $"measured={r.Measured} dark={r.Dark} share={r.DarkFraction:0.000} mean={r.MeanBrightness:0.000}";

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
