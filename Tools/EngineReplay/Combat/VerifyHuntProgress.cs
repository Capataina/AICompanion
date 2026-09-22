extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

using Combat = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;

/// <summary>
/// The committed plan's stall clock: the firing phase may go <see cref="StallWindow"/> ticks without a
/// planned use firing or a planned hit landing before the plan stalls and defers every body it targeted.
/// Ordained travel does not consume the window, suspension renews it, and an unattributed "fired" that the
/// hands never reported renews nothing. The window is shorter than the horizon on purpose: a window as long
/// as the horizon would never bind, because the segment always ends complete first.
/// </summary>
internal static class VerifyHuntProgress
{
    private static int StallWindow => live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.CombatPlanStallTicks;

    public static int Run()
    {
        VerifyIdleHandsStallAndDefer();
        VerifyMovedTargetReopens();
        VerifyFiredUseRenews();
        VerifySuspensionRenews();
        VerifyChurnStillDefers();
        VerifyUnattributedFiredRenewsNothing();
        VerifyTravelDoesNotConsumeTheWindow();
        Console.WriteLine("combat progress: idle hands stall and defer, a moved target reopens, firing and suspension renew, churn still defers, travel is not idleness");
        return 0;
    }

    /// <summary>
    /// A running plan whose hands never fire stalls once the window passes its start: the commitment ends
    /// with reason "stall", and the next preparation finds the body deferred rather than offering again.
    /// </summary>
    private static void VerifyIdleHandsStallAndDefer()
    {
        var (companion, ctx, combat) = RunningScene(12, 160);
        int deadline = StallDeadline(combat.OfferedPlan!, combat.OfferedPlan!.Validity.LastProgressTick);

        SetTick(companion, deadline);
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(combat.CommittedPlan == null && companion.Combat.Planner.LastInvalidation == "stall",
            $"a running plan idle past its window must stall; score={score} invalidation={companion.Combat.Planner.LastInvalidation} reason={combat.EligibilityReason}");

        score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(score == 0f && combat.EligibilityReason == "no-eligible-target",
            $"a stalled body must be deferred, not re-offered; score={score} reason={combat.EligibilityReason}");
        Require(combat.Funnel.Best is { RefusedAt: "engagement-deferred" },
            $"the funnel names the deferred body; best={combat.Funnel.Best} counts={combat.Funnel.Summary()}");
    }

    /// <summary>A deferred body that moves on reopens early: the deferral waits on an unchanged world.</summary>
    private static void VerifyMovedTargetReopens()
    {
        var (companion, ctx, combat) = RunningScene(12, 160);
        SetTick(companion, StallDeadline(combat.OfferedPlan!, combat.OfferedPlan!.Validity.LastProgressTick));
        VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(VerifyPreparedActivities.PrepareAndScore(combat, ctx) == 0f,
            "the reopen scene needs the stall to have deferred the body first");

        Main.npc[12].position.X += 64;
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(score > 0f && combat.OfferedPlan != null,
            $"a deferred body that moved 64px must be reconsidered; score={score} reason={combat.EligibilityReason}");
    }

    /// <summary>
    /// A planned use leaving the hand renews the window from its own tick: the plan is still good past the
    /// deadline its search set, and stalls a window after the use instead.
    /// </summary>
    private static void VerifyFiredUseRenews()
    {
        var (companion, ctx, combat) = RunningScene(12, 160);
        var first = combat.CommittedPlan!;
        int start = Math.Max(first.Validity.LastProgressTick, first.Current(first.Validity.LastProgressTick).StartTick);

        SetTick(companion, start + 30);
        companion.Combat.Planner.NoteUseFired(start + 30);
        SetTick(companion, start + StallWindow + 1);
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(score > 0f && ReferenceEquals(combat.CommittedPlan, first),
            $"a use fired at +30 must keep the plan past the search's deadline; score={score} reason={combat.EligibilityReason} invalidation={companion.Combat.Planner.LastInvalidation}");

        int stallAt = start + 30 + StallWindow + 1;
        int endTick = first.Current(start).EndTick;
        SetTick(companion, stallAt < endTick ? stallAt : endTick + 1);
        VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        if (stallAt < endTick)
            Require(combat.CommittedPlan == null && companion.Combat.Planner.LastInvalidation == "stall",
                $"the renewed window must still end in a stall when nothing more fires; invalidation={companion.Combat.Planner.LastInvalidation}");
        else
            Require(companion.Combat.Planner.LastInvalidation == "segment-complete",
                $"a renewed window that outlasts the horizon ends complete, not as a stall; invalidation={companion.Combat.Planner.LastInvalidation}");
    }

    /// <summary>
    /// Safety suspending the activity renews the window rather than consuming it: the body cannot fire while
    /// something else owns it, and counting that as no progress would end every plan the evade layer touched.
    /// The renewed plan then lives to its horizon and re-searches; suspension is not a stall.
    /// </summary>
    private static void VerifySuspensionRenews()
    {
        var (companion, ctx, combat) = RunningScene(12, 160);
        int searchTick = combat.OfferedPlan!.Validity.LastProgressTick;
        var first = combat.CommittedPlan!;
        var owner = companion.Brain.Activity;

        SetTick(companion, searchTick + 60);
        owner.Suspend(ctx, "follow-recovery-flight");
        for (int i = 0; i < 182; i++) owner.ObserveOutcome(ctx); // suspended: observed by nobody
        SetTick(companion, searchTick + StallWindow + 1);
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(score > 0f && ReferenceEquals(combat.CommittedPlan, first),
            $"suspension must renew the window, not consume it; score={score} reason={combat.EligibilityReason} invalidation={companion.Combat.Planner.LastInvalidation}");

        owner.Select(combat, ctx);
        owner.BeginExecution();
        SetTick(companion, searchTick + 60 + StallWindow + 1);
        score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(companion.Combat.Planner.LastInvalidation == "segment-complete" && score > 0f && !ReferenceEquals(combat.OfferedPlan, first),
            $"a renewed plan lives to its horizon and re-searches rather than stalling; score={score} reason={combat.EligibilityReason} invalidation={companion.Combat.Planner.LastInvalidation}");
    }

    /// <summary>
    /// A crowd makes the pick alternate, and changing target is not progress: two enemies scoring within
    /// noise of each other, a body that never fires, must still end with both deferred — in one window each
    /// rather than one window total, because each plan stalls and defers its own bodies, and never in a fresh
    /// window for ever. The 2026-09-11 session stood still for 329 consecutive ticks inside a guard whose
    /// window is a fraction of that, because a target switch reset the stall counter.
    /// </summary>
    private static void VerifyChurnStillDefers()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        // A handed knife is a pile. SetDefaults leaves one, and the stack cap then prices a
        // single throw; a one-throw plan completes and re-searches instead of stalling.
        foreach (var weapon in companion.Combat.Weapons)
            if (weapon.ItemType == Terraria.ID.ItemID.ThrowingKnife
                && weapon is live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ItemWeapon item)
                item.Item.stack = 999;
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        ClearHostileSlots();
        companion.NPC.Bottom = new Vector2(50 * 16f, FloorY * 16f);
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer);
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        // Equidistant on opposite sides, so neither the distance term nor urgency separates them
        // and the selected target genuinely alternates rather than one winning every tick.
        foreach ((int slot, int offset) in new[] { (20, -150), (21, 150) })
        {
            var npc = new NPC(); npc.SetDefaults(Terraria.ID.NPCID.Zombie);
            npc.whoAmI = slot; npc.active = true;
            npc.Bottom = companion.NPC.Bottom + new Vector2(offset, 0);
            Main.npc[slot] = npc;
            threats.Add(new T { Npc = npc, DistanceToCompanion = 150, DistanceToPlayer = 150 });
        }
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = new Combat();
        companion.Brain.Activity.Select(combat, ctx);
        if (VerifyPreparedActivities.PrepareAndScore(combat, ctx) <= 0)
            throw new InvalidOperationException("Churn fixture must offer a live target");

        // Past two windows with the selection forced to alternate and the hands idle. Scoring each tick is
        // what re-picks the target, exactly as the brain does; the tick is the stall clock.
        int tick = companion.Brain.Senses.Tick;
        for (int i = 0; i < 300; i++)
        {
            threats.Reverse();
            SetTick(companion, ++tick);
            VerifyPreparedActivities.PrepareAndScore(combat, ctx);
            combat.ObserveOutcome(ctx);
        }
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(score == 0f && combat.EligibilityReason == "no-eligible-target",
            $"alternating targets must still end deferred, one window each; score={score} reason={combat.EligibilityReason}");
        Require(combat.Funnel.Counts.TryGetValue("engagement-deferred", out int deferred) && deferred == 2,
            $"both alternating bodies are deferred, not only the selected one; counts={combat.Funnel.Summary()}");
    }

    /// <summary>
    /// Progress is what the hands report through the planner, not the outcome string: a "fired" the hands
    /// never paired with a planned use — the old independent-hands case, where another job's shot used to
    /// renew a pursuit it had nothing to do with — renews nothing and the plan still stalls.
    /// </summary>
    private static void VerifyUnattributedFiredRenewsNothing()
    {
        var (companion, ctx, combat) = RunningScene(12, 160);
        int searchTick = combat.OfferedPlan!.Validity.LastProgressTick;

        var setOutcome = typeof(live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat)
            .GetProperty("LastFireOutcome")!.GetSetMethod(true)!;
        setOutcome.Invoke(companion.Combat, new object[] { "fired" });
        combat.ObserveOutcome(ctx);
        SetTick(companion, StallDeadline(combat.OfferedPlan, searchTick));
        VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(combat.CommittedPlan == null && companion.Combat.Planner.LastInvalidation == "stall",
            $"an outcome the hands never reported must not renew the window; invalidation={companion.Combat.Planner.LastInvalidation}");
    }

    /// <summary>
    /// The window runs from the segment's start: travel the plan ordained is the plan working, so a plan
    /// still flying to its stand is good past the deadline its search set, and stalls a window after arrival.
    /// </summary>
    private static void VerifyTravelDoesNotConsumeTheWindow()
    {
        BuildFloor();
        for (int y = FloorY - 8; y < FloorY; y++) Solid(40, y);
        Rebuild();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(38 * 16f, FloorY * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(38 * 16f, FloorY * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        ClearHostileSlots();
        var enemy = new NPC(); enemy.SetDefaults(Terraria.ID.NPCID.Zombie);
        enemy.whoAmI = 12; enemy.active = true; enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(60 * 16f + 8f, FloorY * 16f);
        Main.npc[12] = enemy;
        companion.Brain.Senses.Update(companion.NPC, player);
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = new Combat();
        companion.Brain.Activity.Select(combat, ctx);

        // Prime the reach flood to completion, then prepare once: the search's finished answer rather
        // than what the first flood slice happened to reach. WithPlayer pumps the flood; a firing request
        // without a flight profile early-outs before it refreshes anything.
        var primeRequest = new live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest(
            live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind.WithPlayer, player.Bottom);
        for (int i = 0; i < 3000 && !companion.Brain.Positioner.ReachComplete; i++)
            companion.Brain.Positioner.Resolve(primeRequest, companion.Brain.Senses);
        Require(companion.Brain.Positioner.ReachComplete, "the travel scene needs a completed flood before the plan can be read");
        VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(combat.CommittedPlan != null,
            $"the travel scene must settle to a committed plan; reason={combat.EligibilityReason}");
        var plan = combat.CommittedPlan!;
        int searchTick = plan.Validity.LastProgressTick;
        int startTick = plan.Current(searchTick).StartTick;
        int travel = startTick - searchTick;
        Require(travel > 0, "the travel scene needs a stand the body must fly to, not the hover it is already at");

        // Past the deadline the search set, still good: the body is doing what the plan says.
        SetTick(companion, searchTick + StallWindow + 1);
        float score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
        Require(score > 0f && ReferenceEquals(combat.CommittedPlan, plan),
            $"ordained travel must not consume the window; score={score} reason={combat.EligibilityReason} invalidation={companion.Combat.Planner.LastInvalidation} travel={travel}");

        // A window after arrival with idle hands, a stall — unless the last segment's horizon
        // remainder is shorter than the window, in which case the plan honestly ends first.
        // EndTick is the last segment's: a two-segment plan's first segment ends at departure,
        // and jumping there only advances the commitment.
        int lastStart = plan.Segments[^1].StartTick;
        int lastEnd = plan.Segments[^1].EndTick;
        if (lastStart + StallWindow + 1 < lastEnd)
        {
            SetTick(companion, lastStart + StallWindow + 1);
            VerifyPreparedActivities.PrepareAndScore(combat, ctx);
            Require(companion.Combat.Planner.LastInvalidation == "stall",
                $"idle hands past arrival must stall; invalidation={companion.Combat.Planner.LastInvalidation}");
        }
        else
        {
            SetTick(companion, lastEnd + 1);
            score = VerifyPreparedActivities.PrepareAndScore(combat, ctx);
            Require(companion.Combat.Planner.LastInvalidation == "segment-complete" && score > 0f,
                $"a plan that spent its horizon travelling ends and re-searches; score={score} invalidation={companion.Combat.Planner.LastInvalidation}");
        }
    }

    /// <summary>A running fight on a flat floor: offered, selected and committed, at the search's tick.</summary>
    private static (CompanionNPC Companion, C Ctx, Combat Combat) RunningScene(int slot, int offsetPx)
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        ClearHostileSlots();
        companion.NPC.Bottom = new Vector2(50 * 16f, FloorY * 16f);
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer);
        var target = new NPC(); target.SetDefaults(Terraria.ID.NPCID.Zombie);
        target.whoAmI = slot; target.active = true;
        target.Bottom = companion.NPC.Bottom + new Vector2(offsetPx, 0);
        Main.npc[slot] = target;
        companion.Brain.Senses.Threats.Threats.Clear();
        companion.Brain.Senses.Threats.Threats.Add(new T { Npc = target, DistanceToCompanion = offsetPx, DistanceToPlayer = offsetPx });
        var ctx = new C(companion, companion.Brain.Senses);
        var combat = new Combat();
        companion.Brain.Activity.Select(combat, ctx);
        if (VerifyPreparedActivities.PrepareAndScore(combat, ctx) <= 0 || combat.OfferedPlan == null || combat.CommittedPlan == null)
            throw new InvalidOperationException($"Combat progress fixture must offer and commit a live target; reason={combat.EligibilityReason}");
        return (companion, ctx, combat);
    }

    private const int FloorY = 80;

    private static void BuildFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 140;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)140, (ushort)140 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 115; x++)
            for (int y = FloorY; y <= FloorY + 2; y++)
                Solid(x, y);
        Rebuild();
    }

    private static void Rebuild()
    {
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void SetTick(CompanionNPC companion, int tick)
    {
        var setTick = typeof(live::AICompanion.Companion.Brain.Infrastructure.Observation.Senses)
            .GetProperty("Tick")!.GetSetMethod(true)!;
        setTick.Invoke(companion.Brain.Senses, new object[] { tick });
    }

    /// <summary>
    /// The stall clock runs from the segment's start, not the search: travel the plan ordained is
    /// the plan working. A BestRange stand a flight away would otherwise still look idle at
    /// search+window, which is the travel fixture's own case.
    /// </summary>
    private static int StallDeadline(live::AICompanion.Companion.Brain.Activities.Combat.Planning.AttackPlan plan, int progressTick)
        => Math.Max(progressTick, plan.Current(progressTick).StartTick) + StallWindow + 1;

    /// <summary>
    /// The hostile slots this file plants into, wiped before each observation. Every scene here
    /// hand-builds its threat list after observing, so a hostile an earlier scene left behind would
    /// still be scanned in and steal the execution a stall scene needs.
    /// </summary>
    private static void ClearHostileSlots()
    {
        foreach (int slot in new[] { 12, 13, 20, 21 }) Main.npc[slot] = new NPC();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
