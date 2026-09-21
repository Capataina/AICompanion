#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

/// <summary>
/// Computational capacity, not a preference for the first objects discovered.
/// A full pinned cache refuses insertion visibly; it never evicts an executing object.
///
/// **A plain recency eviction is a preference for the loudest producer, which is what this store spent
/// its first life being.** Measured on 21 September 2026: six opportunity domains shared one store of
/// sixty-four, and in a dark area lighting minted a hundred and thirty-eight sites while mining, chopping
/// and collection minted one apiece. Every one of those three was evicted and lighting kept all
/// sixty-four, so the companion lit and could not discover a vein, a trunk or a drop at all — with the ore
/// in front of it, a pickaxe in its slot and the policy permitting. Nothing was red, because a domain with
/// no surviving candidate reports no admission group rather than a refusal.
///
/// So eviction is group-aware when a grouping is supplied: every group is guaranteed a floor of
/// <c>Capacity / groups</c>, and only what a group holds *above* its floor competes on recency. A group
/// that has not yet produced anything still has its floor waiting, which is what stops the order sources
/// happen to run in from deciding who exists. The floor is derived rather than tuned — it is the fair
/// share of the capacity the caller already chose — and <paramref name="groups"/> is the number of
/// producers rather than the number currently present, because sizing it to who has arrived lets the
/// first arrival claim everything, which is the defect wearing a smaller hat.
///
/// Supplying no grouping keeps the plain recency rule, for the stores whose entries are genuinely one
/// kind of thing and where a floor would be arbitrary.
/// </summary>
public sealed class DecisionStorage<TKey, TValue> where TKey : notnull
{
    private sealed record Entry(TValue Value, long Touch, bool Pinned);
    private readonly Dictionary<TKey, Entry> entries = new();
    private readonly Func<TKey, string>? group;
    private readonly int groups;
    private long touch;
    public DecisionStorage(int capacity) : this(capacity, null, 0) { }
    public DecisionStorage(int capacity, Func<TKey, string>? group, int groups)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (group != null && groups <= 0) throw new ArgumentOutOfRangeException(nameof(groups),
            "a grouped store needs the number of producers to divide its capacity among");
        Capacity = capacity;
        this.group = group;
        this.groups = groups;
    }
    /// <summary>How many entries one group holds before any of them may be evicted, zero when ungrouped.
    /// At least one, so a capacity smaller than the producer count still guarantees every producer a seat
    /// rather than silently returning to first-past-the-post.</summary>
    public int GroupFloor => group == null ? 0 : Math.Max(1, Capacity / groups);
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
            // Only what a group holds above its floor is evictable, so a prolific producer cannot take
            // another producer's last seat. The incoming key's own group is exempt from protection: an
            // arrival that is itself over its floor must be able to displace its own oldest rather than
            // reach across and take someone else's.
            int floor = GroupFloor;
            Dictionary<string, int>? held = null;
            if (group != null)
            {
                held = new(StringComparer.Ordinal);
                foreach (var pair in entries)
                {
                    string name = group(pair.Key);
                    held[name] = held.TryGetValue(name, out int count) ? count + 1 : 1;
                }
            }
            foreach (var pair in entries)
            {
                if (pair.Value.Pinned) continue;
                if (held != null && held[group!(pair.Key)] <= floor) continue;
                if (pair.Value.Touch >= age) continue;
                oldest = pair.Key; age = pair.Value.Touch; found = true;
            }
            // Every group at or under its floor and the store still full: fall back to plain recency, so
            // a store whose capacity cannot cover every floor still accepts work rather than refusing it.
            if (!found && held != null)
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
