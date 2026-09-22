using System.Globalization;
using AICompanion.Tools.Ledger;

/// <summary>
/// What the morning of 22 September 2026 looked like, as rows a fix can turn green.
///
/// The play it reproduces is one minute in world Lilalio in which the companion, with five to seven
/// hostiles within reach and three drops on the floor, did nothing at all for the last 524 ticks.
/// Every decision of that stretch read the same three things at once and the contradiction between
/// them is the whole finding: the domain census admitted three combat and four collection
/// opportunities as <em>usable</em>, the order search refused all twenty-eight orders built from
/// them — twelve <c>target-capture-missing</c> and sixteen <c>assistance-target-unresolved</c>,
/// which are one predicate in source — and the only order left to price was the empty one, which is
/// companionship. So the companion kept company beside a fight it had already planned.
///
/// Three rows here are verdicts and the rest are measures, and which is which is a judgement about
/// what can be wrong rather than about what is easy to assert:
///
/// <list type="number">
/// <item><b>A refusal that contradicts its own census is always a defect.</b> An order refused for a
/// target the same frozen observation admitted as usable is not a preference the brain expressed;
/// it is two readers of one store disagreeing. There is no scene in which it is correct, so it is a
/// pass line and not a threshold.</item>
/// <item><b>Doing nothing for three seconds while work is admitted is always a defect.</b> The bound
/// is the project's own: <c>ScoreTheRun</c> already gives combat 180 ticks to take the body once a
/// hostile stands beside the route, so a course that binds no step at all inside the same window is
/// held to the same three seconds rather than to a number invented here.</item>
/// <item><b>Silence after the last kill, with hostiles still standing, is always a defect.</b> The
/// window opens at the tick the recording last credited the companion a kill and the hands must
/// come back to work somewhere inside it.</item>
/// </list>
///
/// Everything else — the empty-course share, the refusal tallies, the decision cost, the collector
/// — is a measure, because each of them has a legitimate non-zero value and a pass line on any of
/// them would be a number nobody declared becoming a verdict. The cost measures are taken under the
/// game's own clock and say so in their mode, because the brain a player met was one being cut by
/// its deadline and a figure taken with the allowances lifted is not a frame cost.
/// </summary>
internal static class GradeThePlayMeasures
{
    /// <summary>
    /// How long the course may bind no step at all while some domain admits usable work, in ticks.
    ///
    /// Three seconds, and deliberately the same three seconds <see cref="ScoreTheRun"/> already
    /// allows combat to take the body in. The two are the same question asked from opposite sides —
    /// there is work in front of the companion and it is not doing it — so a second number would be
    /// two pass lines drifting apart about one behaviour.
    /// </summary>
    private const int StepWithinTicks = 180;

    /// <summary>The two refusal reasons that resolve to one predicate in source: the target fact's evidence is not Observed.</summary>
    private static readonly string[] CensusContradictingRefusals =
        { "target-capture-missing", "assistance-target-unresolved" };

    public static int Grade(string suite, ReadRecordedRoute.Route route, RunTheWorld.Outcome run,
        StageRecordedActors stage, ReadRecordedActors.Cast cast, string preferences)
    {
        IReadOnlyList<RunTheWorld.PlayTick> play = run.Play;
        string staging = stage.Describe();
        string scene = $"{route.Capture} replayed whole under the game's own millisecond allowances, "
            + $"{play.Count} ticks from {route[0].Tick}; {preferences}; {staging}";

        // A scene with nothing in it passes every verdict below, and it passes them for the one
        // reason that must never read as health: there was nothing to refuse and nothing to go and
        // do. That is not hypothetical — a capture whose events sidecar is missing produces exactly
        // this run, and the sidecar is the half of the input that is easiest to lose, because
        // `Telemetry/` is gitignored and a capture is often copied as one file. So the verdicts skip
        // and say which half of the scene was empty, and the measures below still run, because a
        // count is not a verdict.
        string[] verdicts =
        {
            "no order is refused for a target its own observation admitted",
            "work the census admits becomes a bound step within three seconds",
            "the hands fire again after the last recorded kill while hostiles stand",
        };
        if (play.Count == 0 || (cast.Hostiles.Count == 0 && cast.Drops.Count == 0))
        {
            string why = play.Count == 0
                ? "the run produced no ticks"
                : $"{Path.GetFileName(cast.EventsPath)} named no hostile and no drop, so this run replayed the player's track through an empty world "
                    + "and every verdict below would pass for want of anything to do rather than because the brain did it";
            foreach (string name in verdicts)
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name, why);
            if (play.Count > 0) Measures(suite, play, cast, scene);
            return 0;
        }

        int failures = 0;
        failures += NoRefusalContradictsItsOwnCensus(suite, play, scene);
        failures += WorkAdmittedIsWorkBegun(suite, play, scene);
        failures += TheHandsComeBackAfterTheLastKill(suite, play, cast, scene);
        Measures(suite, play, cast, scene);
        return failures;
    }

    /// <summary>
    /// The contradiction itself: an order refused because a target is not Observed, on a tick whose
    /// own census called that domain's opportunities usable.
    ///
    /// The conjunction is what makes this a verdict rather than a measure. A refusal on its own is
    /// ordinary — a target genuinely out of reach is refused every tick and should be. A census
    /// admitting usable work on its own is ordinary too. The two together, inside one frozen
    /// observation, say that the census and the binder read the same store and got different
    /// answers, and there is no world in which that is the right behaviour.
    /// </summary>
    private static int NoRefusalContradictsItsOwnCensus(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, string scene)
    {
        var offending = new List<RunTheWorld.PlayTick>();
        var byReason = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RunTheWorld.PlayTick tick in play)
        {
            if (tick.UsableAdmitted <= 0) continue;
            bool contradicts = false;
            foreach (string reason in CensusContradictingRefusals)
                if (tick.Refusals.TryGetValue(reason, out int count) && count > 0)
                {
                    byReason[reason] = byReason.GetValueOrDefault(reason) + count;
                    contradicts = true;
                }
            if (contradicts) offending.Add(tick);
        }

        const string name = "no order is refused for a target its own observation admitted";
        if (offending.Count == 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name,
                $"no tick of {play.Count} refused an order for an unobserved target while its own census admitted usable work; {scene}",
                mode: "production-clock",
                killedBy: "counting refusals without requiring the same tick's census to have admitted usable work, "
                    + "which would make an honest refusal of unreachable work read as this defect");
            return 0;
        }

        string tally = string.Join(", ", byReason.OrderByDescending(p => p.Value).Select(p => $"{p.Key} x{p.Value}"));
        string priced = offending[^1].HasStep ? "a step" : "no step at all";
        string counted = string.Create(CultureInfo.InvariantCulture,
            $"{offending.Count} of {play.Count} ticks refused an order for a target the same frozen observation had admitted as usable, first at recorded tick {offending[0].Tick} and last at {offending[^1].Tick}");
        string lastTick = string.Create(CultureInfo.InvariantCulture,
            $"on the last such tick the census admitted {offending[^1].UsableAdmitted} usable and the search priced {priced}");
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
            $"{counted}; {tally}; {lastTick}; both reasons resolve to one predicate in source — the target fact's evidence is "
            + $"not Observed — so the census and the binder read one store and disagreed; {scene}",
            mode: "production-clock");
        return 1;
    }

    /// <summary>
    /// Whether work the brain admitted ever became work the body did, inside three seconds.
    ///
    /// The measured quantity is the longest unbroken run of ticks on which some domain admitted
    /// usable work and the course bound no step. A decision spans ticks by design and a course
    /// legitimately holds no step while one is in flight, so a short run is the brain working; a run
    /// that outlasts the window combat is already given to take the body is the companion doing
    /// nothing while something was there to do.
    /// </summary>
    private static int WorkAdmittedIsWorkBegun(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, string scene)
    {
        int longest = 0, longestFrom = -1, current = 0, currentFrom = -1;
        foreach (RunTheWorld.PlayTick tick in play)
        {
            if (tick.UsableAdmitted > 0 && !tick.HasStep)
            {
                if (current == 0) currentFrom = tick.Tick;
                current++;
                if (current > longest) { longest = current; longestFrom = currentFrom; }
            }
            else current = 0;
        }

        const string name = "work the census admits becomes a bound step within three seconds";
        string detail = longest == 0
            ? "every tick that admitted usable work also carried a bound step"
            : string.Create(CultureInfo.InvariantCulture,
                $"the longest stretch with usable work admitted and no step bound is {longest} ticks from recorded tick {longestFrom}");
        if (longest < StepWithinTicks)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name,
                $"{detail}, inside the stated {StepWithinTicks}; {scene}",
                mode: "production-clock",
                killedBy: "counting only the published-course reason and not the ticks a decision spans, which would hide a brain that decides forever");
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
            $"{detail}, past the stated {StepWithinTicks}; {scene}",
            mode: "production-clock");
        return 1;
    }

    /// <summary>
    /// Whether the hands ever came back after the last kill the recording credited the companion.
    ///
    /// The window is the recording's own rather than a count from the end, because what the play
    /// showed is a companion that stopped fighting at a particular moment and never started again.
    /// A capture that never credited the companion a kill cannot ask this and skips.
    /// </summary>
    private static int TheHandsComeBackAfterTheLastKill(string suite, IReadOnlyList<RunTheWorld.PlayTick> play,
        ReadRecordedActors.Cast cast, string scene)
    {
        const string name = "the hands fire again after the last recorded kill while hostiles stand";
        if (cast.LastCompanionKillTick < 0)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name,
                $"{Path.GetFileName(cast.EventsPath)} credits the companion no kill, so there is no moment for the hands to have fallen silent after");
            return 0;
        }

        var window = play.Where(t => t.Tick > cast.LastCompanionKillTick && t.HostilesAlive > 0).ToList();
        if (window.Count == 0)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name,
                $"no tick of this run sits after the recorded last kill at tick "
                + $"{cast.LastCompanionKillTick.ToString(CultureInfo.InvariantCulture)} with a hostile still standing; "
                + "either the window was not replayed or nothing was left alive in it");
            return 0;
        }

        int fired = window.Count(t => t.Fired);
        string after = string.Create(CultureInfo.InvariantCulture,
            $"the recorded last kill at tick {cast.LastCompanionKillTick}, after which this run holds {window.Count} ticks with a hostile standing");
        if (fired > 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name,
                $"{fired.ToString(CultureInfo.InvariantCulture)} of those ticks fired; {after}; {scene}",
                mode: "production-clock",
                killedBy: "counting ticks with no hostile alive in the window, which a run that placed no hostile would satisfy trivially");
            return 0;
        }
        string crowd = window.Max(t => t.HostilesAlive).ToString(CultureInfo.InvariantCulture);
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
            $"not one of those ticks fired, with up to {crowd} hostile(s) standing throughout; {after}; {scene}",
            mode: "production-clock");
        return 1;
    }

    /// <summary>
    /// The numbers beside the verdicts: what the course did, what it refused and what it cost.
    ///
    /// None of them is graded, and the reason is the ledger's own: a measure carries its number and
    /// its direction and the scoreboard compares it against the last clean ancestor, where a
    /// threshold written here would turn "ever green" into "green now" and could not tell a flake
    /// from a regression.
    /// </summary>
    private static void Measures(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, ReadRecordedActors.Cast cast, string scene)
    {
        void Share(string name, int numerator, int denominator, string? direction, string message)
            => EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, name,
                denominator <= 0 ? 0 : 100.0 * numerator / denominator, "%", direction, "production-clock",
                message: $"{numerator} of {denominator}; {message}; {scene}");

        int published = play.Count(t => t.Reason == "published-course-holds-no-step");
        Share("share of ticks whose published course holds no step", published, play.Count, "down",
            "the course settled and bound nothing; a decision still in flight is not counted, because a course legitimately holds no step while one runs");

        int admittedAndStepless = play.Count(t => t.UsableAdmitted > 0 && !t.HasStep);
        int admitted = play.Count(t => t.UsableAdmitted > 0);
        Share("share of ticks with usable work admitted and no step bound", admittedAndStepless, admitted, "down",
            "the denominator is the ticks on which some domain admitted usable work at all, so a run through an empty world cannot flatter this");

        long refused = play.Sum(t => (long)t.Refused);
        long orders = refused + play.Count(t => t.HasStep);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "orders refused per tick", play.Count == 0 ? 0 : (double)refused / play.Count,
            "orders", "down", "production-clock",
            message: $"{refused} refusals over {play.Count} ticks against {orders} orders that reached pricing or binding; {scene}");

        foreach (var reason in play.SelectMany(t => t.Refusals).GroupBy(p => p.Key).OrderByDescending(g => g.Sum(p => p.Value)).Take(6))
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, $"orders refused for {reason.Key}", reason.Sum(p => p.Value),
                "orders", "down", "production-clock", message: scene);

        double[] decide = play.Select(t => t.DecideMs).OrderBy(v => v).ToArray();
        double[] brain = play.Select(t => t.BrainMs).OrderBy(v => v).ToArray();
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "decide cost p50", Percentile(decide, 0.50), "ms", "down", "production-clock",
            message: "the course search's own phase under the game's own allowances, which is the regime a player met; not comparable to a figure taken with the allowances lifted; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "decide cost p99", Percentile(decide, 0.99), "ms", "down", "production-clock",
            message: "as above; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "whole-brain cost p50", Percentile(brain, 0.50), "ms", "down", "production-clock",
            message: "against a 16.67 ms frame; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "whole-brain cost p99", Percentile(brain, 0.99), "ms", "down", "production-clock",
            message: "against a 16.67 ms frame; " + scene);

        int collections = play.Count == 0 ? 0 : play[^1].Gen2Collections - play[0].Gen2Collections;
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "second-generation collections over the run", collections, "collections", "down", "production-clock",
            message: "counted for the whole process, so it includes the harness's own allocation as well as the brain's, "
                + "and it is the mechanism the capture named for its worst frames rather than a figure attributable to one component; " + scene);

        // Beside the verdict on whether the hands ever came back: how often they worked once they
        // had. The verdict is a floor — one shot in five hundred ticks satisfies it — and this is
        // the number that says whether the companion is fighting or twitching.
        var afterTheKill = play.Where(t => t.Tick > cast.LastCompanionKillTick && t.HostilesAlive > 0).ToList();
        if (cast.LastCompanionKillTick >= 0 && afterTheKill.Count > 0)
            Share("share of ticks after the last recorded kill that fired", afterTheKill.Count(t => t.Fired), afterTheKill.Count, "up",
                "the denominator is the ticks after the recording's last companion kill with a hostile still standing, "
                + "which in the capture this was built from is the 524-tick stretch the companion spent doing nothing");

        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "recorded drops this schema could not name", cast.Shortfall, "drops", "down", "production-clock",
            message: "the loot column counted more drops at once than the events sidecar names anywhere, so the staged scene is poorer than the play's by this many; " + scene);
    }

    /// <summary>The nearest-rank percentile of an already-sorted sample, which is what every other cost row here uses.</summary>
    private static double Percentile(double[] sorted, double fraction)
        => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
}
