#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using live::AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Tools.EngineReplay.Observation;

/// <summary>
/// The four lines that say why a course decided what it decided, in one place.
///
/// This was written inline in a combat fixture, then again beside it, and was about to be written a
/// third time in a lighting fixture — which is the tell that the repository was missing the interface
/// rather than that the fixtures were repetitive. Each copy would have drifted: the first two already
/// disagreed about whether to print the search's exhaustion flag.
///
/// The four lines answer four different questions and none of them substitutes for another, which is
/// why they are four lines rather than one summary. **Decisions** says whether a comparison happened at
/// all, per tick, because `Last.Reason` is a single sticky field and a scene that decided ten times out
/// of 120 reads identically through it to one that never decided. **Coverage** says whether each
/// domain's census finished. **Admitted** says how much of that census the search may actually order,
/// which coverage cannot answer — a domain can report a complete thirteen of thirteen and contribute
/// nothing, because `SearchCourseOrders.Begin` enumerates only `KnownUsable`. **Values** says what the
/// best order led by each domain scored, which is the only line that answers "why did this lose"
/// rather than "that it lost".
///
/// Reading fewer of them than the question needs is how one behaviour row collected four wrong causes
/// in a row on 21 September 2026, each of which was ruled out by a line that was not being printed yet.
/// </summary>
public static class DescribeCourseFunnel
{
    /// <summary>One tally of decision reasons, accumulated by the caller's own loop. Kept as the
    /// caller's state rather than read off the owner, because the owner keeps only the latest.</summary>
    public static void Count(Dictionary<string, int> reasons, DecideCourseEachTick owner)
    {
        string reason = owner.Last.Reason;
        reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
    }

    public static string Decisions(IReadOnlyDictionary<string, int> reasons)
        => reasons.Count == 0 ? "no ticks observed"
            : string.Join(" ", reasons.OrderByDescending(entry => entry.Value).Select(entry => $"{entry.Key}x{entry.Value}"));

    public static string Coverage(DecideCourseEachTick owner)
        => string.Join(" ", owner.Coverage.Select(source =>
            $"{source.Source}:{source.Examined}/{source.Total}{(source.Exhausted ? "" : "+")}"));

    public static string Refusals(DecideCourseEachTick owner)
    {
        string refusals = string.Join(" ", owner.LastRefusals.OrderByDescending(entry => entry.Value)
            .Select(entry => $"{entry.Key}x{entry.Value}"));
        return refusals.Length == 0 ? "none" : refusals;
    }

    public static string Admitted(DecideCourseEachTick owner)
    {
        string admitted = string.Join(" ", owner.Admitted.Select(domain =>
            $"{domain.Domain}:{domain.Usable}ok/{domain.Unresolved}?/{domain.Unusable}x"
            + (domain.Reason.Length == 0 ? "" : "(" + domain.Reason + ")")));
        return admitted.Length == 0 ? "nothing discovered" : admitted;
    }

    public static string Values(DecideCourseEachTick owner)
    {
        string values = string.Join(" ", owner.LastLeaders.OrderByDescending(entry => entry.Value.Total.Nominal)
            .Select(entry =>
            {
                var value = entry.Value;
                string unknowns = string.Join(",", value.Unknowns.Take(3));
                // One interpolated string, never a concatenation of two inside FormattableString.Invariant:
                // concatenating interpolated strings yields a `string`, which that overload refuses.
                return FormattableString.Invariant(
                    $"{entry.Key}={value.Total.Nominal:0.0000}(useful {value.UsefulEffects:0.0000} harm {value.Harm:0.0000} gap {value.Companionship:0.0000} unknown[{unknowns}])");
            }));
        return values.Length == 0 ? "nothing priced" : values;
    }

    /// <summary>The whole funnel under one label, for a fixture that is about to throw and wants every
    /// line in the log before it does. Printed rather than returned so a row that fails still leaves
    /// the diagnosis behind; `EmitLedgerRows.Detail` is for a line that belongs in the row itself.</summary>
    public static void Print(string label, DecideCourseEachTick owner, IReadOnlyDictionary<string, int> reasons)
    {
        Console.WriteLine($"  {label} decisions: {Decisions(reasons)}");
        Console.WriteLine($"  {label} course: decision={owner.Last.Reason} activity={owner.Last.Activity}"
            + $" steps={owner.Course.Current?.Projection.Steps.Count.ToString(CultureInfo.InvariantCulture) ?? "no-course"}"
            + $" release={owner.Course.ReleaseReason}"
            + $" orders={owner.LastSearch.Evaluated}/{owner.LastSearch.Rejected}"
            + $" exhausted={owner.LastSearch.Exhausted} coverage {Coverage(owner)}");
        Console.WriteLine($"  {label} refusals: {Refusals(owner)}");
        Console.WriteLine($"  {label} admitted: {Admitted(owner)}");
        Console.WriteLine($"  {label} values: {Values(owner)}");
    }
}
