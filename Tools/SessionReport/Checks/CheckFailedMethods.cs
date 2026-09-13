#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// A method that failed more than once is a finding. Identity agreement already lists every
/// attempt-outcome by id; it does not ask whether the same method kept failing. The drowned hop
/// take-off closes two mine attempts Failed with interaction-jump-lost-take-off and zero effects,
/// and without this check that interval produces no finding at all.
/// </summary>
public sealed class RepeatedFailedMethodsAreFindings : ICheck, ICheckCoverage
{
    public string Name => "did one method fail more than once";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the attempt outcomes");

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var failed = log.Events
            .Where(e => e.kind == "attempt-outcome"
                && string.Equals(e.Field("status"), "Failed", StringComparison.Ordinal)
                && Number(e.Field("productive-effects")) is 0)
            .GroupBy(e => (Activity: e.label ?? "", Cause: e.Field("cause") ?? ""))
            .Where(g => g.Count() >= 2 && g.Key.Cause.Length > 0);

        foreach (var group in failed)
        {
            var events = group.OrderBy(e => e.tick).ToList();
            yield return CheckEvents.Aggregate(
                Severity.Potential,
                Name,
                $"{group.Key.Activity} failed {events.Count} times as {group.Key.Cause}",
                events,
                "Identity agreement lists these attempts; it does not ask whether the method kept failing. "
                    + "A repeated Failed outcome with no credited effect is the first failed contract in that interval, "
                    + "not a clean run.");
        }
    }

    private static long? Number(string? value)
        => ReadGodsEyeEvents.TryLong(value, out long n) ? n : null;
}
