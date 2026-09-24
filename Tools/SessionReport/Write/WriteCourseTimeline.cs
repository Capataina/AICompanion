#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// One row per decision: what the course was asked, what it admitted, what it priced, what it bound
/// and what it cost. This is the page a person reads first.
///
/// <para><b>Why it is a different page from the narration beside it.</b>
/// <see cref="DescribeCourseDecisions"/> reads the <c>course-decision</c> occurrences and coalesces
/// them on what was decided — the reason, the activity and the bound purpose — which answers "what was
/// it doing at tick 8127". That is a question about the story. This one is keyed on the decision
/// *identity* the rows carry in <c>choice_id</c> since schema 0.43.0, and joins to it the four things
/// the occurrence payload does not hold: what each domain's census admitted, what the published course
/// ordered and what came second, and what the decision cost. Those are the questions the 22 September
/// 2026 capture was actually read for, and every one of the six readings had to assemble them by
/// script because no page put them side by side.</para>
///
/// <para><b>A decision is not a tick and the distinction is the page's whole shape.</b> A retained
/// course is carried across ticks under one identity, so 2,340 ticks of that capture are 1,093
/// decisions; and consecutive decisions alike in activity, steps, census, orders and release are
/// folded further, because "from tick 1,819 every decision was the empty course beside usable:3 and
/// usable:4" is one row of a table and five hundred rows of a log. The count and the span ride on the
/// folded row, so nothing is hidden by the folding — only repeated.</para>
///
/// <para><b>The release reason rides on an unsettled record, so the row that carries a decision's
/// numbers is never the row that carries why it ended.</b> <c>DecideCourseEachTick</c> traces the
/// release on the tick immediately after publication, and that record is unsettled; the settled record
/// is the one holding the steps, the pricing and the refusals. On the 22 September 2026 capture the two
/// are perfectly disjoint over all 2,340 payloads — <c>settled &amp;&amp; no release</c> 1,364 times,
/// <c>unsettled &amp;&amp; released</c> 976, and no other combination — so a selector that prefers the
/// settled payload and then reads its release reason prints a dash on every row of the table while the
/// producer wrote 976 reasons. It did, for 1,068 of 1,068 decisions, until this was fixed. The two
/// values are therefore picked off the span separately: the numbers off the settled record, the release
/// off the last record in the span that carries one. <c>a1b9857</c> found the same shape in the release
/// contract on the same afternoon, against a fixture whose every row was settled, which is why every
/// fixture here now writes at least one released payload.</para>
///
/// <para><b>Both order columns read `-` on every capture written before schema 0.45.0</b>, and the
/// page says so once rather than printing a dash five hundred times without explanation:
/// <c>task_order</c> and <c>task_order_runner_up</c> read the family chooser's permutation scoring,
/// which <c>0bb2c8a</c> retired, so every row of every capture between those two commits carries a
/// dash in both. A reader meeting that column is meeting a dead producer rather than a course that
/// ordered nothing.</para>
/// </summary>
public static class WriteCourseTimeline
{
    /// <summary>The schema at which <c>choice_id</c> became the course's decision identity. Before it
    /// the column is the family chooser's comparison identity and grouping on it is grouping the wrong
    /// thing, so the page declines rather than drawing a table of a retired brain.</summary>
    internal static readonly Version First = new(0, 43, 0);

    /// <summary>The schema at which the two order columns stopped being the retired chooser's.</summary>
    internal static readonly Version OrdersLive = new(0, 45, 0);

    /// <summary>Rows printed without <c>--timeline</c>. A course session is a thousand decisions and a
    /// person reading a report wants the shape; the whole table is one flag away.</summary>
    private const int DefaultRows = 30;

    private static readonly string[] Columns = { "tick", "choice_id", "choice_tick", "action", "decide_ms" };

    public static string Of(Session session, bool fullTimeline)
    {
        if (session.Count == 0) return "";
        string[] missing = Columns.Where(name => !session.Has(name)).ToArray();
        if (missing.Length > 0)
            return $"course timeline  unavailable: the file has no {string.Join(", ", missing)}\n";
        if (!CompletedTransferClaimsWereReceived.SchemaAtLeast(session, First))
            return $"course timeline  unavailable: `choice_id` is the family chooser's comparison identity below schema {First}, "
                + "so grouping decisions on it would group a brain this capture did not run\n";

        var decisions = Build(session);
        if (decisions.Count == 0) return "";
        var runs = Coalesce(decisions);

        int undescribed = runs.Sum(r => r.Undescribed);
        var text = new StringBuilder();
        text.Append($"course timeline  {decisions.Count:n0} decision(s) over {session.Count:n0} tick(s), in {runs.Count:n0} run(s)");
        text.Append("; a run is consecutive decisions alike in activity, steps, census, orders and release\n");
        if (undescribed > 0)
            text.Append($"                 {undescribed:n0} decision(s) traced no payload of their own and carry a dash where their numbers "
                + "belong; the producer traces an outcome rather than a tick, so a one-tick decision can land between two traces\n");
        if (!CompletedTransferClaimsWereReceived.SchemaAtLeast(session, OrdersLive))
            text.Append($"                 the order columns read the retired family chooser below schema {OrdersLive}, so `-` there is a dead "
                + "producer rather than a course that ordered nothing\n");
        text.Append("  ticks             decisions  bound          steps  priced/refused  decide ms     census                                    order  released\n");

        // **The abridged view keeps both ends, because the end of a session is where its diagnosis
        // usually is.** Printing the first thirty runs of the 22 September 2026 capture stops at tick
        // 788 and hides the whole of what that report is read for: from tick 1,819 every decision was
        // the empty course beside a census admitting three combat targets and four drops.
        var shown = new List<Run>();
        if (fullTimeline || runs.Count <= DefaultRows) shown.AddRange(runs);
        else
        {
            shown.AddRange(runs.Take(DefaultRows - DefaultRows / 3));
            shown.Add(null!);
            shown.AddRange(runs.Skip(runs.Count - DefaultRows / 3));
        }

        foreach (Run? maybe in shown)
        {
            if (maybe is null)
            {
                text.Append($"  … {runs.Count - DefaultRows:n0} run(s) between the two ends; --timeline prints every one\n");
                continue;
            }
            Run run = maybe;
            Decision d = run.First;
            string span = run.FromTick == run.ToTick ? $"{run.FromTick:n0}" : $"{run.FromTick:n0}–{run.ToTick:n0}";
            string priced = d.HasPayload ? $"{d.Priced}/{d.Refused}{(d.Exhausted ? "" : " cut")}" : "-";
            string cost = $"{d.MedianCostMs:0.0}/{run.WorstCostMs:0.0}";
            text.Append($"  {span,-17} {run.Decisions,9:n0}  {Fit(d.Bound + (d.Settled || !d.HasPayload ? "" : "?"), 13)}  "
                + $"{(d.HasPayload ? d.Steps.ToString(CultureInfo.InvariantCulture) : "-"),5}  {priced,14}  {cost,9}  "
                + $"{Fit(d.Census, 40)}  {Fit(d.Order, 5)}  {run.Release}\n");
        }
        return text.ToString();
    }

    /// <summary>One decision: the rows that carried it, joined to the occurrence that described it and
    /// to the census admission standing when it ran.</summary>
    private sealed record Decision(long Id, long FromTick, long ToTick, long DecidedAt, string Bound,
        bool HasPayload, bool Settled, long Steps, long Priced, long Refused, bool Exhausted,
        string Release, string Census, string Order, double MedianCostMs, double WorstCostMs);

    private sealed record Run(long FromTick, long ToTick, int Decisions, Decision First, double WorstCostMs,
        int Undescribed, string Release);

    /// <summary>What a column reads where the producer wrote nothing for it.</summary>
    private const string None = "-";

    private static List<Decision> Build(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        CourseDecisionLog course = ReadCourseDecisions.From(log);
        // **A decision's payload is not on its own `choice_tick`, and assuming it was left half this
        // table blank.** `choice_id` advances on the tick a decision is *reached*; the payload is
        // written on the tick an outcome is *traced*, and a decision that does not settle in one tick
        // traces on a later one. On the 22 September 2026 capture 2,340 payloads land on 1,364 distinct
        // ticks and the decision reached at tick 5 traces at tick 6, so a lookup keyed on `choice_tick`
        // missed roughly half of them and the rows printed a dash where their numbers belong. The
        // payloads inside a decision's own tick span are its own; the last settled one is what it
        // concluded, and the last of any is what it was still doing when the identity moved on.
        CourseDecision[] payloads = course.Decisions.OrderBy(d => d.Tick).ToArray();

        // The census admissions in tick order, carried forward, for the reason
        // ACensusAdmissionSurvivesItsBinder states: the producer's own Admitted list persists while a
        // retained course is carried, and the `decision` occurrence that publishes it is periodic.
        var admissions = new List<(long Tick, string Text)>();
        foreach (GodsEyeEvent e in log.Events)
        {
            if (!string.Equals(e.kind, "decision", StringComparison.Ordinal)) continue;
            var admitted = ACensusAdmissionSurvivesItsBinder.ReadAdmissions(e.detail);
            if (admitted.Count > 0) admissions.Add((e.tick, Summarise(admitted)));
        }

        Column id = session["choice_id"], decided = session["choice_tick"], action = session["action"], cost = session["decide_ms"];
        Column? order = session.Find("task_order"), runnerUp = session.Find("task_order_runner_up");

        var built = new List<Decision>();
        int admissionAt = 0, payloadAt = 0;
        string census = "-";
        for (int start = 0, end; start < session.Count; start = end + 1)
        {
            end = start;
            while (end + 1 < session.Count && id.Text[end + 1] == id.Text[start]) end++;
            if (JoinAttemptEvidence.LongAt(id, start) is not long identity || identity <= 0) continue;

            long from = session.Tick(start), to = session.Tick(end);
            while (admissionAt < admissions.Count && admissions[admissionAt].Tick <= from)
                census = admissions[admissionAt++].Text;

            long at = (long)(float.IsNaN(decided.Number[start]) ? from : decided.Number[start]);
            while (payloadAt < payloads.Length && payloads[payloadAt].Tick < from) payloadAt++;
            var (payload, released) = PickFromSpan(payloads, payloadAt, to);

            var costs = Enumerable.Range(start, end - start + 1)
                .Select(i => (double)cost.Number[i]).Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
            double median = costs.Length == 0 ? 0 : costs[costs.Length / 2];
            double worst = costs.Length == 0 ? 0 : costs[^1];

            string ordered = order is null ? "-" : order.Text[start];
            if (runnerUp is not null && runnerUp.Text[start] is { Length: > 0 } second && second != "-")
                ordered = ordered + " > " + second;

            built.Add(new Decision(identity, from, to, at, action.Text[start],
                payload is not null, payload?.Settled ?? false, payload?.Steps ?? 0, payload?.OrdersPriced ?? 0,
                payload?.OrdersRefused ?? 0, payload?.SearchExhausted ?? false,
                released is not null ? $"{released.ReleaseReason}@{released.Tick:n0}" : None,
                census, ordered, median, worst));
        }
        return built;
    }

    /// <summary>
    /// The record a decision's numbers come from and the record its release comes from, out of the payloads
    /// traced inside its tick span. <b>The two come off different records, because the producer never writes
    /// them on the same one</b> (see the release paragraph on the class): the numbers are the last settled
    /// payload, or the last of any where none settled, and the release is the last payload carrying one.
    /// Internal because <see cref="ExplainOneTick"/> reads a single decision through the same join, and two
    /// joins of one record are how the table and the explanation would come to disagree.
    /// </summary>
    /// <param name="ordered">Every payload of the capture, in tick order.</param>
    /// <param name="first">The index of the first payload at or after the span's first tick.</param>
    /// <param name="to">The span's last tick.</param>
    internal static (CourseDecision? Numbers, CourseDecision? Released) PickFromSpan(IReadOnlyList<CourseDecision> ordered, int first, long to)
    {
        CourseDecision? payload = null, released = null;
        for (int p = first; p < ordered.Count && ordered[p].Tick <= to; p++)
        {
            if (payload is null || ordered[p].Settled || !payload.Settled) payload = ordered[p];
            if (ordered[p].ReleaseReason.Length > 0) released = ordered[p];
        }
        return (payload, released);
    }

    /// <summary>Every domain's admission as <c>domain=usable/unknown/unusable</c>, shortest first so the
    /// line is stable between decisions and a change in it is visible rather than a reshuffle.</summary>
    private static string Summarise(Dictionary<string, (long Usable, string Reason)> admitted)
        => string.Join(" ", admitted.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key.Replace("-target", "", StringComparison.Ordinal)}={p.Value.Usable}"));

    private static List<Run> Coalesce(List<Decision> decisions)
    {
        var runs = new List<Run>();
        Decision open = decisions[0];
        long from = open.FromTick, to = open.ToTick;
        int count = 1, undescribed = open.HasPayload ? 0 : 1;
        double worst = open.WorstCostMs;
        string release = open.Release;
        for (int i = 1; i < decisions.Count; i++)
        {
            Decision next = decisions[i];
            if (Same(open, release, next))
            {
                to = next.ToTick; count++; worst = Math.Max(worst, next.WorstCostMs);
                // The run's release is whichever of its members traced one; they agree by
                // construction, because a run only folds across decisions whose released reasons
                // match or whose spans traced none at all.
                if (release == None) release = next.Release;
                if (!next.HasPayload) undescribed++;
                // A run whose first decision traced nothing takes the first that did as its
                // representative, so the row's numbers are numbers rather than dashes.
                else if (!open.HasPayload) open = next;
                continue;
            }
            runs.Add(new Run(from, to, count, open, worst, undescribed, release));
            open = next; from = next.FromTick; to = next.ToTick; count = 1; worst = next.WorstCostMs;
            undescribed = next.HasPayload ? 0 : 1;
            release = next.Release;
        }
        runs.Add(new Run(from, to, count, open, worst, undescribed, release));
        return runs;
    }

    /// <summary>
    /// What two decisions have to share to be one row. Deliberately not the decision cost or the tick:
    /// those move on every decision in a live session and keying on them gives one row per decision,
    /// which is the table this page exists to avoid. The worst cost in a run rides on the row instead,
    /// so a fold cannot hide a spike.
    ///
    /// <para><b>A decision that traced no payload is not a difference.</b> The producer writes a payload
    /// when an outcome is traced rather than on every tick, so a one-tick decision can land between two
    /// traces and carry none — 260 of the 22 September 2026 capture's 1,092 do. Treating that absence as
    /// a distinguishing value split the table into a hundred runs of which a third were one undescribed
    /// decision each, which is the per-tick log this page exists to replace. Comparison against the
    /// run's representative rather than against the previous decision is what keeps it honest: an
    /// undescribed decision joins whatever run it sits in, and the next described one is still compared
    /// against the run's own numbers rather than against a gap.</para>
    ///
    /// <para><b>A span that traced no release is not a decision that was not released</b>, for the same
    /// reason and with the same cost. The release rides on a record written after publication, so a
    /// decision whose identity moves on before that record lands carries none — 463 of the 22 September
    /// 2026 capture's 1,092 do. Treating that dash as a value split the first four hundred ticks into
    /// alternating one-decision runs (57 runs became 81) while nothing about the course had changed, so
    /// two release reasons are compared only when both are there.</para>
    /// </summary>
    /// <param name="releaseSoFar">The release the open run has already accumulated, which is not
    /// <paramref name="a"/>'s own: the representative's span may have traced none while a later member's
    /// did, and comparing against the representative then lets a third member's *different* reason fold
    /// in silently under the second's. No capture on disk exhibits it — it is closed by construction
    /// rather than after a sighting, because the symptom is a reason that is simply absent from a table
    /// nobody can check against the producer without writing a script.</param>
    private static bool Same(Decision a, string releaseSoFar, Decision b)
    {
        if (!string.Equals(a.Bound, b.Bound, StringComparison.Ordinal)
            || !string.Equals(a.Census, b.Census, StringComparison.Ordinal)
            || !string.Equals(a.Order, b.Order, StringComparison.Ordinal)) return false;
        if (!a.HasPayload || !b.HasPayload) return true;
        return a.Settled == b.Settled && a.Steps == b.Steps && a.Priced == b.Priced
            && a.Refused == b.Refused && a.Exhausted == b.Exhausted
            && (releaseSoFar == None || b.Release == None
                || string.Equals(Reason(releaseSoFar), Reason(b.Release), StringComparison.Ordinal));
    }

    /// <summary>A release without its tick, because the tick moves every decision and the reason does not.</summary>
    private static string Reason(string release)
    {
        int at = release.LastIndexOf('@');
        return at < 0 ? release : release[..at];
    }

    private static string Fit(string value, int width)
        => value.Length <= width ? value.PadRight(width) : value[..(width - 1)] + "…";
}
