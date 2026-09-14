extern alias live;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using Reach = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;

/// <summary>
/// What a world run claims, as rows.
///
/// Three things are asked and they are deliberately in this order, because each one is worthless
/// without the one before it. Determinism first: if one route run twice produces two different
/// runs, every other row of that run is a coin toss and the plan says so outright. Then the
/// recorded comparison, which is a calibration rather than a pass — it says how far this build
/// walks before it stops being the build that made the recording. Then the checkpoints, which are
/// the only row that tries to say whether the companion could go where the player went.
///
/// Nothing here grades a measure. A measure carries a number and its direction and the scoreboard
/// judges it against a baseline; a threshold invented in this file would collapse "ever green" into
/// "green now" and could not tell a flake from a regression, which is the thing the ledger exists
/// to keep separate.
/// </summary>
internal static class ScoreTheRun
{
    public const string Instrument = "world-run";

    /// <summary>
    /// How far apart two tracks may be before they count as having diverged, in pixels.
    ///
    /// One body width, because that is the smallest separation that means anything physically: two
    /// positions closer than a body are the same body in the same place, and the first tick at
    /// which they are further apart than that is the first tick a watcher would see two companions
    /// rather than one.
    /// </summary>
    private const float ABodyWidth = 20f;

    /// <summary>
    /// Two passes of one route must produce one run.
    ///
    /// The comparison is on the trace rather than on position alone, because two runs can stand in
    /// the same place for a hundred ticks while disagreeing about what they are doing, and the
    /// disagreement is what would make every later row unrepeatable. The row reports where they
    /// first parted and by how much, so a red is a lead rather than a verdict.
    /// </summary>
    public static int Determinism(string suite, RunTheWorld.Outcome first, RunTheWorld.Outcome second)
    {
        if (first.TraceHash == second.TraceHash)
        {
            EmitLedgerRows.Pass(Instrument, suite, "one route run twice produces one run",
                $"{first.Ticks} ticks, trace hash {first.TraceHash} both times",
                mode: "unbounded-allowances",
                killedBy: "leaving the light scanner's per-process random seed unpinned, or skipping the route-memory and terrain-revision reset between passes");
            return 0;
        }

        int tick = FirstDisagreement(first.Trace, second.Trace);
        float pixels = tick < Math.Min(first.CompanionFeet.Count, second.CompanionFeet.Count)
            ? Vector2.Distance(first.CompanionFeet[tick], second.CompanionFeet[tick]) : float.NaN;
        EmitLedgerRows.Fail(Instrument, suite, "one route run twice produces one run",
            $"the two passes first disagree at step {tick} of {first.Ticks}, {pixels:0.0} px apart; "
            + $"pass one traced {Line(first.Trace, tick)} and pass two traced {Line(second.Trace, tick)}; "
            + "every other row of this run is unconfirmed",
            mode: "unbounded-allowances");
        return 1;
    }

    /// <summary>
    /// How far this build's companion walks before it stops being the companion that made the
    /// recording.
    ///
    /// This is a measure and never a verdict, and the reason is in the numbers rather than in
    /// caution. The source has moved a long way since either capture was recorded, so the run is
    /// *expected* to diverge; a pass line here would be asserting that this build should behave
    /// like an older one. What the number is good for is calibration in the plan's sense: it says
    /// how much of a recorded route this instrument can reproduce at all, and a build that diverges
    /// at the first tick is telling you the instrument is wrong rather than the build.
    ///
    /// The world is a second reason the number is soft. A saved world is the state at the end of
    /// the session, so terrain the player mined after the capture's last row — and every torch
    /// placed since — is present here and was not present then.
    /// </summary>
    public static void RecordedTrackDivergence(string suite, ReadRecordedRoute.Route route, RunTheWorld.Outcome run, string worldNote)
    {
        int steps = Math.Min(route.Count, run.CompanionFeet.Count);
        int firstApart = -1;
        double total = 0, recordedPath = 0, runPath = 0;
        float worst = 0;
        for (int i = 0; i < steps; i++)
        {
            // How far each body actually travelled, because the divergence figure is meaningless
            // without it and reads as its opposite. A window in which the recorded companion never
            // moved — and this capture has long stretches of exactly that, which is the defect the
            // play was reported for — produces a divergence near zero for a run that also stood
            // still, and that number would otherwise be read as a faithful reproduction. The two
            // path lengths are what tell a reader whether anything was reproduced at all.
            if (i > 0)
            {
                recordedPath += Vector2.Distance(route[i].CompanionLeftBottom, route[i - 1].CompanionLeftBottom);
                runPath += Vector2.Distance(run.CompanionFeet[i], run.CompanionFeet[i - 1]);
            }
            // The recorded column is the body's left edge; the run's track is its centre-bottom, so
            // the recorded pose is moved to the same reference before the two are subtracted.
            var recorded = new Vector2(route[i].CompanionLeftBottom.X + ABodyWidth / 2f, route[i].CompanionLeftBottom.Y);
            float apart = Vector2.Distance(recorded, run.CompanionFeet[i]);
            total += apart;
            worst = Math.Max(worst, apart);
            if (firstApart < 0 && apart > ABodyWidth) firstApart = route[i].Tick;
        }

        string moved = recordedPath < ABodyWidth
            ? $"THE RECORDED BODY BARELY MOVED ({recordedPath:0} px over {steps} ticks), so a small divergence here is not a reproduction of anything"
            : $"the recorded body travelled {recordedPath:0} px and this run travelled {runPath:0} px";
        string note = $"{route.Capture} recorded at {route.SourceRevision[..Math.Min(7, route.SourceRevision.Length)]}, schema {route.Schema}; "
            + $"{steps} ticks from {route[0].Tick}; world {worldNote}; {moved}; "
            + $"first parted by more than a body at tick {(firstApart < 0 ? "never" : firstApart.ToString(CultureInfo.InvariantCulture))}, worst {worst:0} px";

        EmitLedgerRows.Measure(Instrument, suite, "mean distance from the recorded companion track",
            steps == 0 ? 0 : total / steps, "px", direction: null, mode: "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "ticks reproduced before the track parts by a body",
            firstApart < 0 ? steps : firstApart - route[0].Tick, "ticks", direction: "up",
            mode: "unbounded-allowances", message: note);
        // The denominator of the row above, emitted as its own row rather than only as prose,
        // because a comparison whose baseline is zero movement has to be visible on the scoreboard
        // and not only to someone who read the message.
        EmitLedgerRows.Measure(Instrument, suite, "distance the recorded companion travelled in this window",
            recordedPath, "px", direction: null, mode: "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "distance this run's companion travelled in this window",
            runPath, "px", direction: null, mode: "unbounded-allowances", message: note);
    }

    /// <summary>
    /// Whether the companion could go where the player went.
    ///
    /// A checkpoint is a tile the player's own track stood on, taken at a cadence rather than every
    /// tick, because consecutive ticks are the same tile and would weight a place the player
    /// loitered in as heavily as a whole journey.
    ///
    /// The filter is the row's reason for existing, and it is the plan's hardest rule: a place the
    /// player reached by wings is not evidence that the companion failed. The kits are read from
    /// the capture's own header and never inferred from the track, so a capture written before that
    /// header existed produces a skip rather than a guess — which is why both of the captures this
    /// instrument was first run against skip here. The counts are still emitted as measures,
    /// because a count is not a verdict and a reader is better off with the numbers plus an honest
    /// "this was not judged" than with nothing.
    /// </summary>
    public static void Checkpoints(string suite, ReadRecordedRoute.Route route, RunTheWorld.Outcome run, int cadence)
    {
        int arrived = 0, missed = 0, plannerSaidNo = 0, plannerUnfinished = 0, total = 0;
        int steps = Math.Min(route.Count, run.CompanionFeet.Count);
        var visited = new HashSet<Point>();

        for (int i = 0; i < steps; i += cadence)
        {
            Point tile = route[i].PlayerFeet.ToTileCoordinates();
            if (!visited.Add(tile)) continue;
            total++;

            // Arrived at any point in the run, not only on the tick the player stood there: the
            // companion following a player is behind them by design, and scoring it only at the
            // moment of passing would count ordinary following as a failure to arrive.
            bool reached = false;
            for (int j = 0; j < steps && !reached; j++)
                reached = Vector2.Distance(run.CompanionFeet[j], route[i].PlayerFeet) <= ABodyWidth * 2;

            if (reached) { arrived++; continue; }
            missed++;
            if (run.PlannerClaim[i] == Reach.Unreachable) plannerSaidNo++;
            else if (run.PlannerClaim[i] == Reach.NotYet) plannerUnfinished++;
        }

        string note = $"{total} checkpoints every {cadence} ticks along {route.Capture}";
        EmitLedgerRows.Measure(Instrument, suite, "checkpoints the body reached", arrived, "checkpoints", "up", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "checkpoints the body never reached", missed, "checkpoints", "down", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "unreached checkpoints the planner called unreachable", plannerSaidNo, "checkpoints", "down", "unbounded-allowances",
            message: note + "; a missing edge unless the player's kit explains it");
        EmitLedgerRows.Measure(Instrument, suite, "unreached checkpoints whose flood never finished", plannerUnfinished, "checkpoints", "down", "unbounded-allowances",
            message: note + "; the search ran out of region rather than proving anything");

        if (!route.Kits.Known)
        {
            EmitLedgerRows.Skipped(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"{route.Capture} carries no capabilities header, so the player's kit is unknown; "
                + "the plan refuses inferring abilities from the track, and without the kit an unreached checkpoint "
                + "cannot be told apart from one the player flew to. The counts above stand; the verdict does not.",
                tags: new[] { "requires-capabilities-header" });
            return;
        }

        if (route.Kits.PlayerCanLeaveTheGround)
        {
            EmitLedgerRows.Skipped(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"the player carried {route.Kits.Raw}; every unreached checkpoint is filtered because a kit that leaves "
                + "the ground can reach places the companion's declared kit cannot express",
                tags: new[] { "outside-the-envelope" });
            return;
        }

        if (missed == 0)
            EmitLedgerRows.Pass(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"the player walked ({route.Kits.Raw}) and the body reached all {total} checkpoints",
                mode: "unbounded-allowances",
                killedBy: "counting a checkpoint as reached on the tick the player stood there rather than at any point in the run");
        else
            EmitLedgerRows.Fail(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"the player reached {total} checkpoints on foot ({route.Kits.Raw}) and the body missed {missed} of them; "
                + $"the planner called {plannerSaidNo} unreachable and had not finished flooding {plannerUnfinished}",
                mode: "unbounded-allowances");
    }

    private static int FirstDisagreement(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        int shared = Math.Min(first.Count, second.Count);
        for (int i = 0; i < shared; i++)
            if (first[i] != second[i]) return i;
        return shared;
    }

    private static string Line(IReadOnlyList<string> trace, int index)
        => index < trace.Count ? trace[index] : "<no such step>";
}
