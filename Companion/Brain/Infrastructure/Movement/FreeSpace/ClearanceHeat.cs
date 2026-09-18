#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// How close a point is to walls and to enemy bodies, in tiles, and the cost that closeness
/// adds. Terrain is <see cref="ClearanceField"/>; enemies are the live hazard boxes. Both
/// cap at the field's <see cref="ClearanceField.MaxTiles"/> and never refuse a spot: a two-tile
/// crack stays legal, it just costs more than open air. Parking, tool hovers and route edges
/// all read this so one heat drives standing and travel.
/// </summary>
public static class ClearanceHeat
{
    /// <summary>Same cap as the terrain field: beyond it there is nothing left to prefer.</summary>
    public static int MaxTiles => ClearanceField.MaxTiles;

    /// <summary>1 + preference / clearance, matching a route edge. Floor 0.5 so a touch is finite.</summary>
    public static float Penalty(float clearanceTiles)
        => 1f + Weights.CorridorMiddlePreference / MathF.Max(0.5f, clearanceTiles);

    /// <summary>Tiles to the nearest wall at this point, capped.</summary>
    public static float TerrainAt(ITileWorld world, Vector2 point)
        => ClearanceField.Shared.AtCorner(world, CornerGraph.NearestCorner(point));

    /// <summary>Tiles to the nearest hazard box, zero inside one, capped. Empty is the cap.</summary>
    public static float ToBoxes(Vector2 point, IReadOnlyList<Rectangle> boxes)
    {
        if (boxes.Count == 0) return MaxTiles;
        float best = MaxTiles;
        for (int i = 0; i < boxes.Count; i++)
        {
            float tiles = DistanceToBox(point, boxes[i]) / 16f;
            if (tiles < best) best = tiles;
            if (best <= 0f) return 0f;
        }
        return MathF.Max(0f, best);
    }

    /// <summary>The nearer of wall and enemy, which is one heat: closer to either costs more.</summary>
    public static float Combined(ITileWorld world, Vector2 point, IReadOnlyList<Rectangle> boxes)
        => MathF.Min(TerrainAt(world, point), ToBoxes(point, boxes));

    /// <summary>Zero inside or on the edge; otherwise the Euclidean distance to the nearest point on the box.</summary>
    public static float DistanceToBox(Vector2 point, Rectangle box)
    {
        float x = point.X < box.Left ? box.Left - point.X
            : point.X > box.Right ? point.X - box.Right
            : 0f;
        float y = point.Y < box.Top ? box.Top - point.Y
            : point.Y > box.Bottom ? point.Y - box.Bottom
            : 0f;
        return MathF.Sqrt(x * x + y * y);
    }
}
