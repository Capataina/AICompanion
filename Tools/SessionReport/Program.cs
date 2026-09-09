#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Reads a playtest's record and says what is wrong with it, sorted by how sure it is: definitive
/// issues that the design or the record's own arithmetic rules out, potential issues that are wrong
/// in every situation anyone has thought of, and oddities that are shapes in the numbers with no
/// rule behind them yet.
///
/// It exists because the record was never the problem and the reading was: a session is eleven
/// thousand rows of eighty-odd columns, every diagnosis this project has made from one was a
/// hand-rolled column sum, and the columns move whenever the brain grows a new fact — which on
/// 2026-09-09 produced a mean distance of three and a half thousand tiles from an index that had
/// shifted by two. Every check here is grown from a defect that actually happened, and each one
/// names the columns it needs so a file written before those columns exist reads as reduced
/// coverage rather than as a clean run.
///
///   dotnet run --project Tools/SessionReport -- Telemetry/&lt;stamp&gt;.tsv
///   dotnet run --project Tools/SessionReport -- Telemetry          # the newest session in the folder
///
/// Exit code 0 when nothing definitive was found, 1 when something was, so the run is a check and
/// not only a report.
/// </summary>
public static class Program
{
    private static readonly ICheck[] Checks =
    {
        // The instrument first: a finding here means the rest of the file is not yet evidence.
        new TicksAdvance(),
        new ReturnableFitsInsideReach(),
        new ColumnsHoldWhatTheyClaim(),
        // Then the body, the fight and the choices.
        new TheBodyMovesWhenDriven(),
        new ProvenMovesTakeTheirProvenTime(),
        new BeingUnableToReachHimGetsNoticed(),
        new DamageArrivesWhereDangerWasSeen(),
        new TheHandsWorkWhileThreatened(),
        new TheChosenWeaponIsTheBetterOne(),
        new TheCompanionStaysUp(),
        new HuntingStaysOnHisScreen(),
        new TheActionBoardGetsUsed(),
        new TheTorchGivesUpTheHand(),
        new TheBrainFitsInAFrame(),
    };

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: dotnet run --project Tools/SessionReport -- <session.tsv | Telemetry folder>");
            return 2;
        }

        string? path = Resolve(args[0]);
        if (path == null)
        {
            Console.Error.WriteLine($"no session file at or under {args[0]}");
            return 2;
        }

        Session session;
        try
        {
            session = Session.Load(path);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{path}: {e.Message}");
            return 2;
        }

        Console.Write(DescribeSession.Of(session));

        var findings = new List<Finding>();
        var skipped = new List<(string Name, string Missing)>();
        int ran = 0;
        foreach (ICheck check in Checks)
        {
            string[] missing = check.Needs.Where(n => !session.Has(n)).ToArray();
            if (missing.Length > 0)
            {
                skipped.Add((check.Name, string.Join(", ", missing)));
                continue;
            }
            ran++;
            try
            {
                findings.AddRange(check.Run(session));
            }
            catch (Exception e)
            {
                // A check that throws is a broken check, and a broken check that silently returns
                // nothing is indistinguishable from a clean run, which is the failure this tool is
                // built to remove. So it reports itself as a finding against the reader.
                findings.Add(new Finding(Severity.Potential, check.Name,
                    $"the check itself failed: {e.GetType().Name}",
                    $"{e.Message}. Nothing was measured for this question, so treat it as no coverage rather than as "
                        + "a clean result.",
                    0, 0, 0));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"coverage  {ran} of {Checks.Length} checks ran");
        foreach (var (name, missing) in skipped)
            Console.WriteLine($"  skipped  {name}  — the file has no {missing}");

        foreach (Severity severity in new[] { Severity.Definitive, Severity.Potential, Severity.Oddity })
        {
            var group = Trim(findings.Where(f => f.Severity == severity)
                                     .OrderByDescending(f => f.Rows)
                                     .ToArray());
            Console.WriteLine();
            Console.WriteLine($"{Label(severity)}  ({group.Count})");
            if (group.Count == 0)
            {
                Console.WriteLine("  nothing");
                continue;
            }
            foreach (Finding finding in group)
            {
                string where = finding.FirstTick == finding.LastTick
                    ? $"tick {finding.FirstTick:n0}"
                    : $"ticks {finding.FirstTick:n0}..{finding.LastTick:n0}";
                Console.WriteLine();
                Console.WriteLine($"  {finding.Title}");
                Console.WriteLine($"    where  {where}");
                Console.WriteLine($"    asked  {finding.Check}");
                foreach (string line in Wrap(finding.Detail, 92))
                    Console.WriteLine($"    {line}");
            }
        }

        int definitive = findings.Count(f => f.Severity == Severity.Definitive);
        Console.WriteLine();
        Console.WriteLine(definitive == 0
            ? "no definitive issue in this session."
            : $"{definitive} definitive issue(s): something in this session is wrong by construction.");
        return definitive == 0 ? 0 : 1;
    }

    /// <summary>How many times one check may say the same thing before the rest become a count.</summary>
    private const int RepeatsShown = 3;

    /// <summary>
    /// The worst few of each check's findings, with the rest folded into one line. A condition that
    /// held five times in a session is one defect that recurred, and printing its paragraph five
    /// times moves the reading cost rather than removing it — which is the failure a reader built to
    /// replace hand-grepping must not commit itself.
    /// </summary>
    private static List<Finding> Trim(Finding[] group)
    {
        var kept = new List<Finding>();
        foreach (var byCheck in group.GroupBy(f => f.Check))
        {
            var ordered = byCheck.ToArray();
            kept.AddRange(ordered.Take(RepeatsShown));
            if (ordered.Length <= RepeatsShown)
                continue;
            var rest = ordered.Skip(RepeatsShown).ToArray();
            kept.Add(new Finding(
                ordered[0].Severity,
                byCheck.Key,
                $"and {rest.Length} more of the same, the largest {rest[0].Rows:n0} rows",
                $"Ticks {string.Join(", ", rest.Select(f => $"{f.FirstTick:n0}..{f.LastTick:n0}"))}. "
                    + "Same check, same shape; the three above carry the reasoning.",
                rest[0].FirstTick, rest[^1].LastTick, rest.Sum(f => f.Rows)));
        }
        return kept.OrderByDescending(f => f.Rows).ToList();
    }

    /// <summary>A file as given, or the newest .tsv in a folder, so a report is one command after a playtest.</summary>
    private static string? Resolve(string argument)
    {
        if (File.Exists(argument))
            return argument;
        if (!Directory.Exists(argument))
            return null;
        return Directory.EnumerateFiles(argument, "*.tsv")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new List<string>();
        int length = 0;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (length > 0 && length + 1 + word.Length > width)
            {
                yield return string.Join(' ', line);
                line.Clear();
                length = 0;
            }
            line.Add(word);
            length += (length > 0 ? 1 : 0) + word.Length;
        }
        if (line.Count > 0)
            yield return string.Join(' ', line);
    }

    private static string Label(Severity severity) => severity switch
    {
        Severity.Definitive => "DEFINITIVE ISSUES",
        Severity.Potential => "POTENTIAL ISSUES",
        _ => "ODDITIES AND BASELINES",
    };
}
