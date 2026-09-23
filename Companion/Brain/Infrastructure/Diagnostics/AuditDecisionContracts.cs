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
public sealed record DecisionInputs(IReadOnlyList<CensusAdmission> Admitted, IReadOnlyList<TargetFact> Targets,
    IReadOnlyDictionary<string, int>? FactsByKind = null);

/// <summary>
/// One accepted course step, flattened to values for the effect contract. <paramref name="TileX"/> and
/// <paramref name="TileY"/> are the step's work tile as <c>ExecuteCourseBinding.WorkTileOf</c> reads it,
/// present only for a step whose hand acts on a tile; the reader computes them on the mod side, because
/// this file is compiled a second time without <c>Selection/</c> and cannot call that parser itself.
/// </summary>
public readonly record struct BoundStepForAudit(long Id, string Domain, string Purpose, string Target,
    int? TileX, int? TileY, bool Incidental = false);

/// <summary>What the effect contract concluded about one native effect, for the occurrence that records it.
/// <see cref="Verdict"/> is <c>bound</c>, one of the two violation kinds, or <c>unaudited</c> where no
/// binding source is installed and the question could not be asked.</summary>
public readonly record struct EffectVerdict(long BindingId, string Origin, string Verdict)
{
    /// <summary>The fields every audited effect occurrence carries, so a reader joins the effect to the
    /// decision that chose it. Appended to the occurrence's own detail with a leading separator.</summary>
    public string Fields => $";binding-id={BindingId.ToString(CultureInfo.InvariantCulture)};binding-origin={Origin};binding-verdict={Verdict}";
}

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
/// decision records itself. Contract seven is per native effect and hangs off the effect recorders
/// rather than off the decision record — see <see cref="ObserveEffect"/> — and it is the one contract
/// that counts whether or not a recording is open.
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
///
/// <b>What witnesses the wiring, and what cannot.</b> The seam has two halves and they fail
/// separately. The hook — this being called from <c>RecordCourseTrace.Record</c> at all — is held by
/// `the recorder's own seam drives the audit end to end`, which installs a source and goes in through
/// `Record`, and is the only row here that does: every other row calls <see cref="Audit"/> directly,
/// so for a while removing the hook left all of them green. The *installer* cannot be witnessed
/// headlessly at all, because <c>ReadLiveCourseForAudit</c> is not on EngineReplay's compile list and
/// a fixture supplies the source itself; what witnesses it is the capture, through
/// <see cref="Audited"/> and <see cref="ObservationsRead"/> on the closing line and the session
/// reader's check over them.
/// </summary>
public static class AuditDecisionContracts
{
    /// <summary>
    /// How many facts of one kind a frozen observation may carry before that kind's size is the finding.
    ///
    /// <b>It is per kind because a single total could not see the thing it was watching for.</b> The
    /// bound was one number, 512, over the whole observation, and on the 22 September capture's replay it
    /// fired 470–647 times a run — every one of them on `light-target`, a census legitimately sized to
    /// its window, while a runaway in `combat-use` at three or four hundred facts would have sat
    /// invisible behind it. A tripwire whose loudest kind is its healthiest cannot report the others.
    ///
    /// Lighting's own bound is <see cref="RankCensusSitesByWorth.MostSitesAWindowCanHold"/> — the number
    /// of spacing-disjoint torch sites the work window holds — because that is what the census can now
    /// publish rather than a figure anybody chose, and it moves if the work radius or the placer's
    /// spacing does. The margin above it is exactly one site, which is what hysteresis can produce: the
    /// census keeps the single site a published course is bound to (`CaptureAssistanceOpportunities.PinnedLightSite`
    /// returns one target or none), so at most one fact is published past the bound. It was 16 until
    /// 22 September 2026, which was a margin nobody derived — sixteen times what the rule it covers can
    /// emit, and therefore fifteen facts of genuine runaway that the tripwire would not have named.
    ///
    /// Every other kind keeps a flat few hundred, which is a tripwire's own bound rather than a mirror of
    /// any declaration: nothing in <c>Selection/</c> declares a fact budget at all — discovery is capped
    /// at 64 candidates across six domains and the search at three sites per order. Measured on the same
    /// replay, the largest non-lighting kind is `combat-use` at 6–11 and `mine-target` at 13, so 256 is
    /// an order of magnitude above a healthy crowd and fires well below where a leak would hurt.
    /// </summary>
    public static int MaximumFactsOfKind(string kind) => kind switch
    {
        "light-target" => Observation.RankCensusSitesByWorth.MostSitesAWindowCanHold(
            (int)(Selection.Weights.FollowWorkRadius / 16f), TorchSpacingTiles) + 1,
        _ => 256,
    };

    /// <summary>Mirrors <c>CompanionTorches.SpacingTiles</c>, which this file cannot reference: the
    /// placer's own source reaches the tile watcher, and `EngineReplay` compiles this file a second time
    /// without half the mod behind it, so including the placer there fails the build. The two drifting
    /// would silently widen or narrow the light bound, so `the audit's torch spacing is the placer's` in
    /// `VerifyDecisionTripwires` asserts they are equal and reds if either moves alone.</summary>
    public const int TorchSpacingTiles = 8;

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

    /// <summary>Where a decision's census and target facts come from. Installed once on the mod side by
    /// <c>ReadLiveCourseForAudit</c>, which no headless row can exercise, because that file is not on
    /// EngineReplay's compile list and every fixture installs a source of its own. A session whose
    /// installer never ran is therefore witnessed by the capture rather than by a row: it audits every
    /// decision and reads no observation, which is <see cref="ObservationsRead"/> at zero.</summary>
    public static Func<DecisionInputs?>? Source;

    /// <summary>
    /// Where the effect contract reads the step the current activity was handed — the companion's
    /// <c>Brain.Activity.Binding</c>, flattened. Installed beside <see cref="Source"/> by
    /// <c>ReadLiveCourseForAudit.Install</c> for the same compile-boundary reason, and replaced by a
    /// fixture to drive one effect with no world. Null means the question cannot be asked, which the
    /// contract reports as <c>unaudited</c> and never as a violation: an unwired reader is not a
    /// companion acting without a step.
    /// </summary>
    public static Func<BoundStepForAudit?>? BindingSource;

    /// <summary>
    /// The one-step binding the course accepted for an in-passing interaction, and the tick it was
    /// accepted on.
    ///
    /// <b>This is the seam the incidental acceptance writes and the effect contract reads.</b> An in-passing
    /// pot break or torch placement is performed by the grant boundary rather than by the activity holding
    /// the body, so the activity's own binding is the wrong step to judge it against; the plan's grants row
    /// requires that such an effect have "an accepted one-step binding before the native call", and this is
    /// where that binding becomes visible to the audit. The contract, for whoever sets it:
    /// call <see cref="AcceptIncidental"/> (or <c>ReadLiveCourseForAudit.AcceptIncidental</c>, which takes a
    /// <c>StepBinding</c>) on the tick the course accepts the step and before the native call that performs
    /// it; it is honoured for effects recorded on that same tick only, so a stale acceptance can never
    /// excuse a later effect, and the next acceptance replaces it. Nothing needs clearing.
    /// </summary>
    public static BoundStepForAudit? AcceptedIncidental { get; private set; }
    private static long acceptedIncidentalTick = long.MinValue;

    /// <summary>How many native effects reached the effect contract with a binding source to read. A row
    /// that grades the two effect kinds at zero needs this above zero, or it passes for a run that
    /// performed nothing.</summary>
    public static long EffectsAudited { get; private set; }

    /// <summary>Records an accepted incidental step for the tick it was accepted on. See
    /// <see cref="AcceptedIncidental"/> for the whole contract.</summary>
    public static void AcceptIncidental(BoundStepForAudit step, long tick)
    {
        AcceptedIncidental = step with { Incidental = true };
        acceptedIncidentalTick = tick;
    }

    /// <summary>The two operations <c>GodsEyeEvents.RecordWorldInteraction</c> carries that are native
    /// effects; its other operations (<c>placement-refused</c>, <c>approach-abandoned</c>) record a hand that
    /// did nothing, which no binding needs to cover. Both literals are the performers' own, in
    /// <c>LightUsefulArea.Perform</c> and <c>CollectNearbyItems.Perform</c>, and
    /// <c>VerifyEveryEffectIsTheBoundStep</c> pins them against those files by path.</summary>
    public const string PlaceTorchOperation = "place-torch";
    public const string BreakPotOperation = "break-pot";

    /// <summary>The two effect contract kinds, named apart because they are different defects: a hand
    /// acting with no step at all is a second chooser; a hand acting beside its step is an executor
    /// that searched for its own target.</summary>
    public const string EffectWithoutBinding = "effect-without-binding";
    public const string EffectOffBinding = "effect-off-binding";

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

    /// <summary>
    /// How many decisions have reached this audit in the session, and how many of those read a frozen
    /// observation through <see cref="Source"/>. Both ride out on the capture's closing line, because
    /// they are the only witnesses to two wirings that fail *silently* in play and that no headless row
    /// can see: the hook gone from <see cref="RecordCourseTrace.Record"/> leaves decisions recorded and
    /// nothing audited, and <c>ReadLiveCourseForAudit.Install</c> never running leaves every decision
    /// audited against no inputs, which quietly reduces six contracts to two.
    ///
    /// The second is not the first with a smaller number: <see cref="Audited"/> counts at the top of
    /// the audit, before the source is consulted at all, so a session with a null source reports every
    /// decision audited and zero read. A reader that only had the first count would read a broken
    /// installer as a healthy session with nothing to report, which is this folder's own trap.
    /// </summary>
    public static long Audited { get; private set; }

    /// <inheritdoc cref="Audited"/>
    public static long ObservationsRead { get; private set; }

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
        Audited = ObservationsRead = 0;
        AcceptedIncidental = null;
        acceptedIncidentalTick = long.MinValue;
        EffectsAudited = 0;
        LastEffectViolation = "";
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
        // The source is passed rather than invoked, so the frozen observation is read only on the
        // ticks the audit needs it — see `Audit`'s own note on the ordering.
        return Audit(context, payload, Source);
    }

    /// <summary>The explicit-input overload a fixture drives, which is the same audit with a source
    /// that hands back one prepared set.</summary>
    internal static IReadOnlyList<KeyValuePair<string, CourseTraceValue>> Audit(
        CourseTraceContext context, CourseTracePayload payload, DecisionInputs inputs)
        => Audit(context, payload, () => inputs);

    /// <summary>
    /// The audit itself, taking every input explicitly so a fixture can drive one decision without a
    /// world. Every contract is evaluated and none short-circuits another, because two contracts
    /// breaking together is a different finding from either alone.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, CourseTraceValue>> Audit(
        CourseTraceContext context, CourseTracePayload payload, Func<DecisionInputs?>? source)
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

        // The frozen observation is read here and nowhere earlier, which is the whole of the ordering:
        // a carried course repeats its ordinal, so the source is invoked once per decision rather than
        // once per tick. Reading it above — where the first version did — walked every fact in the
        // observation on every tick of a held course, 1,548 of them at the 22 September capture's tail.
        DecisionInputs? read = source?.Invoke();
        if (read == null) return Array.Empty<KeyValuePair<string, CourseTraceValue>>();
        DecisionInputs inputs = read;
        ObservationsRead++;
        RememberObservedFacts(inputs.Targets, tick);
        // Parsed here rather than above for the same reason: the refusal tally is a dictionary per
        // call and no transition contract reads it, so a carried tick allocates nothing at all.
        Dictionary<string, long> refusals = Refusals(payload);

        // Contract five: one kind of the frozen observation is larger than that kind's own bound. The
        // loudest kind is reported rather than every offender, because the signature groups by kind and
        // a decision carrying two runaway kinds is one finding a reader will chase from either end.
        if (inputs.FactsByKind is { Count: > 0 } kinds)
        {
            string worst = ""; int worstCount = 0, worstBound = 0;
            foreach ((string kind, int count) in kinds)
            {
                int bound = MaximumFactsOfKind(kind);
                if (count <= bound || count - bound <= worstCount - worstBound) continue;
                worst = kind; worstCount = count; worstBound = bound;
            }
            if (worst.Length > 0)
                Fire("fact-count-above-bound", tick, context,
                    $"the frozen observation carried {worstCount} {worst} facts against that kind's bound of"
                        + $" {worstBound}, in {factCount} facts overall (observation ordinal {context.ObservationOrdinal})",
                    worst);
        }

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
                // The signature carries *which* domain contradicted itself and how, never how many
                // candidates it admitted: a live count moves between decisions while the contradiction
                // stands, and a signature that moves defeats the coalescing it keys — the count is one
                // record a tick again, which is the failure the coalescing exists to stop. The number
                // is in the detail, where it belongs.
                $"{domain.Domain}:{wanted}:{evidenceValue}");
        }

        // Contract two: an empty course published beside usable work, where every refusal the search
        // recorded is one of the not-observed pair. A course refusing work on a *preference* — a job
        // priced below companionship, a travel answer that never came — is the brain working, so the
        // third string the capture carries (`no-use-with-captured-travel-and-target-impact`) has to
        // keep this quiet. That negative is what separates this rule from "any refusal".
        if (settled && steps == 0 && anyUsable && refusals.Count > 0 && AllNotObserved(refusals))
            Fire("empty-course-beside-usable-work", tick, context,
                $"a settled course with no steps was published (reason={reason}) while {UsableSummary(inputs.Admitted)},"
                    + $" and every refusal was one of the not-observed pair ({RefusalSummary(refusals)})"
                    + StructuralNote(payload),
                // Which domains had usable work, not how much: see the note on the signature above.
                DomainsWithUsableWork(inputs.Admitted));

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
    /// Contract seven: every native world effect the companion causes names the accepted step it
    /// performed, and lands on that step's target.
    ///
    /// <b>Why it exists.</b> Until 23 September 2026 the course chose a target and flew the body to its
    /// pose, while every work activity acted on a target from its own private search, and an in-passing
    /// scan at the grant boundary broke pots and placed torches with no step at all. Both halves were in
    /// the record — the decision named one tile and the `tool-effect` another — and nothing said they
    /// disagreed. The plan's tick step 9 is the rule: perform the accepted use, and "no tactical fallback
    /// may secretly fire and leave the course believing a different action happened".
    ///
    /// <b>Where it hangs.</b> In the effect recorders themselves, which every effect site already calls,
    /// so no performer has to remember to report and a new performer is audited the moment it records
    /// its effect. It is called above the recorders' own stream gate and counts whether or not a session
    /// is open, because the fixtures, the fuzzer and the world run read <see cref="Counts"/> rather than a
    /// file; only the occurrence waits on the stream.
    ///
    /// <b>How a target is matched.</b> A tile effect lands on the step's work tile exactly — a tool
    /// binding is one native application to one tile, and the vein is worked one binding per tile — except
    /// a pot break, which may strike any tile of the two-by-two whose origin the step names, because
    /// <c>WorldGen.KillTile</c> on any of the four breaks the pot. An item effect lands on the step whose
    /// target is <c>item:&lt;slot&gt;</c> for the slot that transferred. An accepted incidental step
    /// recorded on this tick is consulted before the activity's step, and matching either is bound.
    /// </summary>
    /// <param name="tileX">The tile the effect landed on, for a tile effect; null for an item effect.</param>
    /// <param name="itemSlot">The <c>Main.item</c> slot that transferred, for an item effect.</param>
    public static EffectVerdict ObserveEffect(string effect, string operation, int? tileX, int? tileY, int? itemSlot, long tick)
    {
        if (BindingSource == null) return new EffectVerdict(0, "unread", "unaudited");
        EffectsAudited++;
        BoundStepForAudit? activity;
        try { activity = BindingSource(); }
        // A reader that throws is a wiring fault, not a companion acting without a step; it is reported
        // as unaudited so the violation counts stay about the brain.
        catch (Exception) { return new EffectVerdict(0, "unread", "unaudited"); }
        BoundStepForAudit? incidental = acceptedIncidentalTick == tick ? AcceptedIncidental : null;

        if (incidental is { } accepted && Lands(accepted, operation, tileX, tileY, itemSlot))
            return new EffectVerdict(accepted.Id, "incidental", "bound");
        if (activity is { } step && Lands(step, operation, tileX, tileY, itemSlot))
            return new EffectVerdict(step.Id, "activity", "bound");

        string where = itemSlot is { } slot ? $"item:{slot.ToString(CultureInfo.InvariantCulture)}"
            : $"tile:{tileX?.ToString(CultureInfo.InvariantCulture)},{tileY?.ToString(CultureInfo.InvariantCulture)}";
        BoundStepForAudit? held = activity ?? incidental;
        if (held is not { } nearest)
        {
            FireEffect(EffectWithoutBinding, tick, 0,
                $"{effect}/{operation} landed on {where} on tick {tick} with no accepted step: the activity holding the body was"
                    + " handed none and no incidental step was accepted this tick",
                $"{effect}:{operation}");
            return new EffectVerdict(0, "none", EffectWithoutBinding);
        }
        string origin = nearest.Incidental ? "incidental" : "activity";
        string target = nearest.TileX is { } x && nearest.TileY is { } y
            ? $"work tile {x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}" : nearest.Target;
        FireEffect(EffectOffBinding, tick, nearest.Id,
            $"{effect}/{operation} landed on {where} on tick {tick} while the {origin} step {nearest.Id.ToString(CultureInfo.InvariantCulture)}"
                + $" ({nearest.Domain}/{nearest.Purpose}) named {target}",
            // Which kind of effect under which purpose, never the tiles: a private search walking a vein
            // moves its tile every strike, and a signature that moves with it writes a record a strike.
            $"{effect}:{operation}:{nearest.Purpose}");
        return new EffectVerdict(nearest.Id, origin, EffectOffBinding);
    }

    /// <summary>Whether an effect landed on a step's own target. See <see cref="ObserveEffect"/> for the rules.</summary>
    private static bool Lands(BoundStepForAudit step, string operation, int? tileX, int? tileY, int? itemSlot)
    {
        if (itemSlot is { } slot)
            return step.Target == $"item:{slot.ToString(CultureInfo.InvariantCulture)}";
        if (tileX is not { } x || tileY is not { } y || step.TileX is not { } workX || step.TileY is not { } workY) return false;
        if (operation == BreakPotOperation)
            return x >= workX && x <= workX + 1 && y >= workY && y <= workY + 1;
        return x == workX && y == workY;
    }

    /// <summary>An effect violation, counted whether or not a session is open. The record needs a course
    /// context and an effect can precede every recorded decision — the whole point of
    /// <c>effect-without-binding</c> — so one is minted when none has been seen, carrying the binding
    /// the effect was judged against.</summary>
    private static void FireEffect(string kind, long tick, long bindingId, string detail, string signature)
    {
        LastEffectViolation = detail;
        Fire(kind, tick, EffectContext(tick, bindingId), detail, signature);
    }

    /// <summary>The detail of the last effect violation counted this session, so an in-process reader —
    /// the fuzzer, the world run — can say which effect beside which step rather than only that one fired.
    /// The recorded occurrence carries the same text; this exists for the runs that open no recording.</summary>
    public static string LastEffectViolation { get; private set; } = "";

    private static CourseTraceContext EffectContext(long tick, long bindingId)
        => lastContext is { } context
            ? context with { SourceTick = tick, BindingId = bindingId }
            : new CourseTraceContext(tick, "native-effect", 0, 0, 0, bindingId, 0, "effect-audit", -1, 0, "", "", "", "");

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
        if (written.Count == 0) return;
        // An effect violation can be the only thing a session recorded, so the closing records mint the
        // same context the effect contract does rather than going quiet for want of a decision.
        CourseTraceContext context = lastContext ?? EffectContext(0, 0);
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

    /// <summary>The domains that admitted usable work, by name and in order, with no count in it. This
    /// is the coalescing key for the empty-course contract, and it is deliberately the same set of
    /// facts as <see cref="UsableSummary"/> with the numbers removed: the numbers belong in the detail
    /// a reader reads, and a key that moves with them writes a record a tick.</summary>
    private static string DomainsWithUsableWork(IReadOnlyList<CensusAdmission> admitted)
    {
        var text = new StringBuilder();
        foreach (CensusAdmission domain in admitted)
        {
            if (domain.Usable <= 0) continue;
            if (text.Length > 0) text.Append(',');
            text.Append(domain.Domain);
        }
        return text.Length == 0 ? "none" : text.ToString();
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

    /// <summary>
    /// The **evidence** refusals only, which is every reason the contracts here read.
    ///
    /// A refusal proved by a fact of the source tree rather than by the state of an observation rides
    /// under `structurally-refused:` and is deliberately not returned: `AllNotObserved` asks whether
    /// every refusal was one of the two not-observed strings, and the answer is about what the world
    /// would have to change for the order to be accepted. A purpose with no executor answers "nothing
    /// could", so counting it as a third reason silences contract two on every decision where the census
    /// admitted a pot — which is what it did until 22 September 2026, on exactly the mixed case the
    /// contract exists for. The prefix is the whole of the separation: `"structurally-refused:x"` does
    /// not start with `"refused:"`, so this reader skips it by construction rather than by a list.
    /// </summary>
    private static Dictionary<string, long> Refusals(CourseTracePayload payload)
    {
        var refusals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var field in payload.Fields)
            if (field.Key.StartsWith("refused:", StringComparison.Ordinal)
                && long.TryParse(field.Value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
                refusals[field.Key["refused:".Length..]] = count;
        return refusals;
    }

    /// <summary>What a violation's message says about refusals the predicates deliberately ignored, so a
    /// reader is told the pot was refused rather than left to wonder why the tally looks short.</summary>
    private static string StructuralNote(CourseTracePayload payload)
    {
        Dictionary<string, long> structural = StructuralRefusals(payload);
        return structural.Count == 0 ? ""
            : $"; structural refusals the contracts do not read: {RefusalSummary(structural)}";
    }

    /// <summary>The structural refusals, read for the message a violation carries rather than for any
    /// predicate, so a reader of a capture can see that a pot was refused without the contracts
    /// treating that as a reason to stay quiet.</summary>
    private static Dictionary<string, long> StructuralRefusals(CourseTracePayload payload)
    {
        var refusals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var field in payload.Fields)
            if (field.Key.StartsWith("structurally-refused:", StringComparison.Ordinal)
                && long.TryParse(field.Value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
                refusals[field.Key["structurally-refused:".Length..]] = count;
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
