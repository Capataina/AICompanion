#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// Searches short, legal control blocks from the body the engine actually left. It is deliberately
/// below route planning: a route supplies an endpoint, while this search can back away or cross
/// water to clear local geometry without inventing a movement ability or moving the body directly.
/// Successful prefixes are retained only while their expected body state and terrain revision hold.
/// </summary>
public sealed class SearchControlSequences
{
    private const int BlockTicks = 4;
    // This is a clearance search, not a substitute for the route planner.  Keeping both its
    // depth and its per-call expansion count finite means that an unreachable dry pocket cannot
    // retain an ever-growing collection of full control histories.
    private const int MaxPrefixTicks = 96;
    private const int MaxNodes = 512;

    private readonly record struct Frame(Controls Controls, BodyState Before, BodyState After);
    private sealed class Node
    {
        public required BodyState State;
        public required List<Frame> Prefix;
    }

    private readonly PriorityQueue<Node, float> frontier = new();
    private readonly HashSet<StateKey> seen = new();
    private readonly Queue<Frame> retained = new();
    private ITileWorld? plannedWorld;
    private int plannedRevision;
    private BodyState? frontierOrigin;

    private readonly record struct StateKey(int X, int Y, int Vx, int Vy, bool Grounded, bool CollideX, bool Wet,
        int LiquidKind, MobilityState Mobility, bool StairFall, MovementCapabilities Capabilities)
    {
        public static StateKey From(BodyState state) => new(
            (int)MathF.Round(state.Left / 4f), (int)MathF.Round(state.Bottom / 4f),
            (int)MathF.Round(state.Vx * 2f), (int)MathF.Round(state.Vy * 2f),
            state.OnGround, state.CollideX, state.Wet, state.LiquidKind, state.Mobility, state.StairFall, state.Capabilities);

    }

    /// <summary>
    /// True after a budget-limited call that has unfinished work but no safe prefix to execute.
    /// The caller may hold for one tick without clearing the frontier, then ask again.
    /// </summary>
    public bool Pending { get; private set; }
    public int RetainedTicks => retained.Count;

    /// <summary>Stops any retained prefix and any unfinished frontier; call when the request's meaning changes.</summary>
    public void Clear()
    {
        frontier.Clear();
        seen.Clear();
        retained.Clear();
        plannedWorld = null;
        frontierOrigin = null;
        Pending = false;
    }

    /// <summary>
    /// Returns the next control in a bounded, retained search. <paramref name="goal"/> recognises
    /// success (for example, a dry head or a requested endpoint); <paramref name="heuristic"/>
    /// orders exploration but never forbids moving away from the apparent destination.
    /// </summary>
    public bool TryChoose(ITileWorld world, BodyState live, Func<BodyState, bool> goal,
        Func<BodyState, float> heuristic, MovementCapabilities capabilities, int workBudget,
        double milliseconds, out Controls controls, Func<BodyState, int, bool>? unsafeAtTick = null,
        bool allowPartialProgress = true)
    {
        controls = Controls.None;
        Pending = false;
        if (workBudget <= 0)
            return false;

        if (retained.Count > 0)
        {
            Frame next = retained.Peek();
            if (plannedWorld == world && plannedRevision == world.Revision && PlanLocalMovement.Matches(next.Before, live)
                && (unsafeAtTick == null || !unsafeAtTick(next.After, 1)))
            {
                controls = retained.Dequeue().Controls;
                return true;
            }
            Clear();
        }

        // An unfinished frontier describes prefixes from one exact observed state.  It remains
        // meaningful across time slices only while the motor has not made another move; after a
        // fallback control or an external push, those prefixes were never executed and cannot be
        // spliced onto the new body.
        if (plannedWorld != world || plannedRevision != world.Revision
            || frontierOrigin is BodyState origin && !PlanLocalMovement.Matches(origin, live))
        {
            frontier.Clear();
            seen.Clear();
            plannedWorld = world;
            plannedRevision = world.Revision;
            frontierOrigin = live;
        }
        if (frontier.Count == 0)
        {
            // A completed/exhausted slice has no remaining prefixes from which `seen` could
            // prune work.  Retaining it would eventually make the current live root ineligible
            // solely because a previous local query hit its memory cap.
            seen.Clear();
            frontierOrigin = live;
            Add(new Node { State = live with { Capabilities = capabilities }, Prefix = new List<Frame>() }, heuristic);
        }

        long deadline = LimitPlanningWork.Deadline(milliseconds);
        float bestScore = heuristic(live);
        List<Frame>? bestPrefix = null;
        float fallbackScore = float.PositiveInfinity;
        List<Frame>? fallbackPrefix = null;
        for (int expanded = 0; frontier.Count > 0 && expanded < workBudget; expanded++)
        {
            if (expanded > 0 && deadline != 0L && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
                break;
            Node node = frontier.Dequeue();
            // A frontier survives time slices, but the danger forecast does not. Validate the
            // entire prefix from today's live origin before returning or extending old work.
            bool safePrefix = true;
            if (unsafeAtTick != null)
                for (int i = 0; i < node.Prefix.Count; i++)
                    if (unsafeAtTick(node.Prefix[i].After, i + 1)) { safePrefix = false; break; }
            if (!safePrefix) continue;
            if (goal(node.State) && node.Prefix.Count > 0)
            {
                foreach (Frame frame in node.Prefix)
                    retained.Enqueue(frame);
                controls = retained.Dequeue().Controls;
                return true;
            }
            foreach (Controls input in Inputs())
            {
                BodyState state = node.State;
                var prefix = new List<Frame>(node.Prefix);
                bool valid = true;
                // Offer a grounded jump through its landing as well as its first
                // short block. Branching at every four ticks alone fills the state
                // cap with nearly identical early-flight poses before any landing
                // can establish useful progress, especially in slow liquid motion.
                int blockLength = input.Jump && state.OnGround ? MaxPrefixTicks - prefix.Count : BlockTicks;
                for (int tick = 0; tick < blockLength; tick++)
                {
                    BodyState before = state;
                    state = BodyMotion.Step(world, state, input, capabilities);
                    prefix.Add(new Frame(input, before, state));
                    if (state.Stuck || state.CannotAct || unsafeAtTick?.Invoke(state, prefix.Count) == true) { valid = false; break; }
                    if (goal(state))
                    {
                        foreach (Frame frame in prefix) retained.Enqueue(frame);
                        controls = retained.Dequeue().Controls;
                        return true;
                    }
                    if (tick + 1 == BlockTicks && blockLength > BlockTicks)
                        Add(new Node { State = state, Prefix = new List<Frame>(prefix) }, heuristic);
                    if (tick + 1 >= BlockTicks && state.OnGround) break;
                    if (tick % BlockTicks == BlockTicks - 1 && deadline != 0L
                        && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) break;
                }
                if (!valid) continue;
                // A jump apex is not an escape outcome: choosing it repeatedly can return a
                // body to the same wet cell forever.  Commit only a landing, a liquid-boundary
                // crossing, or the explicit goal; the frontier may still explore air states.
                if (state.OnGround || state.Wet != live.Wet)
                {
                    float score = heuristic(state);
                    if (score < fallbackScore)
                    {
                        fallbackScore = score;
                        fallbackPrefix = new List<Frame>(prefix);
                    }
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPrefix = new List<Frame>(prefix);
                    }
                }
                if (prefix.Count < MaxPrefixTicks)
                    Add(new Node { State = state, Prefix = prefix }, heuristic);
            }
        }
        // A time slice ending is not an exhausted search. Returning a stationary or
        // worse fallback here restarts the next query before it reaches the landing
        // of a useful jump, so a short per-frame budget can strand a capable body.
        if (allowPartialProgress && (bestPrefix ?? (frontier.Count == 0 ? fallbackPrefix : null)) is { Count: > 0 } chosenPrefix)
        {
            foreach (Frame frame in chosenPrefix)
                retained.Enqueue(frame);
            controls = retained.Dequeue().Controls;
            return true;
        }
        Pending = frontier.Count > 0;
        return false;
    }

    private void Add(Node node, Func<BodyState, float> heuristic)
    {
        if (seen.Count >= MaxNodes || !seen.Add(StateKey.From(node.State)))
            return;
        frontier.Enqueue(node, node.Prefix.Count + heuristic(node.State));
    }

    private static IEnumerable<Controls> Inputs()
    {
        foreach (float move in new[] { -BodyPhysics.WalkSpeed, 0f, BodyPhysics.WalkSpeed })
        {
            yield return new Controls(move);
            yield return new Controls(move, Jump: true);
        }
    }
}
