#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>Cross-run coverage summary; it compares recorded evidence, never assumed equivalent worlds.</summary>
public static class MultiRunReport
{
    public static bool HasDefinitive(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (Program.Evaluate(Session.Load(path)).Findings.Any(f => f.Severity == Severity.Definitive))
                    return true;
            }
            catch (Exception)
            {
                // A selected capture that cannot be parsed is not proof of a gameplay defect,
                // but it is never a clean multi-run verdict.
                return true;
            }
        }
        return false;
    }

    public static string Of(IEnumerable<string> paths)
    {
        var text = new StringBuilder("multi-run diagnosis\n"); int count = 0;
        foreach (string path in paths)
        {
            text.Append($"\nrun  {Path.GetFileName(path)}\n");
            Session s;
            try { s = Session.Load(path); }
            catch (Exception error)
            {
                count++;
                text.Append($"  continuous samples=unreadable; coverage unavailable because the selected TSV could not be parsed: {error.Message}\n");
                continue;
            }
            count++;
            text.Append($"  continuous samples={s.Count:n0}");
            if (s.Count > 0) text.Append($" ticks={s.Tick(0):n0}..{s.Tick(s.Count - 1):n0}");
            text.Append('\n');
            // These readers deliberately retain their own coverage statements. A multi-run
            // comparison cannot promote an unrecorded field in one run to evidence because a
            // different run happened to capture it.
            text.Append(Indent(DescribeGodsEyeEvents.Of(path, false)));
            var diagnosis = Program.Evaluate(s);
            foreach (var finding in diagnosis.Findings)
            {
                text.Append($"  {finding.Severity}: {finding.Title} [ticks {finding.FirstTick}..{finding.LastTick}]\n");
                text.Append("    ").Append(finding.Detail).Append('\n');
            }
            foreach (var missing in diagnosis.Skipped)
                text.Append($"  unavailable: {missing.Name}; missing {missing.Missing}\n");
            text.Append(Indent(Chronicle.Of(s, full: false)));
        }
        return count == 0 ? text.Append("  no readable sessions\n").ToString() : text.ToString();
    }

    private static string Indent(string value)
        => string.Join("\n", value.Split('\n').Where(line => line.Length > 0).Select(line => "  " + line)) + "\n";
}
