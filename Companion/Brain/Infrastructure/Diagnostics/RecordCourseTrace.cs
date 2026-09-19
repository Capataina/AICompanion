#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>The native ordering point an occurrence was observed at; tick alone cannot order a hit and a decision.</summary>
public enum CourseTracePhase { Brain, NativeReceipt, Lifecycle }

/// <summary>A scalar diagnostic value. This deliberately has no <c>object</c> arm: live game objects are not immutable snapshots.</summary>
public readonly record struct CourseTraceValue(string Kind, string Text)
{
    public static CourseTraceValue TextValue(string value) => new("text", value);
    public static CourseTraceValue Integer(long value) => new("integer", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static CourseTraceValue Decimal(double value) => new("decimal", value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    public static CourseTraceValue Flag(bool value) => new("flag", value ? "true" : "false");
}

/// <summary>Known, data-only occurrence fields for course records that are not a complete decision snapshot.</summary>
public sealed class CourseTracePayload
{
    public string Kind { get; }
    public int Version { get; }
    public IReadOnlyDictionary<string, CourseTraceValue> Fields { get; }

    public CourseTracePayload(string kind, int version, IEnumerable<KeyValuePair<string, CourseTraceValue>> fields)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A payload kind is required.", nameof(kind));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        var copy = new Dictionary<string, CourseTraceValue>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, CourseTraceValue> pair in fields ?? throw new ArgumentNullException(nameof(fields)))
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("A payload field name is required.", nameof(fields));
            if (!copy.TryAdd(pair.Key, pair.Value)) throw new ArgumentException($"Duplicate payload field '{pair.Key}'.", nameof(fields));
        }
        Kind = kind; Version = version; Fields = new ReadOnlyDictionary<string, CourseTraceValue>(copy);
    }
}

/// <summary>Accepts immutable values only and leaves encoding and I/O to the bounded diagnostics worker.</summary>
public static class RecordCourseTrace
{
    public static bool Record(CourseTracePhase phase, CourseTraceContext context, CourseTracePayload payload)
        => GodsEyeEvents.RecordCourse(phase, context, payload, required: phase is CourseTracePhase.NativeReceipt or CourseTracePhase.Lifecycle, snapshot: null);

    public static bool RecordDecisionSnapshot(CourseDecisionSnapshot snapshot)
    {
        var payload = new CourseTracePayload("course-decision-snapshot", snapshot.PayloadVersion, new[]
        {
            new KeyValuePair<string, CourseTraceValue>("input-digest", CourseTraceValue.TextValue(snapshot.InputDigest)),
            new KeyValuePair<string, CourseTraceValue>("model-fingerprint", CourseTraceValue.TextValue(snapshot.ModelFingerprint)),
            new KeyValuePair<string, CourseTraceValue>("expected-read-count", CourseTraceValue.Integer(snapshot.ExpectedReads.Count)),
            new KeyValuePair<string, CourseTraceValue>("captured-read-count", CourseTraceValue.Integer(snapshot.CapturedReads.Count))
        });
        return GodsEyeEvents.RecordCourse(CourseTracePhase.Brain, snapshot.Context, payload, required: false, snapshot);
    }
}
