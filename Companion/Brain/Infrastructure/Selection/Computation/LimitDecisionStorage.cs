#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

/// <summary>Computational capacity, not a preference for the first objects discovered.
/// A full pinned cache refuses insertion visibly; it never evicts an executing object.</summary>
public sealed class DecisionStorage<TKey, TValue> where TKey : notnull
{
    private sealed record Entry(TValue Value, long Touch, bool Pinned);
    private readonly Dictionary<TKey, Entry> entries = new();
    private long touch;
    public DecisionStorage(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }
    public int Capacity { get; }
    public int Count => entries.Count;
    public long Evictions { get; private set; }
    public long Refused { get; private set; }
    public event Action<TKey>? Evicted;
    public bool Put(TKey key, TValue value, bool pinned = false)
    {
        if (entries.TryGetValue(key, out var existing))
        {
            entries[key] = new(value, ++touch, pinned || existing.Pinned);
            return true;
        }
        if (entries.Count == Capacity)
        {
            TKey oldest = default!;
            long age = long.MaxValue;
            bool found = false;
            foreach (var pair in entries)
                if (!pair.Value.Pinned && pair.Value.Touch < age)
                { oldest = pair.Key; age = pair.Value.Touch; found = true; }
            if (!found) { Refused++; return false; }
            entries.Remove(oldest);
            Evictions++;
            Evicted?.Invoke(oldest);
        }
        entries.Add(key, new(value, ++touch, pinned));
        return true;
    }
    public bool TryGet(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var entry)) { value = default!; return false; }
        entries[key] = entry with { Touch = ++touch };
        value = entry.Value;
        return true;
    }
    public void Pin(TKey key, bool pinned)
    {
        if (entries.TryGetValue(key, out var entry)) entries[key] = entry with { Pinned = pinned };
    }
    public bool Remove(TKey key) => entries.Remove(key);
    public void Clear() => entries.Clear();
}
