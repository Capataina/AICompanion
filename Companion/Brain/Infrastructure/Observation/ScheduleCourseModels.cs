using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Owns bounded pending native model work for one observation owner. Completed
/// facts belong to that owner's frozen model catalogue, rather than a second result cache.</summary>
public sealed class ScheduleCourseModels
{
    private readonly ITileWorld world;
    private readonly long capabilityRevision;
    private readonly int observationTerrainRevision;
    private readonly CapturedCourseMotion motion;
    private readonly Dictionary<FactKey, Func<DecisionWorkBudget, DecisionFact?>> pending = new();
    private readonly Queue<FactKey> turns = new();

    public ScheduleCourseModels(ITileWorld world, long capabilityRevision, int capacity)
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
        var query = new CaptureCourseTravel(world, request.From, request.Velocity, request.To, capabilityRevision,
            motion, observationTerrainRevision);
        pending.Add(request.Key, budget => query.Continue(budget, maximumOperations: 1));
        turns.Enqueue(request.Key);
        return true;
    }

    public bool RequestEnemyMotion(CaptureEnemyCourseMotion query)
    {
        if (!query.BelongsTo(world, observationTerrainRevision))
            throw new InvalidOperationException("Enemy motion must be captured against the model owner's original terrain observation.");
        if (pending.ContainsKey(query.Key)) return true;
        if (pending.Count == Capacity) { CapacityRefusals++; return false; }
        pending.Add(query.Key, budget => query.Continue(budget, maximumOperations: 1));
        turns.Enqueue(query.Key);
        return true;
    }

    public IReadOnlyList<DecisionFact> Continue(DecisionWorkBudget budget)
    {
        var completed = new List<DecisionFact>();
        while (turns.Count > 0 && !budget.Exhausted)
        {
            var key = turns.Dequeue();
            var result = pending[key](budget);
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
