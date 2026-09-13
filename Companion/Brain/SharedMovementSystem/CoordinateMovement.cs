#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// The movement entry point for the brain. It owns the navigator's retained route and local
/// repair state, so callers hand it a live body and intent rather than manipulating controls,
/// grid state or planner flags independently.
/// </summary>
public sealed class CoordinateMovement
{
    public Navigator Navigator { get; } = new();
    private readonly SearchControlSequences stateSearch = new();
    private Vector2? unresolvedGoal;
    private bool seekingDestination;
    public bool StateSearchPending => stateSearch.Pending;
    public int StateSearchRetainedTicks => stateSearch.RetainedTicks;
    public void CancelStateSearch()
    {
        stateSearch.Clear();
        unresolvedGoal = null;
        seekingDestination = false;
    }

    public bool SeekState(BodyState live, Func<BodyState, bool> goal, Func<BodyState, float> heuristic,
        int workBudget, out Controls controls, out bool pending)
    {
        if (seekingDestination) CancelStateSearch();
        // A state objective (escape, combat space) is another owner taking the body from any route.
        Navigator.Interrupt(live, AttemptEnding.Preempted, "state-search");
        bool chosen = stateSearch.TryChoose(NavGrid.World, live, goal, heuristic, Navigator.Capabilities,
            workBudget, BehaviourSelection.Weights.EscapeSearchMilliseconds, out controls, Navigator.UnsafeAtTick);
        pending = stateSearch.Pending;
        return chosen;
    }

    public Controls MoveTo(BodyState live, Vector2 goal, float requestedJump = 0f)
    {
        CancelStateSearch();
        return AddRequestedJump(Navigator.MoveTo(live, goal), requestedJump);
    }

    /// <param name="preemptedBy">Null when the brain released the request itself; otherwise the owner
    /// that took the body (downing, recovery flight), so the interrupted attempt is scored as
    /// pre-empted rather than cancelled.</param>
    public Controls Hold(BodyState live, float requestedJump = 0f, string? preemptedBy = null)
    {
        CancelStateSearch();
        // Releasing the movement request interrupts the retained route explicitly, including
        // its census outcome. Survival can request a ground jump through the same body rules.
        Navigator.Interrupt(live, preemptedBy == null ? AttemptEnding.Cancelled : AttemptEnding.Preempted, preemptedBy ?? "released");
        return AddRequestedJump(Controls.None, requestedJump);
    }

    /// <summary>A missing stand does not cancel a travel intention. Search legal body states
    /// while position selection continues, retaining only physically validated prefixes.</summary>
    public Controls SeekDestination(BodyState live, Vector2 anchor, Func<BodyState, bool> arrived)
    {
        if (!seekingDestination || unresolvedGoal is not Vector2 held || Vector2.DistanceSquared(held, anchor) > 32f * 32f)
        {
            stateSearch.Clear();
            unresolvedGoal = anchor;
        }
        seekingDestination = true;
        Vector2 target = unresolvedGoal.Value;
        // The same purpose changing method (route to state search) is a voluntary cancellation.
        Navigator.Interrupt(live, AttemptEnding.Cancelled, "method-change");
        stateSearch.TryChoose(NavGrid.World, live, arrived,
            state => Vector2.Distance(state.Feet, target), Navigator.Capabilities,
            BehaviourSelection.Weights.EscapeSearchWork, BehaviourSelection.Weights.EscapeSearchMilliseconds,
            out Controls controls, Navigator.UnsafeAtTick);
        return controls;
    }

    public Controls AvoidThreats(BodyState live, Func<BodyState, int, bool> unsafeAtTick, Vector2 goal)
    {
        return Navigator.AvoidThreats(live, unsafeAtTick, goal);
    }

    public void Configure(uint clock, bool allowLava, bool allowOneWayDrops)
    {
        AStar.Clock = clock;
        AStar.AllowLava = allowLava;
        AStar.AllowOneWayDrops = allowOneWayDrops;
    }

    public void SetObstacles(System.Collections.Generic.IEnumerable<Rectangle> obstacles)
    {
        AStar.Avoid.Clear();
        foreach (Rectangle obstacle in obstacles)
            AStar.Avoid.Add(obstacle);
    }

    private Controls AddRequestedJump(Controls controls, float requestedJump)
    {
        // A traversal's run-up/flight controls are committed macro state. A behaviour-level jump
        // is a floor for an uncommitted body only, otherwise a drowning bob can restart every
        // running jump and recreate the cadence loop this component exists to prevent.
        if (requestedJump <= 0f || Navigator.Path is { Finished: false })
            return controls;
        return controls with { Jump = true, JumpScale = requestedJump };
    }
}
