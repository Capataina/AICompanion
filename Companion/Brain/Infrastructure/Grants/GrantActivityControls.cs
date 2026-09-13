#nullable enable
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.Brain.Infrastructure.Grants;

public enum HandGrant { Available, WorkTool, Unavailable }

/// <summary>A branch proposes controls; only the common finaliser applies them.</summary>
public readonly record struct ActivityControlRequest(Controls Movement, string Owner, HandGrant Hand = HandGrant.Available,
    Vector2? RecoveryVelocity = null, bool ObserveProgress = false, bool CountReunion = false);

/// <summary>What was requested and applied during AI, before the engine integrates the body.
/// Available grants permission for compatible shooting/light; it does not claim either occurred.</summary>
public readonly record struct ActivityControlGrant(long Id, ulong Tick, long ActivityId, ActivityPhase ActivityPhase,
    string RequestedOwner, string AppliedOwner, Controls RequestedMovement, Controls AppliedMovement,
    HandGrant Hand, Vector2? RequestedRecoveryVelocity, Vector2 AppliedVelocity, long MotorApplications,
    long AttemptId = 0);

public sealed class GrantActivityControls
{
    public ActivityControlGrant? Last { get; private set; }
    private long nextId;

    public ActivityControlGrant Apply(CompanionNPC companion, ActivityControlRequest request, OwnCurrentActivity activity)
    {
        long before = companion.Motor.ControlApplications;
        if (request.RecoveryVelocity is Vector2 flight)
            companion.Motor.ApplyRecoveryFlight(flight);
        else
            companion.Motor.Apply(request.Movement, request.Owner);
        var granted = new ActivityControlGrant(++nextId, Terraria.Main.GameUpdateCount, activity.Id, activity.Phase,
            request.Owner, companion.Motor.ControlSource, request.Movement, companion.Motor.AppliedControls,
            request.Hand, request.RecoveryVelocity, companion.NPC.velocity, companion.Motor.ControlApplications - before,
            activity.AttemptOpen ? activity.AttemptId : 0);
        Last = granted;
        return granted;
    }
}
