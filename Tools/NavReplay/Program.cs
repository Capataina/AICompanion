#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;

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

// The portable tier's reset. It is smaller than the engine tier's because this process holds no
// game: what a case here can leave behind is the shared planning allowance, the search's own
// policy switches, the edge cache, the executed-route archive and the census. The allowances are
// lifted for every case, so the wall clock cannot decide how far a search got — the rows that are
// about a deadline turn it back on around themselves and put it back, because the regime belongs to
// the case and the deadline belongs to the row.
EmitLedgerRows.ResetBeforeCase = keepProductionAllowances =>
{
    LimitPlanningWork.Unbounded = !keepProductionAllowances;
    LimitPlanningWork.End();
    AStar.AllowLava = false;
    AStar.AllowOneWayDrops = true;
    AStar.MsBudget = 0;
    AStar.TraceClosed = null;
    AStar.InvalidateEdges();
    RememberExecutedRoutes.World.Clear();
    BehaviourCensus.Reset();
};

if (args.Length == 1 && args[0] == "--self-test")
    // Neither case names an allowance mode, because neither lifts one: the deadline switch below is
    // set for the replay modes and not for the self-test, whose own fixtures include the ones that
    // exist to tell an unfinished search from exhausted terrain.
    return EmitLedgerRows.Case("nav-replay", "NavReplay", "the portable movement core keeps its state, safety, retention and policy contracts",
            VerifyMovementContracts.Run)
        + EmitLedgerRows.Case("nav-replay", "NavReplay", "the corpus mirror is exact and a reduction preserves the failure it shrank",
            VerifyMirrorAndShrink.Run,
            killedBy: "a reflection about the wrong column, an unpadded short row, or a reduction that keeps the file small and loses the failure");

// Every millisecond allowance in the planning core is lifted for the replay modes below, and that
// is what makes the corpus an oracle rather than a measurement of the machine. `AStar.Find` and
// `AStar.Region` both turn their budget into a wall-clock deadline through `LimitPlanningWork`, so
// without this a search under load reaches less of the world than the same search on an idle
// machine — and a mirror relation checked against an oracle that disagrees with itself is a test of
// the oracle. It held before only by the accident that nothing in this tool ever set
// `AStar.MsBudget`, which is a static any future caller could leave behind; now it is a property of
// the modes that need it. The work-count limits each query carries are untouched, so a search still
// stops, deterministically.
//
// It is set here rather than at the top of the file, and that placement is load-bearing: the
// contract suite above owns fixtures that exist to tell an unfinished search from exhausted
// terrain, and lifting the deadline under them makes the distinction they check impossible to
// express. A deadline fixture is one of the few things that legitimately reads a clock.
LimitPlanningWork.Unbounded = true;

int failed = 0, passed = 0, sealedCount = 0, skipped = 0, missing = 0;
int churnTiles = 0, churnWrong = 0;
int followPassed = 0, followPartial = 0, followFailed = 0;
int mirrorAgreed = 0, mirrorDisagreed = 0;
bool traceJump = false, churn = false, follow = false;
// --mirror: every block run twice, once as captured and once reflected left to right, with the two
// verdicts required to agree. Nothing about the world prefers a direction, so a disagreement is an
// asymmetry in our own code and is a row rather than a coincidence somebody notices.
bool mirror = false;
// --compare-jump FROMX,FROMY,TOX,TOY: the take-off tile and the landing tile of one jump edge,
// and --compare-ticks prints every tick of every run rather than the summary alone.
(Point from, Point to)? compareJump = null;
bool compareVerbose = false;
// --audit-jumps: every jump the planner proves in every block, run through the performer. The
// share that survives is the offline twin of the game census's jump completion rate.
bool auditJumps = false;
var auditTotal = new CompareJumpPaths.Audit();
// --shrink <scenario>: delta debugging over the tiles, keeping the failure's signature.
string? shrink = null;
int shrinkBudget = 3000;
// --extract-scenario <capture.tsv> <tick>: a window around the companion at that tick, cut from the
// capture's own terrain snapshots into a committed-format scenario.
(string capture, int tick)? extract = null;
int extractWidth = 48, extractHeight = 32;
// The sideways speed the body carried into the recorded entry; a plan dump's npcbox records the
// rectangle and not the velocity, so it is supplied rather than guessed at.
float entryVx = 0f;
BodyState? entryState = null;

static BodyState? ParseEntryState(string text)
{
    string[] parts = text.Split(',');
    if (parts.Length is < 3 or > 4) return null;
    if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float left)
        || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float bottom)
        || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vx))
        return null;
    bool ground = parts.Length < 4 || parts[3] is not ("0" or "false" or "False");
    return new BodyState(left, bottom, vx, 0f, ground, Capabilities: MovementCapabilities.Basic);
}
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
    else if (arg == "--compare-jump" && i + 1 < args.Length && args[i + 1].Split(',') is [var cax, var cay, var cbx, var cby]
        && int.TryParse(cax, out int caxi) && int.TryParse(cay, out int cayi) && int.TryParse(cbx, out int cbxi) && int.TryParse(cby, out int cbyi))
    {
        compareJump = (new Point(caxi, cayi), new Point(cbxi, cbyi));
        i++;
    }
    else if (arg == "--compare-ticks")
        compareVerbose = true;
    else if (arg == "--audit-jumps")
        auditJumps = true;
    else if (arg == "--mirror")
        mirror = true;
    else if (arg == "--shrink" && i + 1 < args.Length)
    {
        shrink = args[i + 1];
        i++;
    }
    else if (arg == "--shrink-budget" && i + 1 < args.Length && int.TryParse(args[i + 1], out int budget))
    {
        shrinkBudget = budget;
        i++;
    }
    else if (arg == "--extract-scenario" && i + 2 < args.Length && int.TryParse(args[i + 2], out int extractTick))
    {
        extract = (args[i + 1], extractTick);
        i += 2;
    }
    else if (arg == "--size" && i + 1 < args.Length && args[i + 1].Split('x') is [var sw, var sh] && int.TryParse(sw, out int swi) && int.TryParse(sh, out int shi))
    {
        extractWidth = swi;
        extractHeight = shi;
        i++;
    }
    else if (arg == "--entry-vx" && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float evx))
    {
        entryVx = evx;
        i++;
    }
    // --entry-state LEFT,BOTTOM,VX[,GROUND]: the body the rejection was recorded against, in
    // full. A Rejection record in an events file carries exactly this, so a refusal seen in play
    // is replayable offline without a window whose body happens to stand in the right place.
    else if (arg == "--entry-state" && i + 1 < args.Length && ParseEntryState(args[i + 1]) is BodyState parsed)
    {
        entryState = parsed;
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

// --extract-scenario runs before the file list is consulted, because its input is a capture rather
// than a scenario and its output is the scenario everything else takes for granted.
if (extract is (string capturePath, int captureTick))
{
    if (!File.Exists(capturePath))
    {
        Console.Error.WriteLine($"no such capture: {capturePath}");
        EmitLedgerRows.Error("nav-replay", "corpus", "a capture becomes a scenario", $"no such capture: {capturePath}");
        return 2;
    }
    try
    {
        ExtractScenarioFromCapture.Extract cut = ExtractScenarioFromCapture.Run(capturePath, captureTick, extractWidth, extractHeight, null, Console.WriteLine);
        Console.WriteLine($"extracted {cut.Width}x{cut.Height} around {ReplayOneBlock.Fmt(cut.Start)} at tick {cut.Tick}: "
            + $"goal {ReplayOneBlock.Fmt(cut.Goal)}, player {ReplayOneBlock.Fmt(cut.Player)}, {cut.TrailLength} trail tiles, "
            + $"{cut.Snapshots} snapshots covering {cut.Known} of {cut.Width * cut.Height} tiles ({cut.Coverage:F1}%) as last written, the rest closed, "
            + $"oldest contributing snapshot {cut.OldestAgeSeconds:F1}s before the tick");
        EmitLedgerRows.Measure("nav-replay", "corpus", "extracted-window-coverage", cut.Coverage, "%", "up",
            mode: "unbounded-allowances", message: $"{Path.GetFileName(cut.Path)}: {cut.Known} of {cut.Width * cut.Height} tiles carried by {cut.Snapshots} snapshots");
        return 0;
    }
    catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException)
    {
        Console.Error.WriteLine($"extract-scenario: {e.Message}");
        EmitLedgerRows.Error("nav-replay", "corpus", "a capture becomes a scenario", e.Message);
        return 1;
    }
}

if (shrink is string shrinkPath)
{
    if (!File.Exists(shrinkPath))
    {
        Console.Error.WriteLine($"no such scenario: {shrinkPath}");
        return 2;
    }
    ShrinkFailingScenario.Result? reduced = ShrinkFailingScenario.Run(shrinkPath, follow, shrinkBudget, Console.WriteLine);
    if (reduced is not ShrinkFailingScenario.Result done)
    {
        // A passing scenario is refused rather than minimised. Delta debugging needs a failure to
        // preserve, and reducing until the answer changes would produce a file that fails for a
        // reason the original never had.
        Console.WriteLine($"shrink {Path.GetFileName(shrinkPath)}: every block reaches its goal, so there is no failure to reduce");
        EmitLedgerRows.Skipped("nav-replay", "corpus", $"reduce {Path.GetFileName(shrinkPath)} to its smallest failing window",
            "no block in the file fails, and a passing scenario has no signature to preserve");
        return 0;
    }
    Console.WriteLine($"shrink {Path.GetFileName(shrinkPath)} -> {done.Path}");
    Console.WriteLine($"     signature:  {done.Signature}");
    Console.WriteLine($"     window:     {done.BeforeWidth}x{done.BeforeHeight} -> {done.AfterWidth}x{done.AfterHeight}");
    Console.WriteLine($"     tiles:      {done.BeforeTiles} -> {done.AfterTiles}");
    Console.WriteLine($"     trail:      {done.BeforeTrail} -> {done.AfterTrail}");
    Console.WriteLine($"     transforms: {done.Transforms}");
    Console.WriteLine($"     oracle:     {done.OracleCalls} calls{(done.BudgetSpent ? $", budget of {shrinkBudget} spent — the result is 1-minimal for the reductions it reached and no further" : "")}");
    EmitLedgerRows.Measure("nav-replay", "corpus", $"shrunk-tiles-{Path.GetFileNameWithoutExtension(shrinkPath)}", done.AfterTiles, "tiles", "down",
        mode: "unbounded-allowances", message: $"reduced from {done.BeforeTiles} keeping the signature {done.Signature} in {done.OracleCalls} oracle calls");
    return 0;
}

if (files.Count == 0)
{
    Console.Error.WriteLine("usage: NavReplay <scenario.txt | folder> ...");
    return 2;
}
files.Sort(StringComparer.Ordinal);

foreach (string file in files)
{
    foreach ((int index, List<string> block) in ReplayOneBlock.Blocks(File.ReadAllLines(file)))
    {
        string name = $"{Path.GetFileName(file)}#{index}";

        // --edges, --audit-jumps and the two traces are focused instruments that answer about one
        // tile or one edge rather than about the block's route, so each parses the world itself and
        // leaves before the plan runs.
        if (edgesFrom != null || auditJumps || compareJump != null || traceJump || traceWalkFrom != null)
        {
            var probeWorld = TextTileWorld.Parse(block, out string probeHeader, out _);
            NavGrid.World = probeWorld;
            RememberExecutedRoutes.World.Clear();
            AStar.InvalidateEdges();
            AStar.AllowLava = false;
            AStar.Avoid.Clear();

            if (edgesFrom is Point node)
            {
                BodyPhysics.Pose? here = NavGrid.StandAt(node.X, node.Y, false);
                Console.WriteLine($"edges from {ReplayOneBlock.Fmt(node)} in {name}: {(here is BodyPhysics.Pose hp ? $"pose left {hp.Left} bottom {hp.Bottom}" : "no pose (not a node)")}");
                foreach (Traversal traversal in Traversal.Planning)
                    foreach (NavEdge edge in traversal.Candidates(NavNode.At(node), here, false))
                        Console.WriteLine($"   {edge.Step.Kind,-11} -> {ReplayOneBlock.Fmt(edge.Step.Tile)}  cost {edge.Move:F2} fall {edge.Fall} ticks {edge.Step.Ticks} scale {edge.Step.JumpScale:F2} launchVx {edge.Step.LaunchVx:F2} back {edge.Step.RunUpBack:F0} steerX {edge.Step.SteerX:F1}");
                skipped++;
                continue;
            }

            if (auditJumps)
            {
                var offenders = new List<string>();
                CompareJumpPaths.Audit audit = CompareJumpPaths.AuditWindow(probeWorld, probeWorld.OriginX, probeWorld.OriginY, probeWorld.Width, probeWorld.Height, offenders.Add);
                auditTotal += audit;
                Console.WriteLine($"audit {name}: {audit.Proven} jumps proven, {audit.Flown} flown to their tile, {audit.SatisfiedAtEntry} satisfied at entry, {audit.CompletedBySlack} closed by the arrival slack elsewhere, {audit.Unflyable} unflyable ({audit.FlownShare * 100:F1}% flown)");
                foreach (string line in offenders)
                    Console.WriteLine($"   unflyable {line}");
                skipped++;
                continue;
            }

            // --compare-jump A,B,C,D: the proof path and the execution path over the one jump edge
            // between those tiles, aligned on their take-off ticks. A proven edge the execution path
            // cannot fly is the defect this reads; the block's npcbox supplies the live entry when the
            // dump carries one, so a recorded failure is replayed from the body the game actually had.
            if (compareJump is (Point cfrom, Point cto))
            {
                // An explicit entry outranks the block's own body, and it is used wherever the body
                // stands rather than only on the edge's take-off tile. A rejection recorded in play
                // names the state the macro proof refused, and that state is usually a tile short of
                // the take-off with the body still moving — which is precisely the case worth
                // replaying and precisely the one the header pose could never express.
                BodyState? liveEntry = entryState
                    ?? (ReplayOneBlock.HeaderPose(probeHeader) is BodyPhysics.Pose lp
                        ? new BodyState(lp.Left, lp.Bottom, entryVx, 0f, true, Capabilities: MovementCapabilities.Basic)
                        : null);
                switch (CompareJumpPaths.Compare(probeWorld, name, cfrom, cto, liveEntry, compareVerbose, entryState != null))
                {
                    case true: passed++; break;
                    case false: failed++; break;
                    case null: skipped++; break;
                }
                continue;
            }

            // --trace-jump: the simulated jump from S to G, tick by tick, from standing and from a
            // run-up, so a jump the planner refuses can be read as the arc the body would fly.
            if (traceJump)
            {
                Point? traceStart = probeWorld.Markers.TryGetValue('S', out Point ts) ? ts
                    : probeWorld.Markers.TryGetValue('N', out Point tn) ? tn : ReplayOneBlock.HeaderPoint(probeHeader, "start");
                Point? traceGoal = probeWorld.Markers.TryGetValue('G', out Point tg) ? tg : ReplayOneBlock.HeaderPoint(probeHeader, "goal");
                if (traceStart == null || traceGoal == null)
                {
                    Console.WriteLine($"SKIP {name}: no start or goal ({probeHeader})");
                    skipped++;
                    continue;
                }
                // A trace passes when either start lands on the goal tile, fails when neither does,
                // and skips when there is no pose to jump from, so the exit code means the same thing
                // it means for a replay.
                switch (TraceJump(traceStart.Value, traceGoal.Value))
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
            TraceWalk(traceWalkFrom!.Value, traceWalkDir);
            skipped++;
            continue;
        }

        ReplayOneBlock.Outcome result = ReplayOneBlock.Evaluate(block, name, follow, followTickWindow);
        if (result.Skipped)
        {
            Console.WriteLine($"SKIP {name}: {result.Skip}");
            skipped++;
            continue;
        }
        if (result.Passed) passed++; else if (result.Sealed) sealedCount++; else failed++;

        Console.WriteLine($"{result.Class} {name}: {result.Header}");
        Console.WriteLine($"     recorded goal: start {ReplayOneBlock.Fmt(result.Start)} -> {(result.From == null ? "no standable tile" : ReplayOneBlock.Fmt(result.From.Value))}, goal {ReplayOneBlock.Fmt(result.Goal)}, {ReplayOneBlock.Describe(result.Main)}");
        if (result.Player is Point pl2 && pl2 != result.Goal)
            Console.WriteLine($"     player:        {(result.ToPlayer.Pass ? "reached" : "NOT reached")} at {ReplayOneBlock.Fmt(pl2)}, {ReplayOneBlock.Describe(result.ToPlayer)}");
        if (result.From != null)
        {
            Console.WriteLine($"     reach flood:   {result.Region.Count} tiles, {(result.RegionComplete ? "complete" : "budget spent")}, goal {result.GoalIn}{result.PlayerIn}{result.HomeIn}{result.Pocket}");
            foreach (string trail in result.TrailLines)
                Console.WriteLine($"     trail:         {trail}");
        }
        Console.WriteLine(ReplayOneBlock.Draw(result.World, result.Main.Path, result.Start, result.Goal, result.Passed ? null : result.Main.Closed));

        if (result.Follow is FollowOutcome outcome)
        {
            if (outcome == FollowOutcome.Walked) followPassed++; else if (outcome == FollowOutcome.PartialEnd) followPartial++; else followFailed++;
            Console.WriteLine($"     follow:        {outcome switch { FollowOutcome.Walked => "PASS", FollowOutcome.PartialEnd => "PARTIAL", _ => "FAIL" }}, {result.FollowVerdict}");
            foreach (string line in result.FollowEdges)
                Console.WriteLine($"       {line}");
        }

        // --churn: the cache's invalidation box, tested the only way it can be. A corpus replay
        // never changes a tile, so "the same verdicts with and without the cache" proves nothing
        // about what a kill drops. Here every tile the found path stands on or steps through is
        // broken in turn, the planner is told the way the game tells it, and the plan served
        // from the warm cache must equal the plan from an empty one; a difference is an edge the
        // box kept that the broken tile should have taken with it.
        if (churn && result.From is Point cf && result.Main.Path is NavPath found)
        {
            foreach (Point tile in ChurnTiles(found))
            {
                if (!result.World.InWorld(tile.X, tile.Y))
                    continue;
                char was = result.World.Glyph(tile.X, tile.Y);
                result.World.Set(tile.X, tile.Y, '.');
                AStar.TileChanged(tile.X, tile.Y);
                string warm = Signature(cf, result.Goal);
                AStar.InvalidateEdges();
                string cold = Signature(cf, result.Goal);
                result.World.Set(tile.X, tile.Y, was);
                AStar.TileChanged(tile.X, tile.Y);
                churnTiles++;
                if (warm != cold)
                {
                    churnWrong++;
                    Console.WriteLine($"     CHURN {name}: breaking {ReplayOneBlock.Fmt(tile)} ('{was}') left a stale edge: warm {warm} vs cold {cold}");
                }
            }
        }

        // --mirror: the same block reflected left to right and run again, on a world rebuilt from
        // scratch. The row is the relation rather than the reflection's own verdict: a SEALED block
        // that is sealed both ways is the corpus behaving, and a block that walks one way and not
        // the other is the finding, whichever way round it is.
        //
        // It runs last in the block, after churn, because evaluating the reflection repoints the
        // grid and the edge cache at the mirrored world: anything below it that still meant the
        // captured world would silently be asking about the reflection.
        if (mirror)
        {
            ReplayOneBlock.Outcome reflected = ReplayOneBlock.Evaluate(MirrorScenarioWorlds.Mirror(block), name + " mirrored", follow, (0, -1));
            bool sameClass = result.Class == reflected.Class;
            bool sameFollow = result.Follow == reflected.Follow;
            bool agrees = sameClass && sameFollow;
            string detail = $"{result.Class}{(result.Follow is { } fo ? $"/{fo}" : "")} as captured against {reflected.Class}{(reflected.Follow is { } rf ? $"/{rf}" : "")} reflected"
                + (result.FirstRefusedTrail is { } t ? $"; first refused as captured {t}" : "")
                + (reflected.FirstRefusedTrail is { } rt ? $"; first refused reflected {rt}" : "");
            Console.WriteLine($"     mirror:        {(agrees ? "AGREES" : "DISAGREES")}, {detail}");
            // A disagreement prints both sides' root and both floods, because the relation says only
            // that the two answers differ and the next question is always which half differs. The
            // root separates two findings with different owners: `Ground` ends in `NearestStandable`,
            // whose neighbourhood order is not itself mirror-symmetric, so a reflected start can
            // settle on a tile that is not the reflection of the original's — and then the asymmetry
            // is in how a root is chosen rather than in what the flood did from it. With the roots
            // agreeing, the region sizes are the finding, and a reflection reaching two orders of
            // magnitude more tiles from the mirror image of the same root is a direction-dependent
            // traversal rule rather than anything about the terrain.
            if (!agrees)
            {
                Console.WriteLine($"       as captured  root {(result.From is Point of ? ReplayOneBlock.Fmt(of) : "none")}, {result.Region.Count} tiles, {(result.RegionComplete ? "complete" : "budget spent")}, goal {result.GoalIn}{result.PlayerIn}{result.Pocket}");
                Console.WriteLine($"       reflected    root {(reflected.From is Point rfr ? ReplayOneBlock.Fmt(rfr) : "none")}, {reflected.Region.Count} tiles, {(reflected.RegionComplete ? "complete" : "budget spent")}, goal {reflected.GoalIn}{reflected.PlayerIn}{reflected.Pocket}");
                Console.WriteLine($"       roots agree:  {(result.From is Point a && reflected.From is Point b && MirrorScenarioWorlds.MirrorTile(a.X, result.World.OriginX, result.World.Width) == b.X && a.Y == b.Y ? "yes, the reflected run is rooted in the reflection of the original's tile" : "NO — the two runs are rooted in tiles that are not reflections of each other, so the asymmetry is in how a root is chosen rather than in the verdict")}");
                // The reflected block is written out so the disagreement is reachable by the focused
                // instruments. Without it the reflection exists only inside this loop, and the next
                // reader can see that the two floods differ but cannot ask --edges which edges
                // either one was offered. It goes to the temp directory rather than beside the
                // original, because a reflected block is a diagnostic and a file in Tools/Scenarios
                // is a fixture the whole corpus then replays.
                string reflectedPath = Path.Combine(Path.GetTempPath(),
                    // The block index is part of the identity and is kept: a file carrying only the
                    // scenario's name would be overwritten by the next disagreeing block in it.
                    $"mirrored-{name.Replace('#', '-').Replace(Path.DirectorySeparatorChar, '-')}.txt");
                File.WriteAllLines(reflectedPath, MirrorScenarioWorlds.Mirror(block));
                Console.WriteLine($"       reflected block written to {reflectedPath} — replay it, or point --edges at its root, to see which edges each flood was offered");
            }
            if (agrees) mirrorAgreed++; else mirrorDisagreed++;
            string @case = $"{name} answers the same reflected left to right";
            const string Killer = "reflecting the tiles without flipping a slope glyph, or about the wrong column";
            if (agrees)
                EmitLedgerRows.Pass("nav-replay", "corpus-mirror", @case, detail, mode: "unbounded-allowances", killedBy: Killer);
            else
                // Tagged as a known limitation because it is one: the relation found a real
                // asymmetry on the day it was built, and the tag is what a baseline exemption would
                // key on if the ledger grows one. Until it does, a disagreement is an ordinary red
                // and every run carrying it is disqualified as a baseline — which is the honest
                // cost of the corpus having a defect, and is recorded here rather than hidden by
                // grading the relation as a measure nobody can fail.
                EmitLedgerRows.Fail("nav-replay", "corpus-mirror", @case, detail, mode: "unbounded-allowances",
                    tags: new[] { "known-limitation" }, killedBy: Killer);
        }
    }
}
Console.WriteLine($"{passed}/{passed + failed} passed, {sealedCount} model-closed (not proof of physical impossibility), {skipped} skipped, {missing} missing inputs, planner {Timing.PlannerMs:F0} ms in total"
    + (churn ? $"; churn: {churnTiles} tiles broken, {churnWrong} stale plans" : "")
    + (follow ? $"; follow: {followPassed} walked, {followPartial} to a partial plan's end, {followFailed} not" : "")
    + (mirror ? $"; mirror: {mirrorAgreed} blocks answer the same both ways, {mirrorDisagreed} do not" : "")
    + (auditJumps ? $"; jump proofs: {auditTotal.Proven} proven, {auditTotal.Flown} flown to their tile, {auditTotal.SatisfiedAtEntry} satisfied at entry, {auditTotal.CompletedBySlack} closed by the arrival slack elsewhere, {auditTotal.Unflyable} unflyable ({auditTotal.FlownShare * 100:F1}% flown)" : ""));
// The corpus as rows. A pass count and a sealed count are two different facts and the ledger keeps
// them apart: model-closed is a search that proved nothing, never a proof of impossibility, so it
// is its own verdict rather than a failure or a pass. The counts go in as measures beside them,
// because "how many scenarios passed" is the number that moves when the planner changes and one
// exit code cannot carry it.
//
// In mirror mode the aggregate verdict row is deliberately absent. The mirror run's claim is the
// relation — the same answer both ways — and it already has a row per block; emitting the corpus's
// own long-standing red beside it would put a permanent `fail` into every run that asks for the
// mirror, and a run carrying a fail can never be a baseline, which would cost the ledger its whole
// memory to say something the plain run already says.
if (passed + failed + sealedCount > 0 && !mirror)
{
    EmitLedgerRows.Measure("nav-replay", "corpus", "scenarios-passed", passed, "scenarios", "up",
        message: $"out of {passed + failed} executed, with {sealedCount} model-closed and {skipped} skipped");
    EmitLedgerRows.Measure("nav-replay", "corpus", "scenarios-model-closed", sealedCount, "scenarios", "down",
        message: "searches that closed against the model without proving the move impossible");
    if (failed > 0)
        EmitLedgerRows.Fail("nav-replay", "corpus", "every corpus scenario reaches its recorded goal", $"{failed} scenario(s) did not reach their goal");
    else if (passed > 0)
        EmitLedgerRows.Pass("nav-replay", "corpus", "every corpus scenario reaches its recorded goal", $"{passed} scenario(s)");
}
if (follow && !mirror)
    EmitLedgerRows.Measure("nav-replay", "corpus", "follow-walked", followPassed, "scenarios", "up",
        message: $"{followPartial} reached a budget-cut plan's end and {followFailed} did not walk at all");
if (mirror)
    EmitLedgerRows.Measure("nav-replay", "corpus-mirror", "mirror-disagreements", mirrorDisagreed, "scenarios", "down",
        mode: "unbounded-allowances",
        message: $"blocks whose verdict changed when the world was reflected, out of {mirrorAgreed + mirrorDisagreed}");
if (auditJumps)
{
    // The one property the audit can genuinely fail, as a number rather than a verdict, because a
    // red on a counted residue is a triage list and a number that moves — not a stop.
    EmitLedgerRows.Measure("nav-replay", "corpus", "jump-proofs-unflyable", auditTotal.Unflyable, "edges", "down",
        message: $"jumps the planner proved that the performer refuses, out of {auditTotal.Proven} proven and {auditTotal.Flown} flown");
    EmitLedgerRows.Measure("nav-replay", "corpus", "jump-proofs-flown-share", auditTotal.FlownShare * 100, "%", "up",
        message: "share of proven jump edges the performer actually flew to their tile");
}

// The audit carries its own verdict, because it runs no plan cases at all: every scenario is
// skipped and the pass count is zero by construction, so the ordinary condition below reports a
// failure for doing exactly what the mode is for. What the audit can genuinely fail is its one
// property — a jump the planner proved and the performer refuses — which is the quantity it
// exists to count, and it is red today with 152 of those outstanding across the corpus.
if (auditJumps)
    return auditTotal.Unflyable == 0 && missing == 0 ? 0 : 1;
// The mirror run exits on its own relation for the same reason: the corpus's known incomplete and
// model-closed cases are its inheritance, and a mode asking "does it answer the same both ways"
// would otherwise report every one of them as its own failure.
if (mirror)
    return mirrorDisagreed == 0 && missing == 0 ? 0 : 1;
return failed == 0 && skipped == 0 && missing == 0 && passed > 0 && churnWrong == 0 && followFailed == 0 ? 0 : 1;

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
        sb.Append(step.Kind.ToString()[0]).Append(ReplayOneBlock.Fmt(step.Tile)).Append('<').Append(ReplayOneBlock.Fmt(step.From))
          .Append('/').Append(step.JumpScale.ToString("0.##")).Append('/').Append(step.LaunchVx.ToString("0.##")).Append('/').Append(step.RunUpBack.ToString("0.#")).Append('/').Append(step.SteerX.ToString("0.#"))
          .Append('/').Append(step.Ticks).Append(step.FromRest ? "/rest" : "/run")
          .Append('/').Append(step.Mobility.AirJumpsLeft).Append(step.Mobility.Latched ? "L" : "-").Append(step.Mobility.DashCooldown)
          .Append(' ');
    return sb.ToString().TrimEnd();
}

static bool? TraceJump(Point start, Point goal)
{
    if (NavGrid.StandAt(start.X, start.Y, false) is not BodyPhysics.Pose from)
    {
        Console.WriteLine($"trace-jump: no pose at {ReplayOneBlock.Fmt(start)}");
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
        Console.WriteLine($"trace-jump {ReplayOneBlock.Fmt(start)} -> {ReplayOneBlock.Fmt(goal)} rise {rise} scale {scale:F2} startVx {startVx:F2}: pose left {from.Left} bottom {from.Bottom}");
        BodyPhysics.Pose? landing = BodyPhysics.SimulateJump(NavGrid.World, from, scale, startVx, goal.X, goal.Y, 120, out int ticks, tick =>
            Console.WriteLine($"   t{tick.Tick,3} left {tick.Left,7:F1} bottom {tick.Bottom,7:F1} vx {tick.Vx,5:F2} vy {tick.Vy,5:F2} feet {tick.FeetColumn},{tick.FeetRow}"));
        if (landing is BodyPhysics.Pose l)
        {
            var tile = new Point((int)Math.Floor(l.CentreX / 16f), BodyPhysics.FeetRow(l.Bottom));
            landedOnGoal |= tile == goal;
            Console.WriteLine($"   landed after {ticks} ticks at left {l.Left:F1} bottom {l.Bottom:F1}, feet tile {ReplayOneBlock.Fmt(tile)}{(tile == goal ? " (the goal)" : "")}");
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
        Console.WriteLine($"trace-walk: no pose at {ReplayOneBlock.Fmt(start)}");
        return;
    }
    Console.WriteLine($"trace-walk from {ReplayOneBlock.Fmt(start)} dir {dir}: pose left {from.Left} bottom {from.Bottom}");
    BodyState state = BodyState.Standing(from);
    var controls = new Controls(dir * BodyPhysics.WalkSpeed);
    for (int tick = 1; tick <= 60; tick++)
    {
        state = BodyMotion.Step(NavGrid.World, state, controls);
        Console.WriteLine($"   t{tick,3} left {state.Left,7:F1} bottom {state.Bottom,7:F1} vx {state.Vx,5:F2} vy {state.Vy,5:F2} feet {ReplayOneBlock.Fmt(state.FeetTile)} {(state.OnGround ? "ground" : "air")}{(state.CollideX ? " wall" : "")}{(state.Stuck ? " STUCK" : "")}");
        if (state.Stuck || state.CollideX)
        {
            Console.WriteLine($"   ended: {(state.Stuck ? "stuck in a shape" : "met a shape sideways")} after {tick} ticks");
            return;
        }
        if (state.OnGround && dir * (state.FeetTile.X - (start.X + dir)) >= 0)
        {
            bool node = NavGrid.StandAt(state.FeetTile.X, state.FeetTile.Y, false) != null;
            Console.WriteLine($"   stands in the next column after {tick} ticks at feet tile {ReplayOneBlock.Fmt(state.FeetTile)}{(node ? "" : " (not a node)")}");
            return;
        }
    }
    Console.WriteLine("   never stood in the next column within 60 ticks");
}
