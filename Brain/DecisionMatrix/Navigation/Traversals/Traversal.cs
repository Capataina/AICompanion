#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// An edge as the tiles alone decide it: the step, its move cost before any price the tick
/// sets (enemies, submersion, lava), how many rows it falls (so the one-way rule can be applied
/// on read) and whether it passes through the air between two tiles.
/// </summary>
public readonly record struct NavEdge(NavStep Step, float Move, int Fall, bool Swept);

/// <summary>
/// Why a step cannot be completed, named so the strike, the ban and the scenario capture each
/// read the reason and not a silence: the body stood too long (Timeout), came down on a tile the
/// step did not promise (Misland), pressed a shape it cannot pass (Blocked), or the motion rule
/// could not resolve where it is (Stuck).
/// </summary>
public enum TraversalFault { None, Timeout, Misland, Blocked, Stuck }

/// <summary>
/// One kind of move the body can make, owned once: the same object proves the move for the
/// planner (Candidates, every edge of this kind out of a node, each simulated or tested with the
/// body's own rule) and performs it for the follower (Steer, the controls for this tick; Done, the
/// step is complete; Check, a typed reason it cannot be). The two halves share their state and
/// their arithmetic, which is what makes a proven path an executable one: the body standing still
/// with a valid path in hand, the defect of runs 3, 5, 6 and 7 (2026-09-08), was every time a
/// move proved by one rule and performed by another. A new mobility is a new subclass and one
/// line in <see cref="Fresh"/>; the planner, the follower, the replay and the reflex rollouts
/// pick it up from there.
/// </summary>
public abstract class Traversal
{
    /// <summary>How close the feet come to a step's feet point before the step counts as reached.</summary>
    public const float ArriveSlack = 10f;

    public abstract MoveKind Kind { get; }

    /// <summary>Every edge of this kind out of a node (its tile and the mobility state the body arrives with), proven; <paramref name="here"/> is the pose at the node when one exists. A step's own Mobility is the state at its landing; a move that spends or restores a counter sets it, and every move today leaves it as it found it.</summary>
    public abstract IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava);

    /// <summary>The follower has started performing this step; run state for it begins here.</summary>
    public virtual void Begin(NavStep step) { }

    /// <summary>The controls that perform this step from where the body is now; <paramref name="next"/> is the step after it, so a move can arrive the way the next one starts.</summary>
    public abstract Controls Steer(BodyState live, NavStep step, NavStep? next);

    /// <summary>
    /// The step is part way through a move that a fresh plan would undo: a jump backing away
    /// to its runway mark or running in from it. The navigator's cadence replan waits while
    /// this is true, because a plan made from the mark offered the mirror jump from the same
    /// take-off, which the body moving away from it jumped at once, and the next plan sent it
    /// back for the first, so the body circled between two jumps for eight thousand ticks with
    /// no fault (the follow harness on run 7's platform cap, 2026-09-08). A stuck body and a
    /// moved goal still replan, so a run-up that never ends is still a strike.
    /// </summary>
    public virtual bool MidMove => false;

    /// <summary>
    /// How many rows this kind of move can climb from a standing start, which is what decides
    /// whether a fall is worth asking about: a drop no deeper than the best climb is recoverable
    /// by construction and never needs a probe. Zero for a move that cannot gain height.
    /// </summary>
    public virtual int ClimbTiles => 0;

    /// <summary>A move the body makes from rest at its start tile: a descent, a standing jump, or a walk whose landing depends on arriving slowly. The walk before it coasts to rest on its point instead of arriving at speed.</summary>
    public static bool StartsFromRest(NavStep step)
        => step.Kind is MoveKind.Drop or MoveKind.FallThrough || (step.Kind == MoveKind.Jump && step.StartVx == 0f) || step.FromRest;

    /// <summary>A standing body has settled: still enough that a move proven from rest begins as it was proven.</summary>
    public const float RestSpeed = 0.6f;

    /// <summary>The step is complete: standing within the slack of its feet point; <paramref name="next"/> is the step after it, because a step before a move proven from rest is complete only once the body is at rest.</summary>
    public virtual bool Done(BodyState live, NavStep step, NavStep? next)
        => live.OnGround && Vector2.Distance(live.Feet, NavGrid.FeetWorld(step.Tile)) < ArriveSlack;

    /// <summary>The body has settled enough for the next step to begin as it was proven: at rest where the next move starts from rest, anything otherwise.</summary>
    protected static bool SettledFor(BodyState live, NavStep? next)
        => next is not NavStep n || !StartsFromRest(n) || MathF.Abs(live.Vx) <= RestSpeed;

    /// <summary>Why the step cannot be completed, or None.</summary>
    public virtual TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if (live.Stuck)
            return TraversalFault.Stuck;
        if (ticksOnStep > Allowance(step))
            return TraversalFault.Timeout;
        return TraversalFault.None;
    }

    /// <summary>How many ticks a step may take before it has failed: twice what was proven, plus a second of slack for the approach.</summary>
    protected virtual int Allowance(NavStep step) => Math.Max(90, step.Ticks * 2 + 60);

    /// <summary>A body that has come to rest covering neither the step's tile nor the one it left, after having left the ground for it.</summary>
    protected static bool LandedElsewhere(BodyState live, NavStep step)
        => live.OnGround && !live.Covers(step.Tile) && !live.Covers(step.From);

    /// <summary>A fresh set of every traversal, one instance each, for a follower to own its run state.</summary>
    public static Traversal[] Fresh() => new Traversal[] { new WalkTraversal(), new JumpTraversal(), new DropTraversal(), new FallThroughTraversal() };

    /// <summary>The set the planner generates edges with; Candidates keeps no run state, so one set serves every search.</summary>
    public static readonly Traversal[] Planning = Fresh();

    /// <summary>
    /// The tallest climb any move in the planning set can make, taken from the set rather than
    /// written down, so a new mobility widens it the day its traversal is registered and no rule
    /// about what the body can escape has to be edited. A fall deeper than this is the only kind
    /// worth asking whether it has a way back, and asking about too many falls costs a bounded
    /// probe while asking about too few strands the body, so the error is deliberately one-sided.
    /// </summary>
    public static readonly int ClimbReachTiles = Max(Planning);

    private static int Max(Traversal[] set)
    {
        int best = 0;
        foreach (Traversal t in set)
            best = Math.Max(best, t.ClimbTiles);
        return best;
    }

    /// <summary>Ticks a body takes to fall so many rows from rest under NPC gravity, for a descent's expected duration.</summary>
    public static int FallTicks(int rows)
    {
        float vy = 0f, fallen = 0f;
        int ticks = 0;
        while (fallen < rows * 16f && ticks < 600)
        {
            vy = BodyPhysics.StepFall(vy);
            fallen += vy;
            ticks++;
        }
        return ticks;
    }
}
