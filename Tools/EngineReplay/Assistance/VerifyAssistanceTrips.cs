extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Light;
using Terraria.ID;
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
using CollectNearbyItems = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Offer = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Reach = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.Reach;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using Breath = live::AICompanion.Companion.CharacterBody.CompanionBreath;
using BreathEnvelope = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.BreathEnvelope;
using HandGrant = live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant;
using ConsiderIncidentalInteractions = live::AICompanion.Companion.Brain.Infrastructure.Grants.ConsiderIncidentalInteractions;

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
        Each("hop torch: a site reachable only by a hop from a take-off elsewhere is offered and placed", () => AHopFromATakeOffElsewhere(lighting: true, reachable: true));
        Each("hop torch: no take-off, no offer", () => AHopFromATakeOffElsewhere(lighting: true, reachable: false));
        Each("hop pot: a pot reachable only by a hop from a take-off elsewhere is offered and broken", () => AHopFromATakeOffElsewhere(lighting: false, reachable: true));
        Each("hop pot: no take-off, no offer", () => AHopFromATakeOffElsewhere(lighting: false, reachable: false));
        Each("J08 a lighting trip breaks a permitted pot in passing, with no detour and no second movement owner", () => ALightingTripPassesAPot(potBreaking: true));
        Each("J08 the same pot with pot breaking disabled is left alone", () => ALightingTripPassesAPot(potBreaking: false));
        Each("J08 a tick whose planning allowance is spent skips the incidental scan without losing its turn", AnExpiredAllowanceDefersTheIncidentalScan);
        if (red == 0) Console.WriteLine("assistance trips: lighting and pot trips require a way back, hop from take-offs the walker reaches, and a pot on the way breaks incidentally only when permitted");
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
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
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
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
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
        object? incidental = brain.GetType().GetField("Incidental")?.GetValue(brain);
        object? lastIncidental = incidental?.GetType().GetProperty("Last")?.GetValue(incidental);
        string ledger = $"potBreaking={potBreaking} torch={torch} at tick {torchAt}; pot broken at tick {brokenAt} during {actionAtBreak} feet x={feetAtBreak:0} (start {start.X:0}); "
            + $"request/owner before={before} at break={atBreak}; incidental={lastIncidental}; {lightState}; attempts=[{attempts}]";
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
            Require(lastIncidental != null && lastIncidental.ToString()!.Contains("Method = collect") && lastIncidental.ToString()!.Contains("DuringActivity = place-torches"),
                $"the incidental record must name the pot method and the activity it happened during; {ledger}");
        Console.WriteLine($"incidental pot: broken at tick {brokenAt} during place-torches, torch at tick {torchAt}; decide max {decideMax:0.000} ms, finalise max {finaliseMax:0.000} ms (this machine, never asserted)");
    }

    /// <summary>
    /// A two-tile shelf eleven columns from the companion, with a pot on it or its top as the only dark torch sites. At row 51
    /// its sites are above standing reach from every floor pose and within a ground jump's rise from a take-off under it; at
    /// row 30 no take-off exists. From where the companion starts no jump reaches either, so the only method is walking to a
    /// take-off and hopping, which mining already uses. The reachable shelf must be offered and then performed by the whole
    /// brain with the chosen method and keeping company as the only registered activities; the high shelf must not be offered.
    /// </summary>
    private static void AHopFromATakeOffElsewhere(bool lighting, bool reachable)
    {
        int shelfRow = reachable ? 51 : 30;
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
        AStar.InvalidateEdges();
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        var brain = ctx.Companion.Brain;
        // The flood has to have settled before anything asks an access question, because every one of them
        // now reads it: an unfinished flood answers "not yet known" for the take-off and both the premise
        // rows below and the offer itself would read for a reason that has nothing to do with the hop this
        // row is about. It used to be enough to settle it before the preparation alone, when the premise
        // rows ran their own searches.
        VerifyOreWork.ResettleReach(ctx);
        Require(brain.Positioner.ReachComplete, $"this row needs a settled reach region before it asks anything; shelf row {shelfRow}");
        var reach = brain.Senses.Reach;
        var body = ctx.Companion.Motor.State;
        Require(FindToolAccess.Approach(interaction, ctx.Npc.Bottom, reach, out _) == Reach.No
            && !live::AICompanion.Companion.Brain.Infrastructure.Movement.ProveInteractionJump.CanReach(
                live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World, body, b => FindToolAccess.InReach(b.Feet, interaction)),
            $"the shelf must be out of standing reach from every floor pose and out of a jump from where the companion starts; shelf row {shelfRow}");
        var hop = FindToolAccess.HopApproach(interaction, body, reach, out Vector2 takeOff);
        Require(hop == (reachable ? Reach.Yes : Reach.No),
            $"the premise needs a take-off exactly when the shelf is low; shelf row {shelfRow} hop={hop} take-off={takeOff}");

        string methodName = lighting ? "place-torches" : "collect";
        brain.Chooser.Actions.RemoveAll(a => a.Name != methodName && a.Name != "keep-company");
        var method = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.NearbyAssistance.PerformNearbyWorldWork>().Single();
        float score = VerifyPreparedActivities.PrepareAndScore(method, ctx);
        string offer = $"shelf row {shelfRow}: score={score:0.000} offer={method.Eligibility}/{method.EligibilityReason} target={method.ActivityIdentity}";
        if (!reachable)
        {
            Require(score == 0 && method.ActivityIdentity == null, $"a site no take-off reaches must not be offered; {offer}");
            return;
        }
        Require(score > 0 && method.ActivityIdentity is Point, $"a site reached by a hop from a take-off the walker reaches must be offered; {offer}");
        Point target = (Point)method.ActivityIdentity!;
        // A site on top of the shelf or beside it on the shelf's own row; what matters is that no standing pose reaches it.
        Require(FindToolAccess.Approach(target, ctx.Npc.Bottom, reach, out _) == Reach.No && target.Y <= shelfRow,
            $"the offered target must be a shelf site with no standing pose; target={target}; {offer}");

        // The pot is done when any tile of its footprint is gone: headless KillTile removes the tile struck, which is also what the
        // method's own Perform reads as a break, and a retried attempt may strike a different tile of the same pot than the first
        // preparation named. The rest of the two-by-two footprint is not this fixture's question.
        bool Done() => lighting
            ? Main.tile[target.X, target.Y].HasTile && TileID.Sets.Torch[Main.tile[target.X, target.Y].TileType]
            : Enumerable.Range(0, 4).Any(i => !Main.tile[interaction.X + i % 2, interaction.Y + i / 2].HasTile);
        int tick = 0;
        float highestFeet = float.MaxValue, lowestFeet = 0f;
        var trace = new System.Text.StringBuilder();
        string last = "";
        for (; tick < 900 && !Done(); tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            highestFeet = MathF.Min(highestFeet, ctx.Npc.Bottom.Y);
            lowestFeet = MathF.Max(lowestFeet, ctx.Npc.Bottom.Y);
            string now = $"{brain.LastAction?.Name}/{brain.LastRequest.Kind}/jump={brain.LastRequest.JumpScale:0.0}/ground={ctx.Companion.Motor.State.OnGround}";
            if (now != last && trace.Length < 1600) trace.Append($" t{tick}:{now}@{ctx.Npc.Bottom.X:0},{ctx.Npc.Bottom.Y:0}");
            last = now;
        }
        string attempts = string.Join("; ", brain.Chooser.Activity.RecentAttempts.Select(a => $"{a.Activity}:{a.Status}:{a.Cause}"));
        Require(Done(), $"the whole brain must {(lighting ? "place the torch" : "break the pot")} from a hop; ticks={tick} feet={ctx.Npc.Bottom} action={brain.LastAction?.Name} "
            + $"status={method.Eligibility}/{method.EligibilityReason} highest feet={highestFeet} lowest feet={lowestFeet} attempts=[{attempts}] trace:{trace}");
        Require(lowestFeet <= FloorRow * 16 + 0.5f && highestFeet < FloorRow * 16 - 8f,
            $"the interaction must come from a jump off the floor, never from below it; highest feet={highestFeet} lowest feet={lowestFeet}");
        // One trip, performed: a take-off counted as reached one tile short proves the jump from the wrong pose and ends the attempt
        // as a lost take-off, which a fixture watching only the final tile would pass after enough retries.
        Require(!brain.Chooser.Activity.RecentAttempts.Any(a => a.Activity == methodName && a.Cause == "interaction-jump-lost-take-off"),
            $"the walk must reach the take-off itself before proving and jumping, never lose it on arrival; attempts=[{attempts}] trace:{trace}");
        Console.WriteLine($"hop {(lighting ? "torch" : "pot")}: performed at tick {tick} from take-off {takeOff}, highest feet {highestFeet:0}");
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
                // Lighting reads the light field and the reach region, so the scene is observed and its
                // region primed to settle before preparing: an unprimed flood answers "not yet known" for
                // every site, which would pass the no-offer half of this pair for the wrong reason.
                var brain = ctx.Companion.Brain;
                brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
                // Thrown away and flooded again rather than driven to completion: the island and its staircase
                // were built after the shared setup had already flooded an open floor, and a loop that runs
                // while the region is incomplete does nothing at all when the stale region is complete. That
                // is the quietest way a fixture primes nothing and looks primed, and it is why the staircase
                // half of this pair read the pit as unreturnable with the staircase standing in it.
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
                string ledger = $"staircase={staircase} oneWay={oneWay}: score={score:0.000} offer={light.Eligibility}/{light.EligibilityReason} target={light.ActivityTarget}"
                    + $" [two-way complete={brain.Senses.Reach.Complete} scored complete={brain.Senses.Reach.ScoredComplete}"
                    + $" player-one-way={brain.Senses.Reach.PlayerOnlyOneWay} two-way={brain.Senses.Reach.TwoWayCount} any={brain.Senses.Reach.AnyCount}]";
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
                // After the one-way rule is set, because the flood is run under it, and after the island is
                // built, because the shared setup flooded an open floor that this scene has since replaced.
                // Collection's pot approach reads that region rather than searching, so without this the pit
                // is judged against a world with no pit in it.
                VerifyOreWork.ResettleReach(ctx);
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
