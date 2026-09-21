extern alias live;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Policy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using Protection = live::AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes;
using Torches = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.PlaceTorches;
using OreFinder = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.OreFinder;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;

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
            AnEnemyBesideTheBodyNeitherSuspendsTheJobNorTakesTheFeet();
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
            TorchPlacementNeverUsesUpATorch();
            AmbientFallbackDoesNotInventSamples();
            AFamilyAllowanceDefersSiblingsFairly();
            TorchRecommendationsPreserveThePlayersCursor();
            Console.WriteLine("companion activities: resource/follow competition, actor-specific danger charged once, remote job release, actual swing reach, bed protection and native torch inventory contracts pass");
            return 0;
        }
        finally { Preferences.Current = saved; Protection.Reset(); }
    }

    private static void WorkWinsOutsideFollowComfort()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
        // Driven through the real tick, which is what the course brain is: the row's subject is a
        // behaviour README describes — reachable ore beats ordinary following once the player is far
        // enough that following is not free — so it is worth more as a claim about the brain that runs
        // than as a claim about the scorer that decided it before `0bb2c8a`. Three ticks, because the
        // first frames a companion lives are spent flooding reach and optional work refuses an
        // unanswered search rather than walking at it.
        for (int i = 0; i < 3; i++) VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        string? chosen = ctx.Companion.Brain.Chooser.Current?.Name;
        Require(chosen == "mine", $"reachable ore at 480px separation must beat ordinary following; got {chosen ?? "none"}");
    }

    private sealed class ActivityProbe : live::AICompanion.Companion.Brain.Activities.CompanionAction
    {
        public object Identity = new();
        public Vector2 Target;
        public float Value = 1;
        public int Entries;
        public int Exits;
        public int Preparations;
        public Action? DuringPreparation;
        public live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily Purpose
            = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.NearbyAssistance;
        public bool Excursion = true;
        public override string Name => "probe";
        public override bool IsExcursion => Excursion;
        public override live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily Family
            => Purpose;
        public override Vector2? ActivityTarget => Target;
        public override object ActivityIdentity => Identity;
        public bool Allows(live::AICompanion.Companion.Brain.Activities.ActionContext ctx) => AllowsTarget(ctx, Target, Identity);
        private float preparedValue;
        public override void Prepare(in live::AICompanion.Companion.Brain.Activities.ActionContext ctx)
        {
            Preparations++;
            DuringPreparation?.Invoke();
            bool allowed = Allows(ctx);
            preparedValue = allowed ? Value : 0f;
            Classify(allowed ? live::AICompanion.Companion.Brain.Activities.OfferEligibility.Usable
                : live::AICompanion.Companion.Brain.Activities.OfferEligibility.PolicyForbidden, allowed ? "probe" : "outside-activity-allowance");
        }
        public override float Score() => preparedValue;
        public override void Enter(in live::AICompanion.Companion.Brain.Activities.ActionContext ctx) => Entries++;
        public override void Exit(in live::AICompanion.Companion.Brain.Activities.ActionContext ctx) { Exits++; base.Exit(ctx); }
        public override live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest Execute(in live::AICompanion.Companion.Brain.Activities.ActionContext ctx)
            => live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest.Hold;
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
        var gathering = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.Gathering;
        var deferredOffer = live::AICompanion.Companion.Brain.Activities.OfferEligibility.Deferred;
        var first = new ActivityProbe { Target = ctx.Player.Bottom, Value = .4f, Purpose = gathering };
        var second = new ActivityProbe { Target = ctx.Player.Bottom, Value = .9f, Purpose = gathering };
        var combat = new ActivityProbe { Target = ctx.Player.Bottom, Value = .95f, Purpose = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.Combat };
        var company = new ActivityProbe { Target = ctx.Player.Bottom, Value = .1f, Excursion = false };
        chooser.Actions.Clear();
        chooser.Actions.AddRange(new live::AICompanion.Companion.Brain.Activities.CompanionAction[] { first, second, combat, company });
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
        chooser.Actions.AddRange(new live::AICompanion.Companion.Brain.Activities.CompanionAction[] { left, right, company });
        chooser.Choose(ctx);
        Require(left.Preparations + right.Preparations == 1, "the counter-case must defer one sibling under a zero share");
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            chooser.Choose(ctx);
            Require(left.Preparations + right.Preparations == 3 && chooser.Queries.LastFamilies.All(f => f.Deferred == 0),
                "with wall-clock allowances lifted no sibling may be deferred");
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
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
            if (invalidation != "identity") first.Purpose = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily.Combat;
            if (invalidation == "all") second.Identity = new Item { active = false, stack = 0 };
            second.DuringPreparation = () =>
            {
                if (first.Identity is NPC npc)
                {
                    if (invalidation == "generation") live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Spawn(npc);
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
        // The work radius is measured to the player's intent region, so a fixture that moves the player
        // directly must let the sense see the move or it is measuring against where he used to be. A
        // still player carries no lead, so a refreshed region sits exactly on his feet and the
        // distances below mean what they meant when they were written.
        void SeeThePlayer() => ctx.Senses.Update(ctx.Npc, ctx.Player);
        SeeThePlayer();
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
        SeeThePlayer();
        activity.Target = ctx.Player.Bottom + new Vector2(Preferences.Current.NewActivityRadius + 100, 0);
        ctx.Companion.Brain.Chooser.RecordWork(activity.Target);
        var loot = new live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems();
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
            var collect = new live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems();
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
            var collect = new live::AICompanion.Companion.Brain.Activities.NearbyAssistance.CollectNearbyItems();
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

    /// <summary>
    /// Safety rides on the job. A heavy hitter beside the body used to start combat spacing, which suspended the ordinary activity
    /// and searched for a low-exposure cell; since 15 September 2026 only downing and recovery flight take the body, so the same scene
    /// must leave the probe activity executing on every tick with the hands granted. The zombie stands still and is never on a collision course, so the evade step has nothing to bend
    /// here; the evade rows in VerifySafetyIsALayerOnTheJob are where a bent tick is asserted.
    /// </summary>
    private static void AnEnemyBesideTheBodyNeitherSuspendsTheJobNorTakesTheFeet()
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
            int suspendedTicks = 0, handsWithheld = 0;
            for (int tick = 0; tick < 300; tick++)
            {
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
                if (!emptyOffers && chooser.Activity.Phase == live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityPhase.Suspended) suspendedTicks++;
                if (ctx.Companion.Brain.ControlGrants.Last!.Value.Hand != live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant.Available) handsWithheld++;
                VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
            }
            Require(suspendedTicks == 0 && handsWithheld == 0,
                $"an enemy beside the body must neither suspend the job nor withhold the hands; emptyOffers={emptyOffers}, suspended ticks={suspendedTicks}, hands withheld={handsWithheld}");
            Require(!new live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser().Actions.Any(a => a.Name == "kite"),
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
        Require(playerOnly.Senses.Threats.PlayerDanger > 0 && playerOnly.Senses.Threats.CompanionDanger == 0,
            "player-only danger must read as the player's alone, not the companion's own");
    }

    /// <summary>
    /// Four matched scenes on one ore floor differ only in whom an added hostile threatens: nobody, the
    /// player, the companion or both. Threat, protection, reunion and chooser run in production order. Every
    /// excursion has a real opportunity in every scene — ore, a tree, a dropped item, a measured dark area
    /// with a torch supply, and an attackable zombie between the two actors — so each raw value has something
    /// to read danger into. The zombie is also a mild threat to both actors, identical in every scene, so the
    /// premise is relational: each added threat raises exactly the danger of the actor it names and leaves
    /// the other's unchanged. No excursion's raw value may move between the
    /// scenes that differ only in the player's danger, because the player's need for help reaches optional
    /// work once, through the shared protection factor. Combat's one offer reads the player's danger through
    /// its danger lift and the companion's through its stands' exposure, so a threat to the companion alone
    /// never raises the fight while a threat to the player lifts it net; a threat to the companion alone
    /// must change neither protection nor the reunion charge. The old hunt side's global yield to the
    /// companion's danger is gone with the split: avoiding a hit is a bend in whatever the body is already
    /// doing, owned by the safety layer rather than the offer.
    /// </summary>
    private static void DangerIsChargedOnceToTheActorItThreatens()
    {
        var scenes = new (string Name, bool Player, bool Companion)[]
            { ("neither", false, false), ("player", true, false), ("companion", false, true), ("both", true, true) };
        // `Protection` and `Reunion` are the family chooser's per-activity multipliers, and this row is
        // the last one still driving that scorer — see the comment at its loop for the measurement that
        // sent an attempted course rewrite back. When it is adjudicated under AIC-437, those two columns
        // have no course equivalent to move to: the course prices whole orders and charges danger once
        // through `CourseComparisonEpisode.RelevanceFor`, whose own row is
        // `VerifyEncounterContext.TheCourseChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork` and
        // which already holds the property the protection assertion below holds here — urgency and an
        // equal encounter are one danger read twice, paid once rather than squared.
        // `Protection` and `Reunion` left this tuple with the family chooser, which is what this row used
        // to drive. They were two of that scorer's per-activity multipliers and the course has no
        // per-activity factor to read: it prices whole orders and charges danger once through
        // `CourseComparisonEpisode.RelevanceFor`, whose own row is
        // `VerifyEncounterContext.TheCourseChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork` and which
        // holds exactly the property the protection assertion held here — urgency and an equal encounter
        // are one danger read twice, paid once rather than squared. Keeping a copy against a multiplier
        // nothing computes would be an assertion that passes on two zeroes.
        var seen = new Dictionary<string, (float Raw, float DelayCost, float Guard, float PlayerDanger, float CompanionDanger)>();
        // Whether the course priced an order led by this work at all, which is its own word for what the
        // chooser expressed as a positive raw. Course nominals are signed net values rather than 0..1
        // utilities — lighting led at -0.086 in a calm scene — so `raw > 0` is a premise carried over from
        // a scorer that no longer runs, and asserting it would fail on a perfectly healthy opportunity.
        var priced = new Dictionary<string, Dictionary<string, bool>>();
        var excursions = new Dictionary<string, Dictionary<string, float>>();
        var offers = new Dictionary<string, string>();
        var combatOffers = new Dictionary<string, string>();
        bool torchPlacement = Preferences.Current.TorchPlacement;
        var lightMode = Lighting.Mode;
        float brightness = Lighting.GlobalBrightness;
        Item previousItem = Main.item[5];
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            Preferences.Current.TorchPlacement = true;
            foreach (var scene in scenes)
            {
                var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
                VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (_, _) => .05f);
                ctx.Player.Bottom = new Vector2(50 * 16, 60 * 16);
                Tile trunk = Main.tile[30, 59];
                trunk.ClearEverything();
                trunk.HasTile = true;
                trunk.TileType = TileID.Trees;
                Main.tileAxe[TileID.Trees] = true;
                Main.tileSolid[TileID.Trees] = false;
                // A wall between the companion's stand and the hunt target, so the hunt is not a
                // shot from here: a local hunt pays no approach and reads its own danger at one by
                // design, which is what the yield below turns on. No lob from the muzzle clears
                // thirty tiles, and the flood still completes over the top, so the verdict is a
                // reposition rather than an absence. Constant in every scene, so each relational
                // assertion still compares scenes that differ only in whom the added hostile threatens.
                for (int y = 30; y < 60; y++)
                {
                    Tile wall = Main.tile[28, y];
                    wall.ClearEverything();
                    wall.HasTile = true;
                    wall.TileType = TileID.Dirt;
                }
                live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
                for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
                Item torches = new();
                torches.SetDefaults(ItemID.Torch);
                torches.stack = 5;
                ctx.Player.inventory[0] = torches;
                ctx.Player.selectedItem = 1;
                Item drop = new();
                drop.SetDefaults(ItemID.CopperOre);
                drop.active = true;
                drop.whoAmI = 5;
                drop.Bottom = new Vector2(22 * 16, 60 * 16);
                Main.item[5] = drop;
                for (int i = 30; i <= 32; i++) Main.npc[i] = new NPC();
                void Hostile(int slot, Vector2 feet, bool attackable)
                {
                    NPC npc = Main.npc[slot];
                    npc.SetDefaults(NPCID.Zombie);
                    npc.whoAmI = slot; npc.active = true; npc.Bottom = feet;
                    if (!attackable) { npc.damage = 100; npc.dontTakeDamage = true; }
                }
                // The hunt target. A straight floor has no spot far enough from both actors to leave it outside
                // either arrival window, so it is a mild threat to both, and the same one in every scene.
                Hostile(32, new Vector2(36 * 16, 60 * 16), attackable: true);
                if (scene.Player) Hostile(30, ctx.Player.Bottom - new Vector2(64, 0), attackable: false);
                if (scene.Companion) Hostile(31, ctx.Npc.Bottom - new Vector2(64, 0), attackable: false);
                var brain = ctx.Companion.Brain;
                brain.Senses.Update(ctx.Npc, ctx.Player);
                brain.Senses.SetInterventionEstimate(ctx.Companion.Combat.EstimateInterventionTicks(ctx));
                // The light field must describe the frame this scene presented, not one measured before it.
                Require(brain.Senses.Light.MeasuredSamples > 0,
                    $"the light field must hold this scene's presented frame, or every lighting offer below is vacuous: {scene.Name}");
                // Prime the reach region to completion before comparing. The flood is bounded per advance and
                // grows across resolves, and work that reads it refuses an unfinished answer rather than
                // walking at it, so a single resolve would leave every reach-reading offer unresolved and the
                // comparison below would be measuring the flood's budget instead of the scene's danger.
                var primeHome = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
                    live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, ctx.Player.Bottom);
                // One resolve before the loop, unconditionally. SetUp settles the region and this
                // scene then rewrites its terrain and resets the record, so ReachComplete still reads
                // the settled flag while the flood it names is already invalid; without a refresh the
                // loop exits at once on the stale true, the first tick refloods, and the hand-rolled
                // loop below never grows the replacement the way the live tick would. The resolve
                // refloods first if it must, and the loop then grows the flood that actually answers.
                brain.Positioner.Resolve(primeHome, brain.Senses);
                for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
                    brain.Positioner.Resolve(primeHome, brain.Senses);
                Require(brain.Positioner.ReachComplete,
                    $"the reach region must settle before the comparison, or a refusal reads as an absence: {scene.Name}");
                var combatPreview = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies>().Single();
                int ticks = 0;
                // The plan search decides once its stands do, and the wall means the stands on the
                // companion's side never solve: the scene is ticked in production order — observe,
                // price intervention, compare — until the search decides, so the comparison reads the
                // hunt's answer rather than an unfinished search at value zero. The reach region was
                // primed above, so this is one pass when the shot past the wall solves immediately.
                do
                {
                    brain.Senses.Update(ctx.Npc, ctx.Player);
                    brain.Senses.SetInterventionEstimate(ctx.Companion.Combat.EstimateInterventionTicks(ctx));
                    // **Still the retired family chooser, deliberately, and this is the last row holding
                    // it alive.** Rewriting it against the course was attempted on 21 September 2026 and
                    // reverted on a measurement rather than on difficulty: driven through
                    // `Course.Decide`, with every activity prepared exactly as the live tick prepares
                    // them and up to eight consecutive decisions, the course led **no order at all** with
                    // mining, chopping or collection in this scene — `collect=0, chop=0, mine=0` — while
                    // each of those activities' own `Prepare` reported `Usable` (`vein-target-established`,
                    // `reachable-trunk`, `known-drop-fits-cargo`) and lighting alone priced an order, at
                    // −0.086. So the row's premise, that every excursion has a real opportunity in the
                    // calm scene, fails against the course for a reason that is about the course's
                    // discovery rather than about this row: four ticks of decisions do not produce the
                    // gathering orders the activities can see. `VerifyEncounterContext.MiningScene` does
                    // get a `mine-target` leader, so it is scene-specific and not a blanket absence.
                    //
                    // Adjudicating that belongs to AIC-437, one behaviour at a time, and is the last thing
                    // between AIC-419 and deleting `Choose`. Converting the premise to "the course priced
                    // something" would make the four-scene invariance below hold on four zeroes, which
                    // passes while testing nothing — the one outcome worse than the red.
                    // Prepare every activity, then ask the course — the live tick's decide phase without
                    // the motor, because this scene's premise is four terrains differing only in whom the
                    // added hostile threatens, and letting the body move would make them four geometries.
                    using (CombatFixture.BeginDecision())
                    {
                        foreach (var candidate in brain.Chooser.Actions) candidate.Prepare(ctx);
                        brain.Course.Decide(ctx, ctx.Companion.Combat, null,
                            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Current);
                    }
                    ticks++;
                }
                while ((combatPreview.EligibilityReason == "stands-undecided" || ticks < 6) && ticks < 500);
                Require(combatPreview.EligibilityReason != "stands-undecided",
                    $"the stand search must decide within five hundred ticks, or the scene's hunt is not the reposition it claims: {scene.Name}");
                // What each job is worth comes from the course, through the reader the recorder and the
                // inspector also use. The family chooser's score ledger is empty in anything the course
                // decides, so reading it here would have compared zero against zero on every scene — an
                // invariance that holds vacuously, which is worse than a red.
                var worths = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics
                    .ReadCourseWorthPerActivity.Of(brain).ToDictionary(w => w.Action.Name);
                var combat = brain.Chooser.Actions.OfType<live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies>().Single();
                seen[scene.Name] = (worths["mine"].Raw, brain.Chooser.Reunion.DelayCostPerTick, combat.Score(),
                    brain.Senses.Threats.PlayerDanger, brain.Senses.Threats.CompanionDanger);
                // Combat's offer legitimately reads the player's danger through its danger lift, so it is
                // not one of the excursion raws the invariance loop below holds fixed; it is read directly
                // instead, above. There is no hunt side any more: the stance is one plan, and the companion's
                // own danger bends the body through the safety layer rather than discounting the offer.
                excursions[scene.Name] = worths.Values.Where(w => w.Action.IsExcursion && w.Action.Name != "combat")
                    .ToDictionary(w => w.Action.Name, w => w.Raw);
                priced[scene.Name] = worths.Values.Where(w => w.Action.IsExcursion && w.Action.Name != "combat")
                    .ToDictionary(w => w.Action.Name, w => w.Priced);
                offers[scene.Name] = string.Join(",", worths.Values.Where(w => w.Action.IsExcursion)
                    .Select(w => $"{w.Action.Name}:{w.Offer}/{w.OfferReason}"));
                // Read directly, not through the excursion offers above: whether the winning stand
                // travels past the local-trip line is the planner's answer about this scene's
                // geometry, and a nearer winning stand must not read as a removed offer.
                combatOffers[scene.Name] = $"{combat.Eligibility}/{combat.EligibilityReason}";
            }
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
            Preferences.Current.TorchPlacement = torchPlacement;
            VerifyUsefulAssistance.ClearMeasuredLight();
            Lighting.Mode = lightMode;
            Lighting.GlobalBrightness = brightness;
            Main.item[5] = previousItem;
            for (int i = 30; i <= 32; i++) Main.npc[i] = new NPC();
        }
        string ledger = string.Join("; ", seen.Select(s => $"{s.Key}: raw={s.Value.Raw} delay={s.Value.DelayCost} guard={s.Value.Guard} playerDanger={s.Value.PlayerDanger} companionDanger={s.Value.CompanionDanger}"));
        string excursionLedger = string.Join("; ", excursions.Select(s => s.Key + ": " + string.Join(",", s.Value.Select(v => $"{v.Key}={v.Value:0.#####}"))))
            + " | offers " + string.Join("; ", offers.Select(o => $"{o.Key}: {o.Value}"));
        foreach (string name in new[] { "mine", "chop", "collect", "place-torches" })
            Require(priced["neither"].TryGetValue(name, out bool led) && led,
                $"every excursion needs a real opportunity in the calm scene, or its invariance to danger is vacuous: {name}; {excursionLedger}");
        // **A threat to the companion alone must not change what its work is worth; a threat to the player
        // may, and exactly once.** This is the chooser's "raw never reads the player's danger" restated
        // for a brain that has no raw-before-protection to hold fixed.
        //
        // The chooser kept the player's danger in a `Protection` column beside an untouched `Raw`, so the
        // row could hold the raw invariant across all four scenes. The course has no such split: a need's
        // worth is already multiplied by `CourseComparisonEpisode.RelevanceFor`, which is
        // `1 − max(protectionUrgency, encounterIntensity)` for non-combat work. Measured here, with the
        // threat on the player at urgency 0.907, collection went 0.909 → 0.084 and mining 0.895 → 0.081 —
        // a factor of 0.093, which is that relevance to three places. Charged once, in one place, which is
        // the property the old two-column arrangement existed to protect.
        //
        // So what survives as a matched-scene assertion is the companion half, which the course must leave
        // strictly alone, plus the pairing that proves the player half is charged once rather than
        // squared — and that pairing has its own row at
        // `VerifyEncounterContext.TheCourseChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork`, against
        // the arithmetic rather than against a scene, which is where it belongs.
        foreach (var (calm, threatened) in new[] { ("neither", "companion"), ("player", "both") })
            foreach (var (name, raw) in excursions[calm])
                Require(excursions[threatened][name] == raw,
                    $"{name}'s worth must not read the companion's own danger: {calm}={raw} against {threatened}={excursions[threatened][name]}; {excursionLedger}");
        foreach (var (name, calmRaw) in excursions["neither"])
            Require(excursions["player"][name] < calmRaw || calmRaw <= 0,
                $"{name}'s worth must fall when the player is threatened, because optional work pays his danger once; "
                + $"calm={calmRaw} threatened={excursions["player"][name]}; {excursionLedger}");
        foreach (var scene in scenes)
            Require(combatOffers[scene.Name] == "Usable/planned-attack",
                $"danger reprices the fight but never removes the offer; {scene.Name} offers combat:{combatOffers[scene.Name]}; {excursionLedger}");
        Console.WriteLine($"danger charged once: {excursionLedger}");
        var (neither, player, companion, both) = (seen["neither"], seen["player"], seen["companion"], seen["both"]);
        static bool Same(float a, float b) => MathF.Abs(a - b) < 1e-6f;
        Require(player.PlayerDanger > neither.PlayerDanger && both.PlayerDanger > companion.PlayerDanger
            && companion.CompanionDanger > neither.CompanionDanger && both.CompanionDanger > player.CompanionDanger
            && Same(companion.PlayerDanger, neither.PlayerDanger) && Same(both.PlayerDanger, player.PlayerDanger)
            && Same(player.CompanionDanger, neither.CompanionDanger) && Same(both.CompanionDanger, companion.CompanionDanger),
            $"each added threat must raise exactly the danger of the actor it names and leave the other's unchanged, or the matrix tests nothing; {ledger}");
        // Mining's own copy of the rule above: untouched by a threat to the companion, discounted once by
        // a threat to the player. It read "unchanged in all four scenes" while the chooser kept the
        // player's danger in a separate column; the course folds it into the value, so the invariance is
        // the companion half and the player half is a fall rather than a hold.
        Require(neither.Raw > 0 && Same(companion.Raw, neither.Raw) && Same(both.Raw, player.Raw),
            $"mining's worth must not read the companion's own danger; {ledger}");
        Require(player.Raw < neither.Raw,
            $"mining's worth must fall when the player is threatened, because optional work pays his danger once; {ledger}");
        // The protection assertion went with the multiplier it read; its property is the course's
        // `RelevanceFor`, held by
        // `VerifyEncounterContext.TheCourseChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork`.
        Require(seen.Values.All(v => v.DelayCost == neither.DelayCost),
            $"no threat to either actor may change the separation charge while reunion evidence is unchanged; {ledger}");
        Require(neither.Guard > 0 && companion.Guard <= neither.Guard && player.Guard > neither.Guard,
            $"a threat to the companion alone must never raise the fight's offer, while a threat to the player must lift it net of the exposure it adds; {ledger}");
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
            // Being with the player is being inside his region, with no rest to stand out first, so one observation is the
            // whole premise. Restated on 15 September 2026: the row used to stand the body still for a rescore, because
            // following then read as satisfied only after a settled streak.
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
            // The observation, not a comparison. This called `Choose` when the family chooser owned the
            // tick and regroup urgency was computed inside it; the observation moved to
            // `ObserveCompanionship`, which the live tick calls directly, so the row drives the thing it
            // is actually about rather than a decision procedure that no longer runs.
            ctx.Companion.Brain.Chooser.ObserveCompanionship(ctx);
            Require(ctx.Companion.Brain.Chooser.RegroupUrgency == 0,
                $"{mode} comfortable following must not request regrouping; inside={ctx.Companion.Brain.Senses.Intent.Inside} gap={ctx.Companion.Brain.Senses.Intent.Region.GapBeyond(ctx.Npc.Center)}");
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
        // The subject is `CompanionAction.Allows` — that a job the incumbent picks up next earns its own
        // continuation allowance rather than inheriting the previous job's. Making the probe the incumbent
        // used to mean calling the family chooser twice and checking it won; the course owns selection
        // now, so the row selects it through `OwnCurrentActivity`, which is the same owner the live tick
        // hands the course's chosen activity to and is deliberately not part of the retired scorer.
        activity.Prepare(ctx);
        chooser.Activity.Select(activity, ctx);
        Require(ReferenceEquals(chooser.Current, activity), "premise: the probe is the incumbent");
        activity.Identity = new object();
        activity.Prepare(ctx);
        chooser.Activity.Select(activity, ctx);
        Require(ReferenceEquals(chooser.Current, activity), "next job must remain in the same behaviour");
        activity.Target = ctx.Player.Bottom + new Vector2(Preferences.Current.NewActivityRadius + 100, 0);
        Require(activity.Allows(ctx), "a new job selected by the incumbent must earn its own continuation allowance");
    }

    private static void StallsSurviveBehaviourChanges()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, 59));
        var brain = ctx.Companion.Brain;
        var request = brain.GetType().GetProperty("LastRequest")!;
        var watch = brain.GetType().GetMethod("WatchProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int window = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ObjectiveProgressWindowTicks;
        Vector2 origin = ctx.Npc.Bottom;
        ctx.Player.Bottom = origin + new Vector2(500, 0);
        brain.Senses.Update(ctx.Npc, ctx.Player);
        void Tick(RequestKind kind, float offset = 0)
        {
            ctx.Npc.Bottom = origin + new Vector2(offset, 0);
            request.SetValue(brain, new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(kind, ctx.Player.Bottom));
            watch.Invoke(brain, new object[] { ctx.Companion });
        }
        for (int i = 0; i < window; i++) Tick(RequestKind.WithPlayer);
        Require(brain.MovementStalled, "a stationary unsatisfied follow request must visibly stall even if the motor holds");
        Tick(RequestKind.Hold);
        Require(!brain.MovementStalled, "an intentional settled hold must not be labelled stuck");
        for (int i = 0; i < window; i++)
        {
            brain.Chooser.Activity.Select(i % 2 == 0
                ? new live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies()
                : new live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany(), ctx);
            Tick(i % 2 == 0 ? RequestKind.WithPlayer : RequestKind.Exact, i % 8 - 4);
        }
        Require(brain.MovementStalled, "local oscillation and behaviour churn must not reset continuing non-progress");
        Tick(RequestKind.Hold);
        // A two-point route the row advances by hand, so the stall watcher sees a body making route progress
        // without the fixture having to fly one. Revision zero: this route is never validated against the world, it only has
        // to exist and have an index to move.
        var route = new live::AICompanion.Companion.Brain.Infrastructure.Movement.Route(
            new System.Collections.Generic.List<Vector2> { new(320f, 944f), new(800f, 944f), new(1200f, 944f) },
            0, 0);
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
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
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
        //
        // Both the stand and the body pose are centres, not feet. `FindToolAccess.InReach` names its first
        // parameter `centre` and every production caller passes `ctx.Npc.Center`; so does the stand the ore
        // target carries, which `MineOre` compares against `Center`. Read from `Bottom` the premise asked the
        // reach question about a point one radius below the one the activity uses, which on this geometry put
        // the body's nominal pose on the far side of the boundary the row exists to straddle — so the premise
        // failed and the row never reached the behaviour it is about.
        Vector2 stand = new(ore.X * 16 + 8 - (Player.tileRangeX * 16 + 8) + 1,
            60 * 16 - live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact.Radius);
        ctx.Npc.Center = stand - new Vector2(19, 0);
        Require(!FindToolAccess.InReach(ctx.Npc.Center, ore) && FindToolAccess.InReach(stand, ore),
            $"fixture must straddle the actual mining reach boundary; centre={ctx.Npc.Center} stand={stand} ore={ore}");
        typeof(live::AICompanion.Companion.Brain.Activities.Gathering.MineOre).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(mine, new OreFinder.OreTarget(ore, TileID.Copper, stand));
        Require(mine.Execute(ctx).Kind == RequestKind.Exact, "a non-swingable approximate arrival must keep approaching instead of holding");
    }

    private static void RetainedMiningRespectsTheCompanionsRange()
    {
        var (mine, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0, "fixture must discover a mining job");
        mine.AdmitActivity();
        ctx.Npc.Bottom = ctx.Player.Bottom + new Vector2(Preferences.Current.ActiveActivityRadius + 1, 0);
        ctx.Companion.Brain.Senses.Update(ctx.Npc, ctx.Player);
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
            var light = new live::AICompanion.Companion.Brain.Infrastructure.Observation.LightSense();
            Require(light.ReadTick == null && light.MeasuredSamples == 0,
                "an uninitialised light observer must carry no invented reading time or samples");
            Main.screenPosition = new Vector2(100000, 100000);
            Main.screenWidth = 800; Main.screenHeight = 600;
            light.Update(ctx.Npc, ctx.Player);
            // The field's own version of the same property: a window nobody could read holds no samples, and
            // its dark query says so rather than answering "no dark air", which is what a zero would mean.
            Require(light.MeasuredSamples == 0 && light.ReadTick == Main.GameUpdateCount,
                "off-screen held-light fallback must remain distinguishable from sampled darkness");
            Require(light.DarkAirNear(ctx.Npc.Center.ToTileCoordinates(), 12).Unmeasured,
                "an unread window must answer unmeasured, never a dark fraction of zero");
            Require(light.NearestDarkRegion(ctx.Npc.Center.ToTileCoordinates(), 200) == null,
                "an unread window must nominate no dark region");
            // What separates unmeasured from dark is the engine's own presented frame, not where the screen
            // happens to sit: the field asks the coverage question exactly, so a frame the engine is
            // presenting is read wherever the camera is, and no frame is read nowhere.
            Main.screenPosition = ctx.Npc.Center - new Vector2(400, 300);
            for (int i = 0; i < 20; i++) light.Update(ctx.Npc, ctx.Player);
            Require(light.MeasuredSamples == 0,
                "with no frame presented the field must measure nothing, wherever the camera sits");
            VerifyUsefulAssistance.WriteMeasuredLight(new Rectangle(0, 0, 100, 100), (_, _) => .05f);
            Main.screenPosition = new Vector2(100000, 100000);
            light.Update(ctx.Npc, ctx.Player);
            Require(light.MeasuredSamples > 0,
                "a presented frame must be read even with the camera elsewhere, because coverage is the question");
            Require(light.DarkAirNear(ctx.Npc.Center.ToTileCoordinates(), 12).DarkFraction > 0.9f,
                "a frame presented at .05 brightness must read as dark air, not as unmeasured");
        }
        finally { Main.screenPosition = position; Main.screenWidth = width; Main.screenHeight = height; }
    }

    /// <summary>
    /// The companion's torches are its own (the owner's ruling, 15 September 2026): no placement uses up a torch. A handed
    /// torch still decides the kind that goes in — the bag's first, then the player's — and with none anywhere it places an
    /// ordinary torch. The game's own spacing and an occupied tile still refuse.
    /// </summary>
    private static void TorchPlacementNeverUsesUpATorch()
    {
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 59));
        Item bagTorch = new(); bagTorch.SetDefaults(ItemID.PurpleTorch); bagTorch.stack = 2;
        Item playerTorch = new(); playerTorch.SetDefaults(ItemID.Torch); playerTorch.stack = 3;
        Item[] bag = { bagTorch };
        for (int i = 0; i < ctx.Player.inventory.Length; i++) ctx.Player.inventory[i] = new Item();
        ctx.Player.inventory[0] = playerTorch;
        Require(bagTorch.placeStyle != playerTorch.placeStyle, "premise: the bag's torch and the player's torch are different kinds");
        Require(!Torches.Place(new Point(10, 30), bag, ctx.Player, out _) && bagTorch.stack == 2 && playerTorch.stack == 3,
            "an unsupported placement places nothing and touches no torch");
        Require(Torches.Place(new Point(10, 59), bag, ctx.Player, out string source) && source == "companion bag's torch"
            && bagTorch.stack == 2 && playerTorch.stack == 3 && StyleAt(new Point(10, 59)) == bagTorch.placeStyle,
            $"a floor placement puts in the bag's kind of torch and uses none up; source={source} bag={bagTorch.stack} player={playerTorch.stack} style={StyleAt(new Point(10, 59))}");
        bagTorch.TurnToAir();
        Require(!Torches.Place(new Point(12, 59), bag, ctx.Player, out _),
            "Smart Cursor still refuses a second torch within its spacing");
        Require(Torches.Place(new Point(30, 59), bag, ctx.Player, out source) && source == "player's torch" && playerTorch.stack == 3
            && StyleAt(new Point(30, 59)) == playerTorch.placeStyle,
            $"with the bag empty it puts in the player's kind of torch and uses none of his; source={source} player={playerTorch.stack}");
        playerTorch.TurnToAir();
        Require(Torches.Place(new Point(50, 59), bag, ctx.Player, out source) && source == "its own torch",
            $"with no torch anywhere it still places a torch of its own; source={source}");
        Require(!Torches.Place(new Point(50, 59), bag, ctx.Player, out _),
            "an occupied tile takes no second torch");
    }

    /// <summary>A torch tile's style is its row in the torch sheet, 22 pixels a row.</summary>
    private static int StyleAt(Point tile) => Main.tile[tile.X, tile.Y].TileFrameY / 22;

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
            Require(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.RecommendTorchPlacement.Accepts(new Point(10, 59), torch, ctx.Player),
                "fixture must exercise a native recommended torch site");
            Require(ReferenceEquals(targets.GetValue(null), sentinel) && sentinel.Count == 1 && sentinel[0].Equals(Tuple.Create(71, 42)),
                "companion recommendation must restore the player's exact Smart Cursor scratch state");
            Require(Player.tileTargetX == 71 && Player.tileTargetY == 42, "companion recommendation must not move the player's tile cursor");
        }
        finally { targets.SetValue(null, original); Player.tileTargetX = x; Player.tileTargetY = y; }
    }

    // The interaction-jump row went with the jump. It proved that a ground jump toward an interaction above
    // the body cleared its ceiling and landed safely, and that a low ceiling refused it — a proof step that
    // existed because a walker's arc is ballistic and either fits or does not. The orb steers, so an
    // interaction above it is a hover the route search either reaches or does not, which is what
    // `VerifyAssistanceTrips`'s hover rows and the ceiling-ore row in `VerifyOreWork` assert instead.

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
