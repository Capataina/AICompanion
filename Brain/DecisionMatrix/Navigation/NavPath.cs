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

public readonly record struct NavStep(Point Tile, MoveKind Kind);

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
