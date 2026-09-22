#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>What a domain's census admitted, in the three values discovery answers in.</summary>
public readonly record struct CensusAdmission(string Domain, int Usable, int Unresolved, int Unusable, string Reason);

/// <summary>One target fact of the frozen observation, flattened to values. The key is pre-rendered
/// by the reader rather than carried as a <c>FactKey</c>, because this file is compiled a second time
/// into the headless suite without <c>Selection/</c> beside it.</summary>
public readonly record struct TargetFact(string Kind, string Key, bool Observed, string Evidence);

/// <summary>Everything a decision audit needs that its own record does not carry.</summary>
public sealed record DecisionInputs(IReadOnlyList<CensusAdmission> Admitted, IReadOnlyList<TargetFact> Targets);

/// <summary>
/// Audits every recorded decision against the contracts the brain is supposed to keep, and writes a
/// typed <c>contract-violation</c> occurrence when one breaks.
///
/// <b>Why this exists.</b> The capture of 22 September 2026 was read by six independent readers, and
/// every one of them reached the same finding by hand: the census admitted three combat targets and
/// four collect targets as <c>usable</c>, and the ordering stage in the same decision, against the
/// same frozen observation, refused every order built from them because the target fact was not
/// observed. That is a decision contradicting itself inside one tick, it held for 524 consecutive
/// ticks, and nothing in the record said so — the admission and the refusal are two strings in one
/// detail field and a person has to notice they cannot both be right. A recorder that writes both
/// halves of a contradiction and never names it is the same failure this folder already records for
/// a diagnostic reading a retired producer: nothing goes red, and the symptom surfaces on the next
/// capture somebody happens to open.
///
/// <b>What a tripwire is and is not.</b> Each contract below is a statement the producer guarantees,
/// so a violation is a contradiction rather than a threshold — with two exceptions that carry a
/// declared bound and say so at their constant. A tripwire never changes what the brain decides; it
/// reads the record the brain already produces. It is deliberately not a check in
/// <c>Tools/SessionReport</c> instead: a check reads a capture afterwards, and these fire in play, in
/// the tick they describe, with the frozen observation still in hand — which is the only moment the
/// target fact keys and their evidence can be read at all.
///
/// <b>The clock each contract is judged on.</b> Contracts one, two and five are properties of one
/// decision and are keyed on the frozen observation's ordinal, because the census, the refusal tally
/// and the last decision all persist across the ticks a retained course is carried — judged per tick
/// they would fire once a tick for the life of one decision and report a single contradiction as five
/// hundred. Contracts three and four are transitions between decisions. Contract six is a per-tick
/// cost and is audited from the recorder, because the phase timing does not exist yet when the
/// decision records itself.
///
/// <b>Bounded output.</b> A pathological session breaks one contract on most of its ticks, and the
/// sidecar's optional partition is two megabytes: writing every violation would exhaust it and mark
/// the whole capture incomplete, losing the evidence this was built to keep. So a kind is written
/// when it first fires, when its evidence signature changes, or once every <see cref="CoalesceTicks"/>
/// ticks — the rule the candidate funnel already uses — and every record carries the count since the
/// last one and the running total, so the number survives even where the individual records do not.
///
/// <b>Why the inputs arrive through a delegate.</b> This file is compiled a second time into
/// <c>Tools/EngineReplay</c>, beside the transport and the event writer and without <c>Selection/</c>,
/// because <c>RecordCourseTrace</c> is on that list and calls this. Reaching for
/// <c>CompanionNPC.Instance.Brain.Course</c> here would drag the whole decision tree across that
/// boundary. <see cref="Source"/> is installed once by <c>ReadLiveCourseForAudit</c> on the mod side,
/// which is also the seam a fixture uses to drive one decision without a world.
/// </summary>
public static class AuditDecisionContracts
{
    /// <summary>
    /// How large a frozen observation may get before its size is itself the finding.
    ///
    /// <b>This mirrors nothing, and saying so is the point of the comment.</b> There is no declared
    /// fact budget anywhere in <c>Selection/</c>: discovery is capped at 64 candidates across six
    /// domains and the search at three sites per order, and the number of *facts* a snapshot may hold
    /// is bounded by nothing. Once the course brain names one, this mirrors it and this comment says
    /// which declaration it must equal. Until then it is a tripwire's own bound taken from a
    /// measurement rather than from a rule: in the capture of 22 September 2026 a decision carried a
    /// median 149 facts over its first 200 ticks and 1,548 over ticks 1,801–2,000, growing
    /// monotonically and never falling, with about sixty of them accounted for by the census. So a
    /// few hundred is the shape of a healthy census here, and this fires where the growth has become
    /// the story rather than at a large opening one.
    /// </summary>
    public const int MaximumFactsPerDecision = 512;

    /// <summary>
    /// What a tick's decision may cost before the overrun is reported, in milliseconds.
    ///
    /// The tick's own allowance is <see cref="Weights.TotalPlanningMilliseconds"/>, and a decision
    /// legitimately exceeds it by up to one atomic slice, because a bounded query already inside its
    /// own deadline is not cut mid-flight: the largest such slice declared in <see cref="Weights"/> is
    /// <see cref="Weights.RouteSearchMilliseconds"/>, larger than the family-preparation and
    /// combat-planning slices beside it. Both are read rather than copied, so retuning either moves
    /// this ceiling with it.
    /// </summary>
    public static double DecideCeilingMilliseconds
        => Weights.TotalPlanningMilliseconds + Weights.RouteSearchMilliseconds;

    /// <summary>The two refusal strings that mean "the target fact this order names was not observed".
    /// Both are emitted on one condition, <c>Evidence != FactEvidence.Observed</c>, from
    /// <c>Activities/Combat/CombatCourseOpportunity.cs</c> and
    /// <c>Selection/Opportunities/BindAssistanceOpportunity.cs</c>. They are literals here
    /// deliberately, and <c>VerifyDecisionTripwires</c> pins them against those two files: a rename
    /// there must redden a row rather than silently make every contract below unable to fire, which
    /// is this folder's own trap about a diagnostic reading a producer that moved.</summary>
    public const string CombatNotObserved = "target-capture-missing";
    public const string AssistanceNotObserved = "assistance-target-unresolved";

    private const int CoalesceTicks = 60;
    /// <summary>How many fact keys the last-observed map remembers. A session's live targets are a
    /// handful; the map is cleared wholesale rather than evicted one at a time when it passes this,
    /// because an age computed from a partly forgotten map is worse than an unknown one.</summary>
    private const int EvidenceMemory = 4096;

    /// <summary>Where a decision's census and target facts come from. Installed once on the mod side;
    /// a fixture replaces it to drive one decision with no world, and the seam is itself proved by a
    /// row that runs the whole brain and counts the audits that reached this file.</summary>
    public static Func<DecisionInputs?>? Source;

    private static readonly Dictionary<string, long> lastObserved = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Record> written = new(StringComparer.Ordinal);
    private static long auditedOrdinal = -1;
    private static CourseTraceContext? lastContext;
    private static long publishedTick = long.MinValue;
    private static string lastActivity = "";
    private static long lastDecisionTick = long.MinValue;

    private readonly record struct Record(string Signature, long Tick, long Since, long Total);

    /// <summary>
    /// The evidence the last decision's refused targets read, and how stale each was, as the recorder
    /// writes them into <c>target_evidence</c> and <c>target_evidence_age</c>.
    ///
    /// They are held here rather than recomputed at the row, because the frozen observation is in hand
    /// while the decision records itself and is not at the row; and they survive a carried tick on
    /// purpose, because a retained course is one decision and its evidence does not change while it is
    /// carried. A decision that refused nothing the census admitted clears them, so the columns read
    /// as a dash rather than repeating the last contradiction for ever.
    /// </summary>
    public static string LastTargetEvidence { get; private set; } = "";
    public static string LastTargetEvidenceAge { get; private set; } = "";

    /// <summary>How many decisions have reached this audit in the session. It is the seam's own
    /// witness: an end-to-end row drives the real brain and requires one audit per recorded decision,
    /// so removing the hook in <see cref="RecordCourseTrace"/> reddens it rather than going quiet.</summary>
    public static long Audited { get; private set; }

    /// <summary>Violations counted this session, by kind, whether or not each was written. The
    /// fixtures assert on this rather than on what the coalescing happened to keep.</summary>
    public static IReadOnlyDictionary<string, long> Counts
    {
        get
        {
            var totals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in written) totals[pair.Key] = pair.Value.Total;
            return totals;
        }
    }

    /// <summary>Clears every per-session memory, so one world's fact ages and violation counts cannot
    /// be read as the next world's. <see cref="Source"/> is deliberately not cleared: it is wiring
    /// rather than session state.</summary>
    public static void Reset()
    {
        lastObserved.Clear();
        written.Clear();
        auditedOrdinal = -1;
        lastContext = null;
        publishedTick = long.MinValue;
        lastActivity = "";
        lastDecisionTick = long.MinValue;
        LastTargetEvidence = LastTargetEvidenceAge = "";
        Audited = 0;
    }

    /// <summary>
    /// Audits one recorded decision and returns the evidence fields to append to its payload.
    ///
    /// Called from <see cref="RecordCourseTrace.Record"/> rather than from the course owner, which is
    /// the seam a diagnostic is allowed to occupy: the owner decides and this reads what it recorded.
    /// </summary>
    /// <returns>The <c>target-evidence</c> and <c>target-evidence-age</c> fields for the payload, or
    /// an empty list where this decision refused no target the census had admitted.</returns>
    internal static IReadOnlyList<KeyValuePair<string, CourseTraceValue>> Observe(
        CourseTraceContext context, CourseTracePayload payload)
    {
        if (payload.Kind != "course-decision") return Array.Empty<KeyValuePair<string, CourseTraceValue>>();
        DecisionInputs? inputs = Source?.Invoke();
        if (inputs == null) return Array.Empty<KeyValuePair<string, CourseTraceValue>>();
        return Audit(context, payload, inputs);
    }

    /// <summary>
    /// The audit itself, taking every input explicitly so a fixture can drive one decision without a
    /// world. Every contract is evaluated and none short-circuits another, because two contracts
    /// breaking together is a different finding from either alone.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, CourseTraceValue>> Audit(
        CourseTraceContext context, CourseTracePayload payload, DecisionInputs inputs)
    {
        Audited++;
        long tick = context.SourceTick;
        lastContext = context;
        string reason = Text(payload, "reason");
        string activity = Text(payload, "activity");
        bool settled = Flag(payload, "settled");
        long steps = Integer(payload, "steps");
        long factCount = Integer(payload, "facts");
        string release = Text(payload, "release-reason");
        Dictionary<string, long> refusals = Refusals(payload);

        RememberObservedFacts(inputs.Targets, tick);

        // Contract four, a transition: the bound activity changed while a decision was still running.
        // This is the mid-session fight death of the 22 September capture — a decision that spans
        // ticks reports keep-company while it runs, which exits the combat activity, which releases
        // the attack plan. 212 combat attempts ended at a median of one tick that way.
        if (!settled && lastActivity.Length > 0 && activity != lastActivity && lastDecisionTick != long.MinValue)
            Fire("activity-exited-during-decision", tick, context,
                $"the bound activity went {lastActivity}->{activity} on tick {tick} while the decision was unsettled"
                    + $" (reason={reason}, previous decision on tick {lastDecisionTick})",
                $"{lastActivity}>{activity}");

        // Contract three, a transition: the course released because the use it had accepted was gone,
        // within one tick of having published that course. A course published and invalidated inside
        // one tick is not retention, it is a loop, and the capture holds 483 of them.
        if (release == "next-use-invalid:accepted-use-not-present" && publishedTick != long.MinValue
            && tick - publishedTick <= 1 && tick >= publishedTick)
            Fire("accepted-use-absent-next-tick", tick, context,
                $"the course published on tick {publishedTick} released on tick {tick} because its accepted use"
                    + $" was not present, {tick - publishedTick} tick(s) later (activity={activity}, reason={reason})",
                "released");
        if (reason == "course-published") publishedTick = tick;
        lastActivity = activity;
        lastDecisionTick = tick;

        // The rest is per decision rather than per tick, and must not repeat while a course is carried.
        if (context.ObservationOrdinal == auditedOrdinal) return Array.Empty<KeyValuePair<string, CourseTraceValue>>();
        auditedOrdinal = context.ObservationOrdinal;

        // Contract five: the frozen observation is larger than anything the brain declared it needs.
        if (factCount > MaximumFactsPerDecision)
            Fire("fact-count-above-bound", tick, context,
                $"the frozen observation carried {factCount} facts against a declared bound of"
                    + $" {MaximumFactsPerDecision} (observation ordinal {context.ObservationOrdinal})",
                (factCount / 256).ToString(CultureInfo.InvariantCulture));

        // Contracts one and two both turn on which domains the census admitted usable and which of
        // those the binder then refused for want of an observed target, so the evidence is gathered
        // once and each contract reads it.
        var fields = new List<KeyValuePair<string, CourseTraceValue>>();
        bool anyUsable = false, anyContradiction = false;
        var evidence = new StringBuilder();
        var ages = new StringBuilder();
        foreach (CensusAdmission domain in inputs.Admitted)
        {
            if (domain.Usable <= 0) continue;
            anyUsable = true;
            string wanted = RefusalFor(domain.Domain);
            if (wanted.Length == 0 || !refusals.TryGetValue(wanted, out long refused) || refused <= 0) continue;
            anyContradiction = true;
            string kind = TargetKindFor(domain.Domain);
            (string key, string evidenceValue, long age, int total, int observed) = Sample(inputs.Targets, kind, tick);
            if (evidence.Length > 0) { evidence.Append('|'); ages.Append('|'); }
            evidence.Append(domain.Domain).Append('=').Append(key).Append(':').Append(evidenceValue)
                .Append(':').Append(observed).Append('/').Append(total);
            ages.Append(domain.Domain).Append('=').Append(age.ToString(CultureInfo.InvariantCulture));
            Fire("census-admitted-binder-refused", tick, context,
                $"the {domain.Domain} census admitted {domain.Usable} usable and the same decision refused {refused}"
                    + $" order(s) naming its targets with {wanted}; the snapshot holds {total} {kind} fact(s),"
                    + $" {observed} of them observed, and {key} reads {evidenceValue}, last observed"
                    + $" {(age < 0 ? "never this session" : age + " tick(s) ago")}",
                $"{domain.Domain}:{domain.Usable}:{wanted}:{evidenceValue}");
        }

        // Contract two: an empty course published beside usable work, where every refusal the search
        // recorded is one of the not-observed pair. A course refusing work on a *preference* — a job
        // priced below companionship, a travel answer that never came — is the brain working, so the
        // third string the capture carries (`no-use-with-captured-travel-and-target-impact`) has to
        // keep this quiet. That negative is what separates this rule from "any refusal".
        if (settled && steps == 0 && anyUsable && refusals.Count > 0 && AllNotObserved(refusals))
            Fire("empty-course-beside-usable-work", tick, context,
                $"a settled course with no steps was published (reason={reason}) while {UsableSummary(inputs.Admitted)},"
                    + $" and every refusal was one of the not-observed pair ({RefusalSummary(refusals)})",
                UsableSummary(inputs.Admitted));

        LastTargetEvidence = anyContradiction ? evidence.ToString() : "";
        LastTargetEvidenceAge = anyContradiction ? ages.ToString() : "";
        if (anyContradiction)
        {
            fields.Add(new("target-evidence", CourseTraceValue.TextValue(LastTargetEvidence)));
            fields.Add(new("target-evidence-age", CourseTraceValue.TextValue(LastTargetEvidenceAge)));
        }
        return fields;
    }

    /// <summary>
    /// Contract six: what the decision cost this tick, against the allowance plus its largest atomic
    /// slice. It is filed against the last decision recorded rather than against a context minted
    /// here, so a reader joins the overrun to the decision it describes.
    /// </summary>
    internal static void ObserveDecideCost(double decideMilliseconds, long tick)
    {
        double ceiling = DecideCeilingMilliseconds;
        if (!(decideMilliseconds > ceiling)) return;
        if (lastContext is not { } context) return;
        // One interpolated string rather than two concatenated: `FormattableString.Invariant` takes a
        // FormattableString, and concatenating two interpolated strings has already produced a plain
        // string by the time it is passed. Formatting each number into a local is the shape that
        // compiles and is what the rest of this tree does.
        string cost = decideMilliseconds.ToString("0.000", CultureInfo.InvariantCulture);
        string allowance = Weights.TotalPlanningMilliseconds.ToString("0.000", CultureInfo.InvariantCulture);
        string slice = Weights.RouteSearchMilliseconds.ToString("0.000", CultureInfo.InvariantCulture);
        string over = (decideMilliseconds - ceiling).ToString("0.000", CultureInfo.InvariantCulture);
        Fire("decide-overran-allowance", tick, context,
            $"deciding took {cost} ms against an allowance of {allowance} ms plus its largest atomic slice"
                + $" {slice} ms, overrunning by {over} ms",
            ((int)(decideMilliseconds / 8d)).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes one closing record per kind whose count has moved since its last record, so a session's
    /// total survives the coalescing.
    ///
    /// Without it the totals in a capture are the totals as at the last *written* record and the tail
    /// is silently missing: three hundred contradictions coalesced every sixty ticks close at a
    /// recorded 241, which is a number nobody could tell from the truth. Called from the recorder's
    /// close, before the writer stops, so the records are enqueued while the stream is still taking
    /// them; a capture that was killed rather than closed carries the running totals it had reached,
    /// which is the honest answer for a capture with no terminal line either.
    /// </summary>
    public static void Flush()
    {
        if (lastContext is not { } context) return;
        foreach (string kind in new List<string>(written.Keys))
        {
            Record had = written[kind];
            if (had.Since == 0) continue;
            written[kind] = had with { Since = 0 };
            var payload = new CourseTracePayload("contract-violation", 1, new[]
            {
                new KeyValuePair<string, CourseTraceValue>("violation", CourseTraceValue.TextValue(kind)),
                new KeyValuePair<string, CourseTraceValue>("detail", CourseTraceValue.TextValue(
                    $"the session closed with {had.Since} further occurrence(s) of {kind} coalesced since the last record")),
                new KeyValuePair<string, CourseTraceValue>("signature", CourseTraceValue.TextValue("session-close")),
                new KeyValuePair<string, CourseTraceValue>("observation-ordinal", CourseTraceValue.Integer(context.ObservationOrdinal)),
                new KeyValuePair<string, CourseTraceValue>("occurrences-since-last", CourseTraceValue.Integer(had.Since)),
                new KeyValuePair<string, CourseTraceValue>("occurrences-total", CourseTraceValue.Integer(had.Total)),
            });
            GodsEyeEvents.RecordCourse(CourseTracePhase.Brain, context, payload, required: false, snapshot: null);
        }
    }

    /// <summary>
    /// Writes a violation, coalesced on its evidence signature, so a contradiction that holds for five
    /// hundred ticks is a handful of records carrying a count rather than five hundred records that
    /// exhaust the sidecar's optional partition and mark the capture incomplete.
    /// </summary>
    private static void Fire(string kind, long tick, CourseTraceContext context, string detail, string signature)
    {
        bool known = written.TryGetValue(kind, out Record had);
        long since = had.Since + 1, total = had.Total + 1;
        if (known && had.Signature == signature && tick - had.Tick < CoalesceTicks)
        {
            written[kind] = had with { Since = since, Total = total };
            return;
        }
        written[kind] = new(signature, tick, 0, total);
        var payload = new CourseTracePayload("contract-violation", 1, new[]
        {
            new KeyValuePair<string, CourseTraceValue>("violation", CourseTraceValue.TextValue(kind)),
            new KeyValuePair<string, CourseTraceValue>("detail", CourseTraceValue.TextValue(detail)),
            new KeyValuePair<string, CourseTraceValue>("signature", CourseTraceValue.TextValue(signature)),
            new KeyValuePair<string, CourseTraceValue>("observation-ordinal", CourseTraceValue.Integer(context.ObservationOrdinal)),
            // Both counts ride on every record, because coalescing loses the individual occurrences and
            // must not lose how many there were: `since-last` is what this record stands for, and
            // `total` is the running count of this kind in the session.
            new KeyValuePair<string, CourseTraceValue>("occurrences-since-last", CourseTraceValue.Integer(since)),
            new KeyValuePair<string, CourseTraceValue>("occurrences-total", CourseTraceValue.Integer(total)),
        });
        GodsEyeEvents.RecordCourse(CourseTracePhase.Brain, context, payload, required: false, snapshot: null);
    }

    /// <summary>
    /// Remembers the tick each target fact was last seen observed, which is the only place the age of
    /// a target's evidence can come from: a decision fact carries a version and a digest and no
    /// observation time, so "how stale is this" is a question only something watching every snapshot
    /// can answer.
    /// </summary>
    private static void RememberObservedFacts(IReadOnlyList<TargetFact> targets, long tick)
    {
        if (lastObserved.Count > EvidenceMemory) lastObserved.Clear();
        for (int i = 0; i < targets.Count; i++)
            if (targets[i].Observed) lastObserved[targets[i].Key] = tick;
    }

    /// <summary>One target fact of a kind, chosen to be the sharpest evidence rather than the first: a
    /// fact the binder would have refused where one exists, and otherwise an observed one, because a
    /// domain whose every target reads observed while the binder refuses it is a *different* finding —
    /// the two stages are reading different keys.</summary>
    private static (string Key, string Evidence, long Age, int Total, int Observed) Sample(
        IReadOnlyList<TargetFact> targets, string kind, long tick)
    {
        int total = 0, observed = 0, refused = -1, seen = -1;
        for (int i = 0; i < targets.Count; i++)
        {
            if (!string.Equals(targets[i].Kind, kind, StringComparison.Ordinal)) continue;
            total++;
            if (targets[i].Observed) { observed++; if (seen < 0) seen = i; }
            else if (refused < 0) refused = i;
        }
        int pick = refused >= 0 ? refused : seen;
        if (pick < 0) return ("-", "absent", -1, 0, 0);
        long age = lastObserved.TryGetValue(targets[pick].Key, out long at) ? tick - at : -1;
        return (targets[pick].Key, targets[pick].Evidence, age, total, observed);
    }

    /// <summary>The refusal string a domain's binder emits when its target fact is not observed, or
    /// empty for a domain no named contract covers. Gathering's own <c>native-target-unresolved</c> is
    /// deliberately absent: mining and chopping refuse against a census that legitimately reports
    /// unknown while it is still sweeping, so the contradiction this contract names has not been
    /// observed there and a rule written for it would be written from an imagined failure.</summary>
    private static string RefusalFor(string domain) => domain switch
    {
        "combat" => CombatNotObserved,
        "collect-target" or "light-target" or "pot-target" => AssistanceNotObserved,
        _ => "",
    };

    /// <summary>Combat's census domain is <c>combat</c> and its target facts are keyed
    /// <c>combat-target</c>; every assistance domain names its own facts.</summary>
    private static string TargetKindFor(string domain) => domain == "combat" ? "combat-target" : domain;

    private static bool AllNotObserved(Dictionary<string, long> refusals)
    {
        foreach (var refusal in refusals)
            if (refusal.Value > 0 && refusal.Key != CombatNotObserved && refusal.Key != AssistanceNotObserved) return false;
        return true;
    }

    private static string UsableSummary(IReadOnlyList<CensusAdmission> admitted)
    {
        var text = new StringBuilder();
        foreach (CensusAdmission domain in admitted)
        {
            if (domain.Usable <= 0) continue;
            if (text.Length > 0) text.Append(", ");
            text.Append(domain.Domain).Append(" admitted ").Append(domain.Usable).Append(" usable");
        }
        return text.Length == 0 ? "no domain admitted usable work" : text.ToString();
    }

    private static string RefusalSummary(Dictionary<string, long> refusals)
    {
        var text = new StringBuilder();
        foreach (var refusal in refusals)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(refusal.Key).Append('=').Append(refusal.Value.ToString(CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }

    private static Dictionary<string, long> Refusals(CourseTracePayload payload)
    {
        var refusals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var field in payload.Fields)
            if (field.Key.StartsWith("refused:", StringComparison.Ordinal)
                && long.TryParse(field.Value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
                refusals[field.Key["refused:".Length..]] = count;
        return refusals;
    }

    private static string Text(CourseTracePayload payload, string name)
        => payload.Fields.TryGetValue(name, out CourseTraceValue value) ? value.Text : "";
    private static bool Flag(CourseTracePayload payload, string name)
        => payload.Fields.TryGetValue(name, out CourseTraceValue value) && value.Text == "true";
    private static long Integer(CourseTracePayload payload, string name)
        => payload.Fields.TryGetValue(name, out CourseTraceValue value)
            && long.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
}
