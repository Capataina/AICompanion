#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A short-horizon controller for the route's next destination. The coarse route answers which
/// section of terrain to take; this search starts at the actual body pose and velocity and
/// chooses the next control by simulating that state, so an engine correction, wet transition
/// or interrupted run-up is not silently reset to a standing grid node.
/// </summary>
public sealed class PlanLocalMovement
{
    public readonly record struct Rejection(NavStep Step, BodyState Entry, BodyState Predicted, int Tick, TraversalFault Fault, string Reason);
    public Rejection? LastRejection { get; private set; }
    public string PreparationResult { get; private set; } = "none";
    private const int HorizonTicks = 12;
    private readonly System.Collections.Generic.Queue<(Controls controls, BodyState before, BodyState after)> retained = new();
    private Vector2 retainedTarget;
    private readonly System.Collections.Generic.Queue<(Controls controls, BodyState before, BodyState after)> macro = new();
    private TraversalExecution? macroOwner;
    private ITileWorld? macroWorld;
    private int macroRevision;
    /// <summary>
    /// The brain's allowance for local movement work, in milliseconds. The prefix search no longer
    /// reads it — it is bounded by <see cref="PrepareWorkTicks"/> instead — and `Navigator` still
    /// passes it to the clearance search, which is a search over an open space and is legitimately
    /// bounded by time. The name is the brain's and is set outside this folder.
    /// </summary>
    public static double PreparationMsBudget { get; set; }

    /// <summary>
    /// How many simulated body ticks the prefix search may spend before it reports an unfinished
    /// search. It replaces a two-millisecond wall clock, and the change is not a re-tuning: a
    /// wall-clock bound makes a *physical* verdict depend on the machine, so the same body on the
    /// same terrain is refused on a slow frame and rescued on a fast one, and nothing downstream
    /// can tell which it got. That is not theoretical. The search re-simulates a whole macro per
    /// candidate prefix — for a running jump, a back-off, a run-in and a flight — so two
    /// milliseconds were spent on essentially every jump refusal, `Navigator.Follow` read the spent
    /// allowance as "preparation has not answered" and suppressed the strike and the rejection
    /// memory that the refusal had earned, and the edge came back from every later plan with the
    /// body standing still in front of it. That is the 684 rejections against one jump fault in
    /// the census of the 09:28 capture of 14 September.
    ///
    /// The number is sized to cover the search the loops below can actually perform rather than to
    /// cut it short: four directions, the longest running sixty ticks with a macro probe every
    /// four, against macros bounded by the edge's own proof. Exhausting it is meant to be rare,
    /// and it still means what it always meant — the search has not answered, so its refusal is
    /// neither struck nor remembered. What has changed is that the answer no longer depends on how
    /// busy the machine was.
    /// </summary>
    public static int PrepareWorkTicks { get; set; } = 12000;

    private int prepareWork;
    private bool PreparationExpired => prepareWork >= PrepareWorkTicks;

    public void InvalidatePhysicalProof()
    {
        macro.Clear();
        preparation.Clear();
    }

    internal static bool Matches(BodyState expected, BodyState actual) =>
        Vector2.DistanceSquared(expected.Feet, actual.Feet) < .01f
        && MathF.Abs(expected.Vx - actual.Vx) < .01f && MathF.Abs(expected.Vy - actual.Vy) < .01f
        && expected.OnGround == actual.OnGround && expected.Wet == actual.Wet
        && expected.CollideX == actual.CollideX && expected.Stuck == actual.Stuck && expected.Pinned == actual.Pinned
        && expected.LiquidKind == actual.LiquidKind && expected.StairFall == actual.StairFall
        && expected.Mobility == actual.Mobility && expected.Capabilities == actual.Capabilities;

    private readonly System.Collections.Generic.Queue<(Controls controls, BodyState before, BodyState after)> preparation = new();
    private TraversalExecution? preparationOwner;
    private int preparationRevision;
    private ITileWorld? preparationWorld;
    public bool Preparing => preparation.Count > 0;

    /// <summary>Finds a short control prefix only when the complete traversal succeeds after it.</summary>
    public bool TryPrepare(ITileWorld world, TraversalExecution execution, BodyState live,
        Func<BodyState, int, bool>? unsafeAtTick, out Controls controls)
    {
        prepareWork = 0;
        return Prepare(world, execution, live, unsafeAtTick, out controls);
    }

    private bool Prepare(ITileWorld world, TraversalExecution execution, BodyState live,
        Func<BodyState, int, bool>? unsafeAtTick, out Controls controls)
    {
        controls = Controls.None;
        if (preparationOwner == execution && preparationWorld == world && preparationRevision == world.Revision
            && preparation.Count > 0 && Matches(preparation.Peek().before, live))
        {
            bool safe = true;
            int offset = 0;
            BodyState end = live;
            foreach (var frame in preparation)
            {
                end = frame.after;
                if (unsafeAtTick?.Invoke(end, ++offset) ?? false) { safe = false; break; }
            }
            int prefixTicks = offset;
            if (safe && TryExecute(world, execution.Copy(), end,
                unsafeAtTick == null ? null : (body, tick) => unsafeAtTick(body, tick + prefixTicks), out _, out _))
            {
                controls = preparation.Dequeue().controls;
                return true;
            }
        }
        preparation.Clear();
        // Every refusal names itself, so a reader of PreparationResult never sees the last attempt's
        // verdict (a spent budget, say) standing in for a refusal that never searched.
        if (!live.OnGround) { PreparationResult = "airborne"; return false; }
        if (execution.Ticks != 0) { PreparationResult = "attempt-already-begun"; return false; }
        // Search control sequences, not a timer spent approaching a representative grid point.
        // Every accepted prefix ends in a complete actual-state proof of the intended edge.
        float entryX = NavGrid.FeetWorld(execution.Step.From).X;
        foreach (float direction in new[] { float.NaN, 0f, -BodyPhysics.WalkSpeed, BodyPhysics.WalkSpeed })
        {
            BodyState state = live;
            var prefix = new System.Collections.Generic.List<(Controls controls, BodyState before, BodyState after)>();
            int limit = float.IsNaN(direction) ? 60 : 12;
            for (int length = 1; length <= limit; length++)
            {
                if (PreparationExpired) { PreparationResult = "search-budget-exhausted"; return false; }
                float move = float.IsNaN(direction) ? BodyPhysics.SteerToward(entryX, state.CentreX, state.Vx, .5f) : direction;
                var input = new Controls(move, Descend: execution.Step.Kind is MoveKind.Drop or MoveKind.FallThrough);
                BodyState before = state;
                prepareWork++;
                state = BodyMotion.Step(world, state, input);
                prefix.Add((input, before, state));
                if (state.Stuck || (unsafeAtTick?.Invoke(state, length) ?? false)) break;
                if (Matches(state, live)) break;
                if (!state.OnGround || (length != 1 && length % 4 != 0)) continue;
                int offset = length;
                var probe = execution.Copy();
                // A macro probe is charged its own worst case rather than what it happened to run,
                // so the work spent is a function of the move and the search shape alone. Charging
                // the actual length would make the bound depend on where each probe stopped, which
                // is the same machine-dependence in another coat.
                prepareWork += Math.Max(90, execution.Step.Ticks * 2 + 120);
                if (!TryExecute(world, probe, state, unsafeAtTick == null ? null : (body, tick) => unsafeAtTick(body, tick + offset), out _, out _)) continue;
                foreach (var frame in prefix) preparation.Enqueue(frame);
                preparationOwner = execution;
                preparationWorld = world;
                preparationRevision = world.Revision;
                controls = preparation.Dequeue().controls;
                PreparationResult = $"validated-prefix; ticks={length}; move={(float.IsNaN(direction) ? "align-entry" : direction.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
                return true;
            }
        }
        PreparationResult = "no-executable-prefix";
        return false;
    }

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
        PreparationResult = bestScore == float.MaxValue ? "all-candidates-rejected" : $"selected={best};score={bestScore:0.00}";
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
        // A cache of refused body states used to sit here, answering an identical state with its
        // stored fault instead of simulating again, on the argument that repeating an identical
        // rejected simulation costs and teaches nothing. The argument is true and the cache was
        // still half of what made the failure permanent. A refusal leaves the body with no
        // controls, so the body does not move, so the next tick's state is bit-identical, so the
        // cache answers again — and it answers without simulating, which meant without the
        // evidence anything downstream could act on, and without ever reaching the preparation
        // search that might have rescued it. It saved one simulation of a move the body was
        // standing still in front of, and it bought a companion frozen at a take-off for as long
        // as the plan kept offering the edge: 390 bit-identical rejections on one edge in the
        // 09:28 capture of 14 September, entry velocity and predicted landing the same to the
        // last decimal place on every one of them.
        if (macroOwner != execution || macroWorld != world || macroRevision != world.Revision
            || macro.Count == 0 || !Matches(macro.Peek().before, live))
        {
            macro.Clear();
            macroOwner = execution;
            macroWorld = world;
            macroRevision = world.Revision;
            TraversalExecution probe = execution.Copy();
            BodyState predicted = live;
            // Validate the complete remaining macro from the body the engine actually left, not one
            // hopeful tick. The bound is deliberately the edge's own proof plus preparation slack;
            // a running jump may need its runway before its flight begins.
            int limit = Math.Max(90, execution.Step.Ticks * 2 + 120);
            // The macro proof is bounded by `limit` alone, which is the edge's own proof plus the
            // slack a running jump's runway needs, and is therefore a fact about the move. It used
            // to be cut short by the preparation search's wall clock as well, which is how a
            // complete physical answer could turn into "the search ran out" on a busy frame.
            for (int tick = 1; tick <= limit; tick++)
            {
                if (probe.IsDone(predicted))
                    break;
                BodyState before = predicted;
                predicted = probe.Simulate(world, predicted, out Controls planned, out fault);
                macro.Enqueue((planned, before, predicted));
                if (fault != TraversalFault.None || (unsafeAtTick?.Invoke(predicted, tick) ?? false))
                {
                    LastRejection = new(execution.Step, live, predicted, tick, fault, fault == TraversalFault.None ? "predicted-threat" : "physical-fault");
                    controls = Controls.None;
                    macro.Clear();
                    return false;
                }
                if (tick == limit)
                {
                    LastRejection = new(execution.Step, live, predicted, tick, TraversalFault.Timeout, "macro-did-not-finish");
                    controls = Controls.None;
                    fault = TraversalFault.Timeout;
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
        if (preparationOwner == execution) preparation.Clear();
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

    public static Func<bool>? CaptureRequested;
    public static Action<Controls, Vector2[], float>? CandidateEvaluated;

    private static float Score(ITileWorld world, BodyState state, Vector2 target, Controls first, MovementCapabilities capabilities, Func<BodyState, int, bool>? unsafeAtTick)
    {
        var trace = CaptureRequested?.Invoke() == true ? new System.Collections.Generic.List<Vector2> { state.Feet } : null;
        for (int tick = 0; tick < HorizonTicks; tick++)
        {
            Controls controls = tick == 0
                ? first
                : new Controls(BodyPhysics.SteerToward(target.X, state.CentreX, state.Vx));
            state = BodyMotion.Step(world, state, controls, capabilities);
            trace?.Add(state.Feet);
            if (state.Stuck || (unsafeAtTick?.Invoke(state, tick) ?? false))
            {
                if (trace != null) CandidateEvaluated?.Invoke(first, trace.ToArray(), float.MaxValue);
                return float.MaxValue;
            }
        }
        float distance = Vector2.DistanceSquared(state.Feet, target);
        // A controller that stays airborne while the target is a standing route point is less
        // useful than one that reaches it on a surface; this is a finite preference, never a
        // veto, because jumps intentionally fly through most horizons.
        float score = distance + (state.OnGround ? 0f : 64f);
        if (trace != null) CandidateEvaluated?.Invoke(first, trace.ToArray(), score);
        return score;
    }
}
