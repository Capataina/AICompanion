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
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
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
            Preferences.Current.PotBreaking = false;
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e) { red++; Console.WriteLine($"RED {name}: {e.Message}"); }
            finally
            {
                LimitPlanningWork.Unbounded = false;
                Preferences.Current.PotBreaking = potBreaking;
                Restore();
            }
        }
        Each("I03 a drop in a pit proves its own reach and return", ADropInAPitIsNotCollectedOnAStandableTileAlone);
        Each("I02 a moved drop is proved again", AMovedDropIsProvedAgainBeforeTheWalk);
        Each("I04 partial stack capacity", PartialCargoCapacityValuesAndConcludesTheAcceptedQuantity);
        Each("I05 attribution by transfer", ADropLeavingTheWorldIsAttributedByWhatTheBagReceived);
        Each("I03 one trip unit", ADropsForecastIsTheWalkToAContactPose);
        Each("a drop below a ledge is not a contact pose on the ledge", ADropBelowALedgeIsNotAContactPoseOnTheLedge);
        Each("a heart is not a collection trip", AHeartIsNotACollectionTrip);
        Each("D1 a reachable drop behind refused drops is offered", AReachableDropBehindRefusedDropsIsOffered);
        Each("D2 a drop merged into another world drop", ADropMergedIntoAnotherWorldDropIsNotAPurposeThatWentAway);
        Each("D3 a drop released in the air is offered at its forecast landing", ADropStillFallingIsOfferedAtItsForecastLanding);
        Each("D3b a drop falling through liquid is offered where the liquid will put it", ADropFallingThroughLiquidIsOfferedWhereTheLiquidWillPutIt);
        // Timings under the production allowances, printed and never asserted: they describe this machine.
        foreach (bool warmUp in new[] { true, false })
            foreach (bool pit in new[] { false, true })
            {
                try { MeasureFallingDropCost(pit, warmUp); }
                finally { Preferences.Current.PotBreaking = potBreaking; Restore(); }
            }
        if (red == 0) Console.WriteLine("collection contracts: own reach and return, moved drops, partial capacity, transfer attribution, trip unit, refused-drop budget and merged drops pass");
        return red;
    }

    /// <summary>
    /// Four drops at the bottom of a sealed pit beside the companion, every one of them nearer than a single reachable drop
    /// on the open floor. All five are asked about on one preparation and the floor drop is offered on that preparation,
    /// because the reach question is membership of a flood already run rather than a search that has to be rationed.
    ///
    /// <para>This row used to assert something weaker and it is worth saying why, because the weaker property is what the
    /// starvation looked like from inside. Collection could put only three drops per preparation to a fresh walker search,
    /// so the first preparation spent itself on pit drops and the floor drop arrived some preparations later; the row
    /// asserted a bound on how many, and required the first preparation *not* to offer it, which made the delay part of
    /// the contract. The delay is gone rather than shortened, so the row now asserts its absence. A regression that
    /// reintroduces a per-preparation bound fails here on the first preparation rather than on an arithmetic bound
    /// somebody has to re-derive.</para>
    /// </summary>
    private static void AReachableDropBehindRefusedDropsIsOffered()
    {
        var ctx = SetUpFloor();
        ctx.Npc.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        BuildSealedPit(43, 46);
        var pit = new List<Item>();
        for (int i = 0; i < 4; i++) pit.Add(Drop(ItemID.CopperOre, 5, new Vector2((43 + i) * 16 + 8, 75 * 16), Slot + i));
        Item far = Drop(ItemID.CopperOre, 5, new Vector2(12 * 16 + 8, 60 * 16), Slot + 4);
        float Distance(Item item) => Vector2.Distance(ctx.Npc.Center, item.Center);
        Require(pit.All(drop => Distance(drop) < Distance(far)) && pit.Count >= 4,
            $"premise: every pit drop must be nearer than the floor drop, and there must be several of them; "
            + $"far={Distance(far):0} pit={string.Join(",", pit.Select(d => Distance(d).ToString("0")))}");
        {
            var alone = new CollectNearbyItems();
            ObserveAll(ctx, far);
            Settle(ctx);
            alone.Prepare(ctx);
            Require(alone.Method == "known-drop" && ReferenceEquals(alone.ActivityIdentity, far),
                $"premise: the floor drop must be a usable offer on its own; offer={alone.Eligibility}/{alone.EligibilityReason}");
        }
        var collect = new CollectNearbyItems();
        ObserveAll(ctx, pit.Append(far).ToArray());
        Settle(ctx);
        collect.Prepare(ctx);
        Require(ReferenceEquals(collect.ActivityIdentity, far) && collect.Method == "known-drop",
            "a reachable drop must be offered on the first preparation however many refused drops lie nearer, because "
            + $"nothing rations the reach question any more; target={collect.ActivityIdentity} "
            + $"offer={collect.Eligibility}/{collect.EligibilityReason}");
    }

    /// <summary>Drive the reach flood to completion, because every access question in collection now reads it and an
    /// unfinished flood answers "not yet known" for tiles it simply has not got to yet.</summary>
    private static void Settle(ActionContext ctx)
    {
        var brain = ctx.Companion.Brain;
        var home = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(RequestKind.WithPlayer, ctx.Player.Bottom);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(home, brain.Senses);
        Require(brain.Positioner.ReachComplete, "these rows need a settled reach region before preparing");
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
            WalkBound(ctx, collect, walked);
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
            // Offered where the course discovers work: the census publishes the absorbing drop as usable collection work.
            var offered = VerifyAssistanceTrips.CensusFact(ctx, "collect-target", $"item:{absorber.whoAmI}");
            Require(offered is { Admission: "usable", Purpose: "collect" },
                $"the absorbing drop must be offered on the next observation; census={offered}");
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
        var ctx = SetUpFloor();
        ctx.Npc.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        if (pit) BuildSealedPit(43, 46);
        int landing = (pit ? 75 : 60) * 16;
        var falling = new List<Item>();
        for (int i = 0; i < 4; i++) falling.Add(Drop(ItemID.CopperOre, 5, new Vector2((43 + i) * 16 + 8, 50 * 16), Slot + i));
        Item? far = null;
        var brain = ctx.Companion.Brain;
        var collect = brain.Actions.OfType<CollectNearbyItems>().Single();
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
            // The whole decision, which is what the tick actually spends here. This sampled the family
            // chooser's NearbyAssistance preparation share until 22 September 2026, and that scheduler had
            // been unreached since `0bb2c8a`, so every figure in the line below was taken from an empty
            // list and the line never printed at all.
            nearby.Add((brain.DecideMs, tick));
            // The first tick the course binds the farther drop, which is what sends the body there; the activity's own offer
            // stopped being what the companion does when the hand began working the bound step.
            if (far != null && farOffered < 0 && brain.Course.Last.Binding?.Opportunity.Target == $"item:{far.whoAmI}") farOffered = tick;
        }
        if (warmUp || nearby.Count == 0) return;
        var sorted = nearby.Select(sample => sample.Ms).OrderBy(v => v).ToList();
        string costliest = string.Join(", ", nearby.OrderByDescending(sample => sample.Ms).Take(3).Select(sample => $"{sample.Ms:0.000}@{sample.Tick}"));
        Console.WriteLine($"collection cost, four drops falling {(pit ? "into a sealed pit" : "onto the open floor")} then a reachable drop at tick {farAppears} (300 ticks, production allowances, drops settled by tick {landedBy}): "
            + $"decide p50 {sorted[sorted.Count / 2]:0.000} p95 {sorted[(int)(sorted.Count * .95)]:0.000} max {sorted[^1]:0.000} ms, costliest {costliest}, "
            + $"{sorted.Count(v => v >= 0.5)} of {sorted.Count} ticks at or over 0.5 ms; farther drop first offered at tick {(farOffered < 0 ? "never" : farOffered.ToString())}");
    }

    /// <summary>
    /// A chamber below the floor with no way in that this body fits through. It used to be a pit open at the top, which
    /// was unreachable to a walker because it could drop in and never climb out; a body that flies goes in and comes back,
    /// so the mouth is closed instead and the chamber is sealed on every side. What it tests is unchanged — a drop the
    /// companion cannot get to is not a usable offer however near it is — and only the reason it cannot get there has
    /// moved, from a missing return to a missing way in.
    /// </summary>
    private static void BuildSealedPit(int left, int right)
    {
        for (int x = left - 1; x <= right + 1; x++)
        {
            VerifyOreWork.Place(new Point(x, 60), TileID.Dirt);
            VerifyOreWork.Place(new Point(x, 75), TileID.Dirt);
        }
        for (int x = left; x <= right; x++)
            for (int y = 61; y < 75; y++)
                Main.tile[x, y].ClearEverything();
        for (int y = 60; y <= 75; y++)
        {
            VerifyOreWork.Place(new Point(left - 1, y), TileID.Dirt);
            VerifyOreWork.Place(new Point(right + 1, y), TileID.Dirt);
        }
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
    }

    private static void ObserveAll(ActionContext ctx, params Item[] items)
    {
        ctx.Senses.Loot.Pickups.Clear();
        foreach (Item item in items)
            if (LootIsInWorld(item)) ctx.Senses.Loot.Pickups.Add(new(item, 1f, Vector2.Distance(ctx.Npc.Center, item.Center)));
        ctx.Senses.Loot.Pickups.Sort((a, b) => a.DistanceToCompanion.CompareTo(b.DistanceToCompanion));
    }

    /// <summary>
    /// A drop inside a sealed chamber under the floor the companion stands on. A place to hover beside it proves nothing about
    /// getting to it, and there is no way in that this body fits through, so it must be a known-unusable offer however near it
    /// is; a drop on the open floor beside it must stay usable, which is what stops the row passing on a collection that simply
    /// refuses everything.
    ///
    /// <para>The pair this replaces ran the same scene twice under the walker's one-way-drop switch, once where it could not
    /// get in and once where it could get in and not out. There is no such setting now and no such asymmetry: a flood over free
    /// cells reaches a cell or it does not, and the return is the same flood read backwards.</para>
    /// </summary>
    private static void ADropInAPitIsNotCollectedOnAStandableTileAlone()
    {
        var ctx = SetUpFloor();
        ctx.Npc.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        BuildSealedPit(30, 33);
        Settle(ctx);
        Require(ctx.Companion.Brain.Senses.Reach.Reachable(new Point(31, 74)) == ReachVerdict.Unreachable,
            "the premise needs the chamber genuinely closed to this body, or the refusal below means nothing");
        var collect = new CollectNearbyItems();
        Item pitDrop = Drop(ItemID.CopperOre, 5, new Vector2(31 * 16 + 8, 75 * 16));
        Observe(ctx, pitDrop);
        collect.Prepare(ctx);
        Require(!(collect.Method == "known-drop" || collect.Eligibility == OfferEligibility.Usable),
            $"a drop in a sealed chamber must not be a usable collection offer; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason} value={collect.Score()}");
        Require(collect.Eligibility is OfferEligibility.KnownUnusable,
            $"the refusal must say the drop is known to be unusable; offer={collect.Eligibility}/{collect.EligibilityReason}");
        Item floorDrop = Drop(ItemID.CopperOre, 5, new Vector2(26 * 16 + 8, 60 * 16));
        Observe(ctx, floorDrop);
        collect.Prepare(ctx);
        Require(collect.Method == "known-drop" && collect.Eligibility == OfferEligibility.Usable && ReferenceEquals(collect.ActivityIdentity, floorDrop),
            $"a drop on the open floor must remain a usable offer; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason}");
    }

    /// <summary>
    /// A drop released in mid-air over a floor. The walk must begin before it lands: the pose is proven at the forecast
    /// landing, the offer is usable on the first preparation, and execution asks for the walk rather than a hold on every
    /// tick of the fall. Before the forecast, the pose search ran around the item's instantaneous bottom with a tolerance
    /// of one tile, so a falling drop had no pose at all — 431 rows of the 2026-09-14 capture were refused as
    /// drop-has-no-contact-pose — and the moved-drop guard then held the body every tick, because an item in free fall has
    /// always moved more than a tile since it was proven. The fixture also steps the drop with the game's own arithmetic
    /// and requires the forecast to have named where it actually stopped, so the arc is checked rather than assumed.
    /// </summary>
    private static void ADropStillFallingIsOfferedAtItsForecastLanding()
    {
        var ctx = SetUpFloor();
        var collect = new CollectNearbyItems();
        // Twelve tiles above the floor the ore fixtures build at row 60, four tiles to the companion's side,
        // with a sideways velocity so the landing is not the column it was released in.
        Item drop = Drop(ItemID.CopperOre, 5, new Vector2(26 * 16 + 8, 48 * 16));
        drop.velocity = new Vector2(1.5f, -2f);
        Observe(ctx, drop);
        collect.Prepare(ctx);
        Require(collect.Method == "known-drop" && collect.Eligibility == OfferEligibility.Usable,
            $"a drop still falling must be a usable offer at its forecast landing; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason}");
        var step = WalkBound(ctx, collect, drop);
        var request = collect.Execute(ctx);
        Require(request.Kind == RequestKind.Exact,
            $"the walk must begin before the drop lands rather than holding for every tick of the fall; request={request}");

        // Step the item the way the game does, and hold the bound step to the census on every tick of the fall. A hold used
        // to be the activity's own moved-drop guard; since the course carries the step, what would interrupt the walk is the
        // binder retiring the application because the census moved the contact pose, which is what a wrong forecast does.
        int holds = 0;
        for (int tick = 0; tick < 240 && drop.velocity.Y != 0f; tick++)
        {
            drop.velocity.Y = MathF.Min(drop.velocity.Y + 0.1f, 7f);
            drop.velocity.X *= 0.95f;
            if (MathF.Abs(drop.velocity.X) < 0.1f) drop.velocity.X = 0f;
            Vector2 next = drop.Bottom + drop.velocity;
            if (drop.velocity.Y > 0f && next.Y >= 60 * 16f)
            {
                drop.Bottom = new Vector2(next.X, 60 * 16f);
                drop.velocity = Vector2.Zero;
            }
            else drop.Bottom = next;
            if (!VerifyAssistanceTrips.Revalidate(ctx, step).CanUse) holds++;
            if (collect.Execute(ctx).Kind == RequestKind.Hold) holds++;
        }
        Require(drop.velocity == Vector2.Zero, $"the fixture's own drop must land; bottom={drop.Bottom} velocity={drop.velocity}");
        Require(holds == 0, $"a drop falling exactly as forecast must not interrupt the walk once on the way down; holds={holds}");
        var landed = collect.Execute(ctx);
        Require(landed.Kind == RequestKind.Exact && MathF.Abs(landed.Anchor.X - drop.Bottom.X) <= 3 * 16,
            $"the pose must still be the landed drop's own once it has landed; request={landed} drop={drop.Bottom}");
    }

    /// <summary>
    /// The same contract for a drop falling through liquid, where the game moves an item by a share of its
    /// velocity rather than by all of it.
    ///
    /// <para><c>Item.UpdateItem</c> keeps a separate <c>wetVelocity</c> and steps the position by that instead of
    /// the velocity whenever the item is wet — half in water, three eighths in shimmer, a quarter in honey — and
    /// it captures that share from the velocity as it stood at the top of the tick, before the tick's gravity.
    /// The forecast took the wet gravity and then stepped the whole velocity, so it covered twice the ground in
    /// water and four times in honey, and honey's and shimmer's own gravities were not modelled at all. The
    /// landing row survived that because a drop falling straight down still lands on the same floor; the landing
    /// <b>X</b> did not, and X is what decides which tile the companion walks to.</para>
    ///
    /// <para>The fixture steps the item itself, transcribing the decompiled arithmetic in the game's own order
    /// rather than calling the production forecast, and requires the forecast to have agreed the whole way down:
    /// the moved-drop guard holds the body on any tick the item is more than a tile from where it was proven, so
    /// a forecast with the wrong share reports holds. This isolates the liquid step and not liquid detection —
    /// the wet flags are set directly, because whether the game would call this tile wet is a different question
    /// with a different owner.</para>
    /// </summary>
    private static void ADropFallingThroughLiquidIsOfferedWhereTheLiquidWillPutIt()
    {
        // The release height is per liquid and it is a constraint rather than a preference. A liquid slows the
        // descent by its gravity, its cap and its velocity share together, so honey moves an item down about a
        // fifth as fast as air does — and a twelve-tile fall in honey genuinely does not resolve inside the
        // forecast's horizon, which the offer reports as `drop-landing-undecided`. That is the middle answer
        // working, not a defect, and it is worth knowing before anyone reads an undecided honey drop as a bug:
        // each fall here is sized to land well inside the horizon so that the row is about the arithmetic.
        foreach (var (name, releaseRow, gravity, maxFall, share, setWet) in new (string, int, float, float, float, Action<Item>)[]
        {
            ("water", 48, 0.08f, 5f, 0.5f, (Item i) => i.wet = true),
            ("honey", 56, 0.05f, 3f, 0.25f, (Item i) => { i.wet = true; i.honeyWet = true; }),
            ("shimmer", 54, 0.065f, 4f, 0.375f, (Item i) => { i.wet = true; i.shimmerWet = true; }),
        })
        {
            var ctx = SetUpFloor();
            var collect = new CollectNearbyItems();
            Item drop = Drop(ItemID.CopperOre, 5, new Vector2(26 * 16 + 8, releaseRow * 16));
            // Fast enough sideways that a quarter of it is still a visible drift, since honey's share is the
            // smallest and the premise below has to hold for every liquid in the table.
            drop.velocity = new Vector2(3f, -2f);
            setWet(drop);
            Vector2 released = drop.Bottom;
            Observe(ctx, drop);
            collect.Prepare(ctx);
            Require(collect.Method == "known-drop" && collect.Eligibility == OfferEligibility.Usable,
                $"a drop falling through {name} must be a usable offer at its forecast landing; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason}");
            // The forecast taken at release, which is the whole point: the census's landing is where the drop
            // was priced, and it is the only moment the whole remaining fall is being predicted rather than
            // observed. Comparing the pose after the drop has landed proves nothing, because by then the
            // forecast has nothing left to forecast. It is read from the census the course binds from, the same
            // static forecast the activity's own offer runs.
            var published = VerifyAssistanceTrips.CensusFact(ctx, "collect-target", $"item:{drop.whoAmI}");
            Vector2 forecast = published is { LandingX: double lx, LandingY: double ly } ? new Vector2((float)lx, (float)ly)
                : throw new InvalidOperationException($"a usable {name} drop must be published at the landing it was priced at");
            var step = WalkBound(ctx, collect, drop);

            int holds = 0;
            for (int tick = 0; tick < 480 && drop.velocity.Y != 0f; tick++)
            {
                // Vanilla's order, and the capture before the gravity is the part that matters: wetVelocity is
                // taken at the top of UpdateItem, MoveInWorld then updates the velocity, and the position step
                // at the bottom uses the captured value.
                Vector2 wetVelocity = drop.velocity * share;
                drop.velocity.Y = MathF.Min(drop.velocity.Y + gravity, maxFall);
                drop.velocity.X *= 0.95f;
                if (MathF.Abs(drop.velocity.X) < 0.1f) drop.velocity.X = 0f;
                Vector2 next = drop.Bottom + wetVelocity;
                if (wetVelocity.Y > 0f && next.Y >= 60 * 16f)
                {
                    drop.Bottom = new Vector2(next.X, 60 * 16f);
                    drop.velocity = Vector2.Zero;
                }
                else drop.Bottom = next;
                if (!VerifyAssistanceTrips.Revalidate(ctx, step).CanUse) holds++;
                if (collect.Execute(ctx).Kind == RequestKind.Hold) holds++;
            }
            Require(drop.velocity == Vector2.Zero, $"the fixture's own {name} drop must land; bottom={drop.Bottom} velocity={drop.velocity}");
            // The premise that makes this a test of the share at all: the liquid must have carried the drop a
            // visibly shorter way sideways than a dry fall would, or every arithmetic lands in the same column
            // and the row cannot tell a modelled share from an unmodelled one.
            float drift = MathF.Abs(drop.Bottom.X - released.X);
            float error = MathF.Abs(forecast.X - drop.Bottom.X);
            Console.WriteLine(FormattableString.Invariant(
                $"collection: a drop released at {released.X:F0} fell through {name} to {drop.Bottom.X:F0} ({drift:F0}px of drift); forecast at release said {forecast.X:F0}, out by {error:F0}px, holds={holds}"));
            Require(drift > 8f, FormattableString.Invariant(
                $"the {name} scene must drift sideways, or landing X is the same wherever the share goes; drift={drift:F0}px"));
            // Landing X, asserted against the fall the fixture actually stepped. A tile of slack, which is
            // generous given both sides run the same arithmetic — and far tighter than the error an unmodelled
            // velocity share produces, which is the whole remaining drift over again.
            Require(error <= 16f, FormattableString.Invariant(
                $"the {name} forecast must land the drop where the liquid actually puts it: it said X={forecast.X:F0}, the drop came to rest at X={drop.Bottom.X:F0}, out by {error:F0}px against {drift:F0}px of true drift"));
            Require(holds == 0, $"a drop falling through {name} exactly as forecast must not hold the body on the way down; holds={holds}");
            var landed = collect.Execute(ctx);
            Require(landed.Kind == RequestKind.Exact && MathF.Abs(landed.Anchor.X - drop.Bottom.X) <= 3 * 16,
                $"the pose must be at the {name} drop's own landing column once it has landed; request={landed} drop={drop.Bottom}");
        }
    }

    /// <summary>A drop the course bound rolls fourteen tiles before the walk. The step aimed at where it was must be retired by the
    /// course's own next-use check, not walked, and the drop bound again where it now lies must send the companion there. Until
    /// 23 September 2026 the activity's own moved-drop guard held the body; the census and the binder own that answer now, because
    /// the pose is the course's.</summary>
    private static void AMovedDropIsProvedAgainBeforeTheWalk()
    {
        var ctx = SetUpFloor();
        var collect = new CollectNearbyItems();
        Item drop = Drop(ItemID.CopperOre, 5, new Vector2(26 * 16 + 8, 60 * 16));
        Observe(ctx, drop);
        var step = WalkBound(ctx, collect, drop);
        drop.Bottom = new Vector2(40 * 16 + 8, 60 * 16);
        var stale = VerifyAssistanceTrips.Revalidate(ctx, step);
        Require(!stale.CanUse && stale.Reason == "assistance-application-changed",
            $"a drop that moved after it was bound must retire the application aimed at its old position; validation={stale}");
        var fresh = WalkBound(ctx, collect, drop);
        var request = collect.Execute(ctx);
        Require(fresh.Opportunity == step.Opportunity && request.Kind == RequestKind.Exact && MathF.Abs(request.Anchor.X - drop.Bottom.X) <= 3 * 16,
            $"the same drop bound again must be walked to where it now lies; request={request} drop={drop.Bottom} opportunity={fresh.Opportunity}");
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
        WalkBound(ctx, collect, drop);
        Require(ctx.Companion.Bag.Collect(drop, ctx.Player) && drop.stack == 48 && LootIsInWorld(drop),
            $"the real transfer must take exactly the two that fit; stack left={drop.stack}");
        var conclusion = collect.ConcludeAttempt(0);
        Require(conclusion is { Status: AttemptStatus.Partial, Cause: "drop-partly-transferred" },
            $"taking part of a stack is partial collection, with the rest still in the world; got {conclusion}");
        // Refused where the course discovers work — the census publishes the remainder as unusable for capacity — and in the
        // activity's own offer, which is what the recorder reads.
        var remainder = VerifyAssistanceTrips.CensusFact(ctx, "collect-target", $"item:{drop.whoAmI}");
        Require(remainder is { Admission: "unusable", Reason: "cargo-capacity" },
            $"the census must refuse the remainder the full cargo cannot take; census={remainder}");
        Observe(ctx, drop);
        collect.Prepare(ctx);
        Require(collect.EligibilityReason == "nearby-drops-exceed-cargo-capacity",
            $"the remainder the full cargo cannot take must be refused for capacity; offer={collect.EligibilityReason}");
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

    /// <summary>
    /// Hearts are consumed by the player, never stored. Contact pickup skips them, so walking there is
    /// the stand-on-a-heart freeze of 18 September 2026. Collection must not offer the trip.
    /// </summary>
    private static void AHeartIsNotACollectionTrip()
    {
        var ctx = SetUpFloor();
        ctx.Player.statLife = ctx.Player.statLifeMax;
        var collect = new CollectNearbyItems();
        Item heart = Drop(ItemID.Heart, 1, ctx.Npc.Bottom);
        Observe(ctx, heart);
        collect.Prepare(ctx);
        Require(collect.Method != "known-drop" && collect.Score() == 0,
            $"a heart must not be a collection trip; method={collect.Method} offer={collect.Eligibility}/{collect.EligibilityReason} value={collect.Score()}");
    }

    /// <summary>A gel two tiles below the companion's floor is not something it is already standing on. The 13 Sep 0.22.43
    /// session froze 1,896 ticks on a ledge whose inflated AABB grazed a drop on the slope; proving contact only on the same
    /// floor, inside pickup reach minus arrival slack, is what stops that freeze.</summary>
    private static void ADropBelowALedgeIsNotAContactPoseOnTheLedge()
    {
        var ctx = SetUpFloor();
        int ledge = 58;
        for (int x = 18; x <= 22; x++)
        {
            Tile tile = Main.tile[x, ledge];
            tile.ClearEverything();
            tile.HasTile = true;
            tile.TileType = TileID.Dirt;
        }
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        ctx.Npc.position = new Vector2(20 * 16, ledge * 16 - ctx.Npc.height);
        var collect = new CollectNearbyItems();
        Item drop = Drop(ItemID.Gel, 3, new Vector2(20 * 16 + 8, 60 * 16));
        Observe(ctx, drop);
        collect.Prepare(ctx);
        if (collect.Method == "known-drop")
        {
            var request = collect.Execute(ctx);
            Require(MathF.Abs(request.Anchor.Y - drop.Bottom.Y) <= 16f,
                $"a drop on the floor below a ledge must not use the ledge as its contact pose; pose={request.Anchor} drop={drop.Bottom} offer={collect.EligibilityReason}");
        }
        else
            Require(collect.EligibilityReason is "drop-has-no-contact-pose" or "drop-unreachable" or "drop-would-strand-return" or "drop-approach-undecided",
                $"if the ledge cannot pick the drop up, the offer must say so; method={collect.Method} offer={collect.EligibilityReason}");
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
        float walkSpeed = live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbPace.MaxSpeed;
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
        WalkBound(ctx, collect, drop);
        return (collect, ctx, drop);
    }

    /// <summary>
    /// Hand collection the course's step for this drop the way the tick does — the census's own site bound by the real binder,
    /// selected through the activity owner, whose execution opens the attempt — and run one execution. It replaced a direct
    /// `BeginAttempt` then `Execute` on 23 September 2026, because the hand no longer walks to whatever the activity's own search
    /// found: it walks to the step it is handed, and a row that hands it none watches a hand that does nothing.
    /// </summary>
    private static live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses.StepBinding WalkBound(
        ActionContext ctx, CollectNearbyItems collect, Item drop)
    {
        string target = $"item:{drop.whoAmI}";
        var step = VerifyAssistanceTrips.BindCensusSite(ctx, "collect-target", o => o.Key.Target == target, target);
        var owner = ctx.Companion.Brain.Activity;
        owner.Select(collect, ctx, step);
        owner.BeginExecution();
        collect.Execute(ctx);
        return step;
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
