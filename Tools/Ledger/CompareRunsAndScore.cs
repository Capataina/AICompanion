#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// A proportion with the interval it actually bounds, which is the whole reason a pass rate is
/// never printed here as a point.
///
/// The arithmetic is Wilson's score interval at 95 percent (z = 1.96), chosen over the textbook
/// normal approximation because that one is wrong in exactly the region this suite lives in: at
/// zero failures it produces an interval of zero width, so five green runs would read as proof
/// rather than as the weak evidence they are. Wilson keeps a real interval at the boundaries.
///
/// What it buys, in this suite's own numbers, every one of them what <see cref="Of"/> returns for
/// the stated counts rather than a figure quoted from anywhere: three green runs of five bounds the
/// true pass rate only to 23.1–88.2 percent, so the flake trap's "three of five" said almost
/// nothing; five green runs bound the failure rate below 43.45 percent; thirty bound it below 11.4
/// and a hundred below 3.7 — the verification plan's own "thirty below ten, a hundred below three"
/// is a little more generous than the arithmetic allows, while its "about 43 percent" is right.
/// That is the arithmetic behind the batch size any claim about an
/// intermittent fixture has to carry, and <c>SelfTestTheStore</c> pins all four so a change to the
/// formula moves the sentence rather than leaving it standing as prose nobody rechecks.
/// </summary>
public readonly record struct Wilson(double Low, double High, int Successes, int Trials)
{
    private const double Z = 1.96;

    public static Wilson Of(int successes, int trials)
    {
        if (trials <= 0) return new Wilson(0, 1, successes, trials);
        double p = (double)successes / trials;
        double z2 = Z * Z;
        double denominator = 1 + z2 / trials;
        double centre = (p + z2 / (2 * trials)) / denominator;
        double spread = Z / denominator * Math.Sqrt(p * (1 - p) / trials + z2 / (4.0 * trials * trials));
        return new Wilson(Math.Max(0, centre - spread), Math.Min(1, centre + spread), successes, trials);
    }

    public override string ToString()
        => Trials == 0 ? "no runs" : $"{Successes}/{Trials} = {100.0 * Successes / Trials:0.#}% (95% CI {100 * Low:0.#}–{100 * High:0.#}%)";
}

/// <summary>
/// The spread a measure shows when nothing changed, computed from repeats of one commit.
///
/// Its purpose is to stop a delta being read as an effect. Georges et al. (OOPSLA 2007) is the
/// rule applied: where the two intervals overlap there is no conclusion to draw, so a measure that
/// moved less than the noise is reported as moved-within-noise rather than as an improvement or a
/// regression. Fewer than three repeats prints no band at all rather than a fake one, because a
/// band from two points is a line through two points.
/// </summary>
public readonly record struct NoiseBand(bool Known, double Mean, double HalfWidth, int Repeats)
{
    public static NoiseBand From(IReadOnlyList<double> values)
    {
        if (values.Count < 3) return new NoiseBand(false, values.Count > 0 ? values.Average() : 0, 0, values.Count);
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        // 1.96 standard errors of the mean: the band the mean itself sits in, which is what a
        // second run's mean is compared against.
        double halfWidth = 1.96 * Math.Sqrt(variance / values.Count);
        return new NoiseBand(true, mean, halfWidth, values.Count);
    }

    public bool Covers(double value) => Known && Math.Abs(value - Mean) <= HalfWidth;
}

/// <summary>How one case's verdict differs between two runs.</summary>
/// <summary>
/// What one case did between two runs.
///
/// <see cref="StoppedReporting"/> and <see cref="NowReporting"/> exist because a verdict moving to
/// or from <c>skipped</c> used to be counted as unchanged, and that is the one transition a
/// scoreboard must never fold away: a case that reported a pass yesterday and is skipped today has
/// stopped measuring, which looks in every total exactly like a case that measured and passed. The
/// play measures make it concrete rather than hypothetical — <c>Telemetry/</c> is gitignored, so
/// every fresh checkout skips them, and the diff said "unchanged".
/// </summary>
public enum Change { NewRed, Fixed, Gone, StoppedReporting, NowReporting, New, Flaky, MeasureDrift, Unchanged }

public sealed record CaseChange(Change Change, string Key, string Before, string After, string Detail);

/// <summary>
/// The whole comparison of two runs, and the printed scoreboard that is the only thing anybody
/// reads at the end of a verify.
///
/// The verdict rules are stated here rather than distributed through the callers, so there is one
/// place to argue with. A new red fails the run. An error fails the run, because a check that broke
/// measured nothing and a run that cannot measure is not a run that passed. A fixed case, a new
/// case, a gone case and a measure drift never fail on their own: the first three are the tree
/// moving and the fourth is a number, and a number that decides a verdict from a threshold nobody
/// declared is the thing the plan refuses outright.
///
/// A new red seen on one run is deliberately not confirmed. One observation cannot separate a
/// regression from a flake, so it prints as unconfirmed with the command that would settle it, and
/// the exit code still fails — an unconfirmed red is a stop, and the rerun is how it is cleared.
/// </summary>
public static class Scoreboard
{
    public static IReadOnlyList<CaseChange> Compare(Run before, Run after, IReadOnlyList<Run> beforeRepeats)
    {
        static string Key(LedgerRow row) => $"{row.Instrument}/{row.Suite}/{row.Case}";
        var changes = new List<CaseChange>();

        // Where a run holds several rows for one case — which is exactly what a rerun produces —
        // the case is summarised rather than compared row by row, because "pass then fail" at one
        // commit is the definition of flaky and comparing the last row alone would hide it.
        var beforeByCase = before.Rows.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());
        var afterByCase = after.Rows.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToArray());

        foreach ((string key, LedgerRow[] rows) in afterByCase.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            LedgerRow[] was = beforeByCase.TryGetValue(key, out LedgerRow[]? found) ? found : Array.Empty<LedgerRow>();
            string nowVerdict = Summarise(rows);
            string wasVerdict = was.Length == 0 ? "-" : Summarise(was);

            // A verdict that moves to or from "skipped" is its own answer and never "unchanged".
            // It is decided before the measure branch, because a measure that was skipped at the
            // baseline carries no number and Drift has nothing to average — the crash this ordering
            // removes, and not a hypothetical one: the recorder writes terrain_revision at schema
            // 0.33.0 and the case that reads it is skipped in every run taken before it existed.
            bool wasSkipped = was.Length > 0 && Summarise(was) == "skipped";
            bool nowSkipped = nowVerdict == "skipped";
            if (was.Length > 0 && wasSkipped != nowSkipped)
            {
                changes.Add(nowSkipped
                    ? new CaseChange(Change.StoppedReporting, key, Summarise(was), "skipped", rows[^1].Message)
                    : new CaseChange(Change.NowReporting, key, "skipped", rows[0].Verdict == "measure" ? Value(rows) : nowVerdict, was[^1].Message));
                continue;
            }
            if (rows[0].Verdict == "measure")
            {
                if (was.Length == 0) { changes.Add(new CaseChange(Change.New, key, "-", Value(rows), "first measurement")); continue; }
                changes.Add(Drift(key, was, rows, beforeRepeats, Key));
                continue;
            }
            if (nowVerdict == "flaky") { changes.Add(new CaseChange(Change.Flaky, key, wasVerdict, nowVerdict, $"{rows.Count(r => r.Verdict == "pass")} pass, {rows.Count(r => r.Verdict is "fail" or "error")} fail over {rows.Length} runs of this case; {Wilson.Of(rows.Count(r => r.Verdict == "pass"), rows.Length)}")); continue; }
            if (was.Length == 0) { changes.Add(new CaseChange(Change.New, key, "-", nowVerdict, rows[^1].Message)); continue; }
            if (wasVerdict == "pass" && nowVerdict is "fail" or "error") { changes.Add(new CaseChange(Change.NewRed, key, wasVerdict, nowVerdict, rows[^1].Message)); continue; }
            if (wasVerdict is "fail" or "error" && nowVerdict == "pass") { changes.Add(new CaseChange(Change.Fixed, key, wasVerdict, nowVerdict, "")); continue; }
            changes.Add(new CaseChange(Change.Unchanged, key, wasVerdict, nowVerdict, ""));
        }
        foreach ((string key, LedgerRow[] rows) in beforeByCase.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            if (!afterByCase.ContainsKey(key))
                changes.Add(new CaseChange(Change.Gone, key, Summarise(rows), "-", "the case did not report on this run"));
        return changes;
    }

    private static CaseChange Drift(string key, LedgerRow[] was, LedgerRow[] now, IReadOnlyList<Run> repeats, Func<LedgerRow, string> key0)
    {
        // Either side can carry no number at all — a row filed as a measure whose value never
        // arrived, or a baseline that holds this case as something other than a measurement. An
        // average over nothing throws, and a scoreboard that dies has told the reader less than one
        // that says which side it could not read, so a missing side is reported rather than fatal.
        double? before = Mean(was), after = Mean(now);
        string unit = now[0].Unit ?? "";
        if (before is null || after is null)
            return new CaseChange(Change.MeasureDrift, key,
                before is { } b ? Format(b, unit) : "no number",
                after is { } a ? Format(a, unit) : "no number",
                "one side carries no value, so there is no drift to read");
        double wasValue = before.Value, nowValue = after.Value;
        NoiseBand band = NoiseBand.From(repeats
            .SelectMany(run => run.Rows)
            .Where(row => key0(row) == key && row.Value is not null)
            .Select(row => row.Value!.Value)
            .ToArray());
        string reading = band.Known
            ? band.Covers(nowValue)
                ? $"inside the noise band (±{band.HalfWidth:0.###} over {band.Repeats} repeats), so no conclusion"
                : $"outside the noise band (±{band.HalfWidth:0.###} over {band.Repeats} repeats)"
            : $"no noise band: {band.Repeats} repeat(s) at the baseline commit, and a band needs three";
        string way = now[0].Direction switch
        {
            "up" => nowValue > wasValue ? "better" : nowValue < wasValue ? "worse" : "unchanged",
            "down" => nowValue < wasValue ? "better" : nowValue > wasValue ? "worse" : "unchanged",
            _ => "neither way is declared good",
        };
        if (Math.Abs(nowValue - wasValue) < 1e-9)
            return new CaseChange(Change.Unchanged, key, Format(wasValue, unit), Format(nowValue, unit), "");
        return new CaseChange(Change.MeasureDrift, key, Format(wasValue, unit), Format(nowValue, unit), $"{way}; {reading}");
    }

    /// <summary>The mean of whatever numbers a case's rows actually carry, or null where none does.</summary>
    private static double? Mean(LedgerRow[] rows)
    {
        double[] values = rows.Where(r => r.Value is not null).Select(r => r.Value!.Value).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static string Format(double value, string unit) => $"{value.ToString("0.###", CultureInfo.InvariantCulture)}{(unit.Length > 0 ? " " + unit : "")}";
    private static string Value(LedgerRow[] rows) => Mean(rows) is { } mean ? Format(mean, rows[0].Unit ?? "") : "no number";

    /// <summary>
    /// One word for what a case did across however many times it ran in one file. Mixed pass and
    /// fail at one commit is flaky by observation rather than by suspicion, which is the only
    /// definition available from a ledger and the one the rerun rule produces.
    /// </summary>
    private static string Summarise(LedgerRow[] rows)
    {
        bool passed = rows.Any(r => r.Verdict == "pass");
        bool failed = rows.Any(r => r.Verdict is "fail" or "error");
        if (passed && failed) return "flaky";
        if (failed) return rows.Any(r => r.Verdict == "error") ? "error" : "fail";
        if (passed) return "pass";
        return rows[0].Verdict;
    }

    /// <summary>
    /// The printed scoreboard, and the verdict. Unchanged cases are counted and not listed, because
    /// the scoreboard exists to be read after a build by somebody who wants to know what moved.
    /// </summary>
    public static (string Text, int Exit) Render(Run after, Run? before, IReadOnlyList<Run> beforeRepeats)
    {
        var text = new StringBuilder();
        int reds = after.Rows.Count(r => r.Verdict is "fail" or "error");
        int skips = after.Rows.Count(r => r.Verdict == "skipped");
        int measures = after.Rows.Count(r => r.Verdict == "measure");
        int unkilled = after.Rows.Count(r => r.Verdict == "pass" && r.KilledBy == null);

        text.AppendLine($"ledger  {after.Name}  at {after.Header.Commit}{(after.Header.Dirty ? " (dirty)" : "")}"
            + $"  load {after.Header.Load:0.##}, {after.Header.ConcurrentDotnet} dotnet process(es)");
        text.AppendLine($"        {after.Rows.Count} rows: {after.Rows.Count(r => r.Verdict == "pass")} pass, {reds} red, {skips} skipped, {measures} measure, {after.Rows.Count(r => r.Verdict == "sealed")} sealed");

        // Counted outside the comparison block so the closing line can carry them. A run that
        // stopped measuring things its baseline measured is not a clean run with a footnote: it is
        // a run whose coverage fell, and "nothing red" printed on its own is how that gets read as
        // health. The exit code still comes from this run's own red rows, because coverage falling
        // is not the same event as a check failing and conflating them would make one unfixable
        // without the other.
        int stoppedReporting = 0, gone = 0;

        if (before == null)
        {
            text.AppendLine("        no baseline: no ancestor commit has a clean run in the store, so nothing here is a comparison");
        }
        else
        {
            text.AppendLine($"        against {before.Name} at {before.Header.Commit}");
            var changes = Compare(before, after, beforeRepeats);
            stoppedReporting = changes.Count(c => c.Change == Change.StoppedReporting);
            gone = changes.Count(c => c.Change == Change.Gone);
            foreach (Change kind in new[] { Change.NewRed, Change.StoppedReporting, Change.Gone, Change.Flaky, Change.Fixed, Change.MeasureDrift, Change.NowReporting, Change.New })
            {
                var group = changes.Where(c => c.Change == kind).ToArray();
                if (group.Length == 0) continue;
                text.AppendLine();
                text.AppendLine($"  {Label(kind)} ({group.Length})");
                foreach (CaseChange change in group)
                {
                    text.AppendLine($"    {change.Key}");
                    text.AppendLine($"      {change.Before} -> {change.After}{(change.Detail.Length > 0 ? "  " + change.Detail : "")}");
                    if (kind == Change.NewRed)
                        text.AppendLine($"      unconfirmed on one run; settle it with:  sh Tools/verify.sh --rerun-red 5 --case \"{change.Key.Split('/')[^1]}\"");
                }
            }
            text.AppendLine();
            text.AppendLine($"  unchanged {changes.Count(c => c.Change == Change.Unchanged)}");
        }

        // A case that ran more than once inside this run file is a repeat batch, and it is the only
        // shape that can say anything about a rate. It is reported whether or not a baseline
        // resolved, because a flake batch deliberately has no baseline: the question it asks is
        // about this commit against itself, and the interval is the answer.
        var repeated = after.Rows
            .Where(r => r.Verdict is "pass" or "fail" or "error")
            .GroupBy(r => $"{r.Instrument}/{r.Suite}/{r.Case}")
            .Where(g => g.Count() > 1)
            .ToArray();
        if (repeated.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"  repeats in this run ({repeated.Length})");
            foreach (var group in repeated)
            {
                int passes = group.Count(r => r.Verdict == "pass");
                Wilson rate = Wilson.Of(passes, group.Count());
                bool flaky = passes > 0 && passes < group.Count();
                text.AppendLine($"    {(flaky ? "FLAKY" : "     ")} {group.Key}");
                text.AppendLine($"      {rate}");
                if (flaky)
                    text.AppendLine($"      passed and failed at one commit, so this is intermittent by observation rather than by suspicion; "
                        + $"the interval is what the batch of {group.Count()} actually bounds, and narrowing it costs more runs rather than more argument");
            }
        }

        if (unkilled > 0)
            text.AppendLine($"  unkilled  {unkilled} passing case(s) carry no killed_by, so nothing records which rival rule they reject");
        if (skips > 0)
            foreach (LedgerRow row in after.Rows.Where(r => r.Verdict == "skipped").Take(12))
                text.AppendLine($"  skipped   {row.Instrument}/{row.Case} — {row.Message}");

        // The verdict is the run's own reds, not the diff, because a case that has been red since
        // before the baseline is still red and a scoreboard that exits 0 on it would make "green"
        // mean "no worse than yesterday".
        int exit = reds == 0 ? 0 : 1;
        string coverage = stoppedReporting + gone == 0
            ? ""
            : $", and {stoppedReporting + gone} case(s) that reported at the baseline did not report here"
              + " — this run measured less than the run it is being read against";
        text.AppendLine();
        text.AppendLine(exit == 0
            ? $"ledger: {after.Rows.Count} rows, nothing red{coverage}"
            : $"ledger: {reds} red row(s){coverage}");
        return (text.ToString(), exit);
    }

    private static string Label(Change change) => change switch
    {
        Change.NewRed => "NEW RED",
        Change.Fixed => "fixed",
        Change.Flaky => "flaky",
        Change.Gone => "gone — filed no row at all this run",
        Change.StoppedReporting => "STOPPED REPORTING — reported at the baseline, skipped now",
        Change.NowReporting => "now reporting — skipped at the baseline",
        Change.New => "new",
        Change.MeasureDrift => "measures that moved",
        _ => "unchanged",
    };
}
