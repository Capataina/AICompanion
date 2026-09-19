#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

public readonly record struct FactKey(string Kind, string Identity, long Generation = 0) : IComparable<FactKey>
{
    public int CompareTo(FactKey other)
    {
        int result = StringComparer.Ordinal.Compare(Kind, other.Kind);
        if (result == 0) result = StringComparer.Ordinal.Compare(Identity, other.Identity);
        return result == 0 ? Generation.CompareTo(other.Generation) : result;
    }
    public override string ToString() => $"{Kind}:{Identity}:{Generation}";
}

public enum FactEvidence { Observed, Modelled, Unresolved, Missing }
public readonly record struct FactValue(double Amount = 0, double X = 0, double Y = 0, string Text = "");
public sealed record DecisionFact
{
    public DecisionFact(FactKey key, long version, FactValue value, FactEvidence evidence)
    {
        Key = key; Version = version; Value = value; Evidence = evidence;
        CanonicalValue = System.Text.Json.JsonSerializer.Serialize(new { Key, Version, Value, Evidence });
        Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalValue)));
    }
    public FactKey Key { get; }
    public long Version { get; }
    public FactValue Value { get; }
    public FactEvidence Evidence { get; }
    public string CanonicalValue { get; }
    public string Digest { get; }
}
public readonly record struct FactRead(FactKey Key, long Version, string Digest, FactEvidence Evidence);

/// <summary>Only captured values enter hypothetical evaluation. Missing reads are recorded,
/// so an incomplete capture cannot look like a complete decision about an empty world.</summary>
public sealed class DecisionFactSnapshot
{
    private readonly Dictionary<FactKey, DecisionFact> facts;
    public DecisionFactSnapshot(long id, long worldEpoch, long tick, long observationOrdinal,
        long receiptWatermark, IEnumerable<DecisionFact> facts)
    {
        Id = id; WorldEpoch = worldEpoch; Tick = tick;
        ObservationOrdinal = observationOrdinal; ReceiptWatermark = receiptWatermark;
        this.facts = facts.ToDictionary(f => f.Key);
        Facts = Array.AsReadOnly(this.facts.Values.OrderBy(f => f.Key).ToArray());
    }
    public long Id { get; }
    public long WorldEpoch { get; }
    public long Tick { get; }
    public long ObservationOrdinal { get; }
    public long ReceiptWatermark { get; }
    /// <summary>The frozen census, in deterministic key order. Discovery enumerates this
    /// captured catalogue; binding reads its values through Track so dependencies remain explicit.</summary>
    public IReadOnlyList<DecisionFact> Facts { get; }
    public bool TryRead(FactKey key, out DecisionFact fact) => facts.TryGetValue(key, out fact!);
    public TrackedFactReader Track() => new(this);

    /// <summary>Derived queries may finish after observation freezes. They can extend that
    /// catalogue, but cannot change its clock, receipts, observed world, or an existing answer.</summary>
    public bool IsModelExtensionOf(DecisionFactSnapshot previous)
        => Id == previous.Id && WorldEpoch == previous.WorldEpoch && Tick == previous.Tick
            && ObservationOrdinal == previous.ObservationOrdinal && ReceiptWatermark == previous.ReceiptWatermark
            && facts.Count > previous.facts.Count
            && previous.facts.All(pair => facts.TryGetValue(pair.Key, out var current) && current.Digest == pair.Value.Digest)
            && facts.Values.Where(fact => !previous.facts.ContainsKey(fact.Key))
                .All(fact => fact.Evidence is FactEvidence.Modelled or FactEvidence.Unresolved);
}

public sealed class TrackedFactReader
{
    private readonly DecisionFactSnapshot snapshot;
    private readonly Dictionary<FactKey, FactRead> reads = new();
    public TrackedFactReader(DecisionFactSnapshot snapshot) => this.snapshot = snapshot;
    public long SnapshotId => snapshot.Id;
    public long WorldEpoch => snapshot.WorldEpoch;
    public long Tick => snapshot.Tick;
    /// <summary>The immutable census may be enumerated to find domain candidates; every value a
    /// binder consumes still goes through <see cref="Read"/> so the manifest is complete.</summary>
    public IReadOnlyList<DecisionFact> Facts => snapshot.Facts;
    public DecisionFact Read(FactKey key)
    {
        if (!snapshot.TryRead(key, out var fact)) fact = new(key, -1, default, FactEvidence.Missing);
        reads[key] = new(key, fact.Version, fact.Digest, fact.Evidence);
        return fact;
    }
    public DependencyManifest Manifest() => new(reads.Values);
}

public sealed class DependencyManifest
{
    public DependencyManifest(IEnumerable<FactRead> reads)
    {
        var unique = new Dictionary<FactKey, FactRead>();
        foreach (var read in reads)
        {
            if (unique.TryGetValue(read.Key, out var previous) && previous != read)
                throw new ArgumentException($"Mixed snapshot versions for {read.Key}.", nameof(reads));
            unique[read.Key] = read;
        }
        Reads = Array.AsReadOnly(unique.Values.OrderBy(r => r.Key).ToArray());
    }
    public IReadOnlyList<FactRead> Reads { get; }
    public bool Complete => Reads.All(r => r.Evidence is FactEvidence.Observed or FactEvidence.Modelled);
    public IReadOnlyList<FactKey> Changed(DecisionFactSnapshot snapshot)
        => Array.AsReadOnly(Reads.Where(r => !snapshot.TryRead(r.Key, out var now)
            || now.Version != r.Version || now.Digest != r.Digest).Select(r => r.Key).ToArray());
    public static DependencyManifest Empty { get; } = new(Array.Empty<FactRead>());
}

/// <summary>Reverse dependency graph. The caller performs bounded traversal after marking
/// roots; a changed version dirties a forecast without declaring its binding forbidden.</summary>
public sealed class CourseDependencyIndex
{
    private readonly Dictionary<FactKey, HashSet<long>> factUsers = new();
    private readonly Dictionary<long, HashSet<long>> descendants = new();
    private readonly Dictionary<long, DependencyManifest> manifests = new();

    /// <summary>Builds the one causal graph used by repair. A binding enters before its
    /// effects, so every parent is already known and an effect-only fact read can dirty
    /// the later binding that depends on that effect.</summary>
    public static CourseDependencyIndex Build(CourseProjection projection)
    {
        var index = new CourseDependencyIndex();
        var ids = new HashSet<long>();
        var effectAvailability = new Dictionary<long, double>();
        foreach (long causeId in projection.CompletedCauses)
            if (!ids.Add(causeId)) throw new ArgumentException("Course node IDs must be globally unique.", nameof(projection));
        foreach (var effect in projection.OutstandingEffects)
        {
            if (!ids.Add(effect.Id)) throw new ArgumentException("Course node IDs must be globally unique.", nameof(projection));
            effectAvailability.Add(effect.Id, GuaranteedAvailability(effect));
        }
        foreach (var binding in projection.Steps)
        {
            if (!ids.Add(binding.Id)) throw new ArgumentException("Course node IDs must be globally unique.", nameof(projection));
            foreach (var effect in binding.Effects)
            {
                if (!ids.Add(effect.Id)) throw new ArgumentException("Course node IDs must be globally unique.", nameof(projection));
                effectAvailability.Add(effect.Id, GuaranteedAvailability(effect));
            }
        }
        foreach (long causeId in projection.CompletedCauses)
            index.Register(causeId, DependencyManifest.Empty, Array.Empty<long>());
        foreach (var effect in projection.OutstandingEffects)
        {
            var parents = effect.Parents.Where(index.manifests.ContainsKey).ToArray();
            if (parents.Length != effect.Parents.Count)
                throw new ArgumentException("Outstanding effect parents must precede their child in causal order.", nameof(projection));
            if (effect.Evidence != EstimateStatus.Unresolved
                && parents.Any(parent => effectAvailability.TryGetValue(parent, out double available) && available > effect.EarliestTick))
                throw new ArgumentException("Outstanding effects cannot precede a parent's guaranteed outcome.", nameof(projection));
            index.Register(effect.Id, effect.Dependencies, parents);
        }
        double bindingStart = projection.ProjectionStartTick;
        foreach (var binding in projection.Steps)
        {
            if (!double.IsFinite(bindingStart))
                throw new ArgumentException("Course binding time must remain finite.", nameof(projection));
            index.Register(binding.Id, binding.Dependencies, binding.Parents);
            if (binding.Parents.Any(parent => effectAvailability.TryGetValue(parent, out double due) && due > bindingStart))
                throw new ArgumentException("A binding cannot consume a future effect.", nameof(projection));
            foreach (var effect in binding.Effects.OrderBy(effect => effect.NominalTick).ThenBy(effect => effect.Id))
            {
                if (effect.EarliestTick < bindingStart)
                    throw new ArgumentException("An effect cannot precede its owning binding.", nameof(projection));
                var parents = effect.Parents.Append(binding.Id).ToArray();
                if (effect.Parents.Any(parent => !index.manifests.ContainsKey(parent)))
                    throw new ArgumentException("Effect parents must precede their child in causal order.", nameof(projection));
                if (effect.Parents.Any(parent => effectAvailability.TryGetValue(parent, out double due) && due > effect.EarliestTick))
                    throw new ArgumentException("Effect parents must be due before their child.", nameof(projection));
                index.Register(effect.Id, effect.Dependencies, parents);
            }
            bindingStart += binding.TravelTicks + binding.UseTicks;
        }
        return index;
    }

    private static double GuaranteedAvailability(PredictedEffect effect)
        => effect.Evidence is EstimateStatus.NativeBound or EstimateStatus.ModelBound && double.IsFinite(effect.LatestTick)
            ? effect.LatestTick : double.PositiveInfinity;

    public void Register(long binding, DependencyManifest manifest, IEnumerable<long> parents)
    {
        var parentSet = parents.ToHashSet();
        foreach (long parent in parentSet)
        {
            if (parent == binding || !manifests.ContainsKey(parent))
                throw new ArgumentException("A binding needs previously registered, distinct parents.", nameof(parents));
            var pending = new Stack<long>(); pending.Push(binding);
            var visited = new HashSet<long>();
            while (pending.TryPop(out long node))
            {
                if (!visited.Add(node)) continue;
                if (node == parent) throw new ArgumentException("Binding dependencies must remain acyclic.", nameof(parents));
                if (descendants.TryGetValue(node, out var children))
                    foreach (long child in children) pending.Push(child);
            }
        }
        // Replacing a node's reads must preserve the downstream bindings which still
        // depend on it. Only its own incoming edges and old fact reads are replaced.
        RemoveInputs(binding);
        manifests[binding] = manifest;
        foreach (var read in manifest.Reads)
        {
            if (!factUsers.TryGetValue(read.Key, out var users)) factUsers[read.Key] = users = new();
            users.Add(binding);
        }
        foreach (long parent in parentSet)
        {
            if (parent == binding) throw new ArgumentException("A binding cannot depend on itself.", nameof(parents));
            if (!descendants.TryGetValue(parent, out var children)) descendants[parent] = children = new();
            children.Add(binding);
        }
    }
    public IEnumerable<long> DirectUsers(FactKey key) => factUsers.TryGetValue(key, out var set)
        ? set.OrderBy(x => x).ToArray() : Array.Empty<long>();
    public IEnumerable<long> Children(long binding) => descendants.TryGetValue(binding, out var set)
        ? set.OrderBy(x => x).ToArray() : Array.Empty<long>();
    public void Remove(long binding)
    {
        RemoveInputs(binding);
        descendants.Remove(binding);
    }
    private void RemoveInputs(long binding)
    {
        if (manifests.Remove(binding, out var previous))
            foreach (var read in previous.Reads)
                if (factUsers.TryGetValue(read.Key, out var users))
                { users.Remove(binding); if (users.Count == 0) factUsers.Remove(read.Key); }
        foreach (var children in descendants.Values) children.Remove(binding);
    }
    public void Clear() { factUsers.Clear(); descendants.Clear(); manifests.Clear(); }
}
