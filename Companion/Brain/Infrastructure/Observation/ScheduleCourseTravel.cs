using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Owns bounded pending native travel work for one observation owner. Completed
/// facts belong to that owner's frozen model catalogue, rather than a second result cache.</summary>
public sealed class ScheduleCourseTravel
{
    private readonly ITileWorld world;
    private readonly long capabilityRevision;
    private readonly int observationTerrainRevision;
    private readonly CapturedCourseMotion motion;
    private readonly Dictionary<FactKey, CaptureCourseTravel> pending = new();
    private readonly Queue<FactKey> turns = new();

    public ScheduleCourseTravel(ITileWorld world, long capabilityRevision, int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.world = world; this.capabilityRevision = capabilityRevision; Capacity = capacity;
        observationTerrainRevision = world.Revision; motion = CapturedCourseMotion.Current;
    }
    public int Capacity { get; }
    public int PendingCount => pending.Count;
    public long CapacityRefusals { get; private set; }
    public long CompletedCount { get; private set; }

    /// <summary>Call only for a fact absent from the owner's catalogue. Duplicate pending
    /// requests retain their queue position; capacity pressure never evicts unfinished work.</summary>
    public bool Request(CourseTravelRequest request)
    {
        if (pending.ContainsKey(request.Key)) return true;
        if (pending.Count == Capacity) { CapacityRefusals++; return false; }
        pending.Add(request.Key, new(world, request.From, request.Velocity, request.To, capabilityRevision,
            motion, observationTerrainRevision));
        turns.Enqueue(request.Key);
        return true;
    }

    public IReadOnlyList<DecisionFact> Continue(DecisionWorkBudget budget)
    {
        var completed = new List<DecisionFact>();
        while (turns.Count > 0 && !budget.Exhausted)
        {
            var key = turns.Dequeue();
            var result = pending[key].Continue(budget, maximumOperations: 1);
            if (result == null) turns.Enqueue(key);
            else
            {
                pending.Remove(key); completed.Add(result); CompletedCount++;
            }
        }
        return completed.AsReadOnly();
    }

    /// <summary>The owner resets on world replacement or abandonment of this observation.
    /// An ordinary frame or completed query never resets another pending frontier.</summary>
    public void Clear()
    {
        pending.Clear(); turns.Clear();
    }
}
