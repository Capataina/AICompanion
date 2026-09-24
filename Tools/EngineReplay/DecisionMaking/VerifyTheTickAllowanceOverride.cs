extern alias live;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TickAllowance = live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.TickAllowance;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// The harness-only seam the world run's budget curve moves the tick's allowance through: inert unless set,
/// honoured by the one production owner of an allowance, and refusing a figure it could not honour.
///
/// <para>The curve's whole claim is "the brain ran at 2 ms", and it is worthless if some reader of the 12 ms
/// tunable kept 12 while the tick moved. Every nested slice already derives its deadline from the tick's
/// through <c>LimitPlanningWork.Deadline</c>, so the one reader that has to move is the one that installs the
/// allowance. The second row pins that as a property of the source rather than of this afternoon's reading of
/// it: a new production reader of <c>Weights.TotalPlanningMilliseconds</c> is a reader the curve would not
/// reach, and it goes red here rather than quietly halving what the curve measures.</para>
///
/// <para>That the override actually cuts the decide phase is a timing, and so it is not asserted here; the
/// budget curve prints the decide phase at each allowance, which is where it was shown.</para>
/// </summary>
internal static class VerifyTheTickAllowanceOverride
{
    /// <summary>
    /// Files that may name the tunable, each with the reason.
    ///
    /// <c>AuditDecisionContracts.cs</c> was listed here until the section-profiler merge of 24 September 2026, when
    /// its overrun ceiling moved onto the seam too, so under the budget curve's override its
    /// <c>decide-overran-allowance</c> records are judged against the allowance actually installed.
    /// </summary>
    private static readonly string[] AllowedReaders =
    {
        "Companion/Brain/Infrastructure/Selection/BehaviourWeights.cs",            // the declaration
        "Companion/Brain/Infrastructure/Selection/Computation/OverrideTickAllowance.cs", // the seam
    };

    private const string Tunable = "TotalPlanningMilliseconds";

    public static int Run()
    {
        int red = 0;
        red += Row("the seam is the tunable until a harness sets it, and refuses a figure it could not honour", TheSeamIsInertUntilSet);
        red += Row("no production reader of the tick's allowance bypasses the seam", NoReaderBypassesTheSeam);
        return red;
    }

    private static int Row(string name, Action test)
    {
        double? was = TickAllowance.OverrideMilliseconds;
        try { test(); Console.WriteLine("  GREEN " + name); return 0; }
        catch (Exception error) { Console.WriteLine("  RED " + name + ": " + error.Message); return 1; }
        finally { TickAllowance.OverrideMilliseconds = was; }
    }

    private static void TheSeamIsInertUntilSet()
    {
        if (TickAllowance.OverrideMilliseconds is { } standing)
            throw new InvalidOperationException($"the override was {standing} ms on entry; something left it set, and every later brain tick in this process ran at it");
        Expect(TickAllowance.Milliseconds, Weights.TotalPlanningMilliseconds, "unset");

        TickAllowance.OverrideMilliseconds = 2.5;
        Expect(TickAllowance.Milliseconds, 2.5, "set to 2.5");
        TickAllowance.OverrideMilliseconds = null;
        Expect(TickAllowance.Milliseconds, Weights.TotalPlanningMilliseconds, "cleared");

        foreach (double bad in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            bool refused = false;
            try { TickAllowance.OverrideMilliseconds = bad; }
            catch (ArgumentOutOfRangeException) { refused = true; }
            if (!refused)
                throw new InvalidOperationException($"an override of {bad} ms was accepted; a curve point filed under a figure the tick could not honour is filed under the wrong figure");
            Expect(TickAllowance.Milliseconds, Weights.TotalPlanningMilliseconds, $"after refusing {bad}");
        }
    }

    private static void NoReaderBypassesTheSeam()
    {
        string root = RepositoryRoot();
        string companion = Path.Combine(root, "Companion");
        var readers = Directory.EnumerateFiles(companion, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(Tunable, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        var bypassing = readers.Except(AllowedReaders, StringComparer.Ordinal).ToList();
        if (bypassing.Count > 0)
            throw new InvalidOperationException($"{bypassing.Count} production file(s) read {Tunable} without going through TickAllowance, so an override would not reach them: {string.Join(", ", bypassing)}");
        // The pattern list must assert its own names exist: a renamed seam or tunable would otherwise leave
        // this row searching for a word nobody writes and passing on the silence.
        foreach (string allowed in AllowedReaders)
            if (!readers.Contains(allowed, StringComparer.Ordinal))
                throw new InvalidOperationException($"{allowed} no longer names {Tunable}; the allow-list is stale and this row is no longer searching for the reader it was written about");
        string tick = File.ReadAllText(Path.Combine(root, "Companion", "Brain", "CoordinateBrainTick.cs"));
        if (!tick.Contains("TickAllowance.Milliseconds", StringComparison.Ordinal))
            throw new InvalidOperationException("CoordinateBrainTick.cs no longer installs TickAllowance.Milliseconds, so the brain tick's allowance is not the one a harness overrides");
    }

    /// <summary>The checkout this assembly was built from, found by walking up to the mod's own project file
    /// rather than trusting the working directory, which differs between verify, run-case and a hand run.</summary>
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at != null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "AICompanion.csproj")))
                return at.FullName;
        throw new InvalidOperationException($"no AICompanion.csproj above {AppContext.BaseDirectory}, so the source this row pins cannot be read");
    }

    private static void Expect(double actual, double expected, string when)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{when}, the tick's allowance read {actual} ms where {expected} ms was expected");
    }
}
