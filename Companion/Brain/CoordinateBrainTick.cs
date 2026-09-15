#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
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
/// The tick order of the companion's mind: senses read the world, shared safety may
/// take the body, the chooser picks a family offer, the behaviour acts and asks for
/// a spot, the positioner picks the spot, the navigator walks there. Everything the
/// overlay and telemetry show is left on these objects after the tick.
/// </summary>
public sealed class Brain
{
    public Brain() => ProtectCompanionHomes.Reset();
    public readonly Senses Senses = new();
    public readonly Chooser Chooser = new();
    public readonly Positioner Positioner = new();
    public readonly ChooseMeetingPlace Meeting = new();
    public readonly CoordinateMovement Movement = new();
    public Navigator Navigator => Movement.Navigator;
    public readonly Reflexes Reflexes = new();
    public readonly GrantActivityControls ControlGrants = new();
    public readonly ConsiderIncidentalInteractions Incidental = new();
    public readonly ChooseSafetyResponse Safety = new();
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
    public CompanionAction? LastAction => Chooser.Current;
    public ulong LastTick { get; private set; } = ulong.MaxValue;
    public bool ChoiceEvaluated { get; private set; }
    public bool MovementStalled { get; private set; }
    public string ActivityStatus => FollowRecovery.Active ? "Catching up" : Safety.Active ? "Getting to safety" : MovementStalled ? "Stuck: not making progress" : LastAction?.Name switch
    {
        "keep-company" => "Keeping company", "guard" => "Guarding you", "hunt" => "Hunting",
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
        LimitPlanningWork.Begin(Weights.TotalPlanningMilliseconds);
        ReflexMs = DecideMs = PositionMs = NavigateMs = FinaliseMs = 0;
        // Which liquids are walls this tick is the body's own immunity and nothing else: a liquid the
        // body is not immune to hurts on touch, so no life fraction makes it a crossable cost.
        Movement.Configure(companion.Motor.Immunity);
        try
        {
            ActivityControlRequest request = TickPhases(companion, player);
            FinaliseControls(companion, request);
        }
        finally
        {
            LimitPlanningWork.End();
            TotalMs = whole.Elapsed.TotalMilliseconds;
        }
    }

    public void SuspendActivity(CompanionNPC companion, string reason)
        => Chooser.Activity.Suspend(new ActionContext(companion, Senses, Roaming), reason);

    public void ApplyDownedControls(CompanionNPC companion)
    {
        Safety.Cancel(new ActionContext(companion, Senses, Roaming), "downed");
        SuspendActivity(companion, "downed");
        LastRequest = PositionRequest.Hold;
        FinaliseControls(companion, new ActivityControlRequest(Movement.Hold(companion.Motor.State, preemptedBy: "downed"), "downed", HandGrant.Unavailable));
    }

    private void FinaliseControls(CompanionNPC companion, ActivityControlRequest request)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        ActivityControlGrant grant = ControlGrants.Apply(companion, request, Chooser.Activity);
        var ctx = new ActionContext(companion, Senses, Roaming);
        bool fired = Engage(companion, ctx, grant.Hand);
        // Only an ordinary execution tick: safety, recovery and downed grants belong to responses that own the body for another purpose.
        if (request.ObserveProgress) Incidental.Consider(ctx, grant.Hand, fired, Chooser.Current, Chooser.Activity.Id);
        if (request.CountReunion) CountStranded();
        if (request.ObserveProgress)
        {
            WatchProgress(companion);
            Chooser.Activity.ObserveOutcome(ctx);
        }
        Presentation = new ActivitySnapshot(Terraria.Main.GameUpdateCount, Chooser.Activity.Id,
            Chooser.Current?.Family, Chooser.Current?.Name, Chooser.Activity.Phase,
            companion.IsDowned, FollowRecovery.Active, Safety.Active, MovementStalled);
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
        Senses.Update(companion.NPC, player, companion.Motor);
        ProtectCompanionHomes.Refresh(player.Bottom, companion.NPC.Center);
        companion.Arsenal.Tick();
        companion.Chopper.Tick();
        SensesMs = Lap();

        var ctx = new ActionContext(companion, Senses, Roaming);
        Senses.SetInterventionEstimate(companion.Arsenal.EstimateInterventionTicks(ctx));
        Chooser.ObserveCompanionship(ctx);

        if (FollowRecovery.Active && TryFollowRecovery(companion, player, false, out var initialRecovery)) return initialRecovery;

        bool taken = Reflexes.TryAssess(companion.NPC, Senses, companion.Motor.State, out var unsafeAtTick);
        Navigator.UnsafeAtTick = Senses.Threats.Threats.Count == 0 && Senses.Projectiles.Threats.Count == 0 ? null : unsafeAtTick;
        ReflexMs = Lap();
        if (Safety.TryChoose(ctx, taken, out var safetyRequest))
        {
            LastRequest = PositionRequest.Hold;
            ReflexMs += Lap();
            return safetyRequest;
        }

        CompanionAction? action = Chooser.Choose(ctx);
        ChoiceEvaluated = true;
        Chooser.Activity.BeginExecution();
        LastRequest = action?.Execute(ctx) ?? PositionRequest.Hold;
        DecideMs = Lap();
        // Recovery serves an explicit reunion objective, never an executor's class or a
        // coincidentally player-adjacent work destination. An occupied tool cannot start it.
        bool reunionRequested = LastRequest.Kind == RequestKind.WithPlayer && action?.HandsBusy != true;
        if (TryFollowRecovery(companion, player, reunionRequested, out var selectedRecovery)) return selectedRecovery;

        var profile = companion.Arsenal.ProfileFor(ctx, LastRequest.Target);
        Vector2? spot = Positioner.Resolve(LastRequest, Senses, profile);
        PositionMs = Lap();
        // Enemy bodies are hazards wherever the route passes them, even when neither actor
        // is currently reachable from the enemy's pocket.
        var obstacles = new System.Collections.Generic.List<Rectangle>();
        foreach (var threat in Senses.Threats.Threats)
        {
            Rectangle box = threat.Npc.Hitbox;
            box.Inflate(24, 24);
            obstacles.Add(box);
        }
        Movement.SetObstacles(obstacles);
        Controls movement;
        string movementOwner;
        try
        {
            movement = Navigate(companion, spot, out movementOwner);
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
        Safety.Cancel(new ActionContext(companion, Senses, Roaming), "follow-recovery-flight");
        Chooser.Activity.Suspend(new ActionContext(companion, Senses, Roaming), "follow-recovery-flight");
        Movement.Hold(companion.Motor.State, preemptedBy: "follow-recovery-flight");
        request = new ActivityControlRequest(Controls.None, "follow-recovery-flight", RecoveryVelocity:
            FollowRecovery.Steer(companion.NPC.Center, companion.NPC.velocity, player.Center, player.velocity));
        NavigateMs = Lap();
        return true;
    }

    /// <summary>
    /// The hands, run every tick whatever the feet were told. A player does not choose between
    /// walking and shooting and neither does this: attacking used to live inside three actions —
    /// hunt, guard and kite — which meant the companion was literally incapable of shooting while
    /// following the player, looting, wandering or working, and "it should be attacking things
    /// regardless" was impossible to satisfy by any amount of scoring. So firing left the actions
    /// and came here, and hunting is now only the decision to walk toward something rather than
    /// the decision to fight at all.
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
            companion.Arsenal.NoteHandsBusy();
            return false;
        }
        EngageTarget = companion.Arsenal.BestTarget(ctx);
        // Whether the arm was used this tick: a grant that leaves the hand Available permits a shot, and one arm cannot also break a pot.
        return companion.Arsenal.TryFire(ctx, EngageTarget);
    }

    /// <summary>What the hands are shooting at, independent of what the feet were told to do.</summary>
    public Terraria.NPC? EngageTarget { get; private set; }

    private void CountStranded()
    {
        bool towardPlayer = LastRequest.Kind is RequestKind.WithPlayer or RequestKind.Guard;
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
        bool wantsTravel = Safety.Active
            || (LastRequest.Kind == RequestKind.WithPlayer
            ? !Senses.Intent.Objective.IsSatisfied(centre, LineOfSight.Between(companion.NPC, Senses.PlayerEntity))
            : LastRequest.Kind != RequestKind.Hold && (Positioner.Chosen is not Vector2 spot
                || Vector2.DistanceSquared(spot, centre) > 16f * 16f));
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
            if (Navigator.StuckStrikes >= 2)
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
            Terraria.Player player = Senses.PlayerEntity;
            return Movement.SeekDestination(companion.Motor.State, LastRequest.Anchor,
                centre => objective.IsSatisfied(centre,
                    Terraria.Collision.CanHitLine(centre - new Vector2(CircleContact.Radius),
                        (int)CircleContact.Diameter, (int)CircleContact.Diameter, player.position, player.width, player.height)));
        }
        else
        {
            owner = "hold";
            Controls controls = Movement.Hold(companion.Motor.State);
            // Nothing is being asked for, so whatever was being asked for is over: reached if the
            // navigator got there, abandoned otherwise. A Hold request is the ordinary way an
            // episode ends, which is why this is not treated as a failure.
            BehaviourCensus.RequestEnded();
            return controls;
        }
    }
}
