#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Grants;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.SharedBehaviours.Safety;

/// <summary>Retains a physical safety response independently of the ordinary activity offers.
/// Environmental escape and collision avoidance share the same movement and grant boundary.</summary>
public sealed class ChooseSafetyResponse
{
    public readonly ReachEnvironmentalSafety Escape = new();
    public readonly CreateCombatSpace CombatSpace = new();
    public long Id { get; private set; }
    public bool Active { get; private set; }
    public string Kind { get; private set; } = "none";
    public string Reason { get; private set; } = "inactive";
    public string LastEndReason { get; private set; } = "none";

    public bool TryChoose(in ActionContext ctx, bool imminentCollision, out ActivityControlRequest request)
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
        // An orb has no landing to wait for: the response lasts exactly as long as a collision is predicted.
        if (imminentCollision)
        {
            Begin(ctx, "collision-avoidance");
            ctx.Companion.Brain.Chooser.Activity.Suspend(ctx, "combat-reflex");
            Reason = "predicted-collision";
            var movement = ctx.Companion.Brain.Movement;
            Controls controls = movement.AvoidThreats(ctx.Companion.Motor.State,
                movement.Navigator.UnsafeAtTick ?? ((_, _) => false), ctx.Senses.PlayerEntity.Center);
            request = new ActivityControlRequest(controls, "combat-reflex");
            return true;
        }
        bool retainSpacing = Active && Kind == "combat-spacing" && !CombatSpace.IsSatisfied(ctx);
        if (retainSpacing || CombatSpace.NeedsResponse(ctx))
        {
            Begin(ctx, "combat-spacing");
            bool chosen = CombatSpace.TryMove(ctx, out Controls controls);
            if (chosen || CombatSpace.Pending)
            {
                ctx.Companion.Brain.Chooser.Activity.Suspend(ctx, Kind);
                Reason = chosen ? "reducing-enemy-exposure" : "search-pending";
                request = new ActivityControlRequest(controls, Kind, ObserveProgress: true);
                return true;
            }
            Cancel(ctx, "combat-spacing-no-safe-prefix");
            return false;
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
