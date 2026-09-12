#nullable enable

using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.BehaviourSelection;

public enum ActivityPhase { None, Selected, Executing, Suspended }

/// <summary>One primary activity owns entry, execution and suspension. Opportunity caches
/// remain in their activity adapters; suspension retains only this one current purpose.</summary>
public sealed class OwnCurrentActivity
{
    public CompanionAction? Current { get; private set; }
    public long Id { get; private set; }
    public ActivityPhase Phase { get; private set; }
    public string Reason { get; private set; } = "no-activity";
    public ulong ChangedAt { get; private set; }
    public long LastEndedId { get; private set; }
    public string LastEndReason { get; private set; } = "none";
    private long nextId;
    private object? identity;
    private int generation;

    public void Select(CompanionAction? next, in ActionContext context)
    {
        bool changedExecutor = !ReferenceEquals(Current, next);
        bool resuming = Phase == ActivityPhase.Suspended;
        if (changedExecutor)
        {
            // Suspension already released the old method. Releasing it again can cancel a
            // shared search that another owner has since begun.
            if (!resuming) Current?.Exit(context);
            Current?.ReleaseAdmission();
            next?.Enter(context);
        }
        else if (resuming) next?.Enter(context);

        object? nextIdentity = next?.ActivityIdentity;
        int nextGeneration = nextIdentity is Terraria.NPC npc ? HostileAttackSources.Generation(npc) : 0;
        bool changedPurpose = changedExecutor || !Equals(identity, nextIdentity) || generation != nextGeneration;
        if (next == null || changedPurpose)
        {
            if (Id != 0)
            {
                LastEndedId = Id;
                LastEndReason = next == null ? "no-valid-selection" : "different-purpose-selected";
            }
            long previousId = Id;
            Id = next == null ? 0 : ++nextId;
            if (Id != previousId) ChangedAt = Terraria.Main.GameUpdateCount;
        }
        Current = next;
        identity = nextIdentity;
        generation = nextGeneration;
        next?.AdmitActivity();
        if (next == null || changedPurpose || resuming || Phase != ActivityPhase.Executing)
            SetPhase(next == null ? ActivityPhase.None : ActivityPhase.Selected,
                next == null ? "no-valid-selection" : resuming && !changedPurpose ? "reselected-after-interruption" : "selected");
    }

    public void BeginExecution()
    {
        if (Current != null) SetPhase(ActivityPhase.Executing, "ordinary-execution");
    }

    public void Suspend(in ActionContext context, string reason)
    {
        if (Current == null) return;
        if (Phase != ActivityPhase.Suspended) Current.Suspend(context);
        SetPhase(ActivityPhase.Suspended, reason);
    }

    private void SetPhase(ActivityPhase phase, string reason)
    {
        if (Phase != phase || Reason != reason) ChangedAt = Terraria.Main.GameUpdateCount;
        Phase = phase;
        Reason = reason;
    }
}
