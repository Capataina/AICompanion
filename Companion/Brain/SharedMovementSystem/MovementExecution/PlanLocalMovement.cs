#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// A short-horizon controller for the route's next destination. The coarse route answers which
/// section of terrain to take; this search starts at the actual body pose and velocity and
/// chooses the next control by simulating that state, so an engine correction, wet transition
/// or interrupted run-up is not silently reset to a standing grid node.
/// </summary>
public sealed class PlanLocalMovement
{
    private const int HorizonTicks = 12;
    private readonly System.Collections.Generic.Queue<(Controls controls, BodyState before, BodyState after)> retained = new();
    private Vector2 retainedTarget;
    private readonly System.Collections.Generic.Queue<(Controls controls, BodyState before, BodyState after)> macro = new();
    private TraversalExecution? macroOwner;
    private ITileWorld? macroWorld;
    private int macroRevision;
    private BodyState? rejectedState;
    private TraversalFault rejectedFault;

    private static bool Matches(BodyState expected, BodyState actual) =>
        Vector2.DistanceSquared(expected.Feet, actual.Feet) < .01f
        && MathF.Abs(expected.Vx - actual.Vx) < .01f && MathF.Abs(expected.Vy - actual.Vy) < .01f
        && expected.OnGround == actual.OnGround && expected.Wet == actual.Wet
        && expected.CollideX == actual.CollideX && expected.Stuck == actual.Stuck && expected.Pinned == actual.Pinned
        && expected.LiquidKind == actual.LiquidKind && expected.StairFall == actual.StairFall
        && expected.Mobility == actual.Mobility && expected.Capabilities == actual.Capabilities;

    public Controls Choose(ITileWorld world, BodyState live, Vector2 target, Controls preferred, MovementCapabilities capabilities, Func<BodyState, int, bool>? unsafeAtTick = null)
    {
        // A traversal owns a committed take-off sequence. Replacing its run-up with a control
        // merely closer to the landing makes a running jump turn around at its runway mark; the
        // controller still evaluates flight safety through the traversal's exact controls.
        if (retained.Count > 0 && Vector2.DistanceSquared(retainedTarget, target) < 4f)
        {
            (Controls controls, BodyState before, BodyState after) next = retained.Dequeue();
            // Engine corrections are expected, but a material pose/contact change invalidates a
            // local sequence; rebuild from the live state instead of executing controls planned
            // from a grid node the body no longer occupies.
            if (Matches(next.before, live) && !(unsafeAtTick?.Invoke(BodyMotion.Step(world, live, next.controls, capabilities), 1) ?? false))
                return next.controls;
            retained.Clear();
        }
        Controls[] candidates =
        {
            preferred,
            new Controls(BodyPhysics.SteerToward(target.X, live.CentreX, live.Vx)),
            new Controls(BodyPhysics.WalkSpeed),
            new Controls(-BodyPhysics.WalkSpeed),
            new Controls(BodyPhysics.SteerToward(target.X, live.CentreX, live.Vx), Jump: true),
        };

        Controls best = Controls.None;
        float bestScore = float.MaxValue;
        foreach (Controls candidate in candidates)
        {
            float score = Score(world, live, target, candidate, capabilities, unsafeAtTick);
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        if (bestScore == float.MaxValue)
            return Controls.None;
        Remember(world, live, target, best, capabilities);
        return best;
    }

    /// <summary>
    /// Validates and executes one retained traversal macro. The copy is advanced first, so an
    /// unsafe prediction never mutates the live execution state; a safe macro then emits the
    /// exact policy control that its proof and preparation state require.
    /// </summary>
    public bool TryExecute(ITileWorld world, TraversalExecution execution, BodyState live, Func<BodyState, int, bool>? unsafeAtTick, out Controls controls, out TraversalFault fault)
    {
        fault = TraversalFault.None;
        if (macroOwner == execution && macroWorld == world && macroRevision == world.Revision
            && rejectedState is BodyState rejected && Matches(rejected, live))
        {
            controls = Controls.None;
            fault = rejectedFault;
            return false;
        }
        if (macroOwner != execution || macroWorld != world || macroRevision != world.Revision
            || macro.Count == 0 || !Matches(macro.Peek().before, live))
        {
            macro.Clear();
            macroOwner = execution;
            macroWorld = world;
            macroRevision = world.Revision;
            rejectedState = null;
            TraversalExecution probe = execution.Copy();
            BodyState predicted = live;
            // Validate the complete remaining macro from the body the engine actually left, not one
            // hopeful tick. The bound is deliberately the edge's own proof plus preparation slack;
            // a running jump may need its runway before its flight begins.
            int limit = Math.Max(90, execution.Step.Ticks * 2 + 120);
            for (int tick = 1; tick <= limit; tick++)
            {
                if (probe.IsDone(predicted))
                    break;
                BodyState before = predicted;
                predicted = probe.Simulate(world, predicted, out Controls planned, out fault);
                macro.Enqueue((planned, before, predicted));
                if (fault != TraversalFault.None || (unsafeAtTick?.Invoke(predicted, tick) ?? false))
                {
                    if (fault != TraversalFault.None)
                    {
                        rejectedState = live;
                        rejectedFault = fault;
                    }
                    controls = Controls.None;
                    macro.Clear();
                    return false;
                }
                if (tick == limit)
                {
                    controls = Controls.None;
                    fault = TraversalFault.Timeout;
                    rejectedState = live;
                    rejectedFault = fault;
                    macro.Clear();
                    return false;
                }
            }
        }
        // Threats move independently of terrain. Recheck the remaining commitment against the
        // current observations even while its physical control sequence remains reusable.
        int offset = 1;
        foreach (var frame in macro)
        {
            if (unsafeAtTick?.Invoke(frame.after, offset++) ?? false)
            {
                controls = Controls.None;
                macro.Clear();
                return false;
            }
        }
        if (macro.Count > 0) macro.Dequeue();
        controls = execution.NextControls(live, out fault);
        return fault == TraversalFault.None;
    }

    private void Remember(ITileWorld world, BodyState state, Vector2 target, Controls first, MovementCapabilities capabilities)
    {
        retained.Clear();
        retainedTarget = target;
        for (int tick = 0; tick < HorizonTicks - 1; tick++)
        {
            Controls controls = tick == 0
                ? first
                : new Controls(BodyPhysics.SteerToward(target.X, state.CentreX, state.Vx));
            BodyState before = state;
            state = BodyMotion.Step(world, state, controls, capabilities);
            retained.Enqueue((controls, before, state));
            if (state.OnGround && Vector2.DistanceSquared(state.Feet, target) < 16f)
                break;
        }
        // The first control was returned to the caller, so discard its stored counterpart.
        if (retained.Count > 0)
            retained.Dequeue();
    }

    private static float Score(ITileWorld world, BodyState state, Vector2 target, Controls first, MovementCapabilities capabilities, Func<BodyState, int, bool>? unsafeAtTick)
    {
        for (int tick = 0; tick < HorizonTicks; tick++)
        {
            Controls controls = tick == 0
                ? first
                : new Controls(BodyPhysics.SteerToward(target.X, state.CentreX, state.Vx));
            state = BodyMotion.Step(world, state, controls, capabilities);
            if (state.Stuck || (unsafeAtTick?.Invoke(state, tick) ?? false))
                return float.MaxValue;
        }
        float distance = Vector2.DistanceSquared(state.Feet, target);
        // A controller that stays airborne while the target is a standing route point is less
        // useful than one that reaches it on a surface; this is a finite preference, never a
        // veto, because jumps intentionally fly through most horizons.
        return distance + (state.OnGround ? 0f : 64f);
    }
}
