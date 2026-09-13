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
        typeof(TorchBearer).GetProperty("Shown")!.GetSetMethod(true)!.Invoke(ctx.Companion.Torch, new object[] { true });
        var senses = ctx.Companion.Brain.Senses;
        senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        var light = senses.Light;

        var here = light.DarkAirNear(body, Weights.TorchHoldRadiusTiles);
        Require(here.Unmeasured || here.DarkFraction > Weights.TorchRaiseDarkShare,
            $"a cave lit by nothing but the companion's own torch must not read as a lit room; {Show(here)} at hand {hand}");

        // And the region search must not nominate the hand. Either it finds nothing (every sample near the body
        // is the torch's own and is dropped as unmeasured) or it finds real darkness away from the body; what it
        // may never do is point at where the companion is standing.
        var region = light.NearestDarkRegion(body, Weights.LightRegionSearchTiles);
        if (region is { } found)
        {
            int manhattan = Math.Abs(found.Centre.X - hand.X) + Math.Abs(found.Centre.Y - hand.Y);
            Require(manhattan > 4,
                $"the nominated dark region {found.Centre} sits on the companion's own hand {hand}: the torch was removed by subtraction rather than by the engine's own maximum");
        }
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
                + $"NearbyAssistance preparation max {prepareMax:0.000} ms over 600 ticks (this machine, never asserted)");
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
        TerrainChanges.Reset();
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        return ctx;
    }

    private static bool InChamber(int x, int y) => Math.Abs(x - 20) <= 14 && Math.Abs(y - StandRow) <= 14;

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
