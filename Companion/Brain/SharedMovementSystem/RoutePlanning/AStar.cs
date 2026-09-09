#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

/// <summary>
/// A* over the navigation grid. A node is a feet tile and the body's mobility state on
/// arriving there (<see cref="NavNode"/>); neighbours are generated on the fly from what the
/// body can do. The search is asked from a tile to a tile and answers with tiles, and the goal
/// is reached in whatever mobility state the body arrives with. Bounded by an expansion budget
/// so a hopeless search (the goal sealed off) costs a known amount and returns null rather
/// than hanging.
/// </summary>
public static class AStar
{
    /// <summary>Why a bounded search returned its result. Callers must not infer this from an expansion count.</summary>
    public enum SearchStopReason { Found, Exhausted, ExpansionBudget, Deadline }

    /// <summary>
    /// Lava tiles are nodes at a high cost while this is true, so a short lava crossing beats
    /// a long detour and a long one does not; the brain sets it from the companion's life
    /// each tick, and false makes lava impassable as before.
    /// </summary>
    public static bool AllowLava;

    /// <summary>
    /// A drop with no way back is a door that closes behind the body, and a plan takes one only
    /// while this is true: the brain sets it for a tick in which the companion is following or
    /// guarding the player, or saving itself, because the player being down there is the one
    /// reason to go where there is no way back. A hunt took one in run 5 (2026-09-08, tick 7155,
    /// fifteen rows into a sealed pocket) and stood at its rim for the rest of the session. The
    /// replay tool leaves it true: a scenario records where the player went.
    ///
    /// What "no way back" means is <see cref="OneWay"/>, a question asked of this same search,
    /// and until 2026-09-08 it was the depth of the fall against the jump box, which was wrong in
    /// both directions: a four-row drop into a sealed box read as safe and a twenty-row drop into
    /// an open cavern with a ramp back up read as a door.
    ///
    /// The positioner's reach flood does not read this. It always refuses an edge with no way
    /// back, whatever the request is, so its reachability tier means "spots the body can come home
    /// from" and opens up only when there are none; that tier is where the decision to enter a
    /// place actually gets made, and it makes it without anything in the code naming a scenario.
    /// </summary>
    public static bool AllowOneWayDrops = true;

    /// <summary>Cost added per lava tile in a node's column, against a walk step of one.</summary>
    public const float LavaTileCost = 40f;

    /// <summary>Multiplier on every edge into a node where the body's head is under liquid, on top of the wet-feet multiplier.</summary>
    public const float SubmergedCost = 2f;

    private readonly record struct Open(NavNode Node, float F);

    /// <summary>A total order, so two nodes are never read as one entry by the set: the price, then the tile, then every mobility counter.</summary>
    private sealed class OpenComparer : IComparer<Open>
    {
        public int Compare(Open a, Open b)
        {
            int c = a.F.CompareTo(b.F);
            if (c != 0) return c;
            c = a.Node.Tile.X.CompareTo(b.Node.Tile.X);
            if (c != 0) return c;
            c = a.Node.Tile.Y.CompareTo(b.Node.Tile.Y);
            if (c != 0) return c;
            MobilityState m = a.Node.Mobility, n = b.Node.Mobility;
            c = m.AirJumpsLeft.CompareTo(n.AirJumpsLeft);
            if (c != 0) return c;
            c = m.Latched.CompareTo(n.Latched);
            return c != 0 ? c : m.DashCooldown.CompareTo(n.DashCooldown);
        }
    }

    /// <summary>The path's previous node and the step that arrived from it, for the rebuild.</summary>
    private readonly record struct Arrival(NavStep Step, NavNode From);

    /// <summary>
    /// Path from <paramref name="start"/> to <paramref name="goal"/>, both feet tiles, or
    /// null when none is found inside <paramref name="budget"/> expansions.
    /// </summary>
    /// <summary>
    /// When set, every search leaves the tiles it closed here, so the replay tool can draw
    /// the region a failed search reached. Null in the game: the copy is never made.
    /// </summary>
    public static HashSet<Point>? TraceClosed;

    /// <summary>
    /// The wall-clock a whole plan may spend, in milliseconds; zero or less is no limit. It is a
    /// separate limit from the expansion budget because the two bound different things and only
    /// one of them was bounded: on 2026-09-09 every slow plan sat at exactly the expansion cap
    /// with the reachability flood costing 1 to 15 ms beside it, so the time was going into the
    /// search's own edge proving, where one expansion can cost a hundred times another — a node on
    /// bare floor offers a few walks, and a node on a ledge over a shaft simulates every jump
    /// profile and every descent line. A budget in expansions therefore prices those the same and
    /// the worst plan of that session took 778 ms, forty-six frames, for a decision that had to be
    /// made in one.
    ///
    /// It is a static rather than a parameter so that the one-way probe, which calls this
    /// recursively from inside the loop, spends the same allowance rather than a fresh one each
    /// time. The deadline is set by the outermost call and inherited by every search under it,
    /// which is what makes the bound "a plan costs at most this" instead of "one search does".
    /// </summary>
    public static double MsBudget { get; set; }

    private static long deadline;

    /// <summary>How many expansions between clock reads; often enough to bound the overrun, rare enough that the read is not the cost.</summary>
    private const int ClockEvery = 32;

    public static NavPath? Find(Point start, Point goal, int budget, out int expansions)
        => Find(start, goal, budget, out expansions, out _);

    /// <summary>Finds a route and reports whether its negative result is a proof or a limit.</summary>
    public static NavPath? Find(Point start, Point goal, int budget, out int expansions, out SearchStopReason stopReason)
    {
        expansions = 0;
        stopReason = SearchStopReason.Exhausted;
        if (!probing)
            deadline = MsBudget > 0d ? System.Diagnostics.Stopwatch.GetTimestamp() + (long)(MsBudget * System.Diagnostics.Stopwatch.Frequency / 1000d) : 0L;
        var open = new SortedSet<Open>(new OpenComparer());
        var g = new Dictionary<NavNode, float>();
        var cameFrom = new Dictionary<NavNode, Arrival>();
        var closed = new HashSet<NavNode>();
        // A probe running inside another search must not touch the replay's trace, or the inner
        // one clears the picture the outer search was drawing.
        HashSet<Point>? trace = probing ? null : TraceClosed;
        trace?.Clear();
        NavNode from = NavNode.At(start);
        NavNode nearest = from;
        float nearestH = H(start, goal);

        g[from] = 0f;
        open.Add(new Open(from, nearestH));

        while (open.Count > 0)
        {
            Open current = open.Min;
            open.Remove(current);
            NavNode node = current.Node;
            if (!closed.Add(node))
                continue;
            trace?.Add(node.Tile);

            if (node.Tile == goal)
            {
                stopReason = SearchStopReason.Found;
                return Rebuild(cameFrom, from, node, partial: false);
            }

            float h = H(node.Tile, goal);
            // A partial end is chosen by closeness alone, so a lava tile must not be eligible:
            // its price steers a whole path round it but cannot stop it being the nearest.
            if (h < nearestH && NavGrid.LavaTilesAt(node.Tile.X, node.Tile.Y) == 0)
            {
                nearestH = h;
                nearest = node;
            }
            if (++expansions > budget)
            {
                stopReason = SearchStopReason.ExpansionBudget;
                break;
            }
            // Out of time counts as out of budget: the partial path to the nearest node reached is
            // returned either way, so a plan that runs long degrades to walking toward the goal
            // rather than to a dropped frame.
            if (deadline != 0L && expansions % ClockEvery == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
            {
                stopReason = SearchStopReason.Deadline;
                break;
            }

            float gHere = g[node];
            foreach ((NavStep step, float cost) in Neighbours(node, !AllowOneWayDrops))
            {
                var next = new NavNode(step.Tile, step.Mobility);
                if (closed.Contains(next))
                    continue;
                float tentative = gHere + cost;
                if (g.TryGetValue(next, out float known) && tentative >= known)
                    continue;
                g[next] = tentative;
                cameFrom[next] = new Arrival(step, node);
                open.Add(new Open(next, tentative + H(step.Tile, goal)));
            }
        }
        // Out of budget or out of region: the closest tile the search reached is still the
        // best place to be, and walking there beats standing where the goal went out of view.
        return nearest == from ? null : Rebuild(cameFrom, from, nearest, partial: true);
    }

    /// <summary>
    /// Every feet tile a walker can reach from <paramref name="start"/> over the same edges the
    /// search uses, breadth first, up to <paramref name="budget"/> tiles. <paramref name="complete"/>
    /// is true when the region ran out before the budget did, in which case a tile not in the set
    /// is truly unreachable; otherwise a tile not in the set is unknown. One flood, reused for
    /// every candidate, is what makes "can I get there" affordable for a box of spots.
    /// </summary>
    public static HashSet<Point> Region(Point start, int budget, out bool complete, bool refuseOneWay = false)
    {
        NavNode from = NavNode.At(start);
        var seen = new HashSet<NavNode> { from };
        var tiles = new HashSet<Point> { start };
        var queue = new Queue<NavNode>();
        queue.Enqueue(from);
        complete = true;
        while (queue.Count > 0)
        {
            // The budget is tiles, because the caller asks how many places it can reach: a tile
            // reached in two mobility states is one place, and the flood costs one node per state.
            if (tiles.Count >= budget)
            {
                complete = false;
                break;
            }
            NavNode node = queue.Dequeue();
            foreach ((NavStep step, _) in Neighbours(node, refuseOneWay))
            {
                var next = new NavNode(step.Tile, step.Mobility);
                if (seen.Add(next))
                {
                    tiles.Add(step.Tile);
                    queue.Enqueue(next);
                }
            }
        }
        return tiles;
    }

    private static float H(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return dx + dy * 0.5f;
    }

    private static NavPath Rebuild(Dictionary<NavNode, Arrival> cameFrom, NavNode start, NavNode end, bool partial)
    {
        var steps = new List<NavStep>();
        NavNode at = end;
        while (at != start)
        {
            Arrival arrival = cameFrom[at];
            steps.Add(arrival.Step);
            at = arrival.From;
        }
        steps.Reverse();
        return new NavPath(steps, end.Tile, partial);
    }

    /// <summary>
    /// Whether the landing of <paramref name="edge"/> can get back to the tile it left, asked of
    /// the same search that moves the body rather than compared against a depth. Only a fall
    /// deeper than <see cref="Traversal.ClimbReachTiles"/> is asked about, and only by a caller
    /// that would refuse the edge. That is not the same as "only when the companion is not
    /// following the player": the plan for a follow permits these edges and pays nothing, while the
    /// positioner's flood refuses them on every request and pays on the same tick, so a following
    /// companion does pay for this through its positioner.
    ///
    /// Two things make it affordable, and a third that was claimed here is not one of them. It
    /// early-exits the moment it reaches the tile it left, so a recoverable drop answers in a
    /// handful of expansions because the lip is right there, and that is the case that dominates.
    /// A budget that runs out answers "there is a way back", the same convention
    /// <see cref="Reachability"/> uses for threats, so it refuses only where it has proved a pocket
    /// closed. What is NOT true is that a genuine pocket is small by construction: that read the
    /// corpus's two real pockets, at thirty and thirty-four tiles, as the shape of the class, and a
    /// review built a sealed chamber of any size at all. <see cref="OneWayProbeBudget"/> carries
    /// what that costs and where the guarantee now stops.
    ///
    /// The verdict is memoised on its own rather than with the edge, because the edge cache's
    /// invalidation box is drawn from how far a scan reads and this verdict depends on the shape of
    /// a whole region: a tile dug deep inside a pocket flips the verdict of a lip far outside any
    /// such box. So it is dropped wholesale, and the key carries the lava switch because the
    /// probe's own search generates neighbours under it. Wholesale covers the changes the game
    /// announces through <see cref="TileChanged"/>, which is placing and killing a tile and not a
    /// liquid or a sand column moving on its own; those are what the cache lifetime is for, here as
    /// for the geometry, so a remembered exit can be closed by an unannounced change and stay
    /// remembered until it expires.
    /// </summary>
    private static bool OneWay(Point from, NavEdge edge)
    {
        // Inside a probe the rule is suppressed: without this, asking whether an edge has a way
        // back generates edges, each of which asks the same question, and the failure arrives as
        // a hang rather than a wrong answer.
        if (probing || edge.Fall <= Traversal.ClimbReachTiles)
            return false;
        // The lava switch is part of the key, not just the geometry's: the probe's own search
        // generates neighbours under it, so a return route through a lava passage exists for a
        // healthy body and not for a hurt one, and the brain flips the switch every tick from the
        // companion's life. Keyed only on the two tiles, a verdict earned at full health kept
        // granting permission after the passage it depended on had left the graph.
        // CacheEdges gates this too, or the replay's --no-cache pass proves what it claims about the
        // geometry cache and nothing at all about this one, which is the weaker half of the same
        // question: whether a remembered answer ever differs from a fresh one.
        (Point, Point, bool) key = (from, edge.Step.Tile, AllowLava);
        if (CacheEdges && oneWay.TryGetValue(key, out OneWayVerdict known) && unchecked(Clock - known.Born) <= EdgeCacheLifeTicks)
            return known.Value;

        // Both endpoints are already nodes under the search's own rules: the lip is the node being
        // expanded, and the landing is a tile the traversal proved standable with the same lava
        // permission. So they are used as they are. Snapping to a nearest standable tile was the
        // first version and it read the world through the lava-refusing overload, so a landing on a
        // lava floor resolved to nothing at all, the probe was skipped, and the drop into a sealed
        // lava chamber was admitted as having a way back — the exact case the rule exists to refuse.
        // A tile that is not standable here is a state this cannot reason about, and the safe answer
        // to a question it cannot ask is that there is no way back.
        bool verdict;
        if (!NavGrid.IsStandable(from.X, from.Y, AllowLava) || !NavGrid.IsStandable(edge.Step.Tile.X, edge.Step.Tile.Y, AllowLava))
            verdict = true;
        else
        {
            probing = true;
            try
            {
                NavPath? route = Find(edge.Step.Tile, from, OneWayProbeBudget, out _, out SearchStopReason stop);
                // A deadline or expansion limit established neither an exit nor a pocket. The
                // old `used > budget` test classified a deadline-expired probe as sealed and
                // cached that false door for the edge-cache lifetime.
                if (stop is SearchStopReason.Deadline or SearchStopReason.ExpansionBudget)
                    return false; // Unknown is permitted for this caller, but is never memoised.
                verdict = route == null || route.Partial;
            }
            finally
            {
                probing = false;
            }
        }
        if (CacheEdges)
            oneWay[key] = new OneWayVerdict(verdict, Clock);
        return verdict;
    }

    private static readonly Dictionary<(Point, Point, bool), OneWayVerdict> oneWay = new();
    private readonly record struct OneWayVerdict(bool Value, uint Born);
    private static bool probing;

    /// <summary>
    /// Expansions a return probe may spend before it answers "there is a way back".
    ///
    /// This is not a performance knob, it is the largest pocket the rule can refuse. A probe
    /// refuses an edge only where it has exhausted the landing's whole region without finding the
    /// lip, so a sealed region bigger than this budget spends the budget instead of closing, and
    /// the edge into it is permitted. A review on 2026-09-08 built exactly that: a chamber with no
    /// exit at all, provably sealed by a full reverse search, whose drop was refused at 150 floor
    /// tiles and admitted at 151. The failure grows with the size of the trap, which is the wrong
    /// direction, so the budget is set to cover a cavern rather than a pit.
    ///
    /// Raising it is close to free in the case that dominates, because a drop with a way back
    /// early-exits as soon as the heuristic reaches the lip and never approaches the budget; the
    /// full cost is paid only by a genuinely sealed region, which is the one case worth paying for,
    /// and once per lip-and-landing pair per cache lifetime. What remains true at any value: a
    /// sealed region larger than this is entered, so the guarantee is "no small trap" rather than
    /// "no trap", and a rescue behaviour (AIC-65) is what covers the rest.
    /// </summary>
    public const int OneWayProbeBudget = 1200;

    /// <summary>
    /// What the body can reach from a feet tile. Walk to a standable neighbour on the same
    /// row or one up (a step). Drop off an edge to the first standable tile below. Fall
    /// through the platform underfoot to the first standable tile below it. Jump to any
    /// standable tile in the jump box where the follower's own jump, simulated tick by tick
    /// against the shapes, lands in that tile. From inside liquid every move costs more, and
    /// the simulated jump moves at half speed while wet because the game halves a wet NPC's
    /// movement; the search then prefers walking out along the floor to jumping in place.
    /// With <paramref name="refuseOneWay"/> an edge whose landing cannot get back to this node
    /// is dropped, which is <see cref="OneWay"/> and the only thing that costs a caller extra.
    /// </summary>
    private static IEnumerable<(NavStep, float)> Neighbours(NavNode node, bool refuseOneWay)
    {
        (NavNode, bool) key = (node, AllowLava);
        NavEdge[] edges;
        if (!CacheEdges)
            edges = System.Linq.Enumerable.ToArray(NavEdges(node, AllowLava));
        else if (!edgeCache.TryGetValue(key, out CachedEdges cached) || unchecked(Clock - cached.Born) > EdgeCacheLifeTicks)
        {
            edges = System.Linq.Enumerable.ToArray(NavEdges(node, AllowLava));
            edgeCache[key] = new CachedEdges(edges, Clock);
        }
        else
            edges = cached.Edges;
        Point t = node.Tile;
        foreach (NavEdge e in edges)
        {
            // The cheap test first, deliberately: the probe behind OneWay runs only for a caller
            // that would refuse the edge, so following the player never pays for it.
            if (refuseOneWay && OneWay(t, e))
                continue;
            yield return (e.Step, e.Swept ? PriceSwept(t, e.Step.Tile, e.Move) : Price(e.Step.Tile.X, e.Step.Tile.Y, e.Move));
        }
    }

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
    private static readonly Dictionary<(NavNode, bool), CachedEdges> edgeCache = new();
    private readonly record struct CachedEdges(NavEdge[] Edges, uint Born);

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
    /// wall, the simulated fall steers at the walk speed for as many ticks as the deepest drop
    /// (<see cref="NavGrid.MaxDropTiles"/> rows) takes, and the shape tests around the landing read
    /// the body's width, which spans two columns. The Codex review of 87e8d20 found a support
    /// nineteen columns out changing an edge the hand-set sixteen kept.
    /// </summary>
    public static readonly int EdgeReachX = 1 + NavGrid.OpenSpanReach + (int)Math.Ceiling(Traversal.FallTicks(NavGrid.MaxDropTiles) * BodyPhysics.WalkSpeed / 16f) + 2;

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
        // Every return verdict goes, not a box of them: a verdict is about the shape of a whole
        // region, and a tile dug deep inside a pocket changes whether its lip has a way back
        // while sitting far outside any box drawn from how far a scan reads.
        oneWay.Clear();
        if (edgeCache.Count == 0)
            return;
        int minY = y - NavGrid.MaxDropTiles - 1;
        int maxY = y + NavGrid.JumpHeightTiles + NavGrid.BodyHeightTiles + 2;
        var stale = new List<(NavNode, bool)>();
        foreach ((NavNode, bool) key in edgeCache.Keys)
        {
            Point t = key.Item1.Tile;
            if (Math.Abs(t.X - x) <= EdgeReachX && t.Y >= minY && t.Y <= maxY)
                stale.Add(key);
        }
        foreach ((NavNode, bool) key in stale)
            edgeCache.Remove(key);
    }

    /// <summary>Drop every cached edge: a new world, or the replay tool starting a block or a flood it reads the edge flag from.</summary>
    public static void InvalidateEdges()
    {
        edgeCache.Clear();
        oneWay.Clear();
    }

    /// <summary>How many tiles hold cached edges right now, for the overlay.</summary>
    public static int CachedTiles => edgeCache.Count;

    /// <summary>
    /// Every edge out of a node, as the traversals prove them: each kind of move is owned by one
    /// object that both generates its edges here and performs them in the follower, so an edge
    /// offered is a move the body makes. From inside liquid every move costs double, because the
    /// game halves a wet NPC's movement.
    /// </summary>
    private static IEnumerable<NavEdge> NavEdges(NavNode node, bool lava)
    {
        Point t = node.Tile;
        float costScale = NavGrid.IsLiquid(t.X, t.Y) ? 2f : 1f;
        BodyPhysics.Pose? here = NavGrid.StandAt(t.X, t.Y, lava);
        foreach (Traversal traversal in Traversal.Planning)
            foreach (NavEdge edge in traversal.Candidates(node, here, lava))
                yield return edge with { Move = edge.Move * costScale };
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
