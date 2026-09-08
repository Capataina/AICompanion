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
    /// <summary>
    /// When set, every search leaves the tiles it closed here, so the replay tool can draw
    /// the region a failed search reached. Null in the game: the copy is never made.
    /// </summary>
    public static HashSet<Point>? TraceClosed;

    public static NavPath? Find(Point start, Point goal, int budget, out int expansions)
    {
        expansions = 0;
        var open = new SortedSet<Open>(new OpenComparer());
        var g = new Dictionary<Point, float>();
        var cameFrom = new Dictionary<Point, (Point from, MoveKind kind)>();
        var closed = TraceClosed ?? new HashSet<Point>();
        closed.Clear();
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
            // A partial end is chosen by closeness alone, so a lava tile must not be eligible:
            // its price steers a whole path round it but cannot stop it being the nearest.
            if (h < nearestH && NavGrid.LavaTilesAt(tile.X, tile.Y) == 0)
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

    /// <summary>
    /// Every feet tile a walker can reach from <paramref name="start"/> over the same edges the
    /// search uses, breadth first, up to <paramref name="budget"/> tiles. <paramref name="complete"/>
    /// is true when the region ran out before the budget did, in which case a tile not in the set
    /// is truly unreachable; otherwise a tile not in the set is unknown. One flood, reused for
    /// every candidate, is what makes "can I get there" affordable for a box of spots.
    /// </summary>
    public static HashSet<Point> Region(Point start, int budget, out bool complete)
    {
        var seen = new HashSet<Point> { start };
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        complete = true;
        while (queue.Count > 0)
        {
            if (seen.Count >= budget)
            {
                complete = false;
                break;
            }
            Point tile = queue.Dequeue();
            foreach ((Point next, _, _) in Neighbours(tile))
            {
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }
        return seen;
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
    /// through the platform underfoot to the first standable tile below it. Jump to any
    /// standable tile in the jump box where the follower's own jump, simulated tick by tick
    /// against the shapes, lands in that tile. From inside liquid every move costs more, and
    /// the simulated jump moves at half speed while wet because the game halves a wet NPC's
    /// movement; the search then prefers walking out along the floor to jumping in place.
    /// </summary>
    private static IEnumerable<(Point, MoveKind, float)> Neighbours(Point t)
    {
        bool wet = NavGrid.IsLiquid(t.X, t.Y);
        float costScale = wet ? 2f : 1f;
        bool lava = AllowLava;

        BodyPhysics.Pose? here = NavGrid.StandAt(t.X, t.Y, lava);
        foreach (int dir in new[] { -1, 1 })
        {
            int nx = t.X + dir;
            // Walk to the next column on the same row, a step up, or a step down, whichever
            // poses exist and can be slid to. A slope lowers the feet a row without an edge
            // to fall off, so the step down is a walk like the others and not a drop.
            bool walked = false;
            foreach ((int dy, float cost) in new[] { (0, 1f), (-1, 1.5f), (1, 1.2f) })
            {
                if (NavGrid.StandAt(nx, t.Y + dy, lava) is not BodyPhysics.Pose there)
                    continue;
                if (here is BodyPhysics.Pose h && !BodyPhysics.CanSlide(NavGrid.World, h, there))
                    continue;
                walked = true;
                yield return (new Point(nx, t.Y + dy), MoveKind.Walk, Price(nx, t.Y + dy, cost * costScale));
            }
            if (NavGrid.IsBodyClear(nx, t.Y))
            {
                // Edge: drop to the first standable tile below; one row down was a walk above.
                // Offered beside any walk, not instead of one: the column past a shaft's lip is
                // standable by a two-pixel overhang, and a walk onto that lip must not hide the
                // descent. A landing is asked for before the shape is called a block, because on
                // a half block or a floor slope the feet rest inside the tile itself.
                for (int dy = 1; dy <= NavGrid.MaxDropTiles; dy++)
                {
                    if (dy >= 2 && NavGrid.IsStandable(nx, t.Y + dy, lava))
                    {
                        yield return (new Point(nx, t.Y + dy), MoveKind.Drop, PriceSwept(t, new Point(nx, t.Y + dy), (1f + dy * 0.2f) * costScale));
                        break;
                    }
                    if (NavGrid.IsBlock(nx, t.Y + dy))
                        break;
                }
            }
        }

        // Standing on a platform: fall through it to the first standable tile below, the way a
        // player presses down. A mine shaft capped with platforms is otherwise a ceiling.
        if (NavGrid.IsPlatformUnder(t.X, t.Y))
        {
            for (int dy = 2; dy <= NavGrid.MaxDropTiles; dy++)
            {
                // A landing inside a half block or a floor slope is asked for before the shape
                // is called a block, as in the drop scan.
                if (NavGrid.IsStandable(t.X, t.Y + dy, lava))
                {
                    yield return (new Point(t.X, t.Y + dy), MoveKind.FallThrough, PriceSwept(t, new Point(t.X, t.Y + dy), (1f + dy * 0.2f) * costScale));
                    break;
                }
                if (NavGrid.IsBlock(t.X, t.Y + dy))
                {
                    break;
                }
            }
        }

        // Jumps: the follower's own jump, simulated tick by tick against the shapes, to every
        // standable tile in the search box; an edge exists only where the simulated body lands
        // in exactly that tile, so the planner offers the jumps the body makes and no others. The
        // box (JumpHeightTiles up, JumpGapTiles across, two rows down for a gap jumped downhill) is
        // only where to look; the arc decides. A jump straight up onto the tile above needs a
        // platform to pass through, which Fits allows and a block refuses.
        if (here is not BodyPhysics.Pose fromPose || NavGrid.IsBlock(t.X, t.Y - NavGrid.BodyHeightTiles))
            yield break;
        for (int dx = -NavGrid.JumpGapTiles; dx <= NavGrid.JumpGapTiles; dx++)
        {
            for (int ny = t.Y - NavGrid.JumpHeightTiles; ny <= t.Y + 2; ny++)
            {
                int nx = t.X + dx;
                if ((dx == 0 && ny >= t.Y) || !NavGrid.IsStandable(nx, ny, lava))
                    continue;
                int rise = t.Y - ny;
                // The same scale the follower picks for this rise (Navigator's Jump step). The
                // speed the body carries into the jump is not part of a node, so the edge exists
                // if either a standing jump or a running jump lands in the tile: a path mostly
                // walks into its jumps at speed, and a jump the body cannot make from a stand
                // after a reversal shows up as a stuck count and a replan rather than a missing edge.
                float scale = rise >= 2 ? BodyPhysics.JumpScaleForTiles(rise) : 1f;
                var target = new Point(nx, ny);
                int ticks = -1;
                foreach (float startVx in new[] { 0f, Math.Sign(dx) * BodyPhysics.WalkSpeed })
                {
                    if (BodyPhysics.SimulateJump(NavGrid.World, fromPose, scale, startVx, nx, MaxJumpTicks, out int flight) is not BodyPhysics.Pose landing)
                        continue;
                    var landed = new Point((int)Math.Floor(landing.CentreX / 16f), BodyPhysics.FeetRow(landing.Bottom));
                    if (landed == target && (ticks < 0 || flight < ticks))
                        ticks = flight;
                }
                if (ticks < 0)
                    continue;
                yield return (target, MoveKind.Jump, PriceSwept(t, target, JumpCost(ticks) * costScale));
            }
        }
    }

    /// <summary>The longest flight the planner follows before giving up on a landing.</summary>
    private const int MaxJumpTicks = 120;

    /// <summary>
    /// A jump costs its flight time in walked tiles plus one, so a jump is taken only where the
    /// walk of the same width does not exist, and a long arc costs more than a short hop.
    /// </summary>
    private static float JumpCost(int ticks) => 1f + ticks * BodyPhysics.WalkSpeed / 16f;

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
        if (InAvoid(Body(x, y)))
            move += AvoidCost;
        return move;
    }

    /// <summary>
    /// The price of a move that passes through the air between two tiles (a drop, a fall, a
    /// jump): the destination's price, plus the enemy price once if the body's sweep between
    /// the two meets an enemy the destination alone does not, because a long drop through a
    /// flyer was otherwise free.
    /// </summary>
    private static float PriceSwept(Point from, Point to, float move)
    {
        float price = Price(to.X, to.Y, move);
        if (Avoid.Count > 0 && !InAvoid(Body(to.X, to.Y)) && InAvoid(Rectangle.Union(Body(from.X, from.Y), Body(to.X, to.Y))))
            price += AvoidCost;
        return price;
    }

    private static Rectangle Body(int x, int y) => new(x * 16, (y - NavGrid.BodyHeightTiles + 1) * 16, 16, NavGrid.BodyHeightTiles * 16);

    private static bool InAvoid(Rectangle body)
    {
        foreach (Rectangle r in Avoid)
            if (r.Intersects(body))
                return true;
        return false;
    }

    /// <summary>
    /// Rectangles the search prices as if they were a wall of lava: the bodies of reachable
    /// enemies, set by the brain each tick. A route through an enemy is what jumped the
    /// companion onto zombies on the way to a firing spot beyond them; the price makes the
    /// detour win wherever one exists and leaves the direct route for when none does.
    /// </summary>
    public static readonly List<Rectangle> Avoid = new();
    public const float AvoidCost = 30f;

}
