extern alias live;

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.Brain;
using live::AICompanion.Companion.Brain.Activities;
using live::AICompanion.Companion.Brain.Activities.Combat;
using live::AICompanion.Companion.Brain.Infrastructure.Position;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>
/// A decision that spans ticks must not kill the fight the body is already in.
///
/// A decision spanning ticks is the design rather than an edge: one travel query can cost more than a
/// tick's leftover allowance, and the observation is frozen for the life of the decision because of it.
/// What the tick then does with the body was the defect. It asked for companionship, the tick selects the
/// decision's activity, selecting keeping company exits combat, combat's <c>Exit</c> releases the
/// committed plan with <c>activity-exited</c>, the course's accepted use goes absent, the course is
/// released, and releasing the course starts a decision — which asks for companionship.
///
/// The play of 0.38.13 recorded that loop as 212 combat attempts at a median of one tick, 181 of them
/// ending <c>replaced-before-attacking</c>, 221 plans committed against 211 invalidated
/// <c>activity-exited</c>, and 483 course releases reading <c>accepted-use-not-present</c> — twenty shots
/// fired in a minute with hostiles present throughout.
///
/// The lever that makes this reproducible is an operation cap rather than a clock. The suite lifts every
/// millisecond allowance on purpose, so a fixture cannot time the machine; with the clock lifted the only
/// thing that can end a tick's share of a decision is the operation count, which is
/// <see cref="Brain.PlanningOperationAllowance"/>.
/// </summary>
internal static class VerifyADecisionInFlightKeepsTheFight
{
    public static int Run()
    {
        int red = 0;
        red += Row("a decision spanning three ticks does not exit the fight it found", TheFightSurvivesADecision);
        return red;
    }

    private static int Row(string name, Action test)
    {
        try { test(); Console.WriteLine("  GREEN " + name); return 0; }
        catch (Exception error) { Console.WriteLine("  RED " + name + ": " + error.Message); return 1; }
    }

    private static void TheFightSurvivesADecision()
    {
        Vector2 screen = Main.screenPosition;
        long allowance = Brain.PlanningOperationAllowance;
        try
        {
            long cap = CalibrateTheCap();
            ActionContext ctx = Scene();
            Brain brain = ctx.Companion.Brain;
            var fight = brain.Chooser.Actions.OfType<FightEnemies>().Single();

            // Premise: the body is in a fight it committed to, with the whole allowance available. A row
            // that starves the brain before combat has anything to lose proves nothing about losing it.
            for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
            long committed = ctx.Companion.Combat.Planner.Committed?.Id
                ?? throw new InvalidOperationException(
                    $"premise: no plan was ever committed, so there is no fight to interrupt; "
                    + $"activity={brain.Chooser.Current?.Name ?? "none"}; decision={brain.Course.Last.Reason}; "
                    + $"offered={fight.OfferedPlan?.Id.ToString() ?? "none"}; eligibility={fight.Eligibility}/{fight.EligibilityReason}");

            Brain.PlanningOperationAllowance = cap;
            int decidingTicks = 0, keptTheFight = 0, fired = 0;
            string firstOtherActivity = "";
            for (int tick = 0; tick < 3; tick++)
            {
                Tick(ctx);
                CourseDecision decision = brain.Course.Last;
                if (decision.Settled) continue;
                decidingTicks++;
                if (decision.Activity == "combat" && brain.LastRequest.Kind == RequestKind.FireFrom) keptTheFight++;
                else if (firstOtherActivity.Length == 0)
                    firstOtherActivity = $"{decision.Activity}/{brain.LastRequest.Kind}";
                if (ctx.Companion.Combat.LastFireOutcome == "fired") fired++;
            }

            Require(decidingTicks == 3,
                $"premise: the allowance must hold the decision open for all three ticks; unsettled={decidingTicks}/3, "
                + $"last reason={brain.Course.Last.Reason}");
            Require(keptTheFight == 3,
                $"a decision in flight took the body off the fight on {3 - keptTheFight} of 3 ticks — it asked for "
                + $"{(firstOtherActivity.Length == 0 ? "no continuation" : firstOtherActivity)} instead of combat/FireFrom, "
                + $"which exits combat and releases its plan: invalidation={ctx.Companion.Combat.Planner.LastInvalidation}, "
                + $"course release={brain.Course.Course.ReleaseReason}");
            // Not "the same plan id survived": combat legitimately releases and re-commits for its own
            // reasons in this scene — the player is 750 px away, so `company-gap-doubled` fires on its
            // own schedule and is the stance deciding, which is exactly what it is allowed to do. What a
            // deciding tick must never be is the *cause*, and `activity-exited` is the one release reason
            // that can only come from the tick selecting something else. The ninety-tick window below
            // counts that pairing properly; here it is enough that three deciding ticks left a plan.
            Require(ctx.Companion.Combat.Planner.Committed != null
                || ctx.Companion.Combat.Planner.LastInvalidation != "activity-exited",
                $"a deciding tick released the committed plan by exiting the activity; plan was={committed}, "
                + $"invalidation={ctx.Companion.Combat.Planner.LastInvalidation}, "
                + $"course release={brain.Course.Course.ReleaseReason}");
            // The hand is independent work, so a held fight must still be able to shoot while the brain
            // thinks. Cooldown decides which tick, so this asks for a shot inside a window rather than on
            // a named one: a hand that never fires across a whole use cycle is a held fight in name only.
            int firedInWindow = fired, decidingInWindow = decidingTicks, exitedWhileDeciding = 0, combatTicks = 0;
            for (int tick = 0; tick < 90; tick++)
            {
                // A *new* activity exit on a tick whose decision had not settled. The invalidation string
                // is sticky and a settled tick may legitimately exit combat — that is the course choosing
                // something else — so neither the string alone nor a count of ticks carrying it separates
                // the defect from ordinary play. The pair does.
                long? before = ctx.Companion.Combat.Planner.Committed?.Id;
                Tick(ctx);
                bool deciding = !brain.Course.Last.Settled;
                if (deciding) decidingInWindow++;
                if (brain.Chooser.Current?.Name == "combat") combatTicks++;
                if (deciding && before != null && ctx.Companion.Combat.Planner.Committed == null
                    && ctx.Companion.Combat.Planner.LastInvalidation == "activity-exited") exitedWhileDeciding++;
                if (ctx.Companion.Combat.LastFireOutcome == "fired") firedInWindow++;
            }
            Require(exitedWhileDeciding == 0,
                $"a deciding tick released the committed plan by exiting the activity {exitedWhileDeciding} time(s) "
                + $"in ninety-three ticks, which is the loop the play recorded: combat={combatTicks}/93, "
                + $"deciding={decidingInWindow}/93, course release={brain.Course.Course.ReleaseReason}");
            Require(firedInWindow > 0,
                $"nothing fired across ninety-three ticks with a committed plan and the body at its firing stand, so "
                + $"the fight is held and mute; deciding={decidingInWindow}/93, combat={combatTicks}/93, "
                + $"fire={ctx.Companion.Combat.LastFireOutcome}, request={brain.LastRequest.Kind}, "
                + $"committed={ctx.Companion.Combat.Planner.Committed?.Id.ToString() ?? "released"}");

            Console.WriteLine($"  decision in flight at an allowance of {cap} operations: 3/3 ticks kept combat at "
                + $"FireFrom, plan {committed} never released by an activity exit across 93 ticks "
                + $"({decidingInWindow} of them deciding, combat held {combatTicks}), {firedInWindow} shot(s) fired");
        }
        finally
        {
            Brain.PlanningOperationAllowance = allowance;
            Main.screenPosition = screen;
        }
    }

    /// <summary>
    /// The operation allowance this scene needs, measured rather than written down.
    ///
    /// The window is narrow and it is narrow for a structural reason: every activity's `Prepare` runs
    /// before the decision and spends from the same allowance, so a cap low enough to guarantee an
    /// unfinished search is also low enough to starve combat's own tactical search — measured here, at a
    /// hundred operations combat offers no plan at all and there is nothing left to keep. A hard-coded
    /// number would therefore be a number that stops being true the first time either side of that seam
    /// gets cheaper or dearer, and it would fail by making the row vacuous rather than red.
    ///
    /// So the fixture measures it: the largest cap on the ladder at which all three ticks leave the
    /// decision unsettled *and* combat still offers a plan. Both halves are premises rather than the
    /// property — combat offers a plan on the parent tree too, where the activity is exited on every one
    /// of those ticks and re-searches on the next — so calibrating on them cannot smuggle the property in.
    /// Measured 22 September 2026: the window is 150 to 400 operations, 4,000 finishes every search and
    /// 100 starves the fight.
    /// </summary>
    private static long CalibrateTheCap()
    {
        foreach (long cap in new long[] { 2000, 1000, 600, 400, 300, 200, 150 })
        {
            Vector2 screen = Main.screenPosition;
            long previous = Brain.PlanningOperationAllowance;
            try
            {
                ActionContext ctx = Scene();
                var fight = ctx.Companion.Brain.Chooser.Actions.OfType<FightEnemies>().Single();
                for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
                Brain.PlanningOperationAllowance = cap;
                int unsettled = 0, offering = 0;
                for (int tick = 0; tick < 3; tick++)
                {
                    Tick(ctx);
                    if (!ctx.Companion.Brain.Course.Last.Settled) unsettled++;
                    if (fight.OfferedPlan != null) offering++;
                }
                if (unsettled == 3 && offering == 3) return cap;
            }
            finally
            {
                Brain.PlanningOperationAllowance = previous;
                Main.screenPosition = screen;
            }
        }
        throw new InvalidOperationException(
            "no allowance on the ladder both held the decision open for three ticks and left combat a plan to "
            + "offer, so this scene can no longer express a decision in flight beside a live fight");
    }

    /// <summary>
    /// The shared-eagerness geometry, because it is the one this tree already establishes a course will
    /// choose to fight in: the player standing still, the companion trailing well outside his region, and
    /// a motionless zombie ahead of and above the companion, close enough that a bow shot solves from
    /// where it hovers. The screen is centred on the player as the live game centres it, because the
    /// hunt's own nearness reads the screen.
    /// </summary>
    private static ActionContext Scene()
    {
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Disabled, TileID.Copper, new Point(25, 89));
        Main.tile[25, 89].ClearEverything();
        foreach (Projectile projectile in Main.projectile) projectile.active = false;
        ctx.Player.Bottom = new Vector2(70 * 16, 90 * 16);
        ctx.Player.velocity = Vector2.Zero;
        ctx.Npc.Bottom = new Vector2(70 * 16 - 750, 90 * 16);
        // The gear is whatever `VerifyOreWork.SetUp` hands the companion, deliberately: the shared-eagerness
        // scene this geometry comes from wins its fight on that kit, and overriding the weapon slots here
        // produced a scene where the course priced the fight and declined it.
        Main.screenPosition = ctx.Player.Center - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;

        NPC zombie = Main.npc[30];
        zombie.SetDefaults(NPCID.Zombie);
        zombie.whoAmI = 30;
        zombie.active = true;
        zombie.velocity = Vector2.Zero;
        zombie.Bottom = ctx.Npc.Bottom + new Vector2(400f, -150f);
        ctx.Senses.Update(ctx.Npc, ctx.Player);
        return ctx;
    }

    /// <summary>The body is held where it stands after each tick, so the matchup is measured rather than
    /// chased: the positioner still rescores onto it and the zombie stays shootable throughout.</summary>
    private static void Tick(ActionContext ctx)
    {
        Vector2 held = ctx.Npc.Bottom;
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        ctx.Npc.Bottom = held;
        ctx.Npc.velocity = Vector2.Zero;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
