#nullable enable
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Brain.ActivityCoordination;

/// <summary>Published after control and outcome coordination. Presentation reads one completed
/// tick instead of combining an old ordinary choice with a different controller's live state.</summary>
public readonly record struct ActivitySnapshot(ulong Tick, long ActivityId, PurposeFamily? Family,
    string? Activity, ActivityPhase Phase, bool Downed, bool Recovering, bool SafetyActive, bool MovementStalled)
{
    public bool Suspended => Phase == ActivityPhase.Suspended || Downed || Recovering || SafetyActive;
}
