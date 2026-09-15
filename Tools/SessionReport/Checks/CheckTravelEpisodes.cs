#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// What both travel questions need before they can be asked, and the one denominator they share.
/// </summary>
internal static class TravelEvidence
{
    /// <summary>The schema that first wrote route episodes and stops; an older capture holds neither and is skipped by name.</summary>
    internal static readonly Version First = new(0, 31, 0);

    internal static string? Missing(Session session, string carries)
    {
        if (CheckEvents.SidecarUnavailable(session, carries) is { } unavailable) return unavailable;
        return CompletedTransferClaimsWereReceived.SchemaAtLeast(session, First) ? null
            : $"{carries} (first written by schema {First})";
    }

    /// <summary>
    /// Ticks the body spent on its own route, counted from the rows rather than from the occurrences, because an
    /// occurrence says when a stop happened and nothing says how long the body was travelling around it. The predicate is
    /// the producer's: the ordinary travel owner holding the body while the navigator has something to execute. It must
    /// stay the producer's, because a rate whose numerator and denominator disagree about what travelling is means nothing.
    /// </summary>
    internal static int RouteTicks(Session session)
    {
        string[] owner = session["control_source"].Text, status = session["nav_status"].Text;
        int ticks = 0;
        for (int i = 0; i < owner.Length && i < status.Length; i++)
            if (owner[i] == "travel" && (status[i] == "Executable" || status[i] == "Partial"))
                ticks++;
        return ticks;
    }

    internal static long Ticks(GodsEyeEvent e)
        => ReadGodsEyeEvents.TryLong(e.Field("ticks"), out long n) ? n : e.amount;

    internal static long Number(GodsEyeEvent e, string field)
        => ReadGodsEyeEvents.TryLong(e.Field(field), out long n) ? n : -1;

    internal static int Clamp(long tick) => (int)Math.Clamp(tick, 0, int.MaxValue);

    internal static string Ratio(double top, double bottom)
        => bottom <= 0 ? "-" : (top / bottom).ToString("0.00", CultureInfo.InvariantCulture) + "x";
}

/// <summary>
/// How long a whole journey took, against the two things it can honestly be compared with: the ticks its own route steps
/// were proven to take, and the ticks the player's body took over the same ground.
///
/// It exists because the questions either side of it cannot see this. The edge check times one finished move against its
/// proven ticks, and a journey of forty moves can have every one of them land inside its proven time while the journey
/// takes four times as long, because the time goes into the gaps between moves. The census counts how many places were
/// asked for and how many were reached, and says nothing about how long reaching them took. "It took forever to get to
/// me" fell exactly between the two.
///
/// The grade is Oddity throughout and deliberately so. No threshold here would be grown from a defect anybody has
/// measured — no capture on disk carries one of these occurrences yet — and a number invented now would fire on the shape
/// its author imagined rather than on the failure. The finding therefore states the ratios and lets them be read; the
/// first real capture that shows a journey costing several times its price is what earns a graded check, and the detail
/// below says what such a check would have to establish.
/// </summary>
public sealed class JourneysTakeTheTimeTheyWereProven : ICheck, ICheckCoverage
{
    /// <summary>Journeys named individually per request kind, worst first. Five is enough to see a pattern and few enough to read.</summary>
    private const int Worst = 5;

    public string Name => "how long each whole journey took against its proven ticks and the player's own";
    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session) => TravelEvidence.Missing(session, "route episodes");

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var episodes = log.Events.Where(e => e.kind == "route-episode").ToList();
        if (episodes.Count == 0)
        {
            // A capture new enough to carry journeys and holding none is a statement, not silence: either nothing was
            // ever asked for, or every episode was still open when the capture ended.
            yield return new Finding(Severity.Oddity, Name, "no journey was recorded in this capture",
                "The capture is new enough to carry route episodes and holds none, so either the companion was never asked "
                    + "to go anywhere or the session ended inside its first journey. The census beside this session says which: "
                    + "its places-asked-for table counts the same episodes.", 0, 0, 0);
            yield break;
        }

        foreach (var group in episodes.GroupBy(e => e.label, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
        {
            var journeys = group.OrderBy(e => e.tick).ToList();
            long actual = journeys.Sum(e => TravelEvidence.Number(e, "actual-ticks"));
            long planned = journeys.Sum(e => Math.Max(0, TravelEvidence.Number(e, "planned-ticks")));
            int reached = journeys.Count(e => e.channel == "reached");

            // A journey holding a death is reported as such rather than only as a shorter journey. The producer removes
            // the downed ticks from the duration, which is right — a death is not the follower being slow — but a reader
            // comparing this against the wall clock in the row stamps would otherwise find a gap with no explanation.
            var died = journeys.Where(e => TravelEvidence.Number(e, "downed-ticks") > 0).ToList();

            var compared = journeys.Where(e => TravelEvidence.Number(e, "player-ticks") >= 0).ToList();
            long comparedActual = compared.Sum(e => TravelEvidence.Number(e, "actual-ticks"));
            long comparedPlayer = compared.Sum(e => TravelEvidence.Number(e, "player-ticks"));

            string slowest = string.Join("; ", journeys
                .Where(e => TravelEvidence.Number(e, "planned-ticks") > 0)
                .OrderByDescending(e => (double)TravelEvidence.Number(e, "actual-ticks") / TravelEvidence.Number(e, "planned-ticks"))
                .Take(Worst)
                .Select(e => $"tick {e.tick:n0} {e.channel} {TravelEvidence.Number(e, "actual-ticks")} ticks against "
                    + $"{TravelEvidence.Number(e, "planned-ticks")} proven"
                    + (TravelEvidence.Number(e, "player-ticks") >= 0 ? $" and {TravelEvidence.Number(e, "player-ticks")} of yours" : ", your trail did not cover it")
                    + $", {e.Field("path-tiles")} tiles of route over {e.Field("straight-tiles")} straight"));

            yield return new Finding(Severity.Oddity, Name,
                $"{group.Key}: {journeys.Count:n0} journeys, {reached:n0} reached, {TravelEvidence.Ratio(actual, planned)} of their proven ticks",
                $"{actual:n0} ticks taken against {planned:n0} proven across the steps that finished inside them. "
                    + (compared.Count == 0
                        ? "Your own trail covered neither end of any of them, so there is no player comparison here; that is missing coverage rather than a companion that kept up. "
                        : $"Over the {compared.Count:n0} whose ends your trail did cover, it took {comparedActual:n0} ticks against your {comparedPlayer:n0}, {TravelEvidence.Ratio(comparedActual, comparedPlayer)}. ")
                    + (slowest.Length > 0 ? $"Worst {Math.Min(Worst, journeys.Count)}: {slowest}. " : "None of them carried a proven price, so no ratio can be formed. ")
                    + (died.Count > 0
                        ? $"{died.Count:n0} of them held a downing, and the {died.Sum(e => TravelEvidence.Number(e, "downed-ticks")):n0} tick(s) "
                            + "the body spent downed are outside every figure above, because a death is not a slow journey. "
                        : "")
                    + "Read as a baseline and not as a verdict: the proven total counts only the steps that finished, so waiting, "
                    + "replanning and being pre-empted all land in the actual figure and in neither reference. A graded check here "
                    + "would have to separate those three from a body that is simply slow, which needs a capture showing it.",
                TravelEvidence.Clamp(journeys[0].tick), TravelEvidence.Clamp(journeys[^1].tick), journeys.Count);
        }
    }
}

/// <summary>
/// Where the body stopped on its own route, how often per minute of travelling, and why.
///
/// It reads the reasons rather than deriving them, because the producer attributes each stop from retained state at the
/// moment it happens and nothing in a row can recover that afterwards: braking for a move that must start from rest, a
/// path replaced underneath, and a body genuinely stuck all look identical in a run of near-zero velocities, and they
/// want opposite fixes. The split by reason is therefore the finding, and the count is context for it.
/// </summary>
public sealed class TheBodyStopsOnItsOwnRoute : ICheck, ICheckCoverage
{
    private const int Longest = 5;

    public string Name => "where the body stopped on its own route, and why";
    public string[] Needs => new[] { "tick", "control_source", "nav_status" };

    public string? Missing(Session session) => TravelEvidence.Missing(session, "route stops");

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        var stops = log.Events.Where(e => e.kind == "stop").ToList();
        int travelTicks = TravelEvidence.RouteTicks(session);
        string recorded = session.Find("stops_per_minute") is { Text.Length: > 0 } column
            ? column.Text[^1] : "-";

        if (stops.Count == 0)
        {
            yield return new Finding(Severity.Oddity, Name, "the body never stopped on a route in this capture",
                $"{travelTicks:n0} tick(s) of route travel and no stop of three ticks or more under 0.6 px/tick. "
                    + (travelTicks == 0
                        ? "There was no travel to stop during, so this measured nothing rather than finding nothing."
                        : "That is a clean result over real travel."), 0, 0, 0);
            yield break;
        }

        string split = string.Join(", ", stops
            .GroupBy(e => e.Field("reason") ?? e.channel, StringComparer.Ordinal)
            .OrderByDescending(g => g.Sum(TravelEvidence.Ticks))
            .Select(g => $"{g.Key} {g.Count():n0} ({g.Sum(TravelEvidence.Ticks):n0} ticks)"));

        string longest = string.Join("; ", stops
            .OrderByDescending(TravelEvidence.Ticks)
            .Take(Longest)
            .Select(e => $"tick {e.Field("start-tick") ?? e.tick.ToString(CultureInfo.InvariantCulture)} "
                + $"{TravelEvidence.Ticks(e)} ticks {e.Field("reason")} "
                + $"(against wall {e.Field("against-wall-throughout")}, same segment {e.Field("same-segment-throughout")}, replanned {e.Field("replanned-during")}, fastest {e.Field("fastest-px-per-tick")} px/tick)"));

        double perMinute = travelTicks == 0 ? 0 : stops.Count * 3600.0 / travelTicks;
        yield return new Finding(Severity.Oddity, Name,
            $"{stops.Count:n0} stops over {travelTicks:n0} ticks of route travel, {perMinute:0.00} a minute",
            $"By reason: {split}. Longest {Math.Min(Longest, stops.Count)}: {longest}. "
                + $"The recorder's own running rate on the last row reads {recorded}, computed over the same predicate — the "
                + "ordinary travel owner holding the body while the navigator has an executable or partial route — so the two "
                + "disagreeing means one of them is counting something the other is not. "
                + "A stop is not itself a defect: during-replan is the body waiting for an answer, and against-wall is the "
                + "contact killing the velocity into a wall the steering is aiming through, which names the route rather than "
                + "the body. The reason worth reading is other, which is a body that had a segment to fly and did not move "
                + "with nothing in the record accounting for it.",
            TravelEvidence.Clamp(stops.Min(e => e.tick)), TravelEvidence.Clamp(stops.Max(e => e.tick)), stops.Count);
    }
}
