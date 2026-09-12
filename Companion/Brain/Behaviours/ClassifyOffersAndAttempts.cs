#nullable enable

namespace AICompanion.Companion.Brain.Behaviours;

/// <summary>What preparation established about an opportunity, independent of how much it is worth.
/// Only Usable and Unresolved offers may carry positive value, and an unresolved offer is a bounded
/// investigation rather than proven work. The chooser treats a positive value on any other state as
/// an evaluation error, so an absent or forbidden offer cannot win by carrying a stale number.</summary>
public enum OfferEligibility { NoOpportunity, PolicyForbidden, KnownUnusable, Unresolved, Usable }

/// <summary>How one physical attempt at a purpose ended. Attempted means execution began without an
/// observed productive effect; Executed means a method with no productive-effect claim ran (keeping
/// company); Partial and Complete require the activity's own evidence; Invalid means the target or
/// permission disappeared; Interrupted is a shared controller taking the body and is never rewritten
/// as Failed, which is reserved for a method the activity itself gave up on.</summary>
public enum AttemptStatus { Attempted, Executed, Partial, Complete, Invalid, Interrupted, Failed }

/// <summary>The activity's own reading of how an attempt ended when selection replaced it.</summary>
public readonly record struct AttemptConclusion(AttemptStatus Status, string Cause);

/// <summary>Immutable history of one attempt. Productive effects count only observations the activity
/// credited through the chooser's work record, so an external edit to the same target earns nothing.</summary>
public readonly record struct AttemptOutcome(long AttemptId, long ActivityId, string Activity,
    BehaviourSelection.PurposeFamily Family, ulong StartTick, ulong EndTick, AttemptStatus Status,
    string Cause, int ProductiveEffects);
