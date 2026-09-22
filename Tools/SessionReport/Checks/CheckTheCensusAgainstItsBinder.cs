#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Whether a domain the census admitted as usable survived the binder in the same decision.
///
/// <para><b>This is the finding six independent readings of the 22 September 2026 capture each
/// reached by hand, and that no check asked for.</b> Both halves were already in the record and in one
/// field of one occurrence: the `decision` detail carries
/// <c>course-admitted:combat=usable:3</c> beside <c>course-refused:target-capture-missing=12</c>, and a
/// person had to notice that those cannot both be true of one frozen observation. From tick 829 to the
/// end of that session every order naming a combat target or a drop was refused, the empty course won
/// by default, and the companion kept the player company beside five hostiles and four drops for the
/// last twenty-five seconds of play.</para>
///
/// <para><b>Why it is a contradiction rather than a low score.</b> The two refusal strings are one test
/// in two files. <c>CombatCourseOpportunity</c> returns Unresolved <c>target-capture-missing</c> when
/// the target fact's evidence is not <c>Observed</c>, and <c>AssistanceOpportunityBinder</c> refuses
/// <c>assistance-target-unresolved</c> on the same condition. The census that admitted the domain
/// usable read the same frozen observation. So a domain admitted usable whose orders are all refused
/// for want of an observed target is the discovery side and the binding side of one decision
/// disagreeing about one fact, which no situation makes correct.</para>
///
/// <para><b>The join is two records and the check says which of them is which.</b> The refusal tallies
/// ride on the typed <c>course-decision</c> payload, which the producer writes on every tick; the
/// census admissions ride on the <c>decision</c> occurrence, which is periodic — the 22 September
/// capture holds 469 of them against 2,340 decisions. A decision carrying both is an observed
/// contradiction and is what the finding is graded on. A decision carrying only the refusals is read
/// against the last admission before it, which is an inference and is counted separately in the
/// finding's own text: the producer's <c>Admitted</c> list persists while a retained course is carried,
/// so carrying it forward is the right reading, but an admission that moved between two occurrences is
/// invisible to it.</para>
///
/// <para>From schema 0.45.0 the recorder answers the same question from the producer's side, keyed on
/// the frozen observation's ordinal, as a <c>contract-violation</c> occurrence of kind
/// <c>census-admitted-binder-refused</c>. Where a capture carries those, the finding states whether the
/// two agree; a capture whose tripwire fired on domains this reader found nothing in, or the reverse,
/// is its own finding, because the instruments disagreeing is a fact about the instruments.</para>
/// </summary>
public sealed class ACensusAdmissionSurvivesItsBinder : ICheck, ICheckCoverage
{
    /// <summary>The schema that first wrote <c>course-admitted:</c> and <c>course-refused:</c> — before
    /// it a capture carries the family chooser's nominations and neither half of this question.</summary>
    internal static readonly Version First = new(0, 42, 0);

    /// <summary>The schema whose <c>contract-violation</c> occurrence answers this from the producer's side.</summary>
    internal static readonly Version Tripwire = new(0, 45, 0);

    /// <summary>The producer's own detail prefix for a domain's census admission.</summary>
    internal const string AdmittedPrefix = "course-admitted:";

    /// <summary>The recorder's kind for the contract that asks this question from the producer's side.</summary>
    internal const string TripwireViolation = "census-admitted-binder-refused";

    /// <summary>
    /// Which domains a refusal reason can have come from, mirroring <c>AuditDecisionContracts.RefusalFor</c>
    /// in the other direction. Both literals are the binders' own — <c>CombatCourseOpportunity</c>'s
    /// <c>target-capture-missing</c> and <c>AssistanceOpportunityBinder</c>'s
    /// <c>assistance-target-unresolved</c> — and the self-test reads all five strings out of the two
    /// source files by path, so a rename reddens the fixture rather than silencing the check.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> DomainsBehind = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["target-capture-missing"] = new[] { "combat" },
        ["assistance-target-unresolved"] = new[] { "collect-target", "light-target", "pot-target" },
    };

    public string Name => "did a census admission survive the binder in its own decision";

    public string[] Needs => new[] { "tick" };

    public string? Missing(Session session)
    {
        if (CheckEvents.SidecarUnavailable(session, "the course decisions and their census admissions") is { } unavailable) return unavailable;
        return CompletedTransferClaimsWereReceived.SchemaAtLeast(session, First) ? null
            : $"course census admissions and refusal tallies (first written by schema {First})";
    }

    /// <summary>One domain refused for one reason, over the whole session.
    ///
    /// <para><b>Records and decisions are counted apart, because a retained course republishes.</b> The
    /// producer traces a <c>course-decision</c> payload per tick while one decision is carried across
    /// many, so 842 combat records of the 22 September 2026 capture are 555 decisions under the
    /// <c>choice_id</c> identity the timeline and this report key on everywhere else, and the 6,299
    /// refusals they carry are that decision's tally re-recorded rather than 6,299 distinct orders. The
    /// first version of this check said "842 decision(s)" and "6,299 order(s)" flatly, which matched all
    /// six hand readings of the capture and was inherited inflation in all seven.</para></summary>
    private sealed class Contradiction
    {
        public int ObservedDecisions, CarriedDecisions, MaximumUsable, Ambiguous;
        public long ObservedRefusals, CarriedRefusals;
        public long FirstTick = -1, LastTick = -1;

        /// <summary>The distinct <c>choice_id</c> values behind those records, where the capture carries
        /// the column. Empty means it does not, and the finding then counts records and says so.</summary>
        public readonly HashSet<string> Identities = new(StringComparer.Ordinal);
    }

    public IEnumerable<Finding> Run(Session session)
    {
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(session.Path);
        CourseDecisionLog decisions = ReadCourseDecisions.From(log);
        if (decisions.Decisions.Count == 0) yield break;

        // The census admissions, by the tick of the `decision` occurrence that carried them.
        var admissionsAt = new Dictionary<long, Dictionary<string, (long Usable, string Reason)>>();
        foreach (GodsEyeEvent e in log.Events)
        {
            if (!string.Equals(e.kind, "decision", StringComparison.Ordinal)) continue;
            var admitted = ReadAdmissions(e.detail);
            if (admitted.Count > 0) admissionsAt[e.tick] = admitted;
        }
        if (admissionsAt.Count == 0) yield break;

        // **An admission is carried from the last occurrence at or before the decision, not only from
        // one landing on its own tick.** The two producers keep different cadences: the `decision`
        // occurrence is periodic and the payload is written when an outcome is traced, so on the
        // 22 September 2026 capture 230 of the 468 admission ticks carry no payload at all. Reading the
        // carry out of a per-tick lookup dropped every one of those and left the decisions after them
        // reading an older admission — nearly half the census the finding's numbers are built on.
        long[] admissionTicks = admissionsAt.Keys.OrderBy(t => t).ToArray();
        int admissionAt = 0;

        // The decision identity each tick's records belong to, so the finding can count decisions
        // rather than traces. Below schema 0.43.0 `choice_id` is the retired family chooser's
        // comparison identity and grouping on it would group a brain the capture did not run, so the
        // map stays empty and the prose counts records instead — the same gate WriteCourseTimeline uses.
        var identityAt = new Dictionary<long, string>();
        if (CompletedTransferClaimsWereReceived.SchemaAtLeast(session, WriteCourseTimeline.First)
            && session.Find("choice_id") is { } identities)
            for (int i = 0; i < session.Count; i++)
                if (identities.Text[i] is { Length: > 0 } value && value != "-" && value != "0")
                    identityAt[session.Tick(i)] = value;

        var found = new Dictionary<(string Reason, string Domain), Contradiction>();
        Dictionary<string, (long Usable, string Reason)> carried = new(StringComparer.Ordinal);
        foreach (CourseDecision decision in decisions.Decisions.OrderBy(d => d.Tick))
        {
            while (admissionAt < admissionTicks.Length && admissionTicks[admissionAt] <= decision.Tick)
                carried = admissionsAt[admissionTicks[admissionAt++]];
            // Observed means the admission and the refusal are in one record of one tick; an admission
            // consumed from an earlier tick is the inference, and the grade below is what says so. It is
            // asked of the lookup rather than of the pointer because several decisions share a tick —
            // 2,340 payloads over 1,364 ticks on that capture — and the pointer has already passed the
            // admission by the second of them.
            bool observedHere = admissionsAt.ContainsKey(decision.Tick);
            foreach ((string reason, long refused) in decision.Refusals)
            {
                if (refused <= 0 || !DomainsBehind.TryGetValue(reason, out string[]? domains)) continue;
                // Several assistance domains can be admitted usable at once and the refusal tally names
                // only its reason, so the refusal cannot be attributed to one of them. That is recorded
                // rather than guessed at: the contradiction still holds for each, and the count says so.
                int usableDomains = domains.Count(d => carried.TryGetValue(d, out var a) && a.Usable > 0);
                foreach (string domain in domains)
                {
                    if (!carried.TryGetValue(domain, out var admission) || admission.Usable <= 0) continue;
                    if (!found.TryGetValue((reason, domain), out Contradiction? c))
                        found[(reason, domain)] = c = new Contradiction();
                    if (observedHere) { c.ObservedDecisions++; c.ObservedRefusals += refused; }
                    else { c.CarriedDecisions++; c.CarriedRefusals += refused; }
                    if (identityAt.TryGetValue(decision.Tick, out string? identity)) c.Identities.Add(identity);
                    if (usableDomains > 1) c.Ambiguous++;
                    c.MaximumUsable = (int)Math.Max(c.MaximumUsable, admission.Usable);
                    if (c.FirstTick < 0) c.FirstTick = decision.Tick;
                    c.LastTick = decision.Tick;
                }
            }
        }

        string[] tripwired = TripwireDomains(log);
        bool hasTripwire = CompletedTransferClaimsWereReceived.SchemaAtLeast(session, Tripwire);

        foreach (var pair in found.OrderByDescending(p => p.Value.ObservedRefusals + p.Value.CarriedRefusals))
        {
            (string reason, string domain) = pair.Key;
            Contradiction c = pair.Value;
            // A decision that carries both records is the contradiction observed; one read against a
            // carried admission is the same reading with an inference in it, and the grade follows.
            bool observed = c.ObservedDecisions > 0;
            long recordCount = c.ObservedDecisions + c.CarriedDecisions;
            long refusalCount = c.ObservedRefusals + c.CarriedRefusals;
            // A retained course traces a record per tick, so the records are not the decisions and the
            // refusals they carry are one decision's tally re-recorded. Both are printed, named.
            string counted = c.Identities.Count > 0
                ? $"{c.Identities.Count:n0} decision(s), traced over {recordCount:n0} record(s),"
                : $"{recordCount:n0} traced record(s) — the capture carries no usable `choice_id`, so these are "
                  + "records rather than decisions and a retained course contributes one per tick —";
            long decisionCount = c.Identities.Count > 0 ? c.Identities.Count : recordCount;
            yield return new Finding(observed ? Severity.Definitive : Severity.Potential, Name,
                $"the {domain} census admitted work as usable and the same decision refused every order naming it with {reason}",
                $"{counted} between ticks {c.FirstTick:n0} and {c.LastTick:n0} refused "
                    + $"{refusalCount:n0} order(s) as recorded for {reason} while the {domain} census read up to "
                    + $"{c.MaximumUsable} usable — that order figure counts refusals as they were traced, so a "
                    + "decision carried across ticks republishes its own tally and it is an upper bound on distinct "
                    + $"orders rather than a count of them. {c.ObservedDecisions:n0} of those records carried the "
                    + "admission and the refusal in one record and are the observed contradiction; "
                    + $"{c.CarriedDecisions:n0} carried only the refusal and were read against the last "
                    + "`decision` occurrence before them, which is how the producer's own `Admitted` list "
                    + "behaves while a retained course is carried and is an inference all the same — an "
                    + "admission that moved between two of those occurrences is invisible here. "
                    + Attribution(reason, domain, c)
                    + "Both sides of this read one frozen observation: the census admitted the domain, and "
                    + $"the binder refused every order built from it because the target fact was not Observed, "
                    + "which is the discovery side and the binding side of one decision disagreeing about one "
                    + $"fact. {Corroboration(hasTripwire, tripwired, domain)}",
                (int)Math.Min(int.MaxValue, c.FirstTick), (int)Math.Min(int.MaxValue, c.LastTick), (int)Math.Min(int.MaxValue, decisionCount),
                $"a {reason} refusal against a usable census");
        }

        // The tripwire naming a domain this reader found nothing in is the two instruments disagreeing,
        // which is a fact about the instruments and is reported as one rather than resolved by picking.
        if (!hasTripwire) yield break;
        string[] unseen = tripwired.Where(d => !found.Keys.Any(k => k.Domain == d)).ToArray();
        if (unseen.Length == 0) yield break;
        yield return new Finding(Severity.Potential, Name,
            $"the recorder's own tripwire named {unseen.Length} domain(s) this reader found no contradiction in",
            $"The capture carries `{TripwireViolation}` contract violation(s) for {string.Join(", ", unseen)} and "
                + "this check found none there. The two ask the same question from opposite sides — the recorder "
                + "keys on the frozen observation's ordinal with the live census in hand, this reads the published "
                + "`decision` and `course-decision` records — so a disagreement is one of them being wrong rather "
                + "than a fact about the play. What would settle it: the `decision` occurrence on a tick the "
                + "tripwire fired, read for the domain's own `course-admitted:` entry.",
            0, 0, unseen.Length, "the tripwire and this reader disagree");
    }

    private static string Attribution(string reason, string domain, Contradiction c)
        => c.Ambiguous == 0 ? ""
            : $"On {c.Ambiguous:n0} of those records more than one domain behind `{reason}` was admitted usable at "
              + $"once, so the refusal tally cannot be attributed to {domain} alone on those — the reason names the "
              + "binder and not the site. The contradiction holds for each such domain and is reported for each. ";

    private static string Corroboration(bool hasTripwire, string[] tripwired, string domain)
        => !hasTripwire
            ? $"A capture at schema {Tripwire} or later carries the recorder's own `{TripwireViolation}` contract "
              + "violation for this, keyed on the frozen observation's ordinal; this one predates it, so the "
              + "reading here rests on the published records alone."
            : tripwired.Contains(domain, StringComparer.Ordinal)
                ? $"The recorder's own `{TripwireViolation}` tripwire fired for {domain} in this capture, so the two "
                  + "instruments agree from opposite sides of the decision."
                : $"The recorder's own `{TripwireViolation}` tripwire did **not** fire for {domain} in this capture, "
                  + "which the two instruments cannot both be right about; the disagreement is reported separately.";

    /// <summary>The domains named by the recorder's own contract violations of this kind.</summary>
    private static string[] TripwireDomains(GodsEyeEventLog log)
    {
        var domains = new List<string>();
        foreach (GodsEyeEvent e in log.Events)
        {
            if (!string.Equals(e.payload_kind, "contract-violation", StringComparison.Ordinal)) continue;
            if (e.payload is not { ValueKind: System.Text.Json.JsonValueKind.Object } payload
                || !payload.TryGetProperty("Fields", out var fields)
                || !fields.TryGetProperty("violation", out var violation)
                || !violation.TryGetProperty("Text", out var kind)
                || kind.GetString() != TripwireViolation) continue;
            // The signature is `<domain>:<refusal>:<evidence>`, which is the only place the record names
            // its domain in a form a reader can take without parsing prose.
            if (!fields.TryGetProperty("signature", out var signatureField)
                || !signatureField.TryGetProperty("Text", out var signatureText)) continue;
            string signature = signatureText.GetString() ?? "";
            int colon = signature.IndexOf(':');
            if (colon <= 0) continue;
            string domain = signature[..colon];
            if (!domains.Contains(domain, StringComparer.Ordinal)) domains.Add(domain);
        }
        return domains.ToArray();
    }

    /// <summary>Every <c>course-admitted:&lt;domain&gt;=usable:N,unknown:N,unusable:N,reason:…</c> in a
    /// `decision` occurrence's detail. The entry's own values are comma-separated <c>key:value</c>
    /// pairs, which is a second format inside the first and is why this does not go through
    /// <c>ReadGodsEyeEvents.Field</c>.</summary>
    internal static Dictionary<string, (long Usable, string Reason)> ReadAdmissions(string detail)
    {
        var admitted = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
        foreach (string part in detail.Split(';'))
        {
            if (!part.StartsWith(AdmittedPrefix, StringComparison.Ordinal)) continue;
            int equals = part.IndexOf('=', AdmittedPrefix.Length);
            if (equals < 0) continue;
            string domain = part[AdmittedPrefix.Length..equals];
            long usable = 0;
            string reason = "";
            foreach (string field in part[(equals + 1)..].Split(','))
            {
                int colon = field.IndexOf(':');
                if (colon <= 0) continue;
                string key = field[..colon], value = field[(colon + 1)..];
                if (key == "usable") long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out usable);
                else if (key == "reason") reason = value;
            }
            if (domain.Length > 0) admitted[domain] = (usable, reason);
        }
        return admitted;
    }
}
