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

    /// <summary>Every edge of this kind out of a node, proven; <paramref name="here"/> is the pose at the node when one exists.</summary>
    public abstract IEnumerable<NavEdge> Candidates(Point tile, BodyPhysics.Pose? here, bool lava);

    /// <summary>The follower has started performing this step; run state for it begins here.</summary>
    public virtual void Begin(NavStep step) { }

    /// <summary>The controls that perform this step from where the body is now.</summary>
    public abstract Controls Steer(BodyState live, NavStep step);

    /// <summary>The step is complete: standing within the slack of its feet point.</summary>
    public virtual bool Done(BodyState live, NavStep step)
        => live.OnGround && Vector2.Distance(live.Feet, NavGrid.FeetWorld(step.Tile)) < ArriveSlack;

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

    /// <summary>A body that has come to rest on a tile the step never promised, after having left the ground for it.</summary>
    protected static bool LandedElsewhere(BodyState live, NavStep step)
        => live.OnGround && live.FeetTile != step.Tile && live.FeetTile != step.From;

    /// <summary>A fresh set of every traversal, one instance each, for a follower to own its run state.</summary>
    public static Traversal[] Fresh() => new Traversal[] { new WalkTraversal(), new JumpTraversal(), new DropTraversal(), new FallThroughTraversal() };

    /// <summary>The set the planner generates edges with; Candidates keeps no run state, so one set serves every search.</summary>
    public static readonly Traversal[] Planning = Fresh();

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
