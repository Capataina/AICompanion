#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Grants;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

/// <summary>
/// The one safety response that still takes the body: leaving water or lava. Everything else that keeps
/// the orb out of harm rides on the job instead of replacing it — a predicted hit bends the job's own
/// motion in <see cref="EvadeWhileMoving"/> — because the owner ruled on 15 September 2026 that safety is
/// applied on top of whatever the companion is doing and is never a job of its own. Leaving a liquid is the
/// exception for a reason that is not a preference: a body in water or lava is hurt every interval it
/// stays, and no job's spot is worth that, so escape suspends the activity and owns the feet until the body
/// is dry.
///
/// <para>Collision avoidance and combat spacing were responses here until then, and both suspended the
/// activity. Spacing searched for a low-exposure cell and, in the first play of the orb, held the body still
/// beside zombies for thirteen seconds while the player walked away, because its route to a spot was
/// dropped only when terrain changed under it and never when the spot stopped being safe. The property that
/// failed was the ownership itself — a response that owns the body makes the job wait on the response's own
/// ending — so both went rather than being patched, and a design that brings back a safety response for
/// enemies answers why the job has to stop for it.</para>
/// </summary>
public sealed class ChooseSafetyResponse
{
    public readonly ReachEnvironmentalSafety Escape = new();
    public long Id { get; private set; }
    public bool Active { get; private set; }
    public string Kind { get; private set; } = "none";
    public string Reason { get; private set; } = "inactive";
    public string LastEndReason { get; private set; } = "none";

    public bool TryChoose(in ActionContext ctx, out ActivityControlRequest request)
    {
        request = default;
        Escape.Refresh(ctx);
        if (Escape.NeedsResponse)
        {
            Begin(ctx, "environmental-escape");
            ctx.Companion.Brain.Chooser.Activity.Suspend(ctx, Kind);
            bool chosen = Escape.TryEscape(ctx, out Controls controls, out bool pending);
            Reason = chosen ? Escape.EscapeStage : pending ? "search-pending" : "no-safe-prefix";
            // A pending frontier survives this held packet. Calling Movement.Hold here would
            // erase the very search that needs another time slice.
            request = new ActivityControlRequest(controls, "survival-escape", ObserveProgress: true);
            return true;
        }
        if (Active) Cancel(ctx, "safe-state-observed");
        return false;
    }

    private void Begin(in ActionContext ctx, string kind)
    {
        if (Active && Kind == kind) return;
        if (Active) LastEndReason = "replaced-by-" + kind;
        // Retained controls belong to the response that proved their ending. A replacement
        // must not inherit a search frontier whose goal was a different kind of safety.
        ctx.Companion.Brain.Movement.CancelStateSearch();
        Id++;
        Active = true;
        Kind = kind;
    }

    public void Cancel(in ActionContext ctx, string reason)
    {
        if (Active) LastEndReason = reason;
        Active = false;
        Kind = "none";
        Reason = reason;
        Escape.Cancel(ctx);
    }
}
