#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// The two halves of a jump, run over the same edge and printed against each other.
///
/// A jump edge exists because <see cref="BodyPhysics.SimulateJump"/> flew it from the node's
/// representative pose at the profile's nominal start speed: that is the <em>proof</em> path.
/// The body then performs it through <see cref="TraversalExecution"/> driving
/// <see cref="JumpTraversal.Steer"/> into <see cref="BodyMotion.Step"/>, which is the
/// <em>execution</em> path, and that path owns the run-up as well as the arc. Both are portable
/// code over the same <see cref="ITileWorld"/>, so a disagreement between them is entirely ours
/// and has nothing to do with the native body; this instrument is not a parity check.
///
/// The two paths are aligned on the take-off tick rather than on tick 1, because the execution
/// path spends ticks backing away and running in before it jumps and a raw tick alignment would
/// report that preparation as the divergence. What the alignment exposes is the question nobody
/// had asked: the state the arc actually starts from. A proof flown at a start speed the runway
/// cannot deliver is a proof of a jump the body will never make.
/// </summary>
internal static class CompareJumpPaths
{
    /// <summary>One path's whole run: every tick it took, where it took off from, and how it ended.</summary>
    internal sealed class Trace
    {
        public string Name = "";
        public readonly List<(int Tick, Controls Controls, BodyState After)> Frames = new();
        public BodyState Entry;
        /// <summary>The state the jump tick was issued from, which is the entry the arc really has.</summary>
        public BodyState TakeOff;
        /// <summary>How many ticks the path spent before the jump tick; zero for the proof by construction.</summary>
        public int Preparation = -1;
        public string Outcome = "no-take-off";
        public BodyState Final;
        public bool Reached;
        /// <summary>The shared arrival test held at the entry pose, so this step completes without a jump.</summary>
        public bool SatisfiedAtEntry;
        /// <summary>The body flew and came to rest inside the arrival slack of the step's feet point, but not covering its tile.</summary>
        public bool CompletedBySlack;
    }

    /// <summary>
    /// The proof path: exactly the call <see cref="JumpTraversal.Candidates"/> makes, from a
    /// standing pose at the profile's nominal start speed, with its tick trace kept.
    /// </summary>
    internal static Trace Proof(ITileWorld world, BodyPhysics.Pose entry, NavStep step)
    {
        var trace = new Trace { Name = "proof", Preparation = 0 };
        trace.Entry = new BodyState(entry.Left, entry.Bottom, step.StartVx, 0f, true);
        trace.TakeOff = trace.Entry;
        trace.Final = trace.Entry;
        BodyPhysics.Pose? landing = BodyPhysics.SimulateJump(world, entry, step.JumpScale, step.StartVx,
            step.Tile.X, step.Tile.Y, JumpTraversal.MaxJumpTicks, out int ticks, tick =>
            {
                // The controls are not handed back by the trace, so they are reconstructed from the
                // one rule SimulateJump uses: the impulse on tick 1, the shared steer after it.
                var controls = new Controls(0f, Jump: tick.Tick == 1, JumpScale: step.JumpScale);
                trace.Frames.Add((tick.Tick, controls, new BodyState(tick.Left, tick.Bottom, tick.Vx, tick.Vy, false)));
            });
        if (landing is BodyPhysics.Pose pose)
        {
            trace.Final = new BodyState(pose.Left, pose.Bottom, 0f, 0f, true);
            var feet = new Point((int)MathF.Floor(pose.CentreX / 16f), BodyPhysics.FeetRow(pose.Bottom));
            trace.Reached = feet == step.Tile;
            trace.Outcome = trace.Reached ? $"landed on {Fmt(step.Tile)} after {ticks} ticks" : $"landed on {Fmt(feet)} after {ticks} ticks, not the edge's tile";
        }
        else
            trace.Outcome = $"never landed: abandoned after {ticks} ticks (a ceiling, a wall, or falling past the landing row)";
        return trace;
    }

    /// <summary>
    /// The execution path: the loop <see cref="PlanLocalMovement.TryExecute"/> runs to validate a
    /// macro from the body the engine actually left, with the same bound and the same terminal
    /// conditions, so a Misland printed here is the Misland the navigator recorded.
    /// </summary>
    internal static Trace Execution(ITileWorld world, BodyState entry, NavStep step)
    {
        var trace = new Trace { Name = "execution", Entry = entry, TakeOff = entry, Final = entry };
        var execution = new TraversalExecution(new JumpTraversal(), step, null);
        BodyState state = entry;
        int limit = Math.Max(90, step.Ticks * 2 + 120);
        for (int tick = 1; tick <= limit; tick++)
        {
            if (execution.IsDone(state))
            {
                trace.Reached = state.Covers(step.Tile);
                // Done before a single tick means the shared arrival test was already satisfied at
                // the entry pose: the step is complete without the jump ever being made. That is a
                // property of Traversal.Done's slack rather than of this arc, so it is named
                // separately and never counted as a flown jump.
                trace.SatisfiedAtEntry = tick == 1;
                // Landed, but on a tile the step never named: Traversal.Done's arrival slack is a
                // radius in pixels, so a landing a row below the edge's tile still reads as done.
                // That is the arrival test's business rather than the arc's, and conflating the two
                // would let a real misland hide inside the count.
                trace.CompletedBySlack = !trace.Reached && !trace.SatisfiedAtEntry;
                trace.Outcome = trace.SatisfiedAtEntry
                    ? $"satisfied at entry without jumping, feet still on {Fmt(state.FeetTile)}"
                    : trace.Reached
                        ? $"done after {tick - 1} ticks covering {Fmt(step.Tile)}"
                        : $"done after {tick - 1} ticks by the arrival slack, resting on {Fmt(state.FeetTile)} and not {Fmt(step.Tile)}";
                break;
            }
            BodyState before = state;
            state = execution.Simulate(world, state, out Controls controls, out TraversalFault fault);
            trace.Frames.Add((tick, controls, state));
            trace.Final = state;
            if (controls.Jump && trace.Preparation < 0)
            {
                trace.Preparation = tick - 1;
                trace.TakeOff = before;
            }
            if (fault != TraversalFault.None)
            {
                trace.Outcome = $"{fault} at tick {tick}, at rest on {Fmt(state.FeetTile)}";
                break;
            }
            if (tick == limit)
                trace.Outcome = $"macro did not finish inside {limit} ticks";
        }
        return trace;
    }

    /// <summary>
    /// Runs the proof and the execution over one edge from a set of entries and prints them
    /// aligned on their take-off ticks. Returns true when every execution run reached the edge's
    /// tile, false when any of them did not, and null when the planner proves no such edge.
    /// </summary>
    internal static bool? Compare(ITileWorld world, string name, Point from, Point to, BodyState? live, bool verbose)
    {
        if (NavGrid.StandAt(from.X, from.Y, AStar.AllowLava) is not BodyPhysics.Pose node)
        {
            Console.WriteLine($"compare-jump {Fmt(from)} -> {Fmt(to)} in {name}: no pose at the take-off tile");
            return null;
        }
        NavStep? proven = ProvenEdge(node, from, to);
        if (proven is not NavStep step)
        {
            Console.WriteLine($"compare-jump {Fmt(from)} -> {Fmt(to)} in {name}: the planner proves no jump edge between those tiles");
            return null;
        }
        Console.WriteLine($"compare-jump {Fmt(from)} -> {Fmt(to)} in {name}");
        Console.WriteLine($"  edge as proven   scale {step.JumpScale:F2} startVx {step.StartVx:F2} flight {step.Ticks} ticks");
        Console.WriteLine($"  node pose        left {node.Left:F1} bottom {node.Bottom:F1}   (the representative pose Candidates proved from)");
        Console.WriteLine($"  runway behind    {RunwayReport(from, node, step)}");
        // A recorded entry belongs to this edge only where the body stands on its take-off tile.
        // The dump's npcbox is whatever the body was doing at the tick the window was written, and
        // reading it as the entry for a jump somewhere else prints a misland that means nothing.
        if (live is BodyState offEdge && offEdge.FeetTile != from)
        {
            Console.WriteLine($"  live entry       ignored: the recorded body stands on {Fmt(offEdge.FeetTile)}, not this edge's take-off");
            live = null;
        }
        else if (live is BodyState entry)
            Console.WriteLine($"  live entry       left {entry.Left:F1} bottom {entry.Bottom:F1} vx {entry.Vx:F2} ground {entry.OnGround}");

        // The take-off the run-up actually reaches, which is the entry Candidates now proves the
        // arc from. The nominal-speed proof is kept beside it because the gap between the two is
        // the defect class this instrument exists to name, and a reader has to see both to
        // recognise it coming back.
        BodyState? reached = step.StartVx == 0f
            ? BodyState.Standing(node)
            : JumpTraversal.TakeOff(world, from, node, to, step.JumpScale, step.StartVx);
        var runs = new List<Trace>
        {
            Named(Proof(world, node, step), "proof  x node pose, at the nominal speed"),
        };
        if (reached is BodyState launch)
            runs.Add(Named(Proof(world, launch.Pose, step with { StartVx = launch.Vx }), "proof  x the take-off the run-up reaches"));
        runs.Add(Named(Execution(world, BodyState.Standing(node), step), "exec   x node pose, at rest"));
        if (live is BodyState liveEntry)
        {
            runs.Add(Named(Proof(world, liveEntry.Pose, step), "proof  x live entry, at the nominal speed"));
            runs.Add(Named(Execution(world, liveEntry, step), "exec   x live entry"));
        }

        Console.WriteLine();
        Console.WriteLine("  run                                      prep  take-off left/bottom/vx      outcome");
        foreach (Trace run in runs)
            Console.WriteLine($"  {run.Name,-41} {(run.Preparation < 0 ? "  -" : run.Preparation.ToString().PadLeft(3))}  " +
                $"{run.TakeOff.Left,8:F1} {run.TakeOff.Bottom,8:F1} {run.TakeOff.Vx,6:F2}   {run.Outcome}");

        // The run the others are read against is the one the planner's edge now rests on: the arc
        // from the take-off the performer reaches. Where there is no run-up at all it is the
        // nominal proof, which is the same thing for a standing jump.
        Trace reference = runs.Count > 1 && reached != null ? runs[1] : runs[0];
        foreach (Trace run in runs)
        {
            if (ReferenceEquals(run, reference)) continue;
            Console.WriteLine();
            Console.WriteLine($"  {run.Name} against {reference.Name}: {Divergence(reference, run)}");
        }

        if (verbose)
            foreach (Trace run in runs)
            {
                Console.WriteLine();
                Console.WriteLine($"  {run.Name}");
                foreach ((int tick, Controls controls, BodyState after) in run.Frames)
                    Console.WriteLine($"   t{tick,3} left {after.Left,8:F1} bottom {after.Bottom,8:F1} vx {after.Vx,6:F2} vy {after.Vy,6:F2} feet {Fmt(after.FeetTile)}" +
                        $" {(after.OnGround ? "ground" : "air   ")}{(after.CollideX ? " wall" : "")}{(after.Stuck ? " STUCK" : "")}" +
                        $"  ask {controls.MoveX,6:F2}{(controls.Jump ? $" JUMP x{controls.JumpScale:F2}" : "")}");
            }

        bool everyExecutionReached = true;
        foreach (Trace run in runs)
            if (run.Name.StartsWith("exec", StringComparison.Ordinal) && !run.Reached)
                everyExecutionReached = false;
        return everyExecutionReached;
    }

    /// <summary>What an audit of one window found: how many jumps the planner proved and how many
    /// of them the performer flies from the node's own resting pose.</summary>
    internal readonly record struct Audit(int Proven, int Flown, int SatisfiedAtEntry, int CompletedBySlack, int Unflyable)
    {
        public static Audit operator +(Audit a, Audit b)
            => new(a.Proven + b.Proven, a.Flown + b.Flown, a.SatisfiedAtEntry + b.SatisfiedAtEntry,
                a.CompletedBySlack + b.CompletedBySlack, a.Unflyable + b.Unflyable);
        /// <summary>The share of proven jumps whose arc the performer actually flies to the tile it was proven to land on.</summary>
        public float FlownShare => Proven == 0 ? 1f : Flown / (float)Proven;
    }

    /// <summary>
    /// Every jump the planner proves in a window, run through the performer from the same resting
    /// pose. This is the census's offline twin: the game's census counts jumps begun against jumps
    /// completed and cannot say whether the ones that failed were ever possible, while this counts
    /// the proofs that do not survive their own execution path and needs no playtest to do it.
    /// </summary>
    internal static Audit AuditWindow(TextTileWorld world, int originX, int originY, int width, int height, Action<string>? offender)
    {
        var jump = new JumpTraversal();
        int proven = 0, flown = 0, satisfied = 0, slack = 0, unflyable = 0;
        for (int x = originX; x < originX + width; x++)
            for (int y = originY; y < originY + height; y++)
            {
                if (NavGrid.StandAt(x, y, AStar.AllowLava) is not BodyPhysics.Pose node) continue;
                foreach (NavEdge edge in jump.Candidates(NavNode.At(new Point(x, y)), node, AStar.AllowLava))
                {
                    proven++;
                    Trace run = Execution(world, BodyState.Standing(node), edge.Step);
                    if (run.SatisfiedAtEntry) satisfied++;
                    else if (run.Reached) flown++;
                    else if (run.CompletedBySlack) slack++;
                    else
                    {
                        unflyable++;
                        offender?.Invoke($"{x},{y} -> {Fmt(edge.Step.Tile)} scale {edge.Step.JumpScale:F2} startVx {edge.Step.StartVx:F2}: {run.Outcome}");
                    }
                }
            }
        return new Audit(proven, flown, satisfied, slack, unflyable);
    }

    private static Trace Named(Trace trace, string name)
    {
        trace.Name = name;
        return trace;
    }

    /// <summary>The first jump edge the planner proves between the two tiles, which is the edge the navigator was handed.</summary>
    internal static NavStep? ProvenEdge(BodyPhysics.Pose node, Point from, Point to)
    {
        foreach (NavEdge edge in new JumpTraversal().Candidates(NavNode.At(from), node, AStar.AllowLava))
            if (edge.Step.Tile == to)
                return edge.Step;
        return null;
    }

    /// <summary>
    /// What the take-off row actually offers a run-up, beside what the profile's speed needs. The
    /// two are printed together because a profile admitted on a runway shorter than its speed
    /// requires is a proof of an arc the performer cannot enter.
    /// </summary>
    private static string RunwayReport(Point from, BodyPhysics.Pose pose, NavStep step)
    {
        if (step.StartVx == 0f)
            return "not needed, this is a standing jump";
        int behind = -Math.Sign(step.StartVx);
        int tiles = 0;
        while (tiles < 8 && NavGrid.IsStandable(from.X + behind * (tiles + 1), from.Y))
            tiles++;
        float need = step.StartVx * step.StartVx / (2f * BodyPhysics.Acceleration);
        BodyState? reached = JumpTraversal.TakeOff(NavGrid.World, from, pose, step.Tile, step.JumpScale, step.StartVx);
        string got = reached is BodyState r ? $"the run-up reaches {MathF.Abs(r.Vx):F2}" : "the run-up never reaches a take-off";
        return $"{tiles} standable tiles ({tiles * 16f:F0} px); reaching {MathF.Abs(step.StartVx):F2} from rest needs {need:F1} px; {got}";
    }

    /// <summary>
    /// Where two runs stop agreeing, aligned on their take-off ticks. The take-off state is
    /// reported first because a difference there means the arcs were never the same jump.
    /// </summary>
    private static string Divergence(Trace reference, Trace run)
    {
        float dl = run.TakeOff.Left - reference.TakeOff.Left;
        float db = run.TakeOff.Bottom - reference.TakeOff.Bottom;
        float dv = run.TakeOff.Vx - reference.TakeOff.Vx;
        if (run.Preparation < 0)
            return "it never issued a jump tick, so there is no arc to compare";
        string entry = MathF.Abs(dl) > .05f || MathF.Abs(db) > .05f || MathF.Abs(dv) > .01f
            ? $"the arcs start from different states — take-off left {dl:+0.0;-0.0}, bottom {db:+0.0;-0.0}, vx {dv:+0.00;-0.00}"
            : "the arcs start from the same state";
        int a = reference.Preparation, b = run.Preparation;
        for (int k = 0; a + k < reference.Frames.Count && b + k < run.Frames.Count; k++)
        {
            BodyState x = reference.Frames[a + k].After, y = run.Frames[b + k].After;
            if (MathF.Abs(x.Left - y.Left) <= .5f && MathF.Abs(x.Bottom - y.Bottom) <= .5f)
                continue;
            return $"{entry}; first tick apart is flight tick {k + 1}, left {x.Left:F1} against {y.Left:F1}, bottom {x.Bottom:F1} against {y.Bottom:F1}";
        }
        int flown = Math.Min(reference.Frames.Count - a, run.Frames.Count - b);
        return $"{entry}; the {flown} flight ticks they share stay within half a pixel";
    }

    private static string Fmt(Point p) => $"{p.X},{p.Y}";
}
