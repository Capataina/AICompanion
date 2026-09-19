using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Owns one observation's pending native models and immutable result catalogue.
/// A frame advances queries; a different observed world gets a different owner.</summary>
public sealed class RetainCourseModelQueries
{
    private readonly ScheduleCourseTravel travel;
    private bool abandoned;
    public RetainCourseModelQueries(DecisionFactSnapshot snapshot, ITileWorld world, long capabilityRevision, int pendingCapacity)
    {
        Snapshot = snapshot;
        travel = new(world, capabilityRevision, pendingCapacity);
    }

    public DecisionFactSnapshot Snapshot { get; private set; }
    public int PendingCount => travel.PendingCount;
    public long CapacityRefusals => travel.CapacityRefusals;
    public long CompletedCount => travel.CompletedCount;

    public bool RequestTravel(CourseTravelRequest request)
    {
        if (abandoned) throw new System.InvalidOperationException("An abandoned observation cannot request models.");
        return Snapshot.TryRead(request.Key, out _) || travel.Request(request);
    }

    /// <summary>Publishes a new immutable catalogue only when models complete. The previous
    /// snapshot remains unchanged and the extension preserves all original observation facts.</summary>
    public bool Continue(DecisionWorkBudget budget)
    {
        if (abandoned) throw new System.InvalidOperationException("An abandoned observation cannot advance models.");
        var completed = travel.Continue(budget);
        if (completed.Count == 0) return false;
        var next = new DecisionFactSnapshot(Snapshot.Id, Snapshot.WorldEpoch, Snapshot.Tick, Snapshot.ObservationOrdinal,
            Snapshot.ReceiptWatermark, Snapshot.Facts.Concat(completed));
        if (!next.IsModelExtensionOf(Snapshot))
            throw new System.InvalidOperationException("Native query completion changed the frozen observation catalogue.");
        Snapshot = next;
        return true;
    }

    public void Abandon() { travel.Clear(); abandoned = true; }
}
