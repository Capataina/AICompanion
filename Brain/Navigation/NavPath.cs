#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.Navigation;

/// <summary>How the companion gets from one node to the next.</summary>
public enum MoveKind { Walk, Jump, Drop }

public readonly record struct NavStep(Point Tile, MoveKind Kind);

/// <summary>A planned route as feet tiles, first step first. Goal is the tile the search was asked for.</summary>
public sealed class NavPath
{
    public readonly List<NavStep> Steps;
    public readonly Point Goal;
    public int Index;

    public NavPath(List<NavStep> steps, Point goal)
    {
        Steps = steps;
        Goal = goal;
    }

    public bool Finished => Index >= Steps.Count;
    public NavStep Current => Steps[Index];
}
