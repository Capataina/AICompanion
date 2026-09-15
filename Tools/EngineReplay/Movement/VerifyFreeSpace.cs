using System.Diagnostics;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;

/// <summary>
/// The free-space core on text worlds, game-free: a two-wide corridor is open and a one-wide is
/// closed, a wet tile is exactly as free as a dry one, the diagonal edge between
/// corners passes a one-tile staircase, a search with a goal finds the corridor, and the flood
/// over a screen-sized room finishes within a handful of the brain's own slices — which is the
/// property the walker's flood never had, and the one every "not yet known" answer stands on.
/// </summary>
internal static class VerifyFreeSpace
{
    public static int CorridorsAndLiquids()
    {
        LimitPlanningWork.Unbounded = true;
        try
        {
            // Two tall: usable corners are exactly the 37 on the line between the two rows — a
            // corner's four tiles include the column beside it, so the first usable corner sits one
            // tile in from the wall at x = 2 and the last at x = 38.
            var corridor = Corridor(40, freeRows: 2);
            var flood = Flood(corridor, new Point(2, 2));
            Require(flood.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted, $"the two-tall corridor's flood must exhaust: {flood.Stop}");
            Require(flood.Reached.Contains(new Point(38, 2)), "the two-tall corridor must be open to its far end");
            Require(flood.Reached.Count == 37, $"a two-tall corridor holds one usable corner per interior column; the flood closed {flood.Reached.Count}");

            // One tall: no corner is usable at all, and a flood forced from one goes nowhere.
            var narrow = Corridor(40, freeRows: 1);
            Require(CornerGraph.NearestUsable(narrow, new Vector2(16f, 24f), 3) == null, "a one-tall corridor has no corner the body fits at");
            flood = Flood(narrow, new Point(1, 1));
            Require(flood.Finished && flood.Reached.Count == 1, $"a flood from an unusable corner must close only itself; it closed {flood.Reached.Count}");

            // A two-tall corridor pinched to one tall at column 20 is open up to the pinch and no further.
            var pinched = Corridor(40, freeRows: 2);
            pinched.Set(20, 2, '#');
            flood = Flood(pinched, new Point(2, 2));
            Require(flood.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted && !flood.Reached.Contains(new Point(38, 2)),
                $"the pinch must close the corridor: stop={flood.Stop} farEnd={flood.Reached.Contains(new Point(38, 2))}");
            Require(flood.Reached.Count == 18, $"the corridor up to the pinch holds 18 usable corners; the flood closed {flood.Reached.Count}");

            // Water or lava filling the corridor's whole height across two columns is air to the body, so the flood closes the
            // same 37 corners the dry corridor does and the far end is reached through it. The owner ruled on 15 September 2026
            // that every liquid is air to the companion; this row held water and lava as walls until an immunity opened them.
            // The text world carries only water and lava, so honey and shimmer are proven on native tiles by VerifyLiquidsAreAir.
            foreach ((char glyph, string name) in new[] { ('~', "water"), ('L', "lava") })
            {
                var wet = Corridor(40, freeRows: 2);
                for (int row = 1; row <= 2; row++) { wet.Set(20, row, glyph); wet.Set(21, row, glyph); }
                bool poured = glyph == '~' ? wet.Water(20, 1) && wet.Water(21, 2) : wet.Lava(20, 1) && wet.Lava(21, 2);
                Require(poured, $"the {name} premise needs the corridor's two middle columns to hold {name}");
                ClearanceField.Shared.Invalidate();
                flood = Flood(wet, new Point(2, 2));
                Require(flood.Finished && flood.Stop == FreeSpaceSearch.StopReason.Exhausted && flood.Reached.Contains(new Point(38, 2)),
                    $"{name} across the corridor must be open to the flood: stop={flood.Stop} farEnd={flood.Reached.Contains(new Point(38, 2))}");
                Require(flood.Reached.Count == 37, $"a corridor with {name} across it holds the dry corridor's 37 usable corners; the flood closed {flood.Reached.Count}");
            }
            ClearanceField.Shared.Invalidate();

            // The diagonal edge: a staircase of two-by-two openings each offset one tile is one region.
            var stairs = Staircase(12);
            flood = Flood(stairs, new Point(1, 1));
            Require(flood.Reached.Contains(new Point(13, 13)), "the diagonal staircase must be one region to the flood, corner to corner");
            Require(flood.Finished && flood.Reached.Count == 13, $"the staircase holds one usable corner per step; the flood closed {flood.Reached.Count}");

            // With a goal, the same search is the route search, and it finds the corridor.
            FreeSpaceSearch.WorldOverride = corridor;
            ClearanceField.Shared.Invalidate();
            var route = new FreeSpaceSearch(corridor, new Point(2, 2), new Point(38, 2));
            route.Advance(int.MaxValue);
            Require(route.Finished && route.Stop == FreeSpaceSearch.StopReason.Found, $"the route search must find the far end: {route.Stop}");
            Require(route.RouteCorners()!.Count == 37, $"the corridor route is 37 corners; got {route.RouteCorners()!.Count}");
            return 0;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            ClearanceField.Shared.Invalidate();
        }
    }

    public static int FloodFinishes()
    {
        LimitPlanningWork.Unbounded = true;
        try
        {
            // A room the size of a screen, all air inside its walls: 99 by 59 usable corners.
            var rows = new List<string>();
            for (int y = 0; y < 62; y++) rows.Add(y == 0 || y == 61 ? new string('#', 102) : "#" + new string('.', 100) + "#");
            var room = new TextTileWorld(0, 0, rows);
            FreeSpaceSearch.WorldOverride = room;
            ClearanceField.Shared.Invalidate();
            // Twice: the first flood pays for building the clearance chunks it reads, which a live
            // brain pays once per chunk per terrain revision; the second is the steady cost of a
            // flood over a field already built, which is what a resolve's allowance is spent on.
            double cold = 0, warm = 0;
            int slices = 0, expansions = 0, builds = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                var flood = new FreeSpaceSearch(room, new Point(50, 30), null);
                var clock = Stopwatch.StartNew();
                slices = 0;
                while (!flood.Advance(AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ReachFloodExpansions)) slices++;
                slices++;
                clock.Stop();
                Require(flood.Stop == FreeSpaceSearch.StopReason.Exhausted, $"the room's flood must exhaust: {flood.Stop}");
                Require(flood.Reached.Count == 99 * 59, $"the room holds 99 by 59 usable corners; the flood closed {flood.Reached.Count}");
                expansions = flood.Expansions;
                if (pass == 0) { cold = clock.Elapsed.TotalMilliseconds; builds = ClearanceField.Shared.Builds; }
                else warm = clock.Elapsed.TotalMilliseconds;
            }
            EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "free-space-flood-slices-for-a-screen", slices, "slices", "down",
                message: $"{expansions} corners in {slices} slices of {AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ReachFloodExpansions}");
            EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "free-space-flood-ms-per-thousand-corners", warm / expansions * 1000d, "ms", "down",
                message: $"over a clearance field already built; the cold pass that built {builds} chunks took {cold:0.0} ms in all");
            EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "clearance-field-ms-per-chunk", (cold - warm) / Math.Max(1, builds), "ms", "down",
                message: "the one-off cost of a sixteen-by-sixteen chunk, paid once per chunk per terrain revision");
            Require(slices <= 6, $"a screen-sized room must close in a handful of slices; it took {slices}");
            return 0;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            ClearanceField.Shared.Invalidate();
        }
    }

    /// <summary>
    /// A bounded flood exhausts inside its disc and nowhere else. The reach sense depends on this: a
    /// flood over an open world once ran to the search's node limit, which finishes without exhausting,
    /// so the sense could never say "unreachable" for the life of a session. The corridor is far longer
    /// than the radius, and the count is exact: twenty tiles each way along the start's row, nineteen
    /// along the row one tile off, where the diagonal takes the corner at twenty just past the disc.
    /// Without the bound the flood closes the whole corridor and the count fails. Unpriced here so the
    /// cost of the corner at the radius is the radius itself, which is the row's check that a flood's
    /// costs are what its pricing says.
    /// </summary>
    public static int FloodBounded()
    {
        LimitPlanningWork.Unbounded = true;
        try
        {
            var corridor = Corridor(400, 3);
            FreeSpaceSearch.WorldOverride = corridor;
            ClearanceField.Shared.Invalidate();
            const float radius = 20f * 16f;
            var flood = new FreeSpaceSearch(corridor, new Point(200, 2), null, priceClearance: false, radius: radius);
            flood.Advance(int.MaxValue);
            Require(flood.Stop == FreeSpaceSearch.StopReason.Exhausted, $"a bounded flood must exhaust inside its radius: {flood.Stop}");
            int furthest = flood.Reached.Max(c => Math.Abs(c.X - 200));
            Require(furthest == 20, $"the flood must reach exactly twenty tiles along the corridor; it reached {furthest}");
            Require(flood.Reached.Contains(new Point(220, 2)) && !flood.Reached.Contains(new Point(221, 2)),
                "the corner at the radius is closed and the one past it is not");
            Require(flood.Reached.Count == 41 + 39, $"a twenty-tile ball in a three-row corridor holds eighty corners; the flood closed {flood.Reached.Count}");
            Require(flood.CostTo(new Point(220, 2)) is float cost && Math.Abs(cost - radius) < 0.01f,
                "an unpriced flood's cost is the path length in pixels, so the radius corner costs the radius");
            // The disc is geometric, so pricing the edges by clearance, as the reach sense does, closes
            // the same corners at higher costs; this is the configuration the sense actually runs in.
            var priced = new FreeSpaceSearch(corridor, new Point(200, 2), null, priceClearance: true, radius: radius);
            priced.Advance(int.MaxValue);
            Require(priced.Stop == FreeSpaceSearch.StopReason.Exhausted && priced.Reached.Count == flood.Reached.Count
                && priced.Reached.SetEquals(flood.Reached),
                $"a priced flood exhausts the same disc: {priced.Stop}, {priced.Reached.Count} corners against {flood.Reached.Count}");
            Require(priced.CostTo(new Point(220, 2)) is float pricedCost && pricedCost > radius,
                "a priced flood's cost is above the length, which is why the bound is a disc and not a cost ball");
            return 0;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            ClearanceField.Shared.Invalidate();
        }
    }

    private static FreeSpaceSearch Flood(TextTileWorld world, Point start)
    {
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
        var flood = new FreeSpaceSearch(world, start, null);
        flood.Advance(int.MaxValue);
        return flood;
    }

    /// <summary>A horizontal corridor of the given free rows starting at row one, walls all round.</summary>
    private static TextTileWorld Corridor(int width, int freeRows)
    {
        var rows = new List<string> { new string('#', width) };
        for (int r = 0; r < freeRows; r++) rows.Add("#" + new string('.', width - 2) + "#");
        rows.Add(new string('#', width));
        rows.Add(new string('#', width));
        return new TextTileWorld(0, 0, rows);
    }

    /// <summary>Two-by-two openings each offset one tile down and right, from tile (0,0).</summary>
    private static TextTileWorld Staircase(int steps)
    {
        int size = steps + 4;
        var grid = new char[size][];
        for (int y = 0; y < size; y++) grid[y] = new string('#', size).ToCharArray();
        for (int s = 0; s <= steps; s++)
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                    grid[s + dy][s + dx] = '.';
        return new TextTileWorld(0, 0, grid.Select(r => new string(r)).ToList());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
