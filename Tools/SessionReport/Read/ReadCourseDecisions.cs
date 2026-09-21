#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// One <c>course-decision</c> occurrence, read out of its typed payload.
///
/// <para><see cref="ReadCourseChronicle"/> is the gate above this one and they answer different
/// questions on purpose. That file asks whether the course evidence can be trusted at all — schema,
/// envelope, snapshot manifests, terminal closure — and returns coverage. This one asks what the
/// companion actually decided, tick by tick, and it is only worth reading once the gate says the
/// evidence is there. Keeping them apart is what stops a malformed sidecar from being narrated as a
/// story of decisions the companion never made.</para>
/// </summary>
public sealed record CourseDecision(long Tick, string Reason, string Activity, bool Settled, string Purpose,
    long Steps, long OrdersPriced, long OrdersRefused, bool SearchExhausted, string ReleaseReason, long Facts,
    IReadOnlyList<KeyValuePair<string, long>> Refusals)
{
    /// <summary>What a reader shows as the decision's identity: an empty order is companionship rather than idleness.</summary>
    public string What => Purpose.Length > 0 ? Purpose : Activity.Length > 0 ? Activity : "companionship";
}

/// <summary>
/// The decisions in a capture, with what could not be read counted rather than dropped silently.
/// </summary>
public sealed record CourseDecisionLog(IReadOnlyList<CourseDecision> Decisions, int Unreadable)
{
    public static readonly CourseDecisionLog Empty = new(Array.Empty<CourseDecision>(), 0);
}

public static class ReadCourseDecisions
{
    /// <summary>The producer's own payload kind, from <c>DecideCourseEachTick</c>.</summary>
    public const string Kind = "course-decision";

    /// <summary>The prefix the producer puts on every refusal tally it publishes beside the refused total.</summary>
    public const string RefusalPrefix = "refused:";

    /// <summary>The reason a decision carries when the tick kept the course it already had.</summary>
    public const string RetainedReason = "course-retained";

    public static CourseDecisionLog From(GodsEyeEventLog log)
    {
        if (!log.Present) return CourseDecisionLog.Empty;
        var decisions = new List<CourseDecision>();
        int unreadable = 0;
        foreach (GodsEyeEvent e in log.Events)
        {
            if (!string.Equals(e.payload_kind, Kind, StringComparison.Ordinal)) continue;
            if (e.payload is not { ValueKind: JsonValueKind.Object } payload
                || !payload.TryGetProperty("Fields", out JsonElement fields)
                || fields.ValueKind != JsonValueKind.Object)
            { unreadable++; continue; }
            try
            {
                var refusals = new List<KeyValuePair<string, long>>();
                foreach (JsonProperty field in fields.EnumerateObject())
                    if (field.Name.StartsWith(RefusalPrefix, StringComparison.Ordinal))
                        refusals.Add(new(field.Name[RefusalPrefix.Length..], Integer(fields, field.Name)));
                decisions.Add(new CourseDecision(e.tick,
                    Text(fields, "reason"), Text(fields, "activity"), Flag(fields, "settled"), Text(fields, "purpose"),
                    Integer(fields, "steps"), Integer(fields, "orders-priced"), Integer(fields, "orders-refused"),
                    Flag(fields, "search-exhausted"), Text(fields, "release-reason"), Integer(fields, "facts"),
                    refusals));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
            { unreadable++; }
        }
        return new CourseDecisionLog(decisions, unreadable);
    }

    /// <summary>
    /// A field's text, or empty where the producer did not write it. Absence is not an error here:
    /// the payload is versioned and an older version legitimately carries fewer fields, so a reader
    /// that threw on a missing name would refuse a whole capture over one added field.
    /// </summary>
    private static string Text(JsonElement fields, string name)
        => fields.TryGetProperty(name, out JsonElement value)
            && value.TryGetProperty("Text", out JsonElement text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? "" : "";

    private static long Integer(JsonElement fields, string name)
        => long.TryParse(Text(fields, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

    private static bool Flag(JsonElement fields, string name)
        => string.Equals(Text(fields, name), "true", StringComparison.Ordinal);
}
