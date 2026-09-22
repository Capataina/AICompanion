#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AICompanion.Tools.Ledger;

/// <summary>
/// Gate G10's one mechanically checkable half: the six domains read the shared spatial facts rather than
/// building their own.
///
/// **What this file is not.** G10 asks that light, loot, mining, chopping, combat and company all inspect the
/// same clearance, intent and capability facts at discovery, binding, destination and use — a four-by-six
/// enumeration — plus surface and cave hover and two-by-two passage controls. That enumeration is not built
/// and this row does not pretend to be it; `Observation/CLAUDE.md` and the lane's return carry what is missing
/// and why. What is here is the one property of it that can be asserted mechanically and killed by a plant,
/// and the enumeration it walks is printed so the next attempt at the whole gate starts from a list rather
/// than from a grep somebody ran once.
///
/// **Why it reads source rather than driving a scene.** "Nobody recomputes this privately" is a statement
/// about every call site, and a behavioural fixture can only ever exercise the call sites it happens to
/// reach — a seventh domain added next month is exactly the one a scene would miss. `Tools/check-navigation-
/// boundary.sh` already holds the same shape for route searching, and `VerifyDecisionTripwires` already reads
/// two refusal literals back out of their producers by path, so a source-level assertion is the suite's own
/// existing answer to this kind of question rather than a new device.
///
/// The check is a refusal rather than a presence test on purpose. Requiring each domain to *name* the sense is
/// satisfied by a file that names it once and then computes its own answer beside it; requiring that none of
/// them *builds* one cannot be satisfied that way, because the construction is the thing being refused.
/// </summary>
internal static class VerifySharedSpatialFacts
{
    /// <summary>
    /// The two trees the rule binds. Both are consumers by design: an activity decides what to do and an
    /// interaction does it, and neither is where a spatial fact is derived. `Infrastructure/Observation/` is
    /// deliberately not in the list — it is where the facts come from.
    /// </summary>
    private static readonly string[] ConsumerTrees =
    {
        "Companion/Brain/Activities",
        "Companion/Brain/Infrastructure/Interactions",
    };

    /// <summary>
    /// Each refused shape is a way of answering a shared question privately, named as the text that appears
    /// at the call site rather than as a type, because that is what a grep can be held to and what a new file
    /// will be written as.
    /// </summary>
    private static readonly (string Pattern, string Fact, string Instead)[] Refused =
    {
        ("PlayerIntentRegion.Around(", "the player's intent region", "read ctx.Senses.Intent.Region or .Regions"),
        ("new PlayerIntentRegion(", "the player's intent region", "read ctx.Senses.Intent.Region or .Regions"),
        ("new ObservePlayerIntentRegion(", "the player's intent region", "read ctx.Senses.Intent"),
        ("new ClearanceField(", "the clearance field", "read ClearanceHeat, the one shared surface over it"),
        ("new ObserveReach(", "the reach flood", "read ctx.Senses.Reach"),
        ("new ObserveLight(", "the light field", "read ctx.Senses.Light"),
    };

    public static int Run()
        => RunOneRow.Case("G10 no activity or interaction builds a spatial fact the senses already publish",
               NobodyRecomputesASharedFact);

    private static void NobodyRecomputesASharedFact()
    {
        string root = RepositoryRoot();
        string[] sources = ConsumerTrees
            .Select(tree => Path.Combine(root, tree))
            .Where(Directory.Exists)
            .SelectMany(tree => Directory.EnumerateFiles(tree, "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        // A premise, not a formality. A wrong root, a renamed folder or a move of these trees leaves the
        // sweep with nothing to read, and a rule that searched no files reports exactly what a held rule
        // reports — the failure `check-navigation-boundary.sh` met on a machine with no ripgrep.
        Require(sources.Length >= 20,
            $"the sweep found only {sources.Length} source files under {string.Join(" and ", ConsumerTrees)}, "
            + "which is too few to be those trees; a rule that read nothing reports what a held rule reports");

        var violations = new List<string>();
        foreach (string path in sources)
        {
            string[] lines = File.ReadAllLines(path);
            for (int index = 0; index < lines.Length; index++)
                foreach ((string pattern, string fact, string instead) in Refused)
                    if (lines[index].Contains(pattern, StringComparison.Ordinal))
                        violations.Add($"{Path.GetRelativePath(root, path)}:{index + 1} builds {fact} "
                            + $"({pattern.TrimEnd('(')}); {instead}");
        }

        EmitLedgerRows.Detail($"shared spatial facts: {sources.Length} consumer source files swept for "
            + $"{Refused.Length} private constructions, {violations.Count} found");
        Require(violations.Count == 0,
            "a domain answers a shared spatial question privately, so two domains can disagree about one world: "
            + string.Join(" | ", violations));
    }

    /// <summary>
    /// The repository root, found by walking up for the marker the tree's own scripts key on. It is derived
    /// rather than passed because this row is registered in the default table with no arguments, and it is a
    /// marker rather than a fixed number of parents because the instrument's working directory differs
    /// between `run-case.sh`, `verify.sh` and a hand run.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Companion", "Brain")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("no ancestor of the working directory holds Companion/Brain, "
                + "so the sweep has no sources to read and must not report a held boundary");
    }

    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
