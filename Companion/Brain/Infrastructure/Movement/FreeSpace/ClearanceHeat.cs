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

    /// <summary>
    /// One tile toward clearer walls, only when the body is in a crack (under one tile of
    /// clearance). Parking already prefers eight tiles of air; nudging every stand under the cap
    /// moved shotgun-close. The step is one tile so FireFrom arrival still covers the original pixel.
    /// </summary>
    public static Vector2 NudgeOffTerrain(ITileWorld world, Vector2 point)
    {
        if (world == null)
            return point;
        float here = TerrainAt(world, point);
        // Only a body that is actually in a crack: parking already prefers eight tiles of air,
        // and nudging every stand that is merely under the cap moved shotgun-close P3.
        if (here >= 1f)
            return point;
        Vector2 best = point;
        float bestClear = here;
        foreach (Vector2 dir in StepDirs)
        {
            Vector2 candidate = point + dir;
            if (CircleContact.Overlaps(world, candidate))
                continue;
            float clearance = TerrainAt(world, candidate);
            if (clearance > bestClear)
            {
                bestClear = clearance;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// Walk toward higher clearance, one tile at a time, stopping at the cap or when no neighbour
    /// is clearer. Never refuses a crack: a point that cannot improve is returned as it was.
    /// <paramref name="accept"/> keeps a company or combat sample inside the region that admitted it.
    /// <paramref name="enemiesOnly"/> is the combat read: walking off the floor created an air
    /// perch that dominated the far shotgun and P3 closed at low life. Company uses walls and
    /// enemies together; combat only steps away from bodies.
    /// </summary>
    public static Vector2 PreferClearer(ITileWorld world, Vector2 point, Func<Vector2, bool>? accept = null,
        int maxSteps = ClearanceField.MaxTiles, bool enemiesOnly = false)
    {
        if (world == null)
            return point;
        IReadOnlyList<Rectangle> boxes = MovementQueries.Hazards;
        float Score(Vector2 at) => enemiesOnly ? ToBoxes(at, boxes) : Combined(world, at, boxes);
        Vector2 best = point;
        float bestClear = Score(point);
        if (bestClear >= MaxTiles - 0.05f)
            return point;
        for (int i = 0; i < maxSteps; i++)
        {
            Vector2 next = best;
            float nextClear = bestClear;
            foreach (Vector2 dir in StepDirs)
            {
                Vector2 candidate = best;
                for (int s = 0; s < maxSteps; s++)
                {
                    candidate += dir;
                    if (CircleContact.Overlaps(world, candidate))
                        break;
                    if (accept != null && !accept(candidate))
                        break;
                    float clearance = Score(candidate);
                    if (clearance > nextClear + 0.05f)
                    {
                        nextClear = clearance;
                        next = candidate;
                        break;
                    }
                }
            }
            if (nextClear <= bestClear + 0.05f)
                break;
            best = next;
            bestClear = nextClear;
            if (bestClear >= MaxTiles - 0.05f)
                break;
        }
        return best;
    }

    private static readonly Vector2[] StepDirs =
    {
        new(16f, 0f), new(-16f, 0f), new(0f, 16f), new(0f, -16f),
        new(16f, 16f), new(16f, -16f), new(-16f, 16f), new(-16f, -16f),
    };

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
