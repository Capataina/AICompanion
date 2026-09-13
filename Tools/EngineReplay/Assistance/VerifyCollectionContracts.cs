extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CollectNearbyItems = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;
using AttemptAttribution = live::AICompanion.Companion.Brain.Activities.AttemptAttribution;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;

/// <summary>
/// Collection priced and concluded from what the world and the bag actually hold: its own reach and return rather than a
/// standable tile beside the drop, a drop that moved after preparation, the quantity the cargo can accept, and who moved the
/// items when a drop leaves the world. Every case drives the real activity and the real bag transfer path.
/// </summary>
internal static class VerifyCollectionContracts
{
    private const int Slot = 7;
    private const int SlotCount = 5;

    public static int Run()
    {
        bool potBreaking = Preferences.Current.PotBreaking;
        Item[] previous = Enumerable.Range(Slot, SlotCount).Select(i => Main.item[i]).ToArray();
        int red = 0;
        void Restore()
        {
            for (int i = 0; i < SlotCount; i++) Main.item[Slot + i] = previous[i];
        }
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
                Restore();
            }
        }
        Each("I03 a drop in a pit proves its own reach and return", ADropInAPitIsNotCollectedOnAStandableTileAlone);
        Each("I02 a moved drop is proved again", AMovedDropIsProvedAgainBeforeTheWalk);
        Each("I04 partial stack capacity", PartialCargoCapacityValuesAndConcludesTheAcceptedQuantity);
        Each("I05 attribution by transfer", ADropLeavingTheWorldIsAttributedByWhatTheBagReceived);
        Each("I03 one trip unit", ADropsForecastIsTheWalkToAContactPose);
        Each("D1 a reachable drop behind refused drops is offered", AReachableDropBehindRefusedDropsIsOffered);
        Each("D2 a drop merged into another world drop", ADropMergedIntoAnotherWorldDropIsNotAPurposeThatWentAway);
        // Timings under the production allowances, printed and never asserted: they describe this machine.
        foreach (bool warmUp in new[] { true, false })
            foreach (bool pit in new[] { false, true })
            {
                bool oneWay = AStar.AllowOneWayDrops;
                try { MeasureFallingDropCost(pit, warmUp); }
                finally { AStar.AllowOneWayDrops = oneWay; Preferences.Current.PotBreaking = potBreaking; Restore(); }
            }
        if (red == 0) Console.WriteLine("collection contracts: own reach and return, moved drops, partial capacity, transfer attribution, trip unit, refused-drop budget and merged drops pass");
        return red;
    }

    /// <summary>
    /// Four drops at the bottom of a sealed pit beside the companion, nearer than one reachable drop on the open floor, with
    /// more refused drops than one preparation may put to a fresh walker search. The budget bounds searches, so drops whose
    /// verdicts are already known cost nothing and the farther drop must be reached within a bounded number of preparations.
    /// The first preparation must not already offer it, or the budget is not being exercised at all.
    /// </summary>
    private static void AReachableDropBehindRefusedDropsIsOffered()
    {
        var ctx = SetUpFloor();
        ctx.Npc.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        BuildSealedPit(43, 46);
        AStar.AllowOneWayDrops = false;
        var pit = new List<Item>();
        for (int i = 0; i < 4; i++) pit.Add(Drop(ItemID.CopperOre, 5, new Vector2((43 + i) * 16 + 8, 75 * 16), Slot + i));
        Item far = Drop(ItemID.CopperOre, 5, new Vector2(12 * 16 + 8, 60 * 16), Slot + 4);
        int budget = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.CollectionReachCandidates;
        float Distance(Item item) => Vector2.Distance(ctx.Npc.Center, item.Center);
        Require(pit.All(drop => Distance(drop) < Distance(far)) && pit.Count > budget,
            $"premise: every pit drop must be nearer than the floor drop, and there must be more of them than the budget ({budget}); far={Distance(far):0} pit={string.Join(",", pit.Select(d => Distance(d).ToString("0")))}");
        {
            var alone = new CollectNearbyItems();
            ObserveAll(ctx, far);
            alone.Prepare(ctx);
            Require(alone.Method == "known-drop" && ReferenceEquals(alone.ActivityIdentity, far),
                $"premise: the floor drop must be a usable offer on its own; offer={alone.Eligibility}/{alone.EligibilityReason}");
        }
        var collect = new CollectNearbyItems();
        ObserveAll(ctx, pit.Append(far).ToArray());
        collect.Prepare(ctx);
        Require(!ReferenceEquals(collect.ActivityIdentity, far) && collect.Eligibility == OfferEligibility.KnownUnusable,
            $"premise: the first preparation asks only about the nearest pit drops and finds them unusable; offer={collect.Eligibility}/{collect.EligibilityReason} target={collect.ActivityTarget}");
        int bound = (pit.Count + 1 + budget - 1) / budget;
        int preparations = 1;
        while (!ReferenceEquals(collect.ActivityIdentity, far) && preparations < 4 * bound)
        {
            collect.Prepare(ctx);
            preparations++;
        }
        Require(ReferenceEquals(collect.ActivityIdentity, far) && collect.Method == "known-drop" && preparations <= bound,
            $"a reachable drop behind refused ones must be offered within {bound} preparations, because known verdicts cost no search; offered={ReferenceEquals(collect.ActivityIdentity, far)} after {preparations} preparations, offer={collect.Eligibility}/{collect.EligibilityReason}");
    }

    /// <summary>
    /// Terraria merges two nearby drops of one type in the world: the lower slot absorbs the higher one, which is cleared
    /// and deactivated. A walked-to drop absorbed that way has not left the world and nobody took it, so it must not end its
    /// attempt as a purpose that went away; after a partial transfer the rest is still lying there, so it is partial. A drop
    /// the player took with a same-type drop lying beyond merge distance remains invalid.
    /// </summary>
    private static void ADropMergedIntoAnotherWorldDropIsNotAPurposeThatWentAway()
    {
        (CollectNearbyItems Collect, ActionContext Context, Item Walked, Item Other) Scene(int otherColumn, int? room)
        {
            var ctx = SetUpFloor();
            if (room is int free) FillCargoLeavingRoom(ctx, ItemID.CopperOre, free);
            Item other = Drop(ItemID.CopperOre, 5, new Vector2(otherColumn * 16 + 8, 60 * 16), Slot);
            Item walked = Drop(ItemID.CopperOre, 5, new Vector2(25 * 16 + 8, 60 * 16), Slot + 1);
            other.playerIndexTheItemIsReservedFor = walked.playerIndexTheItemIsReservedFor = Main.myPlayer;
            ObserveAll(ctx, other, walked);
            var collect = new CollectNearbyItems();
            collect.Prepare(ctx);
            Require(ReferenceEquals(collect.ActivityIdentity, walked),
                $"premise: the nearer drop must be the one walked to; offer={collect.Eligibility}/{collect.EligibilityReason} target={collect.ActivityTarget}");
            collect.BeginAttempt();
            collect.Execute(ctx);
            return (collect, ctx, walked, other);
        }
        {
            var (collect, ctx, walked, absorber) = Scene(26, null);
            absorber.TryCombiningIntoNearbyItems(Slot);
            Require(!LootIsInWorld(walked) && absorber.stack == 10,
                $"premise: the native merge must absorb the walked-to drop; walked active={walked.active} absorber stack={absorber.stack}");
            var conclusion = collect.ConcludeAttempt(0);
            Require(conclusion is { Status: AttemptStatus.Attempted, Cause: "drop-merged-into-world-drop" },
                $"a walked-to drop merged into another world drop has not left the world; got {conclusion}");
            ObserveAll(ctx, absorber);
            collect.Prepare(ctx);
            Require(collect.Method == "known-drop" && ReferenceEquals(collect.ActivityIdentity, absorber),
                $"the absorbing drop must be offered on the next preparation; offer={collect.Eligibility}/{collect.EligibilityReason}");
        }
        {
            var (collect, ctx, walked, absorber) = Scene(26, 2);
            Require(ctx.Companion.Bag.Collect(walked, ctx.Player) && walked.stack == 3, $"premise: the bag must take the two that fit; left={walked.stack}");
            absorber.TryCombiningIntoNearbyItems(Slot);
            Require(!LootIsInWorld(walked) && absorber.stack == 8, $"premise: the native merge must absorb the remainder; absorber stack={absorber.stack}");
            var conclusion = collect.ConcludeAttempt(0);
            Require(conclusion is { Status: AttemptStatus.Partial, Cause: "drop-partly-transferred" },
                $"part transferred and the rest merged into a world drop is partial, not a completion; got {conclusion}");
        }
        {
            var (collect, _, walked, _) = Scene(30, null);
            walked.TurnToAir();
            walked.active = false;
            var conclusion = collect.ConcludeAttempt(0);
            Require(conclusion is { Status: AttemptStatus.Invalid, Cause: "drop-left-world-without-companion-transfer" },
                $"a drop taken with the only same-type drop beyond merge distance left the world; got {conclusion}");
        }
    }

    /// <summary>
    /// Whole-brain collection timing under the production allowances while four drops appear at once above the companion and
    /// fall: onto the open floor beside it, or into a sealed pit beside it. A falling drop changes its contact pose as it
    /// descends, which the verdict reuse keys on. Once the drops have landed a reachable drop appears farther away on the
    /// floor, so the pit scene also shows whether refused nearer drops starve it. Prints NearbyAssistance preparation per
    /// evaluated tick, its three costliest ticks and the first tick the farther drop is offered; asserts nothing.
    /// </summary>
    private static void MeasureFallingDropCost(bool pit, bool warmUp)
    {
        const int farAppears = 110;
        Preferences.Current.PotBreaking = false;
        AStar.AllowOneWayDrops = false;
        var ctx = SetUpFloor();
        ctx.Npc.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        if (pit) BuildSealedPit(43, 46);
        int landing = (pit ? 75 : 60) * 16;
        var falling = new List<Item>();
        for (int i = 0; i < 4; i++) falling.Add(Drop(ItemID.CopperOre, 5, new Vector2((43 + i) * 16 + 8, 50 * 16), Slot + i));
        Item? far = null;
        var brain = ctx.Companion.Brain;
        var collect = brain.Chooser.Actions.OfType<CollectNearbyItems>().Single();
        var nearby = new List<(double Ms, int Tick)>();
        int farOffered = -1, landedBy = -1;
        for (int tick = 0; tick < 300; tick++)
        {
            foreach (Item drop in falling)
                if (LootIsInWorld(drop) && drop.Bottom.Y < landing)
                    drop.Bottom = new Vector2(drop.Bottom.X, MathF.Min(landing, drop.Bottom.Y + 4f));
            if (landedBy < 0 && falling.All(drop => !LootIsInWorld(drop) || drop.Bottom.Y >= landing)) landedBy = tick;
            if (tick == farAppears) far = Drop(ItemID.CopperOre, 5, new Vector2(12 * 16 + 8, 60 * 16), Slot + 4);
            VerifyOreWork.AdvanceBrain(ctx);
            if (brain.ChoiceEvaluated)
                foreach (var family in brain.Chooser.Queries.LastFamilies)
                    if (family.Family.ToString() == "NearbyAssistance") nearby.Add((family.Milliseconds, tick));
            if (far != null && farOffered < 0 && ReferenceEquals(collect.ActivityIdentity, far)) farOffered = tick;
        }
        if (warmUp || nearby.Count == 0) return;
        var sorted = nearby.Select(sample => sample.Ms).OrderBy(v => v).ToList();
        string costliest = string.Join(", ", nearby.OrderByDescending(sample => sample.Ms).Take(3).Select(sample => $"{sample.Ms:0.000}@{sample.Tick}"));
        Console.WriteLine($"collection cost, four drops falling {(pit ? "into a sealed pit" : "onto the open floor")} then a reachable drop at tick {farAppears} (300 ticks, production allowances, drops settled by tick {landedBy}): "
            + $"nearby-assistance prepare p50 {sorted[sorted.Count / 2]:0.000} p95 {sorted[(int)(sorted.Count * .95)]:0.000} max {sorted[^1]:0.000} ms, costliest {costliest}, "
            + $"{sorted.Count(v => v >= 0.5)} of {sorted.Count} evaluated ticks at or over 0.5 ms; farther drop first offered at tick {(farOffered < 0 ? "never" : farOffered.ToString())}");
    }

    private static void BuildSealedPit(int left, int right)
    {
        for (int x = left; x <= right; x++)
        {
            Main.tile[x, 60].ClearEverything();
            VerifyOreWork.Place(new Point(x, 75), TileID.Dirt);
        }
        for (int y = 60; y <= 75; y++)
        {
            VerifyOreWork.Place(new Point(left - 1, y), TileID.Dirt);
            VerifyOreWork.Place(new Point(right + 1, y), TileID.Dirt);
        }
        TerrainChanges.Reset();
    }

    private static void ObserveAll(ActionContext ctx, params Item[] items)
    {
        ctx.Senses.Loot.Pickups.Clear();
        foreach (Item item in items)
            if (LootIsInWorld(item)) ctx.Senses.Loot.Pickups.Add(new(item, 1f, Vector2.Distance(ctx.Npc.Center, item.Center)));
        ctx.Senses.Loot.Pickups.Sort((a, b) => a.DistanceToCompanion.CompareTo(b.DistanceToCompanion));
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

    internal static ActionContext SetUpFloor()
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

    internal static Item Drop(int type, int stack, Vector2 bottom, int slot = Slot)
    {
        var item = new Item();
        item.SetDefaults(type);
        item.stack = stack;
        item.active = true;
        item.noGrabDelay = 0;
        item.whoAmI = slot;
        item.Bottom = bottom;
        Main.item[slot] = item;
        return item;
    }

    private static void Observe(ActionContext ctx, Item item)
    {
        ctx.Senses.Loot.Pickups.Clear();
        if (LootIsInWorld(item)) ctx.Senses.Loot.Pickups.Add(new(item, 1f, Vector2.Distance(ctx.Npc.Center, item.Center)));
    }

    private static bool LootIsInWorld(Item item) => live::AICompanion.Companion.Brain.Infrastructure.Observation.LootSense.IsWorldDrop(item);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
