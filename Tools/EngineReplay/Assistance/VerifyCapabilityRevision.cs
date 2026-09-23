extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using MineOre = live::AICompanion.Companion.Brain.Activities.Gathering.MineOre;
using ChopTree = live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree;
using LightUsefulArea = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.LightUsefulArea;
using CollectNearbyItems = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using TorchBearer = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.TorchBearer;
using StepBinding = live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.StepBinding;
using ExecuteCourseBinding = live::AICompanion.Companion.Brain.Infrastructure.Selection.ExecuteCourseBinding;

/// <summary>
/// P11's capability revision for the player-derived capabilities the companion reads live: tool reach
/// (<c>Player.tileRangeX/Y</c>) and tool power (the held pick or axe). Each case changes one of them between two
/// preparations of an activity that already holds evidence derived from the old value, and requires the next
/// preparation to act on the new one: a retained stand that no longer reaches is re-derived rather than walked to,
/// and a "no access" verdict reached under a smaller reach does not outlive the reach that produced it. Every case
/// is prepared and executed on native tiles with the planning allowances lifted, because the premises need proven
/// approaches rather than whatever a deadline let a search finish.
/// </summary>
internal static class VerifyCapabilityRevision
{
    public static int Run()
    {
        // The --capability entry point reaches here without VerifyEngineMotion's setup, and Main's static constructor
        // needs a save path before any tile map exists.
        typeof(Terraria.Program).GetField("SavePath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int red = 0;
        int reachX = Player.tileRangeX, reachY = Player.tileRangeY;
        WorkPolicy mining = WorkPolicies.Mining, chopping = WorkPolicies.Chopping;
        Preferences saved = Preferences.Current;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            try { fixture(); Console.WriteLine($"GREEN capability {name}"); }
            catch (InvalidOperationException e) { red++; Console.WriteLine($"RED capability {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.Unbounded = false;
                Player.tileRangeX = reachX;
                Player.tileRangeY = reachY;
                WorkPolicies.Mining = mining;
                WorkPolicies.Chopping = chopping;
                Preferences.Current = saved;
                VerifyUsefulAssistance.ClearMeasuredLight();
            }
        }
        Each("mining re-derives a working stand after reach shrinks and returns to the feet after it grows", MiningStandFollowsReach);
        Each("mining offers ore a larger reach brings into range on the next preparation", MiningRediscoversOnReachIncrease);
        Each("chopping re-derives its stand after reach shrinks", ChoppingStandFollowsReach);
        Each("chopping offers a trunk deferred under a smaller reach on the next preparation", ChoppingDeferralEndsOnReachIncrease);
        Each("chopping's remaining work follows the held axe on the next preparation", ChoppingRemainingWorkFollowsTheAxe);
        Each("lighting and pot work never walk to a stand the current reach cannot swing from", NearbyWorkStandFollowsReach);
        Each("lighting and pot work offer a site refused with no return under a smaller reach on the next preparation", NearbyWorkNoReturnEndsOnReachIncrease);
        Each("known-drop collection is independent of tool reach", KnownDropIgnoresToolReach);
        Console.WriteLine(red == 0
            ? "capability revision: reach and tool power reach mining, chopping, lighting and collection on the next preparation"
            : $"capability revision: {red} case(s) failed");
        return red;
    }

    // ── mining ───────────────────────────────────────────────────────────────────────────────

    private static void MiningStandFollowsReach()
    {
        Point ore = new(25, 89);
        var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        // The body's own position, which for this body is its centre. Reading `Bottom` here was the walker's
        // question and is a whole tile lower on an orb, so the row asked about reach from a point the activity
        // never works from and the premise failed on geometry rather than on reach.
        Vector2 body = ctx.Npc.Center;
        Require(FindToolAccess.InReach(body, ore), "premise: the ore must be inside reach 5 from where the body hovers");
        // Where the body is sent is the bound step's request, which is what the tick hands the positioner; the stand
        // is the census's, re-derived when reach changes because reach is in the census's own signature.
        PositionRequest Sent(StepBinding step) => ExecuteCourseBinding.RequestFor(step, ctx.Npc.Center);
        var step = DriveGatheringThroughTheCourse.Bind(ctx, "mine", "premise: a vein in reach must be bound");
        Require(Sent(step).Anchor == body,
            $"premise: a vein in reach must be worked from where the body already is; request={Sent(step)} body={body}");
        DriveGatheringThroughTheCourse.Perform(ctx, step);
        Require(mine.HandsBusy, $"premise: the first execution must swing; status={mine.Status}");

        Player.tileRangeX = 2;
        Require(!FindToolAccess.InReach(body, ore), "premise: reach 2 must not reach the ore from the same spot");
        step = DriveGatheringThroughTheCourse.Bind(ctx, "mine", "the ore must still be bound under the smaller reach");
        DriveGatheringThroughTheCourse.Perform(ctx, step);
        PositionRequest shrunk = Sent(step);
        Require(!mine.HandsBusy, $"no swing may come from a pose the current reach cannot swing from; request={shrunk}");
        Require(shrunk.Kind == RequestKind.Exact && Vector2.DistanceSquared(shrunk.Anchor, body) > 4f
                && FindToolAccess.InReach(shrunk.Anchor, ore),
            $"the next decision must re-derive a stand that reaches under the new reach, not hold the old one; request={shrunk} body={body} status={mine.Status}");

        Player.tileRangeX = 5;
        step = DriveGatheringThroughTheCourse.Bind(ctx, "mine", "the ore must be bound again once reach grows");
        Require(Sent(step).Anchor == body,
            $"once reach grows back, work from where the body is again instead of flying to the smaller reach's stand; request={Sent(step)} body={body}");
    }

    private static void MiningRediscoversOnReachIncrease()
    {
        Point placeholder = new(25, 89), ore = new(25, 78);
        var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        VerifyOreWork.Place(ore, TileID.Copper);
        TerrainChanges.Reset();
        // Reach zero, not reach one, and the difference is the whole orb story. For the walker, reach one refused
        // ore twelve rows up because no standable tile was within a tile of it. A body that flies hovers beside
        // any free cell, so at reach one it is already touching this ore and the row asserted nothing (it read
        // value 0.7, Usable). What still refuses a flying body is reach zero, where the only pose within reach of
        // a solid tile is inside that tile — which is exactly the rule the chopping row below establishes on a
        // trunk, and this row now establishes on ore twelve rows off the floor, so that what it still tests is
        // the thing it was written for: the offer arrives on the very next preparation rather than after the
        // discovery cadence.
        Player.tileRangeX = Player.tileRangeY = 0;
        Require(DriveGatheringThroughTheCourse.Decide(ctx, "mine") == null,
            $"premise: no pose reaches a solid ore tile at reach 0, because the only one would be inside it; {DriveGatheringThroughTheCourse.Account(ctx)}");

        Player.tileRangeX = 5;
        Player.tileRangeY = 11;
        var step = DriveGatheringThroughTheCourse.Decide(ctx, "mine");
        Require(step != null && DriveGatheringThroughTheCourse.Tile(step) == ore,
            $"a reach that now covers the ore must be bound on the next decision, not after any cadence; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    // ── chopping ─────────────────────────────────────────────────────────────────────────────

    private static (ChopTree Chop, ActionContext Context, Point Trunk) SetUpTree()
    {
        Point bottom = new(25, 89);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, bottom);
        Tile trunk = Main.tile[bottom.X, bottom.Y];
        trunk.TileType = TileID.Trees;
        Main.tileAxe[TileID.Trees] = true;
        Main.tileSolid[TileID.Trees] = false;
        TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
        TerrainChanges.Reset();
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        return (ctx.Companion.Brain.Actions.OfType<ChopTree>().Single(), ctx, bottom);
    }

    private static void ChoppingStandFollowsReach()
    {
        var (chop, ctx, trunk) = SetUpTree();
        Vector2 feet = ctx.Npc.Bottom;
        var step = DriveGatheringThroughTheCourse.Bind(ctx, "chop", "premise: a trunk inside reach must be bound");
        DriveGatheringThroughTheCourse.Perform(ctx, step);
        Require(chop.HandsBusy, $"premise: the first execution must swing from where the body is; status={chop.Status}");

        Player.tileRangeX = 2;
        Require(!FindToolAccess.InReach(feet, trunk), "premise: reach 2 must not reach the trunk from the same feet");
        step = DriveGatheringThroughTheCourse.Bind(ctx, "chop", "the trunk must still be bound under the smaller reach");
        DriveGatheringThroughTheCourse.Perform(ctx, step);
        PositionRequest shrunk = ExecuteCourseBinding.RequestFor(step, ctx.Npc.Center);
        Require(!chop.HandsBusy && shrunk.Kind == RequestKind.Exact && FindToolAccess.InReach(shrunk.Anchor, trunk),
            $"chopping must be sent to a stand that reaches under the new reach; request={shrunk} feet={feet} status={chop.Status}");
    }

    private static void ChoppingDeferralEndsOnReachIncrease()
    {
        var (_, ctx, trunk) = SetUpTree();
        Player.tileRangeX = Player.tileRangeY = 0;
        Require(DriveGatheringThroughTheCourse.Decide(ctx, "chop") == null,
            $"premise: no pose reaches a trunk at reach 0; {DriveGatheringThroughTheCourse.Account(ctx)}");

        Player.tileRangeX = Player.tileRangeY = 5;
        Require(FindToolAccess.InReach(ctx.Npc.Center, trunk), "premise: reach 5 reaches the trunk from where the body hovers");
        Require(DriveGatheringThroughTheCourse.Decide(ctx, "chop") is { } step && DriveGatheringThroughTheCourse.Tile(step) == trunk,
            $"a trunk refused under a smaller reach must be bound once the reach covers it, on the next decision; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    private static void ChoppingRemainingWorkFollowsTheAxe()
    {
        var (_, ctx, trunk) = SetUpTree();
        // The companion's axe is the one in its own axe slot (gear slot index 3), never the player's
        // held tool, so the swap is made in the gear. The work each axe would do is the census's, which is what
        // the course prices and binds, and a changed axe is in the census's own signature.
        var gear = ctx.Player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear;
        gear.Slots[3].SetDefaults(ItemID.CopperAxe);
        var copperSite = DriveGatheringThroughTheCourse.Site(ctx, "chop-target", trunk);
        Require(copperSite?.Work is { DamagePerHit: > 0 } && DriveGatheringThroughTheCourse.Decide(ctx, "chop") != null,
            $"premise: a copper axe must price and bind the trunk; census={copperSite}; {DriveGatheringThroughTheCourse.Account(ctx)}");
        var copper = copperSite!.Work!;
        gear.Slots[3].SetDefaults(ItemID.GoldAxe);
        var goldSite = DriveGatheringThroughTheCourse.Site(ctx, "chop-target", trunk);
        var gold = goldSite?.Work;
        Require(gold != null && gold.DamagePerHit > copper.DamagePerHit && gold.ItemType == ItemID.GoldAxe,
            $"a stronger axe must price the same trunk with its own damage on the next capture; copper={copper} gold={gold}");
        Require(DriveGatheringThroughTheCourse.Decide(ctx, "chop") is { } bound && bound.Tool.Contains($":{gold!.Power}:", System.StringComparison.Ordinal),
            $"the step bound after the swap must carry the gold axe; {DriveGatheringThroughTheCourse.Account(ctx)}");
    }

    // ── lighting and pots (one shared interaction base) ──────────────────────────────────────

    private static (LightUsefulArea Light, ActionContext Context) SetUpDarkArea()
    {
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, new Point(70, 59));
        Preferences.Current = new Preferences { TorchPlacement = true, PotBreaking = false };
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        for (int i = 0; i < ctx.Companion.Bag.Items.Length; i++) ctx.Companion.Bag.Items[i] = new Item();
        ctx.Player.selectedItem = 1;
        Item supply = new();
        supply.SetDefaults(ItemID.Torch);
        supply.stack = 5;
        ctx.Player.inventory[0] = supply;
        VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (_, _) => .05f);
        typeof(TorchBearer).GetProperty("Shown")!.GetSetMethod(true)!.Invoke(ctx.Companion.Torch, new object[] { false });
        // Lighting reads the light field and the reach region rather than measuring per candidate, so the
        // scene is observed before it is prepared and its reach region is primed to settle. Tool reach, the
        // capability these rows actually vary, is not what either sense holds, so priming here cannot hide
        // the very-next-preparation behaviour the rows are checking.
        var brain = ctx.Companion.Brain;
        brain.Senses.Update(ctx.Npc, ctx.Player);
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses);
        return (new LightUsefulArea(), ctx);
    }

    private static void NearbyWorkStandFollowsReach()
    {
        var (light, ctx) = SetUpDarkArea();
        Require(VerifyPreparedActivities.PrepareAndScore(light, ctx) > 0 && light.ActivityTarget is Vector2,
            $"premise: a measured dark area with a torch must offer a site; offer={light.Eligibility}/{light.EligibilityReason}");
        Point site = light.ActivityTarget!.Value.ToTileCoordinates();
        Player.tileRangeX = Player.tileRangeY = 1;
        VerifyPreparedActivities.PrepareAndScore(light, ctx);
        PositionRequest request = light.Execute(ctx);
        Point? target = light.ActivityTarget?.ToTileCoordinates();
        // The body's own pose is its centre: `InReach` takes a centre and every production caller passes one.
        bool reachesFromBody = target is Point t && FindToolAccess.InReach(ctx.Npc.Center, t);
        Require(request.Kind != RequestKind.Exact || target is Point walkTo && FindToolAccess.InReach(request.Anchor, walkTo),
            $"a walk must end at a pose that reaches the site under the current reach; request={request} site={site} target={target} centre={ctx.Npc.Center}");
        // The walker's version of this admitted a third case, a request holding still with a jump scale on it,
        // which was the body about to leave the ground for a site above. There is no jump now, so holding still
        // means either the site is already in reach or there is no site.
        Require(request.Kind != RequestKind.Hold || reachesFromBody || target == null,
            $"holding still must mean swinging or having no site; request={request} target={target}");
    }

    // DELETED, with the reason rather than the row, because the idea will be had again: "a torch site refused
    // under a small reach is offered as soon as the reach grows" cannot be stated about this body.
    //
    // The walker's version shrank reach to zero and read no site, because a torch site is a piece of air and the
    // walker could only ever stand on a floor under it — at reach zero the only pose within reach of a tile is
    // inside that tile, and the walker could not be inside air it did not stand in. A body that flies can be. So
    // the first orb run of this row read value 0.84 and a Usable offer at reach zero, from a pose in the site's
    // own tile, and there is no smaller reach to shrink to.
    //
    // The property itself — a refusal derived under one capability is re-asked on the very next preparation
    // rather than after the discovery cadence — is not lost: `ChoppingDeferralEndsOnReachIncrease` and
    // `MiningRediscoversOnReachIncrease` both establish it, and both can because a trunk and an ore are solid
    // tiles the body cannot occupy, which is what makes reach zero a real refusal. What no longer has a row is
    // that same property on an air target, and the honest reason is that reach never refuses a flying body an
    // air target at all.
    //
    // `NearbyWorkStandFollowsReach` and `NearbyWorkNoReturnEndsOnReachIncrease` below keep the two
    // lighting and pot-collection capability claims that do survive the body change.

    // ── collection ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The executor keeps two refusal stores with different keys: failed approaches, and trips proven to have no way back, no
    /// breath or no hop take-off. A pot sits on the floor of a pit ten rows deep with no way out, and the companion stands on the
    /// pit's rim. At a small vertical reach its feet cannot swing at the pot and the nearest pose that can is on the pit floor,
    /// so the trip is refused with no return; at a larger one the feet work it from the rim. Pot collection runs the executor
    /// lighting shares, and a pot is the only candidate, so the site is exactly the one placed. It must be offered on the very
    /// next preparation after the reach grows, with no tick and no terrain change between, because a refusal derived under one
    /// reach says nothing about another. The site expected is what a fresh collection offers at the larger reach, and the
    /// premise is that the smaller reach really put that site in the no-return store. The pot sits ten columns out from the rim
    /// because a swing's line (the game's own `Collision.CanHit`) from the eye above the rim is cut by the rim's corner tile, so
    /// only the pit floor beyond that diagonal is visible from the rim: a reach map printed from this scene put the first
    /// visible tile at column 38 on row 68 and 39 on row 69, and a pot at columns 35 and 36 was refused at every reach.
    /// </summary>
    private static void NearbyWorkNoReturnEndsOnReachIncrease()
    {
        const int ReachX = 12, SmallY = 2, LargeY = 12;
        const int PitLeft = 30, PitRight = 41, PitFloor = 70, Rim = PitLeft - 1;
        ActionContext Pit()
        {
            Point placeholder = new(60, 59);
            var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, placeholder);
            Main.tile[placeholder.X, placeholder.Y].ClearEverything();
            for (int x = PitLeft; x <= PitRight; x++)
                for (int y = 60; y < PitFloor; y++) Main.tile[x, y].ClearEverything();
            for (int y = 61; y <= PitFloor; y++)
            {
                VerifyOreWork.Place(new Point(PitLeft - 1, y), TileID.Dirt);
                VerifyOreWork.Place(new Point(PitRight + 1, y), TileID.Dirt);
            }
            for (int x = PitLeft; x <= PitRight; x++) VerifyOreWork.Place(new Point(x, PitFloor), TileID.Dirt);
            for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
            foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
            Preferences.Current = new Preferences { TorchPlacement = false, PotBreaking = true };
            VerifyAssistanceTrips.PlacePot(new Point(39, PitFloor - 2));
            ctx.Npc.Bottom = new Vector2(Rim * 16 + 8, 60 * 16);
            ctx.Npc.velocity = Vector2.Zero;
            ctx.Senses.Loot.Pickups.Clear();
            ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
            return ctx;
        }

        var controlCtx = Pit();
        Player.tileRangeX = ReachX;
        Player.tileRangeY = LargeY;
        var control = new CollectNearbyItems();
        float controlValue = VerifyPreparedActivities.PrepareAndScore(control, controlCtx);
        Require(controlValue > 0f && control.Method == "potential-pot-contents" && control.ActivityIdentity is Point,
            $"premise: at vertical reach {LargeY} the pit's rim must work the pot; value={controlValue} method={control.Method} offer={control.Eligibility}/{control.EligibilityReason}");
        Point expected = (Point)control.ActivityIdentity!;

        var ctx = Pit();
        Player.tileRangeX = ReachX;
        Player.tileRangeY = SmallY;
        var collect = new CollectNearbyItems();
        float small = VerifyPreparedActivities.PrepareAndScore(collect, ctx);
        List<Point> refused = NoReturnSites(collect);
        Require(small == 0f && refused.Contains(expected),
            $"premise: at vertical reach {SmallY} the pot's only pose is on the shaft floor and must be refused with no return; expected={expected} noReturn=[{string.Join(" ", refused)}] value={small} method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason}");

        Player.tileRangeY = LargeY;
        float large = VerifyPreparedActivities.PrepareAndScore(collect, ctx);
        Require(large > 0f && collect.Method == "potential-pot-contents" && Equals(collect.ActivityIdentity, expected),
            $"a site refused with no return under a smaller reach must be offered on the next preparation after reach grows; expected={expected} offered={collect.ActivityIdentity} value={large} method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason} noReturn=[{string.Join(" ", NoReturnSites(collect))}]");
    }

    private static List<Point> NoReturnSites(CollectNearbyItems collect)
    {
        // The executor's store of refused stands, keyed by tile; it also holds stands beyond the flood's known radius,
        // which this scene has none of, so its keys are the no-return sites.
        var field = typeof(CollectNearbyItems).BaseType!.GetField("refused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return ((System.Collections.IDictionary)field.GetValue(collect)!).Keys.Cast<Point>().ToList();
    }

    /// <summary>Coverage rather than a repair: a known drop is picked up by body contact, so the companion's tool reach is
    /// not an input to its offer, trip or target.</summary>
    private static void KnownDropIgnoresToolReach()
    {
        Point placeholder = new(60, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        Preferences.Current = new Preferences { TorchPlacement = false, PotBreaking = false };
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        TerrainChanges.Reset();
        const int slotIndex = 7;
        Item previous = Main.item[slotIndex];
        try
        {
            var drop = new Item();
            drop.SetDefaults(ItemID.CopperOre);
            drop.stack = 5;
            drop.active = true;
            drop.noGrabDelay = 0;
            drop.whoAmI = slotIndex;
            drop.Bottom = new Vector2(30 * 16 + 8, 60 * 16);
            Main.item[slotIndex] = drop;
            ctx.Senses.Loot.Pickups.Clear();
            ctx.Senses.Loot.Pickups.Add(new(drop, 1f, Vector2.Distance(ctx.Npc.Center, drop.Center)));
            var collect = new CollectNearbyItems();
            float wide = VerifyPreparedActivities.PrepareAndScore(collect, ctx);
            float wideTrip = collect.ForecastTicks();
            Require(wide > 0f && collect.Method == "known-drop", $"premise: a floor drop must be a known-drop offer; offer={collect.Eligibility}/{collect.EligibilityReason}");
            Player.tileRangeX = Player.tileRangeY = 1;
            float narrow = VerifyPreparedActivities.PrepareAndScore(collect, ctx);
            Require(narrow == wide && collect.ForecastTicks() == wideTrip && collect.Method == "known-drop" && ReferenceEquals(collect.ActivityIdentity, drop),
                $"tool reach must not change a known drop's offer; wide={wide}/{wideTrip} narrow={narrow}/{collect.ForecastTicks()} method={collect.Method}");
        }
        finally { Main.item[slotIndex] = previous; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
