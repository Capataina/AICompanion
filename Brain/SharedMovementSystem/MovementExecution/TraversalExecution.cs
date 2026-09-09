#nullable enable

using Microsoft.Xna.Framework;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>
/// A copyable in-flight traversal. It is the unit retained by local execution: the same policy
/// state produces controls for the live body and for a predicted successor, so a jump's runway
/// cannot be replaced by a stateless steer toward its landing.
/// </summary>
public sealed class TraversalExecution
{
    private readonly Traversal traversal;
    private object? state;

    public NavStep Step { get; }
    public NavStep? Next { get; }
    public int Ticks { get; private set; }
    public bool MidMove
    {
        get { traversal.RestoreExecutionState(state); return traversal.MidMove; }
    }

    public TraversalExecution(Traversal traversal, NavStep step, NavStep? next, int ticks = 0)
    {
        this.traversal = traversal;
        Step = step;
        Next = next;
        Ticks = ticks;
        traversal.Begin(step);
        state = traversal.CaptureExecutionState();
    }

    private TraversalExecution(Traversal traversal, NavStep step, NavStep? next, int ticks, object? state)
    {
        this.traversal = traversal;
        Step = step;
        Next = next;
        Ticks = ticks;
        this.state = state;
    }

    public TraversalExecution Copy() => new(traversal.CopyForExecution(), Step, Next, Ticks, state);

    public bool IsDone(BodyState live)
    {
        traversal.RestoreExecutionState(state);
        return traversal.Done(live, Step, Next);
    }

    public Controls NextControls(BodyState live, out TraversalFault fault)
    {
        traversal.RestoreExecutionState(state);
        fault = traversal.Check(live, Step, Ticks);
        if (fault != TraversalFault.None)
            return Controls.None;
        Controls controls = traversal.Steer(live, Step, Next);
        state = traversal.CaptureExecutionState();
        Ticks++;
        return controls;
    }

    public BodyState Simulate(ITileWorld world, BodyState live, out Controls controls, out TraversalFault fault)
    {
        controls = NextControls(live, out fault);
        return fault == TraversalFault.None ? BodyMotion.Step(world, live, controls) : live;
    }
}
