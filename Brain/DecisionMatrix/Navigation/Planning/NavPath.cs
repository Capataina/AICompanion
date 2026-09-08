#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// How the companion gets from one node to the next: walk (a step up counts as a walk),
/// jump, drop off an edge, or fall through the platform it stands on, which is a drop that
/// needs the body told to pass the platform for that tick.
/// </summary>
public enum MoveKind { Walk, Jump, Drop, FallThrough }

/// <summary>
/// One move of a path: the feet tile it arrives at, how, and for a jump the profile the planner
/// found it with, which the follower reproduces rather than re-deriving: <paramref name="From"/>
/// is the take-off tile, <paramref name="JumpScale"/> the share of the full jump velocity and
/// <paramref name="StartVx"/> the horizontal speed the body must carry into the jump, signed, so
/// a standing hop and a full-speed running jump are two different steps and the follower backs
/// up for the run-up the second one needs. For a drop or a fall-through <paramref name="SteerX"/>
/// is the world X the body's centre falls along, because in a shaft wider than the body what it
/// lands on depends on which wall it hugs; zero when the move has no steer point.
/// <paramref name="Ticks"/> is how long the move took when it was proven (a jump's flight, a
/// fall's duration), which the follower's allowance and the executed-edge trace are read against.
/// <paramref name="FromRest"/> marks a walk that was proven from rest and lands elsewhere at the
/// walk speed (a ledge onto a narrow lip), so the step before it coasts to a stop the way it
/// does before a descent; a drop, a fall-through and a standing jump start from rest by kind.
/// </summary>
public readonly record struct NavStep(Point Tile, MoveKind Kind, Point From = default, float JumpScale = 1f, float StartVx = 0f, float SteerX = 0f, int Ticks = 0, bool FromRest = false);

/// <summary>
/// A planned route as feet tiles, first step first. Goal is the tile the search was asked
/// for; a partial path ends at the reachable tile nearest it instead, because a search that
/// ran out is still worth walking to the closest point it found.
/// </summary>
public sealed class NavPath
{
    public readonly List<NavStep> Steps;
    public readonly Point Goal;
    public readonly bool Partial;
    public int Index;

    public NavPath(List<NavStep> steps, Point goal, bool partial = false)
    {
        Steps = steps;
        Goal = goal;
        Partial = partial;
    }

    public bool Finished => Index >= Steps.Count;
    public NavStep Current => Steps[Index];
}
