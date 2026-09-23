using System.Globalization;
using AICompanion.Tools.Ledger;

/// <summary>
/// What the morning of 22 September 2026 looked like, as rows a fix can turn green.
///
/// The play it reproduces is one minute in world Lilalio in which the companion, with five to seven
/// hostiles within reach and three drops on the floor, did nothing at all for the last 524 ticks.
/// Every decision of that stretch read the same three things at once and the contradiction between
/// them is the whole finding: the domain census admitted three combat and four collection
/// opportunities as <em>usable</em>, the order search refused all twenty-eight orders built from
/// them — twelve <c>target-capture-missing</c> and sixteen <c>assistance-target-unresolved</c>,
/// which are one predicate in source — and the only order left to price was the empty one, which is
/// companionship. So the companion kept company beside a fight it had already planned.
///
/// Two rows here are verdicts and the rest are measures, and which is which is a judgement about
/// what can be wrong rather than about what is easy to assert:
///
/// <list type="number">
/// <item><b>A refusal that contradicts its own census is always a defect.</b> An order refused for a
/// target the same frozen observation admitted as usable is not a preference the brain expressed;
/// it is two readers of one store disagreeing. There is no scene in which it is correct, so it is a
/// pass line and not a threshold.</item>
/// <item><b>Doing nothing for three seconds while work is admitted is always a defect.</b> The bound
/// is the project's own: <c>ScoreTheRun</c> already gives combat 180 ticks to take the body once a
/// hostile stands beside the route, so a course that binds no step at all inside the same window is
/// held to the same three seconds rather than to a number invented here.</item>
/// </list>
///
/// There was a third, and it was demoted on the day it was reviewed: *the hands fire again after the
/// last recorded kill* had the predicate <c>fired &gt; 0</c> over a five-hundred-tick window, so one
/// fired tick satisfied it as readily as fifty. That is a measure wearing a verdict's clothes, and
/// the share beside it was always the row carrying the meaning, so the share is all that is left.
/// Nothing here invents a firing rate to hold it against, because what a companion in a fight ought
/// to fire is its weapon's cooldown times the ticks it was engaged and no row in this file has that.
///
/// Everything else — the empty-course share, the refusal tallies, the decision cost, the collector,
/// the staging fidelity — is a measure, because each has a legitimate non-zero value and a pass line
/// on any of them would be a number nobody declared becoming a verdict. The cost measures are taken
/// under the game's own clock and say so in their mode, because the brain a player met was one being
/// cut by its deadline and a figure taken with the allowances lifted is not a frame cost.
///
/// **Both verdicts refuse to grade rather than pass whenever the run could not have failed them**,
/// and there are five such conditions: no ticks, a cut sidecar, a cast with nothing in it, a census
/// that admitted usable work on fewer ticks than the floor, and a refusal literal that has left the
/// file that writes it. Each is a skip naming what was missing, because a skip is loud on the
/// scoreboard in its own block and a pass is not.
/// </summary>
internal static class GradeThePlayMeasures
{
    /// <summary>
    /// How long the course may bind no step at all while some domain admits usable work, in ticks.
    ///
    /// Three seconds, and deliberately the same three seconds <see cref="ScoreTheRun"/> already
    /// allows combat to take the body in. The two are the same question asked from opposite sides —
    /// there is work in front of the companion and it is not doing it — so a second number would be
    /// two pass lines drifting apart about one behaviour.
    /// </summary>
    private const int StepWithinTicks = 180;

    /// <summary>
    /// The two refusal reasons that resolve to one predicate in source: the target fact's evidence
    /// is not Observed — paired with the file each is written in, because a literal a reader restates
    /// is a claim about a producer and a claim about a producer goes stale in exactly one direction.
    /// <see cref="ProducerLiteralsAreStillWhatTheBrainWrites"/> is what stops it going stale quietly.
    /// </summary>
    private static readonly (string Reason, string[] Producer)[] CensusContradictingRefusals =
    {
        ("target-capture-missing", new[] { "Companion", "Brain", "Activities", "Combat", "CombatCourseOpportunity.cs" }),
        ("assistance-target-unresolved", new[] { "Companion", "Brain", "Infrastructure", "Selection", "Opportunities", "BindAssistanceOpportunity.cs" }),
    };

    /// <summary>
    /// How many ticks must admit usable work before the refusal verdict means anything.
    ///
    /// The same 180 as <see cref="StepWithinTicks"/>, and the reuse is the point: a second constant
    /// here would be a second number to keep honest about one idea, which is the reason that one is
    /// shared with <see cref="ScoreTheRun"/> in the first place. The scale is right from the capture's
    /// own numbers rather than from taste — the whole capture admits usable work on about 1,790 of
    /// 2,340 ticks, roughly ten times this floor, while the 300-tick window that passed both verdicts
    /// vacuously admitted it on zero.
    /// </summary>
    private const int AdmittingTicksFloor = StepWithinTicks;

    /// <summary>
    /// How many ticks must want a fight before the step verdict means anything.
    ///
    /// The same constant again, and it is doing a harder job here. On the capture of 22 September the
    /// census admits usable work on about 1,790 of 2,340 ticks and README's scenes want a fight on
    /// about 138 of them, so this floor is *below* the qualifying count rather than ten times under
    /// it. That is the honest position: a run whose wanted-fight count is this low is a run whose
    /// verdict rests on a hundred-odd ticks, and the row prints the count so a reader can see how
    /// thin the ground under it is. A second, larger number here would be inventing a sample size
    /// nobody measured.
    /// </summary>
    private const int WantedFightTicksFloor = StepWithinTicks;

    private const string RefusalVerdict = "no order is refused for a target its own observation admitted";
    private const string StepVerdict = "a fight README wants, within ten seconds or inside his region, becomes a bound step within three seconds";

    public static int Grade(string suite, ReadRecordedRoute.Route route, RunTheWorld.Outcome run,
        StageRecordedActors stage, ReadRecordedActors.Cast cast, string preferences)
    {
        IReadOnlyList<RunTheWorld.PlayTick> play = run.Play;
        string staging = stage.Describe();
        string scene = $"{route.Capture} replayed under the game's own millisecond allowances, "
            + $"{play.Count} ticks from {route[0].Tick}; {preferences}; {staging}";
        // The short form every measure carries. The scene belongs on the three verdicts, which are
        // the rows a reader acts on, and nowhere else: the scoreboard prints a changed row's whole
        // message untruncated, every measure here drifts on every run because the clock is real, so
        // a scene string on all fifteen put roughly 26 KB of the same paragraph into every verify —
        // fifteen copies of one firefly's stack trace among them.
        string shortScene = $"{route.Capture}@{route[0].Tick.ToString(CultureInfo.InvariantCulture)}, "
            + $"{play.Count.ToString(CultureInfo.InvariantCulture)} ticks, production clock, "
            + $"{stage.PlacedHostiles.ToString(CultureInfo.InvariantCulture)}/{cast.Hostiles.Count.ToString(CultureInfo.InvariantCulture)} NPCs and "
            + $"{stage.PlacedDrops.ToString(CultureInfo.InvariantCulture)}/{cast.Drops.Count.ToString(CultureInfo.InvariantCulture)} drops staged, "
            + $"{stage.RetiredByAThrow.ToString(CultureInfo.InvariantCulture)} retired by a throw; the verdict rows carry the whole scene";

        // Three ways a verdict below would pass for a reason that is not the brain working, each
        // checked before any of them is asked and each reported as a skip naming what was missing.
        // A skip is loud on the scoreboard in its own block; a pass is not.
        string? cannotGrade =
            play.Count == 0 ? "the run produced no ticks"
            : cast.StoppedReadingAt is { } cut
                ? $"the events sidecar is cut and reading stopped at {cut}, so anything that appeared after the cut is missing from this scene and a verdict here would grade a world poorer than the recording's"
            : cast.Hostiles.Count == 0 && cast.Drops.Count == 0
                ? $"{Path.GetFileName(cast.EventsPath)} named no hostile and no drop, so this run replayed the player's track through an empty world "
                    + "and every verdict below would pass for want of anything to do rather than because the brain did it"
            : ProducerLiteralsAreStillWhatTheBrainWrites() is { } stale ? stale
            : null;

        int admitting = play.Count(t => t.UsableAdmitted > 0);
        string? refusalCannotGrade = cannotGrade ?? (admitting >= AdmittingTicksFloor ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"the census admitted usable work on only {admitting} of {play.Count} ticks, under the floor of {AdmittingTicksFloor}; "
                + $"below it this verdict passes because there was nothing to refuse rather than because the brain refused nothing — a window from tick 1,700 "
                + $"of the 22 September capture admits on 7 ticks and used to pass. Widen the window or check that the scene was staged"));

        // The step verdict counts a different denominator from the refusal verdict and therefore has
        // its own floor. What changed on 22 September, after lane C measured the capture: of its
        // 4,583 hostile-ticks, 3,997 are hostiles README's own 2:00 scene says to decline — a median
        // of 1,665 px from the player, receding, out of the bow's reach, the flight to them ending
        // with the companion stranded — so "some domain admitted usable work" counted as work the
        // product says not to do, and a verdict over it was grading the brain against the wrong wish.
        int wanted = play.Count(t => t.AFightIsWanted);
        string? stepCannotGrade = cannotGrade ?? (wanted >= WantedFightTicksFloor ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"README's scenes want a fight on only {wanted} of {play.Count} ticks — no hostile inside the player's region, none forecast to reach the player within ten seconds — "
                + $"under the floor of {WantedFightTicksFloor}; below it this verdict passes because there was no fight to take rather than because the companion took one"));

        int failures = 0;
        if (refusalCannotGrade != null) EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, RefusalVerdict, refusalCannotGrade);
        else failures += NoRefusalContradictsItsOwnCensus(suite, play, scene);
        if (stepCannotGrade != null) EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, StepVerdict, stepCannotGrade);
        else failures += AWantedFightIsBegun(suite, play, scene);
        failures += TheAuditRanOnTheseDecisions(suite, run, play.Count, scene);
        failures += EveryEffectWasTheAcceptedSteps(suite, run, scene);
        failures += TheObservationsFloorDoesNotClimb(suite, play, scene);
        if (play.Count > 0) Measures(suite, play, route, stage, cast, shortScene);
        return failures;
    }

    /// <summary>
    /// The growth verdict, over the decisions this replay actually made.
    ///
    /// The rule is <see cref="RunTheSoak.GradeTheFactFloor"/>'s and is not restated here, deliberately:
    /// the soak and this run differ in their scene and in nothing else that the verdict reads, and two
    /// copies of a pair of thresholds is how one of them gets tuned to whichever run somebody was looking
    /// at. What this adds is the scene that matters. The soak drives a seeded bot over the surface and its
    /// census peaks at about 170 facts; this drives the capture that leaked, and with the audit wired the
    /// same tree fired <c>fact-count-above-bound</c> on 646 of 2,340 decisions. Length was never the
    /// variable — the play's climb from 150 to 1,603 facts happened in thirty-three seconds — so the
    /// instrument that holds the leaking scene is the one that should carry this verdict.
    ///
    /// **This row is expected red on main until the brain fix lands**, the way the refusal and step
    /// verdicts beside it already are: it measures a defect this suite exists to hold, and a green here
    /// before the fix would mean the sampling stopped rather than that the growth stopped.
    ///
    /// Sampled where the decision ordinal advances rather than per tick, because a carried course holds
    /// one frozen observation across every tick it owns the body, and counting its facts once per tick
    /// would weight a long-held decision by how long it was held.
    /// </summary>
    private static int TheObservationsFloorDoesNotClimb(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, string scene)
    {
        var facts = new List<int>();
        long last = long.MinValue;
        foreach (var tick in play)
        {
            if (tick.DecisionId == last) continue;
            last = tick.DecisionId;
            facts.Add(tick.Facts);
        }
        return RunTheSoak.GradeTheFactFloor(suite,
            "the frozen observation's floor does not climb across the replayed capture",
            facts, play.Count, scene, new[] { SampleTag }, mode: "production-clock")
            // The floor and the window are the two halves of one question and they fail apart: a census
            // that re-sweeps correctly can still climb if its window fills, which is what this capture
            // does, and one that never climbs can still be accumulating inside a window nobody checked.
            // Graded together here because they read the same run and a reader chasing "is the
            // observation honest" wants both answers from one place.
            + CountTheFrozenObservationByKind.GradeTheWindow(suite, scene, new[] { SampleTag },
                mode: "production-clock");
    }

    /// <summary>
    /// The effect verdict: every native effect the replayed companion caused — a strike, a torch, a pot,
    /// a claimed pickup — was the step the course accepted, and landed on that step's target.
    ///
    /// It reads the audit's own two counts rather than the capture, for the reason the wiring row above
    /// gives: a verdict that holds under `--no-recorder` grades the run rather than the recorder's file.
    /// The denominator is `EffectsAudited`, and **zero effects is a skip, never a pass**: a replay that
    /// struck, placed, broke and claimed nothing has not shown that its hand obeys the course, and this is
    /// the grader's standing trap — a verdict green for want of anything to judge. Zero also covers a run
    /// whose binding reader was never installed, which the audit reports as unaudited rather than as a
    /// companion acting without a step.
    ///
    /// **Its ground on this capture is thin, and it says so.** On `04f9df2`, where every work activity still
    /// chose its own target, the whole replay produced one native effect the audit could judge and it was
    /// bound, so this row passed on the tree it was built to catch; the red-before for the contract is the
    /// seeded fuzzer's (`effect-off-binding` on seed 7: claimed pickups of `item:10`/`item:11` under a step
    /// naming `item:12`). The count rides in the message and as the measure beside it, so a pass on one
    /// effect reads as one effect rather than as a clean hand.
    /// </summary>
    private static int EveryEffectWasTheAcceptedSteps(string suite, RunTheWorld.Outcome run, string scene)
    {
        const string name = "every effect the companion caused was the accepted step's";
        long without = run.ContractViolations.TryGetValue("effect-without-binding", out long w) ? w : 0;
        long off = run.ContractViolations.TryGetValue("effect-off-binding", out long o) ? o : 0;
        string counts = string.Create(CultureInfo.InvariantCulture,
            $"{run.EffectsAudited} native effect(s) audited against their step: {without} effect-without-binding, {off} effect-off-binding");
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "native effects audited against the accepted step", run.EffectsAudited,
            "effects", "up", "production-clock", new[] { SampleTag },
            message: "the denominator of the effect verdict; zero skips it by name");
        if (run.EffectsAudited == 0)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name,
                "the replay caused no native effect the audit could judge — no strike, torch, pot or claimed pickup — or ran with no binding reader "
                + "installed, so this verdict would pass for want of an effect rather than because the hand obeyed the course");
            return 0;
        }
        if (without + off > 0)
        {
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
                $"{counts}; the last named was: {run.LastEffectViolation}; {scene}", mode: "production-clock");
            return 1;
        }
        EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name, $"{counts}; {scene}", mode: "production-clock",
            killedBy: "an executor searching for its own target, or an in-passing interaction performed with no accepted step");
        return 0;
    }

    /// <summary>
    /// Whether the decision audit was wired to anything on the run these rows grade.
    ///
    /// Every other row here grades what the brain did; this one grades whether the thing that checks
    /// the brain's own contracts was plugged in while it did it, and it exists because both ways that
    /// wiring fails are silent. <c>AuditDecisionContracts.Audit</c> hangs off
    /// <c>RecordCourseTrace.Record</c> and takes its inputs from a source that
    /// <c>ReadLiveCourseForAudit.Install</c> hands it out of the recorder's <c>Load</c>: lose the hook
    /// and every decision is recorded with nothing audited, and skip the install and every decision is
    /// audited against no inputs, which silently reduces six contracts to the two transitions that
    /// read the payload alone. Neither shows up as a violation, because the contracts that would have
    /// fired were never asked.
    ///
    /// **This run was in the second state until 22 September 2026, and nothing here noticed.**
    /// `AttachTheRecorder.Open` called <c>OnWorldLoad</c> and never <c>Load</c>, and
    /// `AttachCompanion` left the body out of <c>Main.npc</c>, so <c>CompanionNPC.Instance</c> — which
    /// is how the source reaches the course — found nothing. The soak lane measured the capture this
    /// very command writes: <c>decisions-audited=600;audit-observations-read=0</c>. Both halves are
    /// fixed and this row is the thing that stops either coming back, because a replay grading a brain
    /// whose contracts nobody audited is a green run that checked less than it says.
    ///
    /// The two conditions are the session reader's own, deliberately, so the headless row and
    /// <c>CheckTheDecisionAudit</c> cannot drift into disagreeing about one wiring. Equality is **not**
    /// the test and would be wrong: the observation is read once per decision ordinal and a carried
    /// course repeats its ordinal on every tick it holds the body, so a healthy run reads fewer
    /// observations than it audits. The ratio is emitted as a measure beside this.
    /// </summary>
    private static int TheAuditRanOnTheseDecisions(string suite, RunTheWorld.Outcome run, int ticks, string scene)
    {
        const string name = "the decision audit was wired to the run these rows grade";
        long audited = run.DecisionsAudited, read = run.AuditObservationsRead;
        string counts = string.Create(CultureInfo.InvariantCulture,
            $"{audited} decision(s) audited and {read} frozen observation(s) read over {ticks} ticks");

        if (ticks == 0)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name, "the run produced no ticks, so there was nothing for the audit to be wired to");
            return 0;
        }
        if (!AttachTheRecorder.Attached)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, name,
                "this run was asked for with --no-recorder, and the audit's source is installed by the recorder's own Load exactly as it is in play, "
                + "so the audit is unwired by the caller's choice rather than by a defect. Run without that flag to grade the wiring");
            return 0;
        }

        // The audit's own findings, emitted whatever the verdict below decides, because a run whose
        // wiring is broken should still show its two surviving transitions rather than a blank.
        string[] sampled = { SampleTag };
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "frozen observations read per decision audited",
            audited == 0 ? 0 : (double)read / audited, "observations", "up", "production-clock", sampled,
            message: $"{read} of {audited}; under one by the ordinal rule, because a carried course repeats its observation ordinal and the source is "
                + $"invoked once per decision rather than once per tick; {scene}");
        foreach (var kind in run.ContractViolations.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, $"contract violations of kind {kind.Key}", kind.Value,
                "violations", "down", "production-clock", sampled,
                message: "counted by the audit whether or not the recorder's coalescing kept it; a kind with no row here fired zero times on this run, "
                    + $"which means something only while the verdict beside it passes; {scene}");
        if (audited == 0)
        {
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
                $"{counts} — the audit saw none of them. It is called from RecordCourseTrace.Record, so a count of zero beside a run "
                + $"that decided every tick means that call is gone or never reached, not that the decisions were clean. Every contract in every row here measured nothing; {scene}",
                mode: "production-clock");
            return 1;
        }
        if (read == 0)
        {
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
                $"{counts} — the audit ran on every decision and never once read the observation behind it. ReadLiveCourseForAudit.Install hands the audit "
                + "its source from the recorder's Load, and the source reaches the course through CompanionNPC.Instance, which scans Main.ActiveNPCs; with "
                + "either missing, the four contracts that read the census and the target facts cannot fire at all and only the two transitions that read the "
                + $"payload alone survive, so this run looks healthier than a wired one rather than worse; {scene}",
                mode: "production-clock");
            return 1;
        }
        EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name,
            $"{counts}; fewer read than audited is the ordinal rule rather than a fault — the observation is read once per decision and a carried course "
            + $"repeats its ordinal on every tick it holds the body; {scene}",
            mode: "production-clock",
            killedBy: "asserting the two counts equal, which reddens on every carried course; or reading them out of the written capture, which would make the row "
                + "unaskable under --no-recorder and would grade the recorder's file rather than the run");
        return 0;
    }

    /// <summary>
    /// Refuses to grade the refusal row when the strings it names are no longer written by the brain.
    ///
    /// This is the row's own worst failure mode and it is the quiet one. The row counts refusals by
    /// two literals; the code that writes them is exactly the code being fixed; and a fix that
    /// renames a reason rather than removing the disagreement would make the row match nothing and
    /// go **green for the wrong reason** — green being the answer everybody is hoping for, on the
    /// row the fix is aimed at. So the literals are pinned against their producers by reading the
    /// files, which is the shape this repository already uses in
    /// <c>ChronicleTests.IdentityRulesStillMatchTheProducer</c>, in that folder's own words "so a
    /// producer rename fails the self-test instead of silently making a rule unable to fire".
    ///
    /// A missing file is a skip and not a pass, because the working directory is the one thing about
    /// this check that can be wrong for a reason that is nobody's defect.
    /// </summary>
    private static string? ProducerLiteralsAreStillWhatTheBrainWrites()
    {
        foreach ((string reason, string[] producer) in CensusContradictingRefusals)
        {
            string path = Path.Combine(producer);
            if (!File.Exists(path))
                return $"{path} is not where this run can read it — the working directory is not the repository root — "
                    + $"so the refusal literal '{reason}' cannot be checked against the code that writes it, and a row resting on an unchecked literal is not a verdict";
            if (!File.ReadAllText(path).Contains($"\"{reason}", StringComparison.Ordinal))
                return $"'{reason}' is no longer written by {path}. This row counts that literal, so it would now match nothing and pass — "
                    + "which is the wrong kind of green on the row the brain fix is aimed at. Either the disagreement is gone, in which case retire this row on purpose, "
                    + "or the reason was renamed, in which case name the new one here";
        }
        return null;
    }

    /// <summary>
    /// The contradiction itself: an order refused because a target is not Observed, on a tick whose
    /// own census called that domain's opportunities usable.
    ///
    /// The conjunction is what makes this a verdict rather than a measure. A refusal on its own is
    /// ordinary — a target genuinely out of reach is refused every tick and should be. A census
    /// admitting usable work on its own is ordinary too. The two together, inside one frozen
    /// observation, say that the census and the binder read the same store and got different
    /// answers, and there is no world in which that is the right behaviour.
    /// </summary>
    private static int NoRefusalContradictsItsOwnCensus(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, string scene)
    {
        var offending = new List<RunTheWorld.PlayTick>();
        var byReason = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RunTheWorld.PlayTick tick in play)
        {
            if (tick.UsableAdmitted <= 0) continue;
            bool contradicts = false;
            foreach ((string reason, string[] _) in CensusContradictingRefusals)
                if (tick.Refusals.TryGetValue(reason, out int count) && count > 0)
                {
                    byReason[reason] = byReason.GetValueOrDefault(reason) + count;
                    contradicts = true;
                }
            if (contradicts) offending.Add(tick);
        }

        const string name = RefusalVerdict;
        if (offending.Count == 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name,
                $"no tick of {play.Count} refused an order for an unobserved target while its own census admitted usable work; {scene}",
                mode: "production-clock",
                killedBy: "counting refusals without requiring the same tick's census to have admitted usable work, "
                    + "which would make an honest refusal of unreachable work read as this defect; or letting the two refusal "
                    + "literals drift out of their producers, which would make this row match nothing and pass");
            return 0;
        }

        string tally = string.Join(", ", byReason.OrderByDescending(p => p.Value).Select(p => $"{p.Key} x{p.Value}"));
        string priced = offending[^1].HasStep ? "a step" : "no step at all";
        string counted = string.Create(CultureInfo.InvariantCulture,
            $"{offending.Count} of {play.Count} ticks refused an order for a target the same frozen observation had admitted as usable, first at recorded tick {offending[0].Tick} and last at {offending[^1].Tick}");
        string lastTick = string.Create(CultureInfo.InvariantCulture,
            $"on the last such tick the census admitted {offending[^1].UsableAdmitted} usable and the search priced {priced}");
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
            $"{counted}; {tally}; {lastTick}; both reasons resolve to one predicate in source — the target fact's evidence is "
            + $"not Observed — so the census and the binder read one store and disagreed; {scene}",
            mode: "production-clock");
        return 1;
    }

    /// <summary>
    /// Whether a fight README's scenes want ever became work the body did, inside three seconds.
    ///
    /// The measured quantity is the longest unbroken run of ticks on which a fight is wanted and the
    /// course bound no step. A decision spans ticks by design and a course legitimately holds no step
    /// while one is in flight, so a short run is the brain working; a run that outlasts the window
    /// combat is already given to take the body is the companion doing nothing while something it
    /// should have fought was on its way.
    ///
    /// **The denominator was "some domain admitted usable work" until 22 September 2026 and that was
    /// grading against the wrong wish.** Lane C measured the capture: 3,997 of its 4,583 hostile-ticks
    /// are hostiles README tells the companion to decline, at a median 1,665 px, so a row over
    /// admitted work counted work the product says not to do. Under the new denominator the same
    /// capture reads about 138 wanted ticks rather than 1,790, which is the number the verdict should
    /// always have rested on.
    ///
    /// What it still does not hold is named at <c>RunTheWorld.HarmHorizonTicks</c>: a thing slower
    /// than the horizon but "an age to put down", and the receding slime of README line 51 that
    /// becomes worth killing when the *player* stops. Both are under-counts, so this row will pass
    /// on a run that neglected either, and that is stated rather than papered over.
    /// </summary>
    private static int AWantedFightIsBegun(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, string scene)
    {
        int longest = 0, longestFrom = -1, current = 0, currentFrom = -1;
        int wanted = 0, wantedWithStep = 0, inRegion = 0, arriving = 0;
        int nearTheOrbOnly = play.Count(t => !t.AFightIsWanted && t.HostilesArrivingAtCompanion > 0);
        foreach (RunTheWorld.PlayTick tick in play)
        {
            if (!tick.AFightIsWanted) { current = 0; continue; }
            wanted++;
            if (tick.HostilesInRegion > 0) inRegion++;
            if (tick.HostilesArrivingAtPlayer > 0) arriving++;
            if (tick.HasStep) { wantedWithStep++; current = 0; continue; }
            if (current == 0) currentFrom = tick.Tick;
            current++;
            if (current > longest) { longest = current; longestFrom = currentFrom; }
        }

        const string name = StepVerdict;
        string qualified = string.Create(CultureInfo.InvariantCulture,
            $"README wanted a fight on {wanted} of {play.Count} ticks — {inRegion} with a hostile inside the player's own intent region, {arriving} with one the threat sense forecast reaching the player within ten seconds — and a step was bound on {wantedWithStep} of them. ");
        string excluded = string.Create(CultureInfo.InvariantCulture,
            $"A further {nearTheOrbOnly} ticks had a hostile within ten seconds of the companion and of nothing else, and are deliberately not counted: ");
        string denominator = qualified + excluded
            + "that forecast is distance over observed speed, so it is mostly a fact about where the orb flew, and a denominator the companion can enlarge by wandering at hostiles is one it can also pass";
        string detail = longest == 0
            ? "every wanted-fight tick carried a bound step"
            : string.Create(CultureInfo.InvariantCulture,
                $"the longest stretch with a wanted fight and no step bound is {longest} ticks from recorded tick {longestFrom}");
        string blind = "the denominator misses two things README wants fought and nothing here measures: a thing slower than ten seconds to arrive but an age to put down, "
            + "and the receding hostile of README line 51 that becomes worth killing when the player stops walking and starts working";
        if (longest < StepWithinTicks)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, name,
                $"{detail}, inside the stated {StepWithinTicks}; {denominator}; {blind}; {scene}",
                mode: "production-clock",
                killedBy: "counting every tick the census admitted usable work, which on this capture is 1,790 rather than 138 and is mostly hostiles README says to decline; "
                    + "or counting only the published-course reason and not the ticks a decision spans, which would hide a brain that decides forever");
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, name,
            $"{detail}, past the stated {StepWithinTicks}; {denominator}; {blind}; {scene}",
            mode: "production-clock");
        return 1;
    }

    /// <summary>
    /// The numbers beside the verdicts: what the course did, what it refused and what it cost.
    ///
    /// None of them is graded, and the reason is the ledger's own: a measure carries its number and
    /// its direction and the scoreboard compares it against the last clean ancestor, where a
    /// threshold written here would turn "ever green" into "green now" and could not tell a flake
    /// from a regression.
    ///
    /// **Every one of them is a sample rather than a value**, and each says so in a tag, because
    /// this run keeps the game's own wall clock and therefore does not repeat itself: measured over
    /// five whole-capture runs at one commit, the shares move three to four points and the
    /// second-generation collection count ran 12, 13 and 38. The tag is the ledger's own
    /// <c>EmitLedgerRows.SampledTag</c> and not a spelling of this file's, because the scoreboard reads
    /// that constant to print a sampled case under its own heading rather than as drift: a string of
    /// our own here filed fourteen "measures that moved" on every run, which is what the tag was
    /// meant to stop. The tag carries no tolerance, by the ledger guide's ruling; the only bound on a
    /// sample is still the noise band from three or more repeat runs at the baseline commit.
    /// </summary>
    private const string SampleTag = EmitLedgerRows.SampledTag;

    private static void Measures(string suite, IReadOnlyList<RunTheWorld.PlayTick> play, ReadRecordedRoute.Route route,
        StageRecordedActors stage, ReadRecordedActors.Cast cast, string scene)
    {
        string[] sampled = { SampleTag };
        void Share(string name, int numerator, int denominator, string? direction, string message)
            => EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, name,
                denominator <= 0 ? 0 : 100.0 * numerator / denominator, "%", direction, "production-clock", sampled,
                message: $"{numerator} of {denominator}; {message}; {scene}");

        int published = play.Count(t => t.Reason == "published-course-holds-no-step");
        Share("share of ticks whose published course holds no step", published, play.Count, "down",
            "the course settled and bound nothing; a decision still in flight is not counted, because a course legitimately holds no step while one runs");

        int admittedAndStepless = play.Count(t => t.UsableAdmitted > 0 && !t.HasStep);
        int admitted = play.Count(t => t.UsableAdmitted > 0);
        Share("share of ticks with usable work admitted and no step bound", admittedAndStepless, admitted, "down",
            "the denominator is the ticks on which some domain admitted usable work at all, so a run through an empty world cannot flatter this");

        long refused = play.Sum(t => (long)t.Refused);
        long orders = refused + play.Count(t => t.HasStep);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "orders refused per tick", play.Count == 0 ? 0 : (double)refused / play.Count,
            "orders", "down", "production-clock", sampled,
            message: $"{refused} refusals over {play.Count} ticks against {orders} orders that reached pricing or binding; {scene}");

        foreach (var reason in play.SelectMany(t => t.Refusals).GroupBy(p => p.Key).OrderByDescending(g => g.Sum(p => p.Value)).Take(6))
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, $"orders refused for {reason.Key}", reason.Sum(p => p.Value),
                "orders", "down", "production-clock", sampled, message: scene);

        double[] decide = play.Select(t => t.DecideMs).OrderBy(v => v).ToArray();
        double[] brain = play.Select(t => t.BrainMs).OrderBy(v => v).ToArray();
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "decide cost p50", Percentile(decide, 0.50), "ms", "down", "production-clock", sampled,
            message: "the course search's own phase under the game's own allowances, which is the regime a player met; not comparable to a figure taken with the allowances lifted; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "decide cost p99", Percentile(decide, 0.99), "ms", "down", "production-clock", sampled,
            message: "as above; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "whole-brain cost p50", Percentile(brain, 0.50), "ms", "down", "production-clock", sampled,
            message: "against a 16.67 ms frame; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "whole-brain cost p99", Percentile(brain, 0.99), "ms", "down", "production-clock", sampled,
            message: "against a 16.67 ms frame; " + scene);

        int collections = play.Count == 0 ? 0 : play[^1].Gen2Collections - play[0].Gen2Collections;
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "second-generation collections over the run", collections, "collections", "down", "production-clock", sampled,
            message: "counted for the whole process, so it includes the harness's own allocation as well as the brain's, "
                + "and it is the mechanism the capture named for its worst frames rather than a figure attributable to one component; "
                + "the widest sample of any row here — 12, 13 and 38 over runs of one commit; " + scene);

        // The silence, twice corrected. It stopped being a verdict on 22 September 2026 because its
        // predicate was `fired > 0` over the whole post-kill window, so it read green at one fired
        // tick in five hundred as readily as at fifty. Its *denominator* was wrong the same day for
        // the same reason the step verdict's was: "a hostile still standing" counts the 1,665-px
        // median hostile README tells the companion to decline, so a zero here was never the symptom
        // the row is named for. It is qualified now, and where the window holds no wanted fight the
        // row says that rather than printing a share of something nobody wanted.
        const string silence = "share of ticks after the last recorded kill that fired while README wanted a fight";
        var afterTheKill = play.Where(t => t.Tick > cast.LastCompanionKillTick).ToList();
        var wantedAfterTheKill = afterTheKill.Where(t => t.AFightIsWanted).ToList();
        if (cast.LastCompanionKillTick < 0 || afterTheKill.Count == 0)
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, silence,
                $"{Path.GetFileName(cast.EventsPath)} credits the companion no kill this run replayed past, so there is no moment for the hands to have fallen silent after");
        else if (wantedAfterTheKill.Count == 0)
        {
            string window = string.Create(CultureInfo.InvariantCulture,
                $"none of the {afterTheKill.Count} ticks after the recorded last kill at {cast.LastCompanionKillTick} holds a fight README wants — no hostile inside the player's region and none forecast to reach him within ten seconds");
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, silence,
                $"{window}, so a share of them would be a share of a silence nobody asked to be broken. "
                + "This is what the capture itself reads: its 524-tick tail is hostiles the 2:00 scene declines");
        }
        else
            Share(silence, wantedAfterTheKill.Count(t => t.Fired), wantedAfterTheKill.Count, "up",
                "the denominator is the ticks after the recording's last companion kill on which README wants a fight, rather than every tick with a hostile standing, "
                + "because the capture's own tail is hostiles the 2:00 scene tells the companion to decline");

        // Where in the horizon the capture actually sits, and it is here because the horizon is a
        // hedge rather than a measured line. Measured 22 September 2026 on this capture: at a
        // ten-second horizon README wants a fight on 757 of 2,340 ticks, and at five seconds — README
        // line 41's own figure for the slow heavy thing — on **zero**. That is a cliff rather than a
        // gradient, so every wanted tick here rests on the second half of the horizon, and a reader
        // who does not know that would read the verdict as being about hostiles near the player. This
        // row is what makes the cliff visible in the ledger rather than only in a folder guide.
        double[] soonest = play.Select(t => (double)t.SoonestArrivalTicks).Where(double.IsFinite).OrderBy(v => v).ToArray();
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "soonest forecast arrival at the player, p50",
            soonest.Length == 0 ? -1 : Percentile(soonest, 0.50), "ticks", "down", "production-clock", sampled,
            message: $"over the {soonest.Length} ticks on which the threat sense believed any hostile could reach him at all, out of {play.Count}; "
                + "the ten-second horizon the verdict uses is 600 of these, and this says how much of the capture sits near that line rather than well inside it; " + scene);

        // The residual, and the only row here that says what is still wrong rather than what is not.
        // Lane C's reading of the capture: of the ticks README wants a fight, combat is already
        // retained on about two-thirds and the decision is merely unsettled on the rest, and on none
        // of them is a wanted fight priced and beaten by something else. That last clause is why this
        // is a measure and not a verdict — an unsettled decision on a tick a fight is wanted is the
        // brain still deciding, which is legal, and only its size says whether it is a problem.
        int wantedTicks = play.Count(t => t.AFightIsWanted);
        if (wantedTicks > 0)
            Share("share of wanted-fight ticks the companion was not fighting on", play.Count(t => t.AFightIsWanted && !t.Fighting), wantedTicks, "down",
                "the honest residual: README wants a fight and the stance does not have the body. A decision in flight is counted here, because from outside "
                + "a brain still deciding and a brain that declined look the same, and the size of this is what says which it was");

        TheSceneAgainstTheRecording(suite, play, route, stage, cast, scene, sampled);

        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "recorded drops this schema could not name", cast.Shortfall, "drops", "down", "production-clock", sampled,
            message: "the loot column counted more drops at once than the events sidecar names anywhere, so the staged scene is poorer than the play's by this many; " + scene);
    }

    /// <summary>
    /// How faithful the scene the verdicts were graded on actually was.
    ///
    /// Every verdict above is conditional on the staging reproducing the recording, and until
    /// 22 September 2026 no row said whether it had — while the datum sat parsed and unread on every
    /// tick, because <see cref="ReadRecordedRoute.Step.Threats"/> was being read out of the capture
    /// and used nowhere. These are that calibration.
    ///
    /// The comparison is deliberately one-sided. The recording's <c>threats</c> column is the threat
    /// sense's own count of hostiles it considered, not a count of NPCs, so a run with *more* actors
    /// alive than that is the ordinary case — the fireflies and the bunny are staged and were never
    /// threats. What means something is the other direction: a tick on which fewer actors stand here
    /// than the recording counted threats is a tick whose scene is poorer than the play's, and a
    /// verdict taken over a run of those is a verdict about a quieter world.
    /// </summary>
    private static void TheSceneAgainstTheRecording(string suite, IReadOnlyList<RunTheWorld.PlayTick> play,
        ReadRecordedRoute.Route route, StageRecordedActors stage, ReadRecordedActors.Cast cast, string scene, string[] sampled)
    {
        int comparable = 0, short_ = 0, worst = 0;
        for (int i = 0; i < play.Count && i < route.Count; i++)
        {
            if (route[i].Threats < 0) continue;      // a schema that never wrote the column
            comparable++;
            int gap = route[i].Threats - play[i].HostilesAlive;
            if (gap <= 0) continue;
            short_++;
            worst = Math.Max(worst, gap);
        }

        if (comparable == 0)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, "share of ticks with fewer actors alive than the recording counted threats",
                $"{route.Capture} carries no threats column, so how faithful this scene was cannot be said and every verdict above rests on a staging nothing measured");
            return;
        }

        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "share of ticks with fewer actors alive than the recording counted threats",
            100.0 * short_ / comparable, "%", "down", "production-clock", sampled,
            message: $"{short_} of {comparable}; the recording's threat sense counted more hostiles than this run had standing; the reverse is expected and not counted, "
                + "because the staged cast includes critters the threat sense never counted; " + scene);
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "largest shortfall against the recording's threat count",
            worst, "hostiles", "down", "production-clock", sampled,
            message: $"the worst single tick; {stage.PlacedHostiles} of {cast.Hostiles.Count} recorded NPCs were placed at all; " + scene);
    }

    /// <summary>The nearest-rank percentile of an already-sorted sample, which is what every other cost row here uses.</summary>
    private static double Percentile(double[] sorted, double fraction)
        => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
}
