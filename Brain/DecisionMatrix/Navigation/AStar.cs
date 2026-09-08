#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>
/// A* over the navigation grid. Nodes are feet tiles; neighbours are generated on the
/// fly from what the body can do. Bounded by an expansion budget so a hopeless search
/// (the goal sealed off) costs a known amount and returns null rather than hanging.
/// </summary>
public static class AStar
{
    /// <summary>
    /// Lava tiles are nodes at a high cost while this is true, so a short lava crossing beats
    /// a long detour and a long one does not; the brain sets it from the companion's life
    /// each tick, and false makes lava impassable as before.
    /// </summary>
    public static bool AllowLava;

    /// <summary>Cost added per lava tile in a node's column, against a walk step of one.</summary>
    public const float LavaTileCost = 40f;

    /// <summary>Multiplier on every edge into a node where the body's head is under liquid, on top of the wet-feet multiplier.</summary>
    public const float SubmergedCost = 2f;

    private readonly record struct Open(Point Tile, float F);

    private sealed class OpenComparer : IComparer<Open>
    {
        public int Compare(Open a, Open b)
        {
            int c = a.F.CompareTo(b.F);
            if (c != 0) return c;
            c = a.Tile.X.CompareTo(b.Tile.X);
            return c != 0 ? c : a.Tile.Y.CompareTo(b.Tile.Y);
        }
    }

    /// <summary>
    /// Path from <paramref name="start"/> to <paramref name="goal"/>, both feet tiles, or
    /// null when none is found inside <paramref name="budget"/> expansions.
    /// </summary>
    public static NavPath? Find(Point start, Point goal, int budget, out int expansions)
    {
        expansions = 0;
        var open = new SortedSet<Open>(new OpenComparer());
        var g = new Dictionary<Point, float>();
        var cameFrom = new Dictionary<Point, (Point from, MoveKind kind)>();
        var closed = new HashSet<Point>();
        Point nearest = start;
        float nearestH = H(start, goal);

        g[start] = 0f;
        open.Add(new Open(start, nearestH));

        while (open.Count > 0)
        {
            Open current = open.Min;
            open.Remove(current);
            Point tile = current.Tile;
            if (closed.Contains(tile))
                continue;
            closed.Add(tile);

            if (tile == goal)
                return Rebuild(cameFrom, start, goal, partial: false);

            float h = H(tile, goal);
            if (h < nearestH)
            {
                nearestH = h;
                nearest = tile;
            }
            if (++expansions > budget)
                break;

            float gHere = g[tile];
            foreach ((Point next, MoveKind kind, float cost) in Neighbours(tile))
            {
                if (closed.Contains(next))
                    continue;
                float tentative = gHere + cost;
                if (g.TryGetValue(next, out float known) && tentative >= known)
                    continue;
                g[next] = tentative;
                cameFrom[next] = (tile, kind);
                open.Add(new Open(next, tentative + H(next, goal)));
            }
        }
        // Out of budget or out of region: the closest tile the search reached is still the
        // best place to be, and walking there beats standing where the goal went out of view.
        return nearest == start ? null : Rebuild(cameFrom, start, nearest, partial: true);
    }

    private static float H(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return dx + dy * 0.5f;
    }

    private static NavPath Rebuild(Dictionary<Point, (Point from, MoveKind kind)> cameFrom, Point start, Point end, bool partial)
    {
        var steps = new List<NavStep>();
        Point at = end;
        while (at != start)
        {
            (Point from, MoveKind kind) = cameFrom[at];
            steps.Add(new NavStep(at, kind));
            at = from;
        }
        steps.Reverse();
        return new NavPath(steps, end, partial);
    }

    /// <summary>
    /// What the body can reach from a feet tile. Walk to a standable neighbour on the same
    /// row or one up (a step). Drop off an edge to the first standable tile below. Fall
    /// through the platform underfoot to the first standable tile below it. Jump to
    /// standable tiles up to JumpHeightTiles up and JumpGapTiles across when the column
    /// above the start and the body at the landing are clear. From inside liquid every move
    /// costs more and the jump envelope is halved, because the game halves a wet NPC's
    /// movement; the search then prefers walking out along the floor to jumping in place.
    /// </summary>
    private static IEnumerable<(Point, MoveKind, float)> Neighbours(Point t)
    {
        bool wet = NavGrid.IsLiquid(t.X, t.Y);
        float costScale = wet ? 2f : 1f;
        bool lava = AllowLava;

        foreach (int dir in new[] { -1, 1 })
        {
            int nx = t.X + dir;
            if (NavGrid.IsStandable(nx, t.Y, lava))
                yield return (new Point(nx, t.Y), MoveKind.Walk, Price(nx, t.Y, 1f * costScale));
            else if (NavGrid.IsStandable(nx, t.Y - 1, lava) && NavGrid.IsBodyClear(t.X, t.Y - 1))
                yield return (new Point(nx, t.Y - 1), MoveKind.Walk, Price(nx, t.Y - 1, 1.5f * costScale));
            else if (NavGrid.IsBodyClear(nx, t.Y))
            {
                // Edge: drop to the first standable tile below.
                for (int dy = 1; dy <= NavGrid.MaxDropTiles; dy++)
                {
                    if (NavGrid.IsSolid(nx, t.Y + dy))
                        break;
                    if (NavGrid.IsStandable(nx, t.Y + dy, lava))
                    {
                        yield return (new Point(nx, t.Y + dy), MoveKind.Drop, Price(nx, t.Y + dy, (1f + dy * 0.2f) * costScale));
                        break;
                    }
                }
            }
        }

        // Standing on a platform: fall through it to the first standable tile below, the way a
        // player presses down. A mine shaft capped with platforms is otherwise a ceiling.
        if (NavGrid.IsPlatformUnder(t.X, t.Y))
        {
            for (int dy = 2; dy <= NavGrid.MaxDropTiles; dy++)
            {
                if (NavGrid.IsSolid(t.X, t.Y + dy))
                    break;
                if (NavGrid.IsStandable(t.X, t.Y + dy, lava))
                {
                    yield return (new Point(t.X, t.Y + dy), MoveKind.FallThrough, Price(t.X, t.Y + dy, (1f + dy * 0.2f) * costScale));
                    break;
                }
            }
        }

        // Jumps: need headroom above the start.
        int maxUp = wet ? NavGrid.JumpHeightTiles / 2 : NavGrid.JumpHeightTiles;
        int maxGap = wet ? NavGrid.JumpGapTiles / 2 : NavGrid.JumpGapTiles;
        int headroom = 0;
        while (headroom < maxUp && !NavGrid.IsSolid(t.X, t.Y - NavGrid.BodyHeightTiles - headroom))
            headroom++;
        if (headroom == 0)
            yield break;

        for (int dx = -maxGap; dx <= maxGap; dx++)
        {
            for (int up = 1; up <= headroom; up++)
            {
                int nx = t.X + dx, ny = t.Y - up;
                if (!NavGrid.IsStandable(nx, ny, lava))
                    continue;
                // Coarse arc check: the body must be clear at the apex column above the start and at the landing.
                if (!NavGrid.IsBodyClear(t.X, t.Y - up) || !ColumnClearBetween(t.X, nx, ny))
                    continue;
                yield return (new Point(nx, ny), MoveKind.Jump, Price(nx, ny, JumpCost(dx, up) * costScale));
            }
            // Gap jump on the same row, only over a real gap: with every tile between standable
            // the walk exists and is cheaper, and offering the jump as well priced a four-tile
            // hop level with a four-tile walk, which is how the body hopped along flat ground.
            if (Math.Abs(dx) >= 2 && NavGrid.IsStandable(t.X + dx, t.Y, lava) && !RowStandableBetween(t.X, t.X + dx, t.Y, lava) && ColumnClearBetween(t.X, t.X + dx, t.Y - 1))
                yield return (new Point(t.X + dx, t.Y), MoveKind.Jump, Price(t.X + dx, t.Y, JumpCost(dx, 0) * costScale));
        }
    }

    /// <summary>A jump always costs more than walking the same tiles, so it is taken only where the walk does not exist.</summary>
    private static float JumpCost(int dx, int up) => 2f + Math.Abs(dx) * 1f + up * 0.5f;

    private static bool RowStandableBetween(int x0, int x1, int y, bool lava)
    {
        int step = Math.Sign(x1 - x0);
        for (int x = x0 + step; x != x1; x += step)
            if (!NavGrid.IsStandable(x, y, lava))
                return false;
        return true;
    }

    /// <summary>
    /// What arriving at a node costs on top of the move: a submerged head multiplies (a
    /// drowning tile is dearer than a wet one), and each lava tile in the column adds a flat
    /// price, so a one-tile lava stream is crossed when the detour is long and refused when
    /// it is short. Lava is only ever offered while <see cref="AllowLava"/> is true.
    /// </summary>
    private static float Price(int x, int y, float move)
    {
        if (NavGrid.HeadSubmergedAt(x, y))
            move *= SubmergedCost;
        int lavaTiles = NavGrid.LavaTilesAt(x, y);
        if (lavaTiles > 0)
            move += lavaTiles * LavaTileCost;
        if (Avoid.Count > 0)
        {
            Rectangle body = new(x * 16, (y - NavGrid.BodyHeightTiles + 1) * 16, 16, NavGrid.BodyHeightTiles * 16);
            foreach (Rectangle r in Avoid)
                if (r.Intersects(body))
                {
                    move += AvoidCost;
                    break;
                }
        }
        return move;
    }

    /// <summary>
    /// Rectangles the search prices as if they were a wall of lava: the bodies of reachable
    /// enemies, set by the brain each tick. A route through an enemy is what jumped the
    /// companion onto zombies on the way to a firing spot beyond them; the price makes the
    /// detour win wherever one exists and leaves the direct route for when none does.
    /// </summary>
    public static readonly List<Rectangle> Avoid = new();
    public const float AvoidCost = 30f;

    private static bool ColumnClearBetween(int x0, int x1, int y)
    {
        int step = Math.Sign(x1 - x0);
        if (step == 0)
            return true;
        for (int x = x0 + step; x != x1; x += step)
            if (!NavGrid.IsBodyClear(x, y))
                return false;
        return NavGrid.IsBodyClear(x1, y);
    }
}
