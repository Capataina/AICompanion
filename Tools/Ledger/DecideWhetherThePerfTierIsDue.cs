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
        Run? lastPerf = RunStore.All(repositoryRoot)
            .Where(run => run.Header.Tier == RunHeader.PerfTier && !run.Header.Dirty && !run.Header.Filtered)
            .FirstOrDefault(run => ancestry.Any(ancestor => Git.Same(run.Header.Commit, ancestor)));
        if (lastPerf == null)
            return new Answer(true, "no perf run in this branch's history");
        // Committed changes since the perf run, then uncommitted ones against HEAD, then untracked files. Three
        // name-only listings rather than `status --porcelain`, whose leading status columns do not survive
        // Git.Run trimming the output.
        var changed = Git.Run(repositoryRoot, "diff", "--name-only", lastPerf.Header.Commit, "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Concat(Git.Run(repositoryRoot, "diff", "--name-only", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Concat(Git.Run(repositoryRoot, "ls-files", "--others", "--exclude-standard").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        IReadOnlyList<string> relevant = CompiledIntoTheMod(changed);
        if (relevant.Count == 0)
            return new Answer(false, $"nothing the mod is compiled from changed since the last perf run at {lastPerf.Header.Commit}");
        string named = string.Join(", ", relevant.Take(4)) + (relevant.Count > 4 ? $" and {relevant.Count - 4} more" : "");
        return new Answer(true, $"{relevant.Count} file(s) the mod is compiled from changed since the last perf run at {lastPerf.Header.Commit}: {named}");
    }
}
