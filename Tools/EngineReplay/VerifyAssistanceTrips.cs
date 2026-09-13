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
using FindToolAccess = live::AICompanion.Companion.Brain.WorldInteractions.FindToolAccess;
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
        Each("hop torch: a site reachable only by a hop from a take-off elsewhere is offered and placed", () => AHopFromATakeOffElsewhere(lighting: true, reachable: true));
        Each("hop torch: no take-off, no offer", () => AHopFromATakeOffElsewhere(lighting: true, reachable: false));
        Each("hop pot: a pot reachable only by a hop from a take-off elsewhere is offered and broken", () => AHopFromATakeOffElsewhere(lighting: false, reachable: true));
        Each("hop pot: no take-off, no offer", () => AHopFromATakeOffElsewhere(lighting: false, reachable: false));
        if (red == 0) Console.WriteLine("assistance trips: lighting and pot trips require a way back, and hop from take-offs the walker reaches");
        return red;
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
        var body = ctx.Companion.Motor.State;
        Require(FindToolAccess.Approach(interaction, ctx.Npc.Bottom, out _) == Reach.No
            && !live::AICompanion.Companion.Brain.SharedMovementSystem.ProveInteractionJump.CanReach(
                live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World, body, b => FindToolAccess.InReach(b.Feet, interaction)),
            $"the shelf must be out of standing reach from every floor pose and out of a jump from where the companion starts; shelf row {shelfRow}");
        var hop = FindToolAccess.HopApproach(interaction, ctx.Npc.Bottom, body, out Vector2 takeOff);
        Require(hop == (reachable ? Reach.Yes : Reach.No),
            $"the premise needs a take-off exactly when the shelf is low; shelf row {shelfRow} hop={hop} take-off={takeOff}");

        var brain = ctx.Companion.Brain;
        string methodName = lighting ? "place-torches" : "collect";
        brain.Chooser.Actions.RemoveAll(a => a.Name != methodName && a.Name != "keep-company");
        var method = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.PerformNearbyWorldWork>().Single();
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
        Require(FindToolAccess.Approach(target, ctx.Npc.Bottom, out _) == Reach.No && target.Y <= shelfRow,
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
