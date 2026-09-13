extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CollectNearbyItems = live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.CollectNearbyItems;
using WorkPolicy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Behaviours.ActionContext;
using AttemptStatus = live::AICompanion.Companion.Brain.Behaviours.AttemptStatus;
using AttemptAttribution = live::AICompanion.Companion.Brain.Behaviours.AttemptAttribution;
using OfferEligibility = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;
using TerrainChanges = live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges;
using AStar = live::AICompanion.Companion.Brain.SharedMovementSystem.AStar;
using LimitPlanningWork = live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;

/// <summary>
/// Collection priced and concluded from what the world and the bag actually hold: its own reach and return rather than a
/// standable tile beside the drop, a drop that moved after preparation, the quantity the cargo can accept, and who moved the
/// items when a drop leaves the world. Every case drives the real activity and the real bag transfer path.
/// </summary>
internal static class VerifyCollectionContracts
{
    private const int Slot = 7;

    public static int Run()
    {
        bool potBreaking = Preferences.Current.PotBreaking;
        Item previous = Main.item[Slot];
        int red = 0;
        void Each(string name, Action fixture)
        {
            LimitPlanningWork.Unbounded = true;
            bool oneWay = AStar.AllowOneWayDrops;
            Preferences.Current.PotBreaking = false;
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.Unbounded = false;
                AStar.AllowOneWayDrops = oneWay;
                Preferences.Current.PotBreaking = potBreaking;
                Main.item[Slot] = previous;
            }
        }
        Each("I03 a drop in a pit proves its own reach and return", ADropInAPitIsNotCollectedOnAStandableTileAlone);
        Each("I02 a moved drop is proved again", AMovedDropIsProvedAgainBeforeTheWalk);
        Each("I04 partial stack capacity", PartialCargoCapacityValuesAndConcludesTheAcceptedQuantity);
        Each("I05 attribution by transfer", ADropLeavingTheWorldIsAttributedByWhatTheBagReceived);
        Each("I03 one trip unit", ADropsForecastIsTheWalkToAContactPose);
        if (red == 0) Console.WriteLine("collection contracts: own reach and return, moved drops, partial capacity, transfer attribution and trip unit pass");
        return red;
    }

    /// <summary>
    /// A drop at the bottom of a sealed pit beside the floor the companion stands on. A standable tile beside it proves
    /// nothing about getting there or back. With one-way drops refused the companion cannot reach it; with them allowed it
    /// can drop in but not return to a player it can currently reach. Neither may be a usable collection offer, and a drop
    /// on the open floor must stay usable.
    /// </summary>
    private static void ADropInAPitIsNotCollectedOnAStandableTileAlone()
    {
        foreach (bool oneWay in new[] { false, true })
        {
            var ctx = SetUpFloor();
            for (int x = 30; x <= 33; x++)
            {
                Main.tile[x, 60].ClearEverything();
                VerifyOreWork.Place(new Point(x, 75), TileID.Dirt);
            }
            for (int y = 60; y <= 75; y++)
            {
                VerifyOreWork.Place(new Point(29, y), TileID.Dirt);
                VerifyOreWork.Place(new Point(34, y), TileID.Dirt);
            }
            TerrainChanges.Reset();
            AStar.AllowOneWayDrops = oneWay;
            var collect = new CollectNearbyItems();
            Item pitDrop = Drop(ItemID.CopperOre, 5, new Vector2(31 * 16 + 8, 75 * 16));
            Observe(ctx, pitDrop);
            collect.Prepare(ctx);
            Require(!(collect.Method == "known-drop" || collect.Eligibility == OfferEligibility.Usable),
                $"oneWay={oneWay}: a drop in a sealed pit must not be a usable collection offer; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason} value={collect.Score()}");
            Require(collect.Eligibility is OfferEligibility.KnownUnusable,
                $"oneWay={oneWay}: the refusal must say the drop is known to be unusable; offer={collect.Eligibility}/{collect.EligibilityReason}");
            Item floorDrop = Drop(ItemID.CopperOre, 5, new Vector2(26 * 16 + 8, 60 * 16));
            Observe(ctx, floorDrop);
            collect.Prepare(ctx);
            Require(collect.Method == "known-drop" && collect.Eligibility == OfferEligibility.Usable && ReferenceEquals(collect.ActivityIdentity, floorDrop),
                $"oneWay={oneWay}: a drop on the open floor must remain a usable offer; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason}");
        }
    }

    /// <summary>A drop prepared on the floor rolls fourteen tiles before execution. Execution must not walk to where it was, and
    /// the next preparation must prove the drop where it now lies and send the companion there.</summary>
    private static void AMovedDropIsProvedAgainBeforeTheWalk()
    {
        var ctx = SetUpFloor();
        var collect = new CollectNearbyItems();
        Item drop = Drop(ItemID.CopperOre, 5, new Vector2(26 * 16 + 8, 60 * 16));
        Observe(ctx, drop);
        collect.Prepare(ctx);
        Require(collect.Method == "known-drop", $"the moved-drop fixture needs a prepared drop; method={collect.Method}");
        collect.BeginAttempt();
        drop.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        var stale = collect.Execute(ctx);
        Require(stale.Kind == RequestKind.Hold,
            $"a drop that moved after preparation must not be walked to at its old position; request={stale}");
        Observe(ctx, drop);
        collect.Prepare(ctx);
        var fresh = collect.Execute(ctx);
        Require(collect.Method == "known-drop" && fresh.Kind == RequestKind.Exact && MathF.Abs(fresh.Anchor.X - drop.Bottom.X) <= 3 * 16,
            $"the next preparation must prove the drop where it now lies and walk there; request={fresh} drop={drop.Bottom}");
    }

    /// <summary>
    /// The same valuable drop of fifty with room in the cargo for fifty in one scene and for two in the other. Value is for what
    /// can actually be taken, so the second offer must be worth less. Taking the two that fit through the real bag transfer
    /// ends the attempt partial with the rest still in the world, and the full cargo then refuses the remainder.
    /// </summary>
    private static void PartialCargoCapacityValuesAndConcludesTheAcceptedQuantity()
    {
        float ValueWithRoom(int room, out CollectNearbyItems collect, out ActionContext ctx, out Item drop)
        {
            ctx = SetUpFloor();
            FillCargoLeavingRoom(ctx, ItemID.GoldBar, room);
            collect = new CollectNearbyItems();
            drop = Drop(ItemID.GoldBar, 50, ctx.Npc.Bottom);
            drop.value = 5000;
            Observe(ctx, drop);
            collect.Prepare(ctx);
            return collect.Method == "known-drop" ? collect.Score() : -1f;
        }
        float roomy = ValueWithRoom(50, out _, out _, out _);
        float tight = ValueWithRoom(2, out var collect, out var ctx, out var drop);
        Require(roomy > 0 && tight > 0 && tight < roomy,
            $"a drop only two of which fit must be worth less than the same drop that fits whole; room 50={roomy} room 2={tight}");
        collect.BeginAttempt();
        collect.Execute(ctx);
        Require(ctx.Companion.Bag.Collect(drop, ctx.Player) && drop.stack == 48 && LootIsInWorld(drop),
            $"the real transfer must take exactly the two that fit; stack left={drop.stack}");
        var conclusion = collect.ConcludeAttempt(0);
        Require(conclusion is { Status: AttemptStatus.Partial, Cause: "drop-partly-transferred" },
            $"taking part of a stack is partial collection, with the rest still in the world; got {conclusion}");
        Observe(ctx, drop);
        collect.Prepare(ctx);
        Require(collect.Method != "known-drop" && collect.EligibilityReason == "nearby-drops-exceed-cargo-capacity",
            $"the remainder the full cargo cannot take must be refused for capacity; method={collect.Method} offer={collect.EligibilityReason}");
    }

    /// <summary>
    /// A drop leaves the world in four ways. The companion's bag takes all of it: its own completion. The player takes it with
    /// nothing transferred: the purpose went away, which is invalid. The bag takes part and the player the rest: a shared
    /// completion. A contact pickup during some other activity before any collection attempt: there is nothing left to offer,
    /// so no trip is made for it.
    /// </summary>
    private static void ADropLeavingTheWorldIsAttributedByWhatTheBagReceived()
    {
        {
            var (collect, ctx, drop) = PreparedAttempt(10);
            Require(ctx.Companion.Bag.Collect(drop, ctx.Player) && !LootIsInWorld(drop), "the companion's bag must take the whole drop");
            Require(collect.ConcludeAttempt(0) is { Status: AttemptStatus.Complete, Attribution: AttemptAttribution.Companion },
                $"a drop the companion's bag took whole is its own completion; got {collect.ConcludeAttempt(0)}");
        }
        {
            var (collect, _, drop) = PreparedAttempt(10);
            drop.TurnToAir();
            drop.active = false;
            Require(collect.ConcludeAttempt(0) is { Status: AttemptStatus.Invalid, Cause: "drop-left-world-without-companion-transfer" },
                $"a drop that left the world with nothing transferred to the companion is not its collection; got {collect.ConcludeAttempt(0)}");
        }
        {
            var (collect, ctx, drop) = PreparedAttempt(10, room: 4);
            Require(ctx.Companion.Bag.Collect(drop, ctx.Player) && drop.stack == 6, $"the bag must take the four that fit; left={drop.stack}");
            drop.TurnToAir();
            drop.active = false;
            Require(collect.ConcludeAttempt(0) is { Status: AttemptStatus.Complete, Attribution: AttemptAttribution.Shared },
                $"a drop the bag took part of and someone else finished is a shared completion; got {collect.ConcludeAttempt(0)}");
        }
        {
            var ctx = SetUpFloor();
            var collect = new CollectNearbyItems();
            Item drop = Drop(ItemID.CopperOre, 10, ctx.Npc.Bottom);
            Require(ctx.Companion.Bag.Collect(drop, ctx.Player) && !LootIsInWorld(drop), "the incidental contact pickup must take the drop");
            Observe(ctx, drop);
            collect.Prepare(ctx);
            Require(collect.Method != "known-drop" && collect.Score() == 0,
                $"a drop already taken by contact pickup must leave no collection trip; method={collect.Method} value={collect.Score()}");
        }
    }

    /// <summary>A drop on the floor ten tiles from the companion. Collection must price the walk the way mining and chopping do,
    /// distance to a working pose over walking speed, and a pose in pickup contact is never farther than the drop itself.</summary>
    private static void ADropsForecastIsTheWalkToAContactPose()
    {
        var ctx = SetUpFloor();
        var collect = new CollectNearbyItems();
        Item drop = Drop(ItemID.CopperOre, 5, new Vector2(30 * 16 + 8, 60 * 16));
        Observe(ctx, drop);
        collect.Prepare(ctx);
        float walkSpeed = live::AICompanion.Companion.CompanionMotor.WalkSpeed;
        float straight = Vector2.Distance(ctx.Npc.Bottom, drop.Bottom) / walkSpeed;
        Require(collect.Method == "known-drop" && collect.ForecastTicks() > 0 && collect.ForecastTicks() <= straight + 0.01f
            && collect.ForecastTicks() >= straight - 4 * 16 / walkSpeed,
            $"a drop's forecast must be the walk to a contact pose in pixels over walking speed; forecast={collect.ForecastTicks():0.00} straight={straight:0.00}");
    }

    private static (CollectNearbyItems Collect, ActionContext Context, Item Drop) PreparedAttempt(int stack, int? room = null)
    {
        var ctx = SetUpFloor();
        if (room is int free) FillCargoLeavingRoom(ctx, ItemID.CopperOre, free);
        var collect = new CollectNearbyItems();
        Item drop = Drop(ItemID.CopperOre, stack, ctx.Npc.Bottom);
        Observe(ctx, drop);
        collect.Prepare(ctx);
        Require(collect.Method == "known-drop", $"the attribution case needs a prepared drop; method={collect.Method} offer={collect.EligibilityReason}");
        collect.BeginAttempt();
        collect.Execute(ctx);
        return (collect, ctx, drop);
    }

    private static ActionContext SetUpFloor()
    {
        Point placeholder = new(60, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        foreach (Item slot in ctx.Companion.Bag.Items) slot.TurnToAir();
        TerrainChanges.Reset();
        return ctx;
    }

    /// <summary>Every bag slot full of stone except one holding <paramref name="type"/> with exactly <paramref name="room"/> left.</summary>
    private static void FillCargoLeavingRoom(ActionContext ctx, int type, int room)
    {
        Item[] bag = ctx.Companion.Bag.Items;
        for (int i = 0; i < bag.Length - 1; i++) { bag[i].SetDefaults(ItemID.StoneBlock); bag[i].stack = bag[i].maxStack; }
        Item last = bag[^1];
        last.SetDefaults(type);
        last.stack = last.maxStack - room;
    }

    private static Item Drop(int type, int stack, Vector2 bottom)
    {
        var item = new Item();
        item.SetDefaults(type);
        item.stack = stack;
        item.active = true;
        item.noGrabDelay = 0;
        item.whoAmI = Slot;
        item.Bottom = bottom;
        Main.item[Slot] = item;
        return item;
    }

    private static void Observe(ActionContext ctx, Item item)
    {
        ctx.Senses.Loot.Pickups.Clear();
        if (LootIsInWorld(item)) ctx.Senses.Loot.Pickups.Add(new(item, 1f, Vector2.Distance(ctx.Npc.Center, item.Center)));
    }

    private static bool LootIsInWorld(Item item) => live::AICompanion.Companion.Brain.WorldObservation.LootSense.IsWorldDrop(item);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
