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
    /// Each refused shape is a way of answering a shared question privately. <c>Type</c> is the declared name
    /// and <c>Pattern</c> is the text that appears at a call site; they are separate fields because the
    /// premise below needs the first and the sweep needs the second.
    ///
    /// <para><b>A file in this tree is named for what it does and its types are named for what they are, and
    /// the first version of this list forgot it.</b> The senses are declared <c>ReachSense</c>,
    /// <c>LightSense</c> and <c>PlayerIntentRegionSense</c> in files called <c>ObserveReach.cs</c>,
    /// <c>ObserveLight.cs</c> and <c>ObservePlayerIntentRegion.cs</c>, so three of six patterns were spelled
    /// from the filename, matched nothing anywhere, and made half this row a check that could not fail —
    /// which is the defect this lane's own third commit was about, committed again one commit later. The
    /// premise is what closes the class rather than the instance.</para>
    /// </summary>
    private static readonly (string Type, string Pattern, string Fact, string Instead)[] Refused =
    {
        ("PlayerIntentRegion", "PlayerIntentRegion.Around(", "the player's intent region", "read ctx.Senses.Intent.Region or .Regions"),
        ("PlayerIntentRegion", "new PlayerIntentRegion(", "the player's intent region", "read ctx.Senses.Intent.Region or .Regions"),
        ("PlayerIntentRegionSense", "new PlayerIntentRegionSense(", "the player's intent region", "read ctx.Senses.Intent"),
        ("ClearanceField", "new ClearanceField(", "the clearance field", "read ClearanceHeat, the one shared surface over it"),
        ("ReachSense", "new ReachSense(", "the reach flood", "read ctx.Senses.Reach"),
        ("LightSense", "new LightSense(", "the light field", "read ctx.Senses.Light"),
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

        // The premise that stops a dead pattern. A refused shape names a type, and a type that no longer
        // exists under that name cannot be constructed by anybody — so the pattern matches nothing, the row
        // passes, and half the rule is silently switched off. That is not hypothetical here: three of these
        // six were first spelled from their *file* names, which are verbs, where the declarations are nouns.
        // Asserting the declaration exists turns a rename into a red row rather than into lost coverage.
        string[] declarations = Directory
            .EnumerateFiles(Path.Combine(root, "Companion", "Brain", "Infrastructure"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();
        foreach (string type in Refused.Select(refused => refused.Type).Distinct())
            Require(declarations.Any(text => text.Contains($"class {type}", StringComparison.Ordinal)
                    || text.Contains($"struct {type}(", StringComparison.Ordinal)
                    || text.Contains($"struct {type}\n", StringComparison.Ordinal)),
                $"'{type}' is refused below and is declared nowhere under Companion/Brain/Infrastructure, so its "
                + "pattern matches nothing and that part of this rule is switched off rather than held");

        var violations = new List<string>();
        foreach (string path in sources)
        {
            string[] lines = File.ReadAllLines(path);
            for (int index = 0; index < lines.Length; index++)
                foreach ((string _, string pattern, string fact, string instead) in Refused)
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
