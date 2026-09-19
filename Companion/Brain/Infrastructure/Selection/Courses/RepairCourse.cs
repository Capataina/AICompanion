#nullable enable

using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>Dirty roots and transitive time/resource/effect users are visited incrementally.
/// Pending repair never edits the published course in place.</summary>
public sealed class RepairCourse
{
    private readonly Queue<long> pending = new();
    private readonly HashSet<long> queued = new();
    private readonly HashSet<long> dirty = new();
    public IReadOnlyCollection<long> Dirty => dirty.OrderBy(x => x).ToArray();
    public int Pending => pending.Count;
    public string FirstReason { get; private set; } = "";
    public void Invalidate(CourseDependencyIndex index, FactKey fact, string reason)
    {
        foreach (long root in index.DirectUsers(fact)) Enqueue(root);
        if (pending.Count > 0 && FirstReason.Length == 0) FirstReason = reason;
    }
    public void Invalidate(long binding, string reason)
    {
        Enqueue(binding);
        if (FirstReason.Length == 0) FirstReason = reason;
    }
    private void Enqueue(long binding)
    {
        if (queued.Add(binding)) pending.Enqueue(binding);
    }
    public void Continue(CourseDependencyIndex index, DecisionWorkBudget budget)
    {
        while (pending.Count > 0 && budget.TrySpend("dependency-repair"))
        {
            long binding = pending.Dequeue();
            dirty.Add(binding);
            foreach (long child in index.Children(binding)) Enqueue(child);
        }
    }
    public bool IsDirty(long binding) => queued.Contains(binding);
    public void Published() { pending.Clear(); queued.Clear(); dirty.Clear(); FirstReason = ""; }
}
