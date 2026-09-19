using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Owns one observation's pending native models and immutable result catalogue.
/// A frame advances queries; a different observed world gets a different owner.</summary>
public sealed class RetainCourseModelQueries
{
    private readonly ScheduleCourseModels travel;
    private readonly Dictionary<(int Slot, long Generation), CapturedContactEnemy> contactEnemies = new();
    private bool abandoned;
    public RetainCourseModelQueries(DecisionFactSnapshot snapshot, ITileWorld world, long capabilityRevision, int pendingCapacity)
    {
        Snapshot = snapshot;
        travel = new(world, capabilityRevision, pendingCapacity);
        if (snapshot.TryRead(CapturedContactCensus.Key, out var fact) && fact.Evidence == FactEvidence.Observed)
        {
            var census = JsonSerializer.Deserialize<CapturedContactCensus>(fact.Value.Text)
                ?? throw new System.InvalidOperationException("The captured contact census is empty.");
            if (snapshot.Tick < 0 || census.Tick != (ulong)snapshot.Tick || census.Enemies.Any(enemy => enemy.Motion.Tick != census.Tick))
                throw new System.InvalidOperationException("Contact inputs must share the frozen observation tick.");
            foreach (var enemy in census.Enemies) contactEnemies.Add((enemy.Slot, enemy.Generation), enemy);
        }
    }

    public DecisionFactSnapshot Snapshot { get; private set; }
    public int PendingCount => travel.PendingCount;
    public long CapacityRefusals => travel.CapacityRefusals;
    public long CompletedCount => travel.CompletedCount;

    /// <summary>Advances a suspended search and its requested models through the same
    /// allowance. Previously queued models run first so repeated binding cannot spend
    /// every tiny slice asking a question whose answer never gets computation time.</summary>
    public void ContinueSearch(SearchCourseOrders search, DecisionWorkBudget budget)
    {
        foreach (var request in search.RequiredTravel) RequestTravel(request);
        foreach (var request in search.RequiredEnemyMotion) RequestEnemyMotion(request);
        if (Continue(budget)) search.ExtendModelFacts(Snapshot);
        search.Continue(budget);
        // Requests discovered on the final operation remain queued for the next frame.
        // Capacity refusals leave requests on the search, where the next call retries them.
        foreach (var request in search.RequiredTravel) RequestTravel(request);
        foreach (var request in search.RequiredEnemyMotion) RequestEnemyMotion(request);
    }

    public bool RequestTravel(CourseTravelRequest request)
    {
        if (abandoned) throw new System.InvalidOperationException("An abandoned observation cannot request models.");
        return Snapshot.TryRead(request.Key, out _) || travel.Request(request);
    }

    public bool RequestEnemyMotion(CaptureEnemyCourseMotion query)
    {
        if (abandoned) throw new System.InvalidOperationException("An abandoned observation cannot request models.");
        // Validate even a cached key: entity generation and horizon do not identify
        // the observation in which that enemy's pose was captured.
        travel.ValidateEnemyMotion(query);
        return Snapshot.TryRead(query.Key, out _) || travel.RequestEnemyMotion(query);
    }

    public bool RequestEnemyMotion(CourseEnemyMotionRequest request)
    {
        if (abandoned) throw new System.InvalidOperationException("An abandoned observation cannot request models.");
        if (Snapshot.TryRead(request.Key, out _)) return true;
        contactEnemies.TryGetValue((request.Slot, request.Generation), out var enemy);
        return travel.RequestEnemyMotion(request, enemy);
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
