extern alias live;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;

/// <summary>
/// How much of a world the companion can actually get to, found without being shown where to go.
///
/// This is the plan's Go-Explore loop, in the form Mawhorter and Smith use it rather than the
/// learned form: an archive of places already reached, a return to one of them, an excursion from
/// there, and a progress score read off an imperfect tile abstraction the game already computes.
/// The abstraction here is the reach sense, which is the brain's own answer to "where can this body
/// walk to", and the coverage number is the union of everything it has ever claimed.
///
/// Two design decisions are forced by what this brain is, and both are findings rather than
/// conveniences.
///
/// **There is no exploration behaviour to drive.** Independent local exploration was deleted from
/// this brain on purpose; the companion is an opportunistic companion, and left alone beside a
/// stationary player it keeps company and stays put. So the thing being scripted is not the
/// companion's decisions — it is where the player stands. Each excursion puts the player on the
/// frontier of what is known and lets the companion's own following, its own route search and its
/// own body carry it there. What is measured is therefore the movement kit's real coverage, which
/// is exactly the quantity the checkpoint filter is about, and it is measured through the shipping
/// brain rather than around it.
///
/// **A restore is a fresh brain at a saved body pose, and route memory is not part of the
/// snapshot.** Go-Explore's first failure mode is derailment — returning to a cell unreliably — and
/// the honest way to avoid it here is to put the body back exactly where it was rather than replay
/// the journey. What cannot be put back is everything the brain learned on the way: the route
/// archive, the flood, the edge cache. Those are cleared, so each excursion starts from a body in
/// the right place and a mind that has never been there. That makes the coverage number a floor
/// rather than an estimate: a brain that kept what it had learned would do at least this well.
/// </summary>
internal static class ExploreWithoutTheTrack
{
    /// <summary>
    /// How coarse a cell is, in tiles.
    ///
    /// Go-Explore's archive needs cells big enough that two poses in the same place are the same
    /// entry and small enough that a corridor is more than one. Four tiles is about a body and a
    /// half, which is the scale at which two positions are somewhere different to walk from.
    /// </summary>
    private const int CellTiles = 4;

    /// <summary>How many ticks one excursion runs before the archive is consulted again.</summary>
    private const int ExcursionTicks = 300;

    private const int LightHalf = 80;

    public static void Run(string suite, ReadRecordedRoute.Route route, int tickBudget)
    {
        PrepareTheHeadlessEngine.PrepareLightServices();
        PrepareTheHeadlessEngine.PinEveryRandomSource(1);

        Vector2 start = new(route[0].CompanionLeftBottom.X, route[0].CompanionLeftBottom.Y);
        var archive = new Dictionary<Point, Vector2> { [Cell(start.ToTileCoordinates())] = start };
        var visits = new Dictionary<Point, int> { [Cell(start.ToTileCoordinates())] = 0 };
        var reached = new HashSet<Point>();       // every tile the reach sense has ever claimed
        var stood = new HashSet<Point>();         // every tile the body has actually stood on

        int spent = 0, excursions = 0;
        var growth = new List<int>();

        while (spent < tickBudget)
        {
            // Return: the least-visited cell in the archive, which is Go-Explore's own rule and is
            // what stops the loop pouring every excursion into the first corridor it found.
            Point from = archive.Keys.OrderBy(c => visits.GetValueOrDefault(c)).ThenBy(c => c.X).ThenBy(c => c.Y).First();
            visits[from] = visits.GetValueOrDefault(from) + 1;
            Vector2 pose = archive[from];

            // Explore: the player goes to the furthest place the body is known to be able to walk
            // to, which is the frontier of the abstraction rather than anywhere on the recorded
            // track. With nothing claimed yet there is no frontier, so the first excursion sends the
            // player a short way along the floor to give the flood something to chase.
            Vector2 target = Frontier(reached, pose);

            PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();
            var companion = PrepareTheHeadlessEngine.AttachCompanion(pose, target);
            companion.NPC.Bottom = new Vector2(pose.X + companion.NPC.width / 2f, pose.Y);
            Player player = Main.player[0];
            player.Bottom = target;
            player.velocity = Vector2.Zero;
            PrepareTheHeadlessEngine.WarmTheLightEngine(player.Bottom.ToTileCoordinates(), LightHalf, LightHalf);

            int before = reached.Count;
            for (int tick = 0; tick < ExcursionTicks && spent < tickBudget; tick++, spent++)
            {
                PrepareTheHeadlessEngine.AdvanceTheWorldClock();
                PrepareTheHeadlessEngine.DriveLightOnce(companion.NPC.Bottom.ToTileCoordinates(), LightHalf, LightHalf);
                companion.AI();
                PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);

                Point feet = companion.NPC.Bottom.ToTileCoordinates();
                stood.Add(feet);
                Point cell = Cell(feet);
                // A cell is archived with the first pose that reached it. Overwriting with a later
                // pose would quietly make the archive a record of where the body ended up rather
                // than of a place it can be put back into.
                if (!archive.ContainsKey(cell)) archive[cell] = companion.NPC.Bottom - new Vector2(companion.NPC.width / 2f, 0);
                foreach (Point tile in companion.Brain.Senses.Reach.ScoredTiles) reached.Add(tile);
            }
            growth.Add(reached.Count - before);
            excursions++;
        }

        // What the track asked for, against what exploration found on its own. The track's tiles are
        // the question and were never given to the loop as a destination, which is the whole point:
        // a coverage number computed from a route the explorer was steered along would be measuring
        // the steering.
        var trackTiles = new HashSet<Point>();
        for (int i = 0; i < route.Count; i++) trackTiles.Add(route[i].PlayerFeet.ToTileCoordinates());
        int onTrackReached = trackTiles.Count(t => reached.Contains(t));

        string note = $"{excursions} excursions of up to {ExcursionTicks} ticks, {spent} ticks spent, "
            + $"cells {archive.Count}, per-excursion new tiles [{string.Join(",", growth)}]; "
            + "each excursion restores a body pose with a brain that has forgotten the world, so this is a floor";

        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "tiles the reach sense claimed while exploring", reached.Count, "tiles", "up", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "tiles the body actually stood on while exploring", stood.Count, "tiles", "up", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "archive cells found while exploring", archive.Count, "cells", "up", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "share of the player's track tiles exploration reached unaided",
            trackTiles.Count == 0 ? 0 : 100.0 * onTrackReached / trackTiles.Count, "percent", "up", "unbounded-allowances",
            message: note + $"; {onTrackReached} of {trackTiles.Count} track tiles");

        Console.WriteLine($"EXPLORE {note}");
        Console.WriteLine($"EXPLORE reached {reached.Count} tiles, stood on {stood.Count}, "
            + $"{onTrackReached}/{trackTiles.Count} of the player's own track tiles ({(trackTiles.Count == 0 ? 0 : 100.0 * onTrackReached / trackTiles.Count):0.0}%)");
    }

    /// <summary>
    /// The furthest place the body is known to be able to reach, which is where the player is put
    /// next.
    ///
    /// Furthest rather than nearest because a frontier is what has not been worked yet, and the
    /// nearest claimed tile is by definition somewhere the body has just been. Before anything has
    /// been claimed there is no frontier at all, and the fallback walks the player a short way along
    /// the row the body is standing on rather than inventing a destination somewhere it has no
    /// reason to believe is reachable.
    /// </summary>
    private static Vector2 Frontier(HashSet<Point> reached, Vector2 pose)
    {
        if (reached.Count == 0) return pose + new Vector2(20 * 16, 0);
        Point feet = pose.ToTileCoordinates();
        Point best = reached.OrderByDescending(t => (t.X - feet.X) * (t.X - feet.X) + (t.Y - feet.Y) * (t.Y - feet.Y))
            .ThenBy(t => t.X).ThenBy(t => t.Y).First();
        return new Vector2(best.X * 16 + 8, best.Y * 16 + 16);
    }

    private static Point Cell(Point tile)
        => new((int)Math.Floor(tile.X / (double)CellTiles), (int)Math.Floor(tile.Y / (double)CellTiles));
}
