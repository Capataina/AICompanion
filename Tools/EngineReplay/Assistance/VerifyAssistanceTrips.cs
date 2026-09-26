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
            try { red += RunOneRow.GreenOrRed(name, fixture); }
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
        Each("hover pot: a pot no floor pose reaches is a collection trip, broken from a hover beside it and credited to collection", () => ASiteNoFloorPoseReaches(lighting: false));
        Each("J08 a lighting trip breaks a permitted pot in passing, with no detour and no second movement owner", () => ALightingTripPassesAPot(potBreaking: true));
        Each("J08 the same pot with pot breaking disabled is left alone", () => ALightingTripPassesAPot(potBreaking: false));
        Each("J08 a tick whose planning allowance is spent skips the incidental scan without losing its turn", AnExpiredAllowanceDefersTheIncidentalScan);
        Each("J08 a pot the course binds is a trip collection performs: an attempt, the break and its credit", APotIsATripCollectionPerforms);
        Each("A2 the hand walks to the drop the course bound, not the nearer one its own search would take", TheHandTakesTheBoundDrop);
        Each("A2 the hand breaks the pot the course bound, not the nearer one its own search would take", TheHandBreaksTheBoundPot);
        Each("A3 a pot in reach during a recognised encounter is not broken in passing; the same pot with no encounter is", AnEncounterRefusesAPotInPassing);
        Each("A3 a pot the published course holds as a step is left to the course, not broken in passing", APlannedPotIsLeftToTheCourse);
        Each("torch placement switched off orders no light site; the same dark shelf with it on is bound", TorchPlacementOffOrdersNoLight);
        Each("a torch in passing within spacing of a light site the course holds is refused; the same site with no course is not refused for it",
            ATorchInPassingDoesNotSpoilAHeldLightSite);
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
        Require(footprint.Any(t => FindToolAccess.InReach(feet, t)), $"the pot must be within reach where the companion stands; feet={feet} pot={pot}");
        // The scan proposes the census's own sites to the course, so the course needs an observation holding the pot.
        VerifyOreWork.ResettleReach(ctx);
        ObserveForTheCourse(ctx);
        var incidental = new ConsiderIncidentalInteractions();
        LimitPlanningWork.Unbounded = false;
        try
        {
            LimitPlanningWork.Restart(0.000001);
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
    /// A pot the course binds is a trip collection performs, driven through the whole tick: the census admits
    /// the pot as collection work, the course binds it, collection holds the body, the hand breaks exactly that
    /// pot, and collection's attempt completes with the break as its one productive effect.
    ///
    /// The scene removes every rival — torch placement off, both work policies disabled, no hostiles and no
    /// drops — so the pot is the only usable opportunity in the world. Until 23 September 2026 this row asserted
    /// the opposite, that a pot was never a course step, because no activity performed one: a bound pot flew the
    /// body with no attempt and no credit (`3826d32` measured thirty ticks of `bound=pot-target:tile:27,58
    /// action=none`). The owner then ruled a pot a collection task, so the same scene asks for the trip — and the
    /// thing it guards is unchanged in kind: a bound step that nothing performs, or that a hand performs against a
    /// target of its own choosing, fails here by name.
    ///
    /// The premise is the load-bearing part: a scene where the pot is never admitted would fail for a reason that
    /// has nothing to do with the performer, so the admission is asserted first.
    /// </summary>
    /// <summary>
    /// Restores what the scene below writes, because none of it is this case's to leave behind.
    ///
    /// The per-case reset gives the next *case* fresh preferences, a rebuilt tile map and empty actor
    /// slots; it runs between cases and this file is one case with nine rows in it, so anything this row
    /// writes is still standing when the next row of the same file runs. The preferences are the
    /// documented instance of that class — `VerifyOreWork`'s sealed-tree row once read a chopping policy
    /// a cooperation row had left — and the pot tiles are terrain, which is the other one the per-case
    /// rebuild covers and an in-case run does not. Restored to **what was found**, never to a literal,
    /// for the same reason the deadline regime is: a literal here is the current default written down a
    /// second place, and it goes stale silently.
    /// </summary>
    private static void APotIsATripCollectionPerforms()
    {
        bool torchPlacement = Preferences.Current.TorchPlacement, potBreaking = Preferences.Current.PotBreaking;
        Policy chopping = Preferences.Current.Chopping;
        // `Main.player[0]` is the player every row here gets from `VerifyOreWork.SetUp`, and SetUp does
        // not re-seed its inventory — so emptying it reaches every later row of this case. The companion
        // and its bag are not on that list: `SetUp` builds a fresh companion per row, so the bag this
        // scene empties is its own and nobody else's.
        Item[] inventory = (Item[])Main.player[0].inventory.Clone();
        try { RunThePotOnlyScene(); }
        finally
        {
            Preferences.Current.TorchPlacement = torchPlacement;
            Preferences.Current.PotBreaking = potBreaking;
            Preferences.Current.Chopping = chopping;
            for (int i = 0; i < inventory.Length && i < Main.player[0].inventory.Length; i++)
                Main.player[0].inventory[i] = inventory[i];
            for (int i = 0; i < 4; i++)
                Main.tile[27 + i % 2, FloorRow - 2 + i / 2].ClearEverything();
        }
    }

    private static void RunThePotOnlyScene()
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        Preferences.Current.TorchPlacement = false;
        Preferences.Current.PotBreaking = true;
        Preferences.Current.Chopping = Policy.Disabled;
        Point pot = PlacePot(new Point(27, FloorRow - 2));
        TerrainChanges.Reset();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
        Require(footprint.All(t => !FindToolAccess.InReach(ctx.Npc.Bottom, t)),
            "the pot must be out of reach where the run starts, or the incidental scan takes it before any course could");

        var brain = ctx.Companion.Brain;
        bool admitted = false;
        string bound = "", actionWhileBound = "";
        int boundAt = -1, brokenAt = -1;
        for (int tick = 0; tick < 600 && brokenAt < 0; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.Course.Candidates.Any(c => c.Key.Purpose == "break-pot" && c.Admission == live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.OpportunityAdmission.KnownUsable))
                admitted = true;
            if (brain.Course.Last.Binding is { } step && step.Opportunity.Purpose == "break-pot")
            {
                if (boundAt < 0) { bound = step.Opportunity.ToString(); boundAt = tick; }
                actionWhileBound = brain.LastAction?.Name ?? "none";
            }
            if (footprint.Any(t => !Main.tile[t.X, t.Y].HasTile)) brokenAt = tick;
        }
        // One more tick, so the replacement that follows a finished pot closes its attempt.
        for (int tick = 0; tick < 30; tick++) VerifyOreWork.AdvanceBrain(ctx);
        string attempts = string.Join("; ", brain.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}:effects={a.ProductiveEffects}"));
        string ledger = $"bound={bound} at tick {boundAt}; action while bound={actionWhileBound}; broken at tick {brokenAt}; attempts=[{attempts}]";
        Require(admitted, $"premise: the census never admitted this pot as usable collection work; {ledger}");
        Require(boundAt >= 0, $"the course never bound the only usable work in the world, a pot; {ledger}");
        Require(actionWhileBound == "collect",
            $"a bound pot must be performed by collection, and '{actionWhileBound}' held the body — a step with no performer is a "
            + $"trip with no attempt and no credit; {ledger}");
        Require(brokenAt >= 0, $"the pot collection flew to was never broken; {ledger}");
        Require(brain.Activity.RecentAttempts.Any(a => a.Activity == "collect" && a.Status.ToString() == "Complete" && a.ProductiveEffects == 1
                && a.Cause == "pot-broken-contents-unobserved"),
            $"collection's attempt must complete with the broken pot as its one productive effect; {ledger}");
        Console.WriteLine($"pot trip: bound at tick {boundAt}, broken at tick {brokenAt} under collect (this machine, never asserted); world drops now {Main.item.Count(i => i.active)}");
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
        Vector2 start = ctx.Npc.Bottom;
        Require(footprint.All(t => !FindToolAccess.InReach(start, t)), "the pot must be out of reach where the walk starts, or nothing is passed");
        var brain = ctx.Companion.Brain;
        brain.Actions.RemoveAll(a => a.Name != "place-torches" && a.Name != "keep-company");
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
            // The premise the row always leaned on and never said: the course's first pick is the lighting trip. Only
            // lighting and keeping company are registered here, so a pot trip the course picks first has no performer and
            // the scene stalls. Measured on 26 September 2026 the two were within 0.0007 of each other before the distances
            // were fixed (lighting 0.7741, the pot's own trip 0.7734) and the pot leads after (0.7180 against 0.6796), so the
            // row had been passing on a tie; this makes a flip read as what it is rather than as a torch never placed.
            if (tick == 0 && potBreaking)
                Require(brain.Course.Last.Activity == "place-torches",
                    $"premise: the lighting trip must be the course's first pick, or nothing is passed on the way; picked {brain.Course.Last.Activity} for {brain.Course.Last.Binding?.Opportunity}");
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
        string attempts = string.Join("; ", brain.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}:effects={a.ProductiveEffects}"));
        // Lighting's own last word and the torch it is carrying, because every way this row fails runs through
        // one of them: a refusal names why no site was taken, and a shown torch means the field is discounting
        // the companion's own light in exactly the neighbourhood the sites are in.
        var lighting = brain.Actions.OfType<LightUsefulArea>().FirstOrDefault();
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
            // carried torch made every nearby site read lit: now that the field reads the world's own light, the
            // companion places a torch in passing while keeping company, which is the behaviour that reading
            // exists to produce and has nothing to do with the pot this row is about.
            Require(brokenAt < 0, $"a pot with pot breaking disabled must be left alone; {ledger}");
            Require(lastIncidental == null || !lastIncidental.ToString()!.Contains("Method = collect"),
                $"no incidental interaction may reach for the pot while pot breaking is disabled; {ledger}");
            return;
        }
        Require(brokenAt >= 0 && actionAtBreak == "place-torches" && feetAtBreak > start.X + 32f,
            $"a permitted pot in reach must be broken in passing, during the lighting trip and after the walk began; {ledger}");
        Require(before == atBreak, $"breaking the pot must change neither the request nor the movement owner; {ledger}");
        Require(brain.Activity.RecentAttempts.Any(a => a.Activity == "place-torches" && a.Status.ToString() == "Complete" && a.ProductiveEffects == 1),
            $"the lighting attempt must complete with its torch as its only productive effect; the pot is credited to no activity; {ledger}");
        if (incidental != null)
            Require(incidentalAtBreak != null && incidentalAtBreak.ToString()!.Contains("Method = collect") && incidentalAtBreak.ToString()!.Contains("DuringActivity = place-torches"),
                $"the incidental record must name the pot method and the activity it happened during; {ledger}");
        // The effect audit holds a pot break to the step it was accepted for, so the scan must announce the accepted
        // in-passing step to it before the native call; unannounced, every licensed pot read as an effect with no binding.
        var announced = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts.AcceptedIncidental;
        Require(announced is { Incidental: true } held && held.TileX == pot.X && held.TileY == pot.Y,
            $"the pot broken in passing must be announced to the effect audit as its accepted step; announced={announced}; {ledger}");
        Console.WriteLine($"incidental pot: broken at tick {brokenAt} during place-torches, torch at tick {torchAt}; decide max {decideMax:0.000} ms, finalise max {finaliseMax:0.000} ms (this machine, never asserted)");
    }

    /// <summary>
    /// A torch placed in passing within the placer's spacing of a light site the course holds would make the placer refuse
    /// that held site, replacing the running course from the hand. The shelf's two dark sites are one tile apart, so whichever
    /// the course holds, the other is within spacing of it: asked about in passing it must be refused by name, and asked again
    /// once the course is released it must not be refused for that reason — without the second half the first passes on a
    /// course that refuses every torch in passing.
    /// </summary>
    private static void ATorchInPassingDoesNotSpoilAHeldLightSite()
    {
        int shelfRow = 51;
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        VerifyOreWork.Place(new Point(31, shelfRow), TileID.Dirt);
        VerifyOreWork.Place(new Point(32, shelfRow), TileID.Dirt);
        GiveTorches(ctx);
        Preferences.Current.TorchPlacement = true;
        VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (x, y) => y is >= 57 and <= 60 ? .9f : .02f);
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        VerifyOreWork.ResettleReach(ctx);
        ObserveForTheCourse(ctx, keepThePublishedCourse: true);
        var course = ctx.Companion.Brain.Course;
        var held = course.Course.Current?.Projection.Steps.FirstOrDefault(step => step.Opportunity.Domain == "light-target");
        Require(held != null, "premise: the course must hold a light step on the dark shelf");
        // A light site's identity is `tile:x,y`; the mod's own reader of it is internal to the mod.
        static Point TileOf(string identity)
        {
            string[] parts = identity["tile:".Length..].Split(',');
            return new Point(int.Parse(parts[0]), int.Parse(parts[1]));
        }
        Point heldTile = TileOf(held!.Opportunity.Target);
        var neighbour = course.Facts!.Facts.Select(fact => fact.Key)
            .Where(key => key.Kind == "light-target" && key.Identity != held!.Opportunity.Target)
            .Select(key => (Key: key, Tile: TileOf(key.Identity)))
            .Where(site => Math.Abs(site.Tile.X - heldTile.X) <= 8 && Math.Abs(site.Tile.Y - heldTile.Y) <= 8)
            .Select(site => (live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.FactKey?)site.Key).FirstOrDefault();
        Require(neighbour != null, $"premise: a published light site within spacing of the held one at {heldTile}");
        var answer = course.AcceptIncidental(ctx, neighbour!.Value);
        Require(answer.Reason == live::AICompanion.Companion.Brain.Infrastructure.Selection.DecideCourseEachTick.IncidentalTorchSpoilsHeldSite,
            $"a torch in passing at {neighbour.Value.Identity} within spacing of the held {held!.Opportunity.Target} must be refused; answered {answer.Reason}");
        course.Course.Release("fixture-drops-the-held-site");
        var unheld = course.AcceptIncidental(ctx, neighbour.Value);
        Require(unheld.Reason != live::AICompanion.Companion.Brain.Infrastructure.Selection.DecideCourseEachTick.IncidentalTorchSpoilsHeldSite,
            $"control: with no course holding a light site the same torch must not be refused for spoiling one; answered {unheld.Reason}");
        Console.WriteLine($"torch in passing: {neighbour.Value.Identity} refused beside held {held.Opportunity.Target}; unheld answered {unheld.Reason}");
    }

    /// <summary>
    /// The shelf scene's dark sites with torch placement switched off. The census is the only discovery and the lighting activity
    /// only the hand, so a census that published sites the hand's own switch refuses had the course bind a light step, fly to it
    /// and hover with nothing placed. Three hundred ticks off must bind no light step and place no torch; then, in the same scene,
    /// switching it on must bind one within as many ticks again — without that half the first passes on a scene that had nothing
    /// to light.
    /// </summary>
    private static void TorchPlacementOffOrdersNoLight()
    {
        int shelfRow = 51;
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        VerifyOreWork.Place(new Point(31, shelfRow), TileID.Dirt);
        VerifyOreWork.Place(new Point(32, shelfRow), TileID.Dirt);
        GiveTorches(ctx);
        Preferences.Current.TorchPlacement = false;
        VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (x, y) => y is >= 57 and <= 60 ? .9f : .02f);
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        VerifyOreWork.ResettleReach(ctx);
        var brain = ctx.Companion.Brain;
        bool AnyTorch() => Enumerable.Range(20, 40).Any(x => Enumerable.Range(40, 20).Any(y =>
            Main.tile[x, y].HasTile && TileID.Sets.Torch[Main.tile[x, y].TileType]));
        string lightStep = "";
        int boundAt = -1;
        for (int tick = 0; tick < 300 && boundAt < 0; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.Course.Last.Binding is { } step && step.Opportunity.Domain == "light-target")
            {
                lightStep = step.Opportunity.ToString(); boundAt = tick;
            }
        }
        Require(boundAt < 0 && !AnyTorch(),
            $"with torch placement off no light site may be ordered or lit; bound {lightStep} at tick {boundAt}, torch placed={AnyTorch()}, action={brain.LastAction?.Name}");
        Preferences.Current.TorchPlacement = true;
        for (int tick = 0; tick < 300 && boundAt < 0; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.Course.Last.Binding is { } step && step.Opportunity.Domain == "light-target") boundAt = tick;
        }
        Require(boundAt >= 0, $"control: with torch placement on the same dark shelf must be bound as a light step; action={brain.LastAction?.Name}");
        Console.WriteLine($"torch switch: off bound nothing in 300 ticks; on bound a light site at tick {boundAt}");
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
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
        brain.Actions.RemoveAll(a => a.Name != methodName && a.Name != "keep-company");
        var method = brain.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.PerformNearbyWorldWork>().Single();
        float score = VerifyPreparedActivities.PrepareAndScore(method, ctx);
        string offer = $"shelf row {shelfRow}: score={score:0.000} offer={method.Eligibility}/{method.EligibilityReason} target={method.ActivityIdentity}";
        Require(score > 0 && method.ActivityIdentity is Point, $"a site reachable from a hover beside it must be offered; {offer}");
        Point target = (Point)method.ActivityIdentity!;
        Require(target.Y <= shelfRow, $"the offered target must be a shelf site; target={target}; {offer}");

        // The pot is done when any tile of its footprint is gone: headless KillTile removes the tile struck, which is also what the
        // method's own Perform reads as a break, and a retried attempt may strike a different tile of the same pot than the first
        // preparation named. The rest of the two-by-two footprint is not this fixture's question.
        // A torch on any shelf site, not only on the one this activity's own search offered above: the site the hand
        // works is the one the course bound, and the two need not be the same tile of a two-tile shelf.
        bool Done() => lighting
            ? Enumerable.Range(29, 6).Any(x => Enumerable.Range(shelfRow - 3, 3).Any(y =>
                Main.tile[x, y].HasTile && TileID.Sets.Torch[Main.tile[x, y].TileType]))
            : Enumerable.Range(0, 4).Any(i => !Main.tile[interaction.X + i % 2, interaction.Y + i / 2].HasTile);
        int tick = 0;
        float highest = float.MaxValue, atDone = 0f;
        var trace = new System.Text.StringBuilder();
        string last = "";
        // Read from the published course each tick rather than reconstructed at the end: a course is
        // replaced as the run goes, so a pot bound for thirty ticks and released before the pot broke is
        // invisible to anything that only looks at the last one.
        string potStep = ""; int potStepAt = -1; string ownerAtDone = "none";
        for (; tick < 900 && !Done(); tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.Course.Last.Binding is { } step && step.Opportunity.Purpose == "break-pot" && potStepAt < 0)
            {
                potStep = step.Opportunity.ToString(); potStepAt = tick;
            }
            highest = MathF.Min(highest, ctx.Npc.Center.Y);
            atDone = ctx.Npc.Center.Y;
            ownerAtDone = brain.LastAction?.Name ?? "none";
            string now = $"{brain.LastAction?.Name}/{brain.LastRequest.Kind}";
            if (now != last && trace.Length < 1600) trace.Append($" t{tick}:{now}@{ctx.Npc.Center.X:0},{ctx.Npc.Center.Y:0}");
            last = now;
        }
        string attempts = string.Join("; ", brain.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}"));
        Require(Done(), $"the whole brain must {(lighting ? "place the torch" : "break the pot")} from a hover; ticks={tick} centre={ctx.Npc.Center} action={brain.LastAction?.Name} "
            + $"status={method.Eligibility}/{method.EligibilityReason} highest centre={highest} attempts=[{attempts}] trace:{trace}");
        // The tick it finished on, not the highest point it ever reached: a run that drifted upward once and then
        // did the work from the floor would satisfy a check on the highest point alone.
        Require(atDone < floorCentre - 16f,
            $"the interaction must be performed from a hover above the floor; centre at completion={atDone} floor centre={floorCentre}");
        // The two arms are performed by different routes now and the row says which, because they were one
        // assertion until 22 September 2026 and the pot arm quietly stopped meaning what it said.
        //
        // A torch is a trip: lighting is an activity, the course binds it, and an approach that gave up and
        // was retried would still finish the job eventually — so the attempts are read for a failed method
        // as well as the tile being read for the effect.
        //
        // Both arms are trips now. A pot was broken only in passing from 22 to 23 September 2026, because no activity
        // performed a bound pot; the owner then ruled a pot a collection task, so a pot the course binds is a trip
        // collection performs, the same shape as a torch trip lighting performs.
        Require(!brain.Activity.RecentAttempts.Any(a => a.Activity == methodName && a.Status.ToString() == "Failed"),
            $"the trip must reach its hover and work from it, never fail a method on the way; attempts=[{attempts}] trace:{trace}");
        if (!lighting)
        {
            // Three positive assertions, because each one alone is satisfied by a wrong route. The course bound the
            // pot — read off the published course while the run goes; collection owned the body on the tick the pot
            // broke, so the break was the bound step being performed and not a pass-by; and collection's attempt
            // completed with the break as its one productive effect, which is what makes the trip credited work.
            Require(potStepAt >= 0,
                $"no course ever bound the pot, so whatever broke it did not break it as a collection trip; attempts=[{attempts}] trace:{trace}");
            Require(ownerAtDone == methodName,
                $"the pot broke with '{ownerAtDone}' owning the body; a bound pot is performed by '{methodName}'; attempts=[{attempts}] trace:{trace}");
            for (int settle = 0; settle < 30; settle++) VerifyOreWork.AdvanceBrain(ctx);
            attempts = string.Join("; ", brain.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}:effects={a.ProductiveEffects}"));
            Require(brain.Activity.RecentAttempts.Any(a => a.Activity == methodName && a.Status.ToString() == "Complete" && a.ProductiveEffects == 1),
                $"collection's attempt must complete with the broken pot as its one productive effect; attempts=[{attempts}] trace:{trace}");
        }
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
                brain.Senses.Update(ctx.Npc, ctx.Player);
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
                    + $" chamberDark={brain.Senses.Light.ReadForPlacement(new Point((PitLeft + PitRight) / 2, PitFloor - 2), live::AICompanion.Companion.Brain.Infrastructure.Observation.LightSense.Coverage.Current())}"
                    + $" chamberCandidate={(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.PlaceTorches.Candidate(new Point((PitLeft + PitRight) / 2, PitFloor - 2)))}";
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
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

    /// <summary>
    /// Two drops on the floor, the near one where collection's own search would take it and the far one the course binds. The
    /// step is handed to collection the way the tick hands it — built by the real binder from the real census, then
    /// `OwnCurrentActivity.Select` — and the hand must walk to the bound drop: its request is the bound step's pose and the attempt
    /// claims the far drop and not the near one. Until 23 September 2026 the hand worked its own search's nearest drop whatever
    /// the course had bound, so the body flew to one drop while the attempt walked toward another.
    /// </summary>
    private static void TheHandTakesTheBoundDrop()
    {
        Item old7 = Main.item[7], old8 = Main.item[8];
        try
        {
            var ctx = VerifyCollectionContracts.SetUpFloor();
            Vector2 centre = ctx.Npc.Center;
            Item near = VerifyCollectionContracts.Drop(ItemID.CopperOre, 3, new Vector2(centre.X + 2 * 16, 60 * 16), 7);
            Item far = VerifyCollectionContracts.Drop(ItemID.CopperOre, 3, new Vector2(centre.X + 9 * 16, 60 * 16), 8);
            VerifyOreWork.ResettleReach(ctx);
            var (step, collect) = BindTheCensusSite(ctx, "item:8");
            collect.Prepare(ctx);
            Require(ReferenceEquals(collect.ActivityIdentity, near),
                $"premise: collection's own search must prefer the near drop, or the row cannot tell the two choosers apart; offer={collect.ActivityIdentity}");
            var owner = ctx.Companion.Brain.Activity;
            owner.Select(collect, ctx, step);
            owner.BeginExecution();
            var request = collect.Execute(ctx);
            string ledger = $"request={request.Kind}@{request.Anchor} step pose={step.Pose} near={near.Center} far={far.Center}";
            Require(Equals(owner.Current?.ActivityIdentity, step.Opportunity),
                $"the activity's purpose identity must be the bound opportunity, so a new target is a new attempt; identity={owner.Current?.ActivityIdentity}; {ledger}");
            Require(collect.ClaimsDrop(far) && !collect.ClaimsDrop(near),
                $"the attempt must walk toward the drop the course bound and never the one its own search preferred; {ledger}");
            Require(Math.Abs(request.Anchor.X - step.Pose.X) < 1 && Math.Abs(request.Anchor.Y - step.Pose.Y) < 1,
                $"the hand must ask for the bound step's own pose; {ledger}");
        }
        finally { Main.item[7] = old7; Main.item[8] = old8; }
    }

    /// <summary>
    /// Two pots both within the tool's reach of the body, the near one first in collection's own search and the far one bound by the
    /// course. The hand must break the far pot and leave the near one standing; a hand that searched for its own target breaks the
    /// near pot on the first swing.
    /// </summary>
    private static void TheHandBreaksTheBoundPot()
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        Preferences.Current.PotBreaking = true;
        Point body = MovementQueries.Tile(ctx.Npc.Center);
        Point near = PlacePot(new Point(body.X + 1, FloorRow - 2));
        Point far = PlacePot(new Point(body.X + 4, FloorRow - 2));
        Require(FindToolAccess.InReach(ctx.Npc.Center, near) && FindToolAccess.InReach(ctx.Npc.Center, far),
            $"premise: both pots must be within reach of the body, so the choice is the only difference; body={body} near={near} far={far}");
        VerifyOreWork.ResettleReach(ctx);
        var (step, collect) = BindTheCensusSite(ctx, $"tile:{far.X},{far.Y}");
        collect.Prepare(ctx);
        Require(collect.ActivityIdentity is Point offered && Math.Abs(offered.X - near.X) <= 1,
            $"premise: collection's own search must prefer the near pot; offer={collect.ActivityIdentity}");
        var owner = ctx.Companion.Brain.Activity;
        owner.Select(collect, ctx, step);
        owner.BeginExecution();
        for (int tick = 0; tick < 3; tick++) collect.Execute(ctx);
        bool Standing(Point origin) => Enumerable.Range(0, 4).All(i => Main.tile[origin.X + i % 2, origin.Y + i / 2].HasTile);
        string ledger = $"near {near} standing={Standing(near)}; far {far} standing={Standing(far)}; step={step.Opportunity}";
        Require(!Standing(far), $"the hand must break the pot the course bound; {ledger}");
        Require(Standing(near), $"the hand must leave the pot the course did not bind; {ledger}");
    }

    /// <summary>
    /// A permitted pot within reach of the body, proposed in passing by the grant-boundary scan: with no encounter the course accepts a
    /// one-step binding for it and the pot breaks, the effect carrying the binding's id; under a blood moon over the surface — a
    /// recognised encounter — the course refuses it by the same relevance that stops a course choosing optional work, and the pot
    /// stands. Until 23 September 2026 the scan broke the pot either way, because it priced nothing.
    /// </summary>
    private static void AnEncounterRefusesAPotInPassing()
    {
        bool bloodMoon = Main.bloodMoon;
        double surface = Main.worldSurface;
        try
        {
            foreach (bool encounter in new[] { true, false })
            {
                Point placeholder = new(60, FloorRow - 1);
                var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
                Main.tile[placeholder.X, placeholder.Y].ClearEverything();
                foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
                Preferences.Current.PotBreaking = true;
                Main.bloodMoon = encounter;
                // The game runs a blood moon only over players at or above the surface line.
                Main.worldSurface = encounter ? Main.maxTilesY : surface;
                Vector2 feet = ctx.Npc.Bottom;
                Point pot = PlacePot(new Point((int)(feet.X / 16f) + 1, FloorRow - 2));
                Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
                Require(footprint.Any(t => FindToolAccess.InReach(ctx.Npc.Center, t)), $"premise: the pot must be within reach; pot={pot}");
                VerifyOreWork.ResettleReach(ctx);
                ObserveForTheCourse(ctx);
                float intensity = ctx.Companion.Brain.Senses.Encounter.Intensity;
                Require(encounter ? intensity == 1f : intensity == 0f,
                    $"premise: the encounter sense must read {(encounter ? "a recognised blood moon" : "nothing")}; intensity={intensity} source={ctx.Companion.Brain.Senses.Encounter.Source}");
                var incidental = new ConsiderIncidentalInteractions();
                incidental.Consider(ctx, HandGrant.Available, false, null, 0);
                var answer = ctx.Companion.Brain.Course.LastIncidental;
                bool broken = footprint.Any(t => !Main.tile[t.X, t.Y].HasTile);
                string ledger = $"encounter={encounter} broken={broken} answer={answer} last={incidental.Last}";
                if (encounter)
                {
                    Require(!broken, $"a pot in reach must not be broken in passing during a recognised encounter; {ledger}");
                    Require(answer is { Accepted: false } refused && refused.Reason == live::AICompanion.Companion.Brain.Infrastructure.Selection.DecideCourseEachTick.OptionalWorkSuppressed,
                        $"the course must refuse the in-passing pot for the danger, by name; {ledger}");
                }
                else
                {
                    Require(broken, $"the same pot with no encounter must be broken in passing; {ledger}");
                    Require(answer is { Accepted: true } accepted && incidental.Last is { } done && done.BindingId == accepted.Binding!.Id,
                        $"the break must carry the id of the one-step binding the course accepted for it; {ledger}");
                }
            }
        }
        finally { Main.bloodMoon = bloodMoon; Main.worldSurface = surface; }
    }

    /// <summary>
    /// A permitted pot within reach that the course has just published as its own step. The in-passing scan must leave it to the course:
    /// breaking it here would make the course's next use invalid and replace the running course, which an in-passing acceptance may
    /// not do, and would take a step's benefit away from the activity that is about to perform it.
    /// </summary>
    private static void APlannedPotIsLeftToTheCourse()
    {
        Point placeholder = new(60, FloorRow - 1);
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        Preferences.Current.PotBreaking = true;
        Preferences.Current.TorchPlacement = false;
        Vector2 feet = ctx.Npc.Bottom;
        Point pot = PlacePot(new Point((int)(feet.X / 16f) + 1, FloorRow - 2));
        Point[] footprint = { pot, pot + new Point(1, 0), pot + new Point(0, 1), pot + new Point(1, 1) };
        VerifyOreWork.ResettleReach(ctx);
        ObserveForTheCourse(ctx, keepThePublishedCourse: true);
        var steps = ctx.Companion.Brain.Course.Course.Current?.Projection.Steps;
        Require(steps != null && steps.Any(s => s.Opportunity.Purpose == "break-pot" && s.Opportunity.Target == $"tile:{pot.X},{pot.Y}"),
            $"premise: the course must have published the pot as its own step; steps=[{(steps == null ? "none" : string.Join(", ", steps.Select(s => s.Opportunity.ToString())))}]");
        var incidental = new ConsiderIncidentalInteractions();
        incidental.Consider(ctx, HandGrant.Available, false, null, 0);
        Require(footprint.All(t => Main.tile[t.X, t.Y].HasTile) && incidental.Last == null,
            $"a pot the published course holds must be left to the course; last={incidental.Last} answer={ctx.Companion.Brain.Course.LastIncidental}");
    }

    /// <summary>
    /// The census's own site as a bound collection step, through the real capture, the real source and the real binder, and the
    /// brain's collection activity to hand it to. The binding is zero-travel: which target the hand works does not depend on how long
    /// the course priced the journey, and synthesising a travel answer here would be a model result nobody computed.
    /// </summary>
    private static (live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.StepBinding Step, CollectNearbyItems Collect)
        BindTheCensusSite(ActionContext ctx, string target)
        => (BindCensusSite(ctx, "collect-target", o => o.Key.Target == target, target),
            ctx.Companion.Brain.Actions.OfType<CollectNearbyItems>().Single());

    /// <summary>
    /// A census site as a bound step, through the real capture, the real source and the real binder, for any fixture that hands an
    /// assistance activity the step it performs. The first usable site matching <paramref name="pick"/> is bound; the binding is
    /// zero-travel, because which target the hand works does not depend on how long the course priced the journey and a travel
    /// answer synthesised here would be a model result nobody computed.
    /// </summary>
    /// <summary>A fresh observation of the census, as the course takes one every tick it carries a step.</summary>
    internal static live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.DecisionFactSnapshot ObserveTheCensus(ActionContext ctx)
    {
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        var capture = new live::AICompanion.Companion.Brain.Infrastructure.Observation.CaptureAssistanceOpportunities();
        return new live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.DecisionFactSnapshot(
            501, 1, ctx.Companion.Brain.Senses.Tick, 1, 0, capture.Capture(ctx.Companion.Brain.Senses, ctx));
    }

    /// <summary>Whether a bound step still holds against a fresh observation, by the binder's own `ValidateNextUse` — the check
    /// the course makes every tick it carries the step, and the one that retires an application whose target moved.</summary>
    internal static live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.BindingValidation Revalidate(ActionContext ctx,
        live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.StepBinding step)
        => new live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.AssistanceOpportunityBinder(step.Opportunity.Domain)
            .ValidateNextUse(step, ObserveTheCensus(ctx));

    /// <summary>The census's own fact for one site, as published on a fresh observation, or null when it published none.</summary>
    internal static live::AICompanion.Companion.Brain.Infrastructure.Observation.AssistanceOpportunityFact? CensusFact(ActionContext ctx, string domain, string target)
    {
        var snapshot = ObserveTheCensus(ctx);
        var fact = snapshot.Facts.FirstOrDefault(f => f.Key.Kind == domain && f.Key.Identity == target);
        return fact?.Value.Text is { Length: > 0 } text
            ? System.Text.Json.JsonSerializer.Deserialize<live::AICompanion.Companion.Brain.Infrastructure.Observation.AssistanceOpportunityFact>(text)
            : null;
    }

    internal static live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.StepBinding BindCensusSite(ActionContext ctx, string domain,
        Func<live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.Opportunity, bool> pick, string wanted)
    {
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        var capture = new live::AICompanion.Companion.Brain.Infrastructure.Observation.CaptureAssistanceOpportunities();
        var snapshot = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.DecisionFactSnapshot(
            501, 1, ctx.Companion.Brain.Senses.Tick, 1, 0, capture.Capture(ctx.Companion.Brain.Senses, ctx));
        var source = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.DiscoverAssistanceOpportunities(domain);
        var found = source.Continue(snapshot, new(), new(double.PositiveInfinity)).Examined;
        var usable = live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.OpportunityAdmission.KnownUsable;
        var site = found.FirstOrDefault(o => o.Admission == usable && pick(o))
            ?? throw new InvalidOperationException($"premise: the census admitted no usable {domain} site {wanted}; found=[{string.Join(", ", found.Take(12).Select(o => o.Key.Target + ":" + o.Admission + "/" + o.Reason))}]");
        var bound = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities.AssistanceOpportunityBinder(domain)
            .BindInPlace(site, snapshot, new(ctx.Npc.Center.X, ctx.Npc.Center.Y));
        return bound.Binding ?? throw new InvalidOperationException($"premise: the binder refused {site.Key}: {bound.Reason}");
    }

    /// <summary>
    /// Freeze one observation for the course without running a tick, so a row that drives the in-passing scan directly has the
    /// census the scan proposes from and the course accepts against. The decision it starts is the course's own business; the row
    /// selects no activity, so nothing performs whatever it binds.
    /// </summary>
    internal static void ObserveForTheCourse(ActionContext ctx, bool keepThePublishedCourse = false)
    {
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        // Owned the way the tick owns it, because travel and the census borrow the standing allowance rather than minting one.
        var budget = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.DecisionWorkBudget(double.PositiveInfinity);
        var allowance = LimitPlanningWork.Own(budget);
        try { ctx.Companion.Brain.Course.Decide(ctx, ctx.Companion.Combat, null, budget); }
        finally { allowance.Dispose(); }
        // The decision may publish a course that plans the very pot the row proposes in passing, and the scan leaves a site the course
        // holds to the course. The rows using this are about a body passing work nobody planned, so the published course is released
        // and only the frozen observation is kept.
        if (!keepThePublishedCourse) ctx.Companion.Brain.Course.Course.Release("fixture-passes-unplanned-work");
        Require(ctx.Companion.Brain.Course.Facts != null, "premise: the course froze no observation");
    }

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
