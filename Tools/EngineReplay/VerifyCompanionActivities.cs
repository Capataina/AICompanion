extern alias live;
using FindToolAccess = live::AICompanion.Companion.Brain.WorldInteractions.FindToolAccess;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using Protection = live::AICompanion.Companion.Brain.WorldInteractions.WorldProtection.ProtectCompanionHomes;
using Torches = live::AICompanion.Companion.Brain.WorldInteractions.Torch.PlaceSuppliedTorches;
using OreFinder = live::AICompanion.Companion.Brain.WorldInteractions.Mining.OreFinder;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;

internal static class VerifyCompanionActivities
{
    public static int Run()
    {
        Preferences saved = Preferences.Current;
        try
        {
            Preferences.Current = new Preferences { TorchPlacement = false, PotBreaking = false };
            Protection.Reset();
            WorkWinsOutsideFollowComfort();
            ContinuingTargetsKeepTheirIdentity();
            CollectionComparesKnownDropsAndPotentialContents();
            CollectionRejectsReplacedWorldSlots();
            SharedCombatSpacingDoesNotNeedAnOrdinaryOffer();
            DangerIsChargedOnceToTheActorItThreatens();
            ConsecutiveJobsEarnTheirOwnAllowance();
            ActivityOwnershipSurvivesInterruption();
            InvalidCandidatesCannotBecomeTheFallback();
            InvalidatedCandidatesAreReconsideredWithoutDiscovery();
            ReplacedMiningMaterialYieldsToAPreparedSibling();
            StallsSurviveBehaviourChanges();
            ComfortableFollowingHasNoRegroupPressure();
            RemoteJobReleasesAndDiscoversNearbyOre();
            RetainedMiningRespectsTheCompanionsRange();
            ApproximateArrivalMustContinueApproaching();
            BedsProtectTheRoomAndItsBoundary();
            DoorsKeepTheWholeBedroomProtected();
            TorchSupplyIsDebitedOnlyAfterPlacement();
            AmbientFallbackDoesNotInventSamples();
            AFamilyAllowanceDefersSiblingsFairly();
            TorchRecommendationsPreserveThePlayersCursor();
            InteractionJumpsRequireClearanceAndSafeLanding();
            Console.WriteLine("companion activities: resource/follow competition, actor-specific danger charged once, remote job release, actual swing reach, bed protection and native torch inventory contracts pass");
            return 0;
        }
        finally { Preferences.Current = saved; Protection.Reset(); }
    }

    private static void WorkWinsOutsideFollowComfort()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        var chosen = ctx.Companion.Brain.Chooser.Choose(ctx);
        Require(chosen?.Name == "mine", $"reachable ore at 480px separation must beat ordinary following; got {chosen?.Name ?? "none"}");
    }

    private sealed class ActivityProbe : live::AICompanion.Companion.Brain.Behaviours.CompanionAction
    {
        public object Identity = new();
        public Vector2 Target;
        public float Value = 1;
        public int Entries;
        public int Exits;
        public int Preparations;
        public Action? DuringPreparation;
        public live::AICompanion.Companion.Brain.BehaviourSelection.PurposeFamily Purpose
            = live::AICompanion.Companion.Brain.BehaviourSelection.PurposeFamily.NearbyAssistance;
        public bool Excursion = true;
        public override string Name => "probe";
        public override bool IsExcursion => Excursion;
        public override live::AICompanion.Companion.Brain.BehaviourSelection.PurposeFamily Family
            => Purpose;
        public override Vector2? ActivityTarget => Target;
        public override object ActivityIdentity => Identity;
        public bool Allows(live::AICompanion.Companion.Brain.Behaviours.ActionContext ctx) => AllowsTarget(ctx, Target, Identity);
        private float preparedValue;
        public override void Prepare(in live::AICompanion.Companion.Brain.Behaviours.ActionContext ctx)
        {
            Preparations++;
            DuringPreparation?.Invoke();
            bool allowed = Allows(ctx);
            preparedValue = allowed ? Value : 0f;
            Classify(allowed ? live::AICompanion.Companion.Brain.Behaviours.OfferEligibility.Usable
                : live::AICompanion.Companion.Brain.Behaviours.OfferEligibility.PolicyForbidden, allowed ? "probe" : "outside-activity-allowance");
        }
        public override float Score() => preparedValue;
        public override void Enter(in live::AICompanion.Companion.Brain.Behaviours.ActionContext ctx) => Entries++;
        public override void Exit(in live::AICompanion.Companion.Brain.Behaviours.ActionContext ctx) { Exits++; base.Exit(ctx); }
        public override live::AICompanion.Companion.Brain.PositionSelection.PositionRequest Execute(in live::AICompanion.Companion.Brain.Behaviours.ActionContext ctx)
            => live::AICompanion.Companion.Brain.PositionSelection.PositionRequest.Hold;
    }

    /// <summary>
    /// With a zero family share each family prepares exactly one optional child per comparison, so
    /// the rotation, the deferred classification and the never-starved children are deterministic.
    /// The combat probe wins the first two comparisons so the incumbent is outside the gathering
    /// family whose rotation is under test.
    /// </summary>
    private static void AFamilyAllowanceDefersSiblingsFairly()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var chooser = ctx.Companion.Brain.Chooser;
        var gathering = live::AICompanion.Companion.Brain.BehaviourSelection.PurposeFamily.Gathering;
        var deferredOffer = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility.Deferred;
        var first = new ActivityProbe { Target = ctx.Player.Bottom, Value = .4f, Purpose = gathering };
        var second = new ActivityProbe { Target = ctx.Player.Bottom, Value = .9f, Purpose = gathering };
        var combat = new ActivityProbe { Target = ctx.Player.Bottom, Value = .95f, Purpose = live::AICompanion.Companion.Brain.BehaviourSelection.PurposeFamily.Combat };
        var company = new ActivityProbe { Target = ctx.Player.Bottom, Value = .1f, Excursion = false };
        chooser.Actions.Clear();
        chooser.Actions.AddRange(new live::AICompanion.Companion.Brain.Behaviours.CompanionAction[] { first, second, combat, company });
        chooser.FamilyPreparationMilliseconds = 0;

        var chosen = chooser.Choose(ctx);
        Require(first.Preparations == 1 && second.Preparations == 0 && combat.Preparations == 1 && company.Preparations == 1,
            $"a spent share must defer the second optional sibling only; first={first.Preparations} second={second.Preparations} combat={combat.Preparations} company={company.Preparations}");
        Require(chooser.LastScores.Single(s => ReferenceEquals(s.Action, second)) is { Final: 0, Raw: 0 } row && row.Eligibility == deferredOffer,
            "a deferred child must be reported as deferred with no value, not as an absent opportunity");
        Require(chooser.Queries.LastFamilies.Single(f => f.Family == gathering) is { Prepared: 1, Deferred: 1 },
            "the family summary must count what was prepared and what was deferred");
        Require(ReferenceEquals(chosen, combat), "the fixture's incumbent must sit outside the rotating family");

        chooser.Choose(ctx);
        Require(first.Preparations == 1 && second.Preparations == 1 && company.Preparations == 2 && combat.Preparations == 2,
            $"the next comparison must start from the deferred sibling while non-excursion and incumbent children still prepare; first={first.Preparations} second={second.Preparations}");

        combat.Value = 0;
        chosen = chooser.Choose(ctx);
        Require(first.Preparations == 2 && second.Preparations == 1 && ReferenceEquals(chosen, first),
            $"a deferred sibling's retained higher value must not win; chosen={chosen?.Name} first={first.Preparations} second={second.Preparations}");

        // Two fresh optional siblings with no incumbent among them: a zero share defers one, and
        // lifting wall-clock allowances must prepare both, so offline determinism never starves a family.
        var left = new ActivityProbe { Target = ctx.Player.Bottom, Value = .3f, Purpose = gathering };
        var right = new ActivityProbe { Target = ctx.Player.Bottom, Value = .2f, Purpose = gathering };
        chooser.Actions.Clear();
        chooser.Actions.AddRange(new live::AICompanion.Companion.Brain.Behaviours.CompanionAction[] { left, right, company });
        chooser.Choose(ctx);
        Require(left.Preparations + right.Preparations == 1, "the counter-case must defer one sibling under a zero share");
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        try
        {
            chooser.Choose(ctx);
            Require(left.Preparations + right.Preparations == 3 && chooser.Queries.LastFamilies.All(f => f.Deferred == 0),
                "with wall-clock allowances lifted no sibling may be deferred");
        }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }
    }

    private static void ActivityOwnershipSurvivesInterruption()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var owner = ctx.Companion.Brain.Chooser.Activity;
        var activity = new ActivityProbe { Target = ctx.Player.Bottom };
        owner.Select(activity, ctx);
        long first = owner.Id;
        owner.BeginExecution();
        ulong changed = owner.ChangedAt;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        owner.Select(activity, ctx); owner.BeginExecution();
        Require(owner.ChangedAt == changed, "an unchanged executing purpose must not publish a fictional lifecycle transition each tick");
        owner.Suspend(ctx, "projectile"); owner.Suspend(ctx, "projectile");
        Require(owner.Id == first && activity.Exits == 1 && activity.HasActivityAllowance,
            "one interruption must release its method once without losing its purpose or continuation allowance");
        owner.Select(activity, ctx);
        Require(owner.Id == first && activity.Entries == 2 && owner.LastEndedId == 0,
            "reselection after interruption must resume the same purpose rather than manufacture a failed job");
        activity.Identity = new object();
        owner.Select(activity, ctx);
        Require(owner.Id > first && owner.LastEndedId == first && activity.Entries == 2,
            "a new target under the same executor needs a new activity identity without resetting its prepared method");
        owner.Suspend(ctx, "recovery");
        int releases = activity.Exits;
        owner.Select(null, ctx);
        Require(owner.Id == 0 && owner.Current == null && activity.Exits == releases && !activity.HasActivityAllowance,
            "abandoning a suspended activity must clear ownership without releasing its method twice");
    }

    private static void InvalidCandidatesCannotBecomeTheFallback()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        var chooser = ctx.Companion.Brain.Chooser;
        var invalid = new ActivityProbe { Target = ctx.Player.Bottom, Value = float.NaN };
        chooser.Actions.Clear(); chooser.Actions.Add(invalid);
        Require(chooser.Choose(ctx) == null && invalid.Entries == 0,
            "an invalid last candidate must not be activated through the all-zero fallback");
        Require(chooser.LastScores.Single().Error == "invalid-raw-value", "the rejected input must retain its diagnostic reason");
        invalid.Value = 0;
        Require(chooser.Choose(ctx) == null && invalid.Entries == 0
            && chooser.LastNominations.All(n => n.Activity == null),
            "zero-value children must leave all families empty rather than activate the last registered behaviour");
        chooser.Actions.Clear();
        Require(chooser.Choose(ctx) == null, "an empty board must produce no activity rather than indexing a missing fallback");
    }

    private static void InvalidatedCandidatesAreReconsideredWithoutDiscovery()
    {
        foreach (string invalidation in new[] { "identity", "enemy", "generation", "item", "item-type", "item-slot", "all" })
        {
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
            var first = new ActivityProbe { Target = ctx.Player.Bottom, Value = 2 };
            if (invalidation is "enemy" or "generation") first.Identity = new NPC { active = true, life = 10, whoAmI = 12 };
            Item previous = Main.item[5];
            if (invalidation is "item" or "item-type" or "item-slot")
                first.Identity = Main.item[5] = new Item { active = true, stack = 1, type = ItemID.CopperOre, whoAmI = 5 };
            var second = new ActivityProbe { Target = ctx.Player.Bottom, Value = 1 };
            // Families prepare in enum order, so the invalidated candidate sits in the family that
            // prepares first and the invalidating sibling in a later one; within one family the
            // registration order is the rotation's starting order.
            if (invalidation != "identity") first.Purpose = live::AICompanion.Companion.Brain.BehaviourSelection.PurposeFamily.Combat;
            if (invalidation == "all") second.Identity = new Item { active = false, stack = 0 };
            second.DuringPreparation = () =>
            {
                if (first.Identity is NPC npc)
                {
                    if (invalidation == "generation") live::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Spawn(npc);
                    else npc.active = false;
                }
                else if (first.Identity is Item item)
                {
                    if (invalidation == "item-slot") Main.item[5] = new Item { active = true, stack = 1, type = item.type, whoAmI = 5 };
                    else if (invalidation == "item-type") item.type = ItemID.IronOre;
                    else item.stack = 0;
                }
                else first.Identity = new object();
            };
            var chooser = ctx.Companion.Brain.Chooser;
            chooser.Actions.Clear(); chooser.Actions.Add(first); chooser.Actions.Add(second);
            Require(ReferenceEquals(chooser.Choose(ctx), invalidation == "all" ? null : second)
                && first.Entries == 0 && second.Entries == (invalidation == "all" ? 0 : 1),
                "an invalidated nomination must yield to its prepared sibling: " + invalidation);
            Require(first.Preparations == 1 && second.Preparations == 1,
                "activation reconsideration must not repeat discovery: " + invalidation);
            Require(chooser.LastScores[0].Raw == 2 && chooser.LastScores[0].Final == 0
                && chooser.LastScores[0].Error.StartsWith("prepared-"),
                "the rejected nomination must retain its original value and explicit cause: " + invalidation);
            if (invalidation == "item-slot") Require(chooser.LastScores[0].Error == "prepared-item-slot-replaced",
                "replacement must name its availability failure rather than devalue the activity's usefulness");
            Main.item[5] = previous;
        }
    }

    private static void ReplacedMiningMaterialYieldsToAPreparedSibling()
    {
        Point point = new(25, 89);
        var (mine, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, point);
        var sibling = new ActivityProbe { Target = ctx.Player.Bottom, Value = .01f };
        sibling.DuringPreparation = () =>
        {
            Tile tile = Main.tile[point.X, point.Y];
            tile.TileType = TileID.Tin;
        };
        var chooser = ctx.Companion.Brain.Chooser;
        chooser.Actions.Clear(); chooser.Actions.Add(mine); chooser.Actions.Add(sibling);
        Require(ReferenceEquals(chooser.Choose(ctx), sibling) && sibling.Preparations == 1 && sibling.Entries == 1,
            "changed mining material must yield to an already prepared sibling without another discovery pass");
        Require(chooser.LastScores[0].Raw > 0 && chooser.LastScores[0].Final == 0
            && chooser.LastScores[0].Error == "prepared-tile-material-changed",
            "the invalid mining offer must retain its usefulness and name material replacement as the rejection");
    }

    private static void ContinuingTargetsKeepTheirIdentity()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        var activity = new ActivityProbe { Target = ctx.Player.Bottom + new Vector2(500, 0) };
        Require(activity.Allows(ctx), "new target within acquisition radius must be admitted");
        activity.AdmitActivity();
        activity.Target = ctx.Player.Bottom + new Vector2(Preferences.Current.NewActivityRadius + 100, 0);
        Require(activity.Allows(ctx), "same moving target must retain allowance after travelling more than a work-site radius");
        activity.Identity = new object();
        Require(!activity.Allows(ctx), "a different entity at the same position must not inherit continuation");
        activity.Target = ctx.Player.Bottom;
        activity.AdmitActivity(); activity.Exit(ctx);
        activity.Target = ctx.Player.Bottom + new Vector2(Preferences.Current.NewActivityRadius + 100, 0);
        Require(!activity.Allows(ctx), "completed ordinary excursion must release its allowance");
        ctx.Player.Bottom = new Vector2(80, 960);
        activity.Target = ctx.Player.Bottom + new Vector2(Preferences.Current.NewActivityRadius + 100, 0);
        ctx.Companion.Brain.Chooser.RecordWork(activity.Target);
        var loot = new live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.CollectNearbyItems();
        var item = new Item(); item.SetDefaults(ItemID.CopperOre); item.active = true; item.Bottom = activity.Target;
        Item previous = Main.item[5];
        item.whoAmI = 5; Main.item[5] = item;
        ctx.Senses.Loot.Pickups.Clear();
        ctx.Senses.Loot.Pickups.Add(new(item, .3f, Vector2.Distance(ctx.Npc.Bottom, item.Bottom)));
        loot.Prepare(ctx);
        Require(loot.Score() > 0, "actual looting must inherit the completed work site's continuation allowance");
        float capturedValue = loot.Score(), capturedTrip = loot.ForecastTicks();
        Vector2? capturedTarget = loot.ActivityTarget;
        ctx.Senses.Loot.Pickups.Clear();
        item.Bottom += new Vector2(32, 0);
        Require(loot.Score() == capturedValue && loot.ForecastTicks() == capturedTrip && loot.ActivityTarget == capturedTarget,
            "evaluating prepared loot must neither discover again nor change with a live item's movement");
        item.active = false;
        Require(loot.Execute(ctx).Kind == RequestKind.Hold,
            "a disappeared prepared item must be rejected before executing its approach");
        item.active = true;
        item.Bottom = capturedTarget!.Value;
        ctx.Senses.Loot.Pickups.Add(new(item, .3f, Vector2.Distance(ctx.Npc.Bottom, item.Bottom)));
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 601);
        loot.Prepare(ctx);
        Require(loot.Score() == 0, "expired work must not grant a new loot target an indefinite allowance");
        Main.item[5] = previous;
    }

    private static void CollectionComparesKnownDropsAndPotentialContents()
    {
        bool oldPolicy = Preferences.Current.PotBreaking;
        Item previous = Main.item[5];
        try
        {
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
            Main.tile[25, 59].ClearEverything();
            Main.tileSolid[TileID.Pots] = false;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
            {
                Tile tile = Main.tile[22 + x, 58 + y];
                tile.ClearEverything(); tile.HasTile = true; tile.TileType = TileID.Pots;
                tile.TileFrameX = (short)(x * 18); tile.TileFrameY = (short)(y * 18);
            }
            Preferences.Current.PotBreaking = true;
            ctx.Senses.Loot.Pickups.Clear();
            var collect = new live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.CollectNearbyItems();
            collect.Prepare(ctx);
            Require(collect.Score() > 0 && collect.Method == "potential-pot-contents" && collect.ForecastTicks() > 0,
                "an accessible pot must be a costed uncertain collection opportunity");
            var pot = (Point)collect.ActivityIdentity!;
            float value = collect.Score(), time = collect.ForecastTicks();
            var item = new Item(); item.SetDefaults(ItemID.CopperOre); item.active = true; item.Bottom = ctx.Npc.Bottom;
            item.whoAmI = 5; Main.item[5] = item;
            ctx.Senses.Loot.Pickups.Add(new(item, 1f, 0f));
            Require(collect.Score() == value && collect.ForecastTicks() == time && collect.Method == "potential-pot-contents",
                "comparison must not rediscover a newly appeared drop");
            collect.Prepare(ctx);
            Require(collect.Method == "known-drop" && ReferenceEquals(collect.ActivityIdentity, item),
                "a valuable drop at the feet must compete within the same collecting activity");
            collect.AdmitActivity();
            Preferences.Current.PotBreaking = false;
            collect.Prepare(ctx);
            Require(collect.HasActivityAllowance && collect.Method == "known-drop",
                "disabled pot discovery must not release a known-drop activity's admission");
            Preferences.Current.PotBreaking = true;
            ctx.Senses.Loot.Pickups.Clear();
            collect.Prepare(ctx);
            Require(collect.Method == "potential-pot-contents", "collection must reconsider surviving pots after known drops disappear");
            Preferences.Current.PotBreaking = false;
            collect.Execute(ctx);
            Require(Main.tile[pot.X, pot.Y].HasTile, "changing pot permission after preparation must prevent the native edit");
            Preferences.Current.PotBreaking = true;
            collect.Prepare(ctx);
            Require(collect.Score() > 0 && collect.Method == "potential-pot-contents",
                "permission restored after an execution-time refusal must refresh discovery without waiting for the old search deadline");
            foreach (Item slot in ctx.Companion.Bag.Items) { slot.SetDefaults(ItemID.StoneBlock); slot.stack = slot.maxStack; }
            collect.Prepare(ctx);
            Require(collect.Score() == 0, "unknown contents cannot promise collection capacity from a full bag");
            Require(ctx.Companion.Brain.Chooser.Actions.Count(a => a.Name == "collect") == 1
                && !ctx.Companion.Brain.Chooser.Actions.Any(a => a.Name is "loot" or "break-pots"),
                "collection must have one registered behaviour for drops and pots");
        }
        finally { Preferences.Current.PotBreaking = oldPolicy; Main.item[5] = previous; }
    }

    private static void CollectionRejectsReplacedWorldSlots()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var item = new Item(); item.SetDefaults(ItemID.CopperOre);
        item.active = true; item.whoAmI = 5; item.Bottom = ctx.Npc.Bottom;
        Item previous = Main.item[5];
        try
        {
            Main.item[5] = item;
            ctx.Senses.Loot.Pickups.Clear();
            ctx.Senses.Loot.Pickups.Add(new(item, 1f, 0f));
            var collect = new live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.CollectNearbyItems();
            collect.Prepare(ctx);
            Require(collect.Method == "known-drop" && collect.Execute(ctx).Kind != RequestKind.Hold,
                "the captured live world drop must be usable before slot replacement");
            var replacement = new Item(); replacement.SetDefaults(ItemID.CopperOre);
            replacement.active = true; replacement.whoAmI = 5; replacement.Bottom = item.Bottom;
            Main.item[5] = replacement;
            Require(collect.Execute(ctx).Kind == RequestKind.Hold,
                "an active detached item must not remain executable after its world slot is replaced by the same type");
            collect.Prepare(ctx);
            Require(collect.Score() == 0,
                "stale sensed drops must not produce offers after their world slot is replaced");
            ctx.Senses.Loot.Pickups.Clear();
            ctx.Senses.Loot.Pickups.Add(new(replacement, 1f, 0f));
            collect.Prepare(ctx);
            Require(collect.Method == "known-drop" && ReferenceEquals(collect.ActivityIdentity, replacement),
                "a newly observed replacement must receive its own collection identity");
        }
        finally { Main.item[5] = previous; }
    }

    private static void SharedCombatSpacingDoesNotNeedAnOrdinaryOffer()
    {
        foreach (bool emptyOffers in new[] { false, true })
        {
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
            Main.tile[25, 89].ClearEverything();
            ctx.Player.Bottom = new Vector2(80 * 16, 90 * 16);
            var enemy = Main.npc[30];
            enemy.SetDefaults(NPCID.Zombie);
            enemy.active = true; enemy.damage = 100; enemy.dontTakeDamage = true;
            enemy.Bottom = ctx.Npc.Bottom + new Vector2(64, 0);
            enemy.velocity = Vector2.Zero;
            VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            var activity = new ActivityProbe { Target = ctx.Npc.Bottom };
            var chooser = ctx.Companion.Brain.Chooser;
            chooser.Actions.Clear();
            if (!emptyOffers) { chooser.Actions.Add(activity); chooser.Activity.Select(activity, ctx); chooser.Activity.BeginExecution(); }
            bool observedSpacing = false, observedRelease = false;
            float initialGap = Vector2.Distance(ctx.Npc.Bottom, enemy.Bottom);
            for (int tick = 0; tick < 300; tick++)
            {
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
                var safety = ctx.Companion.Brain.Safety;
                if (safety.Active && safety.Kind == "combat-spacing")
                {
                    observedSpacing = true;
                    Require(ctx.Companion.Brain.ControlGrants.Last!.Value.Hand
                        == live::AICompanion.Companion.Brain.ActivityCoordination.HandGrant.Available,
                        "shared spacing must leave compatible shooting available");
                    Require(emptyOffers ? chooser.Current == null
                        : chooser.Activity.Phase == live::AICompanion.Companion.Brain.BehaviourSelection.ActivityPhase.Suspended,
                        "spacing must operate without an offer or suspend the ordinary activity");
                }
                VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
                if (observedSpacing && !safety.Active && ctx.Companion.Motor.State.OnGround)
                { observedRelease = true; break; }
            }
            Require(observedSpacing && observedRelease
                && Vector2.Distance(ctx.Npc.Bottom, enemy.Bottom) > initialGap
                && ctx.Companion.Brain.Safety.LastEndReason == "safe-state-observed"
                && ctx.Companion.Brain.Safety.CombatSpace.IsSatisfied(ctx),
                $"shared spacing must produce and release a stable retreat, emptyOffers={emptyOffers}, started={observedSpacing}, released={observedRelease}, gap={Vector2.Distance(ctx.Npc.Bottom, enemy.Bottom)}, reason={ctx.Companion.Brain.Safety.Reason}");
            Require(!new live::AICompanion.Companion.Brain.BehaviourSelection.Chooser().Actions.Any(a => a.Name == "kite"),
                "kiting must not remain an ordinary family candidate");
        }

        var (_, playerOnly) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 89));
        playerOnly.Player.Bottom = new Vector2(80 * 16, 90 * 16);
        var playerThreat = Main.npc[30];
        playerThreat.SetDefaults(NPCID.Zombie);
        playerThreat.active = true; playerThreat.damage = 100; playerThreat.dontTakeDamage = true;
        playerThreat.Bottom = playerOnly.Player.Bottom - new Vector2(64, 0);
        playerOnly.Companion.Brain.Chooser.Actions.Clear();
        VerifyCompanionLifecycle.TickWithOneControlGrant(playerOnly.Companion);
        Require(playerOnly.Senses.Threats.PlayerDanger > 0 && playerOnly.Senses.Threats.CompanionDanger == 0
            && !playerOnly.Companion.Brain.Safety.Active,
            "player-only danger must not become the companion's own spacing response");
    }

    /// <summary>
    /// Four matched scenes on one ore floor differ only in who a hostile threatens: nobody, the player,
    /// the companion or both. Threat, protection, reunion and chooser run in production order. Mining's
    /// raw value must not move in any of them, because the player's need for help reaches optional work
    /// once, through the shared protection factor, and a threat to the companion alone must change
    /// neither that factor, the reunion charge nor the value of guarding. A raw value that still
    /// multiplied in player danger, or a self-only threat leaking into either shared factor, fails here.
    /// </summary>
    private static void DangerIsChargedOnceToTheActorItThreatens()
    {
        var scenes = new (string Name, bool Player, bool Companion)[]
            { ("neither", false, false), ("player", true, false), ("companion", false, true), ("both", true, true) };
        var seen = new Dictionary<string, (float Raw, float Protection, float Reunion, float DelayCost, float Guard, float PlayerDanger, float CompanionDanger)>();
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        try
        {
            foreach (var scene in scenes)
            {
                var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
                ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
                for (int i = 30; i <= 31; i++) Main.npc[i] = new NPC();
                void Hostile(int slot, Vector2 feet)
                {
                    NPC npc = Main.npc[slot];
                    npc.SetDefaults(NPCID.Zombie);
                    npc.whoAmI = slot; npc.active = true; npc.damage = 100; npc.dontTakeDamage = true;
                    npc.Bottom = feet;
                }
                if (scene.Player) Hostile(30, ctx.Player.Bottom - new Vector2(64, 0));
                if (scene.Companion) Hostile(31, ctx.Npc.Bottom - new Vector2(64, 0));
                var brain = ctx.Companion.Brain;
                brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
                brain.Senses.SetInterventionEstimate(ctx.Companion.Arsenal.EstimateInterventionTicks(ctx));
                brain.Chooser.Choose(ctx);
                var mine = brain.Chooser.LastScores.Single(s => s.Action.Name == "mine");
                var guard = brain.Chooser.LastScores.Single(s => s.Action.Name == "guard");
                seen[scene.Name] = (mine.Raw, mine.Protection, mine.Reunion, brain.Chooser.Reunion.DelayCostPerTick, guard.Raw,
                    brain.Senses.Threats.PlayerDanger, brain.Senses.Threats.CompanionDanger);
            }
        }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }
        string ledger = string.Join("; ", seen.Select(s => $"{s.Key}: raw={s.Value.Raw} protection={s.Value.Protection} reunion={s.Value.Reunion} delay={s.Value.DelayCost} guard={s.Value.Guard} playerDanger={s.Value.PlayerDanger} companionDanger={s.Value.CompanionDanger}"));
        var (neither, player, companion, both) = (seen["neither"], seen["player"], seen["companion"], seen["both"]);
        Require(neither.PlayerDanger == 0 && neither.CompanionDanger == 0 && player.PlayerDanger > 0 && player.CompanionDanger == 0
            && companion.PlayerDanger == 0 && companion.CompanionDanger > 0 && both.PlayerDanger > 0 && both.CompanionDanger > 0,
            $"the four scenes must threaten exactly the actors they name, or the matrix tests nothing; {ledger}");
        Require(neither.Raw > 0 && seen.Values.All(v => v.Raw == neither.Raw),
            $"mining's raw value must not read either actor's danger; the player's need reaches it once, as protection; {ledger}");
        Require(neither.Protection == 1 && companion.Protection == 1 && player.Protection < 1 && MathF.Abs(both.Protection - player.Protection) < 1e-5f,
            $"only a threat to the player may discount optional work for protection, and a companion threat must not add to it; {ledger}");
        Require(seen.Values.All(v => v.DelayCost == neither.DelayCost && v.Reunion == neither.Reunion),
            $"no threat to either actor may change the separation charge while reunion evidence is unchanged; {ledger}");
        Require(neither.Guard == 0 && companion.Guard == 0 && player.Guard > 0,
            $"a threat to the companion alone creates no player-protection offer; {ledger}");
    }

    private static void ComfortableFollowingHasNoRegroupPressure()
    {
        foreach (var mode in Enum.GetValues<live::AICompanion.Companion.PlayerIntegration.CompanionDistanceMode>())
        {
            Preferences.Current.DistanceMode = mode;
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
            Main.tile[25, 59].ClearEverything();
            ctx.Player.Bottom = ctx.Npc.Bottom + new Vector2(192 * Preferences.Current.FollowComfortScale - 1, 0);
            Require(Collision.CanHitLine(ctx.Npc.position, ctx.Npc.width, ctx.Npc.height, ctx.Player.position, ctx.Player.width, ctx.Player.height),
                "comfortable-follow fixture must have a clear local connection");
            ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
            ctx.Companion.Brain.Chooser.Choose(ctx);
            Require(ctx.Companion.Brain.Chooser.RegroupUrgency == 0, $"{mode} comfortable following must not request regrouping");
        }
        Preferences.Current.DistanceMode = live::AICompanion.Companion.PlayerIntegration.CompanionDistanceMode.Standard;
    }

    private static void ConsecutiveJobsEarnTheirOwnAllowance()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var chooser = ctx.Companion.Brain.Chooser;
        chooser.Actions.Clear();
        var activity = new ActivityProbe { Target = ctx.Player.Bottom };
        chooser.Actions.Add(activity);
        Require(chooser.Choose(ctx) == activity, "first job must win");
        activity.Identity = new object();
        Require(chooser.Choose(ctx) == activity, "next job must remain in the same behaviour");
        activity.Target = ctx.Player.Bottom + new Vector2(Preferences.Current.NewActivityRadius + 100, 0);
        Require(activity.Allows(ctx), "a new job selected by the incumbent must earn its own continuation allowance");
    }

    private static void StallsSurviveBehaviourChanges()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var brain = ctx.Companion.Brain;
        var request = brain.GetType().GetProperty("LastRequest")!;
        var watch = brain.GetType().GetMethod("WatchProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int window = live::AICompanion.Companion.Brain.BehaviourSelection.Weights.ObjectiveProgressWindowTicks;
        Vector2 origin = ctx.Npc.Bottom;
        ctx.Player.Bottom = origin + new Vector2(500, 0);
        brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        void Tick(RequestKind kind, float offset = 0)
        {
            ctx.Npc.Bottom = origin + new Vector2(offset, 0);
            request.SetValue(brain, new live::AICompanion.Companion.Brain.PositionSelection.PositionRequest(kind, ctx.Player.Bottom));
            watch.Invoke(brain, new object[] { ctx.Companion });
        }
        for (int i = 0; i < window; i++) Tick(RequestKind.WithPlayer);
        Require(brain.MovementStalled, "a stationary unsatisfied follow request must visibly stall even if the motor holds");
        Tick(RequestKind.Hold);
        Require(!brain.MovementStalled, "an intentional settled hold must not be labelled stuck");
        for (int i = 0; i < window; i++)
        {
            brain.Chooser.Activity.Select(i % 2 == 0
                ? new live::AICompanion.Companion.Brain.PurposeFamilies.Combat.PursueAttackOpportunity()
                : new live::AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance.KeepCompany(), ctx);
            Tick(i % 2 == 0 ? RequestKind.WithPlayer : RequestKind.Guard, i % 8 - 4);
        }
        Require(brain.MovementStalled, "local oscillation and behaviour churn must not reset continuing non-progress");
        Tick(RequestKind.Hold);
        var route = new live::AICompanion.Companion.Brain.SharedMovementSystem.NavPath(new(), new Point(50, 60));
        brain.Navigator.GetType().GetProperty("Path")!.SetValue(brain.Navigator, route);
        for (int i = 0; i < window; i++)
        {
            if (i == window / 2) route.Index++;
            Tick(RequestKind.WithPlayer);
        }
        Require(!brain.MovementStalled, "advancing the retained route must count as progress even when the route returns near its start");
    }

    private static void RemoteJobReleasesAndDiscoversNearbyOre()
    {
        var (mine, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "initial vein not found");
        mine.AdmitActivity();
        int oldJob = mine.JobId;
        ctx.Npc.Bottom = new Vector2(88 * 16, 60 * 16);
        // Place the next vein beyond the old job's continuation envelope from the new player.
        ctx.Player.Bottom = new Vector2(2000, 60 * 16);
        Tile ore = Main.tile[84, 59]; ore.HasTile = true; ore.TileType = TileID.Copper;
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        for (int i = 0; i < 61; i++) VerifyPreparedActivities.PrepareAndScore(mine, ctx);
        Require(mine.JobId != oldJob && mine.TargetTile == new Point(84, 59), "an obsolete retained vein must not prevent discovering reachable local ore");
    }

    private static void ApproximateArrivalMustContinueApproaching()
    {
        Point ore = new(25, 59);
        var (mine, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, ore);
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "initial vein not found");
        // The recorded symptom was Mine.Execute returning Hold while its own reach test failed.
        // Supply a legitimate in-reach stand and an actual pose 19px short of the reach boundary.
        Vector2 stand = new(ore.X * 16 + 8 - (Player.tileRangeX * 16 + 8) + 1, 60 * 16);
        ctx.Npc.Bottom = stand - new Vector2(19, 0);
        Require(!FindToolAccess.InReach(ctx.Npc.Bottom, ore) && FindToolAccess.InReach(stand, ore), "fixture must straddle the actual mining reach boundary");
        typeof(live::AICompanion.Companion.Brain.PurposeFamilies.Gathering.MineOre).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(mine, new OreFinder.OreTarget(ore, TileID.Copper, stand));
        Require(mine.Execute(ctx).Kind == RequestKind.Exact, "a non-swingable approximate arrival must keep approaching instead of holding");
    }

    private static void RetainedMiningRespectsTheCompanionsRange()
    {
        var (mine, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "fixture must discover a mining job");
        mine.AdmitActivity();
        ctx.Npc.Bottom = ctx.Player.Bottom + new Vector2(Preferences.Current.ActiveActivityRadius + 1, 0);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player, ctx.Companion.Breath);
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) == 0 && mine.RemainingTiles == 0,
            "retained ore near the player must not keep a companion outside the active range in mining mode");
    }

    private static void BedsProtectTheRoomAndItsBoundary()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        for (int x = 35; x <= 55; x++) for (int y = 45; y <= 60; y++)
        {
            Tile t = Main.tile[x, y]; t.ClearEverything();
            if (x == 35 || x == 55 || y == 45 || y == 60) { t.HasTile = true; t.TileType = TileID.WoodBlock; }
        }
        Main.tileSolid[TileID.WoodBlock] = true;
        for (int x = 0; x < 4; x++) for (int y = 0; y < 2; y++)
        {
            Tile t = Main.tile[40 + x, 58 + y]; t.HasTile = true; t.TileType = TileID.Beds; t.TileFrameX = (short)(x * 18); t.TileFrameY = (short)(y * 18);
        }
        Protection.Reset(); Protection.Refresh(ctx.Player.Bottom, ctx.Npc.Bottom);
        Require(Protection.IsProtected(new Point(50, 50)), "unlit bed room interior must be protected");
        Require(Protection.IsProtected(new Point(35, 52)), "the home wall must be protected as well as its empty interior");
        Require(!Protection.IsProtected(new Point(20, 59)), "temporary platforms/wood without a bed must not protect a cave");
        Require(!Torches.Candidate(new Point(50, 50)), "torch discovery must reject a protected dim home");
        var axe = new Item(); axe.SetDefaults(ItemID.CopperAxe);
        Require(!ctx.Companion.Chopper.Swing(new Point(35, 52), axe), "the final axe mutation gate must protect the home");
        var pick = new Item(); pick.SetDefaults(ItemID.CopperPickaxe);
        Require(!ctx.Companion.Miner.Swing(new Point(35, 52), pick), "the final pick mutation gate must protect the home");
        Protection.Reset();
    }

    private static void AmbientFallbackDoesNotInventSamples()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        Vector2 position = Main.screenPosition;
        int width = Main.screenWidth, height = Main.screenHeight;
        try
        {
            var light = new live::AICompanion.Companion.Brain.WorldObservation.LightSense();
            Require(light.AmbientReadTick == null && light.AmbientSamples == 0,
                "an uninitialised light observer must carry no invented reading time or samples");
            Main.screenPosition = new Vector2(100000, 100000);
            Main.screenWidth = 800; Main.screenHeight = 600;
            light.Update(ctx.Npc, ctx.Player);
            Require(light.Ambient == 0 && light.AmbientSamples == 0 && light.AmbientReadTick == Main.GameUpdateCount,
                "off-screen held-light fallback must remain distinguishable from sampled darkness");
            Main.screenPosition = ctx.Npc.Center - new Vector2(400, 300);
            for (int i = 0; i < 20; i++) light.Update(ctx.Npc, ctx.Player);
            Require(light.AmbientSamples > 0,
                "an in-world clipped window must retain its actual brightness read count");
        }
        finally { Main.screenPosition = position; Main.screenWidth = width; Main.screenHeight = height; }
    }

    private static void TorchSupplyIsDebitedOnlyAfterPlacement()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        Item bagTorch = new(); bagTorch.SetDefaults(ItemID.Torch); bagTorch.stack = 2;
        Item playerTorch = new(); playerTorch.SetDefaults(ItemID.Torch); playerTorch.stack = 3;
        Item[] bag = { bagTorch };
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        ctx.Player.inventory[0] = playerTorch;
        Require(!Torches.Place(new Point(10, 30), bag, ctx.Player, out _) && bagTorch.stack == 2 && playerTorch.stack == 3,
            "unsupported placement must consume no torch");
        Require(Torches.Place(new Point(10, 59), bag, ctx.Player, out string source) && source == "companion bag" && bagTorch.stack == 1 && playerTorch.stack == 3,
            "native floor placement must consume exactly one companion torch first");
        bagTorch.TurnToAir();
        Require(!Torches.Place(new Point(12, 59), bag, ctx.Player, out _) && playerTorch.stack == 3,
            "Smart Cursor must reject a second torch within eight tiles without consuming it");
        Require(Torches.Place(new Point(30, 59), bag, ctx.Player, out source) && source == "player inventory" && playerTorch.stack == 2,
            "empty companion bag must use exactly one player torch");
        Require(!Torches.Place(new Point(30, 59), bag, ctx.Player, out _) && playerTorch.stack == 2,
            "already occupied placement must not consume another torch");
    }

    private static void DoorsKeepTheWholeBedroomProtected()
    {
        foreach (ushort door in new[] { TileID.ClosedDoor, TileID.OpenDoor })
        {
            var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
            for (int x = 35; x <= 70; x++) for (int y = 45; y <= 60; y++)
            {
                Tile t = Main.tile[x, y]; t.ClearEverything();
                if (x == 35 || x == 70 || y == 45 || y == 60) { t.HasTile = true; t.TileType = TileID.Stone; }
            }
            for (int y = 57; y <= 59; y++) { Tile t = Main.tile[70, y]; t.TileType = door; }
            for (int x = 0; x < 4; x++) for (int y = 0; y < 2; y++)
            {
                Tile t = Main.tile[40 + x, 58 + y]; t.HasTile = true; t.TileType = TileID.Beds;
                t.TileFrameX = (short)(x * 18); t.TileFrameY = (short)(y * 18);
            }
            Protection.Reset(); Protection.Refresh(ctx.Player.Bottom, ctx.Npc.Bottom);
            Require(Protection.IsProtected(new Point(65, 50)), $"door {door} must preserve protection beyond the small bed fallback");
            Require(!Protection.IsProtected(new Point(74, 50)), "the room must not consume the neighbouring cave");
        }
        Protection.Reset();
    }

    private static void TorchRecommendationsPreserveThePlayersCursor()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var targets = typeof(Terraria.GameContent.SmartCursorHelper).GetField("_targets", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? original = targets.GetValue(null);
        var sentinel = new List<Tuple<int, int>> { Tuple.Create(71, 42) };
        int x = Player.tileTargetX, y = Player.tileTargetY;
        try
        {
            targets.SetValue(null, sentinel); Player.tileTargetX = 71; Player.tileTargetY = 42;
            Item torch = new(); torch.SetDefaults(ItemID.Torch);
            Require(live::AICompanion.Companion.Brain.WorldInteractions.Torch.RecommendTorchPlacement.Accepts(new Point(10, 59), torch, ctx.Player),
                "fixture must exercise a native recommended torch site");
            Require(ReferenceEquals(targets.GetValue(null), sentinel) && sentinel.Count == 1 && sentinel[0].Equals(Tuple.Create(71, 42)),
                "companion recommendation must restore the player's exact Smart Cursor scratch state");
            Require(Player.tileTargetX == 71 && Player.tileTargetY == 42, "companion recommendation must not move the player's tile cursor");
        }
        finally { targets.SetValue(null, original); Player.tileTargetX = x; Player.tileTargetY = y; }
    }

    private static void InteractionJumpsRequireClearanceAndSafeLanding()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        // The tiny world's row 60 is space under native gravity. Use a lower floor so this
        // fixture tests a cave jump, not a deliberately unbounded low-gravity flight.
        Main.worldSurface = 60;
        for (int x = 5; x < 95; x++) { Tile t = Main.tile[x, 90]; t.HasTile = true; t.TileType = TileID.Dirt; }
        var start = new live::AICompanion.Companion.Brain.SharedMovementSystem.BodyState(320, 1440, 0, 0, true);
        bool Reach(live::AICompanion.Companion.Brain.SharedMovementSystem.BodyState body) => body.Bottom < start.Bottom - 48;
        Require(live::AICompanion.Companion.Brain.SharedMovementSystem.ProveInteractionJump.CanReach(
            live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World, start, Reach), "clear ground jump must reach an elevated interaction and return safely");
        for (int x = 19; x <= 23; x++) { Tile t = Main.tile[x, 86]; t.HasTile = true; t.TileType = TileID.Stone; }
        Require(!live::AICompanion.Companion.Brain.SharedMovementSystem.ProveInteractionJump.CanReach(
            live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World, start, Reach), "low ceiling must refuse an interaction jump that cannot reach its target");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
