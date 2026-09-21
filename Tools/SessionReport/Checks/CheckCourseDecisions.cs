#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// What the course brain guarantees about its own decision record, restated so a recording that
/// breaks one is a Definitive finding with no threshold in it.
///
/// <para>This is the check half of reading a course, and it exists because the recording side ran
/// ahead of the reading side: the companion has been able to write a typed course trace since schema
/// 0.41.0 and nothing could read it back as a story. The whole argument for the retained-course brain
/// was that a poor decision, a stale model, an invalid binding and an effect that never arrived would
/// be distinguishable after a play — and that distinction lives here and in the measure beside it.</para>
///
/// <para>Each rule below is a producer guarantee taken from <c>DecideCourseEachTick.RecordDecision</c>,
/// named with the line that makes a violation impossible, so a finding says the record and the brain
/// disagree rather than that a number looked high. None of them says whether a decision was any good;
/// that is what <c>MeasureCourseWork</c> reports and deliberately does not grade.</para>
/// </summary>
public sealed class EveryCourseDecisionAccountsForItsOwnSearch : ICheck, ICheckCoverage
{
    /// <summary>Individual decisions named in a finding's detail. Enough to see the shape, few enough to read.</summary>
    private const int Named = 5;

    public string Name => "does each recorded course decision account for its own search";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the typed course decisions")
            ?? (ReadCourseChronicle.Read(session, session.Path).Coverage == "historical-unavailable"
                ? "typed course decisions (first written by schema 0.41.0)" : null);

    public IEnumerable<Finding> Run(Session session)
    {
        CourseDecisionLog log = ReadCourseDecisions.From(ReadGodsEyeEvents.Read(session.Path));
        if (log.Decisions.Count == 0)
        {
            // A capture new enough to carry decisions and holding none is a statement rather than
            // silence: either the course owner never ran, or every one of its records was lost. The
            // chronicle's own coverage line says which, and this names the consequence — that every
            // rule below measured nothing, which in a report reads exactly like a clean run.
            yield return new Finding(Severity.Potential, Name, "the capture carries no course decision at all",
                "The schema is new enough to hold typed course decisions and the sidecar holds none, so either the course "
                    + "owner never decided or its records never arrived. Every course rule in this report therefore measured "
                    + "nothing rather than passing. The course-evidence line above says whether the sidecar itself was intact."
                    + (log.Unreadable > 0 ? $" {log.Unreadable} decision payload(s) could not be read." : ""), 0, 0, 0);
            yield break;
        }

        // A published tally is the producer's top four refusal reasons drawn from the same rejections
        // its refused total counts, so their sum cannot exceed that total. It is the same property the
        // candidate funnel's factor row asserts one layer up — the published terms must account for the
        // published total — and it catches a tally fed from a different counter than the one reported.
        var overCounted = log.Decisions
            .Where(d => d.Refusals.Sum(r => r.Value) > d.OrdersRefused).ToList();
        if (overCounted.Count > 0)
            yield return new Finding(Severity.Definitive, Name,
                "a decision's published refusal reasons account for more orders than it says it refused",
                $"{overCounted.Count:n0} of {log.Decisions.Count:n0} decisions. The producer draws the tally from the same "
                    + "rejections the refused total counts and publishes its four largest, so the sum can be lower and never "
                    + $"higher. {Describe(overCounted, d => $"refused {d.OrdersRefused:n0}, tallied {d.Refusals.Sum(r => r.Value):n0}")}",
                (int)overCounted[0].Tick, (int)overCounted[^1].Tick, overCounted.Count);

        // A purpose is read off the bound step, so a decision naming one describes a course that has
        // at least that step in it. A purpose beside zero steps is a binding published against a course
        // the record says is empty, which is the seam a stale binding would show up in first.
        var boundWithoutSteps = log.Decisions.Where(d => d.Purpose.Length > 0 && d.Steps < 1).ToList();
        if (boundWithoutSteps.Count > 0)
            yield return new Finding(Severity.Definitive, Name,
                "a decision bound a step of a course the same record says has no steps",
                $"{boundWithoutSteps.Count:n0} of {log.Decisions.Count:n0} decisions. The purpose is read from the bound "
                    + "step and the step count from the published course, so a named purpose means at least one step. "
                    + $"{Describe(boundWithoutSteps, d => $"purpose '{d.Purpose}', steps {d.Steps}")}",
                (int)boundWithoutSteps[0].Tick, (int)boundWithoutSteps[^1].Tick, boundWithoutSteps.Count);

        // Every decision the owner records names why it decided, including the one that kept what it
        // had. An unnamed reason is a record that cannot be attributed to a poor decision or a stale
        // model, which is the one distinction the course trace exists to make.
        var unnamed = log.Decisions.Where(d => d.Reason.Length == 0).ToList();
        if (unnamed.Count > 0)
            yield return new Finding(Severity.Definitive, Name, "a decision was recorded without a reason",
                $"{unnamed.Count:n0} of {log.Decisions.Count:n0} decisions carry an empty reason. Every path through the "
                    + "owner names one, so an empty reason is a record that cannot be attributed to a decision or to a "
                    + $"stale model. {Describe(unnamed, d => $"activity '{d.Activity}', steps {d.Steps}")}",
                (int)unnamed[0].Tick, (int)unnamed[^1].Tick, unnamed.Count);

        if (log.Unreadable > 0)
            yield return new Finding(Severity.Potential, Name, "a course decision payload could not be read",
                $"{log.Unreadable:n0} occurrence(s) name the decision payload kind and hold no readable field object, so the "
                    + "rules above measured the capture minus those records. A payload version this reader does not know "
                    + "reads the same way as a corrupt one, which is why this is Potential and names a count rather than a cause.",
                0, 0, log.Unreadable);
    }

    private static string Describe(IReadOnlyList<CourseDecision> decisions, Func<CourseDecision, string> detail)
        => string.Join("; ", decisions.Take(Named).Select(d => $"tick {d.Tick} {detail(d)}"))
            + (decisions.Count > Named ? $"; and {decisions.Count - Named:n0} more" : "");
}
