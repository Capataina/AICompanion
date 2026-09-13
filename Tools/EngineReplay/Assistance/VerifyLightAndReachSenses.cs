extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
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
using TransientLights = live::AICompanion.Companion.Brain.Infrastructure.Observation.TransientLights;
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
/// <item>the companion's own shown torch cannot make its neighbourhood read lit, because the engine merges
/// lights by maximum and the field removes its own by the same inverse</item>
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
        Each("a: the light sense reads the torch the map was written with, through the real AI tick", TheSenseReadsTheTorchTheMapWasWrittenWith);
        Each("a: a carried torch does not blind the placement search that would replace it", ACarriedTorchDoesNotBlindThePlacementSearch);
        // A torch's own colour, so the numbers reproduce the own-torch discount exactly, and then a pet-like
        // source that is a different colour and a different strength, so the row cannot be passing on a
        // constant that happens to match the torch.
        TorchID.TorchColor(TorchID.Torch, out float tr, out float tg, out float tb);
        Each("a: a player's held torch never makes a passage read lit",
            () => ACarriedLightNeverMakesAPassageReadLit("player torch", tr, tg, tb));
        Each("a: a light pet never makes a passage read lit",
            () => ACarriedLightNeverMakesAPassageReadLit("light pet", .45f, .75f, .95f));
        Each("a: two hundred dust lights cost nothing", ManyWeakLightsCostNothing);
        Each("b: a dark wing away from a lit body is offered with a Reachable site", ADarkWingIsOfferedWithAReachableSite);
        Each("c: two sites in one dark region are worked without going back to the player", TwoSitesAreWorkedWithoutReturning);
        Each("c: the same dark floor priced under the production allowances", MeasureTheRegionScanUnderProductionAllowances);
        Each("d: a player no candidate can stand beside still gets a gap-closing destination declaring no region", APlayerNoCandidateReachesStillGetsProgress);
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
        senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
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
    /// The property the torch removal exists for, and the one nothing else exercises. The engine merges lights
    /// with <c>Vector3.Max</c> and blurs them separably, so a carried torch's contribution at a tile is the
    /// source decayed by Manhattan distance and taken as a maximum, never a quantity added on. Subtracting it
    /// instead — which is what the first version did — leaves a pitch-dark blob at the hand, and the companion
    /// walks to its own feet to light them.
    /// </summary>
    private static void ItsOwnTorchIsNotEvidenceOfLight()
    {
        var ctx = Scene(null);
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Vector2 handAt = ctx.Npc.Center + new Vector2(ctx.Npc.direction * 10f, -6f);
        Point hand = handAt.ToTileCoordinates();
        // The torch as the engine would present it: its own colour at the source, decayed per tile of Manhattan
        // distance through air, over a world that is otherwise dark.
        TorchID.TorchColor(TorchID.Torch, out float r, out float g, out float b);
        float source = (r + g + b) / 3f;
        VerifyUsefulAssistance.WriteMeasuredLight(Window, (x, y) =>
            Math.Max(.02f, source * MathF.Pow(.91f, Math.Abs(x - hand.X) + Math.Abs(y - hand.Y))));
        // The torch is announced the way the game announces it, through AddLight, and the map above is what
        // the blur makes of that announcement. Setting the bearer's `Shown` instead is what this row used to
        // do, and it stopped meaning anything the moment the sense started reading the engine's own list
        // rather than asking us which lights exist — which is the point of reading the list.
        AddCarriedLight(hand, r, g, b);
        var senses = ctx.Companion.Brain.Senses;
        ForceRefresh(ctx);
        var light = senses.Light;

        // The field must be non-empty before anything below means anything. Without this the two assertions
        // that follow are both satisfied by a sense that read nothing at all, which is exactly the state this
        // scene produces when the torch flag it depends on is false — so the escape hatches these replace
        // turned the one row covering the torch removal into a row covering nothing.
        Require(light.MeasuredSamples > 0,
            $"the scene must present a field for this row to test anything; samples={light.MeasuredSamples}");

        // Not "unmeasured": the hold radius is a box, so its far corners sit beyond the torch's own reach in
        // Manhattan terms, and a sample there is genuinely dark and genuinely not explained by the torch.
        // Which of the two happens depends on where the stride-4 lattice lands, and a row that depends on
        // lattice alignment is a row that will flake. The property that does not is that the companion's own
        // glow never becomes evidence of a lit room: every sample the sense keeps inside its own hold radius
        // reads dark, because each one is either beyond the torch or dropped as the torch's own.
        var here = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(here.Dark == here.Measured,
            $"the companion's own torchlight is being kept as room light: {here.Measured - here.Dark} of "
            + $"{here.Measured} samples inside the hold radius read lit; {Show(here)} at hand {hand}");

        // And the region search must find real darkness away from the body. Asserted rather than skipped when
        // absent: "found nothing" is the answer a sense that read nothing gives, and it is also the answer a
        // sense that swallowed the whole cavern into its own glow gives.
        var region = light.NearestDarkRegion(body, Weights.LightRegionSearchTiles);
        Require(region is not null,
            "a dark cavern lit only by the companion's own torch must still hold a dark region beyond the torch's reach");
        int manhattan = Math.Abs(region!.Value.Centre.X - hand.X) + Math.Abs(region.Value.Centre.Y - hand.Y);
        Require(manhattan > 4,
            $"the nominated dark region {region.Value.Centre} sits on the companion's own hand {hand}: the torch was removed by subtraction rather than by the engine's own maximum");
    }

    /// <summary>
    /// The ordering this whole discount rests on, driven through the real <c>CompanionNPC.AI</c> rather than
    /// reconstructed. The sense must be discounting the light that is actually in the map it is reading, and
    /// it learns that from the engine's own per-frame list rather than from a flag we maintain — which is
    /// what closed the defect class rather than the one instance. Before the list, the sense asked the torch
    /// bearer whether its torch was out, and the tick cleared that flag one call before the brain read it, so
    /// the answer was false on every live tick and the whole discount was dead in the game.
    ///
    /// <para>Nothing here sets a flag or paints a glow. A fixture that arranges the state it then checks
    /// cannot see an ordering defect at all, which is why every earlier torch row passed throughout.</para>
    /// </summary>
    private static void TheSenseReadsTheTorchTheMapWasWrittenWith()
    {
        var ctx = Scene((_, _) => .02f);
        var brain = ctx.Companion.Brain;
        // Keeping company only, so no tool ever claims the hand and the torch is free to come up.
        brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");

        int ticksWithTorchOut = 0, refreshes = 0, refreshesSeeingTheTorch = 0;
        ulong? lastRead = brain.Senses.Light.ReadTick;
        for (int tick = 0; tick < 400; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (ctx.Companion.Torch.Shown) ticksWithTorchOut++;
            ulong? read = brain.Senses.Light.ReadTick;
            if (read == lastRead) continue;
            lastRead = read;
            refreshes++;
            // The torch the companion is holding must be among the transients the field discounted. Its
            // AddLight runs after the brain, so what the field sees is the previous tick's emission — which
            // is exactly the emission the light map in front of it was blurred from.
            if (ticksWithTorchOut > 0 && NearestTransient(ctx.Npc.Center.ToTileCoordinates()) <= 3)
                refreshesSeeingTheTorch++;
        }
        Require(ticksWithTorchOut > 0,
            $"the torch must actually come out somewhere in a dark cavern, or this row proves nothing; refreshes={refreshes}");
        Require(refreshesSeeingTheTorch > 0,
            $"the field must discount the torch the companion is carrying: no refresh after it came out found a "
            + $"transient source at the companion; refreshes={refreshes} ticks-with-torch-out={ticksWithTorchOut} "
            + $"sources={TransientLights.Count}");
        Console.WriteLine($"        torch ordering: {ticksWithTorchOut} of 400 ticks emitted light; {refreshes} field refreshes, "
            + $"{refreshesSeeingTheTorch} of them with the companion's own torch among the discounted transients");
    }

    /// <summary>Manhattan distance from <paramref name="tile"/> to the nearest transient the field is
    /// discounting, or a large number where there is none.</summary>
    private static int NearestTransient(Point tile)
    {
        int best = int.MaxValue;
        foreach (var source in TransientLights.Sources)
            best = Math.Min(best, Math.Abs(source.Tile.X - tile.X) + Math.Abs(source.Tile.Y - tile.Y));
        return best;
    }

    /// <summary>
    /// A companion already carrying a torch must still be able to see that a place needs a permanent one.
    /// The two readings of the same sample are deliberately opposite: for holding the torch, light the
    /// companion is itself emitting is unknown and skipped, so it cannot decide from its own glow; for placing
    /// one, that same light counts as darkness, because it is leaving when the companion does. Reading it as
    /// room light in both places is a deadlock rather than caution — the torch comes out because the cavern is
    /// dark, and every site then reads lit precisely because the torch is out, so nothing is ever placed.
    ///
    /// <para>The torch here is brought out by the real decision over a real dark scene, not set: the defect
    /// only exists once the sense is reading a torch that is genuinely emitting.</para>
    /// </summary>
    private static void ACarriedTorchDoesNotBlindThePlacementSearch()
    {
        // Dark everywhere, so the assertions below hold wherever keeping company has walked the body by the
        // time the torch is up. A dark pocket was tried instead, to make the offer itself the discriminator,
        // and it failed for two compounding reasons worth recording: a pocket wide enough to raise the torch
        // is wider than the torch's own glow, so its edge is offered either way, and the body walks out of a
        // narrow one before the field has read the torch at all.
        var ctx = Scene((_, _) => .02f);
        GiveTorches(ctx);
        Settle(ctx);
        var brain = ctx.Companion.Brain;
        // Keeping company only, so the hand stays free and nothing places a torch while we wait for one to
        // come up; lighting is prepared directly afterwards.
        brain.Chooser.Actions.RemoveAll(a => a.Name != "keep-company");
        // Driven until the field has resampled *while* the torch was emitting, not merely until the torch is
        // out. The sense only learns about the torch when it next reads the world, so a check made in the gap
        // between the torch coming up and the next refresh tests the scene before the defect can exist — which
        // is how this row first passed against the very code it was written to fail on.
        bool sensedTheTorch = false;
        for (int tick = 0; tick < 600 && !sensedTheTorch; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            sensedTheTorch = ctx.Companion.Torch.Shown
                && NearestTransient(ctx.Npc.Center.ToTileCoordinates()) <= 3;
        }
        Require(sensedTheTorch,
            $"the premise is a companion carrying light in a dark cavern, with the field having read that torch; "
            + $"lit={ctx.Companion.Torch.Lit} shown={ctx.Companion.Torch.Shown} reason={ctx.Companion.Torch.Reason}");

        var action = new LightUsefulArea();
        float score = VerifyPreparedActivities.PrepareAndScore(action, ctx);
        Require(score > 0 && action.Eligibility == Offer.Usable && action.ActivityTarget is not null,
            $"a companion holding a torch must still offer to place one; its own carried light is not evidence that "
            + $"the cave is lit, and reading it as such leaves the ability unable to fire in the dark it exists for; "
            + $"score={score:0.000} {action.Eligibility}:{action.EligibilityReason} torch shown={ctx.Companion.Torch.Shown}");

        // And the mechanism, asked of the sense directly at a tile the carried torch is certainly lighting.
        // Going through the search alone does not discriminate here and two attempts to make it do so failed
        // honestly: on an all-dark floor the search walks past its own glow to a site beyond it, and a dark
        // pocket wide enough to raise the torch at all is wider than the glow, so its edge is offered either
        // way. The end-to-end wiring of this into the placement predicate is covered by the J08 pair in
        // VerifyAssistanceTrips, which was red before the flag and green after; this row covers the sense's
        // own two answers, which is where the asymmetry lives.
        Point body = ctx.Npc.Center.ToTileCoordinates();
        Point nearHand = new(body.X + 2, body.Y - 2);
        var holding = brain.Senses.Light.MeasuredAround(nearHand, Weights.LightSiteRadiusTiles, Weights.LightSiteStrideTiles);
        var placing = brain.Senses.Light.MeasuredAround(nearHand, Weights.LightSiteRadiusTiles, Weights.LightSiteStrideTiles,
            carriedCountsAsDark: true);
        Require(holding.Unmeasured,
            $"the premise is a neighbourhood the companion's own torch accounts for, which the hold decision must "
            + $"refuse to read; {Show(holding)} at {nearHand}");
        Require(!placing.Unmeasured && placing.MeanBrightness < Weights.LightDarkBelow,
            $"the same neighbourhood must read dark when the question is whether to leave a torch behind, because the "
            + $"light in it is the light that leaves with the companion; {Show(placing)} at {nearHand}");
    }

    // ---- every carried light is discounted, not only the companion's own --------------------------------

    /// <summary>
    /// The behaviour this lane exists for, stated as a comparison of the same scene with and without a light
    /// somebody is carrying through it. The companion follows a player holding a torch into a dark passage;
    /// under a rule that only knew about the companion's own torch, everything around him read lit, nothing
    /// was placed, and the passage went dark the moment he walked on — the opposite of lighting a mine as you
    /// go. The sense never learns which source this is: it reads the engine's own per-frame list, so a light
    /// pet, a mining helmet and a lantern from a mod nobody here has heard of arrive by the same door.
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

        // Now the same world with somebody standing in it holding a light.
        AddCarriedLight(carrier, r, g, b);
        ForceRefresh(ctx);
        Require(TransientLights.Count > 0,
            $"{what}: the carried light must reach the sense through the engine's list, or nothing below is being "
            + $"tested; engine list holds {RawTransientCount()} entries, mode={Lighting.Mode}, netMode={Main.netMode}, "
            + $"paused={Main.gamePaused}, global brightness={Lighting.GlobalBrightness:0.000}");

        // Every tile still measured must read exactly what it read before. The carried light may remove tiles
        // from measurement — under the engine's maximum the world light beneath it cannot be recovered, and an
        // unknown is honest — but it must never change one. A value that moved means carried light is being
        // reported as the room's.
        int kept = 0, dropped = 0;
        foreach (var (tile, was) in before)
        {
            float? now = light.MeasuredBrightnessAt(tile);
            if (now is null) { dropped++; continue; }
            kept++;
            Require(Math.Abs(now.Value - was) < 1e-4f,
                $"{what}: the world-light reading at {tile} changed from {was:0.0000} to {now.Value:0.0000} when somebody "
                + $"walked in carrying a light; the sense is reporting their light as the room's");
        }
        Require(dropped > 0,
            $"{what}: the carrier must actually occlude something, or the row would pass against a sense that "
            + $"discounts nothing at all; kept={kept} dropped={dropped} sources={TransientLights.Count}");

        // And the passage still reads dark around the body, which is the behaviour the objective names: a
        // companion beside a player holding a torch must not conclude the place is lit.
        var darkAfter = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(!darkAfter.Unmeasured && darkAfter.DarkFraction > Weights.TorchRaiseDarkShare,
            $"{what}: a passage lit only by a light somebody is carrying through it must still read dark; "
            + $"before {Show(darkBefore)} after {Show(darkAfter)} sources={TransientLights.Count}");
        // One named tile, read both ways, because a share is a summary and the question this lane answers is
        // about a tile: what does the sense say about the ground beside somebody holding a torch.
        string underTheCarrier = before.TryGetValue(carrier, out float wasThere)
            ? $"{wasThere:0.0000} before, {(light.MeasuredBrightnessAt(carrier) is float after ? after.ToString("0.0000") : "unknown")} after"
            : "outside the sampled band";
        Console.WriteLine($"        {what}: {kept} tiles unchanged, {dropped} dropped as unknown, dark share "
            + $"{darkBefore.DarkFraction:0.000} -> {darkAfter.DarkFraction:0.000}; the carrier's own tile {carrier} reads {underTheCarrier}");
    }

    /// <summary>
    /// Cost, on the scene the discount is worst on: a fiery room full of dust and gore lights. Each is an
    /// entry in the same list, so the guard is that reading two hundred of them costs no more than the
    /// refresh is already allowed. Entries too weak to reach the dark threshold at their own tile are
    /// dropped where they are read rather than carried into the per-sample loop, which is what makes this
    /// affordable; the row would catch that filter being lost.
    /// </summary>
    private static void ManyWeakLightsCostNothing()
    {
        var ctx = Scene((_, _) => .02f);
        Point body = ctx.Npc.Center.ToTileCoordinates();
        var random = new Random(20260914);
        for (int i = 0; i < 200; i++)
        {
            Point at = new(body.X - 30 + random.Next(60), body.Y - 20 + random.Next(20));
            // Dust-bright: real, in the list, and below the dark level at its own tile, so it can never make
            // anything read lit and the sense should refuse to spend anything on it.
            AddCarriedLight(at, .05f, .04f, .03f);
        }
        ForceRefresh(ctx);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 60; i++) ForceRefresh(ctx);
        double perRefresh = clock.Elapsed.TotalMilliseconds / 60;
        Require(ctx.Companion.Brain.Senses.Light.MeasuredSamples > 0,
            "two hundred lights too weak to matter must not empty the field");
        // The refresh is one part of the tick's planning allowance and has never been near it; the ceiling is
        // that allowance, so the row fails on a refresh that has become a tick's work rather than on a figure
        // that records today's machine.
        Require(perRefresh < Weights.TotalPlanningMilliseconds,
            $"a refresh with two hundred dust lights in the window took {perRefresh:0.000} ms, past the whole tick's "
            + $"planning allowance of {Weights.TotalPlanningMilliseconds:0.000} ms");
        Console.WriteLine($"        200 dust lights: {perRefresh:0.000} ms per full refresh, {TransientLights.Count} kept of 200 (this machine)");
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
        var request = action.Execute(ctx);
        Vector2? destination = brain.Positioner.Resolve(request, brain.Senses, null);
        Require(destination is { } stand && brain.Senses.Reach.Reachable(MovementQueries.FeetTile(stand)) == ReachVerdict.Reachable,
            $"the lighting destination must be a tile the reach sense calls Reachable; destination={destination} "
            + $"verdict={(destination is { } d ? brain.Senses.Reach.Reachable(MovementQueries.FeetTile(d)).ToString() : "none")} "
            + $"complete={brain.Senses.Reach.Complete}");
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
        brain.Chooser.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");

        int first = -1, second = -1, companyBetween = 0;
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
        }
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
        // With the allowances lifted nothing bounds this but the search's own shape, so the ceiling is a blunt
        // order-of-magnitude guard rather than a statement about any one fix — it measured 37–39 ms both before
        // and after the site bound, because that bound only bites once a deadline exists to expire. It is here
        // so a future change that makes the scan quadratic again fails a run instead of printing a larger number
        // nobody reads. Cold and warm differ twelvefold on this line, so the ceiling is set for the cold case.
        Require(decideMax < 120d,
            $"deciding over a wholly dark floor with the planning allowances lifted must stay within an order of "
            + $"magnitude of what it has historically cost; measured {decideMax:0.000} ms against a ceiling of 120 ms");
    }

    /// <summary>
    /// A measurement, not an assertion. A screen that is dark everywhere is the worst input the region scan
    /// has: every rescore nominates a region and scans a box around every one of its members. The case above
    /// runs with the allowances lifted, which prices the scan with nothing stopping it; this one runs the same
    /// scene under the allowances the live tick actually applies, which is the number that says whether the
    /// scan needs a deadline probe of its own or whether the family's preparation share already holds it.
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
            brain.Chooser.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");
            double decideMax = 0, prepareMax = 0, decideTotal = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                VerifyOreWork.AdvanceBrain(ctx);
                decideMax = Math.Max(decideMax, brain.DecideMs);
                decideTotal += brain.DecideMs;
                foreach (var family in brain.Chooser.Queries.LastFamilies)
                    if (family.Family.ToString() == "NearbyAssistance") prepareMax = Math.Max(prepareMax, family.Milliseconds);
            }
            Console.WriteLine($"        dark floor under production allowances: decide max {decideMax:0.000} ms, mean {decideTotal / 600:0.000} ms, "
                + $"NearbyAssistance preparation max {prepareMax:0.000} ms over 600 ticks (this machine)");
            // A pass line, because the regression this exists to catch was found by a person reading a printed
            // number and would have shipped otherwise. The ceiling is the tick's own planning allowance with
            // room for the measurement and for a cold run's JIT, not a record of today's figure: the property
            // is that the allowance bounds this preparation at all, which is precisely what was untrue when a
            // cheaper proof deleted the loop's bound and one preparation reached 37.6 ms. It is deliberately
            // slack rather than tight, because the same code measures 7.8 ms inside the warmed default suite
            // and 13.0 ms run alone, and a ceiling between those two numbers tests the harness.
            double ceiling = Weights.TotalPlanningMilliseconds * 2d;
            Require(prepareMax < ceiling,
                $"NearbyAssistance preparation on a wholly dark floor must stay inside twice the tick's planning "
                + $"allowance; measured {prepareMax:0.000} ms against a ceiling of {ceiling:0.000} ms");
        }
        finally { LimitPlanningWork.Unbounded = lifted; }
    }

    // ---- (d) following always has somewhere to go ------------------------------------------------------

    /// <summary>
    /// The player stands on a shelf hanging in the air with nothing under it, so the comfort box around him
    /// holds no tile that is both standable and inside the flood. Before, that emptied the candidate list and
    /// the request answered nothing at all, which left the navigator with no proven destination and the brain
    /// reaching for a state search that jumps at the player. Now it answers with a reachable tile that closes
    /// the gap — and declares no success region, because the one thing it is certain of is that the answer is
    /// outside the objective.
    /// </summary>
    private static void APlayerNoCandidateReachesStillGetsProgress()
    {
        var ctx = Scene(null);
        for (int x = 58; x <= 62; x++) VerifyOreWork.Place(new Point(x, 40), TileID.Dirt);
        TerrainChanges.Reset();
        AStar.InvalidateEdges();
        ctx.Player.position = new Vector2(60 * 16f, 40 * 16f - ctx.Player.height);
        ctx.Player.velocity = Vector2.Zero;
        var brain = ctx.Companion.Brain;
        brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);

        var request = new PositionRequest(RequestKind.WithPlayer, ctx.Player.Bottom);
        Vector2? chosen = null;
        // More than one resolve: a single call measures the flood's per-rescore budget rather than the answer,
        // and the whole point of the tri-state is that an unfinished flood is not a refusal.
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            chosen = brain.Positioner.Resolve(request, brain.Senses, null);
        Require(brain.Positioner.ReachComplete, "the flood must settle before this row reads a refusal as final");
        for (int i = 0; i < 8; i++) chosen = brain.Positioner.Resolve(request, brain.Senses, null);

        Require(chosen is not null,
            $"following must always produce somewhere to go; reason={brain.Positioner.ChoiceReason}");
        Require(brain.Positioner.ChoiceReason == "partial-progress-candidate",
            $"a player on an unreachable shelf must be answered with progress toward him, not an accepted candidate; reason={brain.Positioner.ChoiceReason} chosen={chosen}");
        Require(brain.Senses.Reach.Reachable(MovementQueries.FeetTile(chosen!.Value)) == ReachVerdict.Reachable,
            $"the progress tile must be one the companion can actually get to and back from; chosen={chosen} verdict={brain.Senses.Reach.Reachable(MovementQueries.FeetTile(chosen.Value))}");
        Require(brain.Positioner.Region.Kind == SuccessRegionKind.Undeclared,
            $"a destination outside the follow objective must declare no region it cannot meet, or every arrival on it is recorded as a contract violation; kind={brain.Positioner.Region.Kind}");
    }

    // ---- scene ----------------------------------------------------------------------------------------

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
        ForgetTransients();
        TerrainChanges.Reset();
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        return ctx;
    }

    private static bool InChamber(int x, int y) => Math.Abs(x - 20) <= 14 && Math.Abs(y - StandRow) <= 14;

    /// <summary>Empties both the engine's per-frame light list and the sense's memory of it. Headless nothing
    /// drives <c>ProcessArea</c>, so the engine never clears that list itself and one scene's carried lights
    /// would otherwise still be discounted in the next — a leak between rows that reads as a sense defect.</summary>
    private static void ForgetTransients()
    {
        RawTransientList().Clear();
        TransientLights.Forget();
    }

    private static System.Collections.IList RawTransientList()
    {
        object engine = typeof(Lighting).GetField("NewEngine", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return (System.Collections.IList)engine.GetType().GetField("_perFrameLights", InstanceField)!.GetValue(engine)!;
    }

    private static int RawTransientCount() => RawTransientList().Count;

    /// <summary>Adds a light the way anything carried adds one — the player's torch, a pet, a helmet, a mod's
    /// lantern. The sense is never told which it is, and that is the point: it reads the engine's list.</summary>
    private static void AddCarriedLight(Point tile, float r, float g, float b)
        => Lighting.AddLight(tile.ToWorldCoordinates(), r, g, b);

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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
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
            brain.Positioner.Resolve(home, brain.Senses, null);
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
