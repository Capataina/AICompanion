#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>
/// The movement entry point for the brain. It owns the navigator's retained route and local
/// repair state, so callers hand it a live body and intent rather than manipulating controls,
/// grid state or planner flags independently.
/// </summary>
public sealed class CoordinateMovement
{
    public Navigator Navigator { get; } = new();

    public Controls MoveTo(BodyState live, Vector2 goal, float requestedJump = 0f)
    {
        Navigator.UnsafeAtTick = null;
        return AddRequestedJump(Navigator.MoveTo(live, goal), requestedJump);
    }

    public Controls Hold(BodyState live, float requestedJump = 0f)
    {
        // Releasing the movement request interrupts the retained route explicitly, including
        // its census outcome. Survival can request a ground jump through the same body rules.
        Navigator.Interrupt(live);
        return AddRequestedJump(Controls.None, requestedJump);
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
