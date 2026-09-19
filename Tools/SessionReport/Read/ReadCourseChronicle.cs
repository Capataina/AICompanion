#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Course evidence is optional only in the sense that old captures predate it. A capture without
/// it has historical-unavailable coverage; it never means the companion selected no course.
/// </summary>
public static class ReadCourseChronicle
{
    private static readonly Version FirstSchema = new(0, 41, 0);

    public sealed record Result(string Coverage, IReadOnlyList<GodsEyeEvent> Events, IReadOnlyList<string> Problems);

    public static Result Read(Session session, string tsvPath)
    {
        string declared = session.Metadata.TryGetValue("schema", out string? value) ? value : "unlabelled";
        if (!Version.TryParse(declared, out Version? schema) || schema < FirstSchema)
            return new Result("historical-unavailable", Array.Empty<GodsEyeEvent>(), new[] { $"schema {declared} predates typed course evidence ({FirstSchema})" });
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(tsvPath);
        if (!log.Present) return new Result("explanatory-partial", Array.Empty<GodsEyeEvent>(), new[] { "course sidecar missing" });
        var events = log.Events.Where(e => e.kind.StartsWith("course-", StringComparison.Ordinal)).ToArray();
        var problems = new List<string>();
        if (!session.Metadata.TryGetValue("end", out string? end)) problems.Add("capture has no terminal writer evidence");
        else
        {
            var fields = end.Split(';');
            if (!fields.Contains("diagnostics-incomplete=False", StringComparer.Ordinal))
                problems.Add("diagnostic transport did not certify complete delivery");
            string? rows = fields.FirstOrDefault(field => field.StartsWith("rows=", StringComparison.Ordinal));
            if (rows == null || !long.TryParse(rows[5..], out long count) || count != session.Count + session.Ragged)
                problems.Add("terminal row count does not match the capture");
        }
        if (!log.Closed) problems.Add("sidecar has no normal close");
        if (log.Malformed != 0) problems.Add($"{log.Malformed} malformed sidecar line(s)");
        if (log.MissingSequences != 0) problems.Add($"{log.MissingSequences} missing sequence(s)");
        if (events.Length == 0) problems.Add("no typed course occurrence was recorded");
        if (events.Any(e => string.IsNullOrWhiteSpace(e.payload_kind) || e.payload_version is not > 0
            || string.IsNullOrWhiteSpace(e.phase) || e.observation_ordinal == null || e.receipt_watermark == null))
            problems.Add("a course occurrence lacks its typed envelope");
        bool sawSnapshot = false;
        foreach (GodsEyeEvent e in events)
        {
            if (e.payload_kind == "course-decision-snapshot" && e.snapshot is not { ValueKind: JsonValueKind.Object })
            { problems.Add("a decision snapshot event has no object snapshot"); continue; }
            if (e.snapshot is not { ValueKind: JsonValueKind.Object } json) continue;
            sawSnapshot = true;
            try
            {
                CourseDecisionSnapshot? snapshot = JsonSerializer.Deserialize<CourseDecisionSnapshot>(json.GetRawText());
                if (snapshot == null) { problems.Add("a decision snapshot is null"); continue; }
                if (snapshot.Context.SourceTick != e.tick || !string.Equals(snapshot.Context.NativePhase, e.phase, StringComparison.Ordinal)
                    || snapshot.Context.ObservationOrdinal != e.observation_ordinal || snapshot.Context.ReceiptWatermark != e.receipt_watermark)
                    problems.Add("decision snapshot context disagrees with its event envelope");
                CourseDecisionSnapshotCoverage coverage = CourseDecisionSnapshotCoverage.Assess(snapshot);
                if (!coverage.ExactInputComplete)
                    problems.Add("decision snapshot is incomplete: " + string.Join(", ", coverage.Missing.Concat(coverage.Mismatched).Concat(coverage.ExplicitlyUnavailable).Concat(coverage.Invalid)));
            }
            catch (Exception error) when (error is JsonException or ArgumentException or ArgumentNullException or NotSupportedException)
            { problems.Add("a decision snapshot is malformed"); }
        }
        if (!sawSnapshot) problems.Add("no decision snapshot was recorded; exact replay unavailable");
        return new Result(problems.Count == 0 ? "exact-input-complete" : "explanatory-partial", events, problems);
    }

    public static string Describe(Session session, string tsvPath)
    {
        Result result = Read(session, tsvPath);
        string issues = result.Problems.Count == 0 ? "" : "; " + string.Join("; ", result.Problems);
        return $"course evidence  {result.Coverage}; {result.Events.Count} typed occurrence(s){issues}\n";
    }
}
