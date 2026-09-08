#nullable enable

using Microsoft.Xna.Framework;
using AICompanion.Brain.Actions;
using AICompanion.Brain.DecisionMatrix;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Brain.DecisionMatrix.Positioning;
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
    public readonly DecisionMatrix.Senses.Senses Senses = new();
    public readonly Chooser Chooser = new();
    public readonly Positioner Positioner = new();
    public readonly Navigator Navigator = new();
    public readonly DecisionMatrix.Reflexes.Reflexes Reflexes = new();

    // The navigator names nothing of the game, so the failed-plan dump reaches the telemetry
    // through this seam; the replay tool leaves it unset.
    static Brain() => Navigator.PlanFailed = Debug.BrainTelemetry.DumpPlan;

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
        AStar.Clock = Terraria.Main.GameUpdateCount;
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

        bool taken = Reflexes.TryTake(companion.NPC, companion.Motor, Senses);
        ReflexMs = Lap();
        if (taken)
            return;

        var ctx = new ActionContext(companion, Senses, Roaming);
        CompanionAction action = Chooser.Choose(ctx);
        LastRequest = action.Execute(ctx);
        DecideMs = Lap();

        var profile = companion.Arsenal.ProfileFor(ctx, LastRequest.Target);
        // Lava is a crossable cost only while there is life to pay it with.
        AStar.AllowLava = Senses.Self.LifeFraction > 0.6f && !Senses.Self.InLava;
        // A drop with no way back is taken only after the player, or to save the body: a hunt
        // that dropped into a sealed pocket after its target stood at the rim for the rest of
        // run 5 (2026-09-08). This gates the planner alone; the positioner's reach flood answers
        // the same question for itself and against the player's tile rather than the request kind.
        AStar.AllowOneWayDrops = LastRequest.Kind is RequestKind.WithPlayer or RequestKind.Guard || action is Actions.Survival.SurviveAction;
        Vector2? spot = Positioner.Resolve(LastRequest, Senses, profile);
        PositionMs = Lap();
        // Reachable enemies are priced like lava on the route, so a path to a spot beyond one
        // goes round it rather than through it.
        AStar.Avoid.Clear();
        foreach (var threat in Senses.Threats.Threats)
        {
            if (!threat.Reachable)
                continue;
            Rectangle box = threat.Npc.Hitbox;
            box.Inflate(24, 24);
            AStar.Avoid.Add(box);
        }
        try
        {
            Navigate(companion, spot);
        }
        finally
        {
            NavigateMs = Lap();
        }
        CountStranded();
    }

    private void CountStranded()
    {
        bool towardPlayer = LastRequest.Kind is RequestKind.WithPlayer or RequestKind.Guard;
        if (StrandedTicks > 0 && Positioner.Reaches(NavGrid.FeetTile(Senses.Player.Bottom)))
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
            companion.Motor.Apply(Navigator.MoveTo(companion.Motor.State, feet));
            // Stuck twice on the way to one spot: the first strike priced the step and the replan
            // found nothing better, so the spot itself is the problem. Refuse it for a while and
            // let the positioner answer with another, which is Caner's "choose a different
            // position to unstick itself" (2026-09-08).
            if (Navigator.StuckStrikes >= 2)
            {
                Positioner.Ban(NavGrid.FeetTile(feet), Weights.StuckSpotBanTicks);
                Navigator.ResetStrikes();
            }
        }
        else
        {
            companion.Motor.Stop();
            Navigator.Clear();
        }
    }
}
