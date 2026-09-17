#nullable enable

extern alias live;

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// The hold audit: commit the snapshot's shifted plan and run the commitment's own validation, so the
/// audit grades the hold — what the live brain would still stand by on the snapshot's tick — as well as
/// the search. Commit reseeds search-time state, so the stamped per-commitment state (the kills the plan
/// owns, where its progress clock had reached) is installed after it; deferrals outlive commitments, so
/// the restore stages those. The flood is grown like the exhaustive grid's, because an ungrown flood
/// reads every stand unreachable. Pre-stamp captures grade everything but the stall check: without the
/// running stamp the clock may have been paused, so the runner under-claims rather than reporting
/// stalls the live brain never saw.
/// </summary>
internal static class AuditHold
{
    public sealed record HoldVerdict(bool Holds, string Reason);

    public static HoldVerdict Revalidate(RestoredDecision restored)
    {
        if (restored.CommittedShifted == null)
            return new HoldVerdict(false, "no-committed-plan");
        var combat = restored.Companion.Combat;
        combat.Planner.Commit(restored.CommittedShifted);
        foreach (int[] hit in restored.Snapshot.Hits)
            if (hit.Length >= 2)
                combat.Planner.AssumeHit(hit[0], hit[1]);
        if (restored.Snapshot.ProgressTick >= 0)
            combat.Planner.AssumeProgress(restored.Snapshot.ProgressTick);
        RestoreSnapshot.GrowFlood(restored);
        bool holds = combat.Planner.Validate(restored.Ctx, restored.Companion.Brain.Positioner,
            RestoreSnapshot.AllowanceQuery(restored), restored.Snapshot.CombatRunning);
        return new HoldVerdict(holds, holds ? "valid" : combat.Planner.LastInvalidation);
    }
}
