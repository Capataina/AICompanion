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
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using TorchBearer = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.TorchBearer;

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
        bool oneWay = AStar.AllowOneWayDrops;
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
                AStar.AllowOneWayDrops = oneWay;
                VerifyUsefulAssistance.ClearMeasuredLight();
            }
        }
        Each("mining re-derives a working stand after reach shrinks and returns to the feet after it grows", MiningStandFollowsReach);
        Each("mining offers ore a larger reach brings into range on the next preparation", MiningRediscoversOnReachIncrease);
        Each("chopping re-derives its stand after reach shrinks", ChoppingStandFollowsReach);
        Each("chopping offers a trunk deferred under a smaller reach on the next preparation", ChoppingDeferralEndsOnReachIncrease);
        Each("chopping's remaining work follows the held axe on the next preparation", ChoppingRemainingWorkFollowsTheAxe);
        Each("lighting and pot work never walk to a stand the current reach cannot swing from", NearbyWorkStandFollowsReach);
        Each("lighting and pot work search again on the preparation after reach grows", NearbyWorkRediscoversOnReachIncrease);
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
        Vector2 feet = ctx.Npc.Bottom;
        Require(FindToolAccess.InReach(feet, ore), "premise: the ore must be inside reach 5 from the companion's feet");
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0 && mine.TargetStandPosition == feet,
            $"premise: a vein in reach must be worked from the feet; stand={mine.TargetStandPosition} feet={feet} status={mine.Status}");
        mine.Execute(ctx);
        Require(mine.HandsBusy, $"premise: the first execution must swing; status={mine.Status}");

        Player.tileRangeX = 2;
        Require(!FindToolAccess.InReach(feet, ore), "premise: reach 2 must not reach the ore from the same feet");
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        PositionRequest shrunk = mine.Execute(ctx);
        Require(!mine.HandsBusy, $"no swing may come from a pose the current reach cannot swing from; request={shrunk}");
        Require(shrunk.Kind == RequestKind.Exact && Vector2.DistanceSquared(shrunk.Anchor, feet) > 4f
                && FindToolAccess.InReach(shrunk.Anchor, ore),
            $"the next preparation must re-derive a stand that reaches under the new reach, not hold the old one; request={shrunk} feet={feet} stand={mine.TargetStandPosition} status={mine.Status}");

        Player.tileRangeX = 5;
        VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.TargetStandPosition == feet,
            $"once reach grows back, work from the feet again instead of walking to the smaller reach's stand; stand={mine.TargetStandPosition} feet={feet}");
    }

    private static void MiningRediscoversOnReachIncrease()
    {
        Point placeholder = new(25, 89), ore = new(25, 78);
        var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        VerifyOreWork.Place(ore, TileID.Copper);
        TerrainChanges.Reset();
        Player.tileRangeX = Player.tileRangeY = 1;
        float small = VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(small == 0f, $"premise: ore twelve rows up is out of every pose and hop at reach 1; value={small} status={mine.Status} offer={mine.Eligibility}/{mine.EligibilityReason}");

        Player.tileRangeX = 5;
        Player.tileRangeY = 11;
        float grown = VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(grown > 0f && mine.Eligibility == OfferEligibility.Usable && mine.TargetTile == ore,
            $"a reach that now covers the ore must be offered on the next preparation, not after the search cadence; value={grown} status={mine.Status} offer={mine.Eligibility}/{mine.EligibilityReason}");
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
        return (new ChopTree(), ctx, bottom);
    }

    private static void ChoppingStandFollowsReach()
    {
        var (chop, ctx, trunk) = SetUpTree();
        Vector2 feet = ctx.Npc.Bottom;
        Require(VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0, $"premise: a trunk inside reach must be offered; offer={chop.Eligibility}/{chop.EligibilityReason}");
        chop.Execute(ctx);
        Require(chop.HandsBusy, "premise: the first execution must swing from the feet");

        Player.tileRangeX = 2;
        Require(!FindToolAccess.InReach(feet, trunk), "premise: reach 2 must not reach the trunk from the same feet");
        VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        PositionRequest shrunk = chop.Execute(ctx);
        Require(!chop.HandsBusy && shrunk.Kind == RequestKind.Exact && FindToolAccess.InReach(shrunk.Anchor, trunk),
            $"chopping must walk to a stand that reaches under the new reach; request={shrunk} feet={feet} offer={chop.Eligibility}/{chop.EligibilityReason}");
    }

    private static void ChoppingDeferralEndsOnReachIncrease()
    {
        var (chop, ctx, trunk) = SetUpTree();
        Player.tileRangeX = Player.tileRangeY = 0;
        float none = VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        Require(none == 0f, $"premise: no pose reaches a trunk at reach 0; value={none} offer={chop.Eligibility}/{chop.EligibilityReason}");

        Player.tileRangeX = Player.tileRangeY = 5;
        Require(FindToolAccess.InReach(ctx.Npc.Bottom, trunk), "premise: reach 5 reaches the trunk from the feet");
        float grown = VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        Require(grown > 0f && chop.Eligibility == OfferEligibility.Usable,
            $"a trunk refused under a smaller reach must be offered once the reach covers it, not after its deferral expires; value={grown} offer={chop.Eligibility}/{chop.EligibilityReason}");
    }

    private static void ChoppingRemainingWorkFollowsTheAxe()
    {
        var (chop, ctx, _) = SetUpTree();
        Player player = ctx.Player;
        for (int i = 0; i < player.inventory.Length; i++) player.inventory[i] = new Item();
        player.selectedItem = 0;
        player.inventory[0].SetDefaults(ItemID.CopperAxe);
        float copperValue = VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        var copperWork = chop.RemainingWork;
        Require(copperValue > 0 && copperWork != null,
            $"premise: a copper axe must price the trunk; offer={chop.Eligibility}/{chop.EligibilityReason}");
        var copper = copperWork!.Value;
        player.inventory[0].SetDefaults(ItemID.GoldAxe);
        VerifyPreparedActivities.PrepareAndScore(chop, ctx);
        Require(chop.RemainingWork is { } gold && gold.DamagePerHit > copper.DamagePerHit && gold.Hits <= copper.Hits,
            $"a stronger axe must price the same trunk with its own damage on the next preparation; copper={copper} gold={chop.RemainingWork}");
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
        brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses, null);
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
        bool reachesFromFeet = target is Point t && FindToolAccess.InReach(ctx.Npc.Bottom, t);
        Require(request.Kind != RequestKind.Exact || target is Point walkTo && FindToolAccess.InReach(request.Anchor, walkTo),
            $"a walk must end at a pose that reaches the site under the current reach; request={request} site={site} target={target} feet={ctx.Npc.Bottom}");
        Require(request.Kind != RequestKind.Hold || request.JumpScale > 0f || reachesFromFeet || target == null,
            $"holding still must mean swinging, jumping or having no site; request={request} target={target}");
    }

    private static void NearbyWorkRediscoversOnReachIncrease()
    {
        var (light, ctx) = SetUpDarkArea();
        Player.tileRangeX = Player.tileRangeY = 0;
        float none = VerifyPreparedActivities.PrepareAndScore(light, ctx);
        Require(none == 0f, $"premise: no torch site is reachable at reach 0; value={none} offer={light.Eligibility}/{light.EligibilityReason} target={light.ActivityTarget}");

        Player.tileRangeX = Player.tileRangeY = 5;
        float grown = VerifyPreparedActivities.PrepareAndScore(light, ctx);
        Require(grown > 0f && light.ActivityTarget != null,
            $"a site the larger reach covers must be found on the next preparation, not after the search cadence; value={grown} offer={light.Eligibility}/{light.EligibilityReason}");
    }

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
            // With drops refused the walker never reaches the pit floor, and the pot is an ordinary absence rather than a refusal.
            AStar.AllowOneWayDrops = true;
            ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
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
        var field = typeof(CollectNearbyItems).BaseType!.GetField("noReturn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
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
