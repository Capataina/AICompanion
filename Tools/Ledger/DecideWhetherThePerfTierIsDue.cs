#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// Whether this verify should also run the perf tier, the cases heavy enough that an ordinary verify skips
/// them.
///
/// The owner's rule, 24 September 2026: run it "whenever we change the brain or something that might affect
/// the brain … for example, if we don't directly change the brain but change the mastery tree, which has a
/// chance of changing the brain, we would run it." A list of brain folders would miss exactly his example,
/// because the mastery tree lives under the profile card and progression rather than under Brain. So the rule
/// is the build's own: the perf tier is due when any file the mod assembly is compiled from has changed since
/// the newest perf run in this branch's history. `AICompanion.csproj` compiles every `.cs` file outside
/// `Tools/` and outside hidden folders, so that set, plus the project file itself, is the whole of "anything
/// that might affect the brain". A guide, a tool or a ledger run cannot change what the mod does, and none of
/// them makes the tier due.
/// </summary>
public static class PerfTierDecision
{
    public sealed record Answer(bool Due, string Reason);

    /// <summary>The paths among <paramref name="changed"/> that the mod assembly is compiled from.</summary>
    public static IReadOnlyList<string> CompiledIntoTheMod(IEnumerable<string> changed)
        => changed
            .Select(path => path.Replace('\\', '/').Trim())
            .Where(path => path.Length > 0)
            .Where(path => path == "AICompanion.csproj"
                || (path.EndsWith(".cs", StringComparison.Ordinal)
                    && !path.StartsWith("Tools/", StringComparison.Ordinal)
                    && !path.Split('/').Any(segment => segment.StartsWith('.'))))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static Answer Decide(string repositoryRoot)
    {
        string[] ancestry = Git.Ancestry(repositoryRoot, "HEAD");
        // The last perf run is the newest clean-tree, unfiltered perf run in this branch's history whose perf-tier
        // cases actually produced a row other than a skip. A perf run whose timed lane crashed before reaching
        // them measured nothing, and counting it would let the next change go unmeasured.
        Run? lastPerf = RunStore.All(repositoryRoot)
            .Where(run => run.Header.Tier == RunHeader.PerfTier && !run.Header.Dirty && !run.Header.Filtered)
            .Where(run => run.Rows.Any(r => r.Verdict != "skipped" && r.Tags?.Contains(EmitLedgerRows.PerfTierTag) == true))
            .FirstOrDefault(run => ancestry.Any(ancestor => Git.Same(run.Header.Commit, ancestor)));
        if (lastPerf == null)
            return new Answer(true, "no perf run in this branch's history");
        // Committed changes since the perf run, then uncommitted ones against HEAD, then untracked files. Three
        // name-only listings rather than `status --porcelain`, whose leading status columns do not survive the
        // trimming. `--no-renames` because a rename lists only its destination, so a file moved out of the
        // compiled set would vanish from the listing; `core.quotepath=off` because a non-ASCII path otherwise
        // comes back C-quoted and fails every suffix test. Each listing that fails makes the answer unknown.
        string?[] listings =
        {
            Git.TryRun(repositoryRoot, "-c", "core.quotepath=off", "diff", "--no-renames", "--name-only", lastPerf.Header.Commit, "HEAD"),
            Git.TryRun(repositoryRoot, "-c", "core.quotepath=off", "diff", "--no-renames", "--name-only", "HEAD"),
            Git.TryRun(repositoryRoot, "-c", "core.quotepath=off", "ls-files", "--others", "--exclude-standard"),
        };
        if (listings.Any(listing => listing == null))
            throw new InvalidOperationException($"git could not list what changed since the last perf run at {lastPerf.Header.Commit}, so whether the perf tier is due is unknown");
        var changed = listings.SelectMany(listing => listing!.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        IReadOnlyList<string> relevant = CompiledIntoTheMod(changed);
        if (relevant.Count == 0)
            return new Answer(false, $"nothing the mod is compiled from changed since the last perf run at {lastPerf.Header.Commit}");
        string named = string.Join(", ", relevant.Take(4)) + (relevant.Count > 4 ? $" and {relevant.Count - 4} more" : "");
        return new Answer(true, $"{relevant.Count} file(s) the mod is compiled from changed since the last perf run at {lastPerf.Header.Commit}: {named}");
    }
}
