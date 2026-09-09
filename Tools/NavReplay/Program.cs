#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.SharedMovementSystem;

// Replays navigation scenarios off-game: for every file given, load its tile window, ask the
// mod's own planner for the path the game asked for (S to G, the recorded plan) and, when the
// player's feet are also in the window, the path to the player (N or S to P), draw the answer
// over the window, and say pass or fail. A scenario is the telemetry's plan-dump shape (or a
// scenario file, which is the same with entity lines). A scenario passes when the recorded
// goal is reached by a whole path; the player line is printed beside it because "could it
// have reached the player" is the question the design is judged on, and the two can differ
// when the positioner picked a spot the player never stood on.
// Exit code: 0 when every executed scenario passed and nothing was skipped or missing, 1
// otherwise; the counts are in the last line, never in the status, which wraps at 256.

if (args.Length == 1 && args[0] == "--self-test") return VerifyMovementContracts.Run();

int failed = 0, passed = 0, sealedCount = 0, skipped = 0, missing = 0;
int churnTiles = 0, churnWrong = 0;
int followPassed = 0, followPartial = 0, followFailed = 0;
bool traceJump = false, churn = false, follow = false;
Point? edgesFrom = null, traceWalkFrom = null;
int traceWalkDir = 1;
// The ticks of a follow run whose state is printed as it happens (--follow-ticks A,B); none by default.
(int from, int to) followTickWindow = (0, -1);
var files = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg == "--trace-jump")
        traceJump = true;
    else if (arg == "--trace-walk" && i + 1 < args.Length && args[i + 1].Split(',') is [var wx, var wy, var wd] && int.TryParse(wx, out int wxi) && int.TryParse(wy, out int wyi) && int.TryParse(wd, out int wdi))
    {
        traceWalkFrom = new Point(wxi, wyi);
        traceWalkDir = Math.Sign(wdi) == 0 ? 1 : Math.Sign(wdi);
        i++;
    }
    else if (arg == "--edges" && i + 1 < args.Length && args[i + 1].Split(',') is [var ex, var ey] && int.TryParse(ex, out int exi) && int.TryParse(ey, out int eyi))
    {
        edgesFrom = new Point(exi, eyi);
        i++;
    }
    else if (arg == "--no-cache")
        AStar.CacheEdges = false;
    else if (arg == "--churn")
        churn = true;
    else if (arg == "--follow")
        follow = true;
    else if (arg == "--follow-ticks" && i + 1 < args.Length && args[i + 1].Split(',') is [var fa, var fb] && int.TryParse(fa, out int fai) && int.TryParse(fb, out int fbi))
    {
        follow = true;
        followTickWindow = (fai, fbi);
        i++;
    }
    else if (Directory.Exists(arg))
        files.AddRange(Directory.GetFiles(arg, "*.txt"));
    else if (File.Exists(arg))
        files.Add(arg);
    else
    {
        Console.Error.WriteLine($"no such file: {arg}");
        missing++;
    }
}
if (files.Count == 0)
{
    Console.Error.WriteLine("usage: NavReplay <scenario.txt | folder> ...");
    return 2;
}
files.Sort(StringComparer.Ordinal);

foreach (string file in files)
{
    foreach ((int index, List<string> block) in Blocks(File.ReadAllLines(file)))
    {
        var world = TextTileWorld.Parse(block, out string header, out List<string> extras);
        NavGrid.World = world;
        // Every block is its own world, and the edge cache remembers the last one's tiles.
        AStar.InvalidateEdges();
        AStar.AllowLava = false;
        AStar.Avoid.Clear();
        string name = $"{Path.GetFileName(file)}#{index}";

        Point? start = world.Markers.TryGetValue('S', out Point s) ? s : world.Markers.TryGetValue('N', out Point n0) ? n0 : null;
        Point? goal = world.Markers.TryGetValue('G', out Point g) ? g : null;
        Point? player = world.Markers.TryGetValue('P', out Point p) ? p : null;
        // The header carries the recorded request in full; the grid only carries what the window
        // captured and what no other marker hid. A goal the header names but the window does not
        // hold is untestable, never a pass to the player's tile instead.
        Point? headerGoal = HeaderPoint(header, "goal");
        Point? headerStart = HeaderPoint(header, "start");
        goal ??= headerGoal ?? (headerStart == null && player != null ? player : null);
        start ??= headerStart;
        // After the goal is settled, deliberately: the header's player must not become a goal for a
        // block that names no start, which is what the line above uses the marker player for. A
        // hand-cut fixture whose player stands on the goal tile can carry only one glyph there, so
        // its player exists in the header alone, and the flood lines below need him or the block
        // reads as having no player at all.
        player ??= HeaderPoint(header, "player");
        if (start == null || goal == null)
        {
            Console.WriteLine($"SKIP {name}: no start or goal ({header})");
            skipped++;
            continue;
        }
        if (!world.InWorld(goal.Value.X, goal.Value.Y))
        {
            Console.WriteLine($"SKIP {name}: recorded goal {Fmt(goal.Value)} is outside the captured window ({header})");
            skipped++;
            continue;
        }

        // --edges X,Y: every edge each traversal proves out of that tile in this block's world,
        // which is the planner's own answer to "what can the body do from here".
        if (edgesFrom is Point node)
        {
            BodyPhysics.Pose? here = NavGrid.StandAt(node.X, node.Y, false);
            Console.WriteLine($"edges from {Fmt(node)} in {name}: {(here is BodyPhysics.Pose hp ? $"pose left {hp.Left} bottom {hp.Bottom}" : "no pose (not a node)")}");
            foreach (Traversal traversal in Traversal.Planning)
                foreach (NavEdge edge in traversal.Candidates(NavNode.At(node), here, false))
                    Console.WriteLine($"   {edge.Step.Kind,-11} -> {Fmt(edge.Step.Tile)}  cost {edge.Move:F2} fall {edge.Fall} ticks {edge.Step.Ticks} scale {edge.Step.JumpScale:F2} startVx {edge.Step.StartVx:F2} steerX {edge.Step.SteerX:F1}");
            skipped++;
            continue;
        }

        // --trace-jump: the simulated jump from S to G, tick by tick, from standing and from a
        // run-up, so a jump the planner refuses can be read as the arc the body would fly.
        if (traceJump)
        {
            // A trace passes when either start lands on the goal tile, fails when neither does,
            // and skips when there is no pose to jump from, so the exit code means the same thing
            // it means for a replay.
            switch (TraceJump(start.Value, goal.Value))
            {
                case true: passed++; break;
                case false: failed++; break;
                case null: skipped++; break;
            }
            continue;
        }

        // --trace-walk X,Y,DIR: the body driven from that tile's pose that way at the walk speed
        // through its own tick, as the walk traversal proves its edges, so a walk the planner
        // refuses can be read as the ticks the body took. Given as a tile and not as a marker,
        // because a marker glyph written into the map replaces the tile's shape.
        if (traceWalkFrom is Point walkFrom)
        {
            TraceWalk(walkFrom, traceWalkDir);
            skipped++;
            continue;
        }

        Point? from = Ground(world, start.Value);
        Search main = Run(from, goal.Value);
        Search toPlayer = player is Point pl && pl != goal ? Run(from, pl) : main;
        bool pass = main.Pass;

        // The positioner's own question, with the positioner's own budget: is the goal inside the
        // region the companion can flood to from its feet? "out" with a complete region is a goal
        // that can never be reached; "out" with the budget spent is a goal the flood did not get to.
        // It is answered before the first line prints, because the first line is the verdict.
        HashSet<Point> region = new();
        bool complete = false, sealedBlock = false;
        string goalIn = "", playerIn = "", pocket = "", homeIn = "";
        if (from is Point f)
        {
            // The sealed verdicts read whether a flood ever touched the window's edge, and a
            // flood served from the edge cache never reads the world at all; each flood that
            // answers that question starts from an empty cache.
            AStar.InvalidateEdges();
            world.AskedOutside = false;
            region = AStar.Region(f, AICompanion.Companion.Brain.BehaviourSelection.Weights.ReachFloodBudget, out complete);
            bool startClipped = world.AskedOutside;
            goalIn = region.Contains(goal.Value) ? "in" : "out";
            playerIn = player is Point pl3 ? (region.Contains(pl3) ? ", player in" : ", player out") : "";
            // The same flood again with the edges that have no way back refused, which is the one
            // the positioner actually scores against: a tile in the first region and not this one
            // is somewhere the body can get to and not come home from, so it is offered only when
            // nothing returnable is left. Reported beside the raw region rather than replacing it,
            // because SEALED still has to mean what it has always meant, a region the world itself
            // closes, and a region that closes only because its exit is one-way is a different fact.
            HashSet<Point> returnable = AStar.Region(f, AICompanion.Companion.Brain.BehaviourSelection.Weights.ReachFloodBudget, out _, refuseOneWay: true);
            // And the positioner's own escape hatch, modelled here or this line reports a region the
            // positioner does not use: a returnable region that does not hold the player is the wrong
            // map, because being stuck is having no way to the player and not having no way back, so
            // the positioner floods again without the refusal. Printed as "player only via a one-way
            // drop" because that is the whole reason the returnable region was discarded.
            bool playerOut = player is Point pl4 && region.Contains(pl4) && !returnable.Contains(pl4);
            homeIn = playerOut ? "; the player is reachable only through an edge with no way back, so the positioner floods without the refusal and scores the raw region"
                : returnable.Count == region.Count ? ""
                : $"; {returnable.Count} of them returnable, goal {(returnable.Contains(goal.Value) ? "in" : "out")}";
            // A complete region with the goal out is one of three things, and the window's edge
            // tells them apart. The flood records whether it ever read a tile outside the window
            // (the edge is a wall only to the tool), so a flood that never asked is a region the
            // world itself closes. Closed around the start it is a pocket with no way out, which
            // is a rescue's job and not the planner's (Caner, 2026-09-08: a pit too deep to jump
            // out of is not a pathfinding failure); closed around the goal it is a spot the
            // positioner should never have offered (AIC-135's finding); clipped on both sides it
            // is undecidable as cut and wants reshape.py --pad.
            if (!pass && complete && goalIn == "out")
            {
                AStar.InvalidateEdges();
                world.AskedOutside = false;
                HashSet<Point> goalRegion = Ground(world, goal.Value) is Point goalFeet
                    ? AStar.Region(goalFeet, AICompanion.Companion.Brain.BehaviourSelection.Weights.ReachFloodBudget, out _)
                    : new HashSet<Point>();
                bool goalClipped = world.AskedOutside;
                pocket = !startClipped ? "; SEALED START IN MODEL: the graph closes inside the capture; physical impossibility is not established"
                    : !goalClipped ? $"; SEALED GOAL IN MODEL: the goal's region ({goalRegion.Count} tiles) closes inside the capture; inspect terrain and movement coverage"
                    : "; both regions reach the window's edge: undecidable as cut, widen it with reshape.py --pad";
                // A sealed block is a verdict, not a failure: the planner answered "no route" and
                // the world agrees, so it counts on its own and does not fail the run.
                sealedBlock = !startClipped || !goalClipped;
            }
        }
        if (pass) passed++; else if (sealedBlock) sealedCount++; else failed++;

        Console.WriteLine($"{(pass ? "PASS" : sealedBlock ? "SEALED" : "FAIL")} {name}: {header}");
        Console.WriteLine($"     recorded goal: start {Fmt(start.Value)} -> {(from == null ? "no standable tile" : Fmt(from.Value))}, goal {Fmt(goal.Value)}, {Describe(main)}");
        if (player is Point pl2 && pl2 != goal)
            Console.WriteLine($"     player:        {(toPlayer.Pass ? "reached" : "NOT reached")} at {Fmt(pl2)}, {Describe(toPlayer)}");
        if (from != null)
        {
            Console.WriteLine($"     reach flood:   {region.Count} tiles, {(complete ? "complete" : "budget spent")}, goal {goalIn}{playerIn}{homeIn}{pocket}");
            // The player's trail is the design's own pass line: every tile the player's feet were
            // in is a tile the companion must be able to stand in and get to. The first tile the
            // grid refuses names the missing link; a tile outside a complete region is refused too.
            foreach (string extra in extras)
            {
                if (!extra.StartsWith("trail "))
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
                string verdict = refused != null ? "first refused " + refused
                    : inWindow == 0 ? "nothing to check"
                    : unknown > 0 ? $"every one standable, {unknown} beyond the flood's budget so unchecked for reach"
                    : "every one standable and reachable";
                Console.WriteLine($"     trail:         {inWindow} of {total} tiles in the window, {verdict}{(malformed > 0 ? $"; {malformed} malformed pair(s) ignored" : "")}");
            }
        }
        Console.WriteLine(Draw(world, main.Path, start.Value, goal.Value, pass ? null : main.Closed));

        // --follow: the path is walked. The real navigator is run tick by tick over the body's
        // own motion rule from the start pose, its controls applied by BodyMotion.Step, until it
        // arrives, a step faults, the body is stuck, or the allowance runs out; every step it
        // performs is printed with what was proven against what happened. A block that plans and
        // does not follow is the class of defect the traversals exist to end, and this is the
        // line that shows it without a game running.
        if (follow && from is Point followFrom && main.Path != null)
        {
            (FollowOutcome outcome, string verdict, List<string> edges) = FollowPath(world, followFrom, goal.Value, followTickWindow, main.Path.Partial ? main.Path.Goal : null, HeaderPose(header));
            if (outcome == FollowOutcome.Walked) followPassed++; else if (outcome == FollowOutcome.PartialEnd) followPartial++; else followFailed++;
            Console.WriteLine($"     follow:        {outcome switch { FollowOutcome.Walked => "PASS", FollowOutcome.PartialEnd => "PARTIAL", _ => "FAIL" }}, {verdict}");
            foreach (string line in edges)
                Console.WriteLine($"       {line}");
        }

        // --churn: the cache's invalidation box, tested the only way it can be. A corpus replay
        // never changes a tile, so "the same verdicts with and without the cache" proves nothing
        // about what a kill drops. Here every tile the found path stands on or steps through is
        // broken in turn, the planner is told the way the game tells it, and the plan served
        // from the warm cache must equal the plan from an empty one; a difference is an edge the
        // box kept that the broken tile should have taken with it.
        if (churn && from is Point cf && main.Path is NavPath found)
        {
            foreach (Point tile in ChurnTiles(found))
            {
                if (!world.InWorld(tile.X, tile.Y))
                    continue;
                char was = world.Glyph(tile.X, tile.Y);
                world.Set(tile.X, tile.Y, '.');
                AStar.TileChanged(tile.X, tile.Y);
                string warm = Signature(cf, goal.Value);
                AStar.InvalidateEdges();
                string cold = Signature(cf, goal.Value);
                world.Set(tile.X, tile.Y, was);
                AStar.TileChanged(tile.X, tile.Y);
                churnTiles++;
                if (warm != cold)
                {
                    churnWrong++;
                    Console.WriteLine($"     CHURN {name}: breaking {Fmt(tile)} ('{was}') left a stale edge: warm {warm} vs cold {cold}");
                }
            }
        }
    }
}
Console.WriteLine($"{passed}/{passed + failed} passed, {sealedCount} model-closed (not proof of physical impossibility), {skipped} skipped, {missing} missing inputs, planner {Timing.PlannerMs:F0} ms in total"
    + (churn ? $"; churn: {churnTiles} tiles broken, {churnWrong} stale plans" : "")
    + (follow ? $"; follow: {followPassed} walked, {followPartial} to a partial plan's end, {followFailed} not" : ""));
return failed == 0 && skipped == 0 && missing == 0 && passed > 0 && churnWrong == 0 && followFailed == 0 ? 0 : 1;

// The navigator run over the simulated body from a standing start at `from` toward the goal's
// feet, the way the game runs it: one MoveTo per tick, its controls stepped by BodyMotion. The
// allowance is generous (a minute of ticks plus a second per planned step), because a slow
// arrival is a pass and a parked body is the failure. A plan the search cut at its budget
// promises only its last tile, so a body that ends within a tile of that (standing on it,
// or on the tile its feet rest in) with no path left has walked what it was given, and is
// its own outcome rather than a park: the stall is the search's budget (AIC-143), never the
// follower's.
static (FollowOutcome, string, List<string>) FollowPath(TextTileWorld world, Point from, Point goal, (int from, int to) window, Point? partialEnd, BodyPhysics.Pose? recorded)
{
    var edges = new List<string>();
    // The recorded pose wins over the proven one whenever the dump carries it. This is the whole
    // reason a fall-through that hung in play replayed clean for three attempts: StandAt returns
    // the first of nine sub-tile offsets that fits, so it hands the follower the pose the grid
    // proved the move from, while the game's body was at whatever offset it drifted to. Starting
    // from the tile rather than the box measured the planner and never the body.
    BodyPhysics.Pose? standing = NavGrid.StandAt(from.X, from.Y, false);
    if ((recorded ?? standing) is not BodyPhysics.Pose pose)
        return (FollowOutcome.Parked, $"no pose at {Fmt(from)} to start from", edges);
    if (recorded is BodyPhysics.Pose r && standing is BodyPhysics.Pose sp && MathF.Abs(r.Left - sp.Left) > BodyPhysics.Touch)
        edges.Add($"start pose: recorded left {r.Left:F1} bottom {r.Bottom:F1}, the grid would have proven from left {sp.Left:F1} — {r.Left - sp.Left:+0.0;-0.0} px apart");
    Point? to = NavGrid.NearestStandable(goal, 3);
    if (to == null)
        return (FollowOutcome.Parked, "no standable goal", edges);
    Vector2 target = NavGrid.FeetWorld(to.Value);
    var navigator = new Navigator();
    BodyState body = BodyState.Standing(pose);
    int reported = 0, faults = 0;
    string? firstFault = null;
    // The last ticks before the first fault, printed with it: the body and what it was asked,
    // which is the instrument a misland is read from instead of argued about.
    var recent = new Queue<string>();
    // Sized once, from the first plan the navigator makes: a minute of ticks plus a second a
    // step, which an arrival never needs and a parked body always exhausts.
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
        // In the game two strikes make the brain ask for another spot; here the goal is fixed,
        // so a third fault is the verdict and not a loop of plans.
        if (faults >= 3)
            return (FollowOutcome.Parked, $"faulted three times by tick {tick}, the feet at {Fmt(body.FeetTile)} (first: {firstFault})", edges);
        // Standing, not merely near: the navigator's own Arrived is a distance to the target, which
        // a body flying past the goal satisfies in mid-air with steps still unperformed, so it is
        // not a verdict that every move was made as proven (Codex review of 7525a1b).
        if (navigator.Arrived && body.OnGround)
            return (FollowOutcome.Walked, $"arrived in {tick} ticks, {reported} steps performed, {faults} faults{(firstFault != null ? $" (first: {firstFault})" : "")}", edges);
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
            return (FollowOutcome.Parked, $"the body is stuck inside a shape at feet {Fmt(body.FeetTile)} on tick {tick}", edges);
    }
    bool pathless = navigator.Path is not NavPath p || p.Finished;
    if (pathless && partialEnd is Point end && Math.Abs(body.FeetTile.X - end.X) <= 1 && Math.Abs(body.FeetTile.Y - end.Y) <= 1)
        return (FollowOutcome.PartialEnd, $"walked the partial plan to its end at {Fmt(end)}, the feet at {Fmt(body.FeetTile)} after {reported} steps, {faults} faults{(firstFault != null ? $" (first: {firstFault})" : "")}; the goal lies past the search's budget", edges);
    string where = pathless
        ? "with no path"
        : $"on step {navigator.Path!.Index + 1}/{navigator.Path.Steps.Count}, a {navigator.Path.Current.Kind} {Fmt(navigator.Path.Current.From)} -> {Fmt(navigator.Path.Current.Tile)}";
    return (FollowOutcome.Parked, $"never arrived: after {allowance} ticks the feet are at {Fmt(body.FeetTile)} {where}, {faults} faults{(firstFault != null ? $" (first: {firstFault})" : "")}", edges);
}

// The tiles a path depends on: every step's tile and the tile under it (the support the
// step stands on), which is what a pickaxe following the route would break.
static IEnumerable<Point> ChurnTiles(NavPath path)
{
    var seen = new HashSet<Point>();
    foreach (NavStep step in path.Steps)
    {
        if (seen.Add(step.Tile))
            yield return step.Tile;
        var under = new Point(step.Tile.X, step.Tile.Y + 1);
        if (seen.Add(under))
            yield return under;
    }
}

// A plan reduced to a string that two plans can be compared by: each step's tile and kind, the
// tile it leaves from, and every parameter the follower executes it with (the jump's scale and
// start speed, a descent's steer line, the proven tick count the allowance is sized from, whether
// the move starts from rest, and the mobility state it lands in), because two routes over the same
// tiles that ask for different moves are two plans. Ticks, FromRest, From and Mobility were added
// after the Codex review of 7525a1b found the signature blind to fields that change execution and
// fault timing, which made a churn pass agree where the two plans genuinely differed.
static string Signature(Point from, Point goal)
{
    Point? to = NavGrid.NearestStandable(goal, 3);
    if (to == null)
        return "no goal";
    NavPath? path = AStar.Find(from, to.Value, 20000, out _);
    if (path == null)
        return "none";
    var sb = new System.Text.StringBuilder(path.Partial ? "partial " : "");
    foreach (NavStep step in path.Steps)
        sb.Append(step.Kind.ToString()[0]).Append(Fmt(step.Tile)).Append('<').Append(Fmt(step.From))
          .Append('/').Append(step.JumpScale.ToString("0.##")).Append('/').Append(step.StartVx.ToString("0.##")).Append('/').Append(step.SteerX.ToString("0.#"))
          .Append('/').Append(step.Ticks).Append(step.FromRest ? "/rest" : "/run")
          .Append('/').Append(step.Mobility.AirJumpsLeft).Append(step.Mobility.Latched ? "L" : "-").Append(step.Mobility.DashCooldown)
          .Append(' ');
    return sb.ToString().TrimEnd();
}

static string Fmt(Point p) => $"{p.X},{p.Y}";

static bool? TraceJump(Point start, Point goal)
{
    if (NavGrid.StandAt(start.X, start.Y, false) is not BodyPhysics.Pose from)
    {
        Console.WriteLine($"trace-jump: no pose at {Fmt(start)}");
        return null;
    }
    // Every profile the planner tries, in its order: the rise between the two poses' bottoms in
    // pixels rounded up to tiles picks the scales, and each is flown at the walk, half of it and
    // from a stand; without a pose at the goal the row difference stands in for the rise.
    float goalBottom = NavGrid.StandAt(goal.X, goal.Y, false)?.Bottom ?? (goal.Y + 1) * 16f;
    int rise = (int)Math.Ceiling((from.Bottom - goalBottom) / 16f);
    bool landedOnGoal = false;
    foreach ((float scale, float startVx) in JumpTraversal.JumpProfiles(rise, Math.Sign(goal.X - start.X)))
    {
        Console.WriteLine($"trace-jump {Fmt(start)} -> {Fmt(goal)} rise {rise} scale {scale:F2} startVx {startVx:F2}: pose left {from.Left} bottom {from.Bottom}");
        BodyPhysics.Pose? landing = BodyPhysics.SimulateJump(NavGrid.World, from, scale, startVx, goal.X, goal.Y, 120, out int ticks, tick =>
            Console.WriteLine($"   t{tick.Tick,3} left {tick.Left,7:F1} bottom {tick.Bottom,7:F1} vx {tick.Vx,5:F2} vy {tick.Vy,5:F2} feet {tick.FeetColumn},{tick.FeetRow}"));
        if (landing is BodyPhysics.Pose l)
        {
            var tile = new Point((int)Math.Floor(l.CentreX / 16f), BodyPhysics.FeetRow(l.Bottom));
            landedOnGoal |= tile == goal;
            Console.WriteLine($"   landed after {ticks} ticks at left {l.Left:F1} bottom {l.Bottom:F1}, feet tile {Fmt(tile)}{(tile == goal ? " (the goal)" : "")}");
        }
        else
            Console.WriteLine($"   never landed within {Math.Min(ticks, 120)} ticks, or fell past the goal row");
    }
    return landedOnGoal;
}

// The walk traversal's own proof, printed: from the pose at the tile, the walk speed that way,
// every tick's state, and where the body first stands past the next column's centre.
static void TraceWalk(Point start, int dir)
{
    if (NavGrid.StandAt(start.X, start.Y, false) is not BodyPhysics.Pose from)
    {
        Console.WriteLine($"trace-walk: no pose at {Fmt(start)}");
        return;
    }
    Console.WriteLine($"trace-walk from {Fmt(start)} dir {dir}: pose left {from.Left} bottom {from.Bottom}");
    BodyState state = BodyState.Standing(from);
    var controls = new Controls(dir * BodyPhysics.WalkSpeed);
    for (int tick = 1; tick <= 60; tick++)
    {
        state = BodyMotion.Step(NavGrid.World, state, controls);
        Console.WriteLine($"   t{tick,3} left {state.Left,7:F1} bottom {state.Bottom,7:F1} vx {state.Vx,5:F2} vy {state.Vy,5:F2} feet {Fmt(state.FeetTile)} {(state.OnGround ? "ground" : "air")}{(state.CollideX ? " wall" : "")}{(state.Stuck ? " STUCK" : "")}");
        if (state.Stuck || state.CollideX)
        {
            Console.WriteLine($"   ended: {(state.Stuck ? "stuck in a shape" : "met a shape sideways")} after {tick} ticks");
            return;
        }
        if (state.OnGround && dir * (state.FeetTile.X - (start.X + dir)) >= 0)
        {
            bool node = NavGrid.StandAt(state.FeetTile.X, state.FeetTile.Y, false) != null;
            Console.WriteLine($"   stands in the next column after {tick} ticks at feet tile {Fmt(state.FeetTile)}{(node ? "" : " (not a node)")}");
            return;
        }
    }
    Console.WriteLine("   never stood in the next column within 60 ticks");
}

static string Describe(Search s)
    => $"{(s.Path == null ? "no path" : $"{s.Path.Steps.Count} steps{(s.Path.Partial ? $", partial, ends {Fmt(s.Path.Goal)}" : "")}")}, {s.Used} expansions, {s.Closed.Count} tiles reached";

// "npcbox 55795.0,5808.0,20,42" out of a dump header: the companion's own rectangle at the tick
// the window was written, as the pose the follower starts from. Null for any dump older than the
// column, which keeps every committed scenario replayable and makes those blocks measure the
// planner alone, the way they always did.
static BodyPhysics.Pose? HeaderPose(string header)
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
static Point? HeaderPoint(string header, string key)
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
static Point? Ground(TextTileWorld world, Point start)
{
    Point grounded = start;
    while (grounded.Y < world.OriginY + world.Height && !NavGrid.IsBlock(grounded.X, grounded.Y + 1) && !NavGrid.IsStandable(grounded.X, grounded.Y))
        grounded.Y++;
    return NavGrid.NearestStandable(grounded, 2);
}

// Each search keeps its own closed set: the recorded-goal search and the player search share
// the planner's trace slot, and the drawing must show the tiles the search it explains reached.
static Search Run(Point? from, Point goal)
{
    Point? to = NavGrid.NearestStandable(goal, 3);
    AStar.TraceClosed = new HashSet<Point>();
    int used = 0;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    NavPath? path = from == null || to == null ? null : AStar.Find(from.Value, to.Value, 20000, out used);
    Timing.PlannerMs += clock.Elapsed.TotalMilliseconds;
    return new Search(path, used, new HashSet<Point>(AStar.TraceClosed), path != null && !path.Partial);
}

// A plans file holds many dumps separated by blank lines; a scenario file holds one.
static IEnumerable<(int, List<string>)> Blocks(string[] lines)
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
        if (!line.StartsWith("tick ") && !line.StartsWith("scenario ") && !line.StartsWith("companion ") && !line.StartsWith("player ") && !line.StartsWith("threat ") && !line.StartsWith("trail ") && !line.StartsWith("markers "))
            inRows = true;
    }
    if (block.Count > 0)
        yield return (index, block);
}

// Path steps as w j d f; on a failure, every tile the search closed as c, so "no path" reads
// as "it got this far and no edge left from here".
static string Draw(TextTileWorld world, NavPath? path, Point start, Point goal, HashSet<Point>? reached)
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

/// <summary>Planner wall-clock over the run, because the simulated jump is the first edge whose cost is a number worth watching.</summary>
static class Timing
{
    public static double PlannerMs;
}

/// <summary>One search's answer with its own closed set, so two searches in one scenario never share a drawing.</summary>
record Search(NavPath? Path, int Used, HashSet<Point> Closed, bool Pass);

/// <summary>What the follow harness read: the plan walked to the goal, walked to the end a budget-cut plan promised, or a body that parked, faulted out or was lost.</summary>
enum FollowOutcome { Walked, PartialEnd, Parked }
