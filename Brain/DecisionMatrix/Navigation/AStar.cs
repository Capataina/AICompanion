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

    /// <summary>
    /// A drop deeper than the body can jump back up is a door that closes behind it, and it is
    /// offered only while this is true: the brain sets it for a tick in which the companion is
    /// following or guarding the player, or saving itself, because the player being down there
    /// is the one reason to go where there is no way back. A hunt took one in run 5 (2026-09-08,
    /// tick 7155, fifteen rows into a sealed pocket) and stood at its rim for the rest of the
    /// session. The reach flood the positioner uses shares the rule, so a firing spot at the
    /// bottom of a pit is not offered either. The replay tool leaves it true: a scenario
    /// records where the player went.
    /// </summary>
    public static bool AllowOneWayDrops = true;

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
        var cameFrom = new Dictionary<Point, NavStep>();
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
            foreach ((NavStep step, float cost) in Neighbours(tile))
            {
                Point next = step.Tile;
                if (closed.Contains(next))
                    continue;
                float tentative = gHere + cost;
                if (g.TryGetValue(next, out float known) && tentative >= known)
                    continue;
                g[next] = tentative;
                cameFrom[next] = step;
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
            foreach ((NavStep step, _) in Neighbours(tile))
            {
                if (seen.Add(step.Tile))
                    queue.Enqueue(step.Tile);
            }
        }
        return seen;
    }

    private static float H(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return dx + dy * 0.5f;
    }

    private static NavPath Rebuild(Dictionary<Point, NavStep> cameFrom, Point start, Point end, bool partial)
    {
        var steps = new List<NavStep>();
        Point at = end;
        while (at != start)
        {
            NavStep step = cameFrom[at];
            steps.Add(step);
            at = step.From;
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
    private static IEnumerable<(NavStep, float)> Neighbours(Point t)
    {
        (Point, bool) key = (t, AllowLava);
        RawEdge[] edges;
        if (!CacheEdges)
            edges = System.Linq.Enumerable.ToArray(RawEdges(t, AllowLava));
        else if (!edgeCache.TryGetValue(key, out CachedEdges cached) || unchecked(Clock - cached.Born) > EdgeCacheLifeTicks)
        {
            edges = System.Linq.Enumerable.ToArray(RawEdges(t, AllowLava));
            edgeCache[key] = new CachedEdges(edges, Clock);
        }
        else
            edges = cached.Edges;
        foreach (RawEdge e in edges)
        {
            if (e.Fall > NavGrid.JumpHeightTiles && !AllowOneWayDrops)
                continue;
            yield return (e.Step, e.Swept ? PriceSwept(t, e.Step.Tile, e.Move) : Price(e.Step.Tile.X, e.Step.Tile.Y, e.Move));
        }
    }

    /// <summary>
    /// An edge as the tiles alone decide it: the step, its move cost before any price the
    /// tick sets (enemies, submersion, lava), how many rows it falls (so the one-way rule can
    /// be applied on read) and whether it passes through the air between two tiles.
    /// </summary>
    private readonly record struct RawEdge(NavStep Step, float Move, int Fall, bool Swept);

    /// <summary>
    /// The edges of every tile a search has opened, kept between searches. Simulating a tile's
    /// jumps costs about a sixth of a millisecond in the game and the positioner's reach flood
    /// opens hundreds of tiles every few ticks, most of them the same tiles as last time, which
    /// on the sixth run of 2026-09-08 was sixty milliseconds of one tick in every twelve. The
    /// geometry only changes when a tile does, and a changed tile can only change the edges of
    /// the tiles whose scans reach it, so a kill or a placement drops that box of entries
    /// (<see cref="TileChanged"/>) and nothing else: the first version dropped the whole cache,
    /// which while mining (a tile dying every few ticks) is no cache at all. Each entry also
    /// carries the tick it was made and is remade after <see cref="EdgeCacheLifeTicks"/>, because
    /// the game announces only what a player or a pickaxe does to a tile: liquids settling, sand
    /// landing, a door opening and a boulder rolling change the world through no hook the mod
    /// can hear. Prices are applied on read, because enemies move every tick.
    /// </summary>
    private static readonly Dictionary<(Point, bool), CachedEdges> edgeCache = new();
    private readonly record struct CachedEdges(RawEdge[] Edges, uint Born);

    /// <summary>Off, every search simulates every edge afresh: the replay's --no-cache, which proves the cache changes no verdict.</summary>
    public static bool CacheEdges = true;

    /// <summary>
    /// The brain's tick, for the cache's expiry; the replay tool leaves it at zero and the cache
    /// lives for the block. Unsigned like the game's own counter, so an age is the unchecked
    /// difference and still reads right after the counter wraps (a long widened from the uint
    /// read a negative age there and never expired the entry).
    /// </summary>
    public static uint Clock;

    /// <summary>
    /// How long a tile's edges are trusted without a tile change announcing itself: three
    /// seconds, long enough that a settled cave costs one remake per tile in that time and
    /// short enough that a pool draining or a sand column landing is seen before the body walks
    /// far on the old picture. A stale entry is a price, not a wall: liquid and lava are priced
    /// on read from the live tile, so the worst of it is a walk edge priced dry through a tile
    /// that is now wet.
    /// </summary>
    public const uint EdgeCacheLifeTicks = 180;

    /// <summary>
    /// How far sideways a tile's scans read, summed from the scan itself so the box cannot fall
    /// behind it: the drop starts in the neighbouring column, the open span reaches
    /// <see cref="NavGrid.OpenSpanReach"/> columns past that and the body is put against its far
    /// wall, the fall drifts <see cref="DriftPerRow"/> pixels a row for up to
    /// <see cref="NavGrid.MaxDropTiles"/> rows, and the shape tests around the landing read the
    /// body's width, which spans two columns. The Codex review of 87e8d20 found a support
    /// nineteen columns out changing an edge the hand-set sixteen kept.
    /// </summary>
    public const int EdgeReachX = 1 + NavGrid.OpenSpanReach + (NavGrid.MaxDropTiles * (int)DriftPerRow + 15) / 16 + 2;

    /// <summary>
    /// A tile at (<paramref name="x"/>, <paramref name="y"/>) is no longer what it was: drop the
    /// cached edges of every tile whose scans could have read it. A tile's edges look down as
    /// far as a drop can fall and up as far as a jump can rise plus the body's own height, and
    /// sideways by <see cref="EdgeReachX"/>, so the entries dropped are the tiles that lie
    /// within the drop depth above the change, within the jump box below it, and within reach
    /// either side. A pickaxe hit that only cracks the tile is not a change and is not announced.
    /// </summary>
    public static void TileChanged(int x, int y)
    {
        if (edgeCache.Count == 0)
            return;
        int minY = y - NavGrid.MaxDropTiles - 1;
        int maxY = y + NavGrid.JumpHeightTiles + NavGrid.BodyHeightTiles + 2;
        var stale = new List<(Point, bool)>();
        foreach ((Point, bool) key in edgeCache.Keys)
        {
            Point t = key.Item1;
            if (Math.Abs(t.X - x) <= EdgeReachX && t.Y >= minY && t.Y <= maxY)
                stale.Add(key);
        }
        foreach ((Point, bool) key in stale)
            edgeCache.Remove(key);
    }

    /// <summary>Drop every cached edge: a new world, or the replay tool starting a block or a flood it reads the edge flag from.</summary>
    public static void InvalidateEdges() => edgeCache.Clear();

    /// <summary>How many tiles hold cached edges right now, for the overlay.</summary>
    public static int CachedTiles => edgeCache.Count;

    private static IEnumerable<RawEdge> RawEdges(Point t, bool lava)
    {
        bool wet = NavGrid.IsLiquid(t.X, t.Y);
        float costScale = wet ? 2f : 1f;

        BodyPhysics.Pose? here = NavGrid.StandAt(t.X, t.Y, lava);
        foreach (int dir in new[] { -1, 1 })
        {
            int nx = t.X + dir;
            // Walk to the next column on the same row, a step up, or a step down, whichever
            // poses exist and can be slid to. A slope lowers the feet a row without an edge
            // to fall off, so the step down is a walk like the others and not a drop.
            foreach ((int dy, float cost) in new[] { (0, 1f), (-1, 1.5f), (1, 1.2f) })
            {
                if (NavGrid.StandAt(nx, t.Y + dy, lava) is not BodyPhysics.Pose there)
                    continue;
                if (here is BodyPhysics.Pose h && !BodyPhysics.CanSlide(NavGrid.World, h, there))
                    continue;
                yield return new RawEdge(new NavStep(new Point(nx, t.Y + dy), MoveKind.Walk, t), cost * costScale, 0, false);
            }
            // Edge: step off the lip and fall to the first surface that holds the body where the
            // follower steers it, the middle of the open span beside the lip. The body is put there
            // and asked what it rests on, row by row, rather than the grid being asked whether a
            // tile is standable: a tile is standable by a two-pixel overhang, which the body in the
            // open span is not on, and a platform one row down is not a block, which is how a
            // real support one row down was scanned past and a drop offered that the body at the
            // lip never made (run 5, 2026-09-08). A rest one row down is the walk step-down's job
            // and yields no edge; two or more rows is the drop. The column beside the lip must be
            // open underneath as well as clear: a platform there is a floor the body walks onto
            // and stands on, and the way down through it is the fall-through edge from that tile,
            // never a drop from this one (run 6, 2026-09-08: a shaft capped with platforms, the
            // body parked on the lip for the rest of the run pushing at a drop it could not make).
            if (NavGrid.IsBodyClear(nx, t.Y) && !NavGrid.IsSupport(nx, t.Y + 1))
                foreach ((Point drop, int fall, float steerX) in Landings(nx, t.Y, throughPlatform: false, lava))
                    if (fall >= 2)
                        yield return new RawEdge(new NavStep(drop, MoveKind.Drop, t, SteerX: steerX), (1f + fall * 0.2f) * costScale, fall, true);
        }

        // Standing on a platform: fall through it to the first surface below, the way a player
        // presses down. A mine shaft capped with platforms is otherwise a ceiling.
        if (NavGrid.IsPlatformUnder(t.X, t.Y))
            foreach ((Point through, int depth, float steerX) in Landings(t.X, t.Y, throughPlatform: true, lava))
                if (depth >= 2)
                    yield return new RawEdge(new NavStep(through, MoveKind.FallThrough, t, SteerX: steerX), (1f + depth * 0.2f) * costScale, depth, true);

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
            // A jump that lands level or lower is only worth flying from an edge: with the next
            // tile in that direction standable, the walk reaches everything a level jump would at
            // a lower price, and the edge node past the walk offers the gap jump itself. This is
            // what keeps a flat floor from simulating sixteen jumps per node for nothing.
            bool edgeThisWay = dx != 0 && !NavGrid.IsStandable(t.X + Math.Sign(dx), t.Y, lava);
            for (int ny = t.Y - NavGrid.JumpHeightTiles; ny <= t.Y + 2; ny++)
            {
                int nx = t.X + dx;
                if ((dx == 0 && ny >= t.Y) || (ny >= t.Y && !edgeThisWay))
                    continue;
                if (NavGrid.StandAt(nx, ny, lava) is not BodyPhysics.Pose targetPose)
                    continue;
                var target = new Point(nx, ny);
                // The rise in pixels between the two poses' bottoms, rounded up to tiles, which
                // differs from the row difference on a slope or a half block by a whole scale step.
                int rise = (int)Math.Ceiling((fromPose.Bottom - targetPose.Bottom) / 16f);
                // Every profile that could land, lowest arc and fastest start first, and the first
                // that does is the edge. The profile is carried on the step: the follower makes
                // exactly this jump, backing up for the run-up when it needs one, instead of
                // re-deriving a height from the rise and a speed from whatever it arrived with.
                foreach ((float scale, float startVx) in JumpProfiles(rise, Math.Sign(dx)))
                {
                    if (BodyPhysics.SimulateJump(NavGrid.World, fromPose, scale, startVx, nx, ny, MaxJumpTicks, out int flight) is not BodyPhysics.Pose landing)
                        continue;
                    var landed = new Point((int)Math.Floor(landing.CentreX / 16f), BodyPhysics.FeetRow(landing.Bottom));
                    if (landed != target)
                        continue;
                    yield return new RawEdge(new NavStep(target, MoveKind.Jump, t, scale, startVx), JumpCost(flight) * costScale, 0, true);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Every place a body stepping off the lip of feet tile (<paramref name="column"/>, <paramref name="feetRow"/>)
    /// can come to rest, each with how many rows it fell and the X its centre fell along. The
    /// open span beside the lip is wider than the body in most shafts, and what the body lands on
    /// depends on where in it the body falls: hugging one wall it rests on a lip that the other
    /// wall's side falls past, which is how a zigzag shaft is descended a lip at a time. So the
    /// body is dropped against the span's left wall, down its middle and against its right wall,
    /// and each distinct landing is an edge whose step tells the follower where to steer. Nothing
    /// is yielded for a way down the body cannot pass or a fall past the limit.
    /// </summary>
    private static IEnumerable<(Point tile, int fall, float steerX)> Landings(int column, int feetRow, bool throughPlatform, bool lava)
    {
        (int spanLeft, int spanRight) = NavGrid.OpenSpan(column, feetRow, throughPlatform);
        float hugLeft = spanLeft * 16f + 1f;
        float hugRight = (spanRight + 1) * 16f - BodyPhysics.Width - 1f;
        float centre = (spanLeft * 16f + (spanRight + 1) * 16f) / 2f - BodyPhysics.Width / 2f;
        var seen = new HashSet<Point>();
        foreach ((float left, int drift) in new[] { (centre, 0), (hugLeft, -1), (hugRight, 1) })
        {
            if (Landing(left, feetRow, drift, lava) is (Point tile, int fall, float restLeft) && seen.Add(tile))
                yield return (tile, fall, restLeft + BodyPhysics.Width / 2f);
        }
    }

    /// <summary>
    /// A falling body pressed toward a wall moves this far sideways per row of fall, at most:
    /// under NPC gravity the body clears a row in a couple of ticks near its top speed and in
    /// many at the start, and the motor moves it a few pixels a tick, so this is the fast end.
    /// </summary>
    private const float DriftPerRow = 6f;

    /// <summary>
    /// Where a body whose left edge is <paramref name="left"/>, feet in <paramref name="feetRow"/>,
    /// comes to rest when lowered a row at a time, how many rows it fell and where its left edge
    /// ended up: the first row whose surface holds it there is the landing, a row it does not fit
    /// in is a ceiling it cannot pass, and null is either of those failing inside the drop limit.
    /// With <paramref name="drift"/> set the body presses that way as it falls, sliding across each
    /// row as far as it fits, which is how it follows a shaft's wall into a wider segment and rests
    /// on a lip the wall above stood over. The landing must also be a node with lava as the
    /// caller allows it, so the path can continue from it.
    /// </summary>
    private static (Point, int, float)? Landing(float left, int feetRow, int drift, bool lava)
    {
        ITileWorld world = NavGrid.World;
        if (!BodyPhysics.Fits(world, left, (feetRow + 1) * 16f))
            return null;
        for (int dy = 1; dy <= NavGrid.MaxDropTiles; dy++)
        {
            int row = feetRow + dy;
            float floor = (row + 1) * 16f;
            if (drift != 0)
            {
                for (float slid = left + drift * DriftPerRow; drift * (slid - left) > 0f; slid -= drift * 2f)
                {
                    if (BodyPhysics.Fits(world, slid, floor))
                    {
                        left = slid;
                        break;
                    }
                }
            }
            if (BodyPhysics.RestBottom(world, left, row) is float rest && BodyPhysics.FeetRow(rest) == row && BodyPhysics.Fits(world, left, rest))
            {
                // The node the resting body is filed under: the column of its centre when that
                // tile is a node, else either column it covers, because a body two pixels over a
                // lip rests on the lip's tile and stands there in the game whichever column its
                // centre is in.
                int centreColumn = (int)Math.Floor((left + BodyPhysics.Width / 2f) / 16f);
                int leftColumn = (int)Math.Floor(left / 16f), rightColumn = (int)Math.Floor((left + BodyPhysics.Width - 0.02f) / 16f);
                foreach (int node in new[] { centreColumn, leftColumn, rightColumn })
                    if (NavGrid.StandAt(node, row, lava) != null)
                        return (new Point(node, row), dy, left);
                return null;
            }
            if (!BodyPhysics.Fits(world, left, floor))
                return null;
        }
        return null;
    }

    /// <summary>
    /// The jumps the body can start with for a rise of so many tiles, in the order the planner
    /// tries them: each velocity scale of the fighter AI's table and the full jump whose apex
    /// clears the rise, lowest first, and for each the start speed at the walk, half of it and
    /// standing. Lowest first because the shortest flight is the cheapest edge and the arc
    /// least likely to meet a ceiling; the table alone over-jumped a four-tile rise onto a
    /// platform above it, and the walk alone hit a three-tile overhang the half-speed arc
    /// clears. A jump straight up has no run-up, so only the standing start is offered.
    /// </summary>
    public static IEnumerable<(float scale, float startVx)> JumpProfiles(int rise, int direction)
    {
        float need = Math.Max(0, rise) * 16f;
        foreach (float scale in JumpScales)
        {
            float apex = BodyPhysics.JumpVelocity * scale;
            apex = apex * apex / (2f * BodyPhysics.Gravity);
            if (apex < need)
                continue;
            if (direction == 0)
            {
                yield return (scale, 0f);
                continue;
            }
            yield return (scale, direction * BodyPhysics.WalkSpeed);
            yield return (scale, direction * BodyPhysics.WalkSpeed * 0.5f);
            yield return (scale, 0f);
        }
    }

    private static readonly float[] JumpScales =
    {
        BodyPhysics.JumpScaleForTiles(2), BodyPhysics.JumpScaleForTiles(3), BodyPhysics.JumpScaleForTiles(4), 1f,
    };

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
