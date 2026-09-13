#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// Why a held movement goal is not being delivered, named by the first contract the evidence
/// shows broken. Each value sends a repair to a different layer, which is the point of keeping
/// them apart: a missing transition is a representation defect, an unfinished search is a
/// computation-allocation defect, a refused entry is a preparation defect, a divergence between
/// the body the proof predicted and the body the engine produced is an adapter or control defect,
/// a state that arrived and cannot continue is a terminal-contract defect, and a method another
/// owner took the body from has not failed at all. "The companion looked stuck" is none of these,
/// and none of them licenses replacing the search.
/// </summary>
public enum MovementFailure
{
    /// <summary>The goal is being delivered, was delivered, or was released voluntarily.</summary>
    None,

    /// <summary>The search exhausted every transition the model generates and never reached the
    /// goal. This is a claim about the model: a closed graph is not a physically sealed world.</summary>
    AbsentTransition,

    /// <summary>A route or preparation search stopped on work, node or wall-clock limits, or is
    /// still running without a usable prefix. Nothing was established either way.</summary>
    UnfinishedSearch,

    /// <summary>The complete remaining traversal was simulated from the body the engine actually
    /// left and failed physically, and no alternative profile or preparation prefix rescued it.</summary>
    InvalidActualEntry,

    /// <summary>The body the engine produced differed from the successor the proof predicted, the
    /// motor attributed no external cause, and the attempt then failed. In play this is an adapter
    /// or control divergence or an unclassified write by something else; the class cannot tell those
    /// apart and says so rather than guessing.</summary>
    NativeMismatch,

    /// <summary>The body and its proof agreed, yet the state reached cannot support the declared
    /// continuation: no standable node under a grounded body, or a step that faulted on its own
    /// terms while every tick matched its prediction.</summary>
    UnusableTerminal,

    /// <summary>Another owner took the body (shared safety, a reflex, recovery flight, downing), an
    /// external hit displaced it, or a threat forecast refused the move. The method did not fail.</summary>
    Preempted,
}

/// <summary>
/// How one begun traversal attempt ended. Physical completion and voluntary cancellation are
/// scored apart because they answer different questions: the first says whether this kind of move
/// works, the second says only that the brain changed its mind, and a count that joins them reads
/// every replan as a failed collision.
/// </summary>
public enum AttemptEnding
{
    Completed,
    PhysicalFailure,
    Preempted,
    Cancelled,
}

/// <summary>
/// The navigator's current explanation for a held goal it is not delivering: the class, the evidence
/// that produced it, and the search and attempt identities it belongs to, so a telemetry row can be
/// joined to the event that caused it. Sticky until delivery evidence (a completed step, arrival) or
/// a changed goal supersedes it.
/// </summary>
public readonly record struct MovementFailureReport(MovementFailure Kind, string Reason, long SearchId, long AttemptId, NavStep? Step);
