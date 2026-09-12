#nullable enable

using AICompanion.Companion.Brain.ActivityCoordination;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.SharedSafety;

/// <summary>Retains a physical safety response independently of the ordinary activity offers.
/// Environmental escape and collision avoidance share the same movement and grant boundary.</summary>
public sealed class ChooseSafetyResponse
{
    public readonly ReachEnvironmentalSafety Escape = new();
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
            Begin("environmental-escape");
            ctx.Companion.Brain.Chooser.Activity.Suspend(ctx, Kind);
            bool chosen = Escape.TryEscape(ctx, out Controls controls, out bool pending);
            Reason = chosen ? Escape.EscapeStage : pending ? "search-pending" : "no-safe-prefix";
            // A pending frontier survives this held packet. Calling Movement.Hold here would
            // erase the very search that needs another time slice.
            request = new ActivityControlRequest(controls, "survival-escape", ObserveProgress: true);
            return true;
        }
        if (imminentCollision || Active && Kind == "collision-avoidance" && !ctx.Companion.Motor.State.OnGround)
        {
            Begin("collision-avoidance");
            ctx.Companion.Brain.Chooser.Activity.Suspend(ctx, "combat-reflex");
            Reason = imminentCollision ? "predicted-collision" : "awaiting-landing";
            var movement = ctx.Companion.Brain.Movement;
            Controls controls = movement.AvoidThreats(ctx.Companion.Motor.State,
                movement.Navigator.UnsafeAtTick ?? ((_, _) => false), ctx.Senses.Player.Bottom);
            request = new ActivityControlRequest(controls, "combat-reflex");
            return true;
        }
        if (Active) Cancel(ctx, "safe-state-observed");
        return false;
    }

    private void Begin(string kind)
    {
        if (Active && Kind == kind) return;
        if (Active) LastEndReason = "replaced-by-" + kind;
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
