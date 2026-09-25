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
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
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
        red += Row("a decision in flight after work holds the body instead of returning it to the player", WorkHoldsThePlaceWhileDeciding);
        red += Row("a decision begun after the allowance ran out waits for its censuses instead of settling on nothing", AStarvedDecisionWaitsForItsCensuses);
        red += Row("a committed fight is not released because the player walked away from it", AFightOutlastsThePlayerWalkingAway);
        return red;
    }

    /// <summary>
    /// A fight the player walks away from is finished, by the owner's ruling of 25 September 2026. Until then a
    /// committed plan was released once its stand's gap beyond the player's region passed twice what it was admitted
    /// at, so a player walking on ended the fight rather than the fight ending. The scene commits a fight beside a
    /// standing player, then walks him away far enough to have crossed that line, keeping the zombie inside the
    /// work radius so nothing else has a reason to end it.
    /// </summary>
    /// <summary>Whether the plan committed now has its current stand past the line the retired release used: a gap
    /// beyond the player's region of more than twice the gap it was admitted at, plus half the region's smaller
    /// half-size.</summary>
    private static bool PastTheOldLine(ActionContext ctx)
    {
        var plan = ctx.Companion.Combat.Planner.Committed;
        if (plan == null) return false;
        var region = ctx.Senses.Intent.Region;
        Vector2 stand = plan.Current((int)ctx.Senses.Tick).Stand.Stand;
        return region.GapBeyond(stand) > 2f * plan.Validity.AdmittedCompanyGap + MathF.Min(region.HalfSize.X, region.HalfSize.Y) / 2f;
    }

    private static void AFightOutlastsThePlayerWalkingAway()
    {
        Vector2 screen = Main.screenPosition;
        try
        {
            ActionContext ctx = Scene();
            ctx.Npc.Bottom = ctx.Player.Bottom + new Vector2(-120f, 0f);
            Main.npc[30].Bottom = ctx.Npc.Bottom + new Vector2(260f, -150f);
            ctx.Senses.Update(ctx.Npc, ctx.Player);
            for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
            var planner = ctx.Companion.Combat.Planner;
            Require(planner.Committed != null,
                $"premise: no fight was committed beside the player; decision={ctx.Companion.Brain.Course.Last.Reason}");

            int companyReleases = 0, beyondTheLine = 0;
            for (int tick = 0; tick < 80; tick++)
            {
                // Walking left, away from the fight on his right, at a brisk pace; the screen follows him as the game's does.
                ctx.Player.velocity = new Vector2(-6f, 0f);
                ctx.Player.position.X -= 6f;
                Main.screenPosition = ctx.Player.Center - new Vector2(Main.screenWidth, Main.screenHeight) / 2f;
                Tick(ctx);
                if (planner.LastInvalidation == "company-gap-doubled") companyReleases++;
                if (PastTheOldLine(ctx)) beyondTheLine++;
            }

            // The scene the old release was measured firing in: the player standing 750 px from a fight, where it ended
            // the committed plan every few ticks.
            ActionContext far = Scene();
            for (int tick = 0; tick < 120; tick++)
            {
                Tick(far);
                if (far.Companion.Combat.Planner.LastInvalidation == "company-gap-doubled") companyReleases++;
                if (PastTheOldLine(far)) beyondTheLine++;
            }
            Require(beyondTheLine > 0,
                "premise: no committed plan's stand ever sat past the old release line on any tick, so the row is vacuous");
            Require(companyReleases == 0,
                $"a committed fight was released for its distance from the player on {companyReleases} tick(s); a fight he "
                + "walks away from is finished, not dropped");
            Console.WriteLine($"  a committed stand sat past the old release line on {beyondTheLine} tick(s) across both scenes; "
                + "no plan was released for the player's distance");
        }
        finally
        {
            Main.screenPosition = screen;
            Main.npc[30].active = false;
            Main.npc[30].life = 0;
        }
    }

    /// <summary>
    /// A decision that starts after the tick's allowance is spent must not settle on a catalogue discovery never
    /// read for its observation.
    ///
    /// That start is the ordinary case under the game's clock, because every activity prepares before the course
    /// decides. On the replay of the 25 September 2026 capture a zombie died at tick 2463, combat committed a plan
    /// on the next one in the same tick, and the decision that started beside it found the clock already spent:
    /// discovery asked no source, combat's candidates had just been retired with the dead target, its coverage still
    /// read complete from an earlier pass, and the search settled on the empty order with the fight standing ready.
    /// A fresh course owner is driven directly here so the only catalogue it could hold is the one this decision
    /// discovers: the first call is handed a spent allowance, the second a whole one.
    /// </summary>
    private static void AStarvedDecisionWaitsForItsCensuses()
    {
        Vector2 screen = Main.screenPosition;
        try
        {
            ActionContext ctx = Scene();
            Brain brain = ctx.Companion.Brain;
            var fight = brain.Actions.OfType<FightEnemies>().Single();
            for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
            int uses = fight.LastSearch?.Front.Sum(plan => plan.Segments.Sum(segment => segment.Uses.Length)) ?? 0;
            Require(uses > 0,
                $"premise: combat's search must hold a front with uses for the course to discover; front={fight.LastSearch?.Front.Count ?? 0}");

            var owner = new DecideCourseEachTick();
            // Each allowance is made the standing one, because the course's travel borrows the ambient allowance and
            // refuses a second one passed in beside it.
            CourseDecision starved, next;
            var spent = new DecisionWorkBudget(double.PositiveInfinity, 0, () => 0, 1);
            using (live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Own(spent))
                starved = owner.Decide(ctx, ctx.Companion.Combat, fight.LastSearch, spent);
            Require(!starved.Settled,
                $"premise: a decision handed a spent allowance cannot have finished; reason={starved.Reason}");
            var whole = new DecisionWorkBudget(double.PositiveInfinity, long.MaxValue, () => 0, 1);
            using (live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Own(whole))
                next = owner.Decide(ctx, ctx.Companion.Combat, fight.LastSearch, whole);
            string leaders = string.Join(", ", owner.LastLeaders.Keys);
            Require(!(next.Settled && next.Binding == null),
                $"a decision begun on a spent allowance settled as {next.Reason} with no step while combat's front held "
                + $"{uses} use(s); leaders priced: {(leaders.Length == 0 ? "none" : leaders)}");
            Require(next.Activity == "combat" && next.Binding != null,
                $"with the whole allowance the decision should have bound the fight its censuses found; "
                + $"got {next.Activity}/{next.Reason}, leaders priced: {(leaders.Length == 0 ? "none" : leaders)}");
            Console.WriteLine($"  a decision begun on a spent allowance stayed in flight ({starved.Reason}), then bound "
                + $"{next.Activity} once its censuses answered; leaders priced: {leaders}");
        }
        finally
        {
            Main.screenPosition = screen;
            Main.npc[30].active = false;
            Main.npc[30].life = 0;
        }
    }

    /// <summary>
    /// The allowance at which combat's own search is starved and offers nothing, so a released plan leaves no
    /// continuation. Measured 22 September 2026 as the bottom of this scene's ladder: at a hundred operations
    /// combat offers no plan at all. The row asserts that premise rather than trusting the number.
    /// </summary>
    private const long StarvedAllowance = 100;

    /// <summary>
    /// A decision that has not settled is not a decision that found nothing to do, and the owner ruled on
    /// 25 September 2026 that keeping company is what the companion does only then. A body that was working
    /// when the decision began therefore holds where it is; one that was not keeps the player company as
    /// before. Both arms run in the same scene so the difference is the previous tick and nothing else.
    /// </summary>
    private static void WorkHoldsThePlaceWhileDeciding()
    {
        Vector2 screen = Main.screenPosition;
        long allowance = Brain.PlanningOperationAllowance;
        try
        {
            // The arm that was not working: a fresh companion, starved from its first tick, has decided nothing.
            ActionContext idle = Scene();
            Brain.PlanningOperationAllowance = StarvedAllowance;
            Tick(idle);
            CourseDecision first = idle.Companion.Brain.Course.Last;
            Require(!first.Settled,
                $"premise: a starved first decision must still be in flight; reason={first.Reason}");
            Require(idle.Companion.Brain.LastRequest.Kind == RequestKind.WithPlayer,
                $"a companion that had done nothing yet asked for {idle.Companion.Brain.LastRequest.Kind} while deciding "
                + $"(reason={first.Reason}); with nothing done there is nothing to hold, so it keeps the player company");
            Brain.PlanningOperationAllowance = allowance;

            // The arm that was working: a committed fight, then plan and course both released under a starved allowance.
            ActionContext ctx = Scene();
            Brain brain = ctx.Companion.Brain;
            for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
            Require(ctx.Companion.Combat.Planner.Committed != null && brain.Course.Last.Activity == "combat",
                $"premise: the body must be fighting before the decision starts; activity={brain.Course.Last.Activity}, "
                + $"reason={brain.Course.Last.Reason}");
            Vector2 before = ctx.Npc.Center;

            Brain.PlanningOperationAllowance = StarvedAllowance;
            ctx.Companion.Combat.Planner.Release("the fixture ends the fight's plan");
            ReleaseTheCourse(ctx);
            int deciding = 0, held = 0;
            string other = "";
            for (int tick = 0; tick < 3; tick++)
            {
                Tick(ctx);
                CourseDecision decision = brain.Course.Last;
                if (decision.Settled) continue;
                deciding++;
                Require(brain.Fighting?.Continuation == null,
                    $"premise: combat re-committed under a starved allowance, so the tick has a fight to hold; "
                    + $"plan={ctx.Companion.Combat.Planner.Committed?.Id}");
                if (brain.LastRequest.Kind == RequestKind.Hold) held++;
                else if (other.Length == 0) other = $"{decision.Activity}/{brain.LastRequest.Kind}/{decision.Reason}";
            }
            Require(deciding == 3,
                $"premise: the starved allowance must hold the decision open for three ticks; unsettled={deciding}/3, "
                + $"last reason={brain.Course.Last.Reason}");
            Require(held == 3,
                $"a decision in flight right after a fight asked for {other} on {3 - held} of 3 ticks instead of holding, "
                + "which flies the body back to the player between two jobs");
            Console.WriteLine($"  starved at {StarvedAllowance} operations: a fresh companion keeps company while deciding; "
                + $"after its fight's plan and course were released the body held on 3/3 deciding ticks "
                + $"(from {before.X:0},{before.Y:0})");
        }
        finally
        {
            Brain.PlanningOperationAllowance = allowance;
            Main.screenPosition = screen;
            Main.npc[30].active = false;
            Main.npc[30].life = 0;
        }
    }

    private static int Row(string name, Action test)
    {
        return RunOneRow.GreenOrRed(name, test);
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
            var fight = brain.Actions.OfType<FightEnemies>().Single();

            // Premise: the body is in a fight it committed to, with the whole allowance available. A row
            // that starves the brain before combat has anything to lose proves nothing about losing it.
            for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
            long committed = ctx.Companion.Combat.Planner.Committed?.Id
                ?? throw new InvalidOperationException(
                    $"premise: no plan was ever committed, so there is no fight to interrupt; "
                    + $"activity={brain.Activity.Current?.Name ?? "none"}; decision={brain.Course.Last.Reason}; "
                    + $"offered={fight.OfferedPlan?.Id.ToString() ?? "none"}; eligibility={fight.Eligibility}/{fight.EligibilityReason}");

            Brain.PlanningOperationAllowance = cap;
            ReleaseTheCourse(ctx);
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
            // Not "the same plan id survived": combat may release and re-commit for its own reasons, which
            // is the stance deciding and exactly what it is allowed to do. What a
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
                if (brain.Activity.Current?.Name == "combat") combatTicks++;
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
            // The zombie is a slot in `Main.npc` and the per-case reset does not deactivate one, so a
            // scene left standing here is a hostile in whatever runs next — which for a fixture measuring
            // a plain follow journey is work to do instead of a journey to open.
            Main.npc[30].active = false;
            Main.npc[30].life = 0;
        }
    }

    /// <summary>
    /// Releases the held course, the way downing does, so the next tick starts a decision beside a fight that is
    /// still committed. Until 25 September 2026 the scene reached that state on its own: combat released its
    /// plan with `company-gap-doubled` every few ticks because the player stands 750 px away, and each release
    /// started a decision. That release went with the owner's ruling that a fight is never charged for its
    /// distance from the player, and the fight then holds its course on every tick, so the state the row is
    /// about has to be put there rather than waited for.
    /// </summary>
    private static void ReleaseTheCourse(ActionContext ctx) => ctx.Companion.Brain.Course.Interrupt("the fixture puts a decision in flight");

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
                var fight = ctx.Companion.Brain.Actions.OfType<FightEnemies>().Single();
                for (int tick = 0; tick < 60 && ctx.Companion.Combat.Planner.Committed == null; tick++) Tick(ctx);
                Brain.PlanningOperationAllowance = cap;
                ReleaseTheCourse(ctx);
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
