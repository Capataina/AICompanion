#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The source-owned identity attached to a diagnostic occurrence.  It contains values only: a
/// producer must not hand a mutable brain, Terraria entity, or live collection to the recorder,
/// because the diagnostics worker can serialize after the tick that created the occurrence.
/// </summary>
public sealed record CourseTraceContext(
    long SourceTick,
    string NativePhase,
    long CourseId,
    long CourseRevision,
    long StepId,
    long BindingId,
    long AttemptId,
    string Producer,
    long ObservationOrdinal,
    long ReceiptWatermark,
    string WorldEpoch,
    string SourceRevision,
    string PolicyFingerprint,
    string ConfigurationFingerprint);

/// <summary>One tracked read the decision says it needed for exact reconstruction.</summary>
/// <param name="Value">The canonical, immutable fact value. A digest alone cannot reproduce a decision.</param>
public sealed record CourseManifestEntry(string Key, long Version, string Digest, string Status, string? Value = null);

/// <summary>
/// A frozen, data-only decision input.  The manifest lists every tracked read expected by the
/// decision and every captured read; <see cref="CourseDecisionSnapshotCoverage"/> compares them
/// without treating an absent key as a negative fact.
/// </summary>
public sealed class CourseDecisionSnapshot
{
    public int PayloadVersion { get; }
    public CourseTraceContext Context { get; }
    public string InputDigest { get; }
    public string ModelFingerprint { get; }
    public string SchedulerState { get; }
    public string RandomState { get; }
    public IReadOnlyList<CourseManifestEntry> ExpectedReads { get; }
    public IReadOnlyList<CourseManifestEntry> CapturedReads { get; }
    public IReadOnlyList<string> MissingOrEvictedKeys { get; }

    public static CourseDecisionSnapshot Create(CourseTraceContext context, string modelFingerprint,
        string schedulerState, string randomState, IReadOnlyList<CourseManifestEntry> expectedReads,
        IReadOnlyList<CourseManifestEntry> capturedReads, IReadOnlyList<string>? missingOrEvictedKeys = null)
    {
        var frozen = new CourseDecisionSnapshot(1, context, "pending-digest", modelFingerprint, schedulerState,
            randomState, expectedReads, capturedReads, missingOrEvictedKeys);
        return new(1, context, CourseDecisionSnapshotCoverage.InputDigest(frozen), modelFingerprint,
            schedulerState, randomState, frozen.ExpectedReads, frozen.CapturedReads, frozen.MissingOrEvictedKeys);
    }

    public CourseDecisionSnapshot(int payloadVersion, CourseTraceContext context, string inputDigest,
        string modelFingerprint, string schedulerState, string randomState,
        IReadOnlyList<CourseManifestEntry> expectedReads, IReadOnlyList<CourseManifestEntry> capturedReads,
        IReadOnlyList<string>? missingOrEvictedKeys = null)
    {
        if (payloadVersion <= 0) throw new ArgumentOutOfRangeException(nameof(payloadVersion));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        PayloadVersion = payloadVersion;
        InputDigest = Required(inputDigest, nameof(inputDigest));
        ModelFingerprint = Required(modelFingerprint, nameof(modelFingerprint));
        SchedulerState = Required(schedulerState, nameof(schedulerState));
        RandomState = Required(randomState, nameof(randomState));
        ExpectedReads = Freeze(expectedReads, nameof(expectedReads));
        CapturedReads = Freeze(capturedReads, nameof(capturedReads));
        MissingOrEvictedKeys = Freeze(missingOrEvictedKeys ?? Array.Empty<string>(), nameof(missingOrEvictedKeys));
    }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A diagnostic identity cannot be blank.", name) : value;
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values, string name)
    {
        var copy = new List<T>(values ?? throw new ArgumentNullException(name));
        if (copy.Any(value => value is null)) throw new ArgumentException("A diagnostic manifest cannot contain null entries.", name);
        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>The precise result of comparing a snapshot's required reads with its captured values.</summary>
public sealed record CourseDecisionSnapshotCoverage(bool ExactInputComplete, IReadOnlyList<string> Missing,
    IReadOnlyList<string> Mismatched, IReadOnlyList<string> ExplicitlyUnavailable, IReadOnlyList<string> Invalid)
{
    public static CourseDecisionSnapshotCoverage Assess(CourseDecisionSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var captured = new Dictionary<string, CourseManifestEntry>(StringComparer.Ordinal);
        var expectedKeys = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>(); var mismatch = new List<string>(); var unavailable = new List<string>(); var invalid = new List<string>();
        if (snapshot.PayloadVersion != 1) invalid.Add("unsupported snapshot version");
        if (!string.Equals(snapshot.InputDigest, InputDigest(snapshot), StringComparison.OrdinalIgnoreCase)) invalid.Add("input digest");
        foreach (CourseManifestEntry entry in snapshot.CapturedReads)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value)) { invalid.Add(string.IsNullOrWhiteSpace(entry.Key) ? "captured key" : entry.Key); continue; }
            if (!captured.TryAdd(entry.Key, entry)) invalid.Add($"duplicate captured read {entry.Key}");
        }
        if (string.IsNullOrWhiteSpace(snapshot.Context.WorldEpoch) || string.IsNullOrWhiteSpace(snapshot.Context.SourceRevision)
            || string.IsNullOrWhiteSpace(snapshot.Context.PolicyFingerprint) || string.IsNullOrWhiteSpace(snapshot.Context.ConfigurationFingerprint)
            || string.IsNullOrWhiteSpace(snapshot.ModelFingerprint) || string.IsNullOrWhiteSpace(snapshot.SchedulerState)
            || string.IsNullOrWhiteSpace(snapshot.RandomState) || string.IsNullOrWhiteSpace(snapshot.InputDigest)) invalid.Add("snapshot identity/model/config/scheduler/random state");
        foreach (CourseManifestEntry expected in snapshot.ExpectedReads)
        {
            if (!string.Equals(expected.Status, "expected", StringComparison.Ordinal)) invalid.Add("expected read status");
            if (string.IsNullOrWhiteSpace(expected.Key) || string.IsNullOrWhiteSpace(expected.Digest)) { invalid.Add(string.IsNullOrWhiteSpace(expected.Key) ? "expected key" : expected.Key); continue; }
            if (!expectedKeys.Add(expected.Key)) { invalid.Add($"duplicate expected read {expected.Key}"); continue; }
            if (!captured.TryGetValue(expected.Key, out CourseManifestEntry? actual)) { missing.Add(expected.Key); continue; }
            if (!string.Equals(actual.Status, "captured", StringComparison.Ordinal)) { unavailable.Add(expected.Key); continue; }
            if (actual.Version != expected.Version || !string.Equals(actual.Digest, expected.Digest, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(actual.Value)
                || !string.Equals(Digest(actual.Value!), actual.Digest, StringComparison.OrdinalIgnoreCase)) mismatch.Add(expected.Key);
        }
        foreach (string key in captured.Keys)
            if (!expectedKeys.Contains(key)) invalid.Add($"unexpected captured read {key}");
        foreach (string key in snapshot.MissingOrEvictedKeys) if (!missing.Contains(key, StringComparer.Ordinal)) unavailable.Add(key);
        if (snapshot.ExpectedReads.Count == 0 || snapshot.CapturedReads.Count == 0) invalid.Add("empty expected or captured manifest");
        return new CourseDecisionSnapshotCoverage(missing.Count == 0 && mismatch.Count == 0 && unavailable.Count == 0 && invalid.Count == 0,
            new ReadOnlyCollection<string>(missing), new ReadOnlyCollection<string>(mismatch), new ReadOnlyCollection<string>(unavailable), new ReadOnlyCollection<string>(invalid));
    }
    public static string InputDigest(CourseDecisionSnapshot snapshot) => Digest(System.Text.Json.JsonSerializer.Serialize(new
    {
        snapshot.PayloadVersion, snapshot.Context, snapshot.ModelFingerprint, snapshot.SchedulerState, snapshot.RandomState,
        Expected = snapshot.ExpectedReads.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray(),
        Captured = snapshot.CapturedReads.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray(),
        Missing = snapshot.MissingOrEvictedKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray()
    }));
    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
