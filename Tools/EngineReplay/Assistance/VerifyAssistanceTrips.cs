extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using CollectNearbyItems = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Reach = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.Reach;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using HandGrant = live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant;
using ConsiderIncidentalInteractions = live::AICompanion.Companion.Brain.Infrastructure.Grants.ConsiderIncidentalInteractions;

/// <summary>
/// Lighting and pot trips through the shared nearby-interaction executor, on native tiles: a trip is offered only where the body
/// can actually get to the site, a site no floor pose reaches is offered with a hover beside it and performed, and an interaction
/// passed on the way happens incidentally without a second destination or movement owner.
///
/// <para>The trips that went with the walker. Two pairs here asked whether the companion could come *back* from a site: a dark
/// pit and a pot on its floor, twelve rows below a ledge, offered only once a staircase made the drop a round trip. For a body
/// that flies there is no such question — reaching a place and returning from it are one flood over the same free cells, and the
/// reach sense says so in its own code, where <c>ReachableOneWay</c> is <c>Returnable</c>. The pairs are kept as the question
/// they were always standing in for, which this body does have: a site the body cannot fit its way to is not offered, and the
/// same site with an opening it fits through is. The shaft is two tiles wide against one, which is the orb's size rule read back
/// through an activity's offer rather than through the planner.</para>
///
/// <para>Four hop rows went outright. They put a shelf out of standing reach and within a ground jump's rise of a take-off
/// elsewhere, and asserted that the walk found the take-off and hopped; the orb has no jump, no take-off and no ground to leave,
/// so there is no mechanism left for them to be about. What survives them is the part that is still true of any body with a tool:
/// a site no pose on the floor can reach is offered with a hover beside it, and the whole brain performs it from there.</para>
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
            Preferences.Current = new Preferences { TorchPlacement = false, PotBreaking = false };
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
        Each("L05 darkness in a chamber the body cannot fit into is not offered; the same chamber with a two-wide shaft is", DarknessInAnUnreachableChamber);
        Each("I03 a pot in a chamber the body cannot fit into is not offered; the same pot with a two-wide shaft is", APotInAnUnreachableChamber);
        Each("hover torch: a site no floor pose reaches is offered with a hover beside it and placed", () => ASiteNoFloorPoseReaches(lighting: true));
        Each("hover pot: a pot no floor pose reaches is offered with a hover beside it and broken", () => ASiteNoFloorPoseReaches(lighting: false));
        Each("J08 a lighting trip breaks a permitted pot in passing, with no detour and no second movement owner", () => ALightingTripPassesAPot(potBreaking: true));
        Each("J08 the same pot with pot breaking disabled is left alone", () => ALightingTripPassesAPot(potBreaking: false));
        Each("J08 a tick whose planning allowance is spent skips the incidental scan without losing its turn", AnExpiredAllowanceDefersTheIncidentalScan);
        if (red == 0) Console.WriteLine("assistance trips: trips need a way in the body fits through, a site above every floor pose is worked from a hover, and a pot on the way breaks incidentally only when permitted");
        return red;
    }

    /// <summary>
    /// The incidental scan runs at the grant boundary, after planning has had the tick's allowance. A permitted pot is placed within reach
    /// of where the companion stands and the scan is asked directly with the live allowance in force: once already expired, the pot must
    /// be left alone and nothing recorded; then, on the same game tick with the allowance ended, the pot must break, which also proves the
    /// refused tick did not use up the scan's cadence.
    /// </summary>
    private static void AnExpiredAllowanceDefersTheIncidentalScan()
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        Preferences.Current.PotBreaking = true;
        Vector2 feet = ctx.Npc.Bottom;
        Point pot = PlacePot(new Point((int)(feet.X / 16f) + 1, FloorRow - 2));
        TerrainChanges.Reset();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
        Require(footprint.Any(t => FindToolAccess.InReach(feet, t)), $"the pot must be within reach where the companion stands; feet={feet} pot={pot}");
        var incidental = new ConsiderIncidentalInteractions();
        LimitPlanningWork.Unbounded = false;
        try
        {
            LimitPlanningWork.Begin(0.000001);
            Thread.Sleep(2);
            Require(LimitPlanningWork.Expired, "the allowance must already be spent, or the refusal row tests nothing");
            incidental.Consider(ctx, HandGrant.Available, false, null, 0);
            Require(incidental.Last == null && footprint.All(t => Main.tile[t.X, t.Y].HasTile),
                $"a scan on a tick whose planning allowance is spent must not break the pot; last={incidental.Last}");
        }
        finally { LimitPlanningWork.End(); }
        incidental.Consider(ctx, HandGrant.Available, false, null, 0);
        Require(incidental.Last != null && footprint.Any(t => !Main.tile[t.X, t.Y].HasTile),
            $"with time left on the same game tick the scan must break the pot, so the refused tick kept its turn; last={incidental.Last}");
    }

    /// <summary>
    /// The hop lighting trip from <see cref="AHopFromATakeOffElsewhere"/>, with a pot on the floor between the companion and the take-off,
    /// out of reach where the walk starts and within reach as it passes. Only lighting and keeping company are registered, so breaking the
    /// pot can be nobody's activity. With pot breaking on, the pot must break during the lighting activity after the body has moved,
    /// with the same request and the same movement owner on the tick it breaks as on the tick before, and the lighting attempt must still
    /// close complete with exactly one productive effect, the torch. With pot breaking off, the trip must place its torch and leave every
    /// tile of the pot in place.
    /// </summary>
    private static void ALightingTripPassesAPot(bool potBreaking)
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        VerifyOreWork.Place(new Point(31, 51), TileID.Dirt);
        VerifyOreWork.Place(new Point(32, 51), TileID.Dirt);
        GiveTorches(ctx);
        Preferences.Current.TorchPlacement = true;
        Preferences.Current.PotBreaking = potBreaking;
        VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (x, y) => y is >= 57 and <= 60 ? .9f : .02f);
        Point pot = PlacePot(new Point(27, FloorRow - 2));
        TerrainChanges.Reset();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
        Vector2 start = ctx.Npc.Bottom;
        Require(footprint.All(t => !FindToolAccess.InReach(start, t)), "the pot must be out of reach where the walk starts, or nothing is passed");
        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");
        Point? torch = null;
        int brokenAt = -1, torchAt = -1;
        string before = "", atBreak = "", actionAtBreak = "";
        float feetAtBreak = 0f;
        double decideMax = 0, finaliseMax = 0;
        // Fetched before the loop, because the incidental record this row is about has to be read on the tick
        // the pot tile disappears and not at the end of the run. `Last` is the most recent incidental of any
        // kind, and the run continues for ninety ticks after the torch lands — long enough for the companion to
        // wander over and pick the pot's own drop up, which is a later `collect` during `keep-company` and is
        // exactly what the first orb run reported while the pot had in fact been broken correctly at tick 15.
        object? incidental = brain.GetType().GetField("Incidental")?.GetValue(brain);
        object? incidentalAtBreak = null;
        for (int tick = 0; tick < 900 && (torchAt < 0 || tick < torchAt + 90); tick++)
        {
            string last = $"{brain.LastRequest.Kind}@{brain.LastRequest.Anchor}/owner={brain.ControlGrants.Last?.AppliedOwner}";
            VerifyOreWork.AdvanceBrain(ctx);
            decideMax = Math.Max(decideMax, brain.DecideMs);
            finaliseMax = Math.Max(finaliseMax, brain.FinaliseMs);
            if (brokenAt < 0 && footprint.Any(t => !Main.tile[t.X, t.Y].HasTile))
            {
                brokenAt = tick; before = last; actionAtBreak = brain.LastAction?.Name ?? "none"; feetAtBreak = ctx.Npc.Bottom.X;
                atBreak = $"{brain.LastRequest.Kind}@{brain.LastRequest.Anchor}/owner={brain.ControlGrants.Last?.AppliedOwner}";
                incidentalAtBreak = incidental?.GetType().GetProperty("Last")?.GetValue(incidental);
            }
            if (torchAt < 0)
                for (int x = 24; x <= 36 && torch == null; x++)
                    for (int y = 45; y <= 59 && torch == null; y++)
                        if (Main.tile[x, y].HasTile && TileID.Sets.Torch[Main.tile[x, y].TileType]) { torch = new Point(x, y); torchAt = tick; }
        }
        string attempts = string.Join("; ", brain.Chooser.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}:effects={a.ProductiveEffects}"));
        // Lighting's own last word and the torch it is carrying, because every way this row fails runs through
        // one of them: a refusal names why no site was taken, and a shown torch means the field is discounting
        // the companion's own light in exactly the neighbourhood the sites are in.
        var lighting = brain.Chooser.Actions.OfType<LightUsefulArea>().FirstOrDefault();
        string lightState = lighting == null ? "lighting not registered"
            : $"lighting offer={lighting.Eligibility}/{lighting.EligibilityReason} value={lighting.Score():0.000} target={lighting.ActivityTarget}"
            + $"; torch lit={ctx.Companion.Torch.Lit} shown={ctx.Companion.Torch.Shown} reason={ctx.Companion.Torch.Reason}"
            + $"; light samples={brain.Senses.Light.MeasuredSamples}";
        object? lastIncidental = incidental?.GetType().GetProperty("Last")?.GetValue(incidental);
        string ledger = $"potBreaking={potBreaking} torch={torch} at tick {torchAt}; pot broken at tick {brokenAt} during {actionAtBreak} feet x={feetAtBreak:0} (start {start.X:0}); "
            + $"request/owner before={before} at break={atBreak}; incidental at break={incidentalAtBreak}; incidental at end={lastIncidental}; {lightState}; attempts=[{attempts}]";
        Require(torchAt >= 0, $"the lighting trip must place its torch; {ledger}");
        if (!potBreaking)
        {
            // The pot specifically, not "nothing incidental happened". Those were the same claim only while a
            // carried torch made every nearby site read lit: now that the field discounts every carried light,
            // the companion places a torch in passing while keeping company, which is the behaviour the
            // discount exists to produce and has nothing to do with the pot this row is about.
            Require(brokenAt < 0, $"a pot with pot breaking disabled must be left alone; {ledger}");
            Require(lastIncidental == null || !lastIncidental.ToString()!.Contains("Method = collect"),
                $"no incidental interaction may reach for the pot while pot breaking is disabled; {ledger}");
            return;
        }
        Require(brokenAt >= 0 && actionAtBreak == "place-torches" && feetAtBreak > start.X + 32f,
            $"a permitted pot in reach must be broken in passing, during the lighting trip and after the walk began; {ledger}");
        Require(before == atBreak, $"breaking the pot must change neither the request nor the movement owner; {ledger}");
        Require(brain.Chooser.Activity.RecentAttempts.Any(a => a.Activity == "place-torches" && a.Status.ToString() == "Complete" && a.ProductiveEffects == 1),
            $"the lighting attempt must complete with its torch as its only productive effect; the pot is credited to no activity; {ledger}");
        if (incidental != null)
            Require(incidentalAtBreak != null && incidentalAtBreak.ToString()!.Contains("Method = collect") && incidentalAtBreak.ToString()!.Contains("DuringActivity = place-torches"),
                $"the incidental record must name the pot method and the activity it happened during; {ledger}");
        Console.WriteLine($"incidental pot: broken at tick {brokenAt} during place-torches, torch at tick {torchAt}; decide max {decideMax:0.000} ms, finalise max {finaliseMax:0.000} ms (this machine, never asserted)");
    }

    /// <summary>
    /// A two-tile shelf eleven columns from the companion, with a pot on it or its top as the only dark torch sites, nine rows
    /// above the floor so that no pose on the floor is within tool reach of it. The offer must therefore name a hover beside the
    /// site rather than a floor pose, and the whole brain — with the chosen method and keeping company as its only registered
    /// activities — must perform it from up there, with the body's own centre above the floor when it does.
    /// </summary>
    private static void ASiteNoFloorPoseReaches(bool lighting)
    {
        int shelfRow = 51;
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        VerifyOreWork.Place(new Point(31, shelfRow), TileID.Dirt);
        VerifyOreWork.Place(new Point(32, shelfRow), TileID.Dirt);
        Point interaction;
        if (lighting)
        {
            GiveTorches(ctx);
            Preferences.Current.TorchPlacement = true;
            // Light only a band over the floor, so every floor site is vetoed as lit while the area and the shelf stay dark.
            VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (x, y) => y is >= 57 and <= 60 ? .9f : .02f);
            interaction = new Point(31, shelfRow - 1);
        }
        else
        {
            Preferences.Current.PotBreaking = true;
            interaction = PlacePot(new Point(31, shelfRow - 2));
        }
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        var brain = ctx.Companion.Brain;
        // The flood has to have settled before anything asks an access question, because every one of them
        // now reads it: an unfinished flood answers "not yet known" for the hover beside the site, and both
        // the premise row below and the offer itself would read for a reason that has nothing to do with the
        // shelf this row is about.
        VerifyOreWork.ResettleReach(ctx);
        Require(brain.Positioner.ReachComplete, $"this row needs a settled reach region before it asks anything; shelf row {shelfRow}");
        var reach = brain.Senses.Reach;
        // Nothing on the floor reaches it: the premise the whole row stands on, asserted against every column
        // of floor the body could put itself over rather than against the one it happens to start on.
        float floorCentre = FloorRow * 16f - live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact.Radius;
        for (int x = 20; x <= 45; x++)
            Require(!FindToolAccess.InReach(new Vector2(x * 16f + 8f, floorCentre), interaction),
                $"the shelf must be out of tool reach from every pose along the floor; column {x} reaches it, shelf row {shelfRow}");
        Require(FindToolAccess.Approach(interaction, ctx.Npc.Center, reach, out Vector2 hover) == Reach.Yes,
            $"the shelf must be reachable from a hover beside it; shelf row {shelfRow}");
        Require(hover.Y < floorCentre - 8f,
            $"the approach must name a hover above the floor, not a pose on it; hover={hover} floor centre={floorCentre}");

        string methodName = lighting ? "place-torches" : "collect";
        brain.Chooser.Actions.RemoveAll(a => a.Name != methodName && a.Name != "keep-company");
        var method = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.PerformNearbyWorldWork>().Single();
        float score = VerifyPreparedActivities.PrepareAndScore(method, ctx);
        string offer = $"shelf row {shelfRow}: score={score:0.000} offer={method.Eligibility}/{method.EligibilityReason} target={method.ActivityIdentity}";
        Require(score > 0 && method.ActivityIdentity is Point, $"a site reachable from a hover beside it must be offered; {offer}");
        Point target = (Point)method.ActivityIdentity!;
        Require(target.Y <= shelfRow, $"the offered target must be a shelf site; target={target}; {offer}");

        // The pot is done when any tile of its footprint is gone: headless KillTile removes the tile struck, which is also what the
        // method's own Perform reads as a break, and a retried attempt may strike a different tile of the same pot than the first
        // preparation named. The rest of the two-by-two footprint is not this fixture's question.
        bool Done() => lighting
            ? Main.tile[target.X, target.Y].HasTile && TileID.Sets.Torch[Main.tile[target.X, target.Y].TileType]
            : Enumerable.Range(0, 4).Any(i => !Main.tile[interaction.X + i % 2, interaction.Y + i / 2].HasTile);
        int tick = 0;
        float highest = float.MaxValue, atDone = 0f;
        var trace = new System.Text.StringBuilder();
        string last = "";
        for (; tick < 900 && !Done(); tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            highest = MathF.Min(highest, ctx.Npc.Center.Y);
            atDone = ctx.Npc.Center.Y;
            string now = $"{brain.LastAction?.Name}/{brain.LastRequest.Kind}";
            if (now != last && trace.Length < 1600) trace.Append($" t{tick}:{now}@{ctx.Npc.Center.X:0},{ctx.Npc.Center.Y:0}");
            last = now;
        }
        string attempts = string.Join("; ", brain.Chooser.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}"));
        Require(Done(), $"the whole brain must {(lighting ? "place the torch" : "break the pot")} from a hover; ticks={tick} centre={ctx.Npc.Center} action={brain.LastAction?.Name} "
            + $"status={method.Eligibility}/{method.EligibilityReason} highest centre={highest} attempts=[{attempts}] trace:{trace}");
        // The tick it finished on, not the highest point it ever reached: a run that drifted upward once and then
        // did the work from the floor would satisfy a check on the highest point alone.
        Require(atDone < floorCentre - 16f,
            $"the interaction must be performed from a hover above the floor; centre at completion={atDone} floor centre={floorCentre}");
        // One trip, performed: an approach that gave up and was retried would still finish the job eventually, so the
        // attempts are read for a failed method as well as the tile being read for the effect.
        Require(!brain.Chooser.Activity.RecentAttempts.Any(a => a.Activity == methodName && a.Status.ToString() == "Failed"),
            $"the trip must reach its hover and work from it, never fail a method on the way; attempts=[{attempts}] trace:{trace}");
        Console.WriteLine($"hover {(lighting ? "torch" : "pot")}: performed at tick {tick} from centre y {atDone:0}, highest {highest:0}, hover offered {hover}");
    }

    // The floor both actors stand on, and the chamber under it.
    private const int FloorRow = 60, IslandLeft = 14, IslandRight = 41;
    // The chamber below: free rows 61 to 71 between walls at columns 26 and 41, floored at row 72.
    private const int PitFloor = 72, PitLeft = 27, PitRight = 40;
    // The shaft through the floor into it, at this column and this many tiles wide when open.
    private const int ShaftLeft = 38;

    /// <summary>
    /// A measured dark area with a torch to place. Everything outside the chamber is lit, so the only dark sites anywhere a flying
    /// body could go are down in the chamber. The shaft through the floor is one tile wide or two: at one tile no corner inside it is usable, so no
    /// route into the chamber exists for a body ten pixels in radius and nothing down there may be offered; at two tiles the shaft
    /// has a usable corner down its middle with six pixels either side, and a site there must be offered.
    /// </summary>
    private static void DarknessInAnUnreachableChamber()
    {
        foreach (bool open in new[] { false, true })
            {
                var ctx = BuildIsland(open);
                GiveTorches(ctx);
                Preferences.Current.TorchPlacement = true;
                LightIslandOnly();
                // Lighting reads the light field and the reach region, so the scene is observed and its
                // region primed to settle before preparing: an unprimed flood answers "not yet known" for
                // every site, which would pass the no-offer half of this pair for the wrong reason.
                var brain = ctx.Companion.Brain;
                brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
                // Thrown away and flooded again rather than driven to completion: the chamber and its shaft
                // were built after the shared setup had already flooded an open floor, and a loop that runs
                // while the region is incomplete does nothing at all when the stale region is complete. That
                // is the quietest way a fixture primes nothing and looks primed.
                VerifyOreWork.ResettleReach(ctx);
                var light = new LightUsefulArea();
                // Prepared until the search resolves, not once. One discovery search asks a bounded number of
                // sites about their approach, so a single preparation on a scene with many dark tiles measures
                // that budget rather than the verdict, and reports Unresolved with the budget as its reason —
                // which is honest about that search and says nothing about the pit. The live brain re-asks in a
                // rescore for exactly this reason, and each search leaves its refusals behind, so the answer
                // arrives within a few. An Unresolved offer is never a pass here: the loop falls through to the
                // assertions with whatever it last read, so a search that never resolves fails on its reason.
                float score = 0f;
                for (int i = 0; i < 30; i++)
                {
                    score = VerifyPreparedActivities.PrepareAndScore(light, ctx);
                    if (light.Eligibility != Offer.Unresolved) break;
                    VerifyObservedMotion.SetTick(Main.GameUpdateCount + (ulong)Weights.NearbyWorkUnresolvedRetryTicks);
                }
                // The reach flags are in the ledger because every refusal this row can print is downstream of
                // them, and without them a red says which answer came back but not which flood produced it.
                string ledger = $"open={open}: score={score:0.000} offer={light.Eligibility}/{light.EligibilityReason} target={light.ActivityTarget}"
                    + $" [reach complete={brain.Senses.Reach.Complete} corners={brain.Senses.Reach.CornerCount} tiles={brain.Senses.Reach.AnyCount}]"
                    // Which sites the search actually walked and what it said about each: the two refusals this row can
                    // produce — nothing reachable, and nothing the torch placer will attach to — are indistinguishable
                    // from the score alone, and they point at completely different halves of the scene.
                    + $" asked={light.LastSearchAsked} sites={light.LastSearchSites}"
                    // The three gates between "a dark region exists" and "a site was asked about", read straight
                    // from the scene so a red says which one closed: the regions the sense holds, whether a tile
                    // in the middle of the chamber reads dark to the same measurement the site test uses, and
                    // whether the torch policy will take it at all.
                    + $" regions=[{string.Join(" ", brain.Senses.Light.DarkRegionsNearest(MovementQueries.Tile(ctx.Npc.Center), 64).Select(r => $"{r.Centre}:{r.DarkSamples}"))}]"
                    + $" chamberDark={brain.Senses.Light.MeasuredAround(new Point((PitLeft + PitRight) / 2, PitFloor - 2), 4, 1)}"
                    + $" chamberCandidate={(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.PlaceSuppliedTorches.Candidate(new Point((PitLeft + PitRight) / 2, PitFloor - 2)))}";
                if (!open)
                {
                    Require(score == 0 && light.ActivityTarget == null,
                        $"a dark site behind a shaft the body does not fit through must not be offered; {ledger}");
                    continue;
                }
                Require(score > 0 && light.Eligibility == Offer.Usable && light.ActivityTarget is Vector2,
                    $"the same dark chamber behind a two-wide shaft must be a lighting opportunity; {ledger}");
                Point site = light.ActivityTarget!.Value.ToTileCoordinates();
                Require(site.X >= PitLeft && site.Y > FloorRow,
                    $"the premise needs the offered site down in the chamber, not on the floor above it; site={site}; {ledger}");
            }
    }

    /// <summary>The pot analogue of <see cref="DarknessInAnUnreachableChamber"/>: a pot on the chamber floor, pot breaking on.</summary>
    private static void APotInAnUnreachableChamber()
    {
        foreach (bool open in new[] { false, true })
            {
                var ctx = BuildIsland(open);
                Preferences.Current.PotBreaking = true;
                Point pot = PlacePot(new Point(30, PitFloor - 2));
                ctx.Senses.Loot.Pickups.Clear();
                // After the chamber is built, because the shared setup flooded an open floor that this scene
                // has since replaced. Collection's pot approach reads that region rather than searching, so
                // without this the chamber is judged against a world with no chamber in it.
                VerifyOreWork.ResettleReach(ctx);
                var collect = new CollectNearbyItems();
                collect.Prepare(ctx);
                string ledger = $"open={open}: method={collect.Method} value={collect.Score():0.000} offer={collect.Eligibility}/{collect.EligibilityReason} target={collect.ActivityIdentity}";
                if (!open)
                {
                    Require(collect.Score() == 0 && collect.Method == "none",
                        $"a pot behind a shaft the body does not fit through must not be offered; {ledger}");
                    continue;
                }
                Require(collect.Score() > 0 && collect.Method == "potential-pot-contents" && collect.ActivityIdentity is Point target
                    && Math.Abs(target.X - pot.X) <= 1 && Math.Abs(target.Y - pot.Y) <= 1,
                    $"the same pot behind a two-wide shaft must be offered; {ledger}");
            }
    }

    /// <summary>
    /// A floor with a sealed chamber under it, and a shaft through the floor into that chamber which is two tiles wide when
    /// <paramref name="open"/> and one when not. The premise is asserted in the body's own terms rather than by a trip query: the
    /// companion's own cell must be reachable, and the chamber floor must be reachable exactly when the shaft is the wide one.
    /// </summary>
    internal static ActionContext BuildIsland(bool open)
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
                Main.tile[x, y].ClearEverything();
        // The floor the actors stand on, which is also the chamber's ceiling.
        for (int x = IslandLeft; x <= IslandRight; x++) VerifyOreWork.Place(new Point(x, FloorRow), TileID.Dirt);
        // The chamber, cut out of solid rock rather than fenced off by single-tile walls.
        //
        // The walls used to be one tile thick with open air beyond them, which was enough for a walking body:
        // the air outside was unreachable floor and sky, and nothing sampled it. It is not enough for the site
        // test, which asks whether a candidate's whole neighbourhood is dark and takes that mean over open air
        // wherever it is, straight through a tile of rock. So six of the forty-two tiles it sampled around the
        // middle of the chamber were the lit air a few rows under the one-tile floor, the mean came back at 0.15
        // against a chamber reading 0.02, and every site in a demonstrably dark chamber was vetoed as lit — the
        // row failed with a found region, a placeable tile and zero sites asked. A shell thicker than the test's
        // own radius is what makes the chamber's darkness local.
        for (int x = PitLeft - 8; x <= PitRight + 8; x++)
            for (int y = FloorRow + 1; y <= PitFloor + 8; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
        for (int x = PitLeft; x <= PitRight; x++)
            for (int y = FloorRow + 1; y < PitFloor; y++)
                Main.tile[x, y].ClearEverything();
        // The shaft. One tile of opening puts no usable corner anywhere in it, in either row, because every
        // corner inside it has a wall among its four tiles; two tiles put one down its middle.
        for (int x = ShaftLeft; x < ShaftLeft + (open ? 2 : 1); x++) Main.tile[x, FloorRow].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Motor);
        VerifyOreWork.ResettleReach(ctx);
        var reach = ctx.Companion.Brain.Senses.Reach;
        Require(reach.Complete, "the premise needs a settled flood before it can say what is reachable");
        Require(reach.Reachable(new Point(20, FloorRow - 1)) == ReachVerdict.Reachable,
            "the floor above must hold the companion");
        Require(reach.Reachable(new Point(30, PitFloor - 1)) == (open ? ReachVerdict.Reachable : ReachVerdict.Unreachable),
            $"the chamber must be reachable exactly when the shaft is two tiles wide; open={open} "
            + $"verdict={reach.Reachable(new Point(30, PitFloor - 1))}");
        return ctx;
    }

    internal static void GiveTorches(ActionContext ctx)
    {
        Item supply = new();
        supply.SetDefaults(ItemID.Torch);
        supply.stack = 20;
        ctx.Player.inventory[0] = supply;
        ctx.Player.selectedItem = 1;
    }

    /// <summary>
    /// Lit everywhere except inside the chamber, so the chamber is the only place lighting can want to work.
    ///
    /// <para>This used to be a lit disc over the island with everything else dark, and for the walking body that
    /// was enough: the dark it left behind was floor the walker could not stand on and sky it could not enter, so
    /// the chamber was the only dark site it could ever be offered. A body that flies reaches all of it. The disc
    /// covers columns 12 to 28 and the island floor runs to 41, so the dark floor east of the disc — and the whole
    /// dark sky above it — would each be a reachable lighting site, and the sealed half of the pair would have been
    /// offered one of those and read as a pass for the chamber it never looked at. Inverting it states the scene's
    /// actual subject rather than relying on a body that cannot get anywhere.</para>
    /// </summary>
    internal static void LightIslandOnly()
        => VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100),
            (x, y) => x >= PitLeft && x <= PitRight && y > FloorRow && y < PitFloor ? .02f : .9f);

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
        return origin;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
