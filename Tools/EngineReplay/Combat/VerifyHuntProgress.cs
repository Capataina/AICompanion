extern alias live;
using Microsoft.Xna.Framework;
using Terraria;
using H = live::AICompanion.Companion.Brain.Activities.Combat.PursueAttackOpportunity;
using T = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;

internal static class VerifyHuntProgress
{
    public static int Run()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer, companion.Motor);
        var target = new NPC(); target.SetDefaults(Terraria.ID.NPCID.Zombie);
        target.whoAmI = 12; target.active = true; target.Bottom = companion.NPC.Bottom + new Vector2(160, 0);
        Main.npc[12] = target;
        companion.Brain.Senses.Threats.Threats.Clear();
        companion.Brain.Senses.Threats.Threats.Add(new T { Npc = target, DistanceToCompanion = 160, DistanceToPlayer = 160 });
        var context = new C(companion, companion.Brain.Senses);
        var hunt = new H();
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0) throw new InvalidOperationException("Hunt progress fixture must offer a live target");
        for (int i = 0; i < 182; i++) hunt.ObserveOutcome(context);
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) != 0) throw new InvalidOperationException("Stationary hunt without attacks retained its ineffective target");
        target.position.X += 64;
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0) throw new InvalidOperationException("Moved target did not reopen a deferred engagement");
        for (int i = 0; i < 182; i++)
        {
            companion.NPC.position.X += 1;
            hunt.ObserveOutcome(context);
        }
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0) throw new InvalidOperationException("Travelling hunt was deferred despite progress");
        VerifyChurnDoesNotDefeatTheGuard(companion);
        VerifyIndependentHandsDoNotRenewPursuit();
        VerifySuspensionDoesNotConsumePursuitBudget();
        Console.WriteLine("hunt progress: ineffective target deferred, moving target reconsidered, travelling hunt retained, alternating targets still defer");
        return 0;
    }

    private static void VerifyIndependentHandsDoNotRenewPursuit()
    {
        foreach (var (outcome, sameEnemy, retained) in new[]
                 { ("fired", false, false), ("cooldown", true, false), ("fired", true, true) })
        {
            var companion = VerifyCompanionLifecycle.Create();
            Main.LocalPlayer.dead = false;
            Main.LocalPlayer.Bottom = companion.NPC.Bottom;
            companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer, companion.Motor);
            var target = new NPC(); target.SetDefaults(Terraria.ID.NPCID.Zombie);
            target.whoAmI = 12; target.active = true;
            target.Bottom = companion.NPC.Bottom + new Vector2(160, 0);
            Main.npc[12] = target;
            var other = new NPC(); other.SetDefaults(Terraria.ID.NPCID.Zombie);
            other.whoAmI = 13; other.active = true;
            Main.npc[13] = other;
            var threats = companion.Brain.Senses.Threats.Threats;
            threats.Clear();
            threats.Add(new T { Npc = target, DistanceToCompanion = 160, DistanceToPlayer = 160 });
            var context = new C(companion, companion.Brain.Senses);
            var hunt = new H();
            if (VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0)
                throw new InvalidOperationException("Independent-hands fixture must offer a pursuit");
            // Supply the hands boundary's outcome without creating projectiles. This tests
            // attribution, not native firing or whether a projectile subsequently hits.
            companion.Brain.GetType().GetProperty("EngageTarget")!.SetValue(companion.Brain, sameEnemy ? target : other);
            companion.Arsenal.GetType().GetProperty("LastFireOutcome")!.SetValue(companion.Arsenal, outcome);
            for (int i = 0; i < 182; i++) hunt.ObserveOutcome(context);
            bool stillOffered = VerifyPreparedActivities.PrepareAndScore(hunt, context) > 0;
            if (stillOffered != retained)
                throw new InvalidOperationException($"Pursuit attribution: outcome={outcome}, sameEnemy={sameEnemy}, retained={stillOffered}, expected={retained}");
        }
    }

    private static void VerifySuspensionDoesNotConsumePursuitBudget()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer, companion.Motor);
        var target = new NPC(); target.SetDefaults(Terraria.ID.NPCID.Zombie);
        target.whoAmI = 12; target.active = true;
        target.Bottom = companion.NPC.Bottom + new Vector2(160, 0);
        Main.npc[12] = target;
        var threats = companion.Brain.Senses.Threats.Threats;
        threats.Clear();
        threats.Add(new T { Npc = target, DistanceToCompanion = 160, DistanceToPlayer = 160 });
        var context = new C(companion, companion.Brain.Senses);
        var hunt = new H();
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0)
            throw new InvalidOperationException("Suspension fixture must offer a pursuit");
        var owner = companion.Brain.Chooser.Activity;
        owner.Select(hunt, context);
        owner.BeginExecution();
        owner.ObserveOutcome(context);
        owner.ObserveOutcome(context);
        int before = hunt.NoProgressTicks;
        owner.Suspend(context, "environmental-escape");
        for (int i = 0; i < 182; i++) owner.ObserveOutcome(context);
        if (hunt.NoProgressTicks != before || VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0)
            throw new InvalidOperationException($"Suspended pursuit consumed failure budget: before={before}, after={hunt.NoProgressTicks}, rejection={hunt.LastRejection}");
        owner.Select(hunt, context);
        owner.BeginExecution();
        for (int i = 0; i < 182; i++) owner.ObserveOutcome(context);
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) != 0)
            throw new InvalidOperationException("Resumed stationary pursuit no longer expires");
    }

    /// <summary>
    /// A crowd makes the pick alternate, and a target switch used to count as engagement progress,
    /// so the stall counter reset on nearly every tick and the deferral never fired: the 2026-09-11
    /// session stood still for 329 consecutive ticks inside a guard whose window is a fraction of
    /// that. Two enemies scoring within noise of each other, a body that never moves and a weapon
    /// that never fires must still end in a deferral — and in one window rather than one per
    /// enemy, because deferring only the selected target hands its partner a fresh window.
    /// </summary>
    private static void VerifyChurnDoesNotDefeatTheGuard(live::AICompanion.Companion.CharacterBody.CompanionNPC companion)
    {
        Main.LocalPlayer.dead = false;
        Main.LocalPlayer.Bottom = companion.NPC.Bottom;
        companion.Brain.Senses.Update(companion.NPC, Main.LocalPlayer, companion.Motor);
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
        var context = new C(companion, companion.Brain.Senses);
        var hunt = new H();
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) <= 0) throw new InvalidOperationException("Churn fixture must offer a live target");

        // Comfortably past one progress window, with the selection forced to alternate and the body
        // and the weapon both idle. Scoring each tick is what re-picks the target, exactly as the
        // brain does. The margin over the window is deliberate: the first observation always reads
        // as progress because the engagement origin starts unset, and the point of the check is the
        // behaviour across a long stall rather than the exact tick the deferral lands on.
        for (int i = 0; i < 300; i++)
        {
            threats.Reverse();
            VerifyPreparedActivities.PrepareAndScore(hunt, context);
            hunt.ObserveOutcome(context);
        }
        if (VerifyPreparedActivities.PrepareAndScore(hunt, context) != 0)
            throw new InvalidOperationException(
                "Alternating targets defeated the no-progress deferral: a stationary, non-firing hunt retained a target across a full window");
    }
}
