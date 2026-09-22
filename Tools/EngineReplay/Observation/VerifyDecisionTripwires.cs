#nullable enable

using System.Globalization;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// The decision tripwires: each contract fires on a scene that breaks it and stays quiet on the
/// neighbouring scene that does not.
///
/// <b>Every row here is a pair, and the quiet half is the half that matters.</b> A tripwire that
/// fires on its own scene has proved only that it can fire; what a reader of a capture needs to know
/// is that it does *not* fire on the legal case next door, because a violation kind that appears on
/// every decision is one nobody will read twice. So the empty-course rule is run against a decision
/// whose refusals include a third, legitimate reason; the overrun rule is run at exactly its ceiling;
/// the size rule at exactly its bound; the release rule four ticks after its publication rather than
/// one; and the activity-exit rule on a settled decision that changes activity, which is the brain
/// working.
///
/// The scenes are the 22 September 2026 capture's own numbers wherever one exists — combat admitted
/// three usable against twelve `target-capture-missing`, collection four against sixteen
/// `assistance-target-unresolved` — so a row that goes red is read against a recording a person has
/// already looked at rather than against invented arithmetic.
/// </summary>
internal static class VerifyDecisionTripwires
{
    private const string Family = "decision tripwire";

    public static int Run()
    {
        int failed = 0;
        failed += RunOneRow.Case("a census admission its own binder refused is named", ACensusAdmissionRefusedByItsOwnBinderIsNamed, Family);
        failed += RunOneRow.Case("an empty course beside usable work is named only when every refusal is not-observed", AnEmptyCourseIsNamedOnlyForTheNotObservedPair, Family);
        failed += RunOneRow.Case("an accepted use gone within a tick of publishing is named", AnAcceptedUseGoneWithinATickIsNamed, Family);
        failed += RunOneRow.Case("an activity leaving during an unsettled decision is named", AnActivityLeavingDuringAnUnsettledDecisionIsNamed, Family);
        failed += RunOneRow.Case("a frozen observation past its declared size is named", AnOversizedObservationIsNamed, Family);
        failed += RunOneRow.Case("a decision past its allowance and its largest slice is named", AnOverrunningDecisionIsNamed, Family);
        failed += RunOneRow.Case("one decision carried across ticks is audited once", OneDecisionCarriedAcrossTicksIsAuditedOnce, Family);
        failed += RunOneRow.Case("a refused target's evidence and its age ride on the decision", RefusedTargetEvidenceRidesOnTheDecision, Family);
        failed += RunOneRow.Case("a contradiction held for hundreds of ticks is counted in full and written a handful of times", AHeldContradictionIsCountedInFullAndCoalesced, Family);
        failed += RunOneRow.Case("the refusal strings the contracts read are still the ones the binders emit", TheRefusalStringsStillMatchTheirProducers, Family);
        return failed;
    }

    // ── contract one ──────────────────────────────────────────────────────────────────────────────

    private static void ACensusAdmissionRefusedByItsOwnBinderIsNamed()
    {
        AuditDecisionContracts.Reset();
        Audit(100, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60,
                ("target-capture-missing", 12)),
            Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: false)));
        Require(Count("census-admitted-binder-refused") == 1,
            $"a combat census of three usable refused with target-capture-missing must be named; counts={Counts()}");

        // The same admission, refused for a reason that is not about the target's evidence at all.
        // This is the brain declining work it priced, which is not a contradiction.
        AuditDecisionContracts.Reset();
        Audit(100, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60,
                ("no-use-with-captured-travel-and-target-impact", 12)),
            Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: false)));
        Require(Count("census-admitted-binder-refused") == 0,
            $"a refusal that is not about the target's evidence must not be named as a contradiction; counts={Counts()}");

        // And a domain the census admitted nothing in cannot contradict anything, however it refused.
        AuditDecisionContracts.Reset();
        Audit(100, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60,
                ("assistance-target-unresolved", 16)),
            Inputs(Admitted(("collect-target", 0)), Target("collect-target", "7", observed: false)));
        Require(Count("census-admitted-binder-refused") == 0,
            $"a domain that admitted nothing usable cannot be contradicted by its own refusal; counts={Counts()}");
    }

    // ── contract two ──────────────────────────────────────────────────────────────────────────────

    private static void AnEmptyCourseIsNamedOnlyForTheNotObservedPair()
    {
        AuditDecisionContracts.Reset();
        Audit(200, 1, Decision("published-course-holds-no-step", "keep-company", settled: true, steps: 0, facts: 60,
                ("target-capture-missing", 12), ("assistance-target-unresolved", 16)),
            Inputs(Admitted(("combat", 3), ("collect-target", 4)),
                Target("combat-target", "8000001", observed: false), Target("collect-target", "7", observed: false)));
        Require(Count("empty-course-beside-usable-work") == 1,
            $"an empty course beside seven usable opportunities, refused only for want of an observed target, must be named; counts={Counts()}");

        // The negative that separates this rule from "any refusal": one legitimate refusal beside the
        // pair means the search threw work away for a reason the objective is entitled to hold.
        AuditDecisionContracts.Reset();
        Audit(200, 1, Decision("published-course-holds-no-step", "keep-company", settled: true, steps: 0, facts: 60,
                ("target-capture-missing", 12), ("assistance-target-unresolved", 16),
                ("no-use-with-captured-travel-and-target-impact", 1)),
            Inputs(Admitted(("combat", 3), ("collect-target", 4)),
                Target("combat-target", "8000001", observed: false), Target("collect-target", "7", observed: false)));
        Require(Count("empty-course-beside-usable-work") == 0,
            $"a third refusal reason beside the not-observed pair must keep the empty-course rule quiet; counts={Counts()}");

        // A decision still running has not published anything, so it cannot have published an empty course.
        AuditDecisionContracts.Reset();
        Audit(200, 1, Decision("deciding", "keep-company", settled: false, steps: 0, facts: 60,
                ("target-capture-missing", 12)),
            Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: false)));
        Require(Count("empty-course-beside-usable-work") == 0,
            $"an unsettled decision has published nothing and must not be named as an empty course; counts={Counts()}");
    }

    // ── contract three ────────────────────────────────────────────────────────────────────────────

    private static void AnAcceptedUseGoneWithinATickIsNamed()
    {
        AuditDecisionContracts.Reset();
        Audit(10, 1, Decision("course-published", "combat", settled: true, steps: 2, facts: 60), Empty());
        Audit(11, 2, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60,
            release: "next-use-invalid:accepted-use-not-present"), Empty());
        Require(Count("accepted-use-absent-next-tick") == 1,
            $"a course released for a missing accepted use one tick after publishing must be named; counts={Counts()}");

        // Four ticks later is a course that ran and then lost its use, which is what retention is for.
        AuditDecisionContracts.Reset();
        Audit(10, 1, Decision("course-published", "combat", settled: true, steps: 2, facts: 60), Empty());
        Audit(14, 2, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60,
            release: "next-use-invalid:accepted-use-not-present"), Empty());
        Require(Count("accepted-use-absent-next-tick") == 0,
            $"a release four ticks after publication is retention working and must not be named; counts={Counts()}");
    }

    // ── contract four ─────────────────────────────────────────────────────────────────────────────

    private static void AnActivityLeavingDuringAnUnsettledDecisionIsNamed()
    {
        AuditDecisionContracts.Reset();
        Audit(20, 1, Decision("course-published", "combat", settled: true, steps: 1, facts: 60), Empty());
        Audit(21, 2, Decision("deciding", "keep-company", settled: false, steps: 0, facts: 60), Empty());
        Require(Count("activity-exited-during-decision") == 1,
            $"combat giving way to keep-company while a decision is still running must be named; counts={Counts()}");

        // A settled decision changing activity is the brain choosing, which is the whole job.
        AuditDecisionContracts.Reset();
        Audit(20, 1, Decision("course-published", "combat", settled: true, steps: 1, facts: 60), Empty());
        Audit(21, 2, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60), Empty());
        Require(Count("activity-exited-during-decision") == 0,
            $"a settled decision changing activity is a choice and must not be named; counts={Counts()}");
    }

    // ── contract five ─────────────────────────────────────────────────────────────────────────────

    private static void AnOversizedObservationIsNamed()
    {
        int bound = AuditDecisionContracts.MaximumFactsPerDecision;
        AuditDecisionContracts.Reset();
        Audit(30, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: bound + 1), Empty());
        Require(Count("fact-count-above-bound") == 1,
            $"an observation of {bound + 1} facts against a bound of {bound} must be named; counts={Counts()}");

        AuditDecisionContracts.Reset();
        Audit(30, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: bound), Empty());
        Require(Count("fact-count-above-bound") == 0,
            $"an observation of exactly {bound} facts is inside the bound and must not be named; counts={Counts()}");
    }

    // ── contract six ──────────────────────────────────────────────────────────────────────────────

    private static void AnOverrunningDecisionIsNamed()
    {
        double ceiling = AuditDecisionContracts.DecideCeilingMilliseconds;
        AuditDecisionContracts.Reset();
        Audit(40, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60), Empty());
        AuditDecisionContracts.ObserveDecideCost(ceiling + 1d, 40);
        Require(Count("decide-overran-allowance") == 1,
            $"a decision costing {ceiling + 1d:0.000} ms against a ceiling of {ceiling:0.000} ms must be named; counts={Counts()}");

        AuditDecisionContracts.Reset();
        Audit(40, 1, Decision("course-published", "keep-company", settled: true, steps: 0, facts: 60), Empty());
        AuditDecisionContracts.ObserveDecideCost(ceiling, 40);
        Require(Count("decide-overran-allowance") == 0,
            $"a decision costing exactly the ceiling of {ceiling:0.000} ms is inside its allowance plus one atomic slice; counts={Counts()}");

        // A cost with no decision behind it has nothing to be filed against, and inventing a context
        // for it would put an overrun in the record with no decision to join it to.
        AuditDecisionContracts.Reset();
        AuditDecisionContracts.ObserveDecideCost(ceiling + 100d, 40);
        Require(Count("decide-overran-allowance") == 0,
            $"an overrun before any decision has been recorded has nothing to name; counts={Counts()}");
    }

    // ── the clock the contracts are judged on ─────────────────────────────────────────────────────

    private static void OneDecisionCarriedAcrossTicksIsAuditedOnce()
    {
        AuditDecisionContracts.Reset();
        DecisionInputs inputs = Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: false));
        for (long tick = 50; tick < 100; tick++)
            Audit(tick, ordinal: 7, Decision("course-retained", "keep-company", settled: true, steps: 0, facts: 60,
                ("target-capture-missing", 12)), inputs);
        Require(Count("census-admitted-binder-refused") == 1,
            "one frozen observation carried for fifty ticks is one decision and must be named once, not fifty times;"
                + $" counts={Counts()}");
        Require(AuditDecisionContracts.Audited == 50,
            $"every recorded decision must still reach the audit, carried or not; audited={AuditDecisionContracts.Audited}");
    }

    // ── the evidence the payload gains ────────────────────────────────────────────────────────────

    private static void RefusedTargetEvidenceRidesOnTheDecision()
    {
        AuditDecisionContracts.Reset();
        // Tick 1 sees the target observed; tick 41 sees the same key unresolved, which is the shape the
        // capture holds and the one only a watcher of every snapshot can date.
        Audit(1, 1, Decision("course-published", "combat", settled: true, steps: 1, facts: 60),
            Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: true)));
        var fields = Audit(41, 2, Decision("published-course-holds-no-step", "keep-company", settled: true, steps: 0, facts: 60,
                ("target-capture-missing", 12)),
            Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: false)));
        string evidence = Field(fields, "target-evidence"), age = Field(fields, "target-evidence-age");
        Require(evidence.Contains("combat=combat-target:8000001:0:Unresolved", StringComparison.Ordinal),
            $"the refused target's key and evidence must ride on the decision; target-evidence='{evidence}'");
        Require(age == "combat=40",
            $"the age must be the ticks since this recorder last saw that key observed, 40 here; target-evidence-age='{age}'");

        // A key never seen observed is -1 rather than 0, because "fresh" and "never" are opposite facts
        // and a reader of the age alone has to be able to tell them apart.
        AuditDecisionContracts.Reset();
        var never = Audit(41, 1, Decision("published-course-holds-no-step", "keep-company", settled: true, steps: 0, facts: 60,
                ("assistance-target-unresolved", 16)),
            Inputs(Admitted(("collect-target", 4)), Target("collect-target", "7", observed: false)));
        Require(Field(never, "target-evidence-age") == "collect-target=-1",
            $"a key never seen observed must read -1 rather than 0; target-evidence-age='{Field(never, "target-evidence-age")}'");

        // A decision that refused nothing the census admitted gains no evidence fields at all, because
        // an empty pair of columns on every healthy decision is noise a reader learns to skip.
        AuditDecisionContracts.Reset();
        var clean = Audit(41, 1, Decision("course-published", "combat", settled: true, steps: 1, facts: 60),
            Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: true)));
        Require(clean.Count == 0, $"a decision with no contradiction must add no evidence fields; added {clean.Count}");
    }

    // ── what reaches the sidecar ──────────────────────────────────────────────────────────────────

    private static void AHeldContradictionIsCountedInFullAndCoalesced()
    {
        string stem = Path.Combine(Path.GetTempPath(), $"aic-tripwire-{Guid.NewGuid():N}");
        string tsv = stem + ".tsv", events = stem + ".jsonl";
        var records = new List<JsonElement>();
        try
        {
            File.WriteAllText(tsv, "");
            using (FlushDiagnosticRecords writer = FlushDiagnosticRecords.Start(tsv, events))
            {
                GodsEyeEvents.Open(events);
                AuditDecisionContracts.Reset();
                DecisionInputs inputs = Inputs(Admitted(("combat", 3)), Target("combat-target", "8000001", observed: false));
                // Three hundred consecutive decisions, each its own frozen observation, all contradicting
                // themselves the same way. That is the tail of the 22 September capture at half length.
                for (long tick = 0; tick < 300; tick++)
                    Audit(tick, ordinal: tick + 1,
                        Decision("published-course-holds-no-step", "keep-company", settled: true, steps: 0, facts: 60,
                            ("target-capture-missing", 12)), inputs);
                // What the recorder's own close does, and the reason it has to: without it the last
                // written record is tick 240's and the capture's total reads 241 of 300.
                AuditDecisionContracts.Flush();
                writer.FlushForReader(TimeSpan.FromSeconds(5));
                writer.Stop(TimeSpan.FromSeconds(5), "fixture-close");
            }
            foreach (string line in File.ReadAllLines(events))
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonElement record = JsonDocument.Parse(line).RootElement;
                if (record.TryGetProperty("payload_kind", out JsonElement kind) && kind.GetString() == "contract-violation")
                    records.Add(record);
            }
        }
        finally
        {
            GodsEyeEvents.Close();
            if (File.Exists(tsv)) File.Delete(tsv);
            if (File.Exists(events)) File.Delete(events);
        }

        Require(AuditDecisionContracts.Counts.TryGetValue("census-admitted-binder-refused", out long total) && total == 300,
            $"three hundred contradicting decisions must be counted in full; counts={Counts()}");
        // The scene breaks two contracts at once — the census contradiction and the empty course it
        // produces — so the records are counted per kind rather than in total. Counting them together
        // is what the first run of this row did, and it reported ten where each kind wrote five.
        var contradiction = records.Where(r => Payload(r, "violation") == "census-admitted-binder-refused").ToList();
        // One at the first fire and one per sixty ticks after it: five records rather than three hundred,
        // which is what keeps a pathological session inside the sidecar's optional partition.
        Require(contradiction.Count is > 0 and <= 8,
            $"a contradiction held for three hundred ticks must be written a handful of times, not once a tick;"
                + $" wrote {contradiction.Count} of that kind and {records.Count} violation records in all");
        Require(Payload(contradiction[^1], "occurrences-total") == "300",
            $"the last record must carry the running total the coalescing dropped; occurrences-total='{Payload(contradiction[^1], "occurrences-total")}'");
        Require(Payload(contradiction[0], "detail").Contains("admitted 3 usable", StringComparison.Ordinal)
                && Payload(contradiction[0], "observation-ordinal").Length > 0,
            $"a violation record must name its numbers and the observation it describes; first={Payload(contradiction[0], "detail")}");
        Require(records.Any(r => Payload(r, "violation") == "empty-course-beside-usable-work"),
            "the same scene publishes an empty course beside that usable work, and both contracts must reach the sidecar");
    }

    // ── the producer pin ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The contracts read two refusal strings out of the record, and both are literals in two files
    /// this fixture does not compile. A rename there would make every contract above silently unable
    /// to fire while every row here still passed, which is this folder's own trap about a diagnostic
    /// reading a producer that moved — so the literals are pinned against their sources.
    /// </summary>
    private static void TheRefusalStringsStillMatchTheirProducers()
    {
        string root = RepositoryRoot();
        (string Path, string Literal)[] pins =
        {
            ("Companion/Brain/Activities/Combat/CombatCourseOpportunity.cs", AuditDecisionContracts.CombatNotObserved),
            ("Companion/Brain/Infrastructure/Selection/Opportunities/BindAssistanceOpportunity.cs", AuditDecisionContracts.AssistanceNotObserved),
        };
        foreach ((string path, string literal) in pins)
        {
            string full = Path.Combine(root, path);
            Require(File.Exists(full), $"the tripwires' producer {path} is not where they expect it, under {root}");
            Require(File.ReadAllText(full).Contains($"\"{literal}\"", StringComparison.Ordinal),
                $"{path} no longer emits \"{literal}\", so every contract reading it is silently unable to fire");
        }
    }

    /// <summary>The repository root, found by walking up from the running assembly until the mod's own
    /// project file is beside us. Reading it from the working directory would make this row depend on
    /// where the harness was launched from.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AICompanion.csproj")))
            directory = directory.Parent;
        Require(directory != null, $"no AICompanion.csproj above {AppContext.BaseDirectory}");
        return directory!.FullName;
    }

    // ── scene builders ────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<KeyValuePair<string, CourseTraceValue>> Audit(
        long tick, long ordinal, CourseTracePayload payload, DecisionInputs inputs)
        => AuditDecisionContracts.Audit(Context(tick, ordinal), payload, inputs);

    private static CourseTraceContext Context(long tick, long ordinal)
        => new(tick, "brain-decide", 0, 0, 0, 0, 0, "DecideCourseEachTick", ordinal, 0, "1", "1", "policy", "motion:1");

    private static CourseTracePayload Decision(string reason, string activity, bool settled, long steps, long facts,
        params (string Reason, long Count)[] refusals)
        => Decision(reason, activity, settled, steps, facts, "", refusals);

    private static CourseTracePayload Decision(string reason, string activity, bool settled, long steps, long facts,
        string release, params (string Reason, long Count)[] refusals)
    {
        var fields = new List<KeyValuePair<string, CourseTraceValue>>
        {
            new("reason", CourseTraceValue.TextValue(reason)),
            new("activity", CourseTraceValue.TextValue(activity)),
            new("settled", CourseTraceValue.Flag(settled)),
            new("purpose", CourseTraceValue.TextValue("")),
            new("steps", CourseTraceValue.Integer(steps)),
            new("orders-priced", CourseTraceValue.Integer(1)),
            new("orders-refused", CourseTraceValue.Integer(refusals.Sum(r => r.Count))),
            new("search-exhausted", CourseTraceValue.Flag(true)),
            new("release-reason", CourseTraceValue.TextValue(release)),
            new("facts", CourseTraceValue.Integer(facts)),
        };
        foreach ((string reasonName, long count) in refusals)
            fields.Add(new("refused:" + reasonName, CourseTraceValue.Integer(count)));
        return new CourseTracePayload("course-decision", 1, fields);
    }

    private static DecisionInputs Empty() => new(Array.Empty<CensusAdmission>(), Array.Empty<TargetFact>());

    private static DecisionInputs Inputs(IReadOnlyList<CensusAdmission> admitted, params TargetFact[] targets)
        => new(admitted, targets);

    private static CensusAdmission[] Admitted(params (string Domain, int Usable)[] domains)
        => domains.Select(d => new CensusAdmission(d.Domain, d.Usable, 0, 0, "")).ToArray();

    private static TargetFact Target(string kind, string identity, bool observed)
        => new(kind, $"{kind}:{identity}:0", observed, observed ? "Observed" : "Unresolved");

    private static long Count(string kind)
        => AuditDecisionContracts.Counts.TryGetValue(kind, out long value) ? value : 0;

    private static string Counts()
        => AuditDecisionContracts.Counts.Count == 0 ? "(nothing fired)"
            : string.Join(", ", AuditDecisionContracts.Counts.Select(pair => $"{pair.Key}={pair.Value}"));

    private static string Field(IReadOnlyList<KeyValuePair<string, CourseTraceValue>> fields, string name)
    {
        foreach (var field in fields) if (field.Key == name) return field.Value.Text;
        return "";
    }

    private static string Payload(JsonElement record, string name)
        => record.TryGetProperty("payload", out JsonElement payload)
            && payload.TryGetProperty("Fields", out JsonElement fields)
            && fields.TryGetProperty(name, out JsonElement field)
            && field.TryGetProperty("Text", out JsonElement text) ? text.GetString() ?? "" : "";

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
