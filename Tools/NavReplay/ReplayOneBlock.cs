#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// One scenario block, evaluated once: the world reset, the recorded goal planned, the reach flood
/// classified, the player's trail checked against it, and — when asked — the route walked by the
/// real navigator over the simulated body.
///
/// It exists because three callers need the same answer and a second copy of this arithmetic would
/// be the end of the mirror relation before it started. The corpus run prints it, the mirror runs it
/// twice over a world and its reflection and compares the two, and the shrink runs it once per
/// reduction and asks whether the failure survived. If the mirror's two sides went through different
/// code, a disagreement would say nothing about the world.
/// </summary>
internal static class ReplayOneBlock
{
    /// <summary>
    /// What one block did. <see cref="Class"/> is the verdict the corpus has always printed and
    /// <see cref="Signature"/> is what a reduction has to preserve: the same class, the same first
    /// tile the grid refused under the player's own feet, and the same first physical fault. A
    /// reduction that changes any of the three produced a different finding rather than a smaller
    /// one, which is the rule that keeps delta debugging from wandering off its own bug.
    /// </summary>
    internal sealed record Outcome(
        string Name,
        string Class,
        string Skip,
        TextTileWorld World,
        string Header,
        List<string> Extras,
        Point Start,
        Point Goal,
        Point? Player,
        Point? From,
        Search Main,
        Search ToPlayer,
        HashSet<Point> Region,
        bool RegionComplete,
        string GoalIn,
        string PlayerIn,
        string HomeIn,
        string Pocket,
        IReadOnlyList<string> TrailLines,
        string? FirstRefusedTrail,
        FollowOutcome? Follow,
        string FollowVerdict,
        IReadOnlyList<string> FollowEdges,
        string? FirstFault)
    {
        public bool Passed => Class == "PASS";
        public bool Sealed => Class == "SEALED";
        public bool Failed => Class == "FAIL";
        public bool Skipped => Class == "SKIP";

        /// <summary>
        /// The failure's identity, as a string two runs can be compared by. The fault is reduced to
        /// its kind rather than its tiles on purpose: a reduction that moves the same misland one
        /// tile along is still the same finding, while a reduction that turns a misland into a
        /// timeout is a different one.
        /// </summary>
        public string Signature
        {
            get
            {
                string fault = FirstFault is { } f ? f.Split(' ')[0] : "-";
                string follow = Follow is { } outcome ? outcome.ToString() : "-";
                // Which region the model closed around is part of the finding and not a detail of
                // its wording: a body sealed into a pocket and a goal sealed into one are different
                // bugs with different owners, and a reduction that turned the first into the second
                // would otherwise report itself as having preserved the failure. It did not — it
                // found a second one.
                string closure = Pocket.Contains("SEALED START", StringComparison.Ordinal) ? "start-closed"
                    : Pocket.Contains("SEALED GOAL", StringComparison.Ordinal) ? "goal-closed"
                    : Pocket.Length > 0 ? "undecidable-as-cut"
                    : "-";
                return $"{Class}|{closure}|{follow}|{fault}|{FirstRefusedTrail ?? "-"}";
            }
        }
    }

    /// <summary>
    /// Evaluate one block from its raw lines. Every piece of process-wide state a search can reach
    /// is reset here rather than by the caller, because the mirror's second side runs immediately
    /// after its first and an edge cache carried across would answer the reflection with the
    /// original's tiles.
    /// </summary>
    internal static Outcome Evaluate(IReadOnlyList<string> block, string name, bool follow, (int from, int to) followWindow)
    {
        TextTileWorld world = TextTileWorld.Parse(block, out string header, out List<string> extras);
        NavGrid.World = world;
        RememberExecutedRoutes.World.Clear();
        AStar.InvalidateEdges();
        AStar.AllowLava = false;
        AStar.Avoid.Clear();

        Point? start = world.Markers.TryGetValue('S', out Point s) ? s : world.Markers.TryGetValue('N', out Point n0) ? n0 : null;
        Point? goal = world.Markers.TryGetValue('G', out Point g) ? g : null;
        Point? player = world.Markers.TryGetValue('P', out Point p) ? p : null;
        Point? headerGoal = HeaderPoint(header, "goal");
        Point? headerStart = HeaderPoint(header, "start");
        goal ??= headerGoal ?? (headerStart == null && player != null ? player : null);
        start ??= headerStart;
        player ??= HeaderPoint(header, "player");

        var noSearch = new Search(null, 0, new HashSet<Point>(), false, 0);
        if (start == null || goal == null)
            return Skip(name, world, header, extras, $"no start or goal ({header})", noSearch);
        if (!world.InWorld(goal.Value.X, goal.Value.Y))
            return Skip(name, world, header, extras, $"recorded goal {Fmt(goal.Value)} is outside the captured window ({header})", noSearch, start.Value, goal.Value, player);

        Point? from = Ground(world, start.Value);
        Search main = Run(from, goal.Value);
        Search toPlayer = player is Point pl && pl != goal ? Run(from, pl) : main;
        bool pass = main.Pass;

        HashSet<Point> region = new();
        bool complete = false, sealedBlock = false;
        string goalIn = "", playerIn = "", pocket = "", homeIn = "";
        var trailLines = new List<string>();
        string? firstRefused = null;
        if (from is Point f)
        {
            AStar.InvalidateEdges();
            world.AskedOutside = false;
            region = AStar.Region(f, AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ReachFloodBudget, out complete);
            bool startClipped = world.AskedOutside;
            goalIn = region.Contains(goal.Value) ? "in" : "out";
            playerIn = player is Point pl3 ? (region.Contains(pl3) ? ", player in" : ", player out") : "";
            HashSet<Point> returnable = AStar.Region(f, AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ReachFloodBudget, out _, refuseOneWay: true);
            bool playerOut = player is Point pl4 && region.Contains(pl4) && !returnable.Contains(pl4);
            homeIn = playerOut ? "; the player is reachable only through an edge with no way back, so the positioner floods without the refusal and scores the raw region"
                : returnable.Count == region.Count ? ""
                : $"; {returnable.Count} of them returnable, goal {(returnable.Contains(goal.Value) ? "in" : "out")}";
            if (!pass && complete && goalIn == "out")
            {
                AStar.InvalidateEdges();
                world.AskedOutside = false;
                HashSet<Point> goalRegion = Ground(world, goal.Value) is Point goalFeet
                    ? AStar.Region(goalFeet, AICompanion.Companion.Brain.Infrastructure.Selection.Weights.ReachFloodBudget, out _)
                    : new HashSet<Point>();
                bool goalClipped = world.AskedOutside;
                pocket = !startClipped ? "; SEALED START IN MODEL: the graph closes inside the capture; physical impossibility is not established"
                    : !goalClipped ? $"; SEALED GOAL IN MODEL: the goal's region ({goalRegion.Count} tiles) closes inside the capture; inspect terrain and movement coverage"
                    : "; both regions reach the window's edge: undecidable as cut, widen it with reshape.py --pad";
                sealedBlock = !startClipped || !goalClipped;
            }

            foreach (string extra in extras)
            {
                if (!extra.StartsWith("trail ", StringComparison.Ordinal))
                    continue;
                int inWindow = 0, total = 0, unknown = 0, malformed = 0;
                string? refused = null;
                foreach (string pair in extra[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] xy = pair.Split(',');
                    if (xy.Length != 2 || !int.TryParse(xy[0], out int tx) || !int.TryParse(xy[1], out int ty))
                    {
                        malformed++;
                        continue;
                    }
                    var tile = new Point(tx, ty);
                    total++;
                    if (!world.InWorld(tile.X, tile.Y))
                        continue;
                    inWindow++;
                    if (refused != null)
                        continue;
                    if (!NavGrid.IsStandable(tile.X, tile.Y))
                        refused = $"{Fmt(tile)} not standable";
                    else if (complete && !region.Contains(tile))
                        refused = $"{Fmt(tile)} unreachable from the start";
                    else if (!complete && !region.Contains(tile))
                        unknown++;
                }
                firstRefused ??= refused;
                string verdict = refused != null ? "first refused " + refused
                    : inWindow == 0 ? "nothing to check"
                    : unknown > 0 ? $"every one standable, {unknown} beyond the flood's budget so unchecked for reach"
                    : "every one standable and reachable";
                trailLines.Add($"{inWindow} of {total} tiles in the window, {verdict}{(malformed > 0 ? $"; {malformed} malformed pair(s) ignored" : "")}");
            }
        }

        FollowOutcome? followOutcome = null;
        string followVerdict = "";
        IReadOnlyList<string> followEdges = Array.Empty<string>();
        string? firstFault = null;
        if (follow && from is Point followFrom && main.Path != null)
        {
            (FollowOutcome outcome, string verdict, List<string> edges, string? fault) =
                FollowPath(world, followFrom, goal.Value, followWindow, main.Path.Partial ? main.Path.Goal : null, HeaderPose(header));
            followOutcome = outcome;
            followVerdict = verdict;
            followEdges = edges;
            firstFault = fault;
        }

        return new Outcome(name, pass ? "PASS" : sealedBlock ? "SEALED" : "FAIL", "", world, header, extras,
            start.Value, goal.Value, player, from, main, toPlayer, region, complete, goalIn, playerIn, homeIn, pocket,
            trailLines, firstRefused, followOutcome, followVerdict, followEdges, firstFault);
    }

    private static Outcome Skip(string name, TextTileWorld world, string header, List<string> extras, string reason, Search empty,
        Point start = default, Point goal = default, Point? player = null)
        => new(name, "SKIP", reason, world, header, extras, start, goal, player, null, empty, empty,
            new HashSet<Point>(), false, "", "", "", "", Array.Empty<string>(), null, null, "", Array.Empty<string>(), null);

    // A plans file holds many dumps separated by blank lines; a scenario file holds one.
    internal static IEnumerable<(int, List<string>)> Blocks(string[] lines)
    {
        var block = new List<string>();
        int index = 0;
        bool inRows = false;
        foreach (string line in lines)
        {
            if (line.Length == 0)
            {
                if (inRows && block.Count > 0)
                {
                    yield return (index++, block);
                    block = new List<string>();
                    inRows = false;
                }
                continue;
            }
            block.Add(line);
            if (!IsHeaderLine(line) && !IsExtraLine(line))
                inRows = true;
        }
        if (block.Count > 0)
            yield return (index, block);
    }

    /// <summary>
    /// The two line kinds that are not tiles, named here rather than spelled out at each site.
    /// Anything else in a block is a row of glyphs, which is why a scenario writer may not invent a
    /// new prefix: an unrecognised line becomes terrain the moment it is written.
    /// </summary>
    internal static bool IsHeaderLine(string line)
        => line.StartsWith("tick ", StringComparison.Ordinal) || line.StartsWith("scenario ", StringComparison.Ordinal);

    internal static bool IsExtraLine(string line)
        => line.StartsWith("companion ", StringComparison.Ordinal) || line.StartsWith("player ", StringComparison.Ordinal)
        || line.StartsWith("threat ", StringComparison.Ordinal) || line.StartsWith("trail ", StringComparison.Ordinal)
        || line.StartsWith("markers ", StringComparison.Ordinal);

    internal static string Fmt(Point p) => $"{p.X},{p.Y}";

    internal static string Describe(Search s)
        => $"{(s.Path == null ? "no path" : $"{s.Path.Steps.Count} steps{(s.Path.Partial ? $", partial, ends {Fmt(s.Path.Goal)}" : "")}")}, {s.Used} expansions, {s.Closed.Count} tiles reached, {s.Ms:F1} ms";

    // "npcbox 55795.0,5808.0,20,42" out of a dump header: the companion's own rectangle at the tick
    // the window was written, as the pose the follower starts from. Null for any dump older than the
    // column, which keeps every committed scenario replayable and makes those blocks measure the
    // planner alone, the way they always did.
    internal static BodyPhysics.Pose? HeaderPose(string header)
    {
        const string Key = "npcbox ";
        int at = header.IndexOf(Key, StringComparison.Ordinal);
        if (at < 0)
            return null;
        string[] parts = header[(at + Key.Length)..].Split(' ', 2)[0].Split(',');
        return parts.Length >= 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float left)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float bottom)
                ? new BodyPhysics.Pose(left, bottom)
                : null;
    }

    // "goal 3526,480" out of a dump header; null when the header does not carry that key.
    internal static Point? HeaderPoint(string header, string key)
    {
        int at = header.IndexOf(key + " ", StringComparison.Ordinal);
        if (at < 0)
            return null;
        string[] xy = header[(at + key.Length + 1)..].Split(' ', 2)[0].Split(',');
        return xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y) ? new Point(x, y) : null;
    }

    // A start captured mid-jump lands first: the game plans only from the ground now, so the
    // question is what the body can do from where it comes down. The fall runs to the bottom of
    // the captured window; below it the world is unknown and the start stays unstandable.
    internal static Point? Ground(TextTileWorld world, Point start)
    {
        Point grounded = start;
        while (grounded.Y < world.OriginY + world.Height && !NavGrid.IsBlock(grounded.X, grounded.Y + 1) && !NavGrid.IsStandable(grounded.X, grounded.Y))
            grounded.Y++;
        return NavGrid.NearestStandable(grounded, 2);
    }

    // Each search keeps its own closed set: the recorded-goal search and the player search share
    // the planner's trace slot, and the drawing must show the tiles the search it explains reached.
    internal static Search Run(Point? from, Point goal)
    {
        Point? to = NavGrid.NearestStandable(goal, 3);
        AStar.TraceClosed = new HashSet<Point>();
        int used = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        NavPath? path = from == null || to == null ? null : AStar.Find(from.Value, to.Value, 20000, out used);
        double ms = clock.Elapsed.TotalMilliseconds;
        Timing.PlannerMs += ms;
        return new Search(path, used, new HashSet<Point>(AStar.TraceClosed), path != null && !path.Partial, ms);
    }

    // The navigator run over the simulated body from a standing start at `from` toward the goal's
    // feet, the way the game runs it: one MoveTo per tick, its controls stepped by BodyMotion. The
    // allowance is generous (a minute of ticks plus a second per planned step), because a slow
    // arrival is a pass and a parked body is the failure. A plan the search cut at its budget
    // promises only its last tile, so a body that ends within a tile of that (standing on it,
    // or on the tile its feet rest in) with no path left has walked what it was given, and is
    // its own outcome rather than a park: the stall is the search's budget (AIC-143), never the
    // follower's.
    internal static (FollowOutcome, string, List<string>, string?) FollowPath(TextTileWorld world, Point from, Point goal, (int from, int to) window, Point? partialEnd, BodyPhysics.Pose? recorded)
    {
        var edges = new List<string>();
        // The recorded pose wins over the proven one whenever the dump carries it. This is the whole
        // reason a fall-through that hung in play replayed clean for three attempts: StandAt returns
        // the first of nine sub-tile offsets that fits, so it hands the follower the pose the grid
        // proved the move from, while the game's body was at whatever offset it drifted to. Starting
        // from the tile rather than the box measured the planner and never the body.
        BodyPhysics.Pose? standing = NavGrid.StandAt(from.X, from.Y, false);
        if ((recorded ?? standing) is not BodyPhysics.Pose pose)
            return (FollowOutcome.Parked, $"no pose at {Fmt(from)} to start from", edges, null);
        if (recorded is BodyPhysics.Pose r && standing is BodyPhysics.Pose sp && MathF.Abs(r.Left - sp.Left) > BodyPhysics.Touch)
            edges.Add($"start pose: recorded left {r.Left:F1} bottom {r.Bottom:F1}, the grid would have proven from left {sp.Left:F1} — {r.Left - sp.Left:+0.0;-0.0} px apart");
        Point? to = NavGrid.NearestStandable(goal, 3);
        if (to == null)
            return (FollowOutcome.Parked, "no standable goal", edges, null);
        Vector2 target = NavGrid.FeetWorld(to.Value);
        var navigator = new Navigator();
        BodyState body = BodyState.Standing(pose);
        int reported = 0, faults = 0;
        string? firstFault = null;
        var recent = new Queue<string>();
        int allowance = 3600;
        bool sized = false;
        for (int tick = 1; tick <= allowance; tick++)
        {
            Controls controls = navigator.MoveTo(body, target);
            if (tick == 1 && navigator.LastRejection is { } rejection)
                edges.Add($"initial proof rejected: {rejection.Reason} {rejection.Fault} at predicted tick {rejection.Tick}, entry {rejection.Entry}, predicted {rejection.Predicted}; preparation {navigator.PreparationResult}");
            if (navigator.EdgeCount > reported && navigator.LastEdge is EdgeReport e)
            {
                reported = navigator.EdgeCount;
                edges.Add($"t{tick,5} {e.Kind,-11} {Fmt(e.From)} -> {Fmt(e.Tile)}  proven {e.Expected,3} ticks, took {e.Actual,3}  {(e.Outcome == TraversalFault.None ? "ok" : e.Outcome.ToString().ToUpperInvariant())}");
                if (e.Outcome is not (TraversalFault.None or TraversalFault.Interrupted))
                {
                    faults++;
                    if (firstFault == null)
                    {
                        firstFault = $"{e.Outcome} on {e.Kind} {Fmt(e.From)} -> {Fmt(e.Tile)} at tick {tick}";
                        foreach (string line in recent)
                            edges.Add("   " + line);
                    }
                }
            }
            if (faults >= 3)
                return (FollowOutcome.Parked, $"faulted three times by tick {tick}, the feet at {Fmt(body.FeetTile)} (first: {firstFault})", edges, firstFault);
            if (navigator.Arrived && body.OnGround)
                return (FollowOutcome.Walked, $"arrived in {tick} ticks, {reported} steps performed, {faults} faults{(firstFault != null ? $" (first: {firstFault})" : "")}", edges, firstFault);
            if (!sized && navigator.Path is NavPath planned)
            {
                allowance += planned.Steps.Count * 60;
                sized = true;
            }
            string stateLine = $"t{tick,5} feet {Fmt(body.FeetTile),-9} left {body.Left,8:F1} bottom {body.Bottom,8:F1} vx {body.Vx,5:F2} vy {body.Vy,5:F2} {(body.OnGround ? "ground" : "air   ")}{(body.CollideX ? " wall" : "")}  ask move {controls.MoveX,5:F2}{(controls.Jump ? $" JUMP x{controls.JumpScale:F2}" : "")}{(controls.FallThrough ? " PRESS" : "")}";
            if (tick >= window.from && tick <= window.to)
                Console.WriteLine($"          {stateLine}  step {(navigator.Path is NavPath cur && !cur.Finished ? $"{cur.Current.Kind} {Fmt(cur.Current.From)} -> {Fmt(cur.Current.Tile)}" : "none")}");
            recent.Enqueue(stateLine);
            if (tick >= window.from && tick <= window.to)
                Console.WriteLine($"          attempts: {navigator.EdgeCount} outcomes, {navigator.FaultCount} faults, {navigator.StuckStrikes} strikes, last {navigator.LastFault}, planned {navigator.PlannedThisTick}, plan_ms {navigator.LastPlanMs:F2}");
            if (recent.Count > 24)
                recent.Dequeue();
            body = BodyMotion.Step(world, body, controls);
            if (body.Stuck)
                return (FollowOutcome.Parked, $"the body is stuck inside a shape at feet {Fmt(body.FeetTile)} on tick {tick}", edges, firstFault ?? "Stuck");
        }
        bool pathless = navigator.Path is not NavPath p || p.Finished;
        if (pathless && partialEnd is Point end && Math.Abs(body.FeetTile.X - end.X) <= 1 && Math.Abs(body.FeetTile.Y - end.Y) <= 1)
            return (FollowOutcome.PartialEnd, $"walked the partial plan to its end at {Fmt(end)}, the feet at {Fmt(body.FeetTile)} after {reported} steps, {faults} faults{(firstFault != null ? $" (first: {firstFault})" : "")}; the goal lies past the search's budget", edges, firstFault);
        string where = pathless
            ? "with no path"
            : $"on step {navigator.Path!.Index + 1}/{navigator.Path.Steps.Count}, a {navigator.Path.Current.Kind} {Fmt(navigator.Path.Current.From)} -> {Fmt(navigator.Path.Current.Tile)}";
        return (FollowOutcome.Parked, $"never arrived: after {allowance} ticks the feet are at {Fmt(body.FeetTile)} {where}, {faults} faults{(firstFault != null ? $" (first: {firstFault})" : "")}", edges, firstFault ?? "NeverArrived");
    }

    // Path steps as w j d f; on a failure, every tile the search closed as c, so "no path" reads
    // as "it got this far and no edge left from here".
    internal static string Draw(TextTileWorld world, NavPath? path, Point start, Point goal, HashSet<Point>? reached)
    {
        var overlay = new Dictionary<Point, char>();
        if (reached != null)
            foreach (Point t in reached)
                overlay[t] = 'c';
        if (path != null)
            foreach (NavStep step in path.Steps)
                overlay[step.Tile] = step.Kind switch { MoveKind.Walk => 'w', MoveKind.Jump => 'j', MoveKind.Drop => 'd', _ => 'f' };
        overlay[start] = 'N';
        overlay[goal] = 'G';
        if (world.Markers.TryGetValue('P', out Point p) && p != goal)
            overlay[p] = 'P';
        var sb = new StringBuilder();
        for (int y = world.OriginY; y < world.OriginY + world.Height; y++)
        {
            sb.Append("     ");
            for (int x = world.OriginX; x < world.OriginX + world.Width; x++)
            {
                var t = new Point(x, y);
                if (overlay.TryGetValue(t, out char c)) sb.Append(c);
                else
                {
                    char g = world.Glyph(x, y);
                    sb.Append(g == '.' && NavGrid.IsStandable(x, y) ? 'o' : g);
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>Planner wall-clock over the run, because the simulated jump is the first edge whose cost is a number worth watching.</summary>
internal static class Timing
{
    public static double PlannerMs;
}

/// <summary>
/// One search's answer with its own closed set, so two searches in one scenario never share a
/// drawing. <paramref name="Ms"/> is that one search's own wall clock, printed per route because
/// the run total cannot say whether a change spread its cost evenly or built one expensive route:
/// a graph that multiplies nodes is judged on the worst route, not the mean. Read it beside
/// <paramref name="Used"/> rather than instead of it — expansions are deterministic and the
/// milliseconds are this machine on this run.
/// </summary>
internal record Search(NavPath? Path, int Used, HashSet<Point> Closed, bool Pass, double Ms);

/// <summary>What the follow harness read: the plan walked to the goal, walked to the end a budget-cut plan promised, or a body that parked, faulted out or was lost.</summary>
internal enum FollowOutcome { Walked, PartialEnd, Parked }
