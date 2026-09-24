using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AICompanion.Tools.Ledger;

/// <summary>
/// What a headless number means in the game: a capture's own per-tick phase timings, taken while it was
/// played, against the same phases taken by replaying that capture headlessly, filed as the ratio in-game
/// over headless per phase at p50 and p99.
///
/// <para><b>Both sides are read out of a capture written by the same recorder.</b> The replay attaches the
/// mod's own recorder, exactly as the play-measures command does, so the headless figures are the columns
/// <c>senses_ms</c>, <c>decide_ms</c>, … written by the code that wrote the in-game ones. A harness reading
/// the brain's fields itself would be a second producer, and the ratio would then partly measure the
/// difference between two readers of one clock.</para>
///
/// <para><b>Distributions are compared, never ticks.</b> The replay diverges from the play within a hundred
/// ticks, so tick N of one is a different moment from tick N of the other and a per-tick ratio would be the
/// ratio of two unrelated events. Each phase is compared over the ticks on which it ran on each side — a
/// value above zero at the recorder's two decimals — because several phases run on a minority of ticks and a
/// median over all ticks would be the median of the zeros.</para>
///
/// <para><b>What the ratio cannot carry.</b> Rendering, other mods and the game's own update are absent from
/// the headless side by construction, and the frame column has no headless counterpart, so none of them is
/// in any ratio here: the ratio converts a <em>brain</em> figure, and a brain figure is not a frame. It also
/// cannot separate the machine from the scene. The in-game side ran on the build that was played and the
/// headless side on this tree, so a phase whose work changed between the two builds reads as a change of
/// speed. And a phase bounded by the allowance — decide, the route search, the reach flood — has its p99
/// pinned near the deadline on both sides, so its p99 ratio says little about the machine; the unbounded
/// phases carry the conversion.</para>
/// </summary>
internal static class RunTheCalibration
{
    /// <summary>The recorder's phase columns, in the order it writes them. <c>brain_ms</c> is the whole
    /// brain tick and is the overall ratio. Lane C's section columns slot in here by name once both
    /// captures carry them.</summary>
    private static readonly string[] Phases =
        { "senses_ms", "reflex_ms", "decide_ms", "position_ms", "navigate_ms", "plan_ms", "flood_ms", "finalise_ms", "brain_ms" };

    /// <summary>Phases whose own work is cut by a millisecond allowance, so their tail is the deadline's
    /// rather than the machine's.</summary>
    private static readonly HashSet<string> DeadlineBounded = new(StringComparer.Ordinal) { "decide_ms", "plan_ms", "flood_ms" };

    /// <summary>How many ticks a phase must have run on, on each side, before its p50 ratio is filed. Under
    /// this a median is a handful of ticks.</summary>
    private const int MedianFloor = 20;

    /// <summary>How many for a p99 ratio: under a hundred, nearest-rank p99 is the maximum.</summary>
    private const int TailFloor = 100;

    /// <summary>
    /// The smallest percentile, in milliseconds, a ratio is taken of. The recorder writes two decimals, so a
    /// phase at 0.01 ms is one quantum and a ratio of two such figures is rounding: the first full run filed
    /// 200 for the reach flood (2.00 over 0.01) and 0.5 for navigation (0.02 over 0.04). Ten quanta bounds the
    /// rounding at five percent a side. This is the column's resolution, not a judgement about cost.
    /// </summary>
    private const double ResolutionFloorMs = 0.10;

    public static int Run(string capturePath, string world, int ticks, string suite, string? recordTo)
    {
        var route = ReadRecordedRoute.Read(capturePath, 1, ticks);
        Console.WriteLine($"ROUTE {route.Capture} schema={route.Schema} steps={route.Count} from tick {route[0].Tick} to {route[^1].Tick}");
        LoadTheSavedWorld.Load(world);
        var cast = ReadRecordedActors.Read(capturePath, route.Steps.Max(s => s.Loot));
        var stage = new StageRecordedActors(cast, StageRecordedActors.HostileMotion.Native);
        ApplyTheRecordedPreferences.From(cast.Configuration, route.ConfigLine);
        RunTheWorld.Actors = stage;
        RunTheWorld.ProductionClock = true;
        PrepareTheHeadlessEngine.ForgetEverythingLearnedAboutTheWorld();

        string? folder = null;
        DateTime started = DateTime.UtcNow;
        RunTheWorld.AfterTheCompanionIsAttached = () =>
        {
            folder = AttachTheRecorder.Open(recordTo, route.Capture);
            Console.WriteLine("RECORDER " + AttachTheRecorder.Describe());
        };
        RunTheWorld.Outcome run;
        try
        {
            run = RunTheWorld.Play(route, "calibration", seed: 1);
        }
        finally
        {
            AttachTheRecorder.Close();
            RunTheWorld.AfterTheCompanionIsAttached = null;
            RunTheWorld.Actors = null;
            RunTheWorld.ProductionClock = false;
        }
        Console.WriteLine($"RUN {run.Ticks} ticks in {run.Seconds:0.0}s under the production clock");

        string headlessPath = folder is null ? "" : Directory.EnumerateFiles(folder, "*.tsv")
            .Where(path => File.GetLastWriteTimeUtc(path) >= started)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "";
        if (headlessPath.Length == 0)
        {
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, "the headless replay wrote a capture to calibrate against",
                $"no .tsv written after {started:O} under {folder ?? "<recorder never opened>"}; the calibration has no headless side",
                mode: "production-clock", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
            return 1;
        }

        var inGame = ReadTheColumns(capturePath, Phases);
        var headless = ReadTheColumns(headlessPath, Phases);
        string playedRevision = HeaderField(capturePath, "source_revision"), replayedRevision = HeaderField(headlessPath, "source_revision");
        string builds = playedRevision == replayedRevision && playedRevision is not ("" or "unknown")
            ? $"both sides ran source revision {playedRevision}, so the ratio is the machine and the host rather than the build"
            : $"the in-game side ran source revision '{(playedRevision.Length == 0 ? "unrecorded" : playedRevision)}' and the headless side '{(replayedRevision.Length == 0 ? "unrecorded" : replayedRevision)}', "
                + "so every ratio here mixes what changed in the brain between the two builds with what differs between the two hosts, and it is a conversion factor only once a capture of this tree is played";
        string machine = $"headless process {RuntimeInformation.ProcessArchitecture} on {RuntimeInformation.OSArchitecture} ({Environment.ProcessorCount} logical processors, load {SummariseCostDistributions.MachineLoad()}); "
            + $"the in-game side is {route.Capture} as played (schema {route.Schema}), whose header does not record its process architecture — the root guide sampled the game running x86-64 under Rosetta on this machine; "
            + builds;
        Console.WriteLine($"CALIBRATE {Path.GetFileName(capturePath)} ({inGame.Rows} rows) against {headlessPath} ({headless.Rows} rows); {machine}");

        string[] tags = { EmitLedgerRows.ProductionAllowancesTag, EmitLedgerRows.SampledTag, EmitLedgerRows.TimedTag, EmitLedgerRows.PerfTierTag };
        foreach (string phase in Phases)
        {
            if (!inGame.Columns.TryGetValue(phase, out var played) || !headless.Columns.TryGetValue(phase, out var replayed))
            {
                EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, CaseName(phase, "p50"),
                    $"{phase} is absent from {(inGame.Columns.ContainsKey(phase) ? "the headless capture" : "the in-game capture")}, so there is nothing to divide");
                continue;
            }
            var ranInGame = played.Where(v => v > 0).ToList();
            var ranHeadless = replayed.Where(v => v > 0).ToList();
            string coverage = string.Create(CultureInfo.InvariantCulture,
                $"{phase} ran on {ranInGame.Count} of {played.Count} in-game ticks and {ranHeadless.Count} of {replayed.Count} headless ticks");
            foreach ((string label, double fraction, int floor) in new[] { ("p50", 0.50, MedianFloor), ("p99", 0.99, TailFloor) })
            {
                string name = CaseName(phase, label);
                if (ranInGame.Count < floor || ranHeadless.Count < floor)
                {
                    EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name,
                        $"{coverage}; a {label} ratio needs {floor} on each side, because under that the percentile is a handful of ticks");
                    continue;
                }
                double game = SummariseCostDistributions.Percentile(ranInGame, fraction);
                double here = SummariseCostDistributions.Percentile(ranHeadless, fraction);
                if (game < ResolutionFloorMs || here < ResolutionFloorMs)
                {
                    EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name,
                        string.Create(CultureInfo.InvariantCulture,
                            $"in-game {label} {game:0.00} ms and headless {label} {here:0.00} ms; a ratio needs both at or above {ResolutionFloorMs:0.00} ms, ten of the recorder's 0.01 ms quanta, because below that it is a ratio of rounding; {coverage}"));
                    continue;
                }
                string pinned = DeadlineBounded.Contains(phase) && label == "p99"
                    ? "; this phase is cut by the millisecond allowance, so its tail is the deadline's on both sides and this ratio says little about the machine"
                    : "";
                EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, name, here > 0 ? game / here : 0, "ratio", null,
                    "production-clock", tags,
                    message: string.Create(CultureInfo.InvariantCulture,
                        $"in-game {label} {game:0.00} ms over headless {label} {here:0.00} ms; {coverage}; distributions compared, never ticks, because the replay diverges from the play within a hundred ticks{pinned}; ")
                        + "rendering, other mods and the game's own update are in neither side, so this converts a brain figure and not a frame; "
                        + machine);
            }
        }
        return 0;
    }

    private static string CaseName(string phase, string label)
        => phase == "brain_ms"
            ? $"in-game over headless brain cost at {label}"
            : $"in-game over headless {phase[..^3]} phase cost at {label}";

    /// <summary>One field of a capture's `# key=value;…` header lines, or empty when no header line carries it.</summary>
    private static string HeaderField(string path, string key)
    {
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.TrimStart('﻿');
            if (line.Length == 0 || line[0] != '#') break;
            foreach (string part in line.TrimStart('#', ' ').Split(';'))
            {
                int equals = part.IndexOf('=');
                if (equals > 0 && part[..equals].Trim() == key) return part[(equals + 1)..].Trim();
            }
        }
        return "";
    }

    /// <summary>The named columns of a capture, parsed invariantly, keyed on the header's own names so a
    /// schema that moved or added a column is read by name and a missing one is absent rather than
    /// misread.</summary>
    private static (int Rows, Dictionary<string, List<double>> Columns) ReadTheColumns(string path, IReadOnlyList<string> names)
    {
        string[]? header = null;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var columns = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        int rows = 0;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.TrimStart('﻿');
            if (line.Length == 0 || line[0] == '#') continue;
            string[] cells = line.Split('\t');
            if (header == null)
            {
                header = cells;
                foreach (string name in names)
                {
                    int at = Array.IndexOf(header, name);
                    if (at >= 0) { indices[name] = at; columns[name] = new List<double>(); }
                }
                continue;
            }
            rows++;
            foreach ((string name, int at) in indices)
                if (at < cells.Length && double.TryParse(cells[at], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    columns[name].Add(value);
        }
        return (rows, columns);
    }
}
