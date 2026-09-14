#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// What was true of the machine and the tree when a run was taken, which is the difference between
/// a red that means something and a red nobody can place.
///
/// <see cref="Commit"/> is the commit the rows describe, and it is deliberately not always the
/// commit that was checked out. A run over a recorded capture describes the build that wrote the
/// capture, so it stores under that build's revision and <see cref="RanAt"/> carries the checkout
/// — which is what lets history be backfilled at all, and is the same mechanism by which a
/// calibration run on a known-bad build is stored like everything else rather than specially.
///
/// <see cref="Load"/> and <see cref="ConcurrentDotnet"/> exist because this suite's own flake trap
/// is a wall-clock one: a planning deadline decides how far a search gets, so a red taken with four
/// seats building in the same checkout and a red taken idle are different facts, and without the
/// two columns they are the same row.
/// </summary>
public sealed record RunHeader(
    string Commit,
    bool Dirty,
    string Timestamp,
    string Machine,
    double Load,
    int ConcurrentDotnet,
    string RanAt = "",
    string Note = "",
    string Filter = "")
{
    /// <summary>
    /// The case filter the run was taken under, empty when it ran everything. It is a header field
    /// rather than a note because <see cref="RunStore.Baseline"/> has to act on it: a filtered run
    /// on a clean tree is clean and non-dirty and would otherwise resolve as the baseline for the
    /// next full run, which would read every case the filter excluded as a case that disappeared.
    /// </summary>
    public bool Filtered => Filter.Length > 0;

    public static RunHeader Now(string commit, bool dirty, string ranAt, string note, string filter = "")
        => new(commit, dirty,
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Environment.MachineName,
            ReadLoad(),
            CountDotnet(),
            ranAt,
            note,
            filter);

    /// <summary>
    /// The one-minute load average. macOS has no <c>/proc/loadavg</c>, so this asks the kernel
    /// through <c>sysctl</c> and returns a negative number when it cannot — never zero, because a
    /// zero load is a real and very different claim from an unmeasured one.
    /// </summary>
    private static double ReadLoad()
    {
        try
        {
            if (File.Exists("/proc/loadavg"))
                return double.Parse(File.ReadAllText("/proc/loadavg").Split(' ')[0], CultureInfo.InvariantCulture);
            using var process = Process.Start(new ProcessStartInfo("/usr/sbin/sysctl", "-n vm.loadavg") { RedirectStandardOutput = true });
            if (process == null) return -1;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            // "{ 2.31 2.45 2.60 }"
            string[] parts = output.Trim().Trim('{', '}').Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double one) ? one : -1;
        }
        catch (Exception e) when (e is IOException or FormatException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }

    private static int CountDotnet()
    {
        try { return Process.GetProcessesByName("dotnet").Length; }
        catch (InvalidOperationException) { return -1; }
    }

    public string Serialise()
    {
        var text = new StringBuilder("{\"kind\":\"run\"");
        text.Append(",\"commit\":").Append(EmitLedgerRows.Quote(Commit));
        text.Append(",\"dirty\":").Append(Dirty ? "true" : "false");
        text.Append(",\"timestamp\":").Append(EmitLedgerRows.Quote(Timestamp));
        text.Append(",\"machine\":").Append(EmitLedgerRows.Quote(Machine));
        text.Append(",\"load\":").Append(Load.ToString("R", CultureInfo.InvariantCulture));
        text.Append(",\"dotnet_processes\":").Append(ConcurrentDotnet.ToString(CultureInfo.InvariantCulture));
        text.Append(",\"ran_at\":").Append(EmitLedgerRows.Quote(RanAt));
        text.Append(",\"note\":").Append(EmitLedgerRows.Quote(Note));
        text.Append(",\"filter\":").Append(EmitLedgerRows.Quote(Filter));
        return text.Append('}').ToString();
    }
}

/// <summary>One run file as read: its header, its rows, and the lines that were not either.</summary>
public sealed record Run(string Path, RunHeader Header, IReadOnlyList<LedgerRow> Rows, int Malformed)
{
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// A run is clean when nothing in it failed or errored. Skips do not make a run dirty, because
    /// a skip is missing coverage rather than a verdict, and a baseline that refused every run with
    /// a skip in it would never resolve on a repository whose captures are gitignored. A red row
    /// tagged <c>known-limitation</c> does not make a run dirty either: it is a defect the suite
    /// found and the board carries (the corpus mirror's one asymmetric block, AIC-260, is the first),
    /// and a repository that could never have a clean baseline while one such defect stood open
    /// would lose every comparison for as long as the defect took to fix. The tag comes off with
    /// the fix, at which point the row is an ordinary red again; the scoreboard still prints it.
    /// </summary>
    public bool Clean => Rows.All(r => r.Verdict is not ("fail" or "error") || (r.Tags?.Contains("known-limitation") ?? false));

    /// <summary>The cases this run actually measured: every case carrying at least one row that is
    /// not a skip. A skip is the absence of a measurement, so it belongs in <see cref="SkippedOnly"/>
    /// and never here.</summary>
    public IReadOnlySet<string> Reporting => reporting ??= Rows.Where(r => r.Verdict != "skipped")
        .Select(Key).ToHashSet(StringComparer.Ordinal);
    private HashSet<string>? reporting;

    /// <summary>Cases this run holds only as skips — asked for and not answered.</summary>
    public IReadOnlySet<string> SkippedOnly => skippedOnly ??= Rows.Select(Key).ToHashSet(StringComparer.Ordinal)
        .Except(Reporting).ToHashSet(StringComparer.Ordinal);
    private HashSet<string>? skippedOnly;

    /// <summary>
    /// Whether this run is a fair yardstick for <paramref name="candidate"/>, which is a question
    /// about coverage and not about verdicts.
    ///
    /// Two rules, and each was written from a run that actually reached the scoreboard. A baseline
    /// must not hold as a skip anything the new run measured: <c>measure-flake.sh</c> opens a run
    /// with no <c>--filter</c> and drives every repeat under <c>AIC_LEDGER_CASE</c>, so its file
    /// carries one real case and forty-odd skips, and without this rule it resolved as the baseline
    /// for the next full run — observed, not supposed. And a baseline must not measure cases the new
    /// run does not: <c>backfill-capture.sh</c> opens a run over a recorded capture whose rows are
    /// play measures and no fixtures at all, and comparing a full run against it printed "new 41,
    /// gone 46, nothing red" at exit 0. Neither producer is filtered by the header, so the header's
    /// own flag cannot catch either; what separates them is what they covered, which the rows say.
    ///
    /// Cases the new run measures and the baseline never held are not covered by either rule, and
    /// deliberately: that is a case being added, which must stay possible without disqualifying
    /// every ancestor in the store.
    /// </summary>
    public bool CoversRunsOf(Run candidate)
        => !candidate.SkippedOnly.Overlaps(Reporting) && candidate.Reporting.IsSubsetOf(Reporting);

    internal static string Key(LedgerRow row) => $"{row.Instrument}/{row.Suite}/{row.Case}";
}

/// <summary>
/// The store: where run files live, how one is opened, and which past run a new one is read
/// against. One file per run rather than one file appended to, because several lanes run the suite
/// in their own worktrees at once and a shared file is a conflict on every one of them.
/// </summary>
public static class RunStore
{
    public static string Folder(string repositoryRoot) => Path.Combine(repositoryRoot, "Tools", "Ledger", "runs");

    /// <summary>
    /// Open a run file and return its path. The stem is the commit the rows describe and the
    /// instant the run began, so the folder listing sorts by commit and a second run at one commit
    /// never overwrites the first — which is the whole basis of the repeat count the noise band and
    /// the pass-rate interval are computed from.
    /// </summary>
    public static string Begin(string repositoryRoot, RunHeader header)
    {
        string folder = Folder(repositoryRoot);
        Directory.CreateDirectory(folder);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(folder, $"{header.Commit}-{stamp}.jsonl");
        for (int attempt = 1; File.Exists(path); attempt++)
            path = Path.Combine(folder, $"{header.Commit}-{stamp}-{attempt}.jsonl");
        File.WriteAllText(path, header.Serialise() + "\n", new UTF8Encoding(false));
        return path;
    }

    public static Run? Read(string path)
    {
        if (!File.Exists(path)) return null;
        RunHeader? header = null;
        var rows = new List<LedgerRow>();
        int malformed = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Trim().Length == 0) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                string kind = root.GetProperty("kind").GetString() ?? "";
                if (kind == "run")
                {
                    header = new RunHeader(
                        root.GetProperty("commit").GetString() ?? "",
                        root.GetProperty("dirty").GetBoolean(),
                        root.GetProperty("timestamp").GetString() ?? "",
                        root.GetProperty("machine").GetString() ?? "",
                        root.GetProperty("load").GetDouble(),
                        root.GetProperty("dotnet_processes").GetInt32(),
                        root.TryGetProperty("ran_at", out JsonElement ranAt) ? ranAt.GetString() ?? "" : "",
                        root.TryGetProperty("note", out JsonElement note) ? note.GetString() ?? "" : "",
                        root.TryGetProperty("filter", out JsonElement filter) ? filter.GetString() ?? "" : "");
                    continue;
                }
                if (kind != "row") { malformed++; continue; }
                rows.Add(new LedgerRow(
                    root.GetProperty("instrument").GetString() ?? "",
                    root.GetProperty("suite").GetString() ?? "",
                    root.GetProperty("case").GetString() ?? "",
                    root.GetProperty("verdict").GetString() ?? "",
                    root.TryGetProperty("value", out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null,
                    root.TryGetProperty("unit", out JsonElement u) ? u.GetString() : null,
                    root.TryGetProperty("direction", out JsonElement d) ? d.GetString() : null,
                    root.TryGetProperty("mode", out JsonElement m) ? m.GetString() ?? "in-suite" : "in-suite",
                    root.TryGetProperty("tags", out JsonElement t) && t.ValueKind == JsonValueKind.Array
                        ? t.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : Array.Empty<string>(),
                    root.TryGetProperty("killed_by", out JsonElement k) ? k.GetString() : null,
                    root.TryGetProperty("message", out JsonElement g) ? g.GetString() ?? "" : "",
                    root.TryGetProperty("duration_ms", out JsonElement du) && du.ValueKind == JsonValueKind.Number ? du.GetDouble() : 0));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { malformed++; }
        }
        // A file whose header never parsed is not a run. Returning one with an invented header
        // would put a row under a commit nobody ran it at, which is the one lie the store must not
        // tell, because every comparison below is keyed on that commit.
        return header == null ? null : new Run(path, header, rows, malformed);
    }

    /// <summary>Every run in the store, newest first by its own timestamp.</summary>
    public static IReadOnlyList<Run> All(string repositoryRoot)
    {
        string folder = Folder(repositoryRoot);
        if (!Directory.Exists(folder)) return Array.Empty<Run>();
        return Directory.EnumerateFiles(folder, "*.jsonl")
            .Select(Read)
            .Where(run => run != null)
            .Select(run => run!)
            .OrderByDescending(run => run.Header.Timestamp, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Every run stored under one commit, newest first. More than one is the repeat count.</summary>
    public static IReadOnlyList<Run> At(string repositoryRoot, string commit)
        => All(repositoryRoot).Where(run => Git.Same(run.Header.Commit, commit)).ToArray();

    /// <summary>
    /// The nearest ancestor of <paramref name="commit"/> that has a clean run, which is what a new
    /// run is read against.
    ///
    /// Ancestry comes from git rather than from the file timestamps, because the runs folder is
    /// shared by every lane and a run taken ten minutes ago on another branch is not this change's
    /// baseline. A run whose commit git does not know — a capture's own source revision on a
    /// machine that never fetched it, say — is skipped rather than guessed at.
    /// </summary>
    /// <param name="excluding">
    /// The run being scored, which is never its own baseline. It is excluded here, inside the walk,
    /// rather than by the caller nulling the answer afterwards — that shape resolved the run to
    /// itself, threw the match away and reported "no baseline", so a clean run at a new commit was
    /// compared against nothing while its parent's run sat in the store. Excluding and continuing
    /// are different operations and only one of them answers the question.
    /// </param>
    /// <param name="scoring">
    /// The run being scored, when there is one. Its coverage decides which candidates are fair
    /// yardsticks — see <see cref="Run.CoversRunsOf"/> — because the header's filter flag cannot
    /// catch a partial run that never declared itself one, and two of this repository's own scripts
    /// produce exactly that.
    /// </param>
    public static Run? Baseline(string repositoryRoot, string commit, string? excluding = null, Run? scoring = null)
    {
        string[] ancestry = Git.Ancestry(repositoryRoot, commit);
        var runs = All(repositoryRoot);
        foreach (string ancestor in ancestry)
        {
            Run? clean = runs.FirstOrDefault(run => !run.Header.Dirty
                && !run.Header.Filtered
                && (excluding == null || !string.Equals(run.Path, excluding, StringComparison.Ordinal))
                && Git.Same(run.Header.Commit, ancestor)
                && (scoring == null || scoring.CoversRunsOf(run))
                && run.Clean);
            if (clean != null) return clean;
        }
        return null;
    }
}

/// <summary>
/// The few git questions the ledger asks, each as one plain invocation. It never writes, and every
/// failure answers "I do not know" rather than a default, because a baseline resolved from a wrong
/// ancestry is worse than no baseline: it produces a scoreboard of confident nonsense.
/// </summary>
public static class Git
{
    public static string Run(string repositoryRoot, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = repositoryRoot, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using Process? process = Process.Start(info);
            if (process == null) return "";
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : "";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return "";
        }
    }

    public static string Head(string repositoryRoot) => Run(repositoryRoot, "rev-parse", "--short", "HEAD");

    /// <summary>
    /// Whether two commit identifiers name one commit, when either may be abbreviated. A run header
    /// stores the short hash <see cref="Head"/> produces and <see cref="Ancestry"/> returns the full
    /// forty-character ones <c>rev-list</c> prints, so the comparison has to run in both directions:
    /// a one-way <c>StartsWith</c> from the stored short hash to a full ancestor is always false,
    /// which silently made every baseline unresolvable while the store held a perfectly good run.
    /// </summary>
    public static bool Same(string a, string b)
        => a.Length > 0 && b.Length > 0
        && (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the working tree carries changes the run's rows were produced from. A dirty run is
    /// never a baseline for anybody, because nothing identifies what it was actually run against.
    /// </summary>
    /// <remarks>
    /// The store's own run files are excluded, and without that exclusion the ledger defeats itself:
    /// a run leaves an untracked file in <c>runs/</c>, which makes the tree dirty, which makes the
    /// next run dirty, which disqualifies it as a baseline — so after the first run no run can ever
    /// be clean again until somebody commits in between. A run file is the record of a run and not
    /// a change to the code the run measures, which is exactly the thing this flag is asked about.
    /// </remarks>
    public static bool Dirty(string repositoryRoot)
        => Run(repositoryRoot, "status", "--porcelain", "--", ".", ":(exclude)Tools/Ledger/runs").Length > 0;

    /// <summary>
    /// <paramref name="commit"/> and then its ancestors, newest first — the order a baseline search
    /// walks. An unknown commit yields just itself, so a run stored under a capture's own revision
    /// still resolves against another run at that same revision.
    /// </summary>
    public static string[] Ancestry(string repositoryRoot, string commit)
    {
        string output = Run(repositoryRoot, "rev-list", "--max-count=400", commit);
        if (output.Length == 0) return new[] { commit };
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToArray();
    }

    /// <summary>The repository root from any directory inside it, or the current directory.</summary>
    public static string Root(string from)
    {
        string output = Run(from, "rev-parse", "--show-toplevel");
        return output.Length > 0 ? output : from;
    }
}
