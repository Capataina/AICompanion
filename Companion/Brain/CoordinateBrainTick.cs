#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Grants;
using AICompanion.Companion.Brain.SharedBehaviours.Safety;
using AICompanion.Companion.Brain.SharedBehaviours.Recovery;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.Brain;

/// <summary>
/// The tick order of the companion's mind: senses read the world, the chooser picks a
/// family offer, the behaviour acts and asks for a spot, the positioner picks the spot,
/// the navigator flies there and the evade layer bends that flight away from a predicted
/// hit. Everything the overlay and telemetry show is left on these objects after the tick.
/// </summary>
public sealed class Brain
{
    public Brain() => ProtectCompanionHomes.Reset();
    public readonly Senses Senses = new();
    /// <summary>The course owner: what the companion is doing and why, decided once per tick from one
    /// frozen observation. Since 22 September 2026 it is the brain's only decision surface — the family
    /// chooser that stood beside it for attribution went with `AIC-419`, and the three things it held
    /// that were never decisions are the three fields below.</summary>
    public readonly DecideCourseEachTick Course = new();
    /// <summary>The six jobs a course can bind. A registration, not a decision: see `RegisterActivities`.</summary>
    public readonly List<CompanionAction> Actions = RegisterActivities.All();
    /// <summary>Which of them holds the body, and the attempt lifecycle around it.</summary>
    public readonly OwnCurrentActivity Activity = new();
    /// <summary>How far apart the companion and the player are, and what that is costing. Observation only.</summary>
    public readonly ObserveCompanionship Companionship = new();
    /// <summary>
    /// The combat activity, held by name because the tick prepares it every frame to produce the priced
    /// attack front the course discovers shots from.
    ///
    /// Null when combat is not registered, which is a real case rather than a defensive nicety: fixtures
    /// register restricted activity sets on purpose — "only lighting and keeping company" is how the
    /// lighting rows prove a torch was placed by lighting rather than won by something else — and a
    /// lookup that threw there turned every one of those scenes into a crash inside the tick.
    /// </summary>
    public FightEnemies? Fighting => fighting ??= Actions.OfType<FightEnemies>().FirstOrDefault();
    private FightEnemies? fighting;
    public readonly Positioner Positioner = new();
    public readonly ChooseMeetingPlace Meeting = new();
    public readonly CoordinateMovement Movement = new();
    public Navigator Navigator => Movement.Navigator;
    public readonly Reflexes Reflexes = new();
    public readonly GrantActivityControls ControlGrants = new();
    public readonly ConsiderIncidentalInteractions Incidental = new();
    public readonly RecoverDistantCompanion FollowRecovery = new();
    public ActivitySnapshot Presentation { get; private set; }

    // The navigator names nothing of the game, so the failed-plan dump reaches the telemetry
    // through this seam; the replay tool leaves it unset.
    static Brain()
    {
        Navigator.PlanFailed = BrainTelemetry.DumpPlan;
        Navigator.PlanMsBudget = Weights.RouteSearchMilliseconds;
    }

    public PositionRequest LastRequest { get; private set; }
    public CompanionAction? LastAction => Activity.Current;
    public ulong LastTick { get; private set; } = ulong.MaxValue;
    public bool ChoiceEvaluated { get; private set; }
    public bool MovementStalled { get; private set; }
    public string ActivityStatus => FollowRecovery.Active ? "Catching up" : MovementStalled ? "Stuck: not making progress" : LastAction?.Name switch
    {
        "keep-company" => "Keeping company",
        "combat" => LastAction is FightEnemies stance && stance.ServesPlayerDirectly ? "Guarding you" : "Hunting",
        "mine" => "Mining ore", "chop" => "Chopping a tree", "collect" => "Collecting",
        "place-torches" => "Lighting the way", _ => "Resting"
    };
    private Vector2 progressOrigin;
    private int progressTicks;
    private Route? progressPath;
    private int progressStep;

    /// <summary>
    /// What each phase of the last tick cost, in milliseconds of wall-clock, so the lag has a
    /// column per suspect: the sixth run of 2026-09-08 was unplayable and the two searches
    /// already timed said forty percent of every second, which left the other sixty to guess.
    /// A phase that did not run this tick reads zero.
    /// </summary>
    public double SensesMs, ReflexMs, DecideMs, PositionMs, NavigateMs, FinaliseMs, TotalMs;
    private readonly System.Diagnostics.Stopwatch phase = new(), whole = new();

    /// <summary>
    /// How long the body has been sealed off from the player: counted from the first plan to a
    /// player-anchored spot that found nothing while the flood from the feet closed under its
    /// budget (a pocket the world seals, never a player who is merely far), ageing every tick
    /// after whatever the request, and cleared by the next such plan that finds anything or by
    /// the player's feet turning up inside the flood from the companion's own (a player who
    /// walked into the pocket and stopped beside it asks for no plan, and was stranded for ever
    /// on the plan rule alone: the Codex review of 2303802).
    /// </summary>
    public int StrandedTicks { get; private set; }

    /// <summary>
    /// An operation cap on the tick's own allowance. <c>long.MaxValue</c> in play, which is exactly what
    /// the tick has always had: the wall clock is production's only bound.
    ///
    /// It exists because the suite lifts every millisecond allowance, deliberately, so a fixture cannot
    /// end up timing the machine — and a decision that spans ticks is then unreachable, because the only
    /// thing that ends a tick's share of the work is the allowance running out. A row whose subject *is*
    /// a decision spanning ticks therefore needs an operation count, which is the reproducible half of
    /// <see cref="DecisionWorkBudget"/> and the half <c>G11</c>'s rows already cut on. Offline
    /// determinism only, in the same sense as <see cref="LimitPlanningWork.Unbounded"/> beside it, and a
    /// fixture that sets it puts it back.
    /// </summary>
    public static long PlanningOperationAllowance { get; set; } = long.MaxValue;

    /// <summary>
    /// This tick is one for walking the pocket: stranded long enough, and inside the roam part of
    /// the roam-then-retry cycle, whose retry part hands the body back to the follow for a few
    /// ticks so a plan to the player runs again and the count can clear.
    /// </summary>
    public bool Roaming
    {
        get
        {
            if (StrandedTicks < Weights.StrandedAfterTicks)
                return false;
            int cycle = (StrandedTicks - Weights.StrandedAfterTicks) % (Weights.RoamTicks + Weights.RoamRetryTicks);
            return cycle < Weights.RoamTicks;
        }
    }

    public void Tick(CompanionNPC companion, Terraria.Player player)
    {
        LastTick = Terraria.Main.GameUpdateCount;
        ChoiceEvaluated = false;
        whole.Restart();
        // The tick owns the allowance and hands back whatever was standing before it. In the game
        // nothing is, so this is the Begin/End pair it always was; under a harness that installs an
        // ambient allowance per case, a fixture driving a whole tick no longer leaves the rows after
        // it with nothing to borrow.
        LimitPlanningWork.Ownership allowance = LimitPlanningWork.Own(new DecisionWorkBudget(
            LimitPlanningWork.Unbounded ? double.PositiveInfinity : Weights.TotalPlanningMilliseconds,
            PlanningOperationAllowance));
        ReflexMs = DecideMs = PositionMs = NavigateMs = FinaliseMs = 0;
        try
        {
            ActivityControlRequest request = TickPhases(companion, player);
            FinaliseControls(companion, request);
        }
        finally
        {
            allowance.Dispose();
            TotalMs = whole.Elapsed.TotalMilliseconds;
        }
    }

    public void SuspendActivity(CompanionNPC companion, string reason)
        => Activity.Suspend(new ActionContext(companion, Senses, Roaming), reason);

    public void ApplyDownedControls(CompanionNPC companion)
    {
        SuspendActivity(companion, "downed");
        LastRequest = PositionRequest.Hold;
        FinaliseControls(companion, new ActivityControlRequest(Movement.Hold(companion.Motor.State, preemptedBy: "downed"), "downed", HandGrant.Unavailable));
    }

    private void FinaliseControls(CompanionNPC companion, ActivityControlRequest request)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        ActivityControlGrant grant = ControlGrants.Apply(companion, request, Activity);
        var ctx = new ActionContext(companion, Senses, Roaming);
        bool fired = Engage(companion, ctx, grant.Hand);
        // Only an ordinary execution tick: recovery and downed grants belong to responses that own the body for another purpose.
        if (request.ObserveProgress) Incidental.Consider(ctx, grant.Hand, fired, Activity.Current, Activity.Id);
        if (request.CountReunion) CountStranded();
        if (request.ObserveProgress)
        {
            WatchProgress(companion);
            Activity.ObserveOutcome(ctx);
        }
        Presentation = new ActivitySnapshot(Terraria.Main.GameUpdateCount, Activity.Id,
            Activity.Current?.Family, Activity.Current?.Name, Activity.Phase,
            companion.IsDowned, FollowRecovery.Active, MovementStalled);
        FinaliseMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private double Lap()
    {
        double ms = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        return ms;
    }

    private ActivityControlRequest TickPhases(CompanionNPC companion, Terraria.Player player)
    {
        phase.Restart();
        Senses.Update(companion.NPC, player);
        // The reach flood grows every tick; where it is rooted and when a
        // replacement takes over is decided on the positioner's rescore, which is where it is refreshed.
        Senses.Reach.Grow();
        ProtectCompanionHomes.Refresh(player.Bottom, companion.NPC.Center);
        companion.Combat.Tick();
        companion.Chopper.Tick();
        SensesMs = Lap();

        var ctx = new ActionContext(companion, Senses, Roaming);
        Senses.SetInterventionEstimate(companion.Combat.EstimateInterventionTicks(ctx));
        Companionship.Observe(ctx);

        if (FollowRecovery.Active && TryFollowRecovery(companion, player, false, out var initialRecovery)) return initialRecovery;

        // The reflex no longer takes the body: it names an imminent hit for the record and supplies the
        // predicate that the evade step bends the job's own controls against after navigation.
        Reflexes.TryAssess(companion.NPC, Senses, companion.Motor.State, out var unsafeAtTick);
        Navigator.UnsafeAtTick = Senses.Threats.Threats.Count == 0 && Senses.Projectiles.Threats.Count == 0 ? null : unsafeAtTick;
        ReflexMs = Lap();

        // Hazards before choose: combat's HereAndCompany walk and the company park both read the
        // same enemy boxes the route will, and a search that ran first saw last tick's boxes or none.
        var obstacles = new System.Collections.Generic.List<Rectangle>();
        foreach (var threat in Senses.Threats.Threats)
        {
            Rectangle box = threat.Npc.Hitbox;
            box.Inflate(24, 24);
            obstacles.Add(box);
        }
        Movement.SetObstacles(obstacles);

        // The course decides, and the activity performs. `DecideCourseEachTick` owns the whole decision:
        // one frozen observation, one discovery pass over all six domains, one bounded order search
        // priced by real consequences, and one published course whose next step this tick carries out.
        //
        // The family chooser this replaced is gone as of 22 September 2026 (`AIC-419`). It stayed compiled
        // and off this path for a day so a defect met in play could be attributed to one brain or the
        // other; the world run's play-measures instrument grades the course on the capture that motivated
        // it now, so the attribution no longer needs a second brain standing behind it.
        //
        // The bound step supplies the position request rather than the activity's own `Execute`. That is
        // the load-bearing half of the switch: a course's whole value is that the *course* decided where
        // to go and in what order, and letting the activity re-choose a site would put the post-grant
        // second chooser back — the one thing the plan names for deletion by name. The activity still
        // performs the work, the positioner still resolves the point, and the motor is still the only
        // writer to the body.
        // Combat's front is a tactical search rather than a world scan, so it is the one domain whose
        // opportunities do not exist until its own preparation has run. Every other source reads facts
        // the snapshot captures for it. Preparing combat unconditionally is the cost of letting the
        // course *choose* to fight rather than letting combat choose for itself: a shot the course never
        // saw is a shot it cannot weigh against mining the vein beside it.
        // Preparation is not part of choosing — it is how an activity works out what it would do, which
        // its own `Execute` then needs in order to have a target at all. Wiring the course to prepare only
        // combat left the other five unprepared, and the symptom was a lighting trip reporting
        // `offer=NoOpportunity/not-prepared` while keeping company executed instead: the course had named
        // lighting, and lighting had nothing to light because nobody had asked it to look.
        //
        // Combat is the one that must prepare before the decision rather than after it, because its
        // opportunities are a tactical search rather than a world scan and the course cannot weigh a shot
        // it never saw. The rest are prepared here too so that a chosen activity is always ready to act.
        //
        // **Narrowing this list was built, measured and taken out again on 22 September 2026, and what it
        // cost is a property rather than a number.** The plan's section 6 names the blanket as the family
        // chooser's cost model surviving inside the course brain's tick, and narrowing it to combat, the
        // activity holding the body and whatever the course still names took a retained tick from six
        // preparations to 2.05 on the ore-seam scene. It also turned `D4 Mimic after Disabled reads no
        // stale player hit` red with `NoOpportunity/no-admissible-tree`, because **an activity's offer is
        // refreshed by nothing but its own preparation**: chopping had answered under a disabled policy,
        // the policy was switched on, and nothing asked chopping to look again, so its offer described a
        // world that no longer existed. A rotation re-preparing every skipped activity once per turn of
        // the list did not save it either — the switch is read on the tick after it happens, so any
        // staleness bound above zero is too old. The honest form of section 6 is the one it actually
        // writes: the narrowing *moves into discovery*, where the censuses are already sliced and
        // budgeted against the frozen observation, rather than being skipped on the tick. That is a
        // design change rather than a condition on this loop, and it stays with the plan.
        foreach (CompanionAction candidate in Actions) candidate.Prepare(ctx);
        CourseDecision decision = Course.Decide(ctx, companion.Combat, Fighting?.LastSearch,
            LimitPlanningWork.Current);
        CompanionAction? action = decision.Activity.Length == 0 ? null
            : Actions.Find(candidate => candidate.Name == decision.Activity);
        Activity.Select(action, ctx);
        ChoiceEvaluated = true;
        Activity.BeginExecution();
        LastRequest = decision.Binding is { } step ? ExecuteCourseBinding.RequestFor(step, ctx.Npc.Center)
            // An unsettled decision may carry a continuation instead of a step — the fight the body is
            // already in, kept while the brain thinks rather than abandoned to keeping company, which is
            // the plan's tick-order step 5 and is what `DecideCourseEachTick.Deciding` owns.
            : decision.Request ?? // No step and nothing to continue is companionship rather than a hold,
                                  // because a course that found nothing worth doing must not look
                                  // identical to a course that told the body to freeze.
                ExecuteCourseBinding.Companionship(Senses.Intent.Region.Centre);
        // The activity still runs its own tick, for the hand it reserves and the state it keeps; its
        // returned request is discarded, because the course already said where the body goes.
        _ = action?.Execute(ctx);
        DecideMs = Lap();
        // The rule lives in `RecoverDistantCompanion.ReunionRequested`, which owns why each of its three
        // conditions is there. It is a named predicate rather than an expression here so that it can be
        // checked without standing up a whole scene.
        bool reunionRequested = RecoverDistantCompanion.ReunionRequested(
            LastRequest.Kind, action?.HandsBusy == true, decision.Settled);
        if (TryFollowRecovery(companion, player, reunionRequested, out var selectedRecovery)) return selectedRecovery;

        Vector2? spot = Positioner.Resolve(LastRequest, Senses);
        PositionMs = Lap();
        Controls movement;
        string movementOwner;
        try
        {
            movement = Navigate(companion, spot, out movementOwner);
            // Safety on top of the job: whatever the job asked for, bent away from a predicted hit when following it
            // would meet one. The job keeps running and keeps its attempt and its hands; only the tick's direction
            // changes, and the grant names the tick so the record can tell a bent tick from an ordinary one.
            movement = Movement.Evade(companion.Motor.State, movement, Navigator.UnsafeAtTick, out bool bent);
            if (bent) movementOwner = "evade";
        }
        finally
        {
            NavigateMs = Lap();
        }
        return new ActivityControlRequest(movement, movementOwner,
            action?.HandsBusy == true ? HandGrant.WorkTool : HandGrant.Available,
            ObserveProgress: true, CountReunion: true);
    }

    private bool TryFollowRecovery(CompanionNPC companion, Terraria.Player player, bool mayStart, out ActivityControlRequest request)
    {
        request = default;
        // A clear arrival is the orb out of every tile and no lower than the player's own centre, which
        // is where following hovers anyway; a body that phased in below him would have to climb back out.
        if (!FollowRecovery.Update(mayStart, companion.IsDowned, !player.dead && player.active,
            companion.NPC.Center, player.Center, companion.Motor.ClearOfTerrain
                && player.velocity.Y == 0f && companion.NPC.Center.Y <= player.Center.Y)) return false;
        LastRequest = new PositionRequest(RequestKind.WithPlayer, player.Bottom);
        Activity.Suspend(new ActionContext(companion, Senses, Roaming), "follow-recovery-flight");
        Movement.Hold(companion.Motor.State, preemptedBy: "follow-recovery-flight");
        request = new ActivityControlRequest(Controls.None, "follow-recovery-flight", RecoveryVelocity:
            FollowRecovery.Steer(companion.NPC.Center, companion.NPC.velocity, player.Center, player.velocity));
        NavigateMs = Lap();
        return true;
    }

    /// <summary>
    /// The hands, run on a tick combat is the running activity and quiet on every other. Firing
    /// used to live here unconditionally — attacking had once lived inside three actions, which made
    /// the companion incapable of shooting while following, looting or working, so firing left the
    /// actions for the tick. The combat stance reverses that: fighting is one activity's decision
    /// again, and the hands fire only while it runs, because a body mining a vein is not fighting.
    ///
    /// It runs after Navigate on purpose. The motor has already been told where to go, so facing
    /// the target wins the tick and the body aims where it shoots while walking somewhere else,
    /// which is what a player looks like. The hands stay out of it while they are driving a tool,
    /// because a swing and a throw cannot share the same arm.
    /// </summary>
    private bool Engage(CompanionNPC companion, in ActionContext ctx, HandGrant hand)
    {
        // The final grant owns compatibility, including early safety and downed paths. Tool
        // phases reserve the hand across cooldown gaps; travelling to that work leaves it free.
        if (hand != HandGrant.Available)
        {
            EngageTarget = null;
            companion.Combat.NoteHandsBusy();
            return false;
        }
        if (Activity.Current is not FightEnemies fight)
        {
            EngageTarget = null;
            companion.Combat.NoteNotFighting();
            return false;
        }
        // Whether the arm was used this tick: a grant that leaves the hand Available permits a shot, and one arm cannot also break a pot.
        EngageTarget = fight.CommittedPlanTarget;
        return companion.Combat.Hands.Fire(ctx, companion.Combat, fight.CommittedPlan);
    }

    /// <summary>What the hands are shooting at on a tick combat runs; null on every other tick.</summary>
    public Terraria.NPC? EngageTarget { get; private set; }

    private void CountStranded()
    {
        bool towardPlayer = LastRequest.Kind is RequestKind.WithPlayer or RequestKind.FireFrom;
        if (StrandedTicks > 0 && Positioner.Reaches(MovementQueries.FeetTile(Senses.Player.Bottom)))
            StrandedTicks = 0;
        else if (towardPlayer && Navigator.PlannedThisTick)
            StrandedTicks = Navigator.LastPlanEmpty && Navigator.LastSearchStop == FreeSpaceSearch.StopReason.Exhausted
                && Positioner.ReachComplete ? System.Math.Max(StrandedTicks, 1) : 0;
        else if (StrandedTicks > 0)
            StrandedTicks++;
    }

    private void WatchProgress(CompanionNPC companion)
    {
        Vector2 centre = companion.NPC.Center;
        if (progressTicks == 0)
        {
            progressOrigin = centre;
            progressPath = Navigator.Path;
            progressStep = progressPath?.Index ?? 0;
        }
        bool wantsTravel = LastRequest.Kind == RequestKind.WithPlayer
            ? !Senses.Intent.Objective.IsSatisfied(centre)
            : LastRequest.Kind != RequestKind.Hold && (Positioner.Chosen is not Vector2 spot
                || Vector2.DistanceSquared(spot, centre) > Navigator.SettleRadius * Navigator.SettleRadius);
        if (!wantsTravel)
        {
            progressOrigin = centre; progressTicks = 0; MovementStalled = false;
            progressPath = Navigator.Path; progressStep = progressPath?.Index ?? 0;
            return;
        }
        if (MovementStalled && Vector2.DistanceSquared(progressOrigin, centre) >= Weights.ObjectiveProgressPixels * Weights.ObjectiveProgressPixels)
            MovementStalled = false;
        if (++progressTicks >= Weights.ObjectiveProgressWindowTicks)
        {
            // Net displacement across a window catches stationary holds and local oscillation;
            // it makes no claim that moving away from the player is a bad route detour.
            bool advancedRoute = progressPath != null && ReferenceEquals(progressPath, Navigator.Path) && progressPath.Index > progressStep;
            MovementStalled = !advancedRoute && Vector2.DistanceSquared(progressOrigin, centre) < Weights.ObjectiveProgressPixels * Weights.ObjectiveProgressPixels;
            progressOrigin = centre; progressTicks = 0;
            progressPath = Navigator.Path; progressStep = progressPath?.Index ?? 0;
        }
    }

    private Controls Navigate(CompanionNPC companion, Vector2? spot, out string owner)
    {
        if (LastRequest.Kind == RequestKind.WithPlayer && Senses.Intent.Inside)
        {
            // Inside the player's region the companion is with him, so there is nowhere to go: the body moves about the region
            // with the region. A journey that was open ends here as reached, because being inside is what it was for; nothing
            // opens a new one, so a minute of company is not counted as thousands of asks.
            BehaviourCensus.RequestReached();
            BehaviourCensus.RequestEnded();
            owner = "accompany";
            var region = Senses.Intent.Region;
            Rectangle? footprint = Senses.Player.Interference;
            // Refused: the tiles the player is building on or walking down. The target is not asked about the reach flood as well.
            // The flood holds lattice corners, and a place pressed against a wall can have none the circle fits at while the
            // circle can still sweep straight to it; the walk is continuous and swept clear from its last target, which already
            // keeps it inside the free space connected to the body, and that is what being held by the flood was for.
            // The floor the tour draws its legs above is the top of his head, derived from the sense's own two points
            // rather than from a constant, so a player whose height changes carries it with him. The region still holds
            // him — its lower part is level with his legs, which is what put the tour at his shins — and the floor is
            // the narrow answer to that: it bounds where the walk *aims*, and nothing else. Lifting the region itself
            // was built and reverted on 22 September, because `Region.Accepts` is also combat's stand admission and a
            // lifted region cannot admit a melee stand beside a hostile standing where he stands.
            float head = Senses.Player.Position.Y - (Senses.Player.Bottom.Y - Senses.Player.Position.Y);
            return Movement.Accompany(companion.Motor.State, region.Centre, region.HalfSize, region.Lead, head,
                point => footprint is Rectangle asked && Infrastructure.Observation.PlayerSense.BodyTiles(point + new Vector2(0f, CircleContact.Radius),
                    (int)CircleContact.Diameter, (int)CircleContact.Diameter).Intersects(asked));
        }
        if (spot is Vector2 goal)
        {
            // One continuous stretch of wanting one kind of place is one ask, so the census reads
            // "asked for 40 places, reached 11" rather than counting a minute of following as
            // 3,600 requests. The kind is the episode's identity because the exact point moves
            // under a request that has not changed.
            BehaviourCensus.RequestBegan(LastRequest.Kind.ToString());
            owner = "travel";
            Controls controls = Movement.MoveTo(companion.Motor.State, goal);
            // Stuck twice on the way to one spot: the first strike replanned and the replan found
            // nothing better, so the spot itself is the problem. Refuse it for a while and let the
            // positioner answer with another, which is Caner's "choose a different position to
            // unstick itself" (2026-09-08).
            // A spot the navigator's search has proven unreachable is refused the same way and at once, because asking the
            // same place again from here can only prove the same absence.
            if (Navigator.StuckStrikes >= 2 || Navigator.GoalProvenUnreachable)
            {
                Positioner.Ban(MovementQueries.Tile(goal), Weights.StuckSpotBanTicks);
                Navigator.ResetStrikes();
            }
            return controls;
        }
        else if (LastRequest.Kind == RequestKind.WithPlayer && !Positioner.FollowObjectiveSatisfied)
        {
            var objective = Senses.Intent.Objective.At(LastRequest.Anchor);
            BehaviourCensus.RequestBegan(LastRequest.Kind.ToString());
            owner = "seeking-destination";
            // Being inside the region is the whole of arrival; losing sight of the player is not distance.
            return Movement.SeekDestination(companion.Motor.State, LastRequest.Anchor, centre => objective.IsSatisfied(centre));
        }
        else
        {
            owner = "hold";
            // The brain's hold is a hover around where the hold began, never a brake: the orb is never strictly
            // standing still. Downing and recovery flight still ask Movement.Hold for nothing, because they own the
            // body for reasons that are not a place to be.
            Controls controls = Movement.HoverHere(companion.Motor.State);
            // Nothing is being asked for, so whatever was being asked for is over: reached if the
            // navigator got there, abandoned otherwise. A Hold request is the ordinary way an
            // episode ends, which is why this is not treated as a failure.
            BehaviourCensus.RequestEnded();
            return controls;
        }
    }
}
