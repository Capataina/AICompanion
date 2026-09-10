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
        => paths.Select(Session.Load).Any(session => Program.Evaluate(session).Findings.Any(f => f.Severity == Severity.Definitive));

    public static string Of(IEnumerable<string> paths)
    {
        var text = new StringBuilder("multi-run diagnosis\n"); int count = 0;
        foreach (string path in paths)
        {
            Session s = Session.Load(path); count++;
            text.Append($"\nrun  {Path.GetFileName(path)}\n");
            text.Append($"  continuous samples={s.Count:n0}");
            if (s.Count > 0) text.Append($" ticks={s.Tick(0):n0}..{s.Tick(s.Count - 1):n0}");
            text.Append('\n');
            // These readers deliberately retain their own coverage statements. A multi-run
            // comparison cannot promote an unrecorded field in one run to evidence because a
            // different run happened to capture it.
            text.Append(Indent(DescribeGodsEyeEvents.Of(path, false)));
            text.Append(Indent(Chronicle.Of(s, full: false)));
        }
        return count == 0 ? text.Append("  no readable sessions\n").ToString() : text.ToString();
    }

    private static string Indent(string value)
        => string.Join("\n", value.Split('\n').Where(line => line.Length > 0).Select(line => "  " + line)) + "\n";
}
