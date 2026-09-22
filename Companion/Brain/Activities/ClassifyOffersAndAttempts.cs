#nullable enable

namespace AICompanion.Companion.Brain.Activities;

/// <summary>What preparation established about an opportunity, independent of how much it is worth.
/// Only Usable and Unresolved offers may carry positive value, and an unresolved offer is a bounded
/// investigation rather than proven work. A positive value on any other state is an evaluation error,
/// so an absent or forbidden offer cannot win by carrying a stale number.
/// Deferred survives its original writer and now has exactly one: a nearby-work stand outside a
/// finished flood's known radius (`PerformNearbyWorldWork`). It used to be the per-family query
/// scheduler's too — an activity not prepared this comparison because its family's allowance was
/// spent — and that scheduler went with the chooser under `AIC-419`.
/// Every default is NoOpportunity. **Nothing clears a classification between preparations any more**,
/// which `0bb2c8a` ended rather than `AIC-419`: the chooser's own loop called `ResetClassification`
/// before each `Prepare`, so a path that forgot to classify failed closed, and since the course took
/// the tick such a path keeps last tick's answer instead. No path is known to do it, and nothing
/// enforces that; restoring the guarantee means clearing in the tick's preparation loop and is a
/// behaviour change, since an activity would flip from a stale offer to NoOpportunity.</summary>
public enum OfferEligibility { NoOpportunity, PolicyForbidden, KnownUnusable, Unresolved, Usable, Deferred }

/// <summary>How one physical attempt at a purpose ended. Attempted means execution began without an
/// observed productive effect; Executed means a method with no productive-effect claim ran (keeping
/// company); Partial and Complete require the activity's own evidence; Invalid means the target or
/// permission disappeared; Interrupted is a shared controller taking the body and is never rewritten
/// as Failed, which is reserved for a method the activity itself gave up on.</summary>
public enum AttemptStatus { Attempted, Executed, Partial, Complete, Invalid, Interrupted, Failed }

/// <summary>Who produced a completion, kept apart from whether the purpose is complete, because the
/// evidence an activity holds usually shows that the companion contributed rather than that it
/// finished. Companion: the companion's own observed effect finished it. Shared: its effects
/// contributed and something else finished it. Unattributed: the purpose ended (a target died, a
/// drop left the world) and no observation names who did it. NotApplicable for non-completions.</summary>
public enum AttemptAttribution { NotApplicable, Companion, Shared, Unattributed }

/// <summary>The activity's own reading of how an attempt ended when selection replaced it. A claimed
/// yield is the item type and quantity the activity says reached the companion's cargo, zero when it
/// claims none; only an activity that reads a transfer ledger may claim one, and a reader holds the
/// claim against the pickups recorded under the same attempt.</summary>
public readonly record struct AttemptConclusion(AttemptStatus Status, string Cause,
    AttemptAttribution Attribution = AttemptAttribution.NotApplicable, int ClaimedYieldType = 0, int ClaimedYieldQuantity = 0);

/// <summary>Immutable history of one attempt. Productive effects count only observations the activity
/// credited through `OwnCurrentActivity.RecordWork`, so an external edit to the same target earns nothing.
/// Attempt identities are unique across every activity owner in the process. The claimed yield is the
/// conclusion's, and is zero for an interruption, which the owner concludes without asking the activity.</summary>
public readonly record struct AttemptOutcome(long AttemptId, long ActivityId, string Activity,
    Infrastructure.Selection.PurposeFamily Family, ulong StartTick, ulong EndTick, AttemptStatus Status,
    string Cause, int ProductiveEffects, AttemptAttribution Attribution = AttemptAttribution.NotApplicable,
    int ClaimedYieldType = 0, int ClaimedYieldQuantity = 0);
