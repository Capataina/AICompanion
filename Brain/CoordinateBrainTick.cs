#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Brain.Behaviours;
using AICompanion.Brain.BehaviourSelection;
using AICompanion.Brain.SharedMovementSystem;
using AICompanion.Brain.PositionSelection;
using AICompanion.Companion;

namespace AICompanion.Brain;

/// <summary>
/// The tick order of the companion's mind: senses read the world, reflexes may take
/// the body, the chooser picks an action, the action acts and asks for a spot, the
/// positioner picks the spot, the navigator walks there. Everything the overlay and
/// telemetry show is left on these objects after the tick.
/// </summary>
public sealed class Brain
{
    public readonly WorldObservation.Senses Senses = new();
    public readonly Chooser Chooser = new();
    public readonly Positioner Positioner = new();
    public readonly CoordinateMovement Movement = new();
    public Navigator Navigator => Movement.Navigator;
    public readonly CombatReflexes.Reflexes Reflexes = new();

    // The navigator names nothing of the game, so the failed-plan dump reaches the telemetry
    // through this seam; the replay tool leaves it unset.
    static Brain() => Navigator.PlanFailed = BehaviourDiagnostics.BrainTelemetry.DumpPlan;

    public PositionRequest LastRequest { get; private set; }
    public CompanionAction? LastAction => Chooser.Current;

    /// <summary>
    /// What each phase of the last tick cost, in milliseconds of wall-clock, so the lag has a
    /// column per suspect: the sixth run of 2026-09-08 was unplayable and the two searches
    /// already timed said forty percent of every second, which left the other sixty to guess.
    /// A phase that did not run this tick reads zero.
    /// </summary>
    public double SensesMs, ReflexMs, DecideMs, PositionMs, NavigateMs, TotalMs;
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
        whole.Restart();
        ReflexMs = DecideMs = PositionMs = NavigateMs = 0;
        Movement.Configure(Terraria.Main.GameUpdateCount, Senses.Self.LifeFraction > 0.6f, false);
        try
        {
            TickPhases(companion, player);
        }
        finally
        {
            TotalMs = whole.Elapsed.TotalMilliseconds;
        }
    }

    private double Lap()
    {
        double ms = phase.Elapsed.TotalMilliseconds;
        phase.Restart();
        return ms;
    }

    private void TickPhases(CompanionNPC companion, Terraria.Player player)
    {
        phase.Restart();
        Senses.Update(companion.NPC, player, companion.Breath);
        companion.Arsenal.Tick();
        companion.Chopper.Tick();
        SensesMs = Lap();

        var ctx = new ActionContext(companion, Senses, Roaming);

        Navigator.Capabilities = companion.Motor.Capabilities;
        bool taken = Reflexes.TryAssess(companion.NPC, Senses, companion.Motor.State, out var unsafeAtTick);
        ReflexMs = Lap();
        if (taken)
        {
            companion.Motor.Apply(Movement.AvoidThreats(companion.Motor.State, unsafeAtTick, Senses.Player.Bottom), "combat-reflex");
            // A reflex takes the *feet*, and this used to return before the hands ran, which
            // quietly contradicted the contract Engage is written under: the weapon fires every
            // tick whatever the feet were told. A dodge is exactly when there is most worth
            // shooting at, and the dodge itself is a jump or a step — it wants the legs, never the
            // arm — so the hands have no reason to stop. The 2026-09-09 combat session spent 153
            // ticks inside reflexes with the fire column frozen on whatever it last read, which is
            // both a lost shot and a lying column.
            Engage(companion, ctx, null);
            return;
        }

        CompanionAction action = Chooser.Choose(ctx);
        LastRequest = action.Execute(ctx);
        DecideMs = Lap();

        var profile = companion.Arsenal.ProfileFor(ctx, LastRequest.Target);
        // Lava is a crossable cost only while there is life to pay it with.
        // A drop with no way back is taken only after the player, or to save the body: a hunt
        // that dropped into a sealed pocket after its target stood at the rim for the rest of
        // run 5 (2026-09-08). This gates the planner alone; the positioner's reach flood answers
        // the same question for itself and against the player's tile rather than the request kind.
        Movement.Configure(Terraria.Main.GameUpdateCount,
            Senses.Self.LifeFraction > 0.6f && !Senses.Self.InLava,
            LastRequest.Kind is RequestKind.WithPlayer or RequestKind.Guard || action is Behaviours.Survival.SurviveAction);
        Vector2? spot = Positioner.Resolve(LastRequest, Senses, profile);
        PositionMs = Lap();
        // Reachable enemies are priced like lava on the route, so a path to a spot beyond one
        // goes round it rather than through it.
        var obstacles = new System.Collections.Generic.List<Rectangle>();
        foreach (var threat in Senses.Threats.Threats)
        {
            if (!threat.Reachable)
                continue;
            Rectangle box = threat.Npc.Hitbox;
            box.Inflate(24, 24);
            obstacles.Add(box);
        }
        Movement.SetObstacles(obstacles);
        try
        {
            Navigate(companion, spot);
        }
        finally
        {
            NavigateMs = Lap();
        }
        Engage(companion, ctx, action);
        CountStranded();
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
    private void Engage(CompanionNPC companion, in ActionContext ctx, CompanionAction? action)
    {
        // An axe or a pickaxe occupies the arm the throw needs, so working is the one thing that
        // stops the hands. Everything else — following, guarding, kiting, looting, wandering,
        // saving itself from drowning — shoots. A null action is a reflex tick: nothing was
        // chosen, so nothing can be holding a tool, and the hands are free by construction.
        if (action?.Name is "chop" or "mine")
        {
            EngageTarget = null;
            companion.Arsenal.NoteHandsBusy();
            return;
        }
        EngageTarget = companion.Arsenal.BestTarget(ctx);
        companion.Arsenal.TryFire(ctx, EngageTarget);
    }

    /// <summary>What the hands are shooting at, independent of what the feet were told to do.</summary>
    public Terraria.NPC? EngageTarget { get; private set; }

    private void CountStranded()
    {
        bool towardPlayer = LastRequest.Kind is RequestKind.WithPlayer or RequestKind.Guard;
        if (StrandedTicks > 0 && Positioner.Reaches(MovementQueries.FeetTile(Senses.Player.Bottom)))
            StrandedTicks = 0;
        else if (towardPlayer && Navigator.PlannedThisTick)
            StrandedTicks = Navigator.LastPlanEmpty && Positioner.ReachComplete ? System.Math.Max(StrandedTicks, 1) : 0;
        else if (StrandedTicks > 0)
            StrandedTicks++;
    }

    private void Navigate(CompanionNPC companion, Vector2? spot)
    {
        if (spot is Vector2 feet)
        {
            // One continuous stretch of wanting one kind of place is one ask, so the census reads
            // "asked for 40 places, reached 11" rather than counting a minute of following as
            // 3,600 requests. The kind is the episode's identity because the exact tile moves
            // under a request that has not changed.
            BehaviourCensus.RequestBegan(LastRequest.Kind.ToString());
            companion.Motor.Apply(Movement.MoveTo(companion.Motor.State, feet, LastRequest.JumpScale), "travel");
            // Stuck twice on the way to one spot: the first strike priced the step and the replan
            // found nothing better, so the spot itself is the problem. Refuse it for a while and
            // let the positioner answer with another, which is Caner's "choose a different
            // position to unstick itself" (2026-09-08).
            if (Navigator.StuckStrikes >= 2)
            {
                Positioner.Ban(MovementQueries.FeetTile(feet), Weights.StuckSpotBanTicks);
                Navigator.ResetStrikes();
            }
        }
        else
        {
            companion.Motor.Apply(Movement.Hold(companion.Motor.State, LastRequest.JumpScale), "hold");
            // Nothing is being asked for, so whatever was being asked for is over: reached if the
            // navigator got there, abandoned otherwise. A Hold request is the ordinary way an
            // episode ends, which is why this is not treated as a failure.
            BehaviourCensus.RequestEnded();
        }
    }
}
