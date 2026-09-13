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
        string[] selected = paths.ToArray();
        var text = new StringBuilder("multi-run diagnosis\n"); int count = 0;
        // Provenance comes before any run is read beside another, so a pattern seen across runs is
        // never mistaken for a comparison of one build when the runs were recorded by different ones.
        text.Append(DescribeProvenance(selected));
        foreach (string path in selected)
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

    /// <summary>The recorded identity fields compared across runs; each is written by RecordBrainTelemetry.WriteMetadata.</summary>
    private static readonly string[] ProvenanceKeys = { "schema", "terraria", "tml_assembly", "runtime", "os", "mods" };

    /// <summary>
    /// Which recorded build, loader and mod identities the selected runs share and which they do not.
    /// A field a run did not record is named as unrecorded for that run and never filled from this
    /// checkout, because the checkout is not the build that recorded it.
    /// </summary>
    internal static string DescribeProvenance(IReadOnlyList<string> paths)
    {
        var text = new StringBuilder($"provenance  {paths.Count} run(s); recorded build and loader identity, compared before any run is read beside another\n");
        var runs = new List<(string Name, Dictionary<string, string> Fields)>();
        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            try { runs.Add((name, Provenance(Session.ReadMetadata(path)))); }
            catch (Exception error) { text.Append($"  unreadable  {name} — its metadata could not be read ({error.GetType().Name}), so its provenance is unknown\n"); }
        }
        bool anyDifference = false;
        foreach (string key in ProvenanceKeys)
        {
            var recorded = runs.Where(r => r.Fields.ContainsKey(key)).GroupBy(r => r.Fields[key], StringComparer.Ordinal).ToList();
            var unrecorded = runs.Where(r => !r.Fields.ContainsKey(key)).Select(r => r.Name).ToList();
            if (recorded.Count > 1)
            {
                anyDifference = true;
                text.Append($"  differs     {key}: ").Append(string.Join(" · ", recorded.Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Name))})"))).Append('\n');
            }
            else if (recorded.Count == 1)
                text.Append($"  shared      {key}={recorded[0].Key}").Append(unrecorded.Count > 0 ? $" among the {recorded[0].Count()} run(s) that record it\n" : "\n");
            if (unrecorded.Count > 0)
                text.Append($"  unrecorded  {key} in {string.Join(", ", unrecorded)} — unknown for those runs, not filled from this checkout\n");
        }
        if (anyDifference)
            text.Append("  The runs below were recorded by different builds or loaders; each keeps its own findings, and a pattern across them is not a comparison of one build.\n");
        return text.ToString();
    }

    /// <summary>
    /// Metadata with packed values expanded: <c>terraria=v1.4.4.9;tml_assembly=1.4.4.9;runtime=8.0.0;os=Unix</c>
    /// arrives as one key, so a value whose every later segment is itself <c>key=value</c> is split.
    /// The mods list (<c>ModLoader@…;AICompanion@…</c>) has no equals signs and stays whole.
    /// </summary>
    internal static Dictionary<string, string> Provenance(IReadOnlyDictionary<string, string> metadata)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            string[] parts = value.Split(';');
            if (parts.Length > 1 && parts.Skip(1).All(part => part.IndexOf('=') > 0))
            {
                fields.TryAdd(key, parts[0]);
                foreach (string part in parts.Skip(1))
                {
                    int equals = part.IndexOf('=');
                    fields.TryAdd(part[..equals], part[(equals + 1)..]);
                }
            }
            else
                fields.TryAdd(key, value);
        }
        return fields;
    }

    private static string Indent(string value)
        => string.Join("\n", value.Split('\n').Where(line => line.Length > 0).Select(line => "  " + line)) + "\n";
}
