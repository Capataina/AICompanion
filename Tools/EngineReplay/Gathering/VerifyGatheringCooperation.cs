extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using FindToolAccess = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using MineOre = live::AICompanion.Companion.Brain.Activities.Gathering.MineOre;
using ChopTree = live::AICompanion.Companion.Brain.Activities.Gathering.ChopTree;
using TileChopper = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping.TileChopper;
using TileMiner = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Mining.TileMiner;
using WorkPolicies = live::AICompanion.Companion.Brain.Activities.WorkPolicies;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using AttemptStatus = live::AICompanion.Companion.Brain.Activities.AttemptStatus;
using AttemptAttribution = live::AICompanion.Companion.Brain.Activities.AttemptAttribution;
using AttemptOutcome = live::AICompanion.Companion.Brain.Activities.AttemptOutcome;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using HandGrant = live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using Protection = live::AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes;
using TileDamageWatcher = live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageWatcher;
using TileDamageClock = live::AICompanion.Companion.Brain.Infrastructure.Observation.TileDamageClock;

/// <summary>
/// P09's gathering cooperation and permission lifecycle against the whole brain and native tiles: chopping
/// from actual reach, the player taking or felling the companion's trunk, protection and policy changing after
/// the approach and during every tool phase, remaining work against a departing player, and the family choice
/// between an unusable ore and a usable tree. Every whole-brain run also diffs the terrain, so an edit to any
/// tile other than the work target fails it.
/// </summary>
internal static class VerifyGatheringCooperation
{
    public static int Run()
    {
        // Whole-brain phases here are caught by condition, and under the production millisecond allowances a search can stop
        // at its deadline and leave an approach undecided, so the phase a fixture catches would depend on machine load.
        // The allowances are lifted as the seal and brain-cost fixtures do; the undecided state is exercised directly.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try { return RunAll(); }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
    }

    private static int RunAll()
    {
        WorkPolicy mining = WorkPolicies.Mining, chopping = WorkPolicies.Chopping;
        int red = 0;
        void Each(string name, Action fixture)
        {
            try { fixture(); Console.WriteLine($"GREEN {name}"); }
            catch (Exception e)
            {
                red++;
                // An `InvalidOperationException` is this file's own `Require` and its message is the whole story.
                // Anything else is the instrument breaking, and a bare message for those is useless — a red
                // reading only "Object reference not set to an instance of an object" cost a separate isolation
                // run to locate. Those carry their type and where they were thrown.
                string detail = e is InvalidOperationException ? e.Message
                    : $"{e.GetType().Name}: {e.Message}{Environment.NewLine}{e.StackTrace}";
                AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"RED {name}: {detail}");
            }
            finally { WorkPolicies.Mining = mining; WorkPolicies.Chopping = chopping; Protection.Reset(); new TileDamageClock().OnWorldUnload(); }
        }
        Each("W01 chopping from actual reach", ChoppingFellsFromActualReachOnEitherSide);
        Each("W02 player takes the companion's trunk", APlayerTakingTheTrunkMovesTheCompanionAndConcludesByTrunk);
        Each("W03 externally felled trunk", AnExternallyFelledTrunkIsNotCompanionProduction);
        Each("W04 chop remaining work against departure", ChoppingRemainingWorkMeetsADepartingPlayer);
        Each("W05 protection after approach", ProtectionAddedAfterTheApproachStopsTheTool);
        Each("policy toggles during every phase", PolicyTogglesDuringEveryToolPhaseStopWorkAndResume);
        Each("D4 Mimic after Disabled reads no stale player hit", AMimicSwitchAfterDisabledReadsNoStalePlayerHit);
        Each("G01 unmineable ore beside a usable tree", AnUnmineableOreDoesNotMaskAUsableTree);
        Each("G02 retained vein against a new tree", ARetainedVeinIsComparedNotReserved);
        Each("G03 one trip unit", MiningAndChoppingPriceTravelInOneUnit);
        if (red == 0) Console.WriteLine("gathering cooperation: actual-reach chopping, trunk hand-over, external felling, departure, protection and policy changes in every phase, and family choice pass");
        return red;
    }

    /// <summary>A trunk with a two-tile block beside it on one side. From either start the whole brain must fell it, every
    /// strike must come from actual axe reach, and nothing but the tree may change. The companion's own fell is its own completion.</summary>
    private static void ChoppingFellsFromActualReachOnEitherSide()
    {
        foreach (bool mirrored in new[] { false, true })
        {
            Point trunk = new(25, 59);
            var ctx = SetUpTrees(trunk);
            VerifyOreWork.Place(new Point(mirrored ? 26 : 24, 58), TileID.Dirt);
            VerifyOreWork.Place(new Point(mirrored ? 26 : 24, 59), TileID.Dirt);
            ctx.Npc.Bottom = new Vector2((mirrored ? 36 : 14) * 16 + 8, 60 * 16);
            TerrainChanges.Reset();
            var before = Snapshot();
            var run = RunBrain(ctx, 900, () => !TileChopper.TreeStands(trunk));
            Require(!TileChopper.TreeStands(trunk) && run.ChopStrikes.Count > 0,
                $"mirrored={mirrored}: the whole brain must fell the trunk; strikes={run.ChopStrikes.Count} feet={ctx.Npc.Bottom} action={ctx.Companion.Brain.LastAction?.Name}");
            Require(run.ChopStrikes.All(feet => FindToolAccess.InReach(feet, trunk)),
                $"mirrored={mirrored}: every axe strike must come from actual reach; strikes at {string.Join("; ", run.ChopStrikes)}");
            RequireOnlyChanged(before, trunk);
            Advance(ctx, 10);
            Require(LastAttempt(ctx, "chop") is { Status: AttemptStatus.Complete, Attribution: AttemptAttribution.Companion },
                $"mirrored={mirrored}: a trunk the companion's own strike removed is its own completion; got {LastAttempt(ctx, "chop")}");
        }
    }

    /// <summary>
    /// The companion is chopping the nearer of two trunks when the player starts hitting that same trunk. Cooperation is a
    /// preference for separate trees, so the companion must move to the other trunk without another strike on the shared
    /// one, and the attempt it left must end partial and say the player took the trunk. When the player then fells the first
    /// trunk, that is nobody's completion for the companion; the second trunk, felled by the companion, is its own.
    /// </summary>
    private static void APlayerTakingTheTrunkMovesTheCompanionAndConcludesByTrunk()
    {
        Point near = new(24, 59), far = new(34, 59);
        var ctx = SetUpTrees(near, far);
        var clock = new TileDamageClock();
        clock.OnWorldLoad();
        var chop = ctx.Companion.Brain.Chooser.Actions.OfType<ChopTree>().Single();
        var run = RunBrain(ctx, 600, () => ctx.Companion.Chopper.LastOutcome is { Productive: true } outcome && outcome.Target == near);
        Require(run.ChopStrikes.Count > 0 && TileChopper.TreeStands(near),
            $"the hand-over fixture needs a productive strike on the nearer trunk first; strikes={run.ChopStrikes.Count} target={chop.ActivityTarget}");
        bool fail = true, effectOnly = false, noItem = false;
        new TileDamageWatcher().KillTile(near.X, near.Y, TileID.Trees, ref fail, ref effectOnly, ref noItem);
        long lastAttemptOnNear = ctx.Companion.Chopper.LastOutcome!.Value.Attempt;
        Advance(ctx, 60);
        Require(chop.ActivityTarget == far.ToWorldCoordinates(),
            $"a trunk the player took must move the companion to the separate trunk; target={chop.ActivityTarget}");
        Require(!(ctx.Companion.Chopper.LastOutcome is { } later && later.Target == near && later.Attempt != lastAttemptOnNear),
            "the companion must not strike the trunk the player is working after the player started on it");
        var left = ctx.Companion.Brain.Chooser.Activity.RecentAttempts.LastOrDefault(a => a.Activity == "chop" && a.ProductiveEffects > 0);
        Require(left is { Status: AttemptStatus.Partial, Cause: "player-took-trunk" },
            $"the attempt on the trunk the player took must end partial and say so; got {left}");
        Main.tile[near.X, near.Y].ClearEverything();
        var second = RunBrain(ctx, 900, () => !TileChopper.TreeStands(far));
        Require(!TileChopper.TreeStands(far) && second.ChopStrikes.Count > 0, $"the companion must fell its own trunk; strikes={second.ChopStrikes.Count}");
        Advance(ctx, 10);
        Require(LastAttempt(ctx, "chop") is { Status: AttemptStatus.Complete, Attribution: AttemptAttribution.Companion },
            $"the separate trunk the companion felled must be its own completion; got {LastAttempt(ctx, "chop")}");
        Require(!ctx.Companion.Brain.Chooser.Activity.RecentAttempts.Any(a => a.Activity == "chop" && a.Attribution == AttemptAttribution.Shared),
            "the trunk the player felled alone must not appear as shared companion production");
    }

    /// <summary>A trunk felled by someone else while the companion walks to it is an invalid attempt; the same fell after the
    /// companion's own strikes is a shared completion, never the companion's own.</summary>
    private static void AnExternallyFelledTrunkIsNotCompanionProduction()
    {
        {
            Point trunk = new(34, 59);
            var ctx = SetUpTrees(trunk);
            ctx.Npc.Bottom = new Vector2(14 * 16 + 8, 60 * 16);
            TerrainChanges.Reset();
            RunBrain(ctx, 300, () => ctx.Companion.Brain.LastAction?.Name == "chop" && ctx.Companion.Brain.Chooser.Activity.AttemptOpen
                && !FindToolAccess.InReach(ctx.Npc.Bottom, trunk));
            Require(ctx.Companion.Brain.LastAction?.Name == "chop" && ctx.Companion.Chopper.LastOutcome == null,
                $"the invalid case needs the companion walking to the trunk with no strike; action={ctx.Companion.Brain.LastAction?.Name}");
            Main.tile[trunk.X, trunk.Y].ClearEverything();
            Advance(ctx, 10);
            Require(LastAttempt(ctx, "chop") is { Status: AttemptStatus.Invalid, Cause: "trunk-gone-without-companion-effect", ProductiveEffects: 0 },
                $"a trunk felled by someone else before any companion strike is an invalid attempt; got {LastAttempt(ctx, "chop")}");
        }
        {
            Point trunk = new(24, 59);
            var ctx = SetUpTrees(trunk);
            var run = RunBrain(ctx, 300, () => ctx.Companion.Chopper.LastOutcome is { Productive: true });
            Require(run.ChopStrikes.Count > 0 && TileChopper.TreeStands(trunk), "the shared case needs a companion strike on a standing trunk");
            Main.tile[trunk.X, trunk.Y].ClearEverything();
            Advance(ctx, 10);
            Require(LastAttempt(ctx, "chop") is { Status: AttemptStatus.Complete, Attribution: AttemptAttribution.Shared },
                $"a trunk the player finished after companion strikes is a shared completion; got {LastAttempt(ctx, "chop")}");
        }
    }

    /// <summary>The mining departure pair, with a tree: held geometry and separation, a player sustaining travel away, and only
    /// the trunk's native remaining hits changed. A fresh trunk must yield to following and a one-hit trunk must be finished.</summary>
    private static void ChoppingRemainingWorkMeetsADepartingPlayer()
    {
        // The same restatement as VerifyOreWork's departure pair, and for the same reason: since 15 September 2026 a new job is
        // taken within 1000 px rather than 1120, so at 640 the trunk has already left the work radius and both arms read zero.
        // The gradient between a fresh trunk and a one-hit finish is asked where the trunk is still inside it.
        foreach (int separation in new[] { 400, 480, 576 })
        foreach (bool nearlyDone in new[] { false, true })
        {
            Point trunk = new(25, 89);
            var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, trunk);
            MakeTree(trunk);
            WorkPolicies.Chopping = WorkPolicy.Opportunistic;
            var brain = ctx.Companion.Brain;
            var workClock = new TileDamageClock();
            workClock.OnWorldLoad();
            brain.Chooser.Actions.RemoveAll(action => action.Name is not ("chop" or "keep-company"));
            ctx.Player.Bottom = ctx.Npc.Bottom + new Vector2(separation - 120 * 4, 0);
            for (int tick = 0; tick < 120; tick++)
            {
                ctx.Player.velocity = new Vector2(4, 0);
                ctx.Player.position += ctx.Player.velocity;
                VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
                brain.Senses.Update(ctx.Npc, ctx.Player);
                workClock.PostUpdateEverything();
            }
            Item axe = TileChopper.AxeFor(ctx.Player);
            if (nearlyDone)
                while (ctx.Companion.Chopper.EstimateRemaining(trunk, axe) is { Hits: > 1 })
                {
                    Require(ctx.Companion.Chopper.Swing(trunk, axe), "the chop departure fixture needs a native hit");
                    for (int tick = 0; tick < axe.useTime; tick++) ctx.Companion.Chopper.Tick();
                }
            var selected = brain.Chooser.Choose(ctx);
            Require(selected?.Name == (nearlyDone ? "chop" : "keep-company"),
                $"departure must distinguish a fresh trunk from a one-hit finish: separation={separation}; nearlyDone={nearlyDone}; selected={selected?.Name}; scores={string.Join(",", brain.Chooser.LastScores.Select(s => s.Action.Name + "=" + s.Final))}");
        }
    }

    /// <summary>
    /// A bed placed after the companion has committed, both while it walks to the work and after its first strike. Home
    /// protection is read at preparation and at the native mutation, so no further strike may land, the tool hand must be
    /// released, the attempt must not claim completion, and removing the bed must let the same work finish.
    /// </summary>
    private static void ProtectionAddedAfterTheApproachStopsTheTool()
    {
        foreach (bool chopping in new[] { false, true })
        foreach (bool afterStrike in new[] { false, true })
        {
            Point target = new(25, 59);
            var ctx = chopping ? SetUpTrees(target) : SetUpOre(target);
            string name = chopping ? "chop" : "mine";
            if (!afterStrike) ctx.Npc.Bottom = new Vector2(12 * 16 + 8, 60 * 16);
            TerrainChanges.Reset();
            string phase = $"tool={name} afterStrike={afterStrike}";
            RunBrain(ctx, 600, () => afterStrike ? LastToolOutcome(ctx, chopping) is { Productive: true }
                : ctx.Companion.Brain.LastAction?.Name == name && Vector2.Distance(ctx.Npc.Bottom, target.ToWorldCoordinates()) < 7 * 16
                    && !FindToolAccess.InReach(ctx.Npc.Bottom, target));
            Require(ctx.Companion.Brain.LastAction?.Name == name && TargetPresent(target, chopping)
                && (afterStrike ? LastToolOutcome(ctx, chopping) != null : LastToolOutcome(ctx, chopping) == null),
                $"{phase}: the fixture must catch the committed phase; action={ctx.Companion.Brain.LastAction?.Name}");
            var bed = PlaceBed(new Point(29, 58));
            long strikes = LastToolOutcome(ctx, chopping)?.Attempt ?? -1;
            bool handReleased = true;
            for (int tick = 0; tick < 120; tick++)
            {
                VerifyOreWork.AdvanceBrain(ctx);
                if (tick >= 1 && ctx.Companion.Brain.ControlGrants.Last?.Hand == HandGrant.WorkTool) handReleased = false;
            }
            Require(Protection.IsProtected(target), $"{phase}: the fixture bed must protect the work target");
            Require((LastToolOutcome(ctx, chopping)?.Attempt ?? -1) == strikes && TargetPresent(target, chopping),
                $"{phase}: protection added after the approach must stop every further native strike");
            Require(handReleased, $"{phase}: protection must release the tool hand rather than hold a work tool it may not use");
            var attempt = LastAttempt(ctx, name);
            Require(attempt is { Status: AttemptStatus.Partial or AttemptStatus.Invalid }
                && (attempt.Value.ProductiveEffects > 0 || attempt.Value.Status == AttemptStatus.Invalid),
                $"{phase}: the attempt protection ended must be partial with effects or invalid without them, never complete; got {attempt}");
            foreach (Point p in bed) { Main.tile[p.X, p.Y].ClearEverything(); TerrainChanges.Changed(p.X, p.Y); }
            RunBrain(ctx, 900, () => !TargetPresent(target, chopping));
            Require(!TargetPresent(target, chopping), $"{phase}: removing the protection must let the same work finish");
        }
        // The undecided approach, which the lifted allowances above never reach: mining does not
        // walk at ore its bounded search could not decide, so a bed placed over that ore has
        // nothing to invalidate. The undecided state comes from an unsettled reach region rather than
        // from a starved A* clock, as the unproven-approach fixture in the ore suite now does: mining's
        // approach asks the region instead of searching, so `AStar.MsBudget` cannot reach it and a row
        // still starving it would have gone on passing while testing nothing.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
        try
        {
            Point far = new(50, 59);
            var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, far);
            VerifyOreWork.EmptyTheReachRegion(ctx);
            Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) == 0f && mine.Status == "approach unknown",
                $"the undecided case must not be a plan; status={mine.Status} score={mine.Score()}");
            Require(mine.Execute(ctx).Kind == live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.Hold,
                "the undecided case must not walk at the ore");
            Protection.Reset();
            PlaceBed(new Point(52, 58));
            Protection.Refresh(ctx.Player.Bottom, ctx.Npc.Bottom);
            Require(Protection.IsProtected(far), "the fixture bed must protect the undecided ore");
            Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) == 0f, "a protected undecided ore must still not be offered");
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
            Protection.Reset();
        }
    }

    /// <summary>
    /// The work policy switched off, or from opportunistic to Mimic with no player contact, during each tool phase: walking to
    /// the work, the cooldown between two strikes, and a ceiling hop in flight. No strike may land while it is off, the hand
    /// must be released and the attempt must not claim completion; switching it back must let the same work finish.
    /// </summary>
    private static void PolicyTogglesDuringEveryToolPhaseStopWorkAndResume()
    {
        var cases = new (string Name, bool Chopping, string Phase, WorkPolicy Off)[]
        {
            ("mine walking", false, "walking", WorkPolicy.Disabled),
            ("mine cooldown", false, "cooldown", WorkPolicy.Disabled),
            ("mine cooldown mimic", false, "cooldown", WorkPolicy.Mimic),
            ("mine hop airborne", false, "airborne", WorkPolicy.Disabled),
            ("chop walking", true, "walking", WorkPolicy.Disabled),
            ("chop cooldown", true, "cooldown", WorkPolicy.Disabled),
            ("chop cooldown mimic", true, "cooldown", WorkPolicy.Mimic),
        };
        foreach (var c in cases)
        {
            Point target = c.Phase == "airborne" ? new Point(25, 52) : new Point(25, 59);
            ActionContext ctx;
            if (c.Phase == "airborne")
            {
                ctx = SetUpOre(new Point(40, 59));
                Main.tile[40, 59].ClearEverything();
                for (int x = 5; x < 45; x++) for (int y = 51; y <= 52; y++)
                    if (new Point(x, y) != target) VerifyOreWork.Place(new Point(x, y), TileID.Dirt);
                VerifyOreWork.Place(target, TileID.Copper);
            }
            else ctx = c.Chopping ? SetUpTrees(target) : SetUpOre(target);
            if (c.Phase == "walking") ctx.Npc.Bottom = new Vector2(12 * 16 + 8, 60 * 16);
            new TileDamageClock().OnWorldUnload();
            TerrainChanges.Reset();
            string name = c.Chopping ? "chop" : "mine";
            var before = Snapshot();
            RunBrain(ctx, 600, () => c.Phase switch
            {
                "walking" => ctx.Companion.Brain.LastAction?.Name == name && !FindToolAccess.InReach(ctx.Npc.Bottom, target)
                    && Vector2.Distance(ctx.Npc.Bottom, target.ToWorldCoordinates()) < 7 * 16,
                "cooldown" => LastToolOutcome(ctx, c.Chopping) is { Productive: true } && !(c.Chopping ? ctx.Companion.Chopper.Ready : ctx.Companion.Miner.Ready)
                    && TargetPresent(target, c.Chopping),
                // The walker's "in flight" phase was a jump; the orb's is simply being up off the floor line,
                // which is where a body that hovers does ceiling work from.
                _ => ctx.Companion.Brain.LastAction?.Name == "mine" && ctx.Npc.Center.Y < 60 * 16 - 24,
            });
            Require(ctx.Companion.Brain.LastAction?.Name == name && TargetPresent(target, c.Chopping),
                $"{c.Name}: the fixture must catch the {c.Phase} phase; action={ctx.Companion.Brain.LastAction?.Name} centre={ctx.Npc.Center} clear={ctx.Companion.Motor.ClearOfTerrain}");
            long strikes = LastToolOutcome(ctx, c.Chopping)?.Attempt ?? -1;
            if (c.Chopping) WorkPolicies.Chopping = c.Off; else WorkPolicies.Mining = c.Off;
            bool handReleased = true;
            for (int tick = 0; tick < 90; tick++)
            {
                VerifyOreWork.AdvanceBrain(ctx);
                if (tick >= 1 && ctx.Companion.Brain.ControlGrants.Last?.Hand == HandGrant.WorkTool) handReleased = false;
            }
            Require((LastToolOutcome(ctx, c.Chopping)?.Attempt ?? -1) == strikes && TargetPresent(target, c.Chopping),
                $"{c.Name}: no native strike may land while the policy is {c.Off}");
            Require(handReleased, $"{c.Name}: the tool hand must be released while the policy is {c.Off}");
            var attempt = LastAttempt(ctx, name);
            Require(attempt is { } ended && ended.Status != AttemptStatus.Complete,
                $"{c.Name}: the attempt the policy change ended must exist and must not be complete; got {attempt}");
            if (c.Chopping) WorkPolicies.Chopping = WorkPolicy.Opportunistic; else WorkPolicies.Mining = WorkPolicy.Opportunistic;
            RunBrain(ctx, 900, () => !TargetPresent(target, c.Chopping));
            Require(!TargetPresent(target, c.Chopping), $"{c.Name}: restoring the policy must let the same work finish");
            RequireOnlyChanged(before, target);
        }
    }

    /// <summary>
    /// The player hits a tree under Mimic, then chopping is disabled for longer than both the player-contact memory and the
    /// mimic job window, then Mimic is switched back on with no player contact. The offer must read as waiting for the
    /// player, because the player's axe contact ages under every policy; a contact frozen while chopping was disabled would
    /// read as a swing made a moment ago.
    /// </summary>
    private static void AMimicSwitchAfterDisabledReadsNoStalePlayerHit()
    {
        Point trunk = new(25, 59);
        var ctx = SetUpTrees(trunk);
        WorkPolicies.Chopping = WorkPolicy.Mimic;
        var workClock = new TileDamageClock();
        workClock.OnWorldLoad();
        var chop = ctx.Companion.Brain.Chooser.Actions.OfType<ChopTree>().Single();
        bool fail = true, effectOnly = false, noItem = false;
        new TileDamageWatcher().KillTile(trunk.X, trunk.Y, TileID.Trees, ref fail, ref effectOnly, ref noItem);
        VerifyOreWork.AdvanceBrain(ctx);
        workClock.PostUpdateEverything();
        chop.Prepare(ctx);
        Require(ctx.Companion.Brain.Senses.Player.IsChoppingTree, "premise: the player's axe contact must be observed under Mimic");
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        for (int tick = 0; tick < 300; tick++)
        {
            VerifyOreWork.AdvanceBrain(ctx);
            workClock.PostUpdateEverything();
        }
        Require(!ctx.Companion.Brain.Senses.Player.IsChoppingTree, "premise: the player's axe contact must have expired while chopping was disabled");
        WorkPolicies.Chopping = WorkPolicy.Mimic;
        chop.Prepare(ctx);
        Require(chop.Eligibility == OfferEligibility.PolicyForbidden && chop.EligibilityReason == "mimic-awaiting-player-tree-contact",
            $"Mimic switched on long after the player's last axe contact must wait for the player; got {chop.Eligibility}/{chop.EligibilityReason}");
    }

    /// <summary>Copper the fallback pick cannot damage beside the companion, and a usable tree farther away. The unmineable ore
    /// must be a known-unusable offer and the family must nominate the tree.</summary>
    private static void AnUnmineableOreDoesNotMaskAUsableTree()
    {
        // Whole brain rather than one comparison: a single comparison prepares one optional gathering child under the
        // family allowance, so it can defer mining or leave chopping's first search undecided without anything masking.
        Point ore = new(22, 59), trunk = new(31, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Chlorophyte, ore);
        MakeTree(trunk);
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        TerrainChanges.Reset();
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        bool mineRuledOut = false;
        var before = Snapshot();
        var run = RunBrain(ctx, 900, () =>
        {
            mineRuledOut |= mine.Eligibility == OfferEligibility.KnownUnusable;
            return !TileChopper.TreeStands(trunk);
        });
        Require(!TileChopper.TreeStands(trunk) && run.ChopStrikes.Count > 0 && run.MineStrikes.Count == 0 && Main.tile[ore.X, ore.Y].HasTile,
            $"an ore the pick cannot damage must not mask a usable tree; felled={!TileChopper.TreeStands(trunk)} chop strikes={run.ChopStrikes.Count} mine strikes={run.MineStrikes.Count} mine={mine.Eligibility}/{mine.EligibilityReason}");
        Require(mineRuledOut, $"the unmineable ore must be classified as known-unusable work while the tree was chosen; last={mine.Eligibility}/{mine.EligibilityReason}");
        RequireOnlyChanged(before, trunk);
    }

    /// <summary>
    /// A mining job is retained on a far vein when a usable tree appears beside the companion. Retaining a job must not
    /// reserve the family for it: the tree is prepared and carries its value on the same board while mining is the
    /// incumbent, and when the retained vein stops being worth anything on the same facts the tree wins that very
    /// comparison. Whether commitment should also lose to a shorter remaining trip is the reunion valuation's question;
    /// calm, with no reunion cost per tick, equal raw values leave the incumbent ahead by its commitment.
    /// </summary>
    private static void ARetainedVeinIsComparedNotReserved()
    {
        Point ore = new(45, 59), trunk = new(23, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        var brain = ctx.Companion.Brain;
        brain.Chooser.Actions.RemoveAll(action => action.Name is not ("mine" or "chop" or "keep-company"));
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        for (int tick = 0; tick < 30; tick++) VerifyOreWork.AdvanceBrain(ctx);
        var mine = brain.Chooser.Actions.OfType<MineOre>().Single();
        var chop = brain.Chooser.Actions.OfType<ChopTree>().Single();
        Require(brain.LastAction?.Name == "mine" && mine.JobId > 0, $"the fixture needs a retained mining job; action={brain.LastAction?.Name} status={mine.Status}");
        MakeTree(trunk);
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        TerrainChanges.Changed(trunk.X, trunk.Y);
        brain.Chooser.Choose(ctx);
        var chopScore = brain.Chooser.LastScores.FirstOrDefault(score => score.Action.Name == "chop");
        Require(chopScore.Raw > 0 && chop.Eligibility == OfferEligibility.Usable,
            $"the new tree must be prepared and compared while mining is retained; chop={chop.Eligibility}/{chop.EligibilityReason} raw={chopScore.Raw}");
        Tile transformed = Main.tile[ore.X, ore.Y];
        transformed.TileType = TileID.Chlorophyte;
        Main.tileSolid[TileID.Chlorophyte] = true;
        var chosen = brain.Chooser.Choose(ctx);
        Require(chosen?.Name == "chop",
            $"with the retained vein worth nothing the tree must win on the next comparison; chosen={chosen?.Name} mine={mine.Eligibility}/{mine.EligibilityReason} scores={string.Join(",", brain.Chooser.LastScores.Select(s => $"{s.Action.Name}={s.Raw:0.000}->{s.Final:0.000}"))}");
    }

    /// <summary>An ore and a tree either side of the companion, neither in reach. Mining and chopping must price the walk in the
    /// same unit: each forecast less its native remaining work is the distance from the feet to that activity's own working
    /// pose over walking speed. The two walks differ, because a solid ore offers one face and a trunk either side.</summary>
    private static void MiningAndChoppingPriceTravelInOneUnit()
    {
        // Clear of the ten-tile world margin TreeFinder refuses to search.
        Point ore = new(40, 59), trunk = new(16, 59);
        var (mine, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        MakeTree(trunk);
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        // A hover one radius clear of the floor, and every distance below measured from the same centre the
        // activities price from. Writing `Bottom` put half the circle inside the floor and priced the walk from a
        // point ten pixels under the one the forecast used, which is why the two sides disagreed by a fraction of
        // a tick rather than by anything structural.
        ctx.Npc.Center = new Vector2(28 * 16 + 8, 60 * 16 - live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact.Radius);
        TerrainChanges.Reset();
        var chop = new ChopTree();
        Require(VerifyPreparedActivities.PrepareAndScore(mine, ctx) > 0 && VerifyPreparedActivities.PrepareAndScore(chop, ctx) > 0
            && mine.RemainingWork is { } oreWork && chop.RemainingWork is { } treeWork,
            "the unit fixture needs both a proven ore job and a proven trunk");
        float walkSpeed = live::AICompanion.Companion.Brain.Infrastructure.Movement.OrbPace.MaxSpeed;
        float mineWalk = mine.ForecastTicks() - mine.RemainingWork!.Value.Ticks;
        float chopWalk = chop.ForecastTicks() - chop.RemainingWork!.Value.Ticks;
        float expectedMine = Vector2.Distance(ctx.Npc.Center, mine.TargetStandPosition!.Value) / walkSpeed;
        // The trunk's working pose from the same shared query and the same body point that chopping's discovery asks.
        Require(FindToolAccess.Approach(trunk, ctx.Npc.Center, ctx.Companion.Brain.Senses.Reach, out Vector2 chopStand) == live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability.Reach.Yes,
            "the unit fixture needs a proven working pose for the trunk");
        float expectedChop = Vector2.Distance(ctx.Npc.Center, chopStand) / walkSpeed;
        Require(MathF.Abs(mineWalk - expectedMine) < 0.01f && MathF.Abs(chopWalk - expectedChop) < 0.01f && chopWalk > 0 && mineWalk > 0,
            $"mining and chopping must price the walk in one unit, pixels to their own working pose over walking speed; mine walk={mineWalk:0.00} (expected {expectedMine:0.00}) chop walk={chopWalk:0.00} (expected {expectedChop:0.00})");
    }

    private readonly record struct BrainRun(List<Vector2> ChopStrikes, List<Vector2> MineStrikes);

    private static BrainRun RunBrain(ActionContext ctx, int ticks, Func<bool> stop)
    {
        var chops = new List<Vector2>();
        var mines = new List<Vector2>();
        long lastChop = ctx.Companion.Chopper.LastOutcome?.Attempt ?? -1, lastMine = ctx.Companion.Miner.LastOutcome?.Attempt ?? -1;
        for (int tick = 0; tick < ticks && !stop(); tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
            if (ctx.Companion.Chopper.LastOutcome is { Productive: true } chop && chop.Attempt != lastChop) { chops.Add(ctx.Npc.Bottom); lastChop = chop.Attempt; }
            if (ctx.Companion.Miner.LastOutcome is { Productive: true } mine && mine.Attempt != lastMine) { mines.Add(ctx.Npc.Bottom); lastMine = mine.Attempt; }
            VerifyResponsiveFollowing.AdvanceNative(ctx.Companion);
        }
        return new(chops, mines);
    }

    private static void Advance(ActionContext ctx, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++) VerifyOreWork.AdvanceBrain(ctx);
    }

    private static AttemptOutcome? LastAttempt(ActionContext ctx, string activity)
        => ctx.Companion.Brain.Chooser.Activity.RecentAttempts.LastOrDefault(a => a.Activity == activity) is { AttemptId: > 0 } found ? found : null;

    private static live::AICompanion.Companion.Brain.Infrastructure.Interactions.TileToolObservation? LastToolOutcome(ActionContext ctx, bool chopping)
        => chopping ? ctx.Companion.Chopper.LastOutcome : ctx.Companion.Miner.LastOutcome;

    private static bool TargetPresent(Point target, bool chopping)
        => chopping ? TileChopper.TreeStands(target) : Main.tile[target.X, target.Y].HasTile;

    private static ActionContext SetUpOre(Point ore)
    {
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        WorkPolicies.Chopping = WorkPolicy.Disabled;
        Protection.Reset();
        return ctx;
    }

    /// <summary>A flat floor at row 60 with single-tile trees, mining disabled so the tree is the only gathering work.</summary>
    private static ActionContext SetUpTrees(params Point[] trunks)
    {
        Point placeholder = new(40, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, placeholder);
        Main.tile[placeholder.X, placeholder.Y].ClearEverything();
        foreach (Point trunk in trunks) MakeTree(trunk);
        WorkPolicies.Chopping = WorkPolicy.Opportunistic;
        Protection.Reset();
        TerrainChanges.Reset();
        return ctx;
    }

    private static void MakeTree(Point trunk)
    {
        Main.tileAxe[TileID.Trees] = true;
        Main.tileSolid[TileID.Trees] = false;
        TileID.Sets.IsATreeTrunk[TileID.Trees] = true;
        VerifyOreWork.Place(trunk, TileID.Trees);
    }

    /// <summary>A four-by-two bed with no enclosing room, which protects its conservative vicinity, announced as a terrain edit.</summary>
    private static List<Point> PlaceBed(Point origin)
    {
        var tiles = new List<Point>();
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 2; y++)
            {
                Point p = new(origin.X + x, origin.Y + y);
                Tile t = Main.tile[p.X, p.Y];
                t.ClearEverything();
                t.HasTile = true;
                t.TileType = TileID.Beds;
                t.TileFrameX = (short)(x * 18);
                t.TileFrameY = (short)(y * 18);
                TerrainChanges.Changed(p.X, p.Y);
                tiles.Add(p);
            }
        return tiles;
    }

    private static Dictionary<Point, (bool Present, ushort Type)> Snapshot()
    {
        var map = new Dictionary<Point, (bool, ushort)>();
        for (int x = 0; x < Main.maxTilesX; x++)
            for (int y = 0; y < Main.maxTilesY; y++)
                map[new Point(x, y)] = (Main.tile[x, y].HasTile, Main.tile[x, y].TileType);
        return map;
    }

    /// <summary>No tile but <paramref name="allowed"/> may have changed presence or material since the snapshot: gathering never excavates.</summary>
    private static void RequireOnlyChanged(Dictionary<Point, (bool Present, ushort Type)> before, params Point[] allowed)
    {
        var changed = before.Where(entry => !allowed.Contains(entry.Key)
            && (Main.tile[entry.Key.X, entry.Key.Y].HasTile, Main.tile[entry.Key.X, entry.Key.Y].TileType) != entry.Value)
            .Select(entry => entry.Key).Take(8).ToList();
        Require(changed.Count == 0, $"gathering changed tiles other than its target: {string.Join(", ", changed)}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
