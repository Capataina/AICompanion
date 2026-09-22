#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

// Threshold-free contradiction checks from Proposal 1's P12: each asks whether two records the
// producer guarantees to agree actually do. None of them measures a duration or a rate, so none has
// a number to tune, and each rule below names the producer line that makes its violation impossible.

/// <summary>
/// A selected activity must have carried a usable or unresolved offer.
///
/// <para><b>The producers this rule was derived from no longer exist, and the rule is kept for the
/// captures they wrote.</b> On a recording made before 22 September 2026 the guarantee came from three
/// places that <c>AIC-419</c> has since deleted with the family chooser: the evaluator returned
/// <c>value-without-eligible-offer</c> for positive value beside any other eligibility
/// (<c>EvaluatePreparedActivities</c>), a family nominated only positive final value
/// (<c>NominateFamilyActivities</c>), and a Deferred child was written with zero value
/// (<c>ChooseBehaviour</c>) — so on those captures a selection whose own offer column read anything
/// else, <c>not-compared</c> included, contradicted the brain that wrote it. None of those three files
/// is in the tree, and this paragraph is history rather than a pointer: following it to check the rule
/// leads nowhere, which is exactly why it says so instead of naming them in the present tense.</para>
///
/// <para>Freshness is read from <c>choice_fresh</c>, not <c>brain_fresh</c>: a safety, recovery or
/// downed tick runs the brain without a comparison. On those same captures it barely mattered, because
/// the <c>action</c> column was <c>Chooser.Current</c> and changed only inside <c>Chooser.Choose</c>,
/// which rebuilt the score board the offer columns were read from and then incremented the comparison
/// identity, so a retained row restated its comparison's label and offers together. The check judges
/// each comparison once, on its fresh row where one was captured, and counts its retained rows as one
/// contradiction rather than one per row — that part is the check's own arithmetic and holds
/// whatever wrote the rows.</para>
///
/// <para><b>Schema 0.44.0 repointed the column this check is entirely about, and the paragraph above
/// describes the producer that no longer writes it.</b> <c>&lt;activity&gt;_offer</c> is the course's
/// own three-valued census admission now, not the chooser's eligibility, and
/// <c>ReadCourseWorthPerActivity</c> writes the literal <c>not-compared</c> for any activity the course
/// mints no domain for — which keeping company always is, because an empty course <em>is</em>
/// companionship, and which any domain is on a decision whose census carried no entry for it. So on a
/// 0.44.0 capture <c>not-compared</c> beside a selected activity is the honest reading and not a
/// contradiction: the 22 September 2026 capture produced 875 Definitive findings from this one rule,
/// every one of them keeping company reading the word its producer is documented to write. What stays
/// a contradiction at that schema is a selected activity whose own domain census read
/// <c>KnownUnusable</c> or <c>NoOpportunity</c> — the course binding a step in a domain it had itself
/// proved unusable — and that is the form the check keeps.</para>
/// </summary>
public sealed class SelectedActivitiesHadAnEligibleOffer : ICheck, ICheckCoverage
{
    public string Name => "was every selected activity's own offer usable or unresolved";
    public string[] Needs => new[] { "action", "choice_id", "choice_fresh" };

    public string? Missing(Session session)
        => session.Names.Any(name => name.EndsWith("_offer", StringComparison.Ordinal)) ? null
            : "<activity>_offer eligibility columns (first written by schema 0.20.0)";

    /// <summary>The schema at which <c>&lt;activity&gt;_offer</c> stopped being the chooser's eligibility
    /// and became the course's census admission, which is what makes <c>not-compared</c> honest.</summary>
    internal static readonly Version CourseAdmission = new(0, 44, 0);

    public IEnumerable<Finding> Run(Session s)
    {
        bool course = CompletedTransferClaimsWereReceived.SchemaAtLeast(s, CourseAdmission);
        Column action = s["action"], choice = s["choice_id"], fresh = s["choice_fresh"];
        for (int start = 0, end; start < s.Count; start = end + 1)
        {
            end = start;
            while (end + 1 < s.Count && choice.Text[end + 1] == choice.Text[start]) end++;
            if (JoinAttemptEvidence.LongAt(choice, start) is not long id || id <= 0) continue;
            int anchor = start;
            while (anchor <= end && fresh.Number[anchor] != 1f) anchor++;
            bool freshRow = anchor <= end;
            if (!freshRow) anchor = start;
            string selected = action.Text[anchor];
            if (selected is "-" or "") continue;
            string where = $"Comparison {id}, {(freshRow ? "its fresh row" : "no fresh row was captured, so its first retained row")} at tick {s.Tick(anchor):n0}, spanning {end - start + 1:n0} row(s)";
            if (s.Find(selected + "_offer") is not { } offer)
            {
                yield return new Finding(Severity.Potential, Name, $"the selected {selected} has no offer column",
                    $"{where}. The writer names offer columns from the same registration list as the action names, so a selected activity without one is a writer and reader disagreeing; nothing about its eligibility was measured.",
                    s.Tick(start), s.Tick(end), end - start + 1);
                continue;
            }
            string cell = offer.Text[anchor];
            int colon = cell.IndexOf(':');
            string eligibility = colon < 0 ? cell : cell[..colon];
            if (eligibility is "Usable" or "Unresolved") continue;
            // From 0.44.0 the column is the course's census admission and `not-compared` means the
            // course minted no domain for this activity on this decision — which keeping company always
            // is. Exempting it here rather than skipping the whole check keeps the two readings that are
            // still contradictions at that schema.
            if (course && eligibility == "not-compared") continue;
            yield return new Finding(Severity.Definitive, Name, $"the selected {selected} carried a {eligibility} offer",
                course
                    ? $"{where}, selected {selected} while its own course census admission read '{cell}'. Since schema {CourseAdmission} that column is the course's three-valued census admission, so a bound step in a domain the same decision's census proved unusable is the census and the binding disagreeing. `not-compared` is exempt at this schema and is not this finding: it means the course minted no domain for the activity, which keeping company always is."
                    : $"{where}, selected {selected} while its own offer column read '{cell}'. Only a usable or unresolved offer may carry the positive value selection requires, so this selection contradicts its recorded offer. The label and offers are retained together from one comparison, so the retained rows restate this one contradiction.",
                s.Tick(start), s.Tick(end), end - start + 1,
                $"the selected {selected} carried a {eligibility} offer");
        }
    }
}

/// <summary>
/// The selected activity changes only when a comparison completes — on the captures this still grades,
/// which are the ones a family chooser wrote. The current activity was <c>Chooser.Current</c>, set in
/// exactly one place, <c>OwnCurrentActivity.Select</c> called from <c>Chooser.Choose</c>, which then
/// incremented the comparison identity; so two consecutive rows of such a capture sharing a
/// <c>choice_id</c> with different actions are a label that moved without a decision. A respawned brain
/// restarts its identities, which changes the id and ends the run. EngineReplay fixtures call Select
/// directly and are not playtest recordings.
///
/// <c>Chooser</c> is gone — <c>AIC-419</c> deleted it on 22 September 2026 — and <c>Select</c> is now
/// called from <c>CoordinateBrainTick</c> with no comparison identity of its own to increment, which is
/// the mechanical reason the guarantee below retires rather than the schema being a convention.
///
/// <para><b>Schema 0.43.0 gave <c>choice_id</c> to the course, and that retires the guarantee rather
/// than weakening it.</b> The column reads <c>DecideCourseEachTick.DecisionId</c> now and advances once
/// per decision *reached*, while the tick's activity is the bound step's or the fallback the tick takes
/// while a decision is in flight — so a retained course carried across an activity change is legal by
/// construction, and the rule above has nothing left to contradict. A capture at that schema or later
/// is skipped by name, and <see cref="TheBoundActivityHoldsWhileOneDecisionRuns"/> asks the course's own
/// version of the question. Skipping rather than deleting is deliberate: every capture on disk older
/// than 21 September 2026 carries the chooser's identity and this rule still grades it.</para>
/// </summary>
public sealed class ARetainedChoiceKeepsItsSelection : ICheck, ICheckCoverage
{
    public string Name => "does a selection change only with a new comparison";
    public string[] Needs => new[] { "action", "choice_id" };

    /// <summary>The schema at which <c>choice_id</c> stopped being the chooser's comparison identity.</summary>
    internal static readonly Version CourseDecisionIdentity = new(0, 43, 0);

    public string? Missing(Session session)
        => CompletedTransferClaimsWereReceived.SchemaAtLeast(session, CourseDecisionIdentity)
            ? $"comparison identity the family chooser advanced: from schema {CourseDecisionIdentity} `choice_id` is the course's decision identity, which a changing activity does not contradict"
            : null;

    public IEnumerable<Finding> Run(Session s)
    {
        Column action = s["action"], choice = s["choice_id"];
        for (int i = 1; i < s.Count; i++)
        {
            if (choice.Text[i] != choice.Text[i - 1] || action.Text[i] == action.Text[i - 1]) continue;
            yield return new Finding(Severity.Definitive, Name, $"the selected activity changed from {action.Text[i - 1]} to {action.Text[i]} without a new comparison",
                $"Ticks {s.Tick(i - 1):n0} and {s.Tick(i):n0} both carry comparison {choice.Text[i]}. The current activity is replaced only by a completed comparison, which also advances this identity, so one of the two rows describes a decision that did not happen.",
                s.Tick(i - 1), s.Tick(i), 2, "the selected activity changed without a new comparison");
        }
    }
}

/// <summary>
/// The course's own version of the question above, and a different question rather than the same one
/// renamed. Since schema 0.43.0 <c>choice_id</c> advances once per decision *reached*, so consecutive
/// rows sharing it are one decision — which may be a published course being carried, or a decision that
/// has not settled yet. The activity may legitimately change across the first. It should not change
/// across the second, and when it does the change is the loop the 22 September 2026 capture recorded:
/// a decision in flight selects keeping company, which exits combat, which releases its committed plan
/// with <c>activity-exited</c>, which makes the course's accepted use absent, which releases the course
/// and starts another decision that has not settled either.
///
/// <para><b>It is Potential and not Definitive, because the record cannot separate the two cases on its
/// own.</b> A row carries the decision identity and the activity and does not carry whether that
/// decision was settled; the settled flag lives on the <c>course-decision</c> occurrence. So this counts
/// the transitions and names what would settle it — on a schema 0.45.0 capture the recorder's own
/// <c>activity-exited-during-decision</c> contract violation answers the same question from the
/// producer's side, keyed on the transition into an unsettled decision, and the two agreeing is what
/// turns this count into an observation.</para>
///
/// <para>One finding for the whole session rather than one per transition. The 22 September capture
/// holds 214 of them and each says the same thing; the report's fold would collapse them anyway, and a
/// check that knows it is counting a session-wide pattern should say so in its own numbers.</para>
/// </summary>
public sealed class TheBoundActivityHoldsWhileOneDecisionRuns : ICheck, ICheckCoverage
{
    public string Name => "did the bound activity hold while one decision ran";
    public string[] Needs => new[] { "action", "choice_id" };

    public string? Missing(Session session)
        => CompletedTransferClaimsWereReceived.SchemaAtLeast(session, ARetainedChoiceKeepsItsSelection.CourseDecisionIdentity)
            ? null
            : $"course decision identity in `choice_id` (first written by schema {ARetainedChoiceKeepsItsSelection.CourseDecisionIdentity}); before it the column is the family chooser's comparison identity";

    public IEnumerable<Finding> Run(Session s)
    {
        Column action = s["action"], choice = s["choice_id"];
        var transitions = new Dictionary<string, int>(StringComparer.Ordinal);
        int count = 0, first = -1, last = -1;
        for (int i = 1; i < s.Count; i++)
        {
            if (choice.Text[i] != choice.Text[i - 1] || action.Text[i] == action.Text[i - 1]) continue;
            string move = $"{action.Text[i - 1]}→{action.Text[i]}";
            transitions[move] = transitions.TryGetValue(move, out int n) ? n + 1 : 1;
            count++;
            if (first < 0) first = s.Tick(i - 1);
            last = s.Tick(i);
        }
        if (count == 0) yield break;

        string split = string.Join(", ", transitions.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value:n0}×"));
        yield return new Finding(Severity.Potential, Name,
            $"the bound activity changed {count:n0} time(s) inside a decision identity that did not advance",
            $"Ticks {first:n0}..{last:n0}; the split is {split}. Since schema "
                + $"{ARetainedChoiceKeepsItsSelection.CourseDecisionIdentity} `choice_id` advances once per decision reached, "
                + "so two consecutive rows sharing it are one decision — a published course being carried, where an activity "
                + "change is legal, or a decision that has not settled, where it is the flicker loop: the deciding tick selects "
                + "keeping company, combat exits, its plan releases with activity-exited, the course's accepted use goes absent "
                + "and the course releases into another unsettled decision. The row cannot tell those two apart, which is why "
                + "this is Potential and carries no threshold. What would settle it: the `course-decision` occurrence's settled "
                + "flag on each of these ticks, and on a schema 0.45.0 capture the recorder's own "
                + "`activity-exited-during-decision` contract violation, which keys on the transition into an unsettled decision "
                + "and answers this from the producer's side.",
            first, last, count);
    }
}

/// <summary>
/// One attempt is named consistently by every record that names it. The rules, each a producer guarantee:
///
///   outcome order   attempt outcomes are written in strictly increasing attempt identity, because
///                   the writer's cursor drops any id not above the last written
///                   (RecordGodsEyeEvents.RecordAttemptOutcome) and ids come from one process-wide
///                   counter (OwnCurrentActivity); a repeat or a lower id is two records for one attempt
///   one activity    an attempt id never names two activity ids or two activity names across its
///                   outcome, its grants, a tool effect whose row names it open, and the rows'
///                   attempt_end_* columns; activity ids restart with a new brain, but attempt ids do
///                   not, so the attempt id is the key and no epoch is needed
///   grant interval  a grant carrying attempt X was issued inside X's recorded [start, end], since a
///                   grant names only the attempt open at finalisation
///   row interval    every row strictly inside (start, end) names X as the open attempt; the end
///                   points are not judged, so the rule holds whichever side of the row the open or
///                   close happened on
///   tool choice     a tool effect's choice-id equals the choice_id of the row at its tick: the
///                   comparison identity advances before Execute strikes and nothing else advances
///                   it that tick (ChooseBehaviour, then CoordinateBrainTick)
///   tool attempt    a tool effect carrying activity-attempt-id (schema 0.25.0) names a non-zero
///                   attempt, that attempt is the one its tick's row names open or names closed on
///                   that tick, and its tick lies inside that attempt's recorded interval: only an
///                   executing activity strikes, Execute runs after BeginExecution opened the attempt
///                   (CoordinateBrainTick), and the row is written after the brain in the same update
///
/// Deliberately not a rule: an outcome whose attempt never appears in any row or grant. An attempt
/// can open at BeginExecution and be suspended by recovery later in the same tick, so no row or grant
/// ever carries it, and that is correct behaviour.
/// </summary>
public sealed class AttemptIdentitiesAgreeAcrossRecords : ICheck, ICheckCoverage
{
    public string Name => "does every record name each attempt under one activity and inside its interval";
    public string[] Needs => new[] { "tick", "choice_id", "activity_attempt_id", "attempt_end_id", "attempt_end_activity_id", "attempt_end_activity", "attempt_end_tick" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the attempt outcomes, grants and tool effects");

    public IEnumerable<Finding> Run(Session s)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(s.Path);
        AttemptJoin join = JoinAttemptEvidence.Build(s, log);
        Dictionary<long, int> rowAt = JoinAttemptEvidence.RowsByTick(s);
        var findings = new List<Finding>();

        var outOfOrder = new List<GodsEyeEvent>();
        long highest = 0;
        foreach (GodsEyeEvent e in log.Events.Where(e => e.kind == "attempt-outcome"))
        {
            if (!ReadGodsEyeEvents.TryLong(e.Field("attempt-id"), out long id) || id <= 0) continue;
            if (id <= highest) outOfOrder.Add(e); else highest = id;
        }
        if (outOfOrder.Count > 0)
            findings.Add(Aggregate(Severity.Definitive, "attempt outcomes were not written in strictly increasing attempt identity", outOfOrder,
                "The outcome writer skips any attempt id not above the last it wrote and ids come from one process-wide counter, so a repeated or lower id means two records claim one attempt or two streams were spliced."));
        if (join.OutcomesWithoutIdentity.Count > 0)
            findings.Add(Aggregate(Severity.Potential, "attempt outcomes carry no readable attempt identity", join.OutcomesWithoutIdentity,
                "These conclusions cannot be joined to their grants or effects, so nothing about them was checked."));

        // One attempt, one activity.
        var claims = new Dictionary<long, (long ActivityId, string? Activity, string Source, long Tick)>();
        var conflicts = new SortedDictionary<long, List<string>>();
        void Claim(long attempt, long activityId, string? activity, string source, long tick)
        {
            if (!claims.TryGetValue(attempt, out var first))
            {
                claims[attempt] = (activityId, activity, source, tick);
                return;
            }
            bool idDiffers = first.ActivityId != activityId;
            bool nameDiffers = activity != null && first.Activity != null && activity != first.Activity;
            if (idDiffers || nameDiffers)
            {
                if (!conflicts.TryGetValue(attempt, out var list))
                    conflicts[attempt] = list = new List<string> { $"{first.Source} at tick {first.Tick} names activity {first.ActivityId}{(first.Activity == null ? "" : " " + first.Activity)}" };
                list.Add($"{source} at tick {tick} names activity {activityId}{(activity == null ? "" : " " + activity)}");
            }
            else if (first.Activity == null && activity != null)
                claims[attempt] = first with { Activity = activity };
        }
        foreach (AttemptEvidence attempt in join.Attempts.Values)
        {
            foreach (GodsEyeEvent outcome in attempt.Outcome == null ? attempt.DuplicateOutcomes : attempt.DuplicateOutcomes.Prepend(attempt.Outcome))
                if (ReadGodsEyeEvents.TryLong(outcome.Field("activity-id"), out long activity))
                    Claim(attempt.AttemptId, activity, outcome.label, "attempt-outcome", outcome.tick);
            foreach (GodsEyeEvent grant in attempt.Grants)
                if (ReadGodsEyeEvents.TryLong(grant.Field("activity-id"), out long activity))
                    Claim(attempt.AttemptId, activity, null, "control-grant", grant.tick);
            foreach (var (effect, route) in attempt.ToolEffects)
                if (route is ToolEffectRoute.OpenAttemptRow or ToolEffectRoute.ProducerAttemptId && ReadGodsEyeEvents.TryLong(effect.ChannelField("activity-id"), out long activity))
                    Claim(attempt.AttemptId, activity, null, route == ToolEffectRoute.ProducerAttemptId ? "tool-effect naming it" : "tool-effect on a row naming it open", effect.tick);
        }
        Column endId = s["attempt_end_id"], endActivityId = s["attempt_end_activity_id"], endActivity = s["attempt_end_activity"];
        for (int i = 0; i < s.Count; i++)
        {
            // The attempt_end_* columns are sticky; claim each retained conclusion once per change.
            if (i > 0 && endId.Text[i] == endId.Text[i - 1] && endActivityId.Text[i] == endActivityId.Text[i - 1] && endActivity.Text[i] == endActivity.Text[i - 1]) continue;
            if (JoinAttemptEvidence.LongAt(endId, i) is long attemptId && attemptId > 0 && JoinAttemptEvidence.LongAt(endActivityId, i) is long activityId)
                Claim(attemptId, activityId, endActivity.Text[i], "row attempt_end_*", s.Tick(i));
        }
        foreach (var (attempt, list) in conflicts)
            findings.Add(new Finding(Severity.Definitive, Name, $"attempt {attempt} is named under two activities",
                string.Join("; ", list) + ". Attempt identities are unique across every activity owner in the process, so one of these records misattributes the attempt, and anything credited or charged to it is being read across records that disagree.",
                (int)Math.Min(int.MaxValue, join.Attempts.TryGetValue(attempt, out var named) ? named.FirstTick : 0), (int)Math.Min(int.MaxValue, named?.LastTick ?? 0), list.Count));

        // Grants and rows inside the attempt's recorded interval.
        long[] rowTicks = new long[s.Count];
        bool ordered = true;
        Column tick = s["tick"], open = s["activity_attempt_id"];
        for (int i = 0; i < s.Count; i++)
        {
            rowTicks[i] = JoinAttemptEvidence.LongAt(tick, i) ?? long.MinValue;
            if (i > 0 && rowTicks[i] < rowTicks[i - 1]) ordered = false;
        }
        if (!ordered)
            findings.Add(new Finding(Severity.Potential, Name, "rows are not in tick order, so rows were not checked against attempt intervals",
                "The interval rule needs rows ordered by engine tick; TicksAdvance reports where the order breaks. Grant intervals and identities were still checked.", 0, 0, 0));
        foreach (AttemptEvidence attempt in join.Attempts.Values)
        {
            if (attempt.StartTick is not long start || attempt.EndTick is not long end) continue;
            var outside = attempt.Grants.Where(g => (ReadGodsEyeEvents.TryLong(g.Field("grant-tick"), out long at) ? at : g.tick) is var t && (t < start || t > end)).ToList();
            if (outside.Count > 0)
                findings.Add(Aggregate(Severity.Definitive, $"a control grant under attempt {attempt.AttemptId} lies outside its recorded ticks {start}..{end}", outside,
                    "A grant names only the attempt open when controls were finalised, so a grant carrying this attempt before it began or after it ended misnames the attempt or the attempt's interval."));
            var strikesOutside = attempt.ToolEffects.Where(t => t.Route == ToolEffectRoute.ProducerAttemptId && (t.Effect.tick < start || t.Effect.tick > end)).Select(t => t.Effect).ToList();
            if (strikesOutside.Count > 0)
                findings.Add(Aggregate(Severity.Definitive, $"a tool effect naming attempt {attempt.AttemptId} lies outside its recorded ticks {start}..{end}", strikesOutside,
                    "A strike names only the attempt open when Execute struck, and an attempt is open from its start tick to its end tick, so a strike naming it outside that interval misnames the attempt or the attempt's interval."));
            if (!ordered || end - start < 2) continue;
            int first = LowerBound(rowTicks, start + 1), mismatched = 0, firstMismatch = -1;
            // Only a row that names an attempt can name a different one: an unreadable cell is a damaged record, which the
            // column audit reports, and comparing its null to the attempt would call that damage a contradiction.
            for (int r = first; r < rowTicks.Length && rowTicks[r] < end; r++)
                if (JoinAttemptEvidence.LongAt(open, r) is long named && named != attempt.AttemptId) { mismatched++; if (firstMismatch < 0) firstMismatch = r; }
            if (mismatched > 0)
                findings.Add(new Finding(Severity.Definitive, Name, $"rows inside attempt {attempt.AttemptId} name a different open attempt",
                    $"{mismatched:n0} row(s) strictly between its start tick {start} and end tick {end} do not carry it as activity_attempt_id; the first, at tick {rowTicks[firstMismatch]}, carries '{open.Text[firstMismatch]}'. An attempt stays open from BeginExecution until suspension or replacement closes it, and the owner holds one attempt at a time.",
                    s.Tick(firstMismatch), (int)Math.Min(int.MaxValue, end), mismatched));
        }

        // A strike belongs to the comparison recorded on its tick.
        Column choice = s["choice_id"];
        var wrongChoice = log.Events.Where(e => e.kind == "tool-effect"
            && rowAt.TryGetValue(e.tick, out int row)
            && ReadGodsEyeEvents.TryLong(e.ChannelField("choice-id"), out long effectChoice)
            && JoinAttemptEvidence.LongAt(choice, row) is long rowChoice && rowChoice != effectChoice).ToList();
        if (wrongChoice.Count > 0)
            findings.Add(Aggregate(Severity.Definitive, "tool effects name a comparison their own tick's row does not", wrongChoice,
                "Execute strikes after that tick's comparison has advanced the identity, the row is written after the brain in the same NPC update, and nothing else advances the identity between them, so the two must agree."));

        // A strike that names its own attempt names the one open when it struck.
        if (join.ToolEffectsWithNoOpenAttempt.Count > 0)
            findings.Add(Aggregate(Severity.Definitive, "tool effects were struck with no attempt open", join.ToolEffectsWithNoOpenAttempt,
                "Only an executing activity strikes, and Execute runs after BeginExecution has opened an attempt for the current activity, so a strike naming attempt zero ran outside any attempt and its effect is credited to nobody's purpose."));
        Column closedTick = s["attempt_end_tick"];
        var wrongAttempt = join.Attempts.Values
            .SelectMany(a => a.ToolEffects.Where(t => t.Route == ToolEffectRoute.ProducerAttemptId).Select(t => (a.AttemptId, t.Effect)))
            .Where(x => rowAt.TryGetValue(x.Effect.tick, out int row) && JoinAttemptEvidence.LongAt(open, row) is long rowOpen && rowOpen != x.AttemptId
                // The row must say readably that it closed some other attempt, or none, on another tick: unreadable end cells
                // may have held exactly the closure that makes this strike right, so they leave the strike unjudged.
                && JoinAttemptEvidence.LongAt(endId, row) is long endAttempt && JoinAttemptEvidence.LongAt(closedTick, row) is long endAt
                && !(endAttempt == x.AttemptId && endAt == x.Effect.tick))
            .Select(x => x.Effect).OrderBy(e => e.seq).ToList();
        if (wrongAttempt.Count > 0)
            findings.Add(Aggregate(Severity.Definitive, "tool effects name an attempt their own tick's row does not", wrongAttempt,
                "The row at a strike's tick is written after the brain in the same update, so it names the attempt the strike named as still open or as closed on that tick; naming neither means the strike or the row misattributes the attempt."));
        return findings;
    }

    internal Finding Aggregate(Severity severity, string title, List<GodsEyeEvent> events, string explanation)
        => CheckEvents.Aggregate(severity, Name, title, events, explanation);

    private static int LowerBound(long[] values, long target)
    {
        int lo = 0, hi = values.Length;
        while (lo < hi) { int mid = (lo + hi) >>> 1; if (values[mid] < target) lo = mid + 1; else hi = mid; }
        return lo;
    }
}

/// <summary>
/// A claimed transfer arrived. A collection attempt claims a yield only from what the cargo's transfer ledger received
/// from the drop it walked to (CollectNearbyItems.ConcludeAttempt), every accepted transfer happens inside
/// CompanionNPC.CollectTouchedItems, and that site writes one pickup per transfer naming the open collection attempt
/// when the item is that drop. So the pickups recorded under an attempt, of the claimed type, deliver at least the
/// claimed quantity. The rule is one-sided on purpose: the ledger keeps only the latest transfers, so a claim can fall
/// short of what arrived and that is not reported, while a claim above what arrived names a transfer nobody recorded.
/// It is Definitive only over a whole occurrence stream; a missing sequence, a malformed line or a stream that never
/// closed could have lost the pickup, and then it is Potential.
/// </summary>
public sealed class CompletedTransferClaimsWereReceived : ICheck, ICheckCoverage
{
    public string Name => "did every claimed transfer arrive as pickups under its own attempt";
    public string[] Needs => new[] { "tick", "activity_attempt_id" };

    public string? Missing(Session session)
    {
        if (CheckEvents.SidecarUnavailable(session, "the attempt outcomes and pickups") is { } unavailable) return unavailable;
        return SchemaAtLeast(session, new Version(0, 26, 0)) ? null
            : "claimed yields on attempt outcomes and collection attempts on pickups (first written by schema 0.26.0)";
    }

    public IEnumerable<Finding> Run(Session s)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(s.Path);
        AttemptJoin join = JoinAttemptEvidence.Build(s, log);
        bool whole = log.MissingSequences == 0 && log.Malformed == 0 && log.Closed;
        foreach (AttemptEvidence attempt in join.Attempts.Values)
        {
            if (attempt.Outcome is not { } outcome || attempt.ClaimedYieldQuantity <= 0) continue;
            if (outcome.Field("status") is not ("Complete" or "Partial")) continue;
            int received = attempt.ReceivedOf(attempt.ClaimedYieldType);
            if (received >= attempt.ClaimedYieldQuantity) continue;
            string others = attempt.Pickups.Count == 0 ? "no pickup names this attempt"
                : "pickups naming it: " + string.Join(", ", attempt.Pickups.Select(p => $"tick {p.tick} item {p.label}×{p.amount}"));
            string coverage = whole ? "The occurrence stream is whole (no missing sequence, no malformed line, closed normally), so no pickup was lost from the record."
                : $"The occurrence stream is not whole ({log.MissingSequences} missing sequence(s), {log.Malformed} malformed line(s), {(log.Closed ? "closed" : "never closed")}), so a pickup may have been lost from the record rather than never made.";
            yield return new Finding(whole ? Severity.Definitive : Severity.Potential, Name,
                $"a completed transfer claim with no matching received quantity: attempt {attempt.AttemptId} ({outcome.label}, {outcome.channel}) claimed {attempt.ClaimedYieldQuantity} of item {attempt.ClaimedYieldType} and its pickups delivered {received}",
                $"Ticks {outcome.Field("start-tick") ?? "?"}..{outcome.Field("end-tick") ?? "?"}; {others}. The claim is read from the cargo's own transfer ledger and every transfer writes a pickup naming its collection attempt, so a claim above what those pickups delivered credits the companion with items no recorded transfer carried. {coverage}",
                (int)Math.Min(int.MaxValue, attempt.FirstTick), (int)Math.Min(int.MaxValue, attempt.LastTick), attempt.Pickups.Count);
        }
    }

    /// <summary>Whether the capture's recorded schema is at least <paramref name="minimum"/>; an unrecorded or unreadable schema is not.</summary>
    internal static bool SchemaAtLeast(Session session, Version minimum)
        => session.Metadata.TryGetValue("schema", out string? value) && Version.TryParse(value, out Version? recorded) && recorded >= minimum;

    /// <summary>
    /// The schema at which every column and board entry the family chooser wrote was removed, `AIC-419`
    /// having deleted the chooser itself on 22 September 2026.
    ///
    /// <para><b>Every other gate in this tool is a floor and a removal is the one thing a floor cannot
    /// see.</b> `SchemaAtLeast` answers "is this capture new enough to carry the evidence", which a
    /// 0.46.0 capture satisfies for every question ever asked of it — including the questions whose
    /// evidence 0.46.0 is precisely what took away. So a reader of a retired column needs a *ceiling*
    /// beside its floor, and the shape is the same: a named decline, never a silent empty answer. This
    /// folder already has the general form of that lesson written down — a check whose *producer* was
    /// replaced runs, finds plenty and is confidently wrong, where a check whose *column* was removed
    /// skips and says so — and a floor-only gate turns the second kind into the first.</para>
    ///
    /// <para>What went: `&lt;family&gt;_prepared` / `_deferred` / `_prepare_ms`, which nothing here read;
    /// `&lt;activity&gt;_time`, the per-activity time discount, pinned at 1.000 by decision at 0.44.0;
    /// `&lt;activity&gt;_funnel` and the `candidate-funnel` occurrence, a preparation-time shortlist the
    /// course does not keep; and the decision board's score list with its `factors:`, `family:` and
    /// `queries:` entries. The recorder's own comment at `RecordBrainTelemetry.cs:128` is the
    /// authority for that list and carries why each was safe to take.</para>
    /// </summary>
    internal static readonly Version ChooserColumnsRetired = new(0, 46, 0);

    /// <summary>Whether the capture predates <paramref name="retirement"/>, and so can still carry a
    /// column removed at it. An unrecorded or unreadable schema is treated as old, because every
    /// capture on disk that carries no readable schema predates all of this.</summary>
    internal static bool SchemaBelow(Session session, Version retirement)
        => !session.Metadata.TryGetValue("schema", out string? value)
            || !Version.TryParse(value, out Version? recorded) || recorded < retirement;
}

/// <summary>
/// Control grants whose owner, hand, attempt and phase the producer cannot issue together. The rules
/// constrain the <em>requested</em> owner, never the applied one: a downed hold can legitimately be
/// applied as recovery clearance inside terrain (CharacterBody), so the applied owner is not the branch
/// that issued the request.
///
///   WorkTool hand        only the ordinary branch issues it (CoordinateBrainTick TickPhases), and only
///                        after BeginExecution opened an attempt, so it never carries a suspending
///                        owner and never carries attempt zero
///   Unavailable hand     only the downed branch issues it, and the downed branch issues nothing else
///   suspending owners    follow-recovery-flight and downed (CoordinateBrainTick) suspend the activity
///                        before finalising, so they carry attempt zero. combat-reflex and combat-spacing
///                        suspended it too until the orb's safety became a layer on the job, and
///                        survival-escape until every liquid became air to the orb, both on 15 September
///                        2026; they stay in the set so a capture recorded before then is still judged by
///                        its own rules
///   evade                an ordinary owner: the job's own controls bent away from a predicted hit
///                        (CoordinateBrainTick after navigation), so it keeps the job's attempt and hand
///   attempt phase        an attempt is open only while the owner's phase is Executing
///   one per tick         one NPC update finalises controls once — CompanionNPC.AI takes the downed
///                        branch or the brain branch, never both; Terraria's UpdateNPC_Inner calls AI
///                        once and NPCs have no extra updates — so two grant ids rising within one
///                        engine tick are two finalisations; a falling id is a new brain and is not judged
///
/// A requested owner outside both known sets is reported as an oddity naming it rather than judged,
/// because a new ordinary owner is a producer change, not an impossibility.
/// </summary>
public sealed class ControlGrantsAreCompatible : ICheck, ICheckCoverage
{
    internal static readonly HashSet<string> OrdinaryOwners = new(StringComparer.Ordinal) { "travel", "seeking-destination", "hold", "evade", "accompany" };
    internal static readonly HashSet<string> SuspendingOwners = new(StringComparer.Ordinal) { "survival-escape", "combat-reflex", "combat-spacing", "follow-recovery-flight", "downed" };

    public string Name => "could the producer have issued every control grant as recorded";
    public string[] Needs => new[] { "control_grant_id", "activity_attempt_id" };

    public string? Missing(Session session)
        => CheckEvents.SidecarUnavailable(session, "the control grant occurrences");

    public IEnumerable<Finding> Run(Session s)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(s.Path);
        var incomplete = new List<GodsEyeEvent>();
        var workToolUnderSuspension = new List<GodsEyeEvent>();
        var workToolWithoutAttempt = new List<GodsEyeEvent>();
        var unavailableApart = new List<GodsEyeEvent>();
        var attemptUnderSuspension = new List<GodsEyeEvent>();
        var attemptNotExecuting = new List<GodsEyeEvent>();
        var twoInOneTick = new List<GodsEyeEvent>();
        var unknownOwners = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lastByTick = new Dictionary<long, long>();
        foreach (GodsEyeEvent g in log.Events.Where(e => e.kind == "control-grant"))
        {
            string? owner = g.Field("requested-owner"), hand = g.Field("hand"), phase = g.Field("activity-phase");
            if (owner == null || hand == null || phase == null
                || !ReadGodsEyeEvents.TryLong(g.Field("attempt-id"), out long attempt) || !ReadGodsEyeEvents.TryLong(g.Field("grant-id"), out long grantId))
            {
                incomplete.Add(g);
                continue;
            }
            long grantTick = ReadGodsEyeEvents.TryLong(g.Field("grant-tick"), out long at) ? at : g.tick;
            bool ordinary = OrdinaryOwners.Contains(owner), suspending = SuspendingOwners.Contains(owner);
            if (!ordinary && !suspending) unknownOwners[owner] = unknownOwners.TryGetValue(owner, out int n) ? n + 1 : 1;
            if (hand == "WorkTool" && suspending) workToolUnderSuspension.Add(g);
            if (hand == "WorkTool" && attempt == 0) workToolWithoutAttempt.Add(g);
            if ((hand == "Unavailable") != (owner == "downed")) unavailableApart.Add(g);
            if (attempt != 0 && suspending) attemptUnderSuspension.Add(g);
            if (attempt != 0 && phase != "Executing") attemptNotExecuting.Add(g);
            if (lastByTick.TryGetValue(grantTick, out long previous) && grantId > previous) twoInOneTick.Add(g);
            lastByTick[grantTick] = grantId;
        }

        if (workToolUnderSuspension.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Definitive, Name, "a work tool held the hand under a safety, recovery or downed request", workToolUnderSuspension,
                "Only the ordinary branch grants the hand to a work tool; every suspending branch requests an available or unavailable hand.");
        if (workToolWithoutAttempt.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Definitive, Name, "a work tool held the hand with no attempt open", workToolWithoutAttempt,
                "A work-tool grant follows BeginExecution in the same tick, which always opens an attempt, so the grant must carry it.");
        if (unavailableApart.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Definitive, Name, "the unavailable hand and the downed request came apart", unavailableApart,
                "The downed branch is the only issuer of an unavailable hand and always issues one; the applied owner may differ, the requested owner may not.");
        if (attemptUnderSuspension.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Definitive, Name, "a safety, recovery or downed request carried an open attempt", attemptUnderSuspension,
                "Each of these branches suspends the ordinary activity before finalising, which closes its attempt, so the grant cannot name one; charging this movement to that attempt would be wrong.");
        if (attemptNotExecuting.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Definitive, Name, "a grant carried an open attempt outside the Executing phase", attemptNotExecuting,
                "An attempt opens as the owner enters Executing and closes before it leaves, so an open attempt with another phase contradicts the owner.");
        if (twoInOneTick.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Definitive, Name, "two control finalisations were stamped with one engine tick", twoInOneTick,
                "One NPC update finalises controls once, so a second, higher grant id at the same grant tick means a second finalisation or a misstamped tick.");
        if (incomplete.Count > 0)
            yield return CheckEvents.Aggregate(Severity.Potential, Name, "control grants lack the fields these rules read", incomplete,
                "Owner, hand, phase, attempt and grant identity are all required; these occurrences were not checked.");
        if (unknownOwners.Count > 0)
            yield return new Finding(Severity.Oddity, Name, "control grants name requested owners the reader has no rule for",
                string.Join(", ", unknownOwners.Select(p => $"{p.Key} ×{p.Value:n0}")) + ". The known ordinary owners are travel, seeking-destination, hold, evade and accompany, and the suspending owners survival-escape, combat-reflex, combat-spacing, follow-recovery-flight and downed; these grants were judged only by the hand, phase and tick rules until the reader is told which branch issues them.",
                0, 0, unknownOwners.Values.Sum());
    }
}

internal static class CheckEvents
{
    /// <summary>
    /// Why an event-based check cannot run, or null when it can. An absent sidecar is one reason; a sidecar the recorder never
    /// opened is the other, because it holds no session record and every presence-based rule over it finds nothing to
    /// contradict, so it would report a clean stream it never measured. A stream that opened and was later cut still runs:
    /// loss can hide a record but cannot make two recorded ones disagree.
    /// </summary>
    public static string? SidecarUnavailable(Session session, string carries)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        if (!log.Present) return $"-events.jsonl sidecar, which carries {carries}";
        return log.Opened ? null
            : $"-events.jsonl sidecar the recorder opened: the file holds no session record, so an empty stream cannot be told from one never written, and it would carry {carries}";
    }

    public static Finding Aggregate(Severity severity, string check, string title, List<GodsEyeEvent> events, string explanation)
    {
        GodsEyeEvent first = events[0];
        string example = $"tick {first.tick} {first.label} channel={first.channel} {JoinAttemptEvidence.Abbreviate(first.detail, 400)}";
        return new Finding(severity, check, events.Count == 1 ? title : $"{title} ({events.Count:n0} occurrences)",
            $"{explanation} First: {example}.", Clamp(events.Min(e => e.tick)), Clamp(events.Max(e => e.tick)), events.Count);
    }

    private static int Clamp(long tick) => (int)Math.Clamp(tick, 0, int.MaxValue);
}
