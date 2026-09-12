#nullable enable

using System.Collections.Generic;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.BehaviourSelection;

public enum ActivityPhase { None, Selected, Executing, Suspended }

/// <summary>One primary activity owns entry, execution and suspension. Opportunity caches
/// remain in their activity adapters; suspension retains only this one current purpose.
/// Each continuous stretch of execution is one attempt with its own identity, so resuming a
/// suspended purpose keeps the activity identity and receives a new attempt identity.</summary>
public sealed class OwnCurrentActivity
{
    public CompanionAction? Current { get; private set; }
    public long Id { get; private set; }
    public ActivityPhase Phase { get; private set; }
    public string Reason { get; private set; } = "no-activity";
    public ulong ChangedAt { get; private set; }
    public long LastEndedId { get; private set; }
    public string LastEndReason { get; private set; } = "none";
    /// <summary>The open attempt's identity, or the last one opened when none is open.</summary>
    public long AttemptId { get; private set; }
    public bool AttemptOpen { get; private set; }
    public ulong AttemptStartedAt { get; private set; }
    public int AttemptEffects { get; private set; }
    public AttemptOutcome? LastAttempt => recent.Count == 0 ? null : recent[^1];
    /// <summary>Bounded history so two conclusions inside one tick (a selection replacing one
    /// attempt, then recovery interrupting the next) both reach the recorder.</summary>
    public IReadOnlyList<AttemptOutcome> RecentAttempts => recent;
    private readonly List<AttemptOutcome> recent = new();
    private const int RecentAttemptCapacity = 16;
    private long nextId, nextAttemptId;
    private object? identity;
    private int generation;

    public void Select(CompanionAction? next, in ActionContext context)
    {
        bool changedExecutor = !ReferenceEquals(Current, next);
        bool resuming = Phase == ActivityPhase.Suspended;
        object? nextIdentity = next?.ActivityIdentity;
        int nextGeneration = nextIdentity is Terraria.NPC npc ? HostileAttackSources.Generation(npc) : 0;
        bool changedPurpose = changedExecutor || !Equals(identity, nextIdentity) || generation != nextGeneration;
        // The replaced activity reads its own evidence before Exit releases the method state it
        // would conclude from. A purpose kept under the same executor keeps its attempt open.
        if (AttemptOpen && Current != null && (next == null || changedPurpose))
        {
            var conclusion = Current.ConcludeAttempt(AttemptStartedAt, AttemptEffects);
            CloseAttempt(conclusion.Status, conclusion.Cause);
        }
        if (changedExecutor)
        {
            // Suspension already released the old method. Releasing it again can cancel a
            // shared search that another owner has since begun.
            if (!resuming) Current?.Exit(context);
            Current?.ReleaseAdmission();
            next?.Enter(context);
        }
        else if (resuming) next?.Enter(context);

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
        if (Current == null) return;
        if (!AttemptOpen)
        {
            AttemptId = ++nextAttemptId;
            AttemptStartedAt = Terraria.Main.GameUpdateCount;
            AttemptEffects = 0;
            AttemptOpen = true;
        }
        SetPhase(ActivityPhase.Executing, "ordinary-execution");
    }

    /// <summary>Credit an observed productive effect to the open attempt. Effects observed while no
    /// attempt is executing belong to nobody's purpose and are deliberately not counted.</summary>
    public void RecordProductiveEffect()
    {
        if (AttemptOpen) AttemptEffects++;
    }

    public void ObserveOutcome(in ActionContext context)
    {
        // A safety controller may still request body-progress observation. Its movement
        // must not consume the interrupted ordinary activity's failure budget.
        if (Phase == ActivityPhase.Executing) Current?.ObserveOutcome(context);
    }

    public void Suspend(in ActionContext context, string reason)
    {
        if (Current == null) return;
        if (AttemptOpen) CloseAttempt(AttemptStatus.Interrupted, reason);
        if (Phase != ActivityPhase.Suspended) Current.Suspend(context);
        SetPhase(ActivityPhase.Suspended, reason);
    }

    private void CloseAttempt(AttemptStatus status, string cause)
    {
        if (recent.Count == RecentAttemptCapacity) recent.RemoveAt(0);
        recent.Add(new AttemptOutcome(AttemptId, Id, Current!.Name, Current.Family, AttemptStartedAt,
            Terraria.Main.GameUpdateCount, status, cause, AttemptEffects));
        AttemptOpen = false;
    }

    private void SetPhase(ActivityPhase phase, string reason)
    {
        if (Phase != phase || Reason != reason) ChangedAt = Terraria.Main.GameUpdateCount;
        Phase = phase;
        Reason = reason;
    }
}
