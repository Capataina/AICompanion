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
    /// <summary>
    /// Records one course occurrence, auditing a decision against its contracts on the way through.
    ///
    /// The audit sits here rather than in the course owner because this is the seam a diagnostic is
    /// allowed to occupy: the owner decides, and this reads what it recorded. It also sits here
    /// rather than in the recorder's per-tick row, because the contracts are properties of a
    /// *decision* and the row is a property of a tick — a retained course carried for five hundred
    /// ticks is one decision, and a per-tick audit reports one contradiction five hundred times.
    ///
    /// The audit may return fields to append, which is how <c>target-evidence</c> and
    /// <c>target-evidence-age</c> reach a payload built in <c>Selection/</c>: a refused order records
    /// the count of its refusals and not the evidence the binder read, and the evidence is only
    /// reachable while the frozen observation is still in hand. Appending rather than rebuilding
    /// upstream keeps the decision's own record the course owner's and this addition Diagnostics'.
    /// </summary>
    public static bool Record(CourseTracePhase phase, CourseTraceContext context, CourseTracePayload payload)
    {
        // Nothing is audited while the recorder is off, because the audit's only output is an
        // occurrence nobody would write and two payload fields on a record nobody would keep. The
        // gate is here rather than inside the audit so that the delegate hop is not paid either: a
        // session with recording disabled runs this method to a single boolean read.
        IReadOnlyList<KeyValuePair<string, CourseTraceValue>> extra = GodsEyeEvents.Active
            ? AuditDecisionContracts.Observe(context, payload)
            : Array.Empty<KeyValuePair<string, CourseTraceValue>>();
        if (extra.Count > 0)
        {
            var fields = new List<KeyValuePair<string, CourseTraceValue>>(payload.Fields);
            foreach (var field in extra) if (!payload.Fields.ContainsKey(field.Key)) fields.Add(field);
            payload = new CourseTracePayload(payload.Kind, payload.Version, fields);
        }
        return GodsEyeEvents.RecordCourse(phase, context, payload, required: phase is CourseTracePhase.NativeReceipt or CourseTracePhase.Lifecycle, snapshot: null);
    }

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
