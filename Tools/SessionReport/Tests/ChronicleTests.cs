#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// A deterministic, dependency-free test entry point. SessionReport has no test framework by
/// design, so this uses a temporary TSV through the real parser and report rather than testing a
/// parallel model of the format.
/// </summary>
public static class ChronicleTests
{
    public static int Run()
    {
        try
        {
            ObservedTransitionsStayChronological();
            OldRecordsDeclareChronologyMissing();
            NonMonotonicWallTimeIsRejected();
            AHoldIsNotCalledHesitationAndProgressSurvivesIt();
            SustainedRequestedMovementWithoutObservedProgressIsReported();
            FollowingDiagnosisUsesNavigatorProgressRatherThanDistance();
            EmptyHeaderOnlySessionIsReadable();
            RecorderChronologyContractUsesActualLifeColumn();
            RecorderCapturesFreshNavigationEvidence();
            RecorderLifecycleAndReservationContractsArePresent();
            EventSiblingReportsCountsAndCorruption();
            MultiRunAndHtmlKeepEverySelectedRun();
            MultiRunFolderKeepsFirstAndLastRuns();
            MultiRunRetainsDefinitiveExit();
            DecisionContractsDistinguishStallsFromProgress();
            AFrozenBodyWithARouteAheadIsReportedAndARestingOneIsNot();
            CombatHoldingUnobservableTargetsIsFoundAndOldCapturesSkip();
            DowningDoesNotProveAvoidability();
            ASelectedActivityMustHaveCarriedAnEligibleOffer();
            ASelectionChangesOnlyWithANewComparison();
            FindingsFoldToOneLinePerClassCarryingItsCount();
            TheCourseErasChecksReplaceTheChoosersAtTheirSchema();
            TheRetiredChooserColumnsDeclineByNameAtTheirSchema();
            AttemptEvidenceJoinsByIdentityAndDisagreementsAreDefinitive();
            ACompletedTransferClaimNeedsItsReceivedQuantity();
            AClaimedArrivalMustLieInsideItsSuccessRegion();
            ControlGrantRulesJudgeTheRequestedOwner();
            IdentityChecksSkipOldAndPartialCapturesByName();
            MultiRunStatesProvenanceBeforeAnyRun();
            ACaptureStatesItsSourceAndWhetherItClosed();
            RecordingStatesItsCostWhatItDroppedAndWhatItKeeps();
            ADamagedCaptureReducesCoverageAndInventsNoContradiction();
            IdentityRulesStillMatchTheProducer();
            TravelIsReadPerJourneyAndSkippedByNameOnAnOlderCapture();
            StillnessAndMotionPairsBreakAtAGapAndAtDeath();
            ThePlayersReferenceChecksFindWhatTheyClaim();
            TheFunnelSectionTalliesEachActivity();
            WeaponKnowledgeCalibrationIsGraded();
            CombatAuditSidecarIsReadBack();
            MissingCombatAuditReadsAsUnmeasured();
            CorruptCombatAuditReadsAsUnreadable();
            ForeignCombatAuditIsNamed();
            EagernessFiresWhenDangerStandsUnfought();
            NotFightingFiresBesideATargetInRange();
            CommittedPlanWasPerformedFiresWhenTheStandIsNeverReached();
            CombatFlickerFiresOnANewPlanEveryTick();
            CourseSnapshotRequiresActualValuesAndMatchingDigests();
            CourseReaderRejectsMixedAndDigestOnlySnapshots();
            CourseDecisionsAreReadCheckedAndNarrated();
            ACensusAdmissionMustSurviveItsOwnBinder();
            EveryEffectMustBeTheAcceptedStep();
            ASyntheticCaptureIsNotReadAsPlay();
            TheCourseTimelineIsOneRowPerDecisionAndFoldsWhatRepeats();
            TheBehaviourParityTableStillNamesRealBehavioursAndRealFixtures();
            TheGuideQuotesTheSchemaConstantItDocuments();
            TheAuditsOwnWiringIsWitnessedByTheCapture();
            TheFrameLedgerSplitsTheUpdateAndSeparatesDrawsFromUpdates();
            TheSectionProfileIsReadAndAnOlderCaptureDeclinesByName();
            ExplainingATickPrintsWhatTheRecordHoldsAndNamesWhatItLacks();
            APngRoundTripsThroughAStandardInflateWithEveryCrcRight();
            ATickPictureDrawsWhatTheRecordPlacedAndLeavesTheRestUnknown();
            // Last, because it writes a chronicle and an events sibling into the temp directory and
            // the multi-run cases above read that directory for runs to join.
            Console.WriteLine($"Chronicle self-tests passed ({Ran.Count} assertion groups).{DeclaredButSilent()}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Chronicle self-test failed: {error.Message}");
            return 1;
        }
    }

    private static void CourseSnapshotRequiresActualValuesAndMatchingDigests()
    {
        var context = new CourseTraceContext(1, "brain", 1, 1, 1, 1, 1, "fixture", 1, 0, "world", "source", "policy", "config");
        string value = "actual-fact";
        var expected = new[] { new CourseManifestEntry("fact", 2, CourseDecisionSnapshotCoverage.Digest(value), "expected") };
        var complete = CourseDecisionSnapshot.Create(context, "model", "scheduler", "random", expected,
            new[] { new CourseManifestEntry("fact", 2, CourseDecisionSnapshotCoverage.Digest(value), "captured", value) });
        Require(CourseDecisionSnapshotCoverage.Assess(complete).ExactInputComplete, "a complete snapshot with an actual hashed value was refused");
        var digestOnly = CourseDecisionSnapshot.Create(context, "model", "scheduler", "random", expected,
            new[] { new CourseManifestEntry("fact", 2, CourseDecisionSnapshotCoverage.Digest(value), "captured") });
        Require(!CourseDecisionSnapshotCoverage.Assess(digestOnly).ExactInputComplete, "a digest-only fact certified exact replay");
    }

    /// <summary>
    /// The three readers of a course decision, driven end to end on a synthetic sidecar: the payload
    /// parser, the contracts check and the narration.
    ///
    /// <para>Each contract is proven by mutation rather than by a clean pass, because a rule that has
    /// never been made to fire and a rule that cannot fire report identically. The clean arm is
    /// asserted too, since a check that fires on everything is no more use than one that fires on
    /// nothing — and the measure is read for its own arithmetic, so a share whose denominator was the
    /// wrong set would show up here rather than in a report about a real play.</para>
    /// </summary>
    /// <summary>
    /// Schema 0.45.0's frame ledger, read end to end: the measure's arithmetic, the check's threshold
    /// and the one distinction the whole column set exists to preserve.
    ///
    /// <para><b>An update is not a frame.</b> The engine runs a fixed timestep and catches up by
    /// running two updates back to back with no draw between them, so a session's frames a second and
    /// its updates a second are two different numbers and only the first is what a player sees. A
    /// reader that derived "frames per second" from the interval would report the catch-up update as a
    /// fast frame and the whole capture as healthier than it was, which is why the producer counts
    /// draws at the draw callback and why the row below sets half its updates to zero draws and
    /// requires the two figures to differ.</para>
    ///
    /// <para>Every other assertion here is a pair, because a check that fires on everything is worth
    /// no more than one that fires on nothing: the threshold is asked of a capture above it and a
    /// capture below it, and the unmeasured first interval is asked for as an exclusion rather than as
    /// a fast frame.</para>
    /// </summary>
    /// <summary>
    /// Schema 0.48.0's section profile, read back by the measure and the report block: a section's share of the
    /// brain, its share on spike ticks against ordinary ones, the allocators, the spike count read from each row's own
    /// fence, a spike's whole tree from the sidecar — and a 0.47.0 capture declined by name rather than read as zeros.
    /// The numbers are built so each expected value is arithmetic a reader can do by hand.
    /// </summary>
    private static void TheSectionProfileIsReadAndAnOlderCaptureDeclinesByName()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aic-section-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 200 brain ticks at 2 ms: 1.5 in the search and 0.3 in the intent sense. Ticks 100 and 150 are spikes of
            // 10 ms, 9 of them in combat's preparation. The fence is `-` for the first ten rows, as the producer writes
            // it while its window fills. Every tick allocates 2,000 bytes, 1,000 of them in the assistance capture.
            // Ten downed ticks follow, as the recorder writes them: no brain ran, the finalise section rolled over, and
            // brain_ms repeats the last brain tick's 10 ms — so counting them would read twelve spikes, not two.
            var trace = new StringBuilder("# schema=0.48.0\n# text_columns=sections,alloc_sections\n"
                + "tick\twall_elapsed_ms\tbrain_ms\tbrain_fresh\trecord_ms\tgc0\tgc1\tgc2\ttick_alloc_bytes\tbrain_alloc_bytes\tsections\tsections_other_ms\tcost_fence_ms\talloc_sections\n");
            for (int tick = 0; tick < 200; tick++)
            {
                bool spike = tick is 100 or 150;
                string sections = spike ? "decide.prepare.combat=9.000/1|decide.course.search=0.500/1|record.row=0.100/1"
                    : "decide.course.search=1.500/1|senses.intent=0.300/1|record.row=0.100/1";
                trace.Append(FormattableString.Invariant(
                    $"{tick}\t{tick * 1000.0 / 60.0:0.00}\t{(spike ? 10.0 : 2.0):0.00}\t1\t0.50\t{(tick % 10 == 0 ? 1 : 0)}\t0\t0\t2100\t2000\t{sections}\t0.100\t{(tick < 10 ? "-" : "5.000")}\tdecide.course.snapshot.assistance=1000|senses.intent=10\n"));
            }
            for (int tick = 200; tick < 210; tick++)
                trace.Append(FormattableString.Invariant(
                    $"{tick}\t{tick * 1000.0 / 60.0:0.00}\t10.00\t0\t0.50\t0\t0\t0\t2100\t-\tfinalise=0.050/1\t0.000\t-\t-\n"));
            trace.Append("# closing=world-unload;rows=210;cost-spikes=2;cost-spike-dumps=1;profiler-overflowed=3;profiler-unbalanced=1\n");
            string current = Path.Combine(directory, "2026-09-24_00-00-00-000.tsv");
            File.WriteAllText(current, trace.ToString());
            File.WriteAllText(ReadGodsEyeEvents.PathFor(current),
                "{\"v\":1,\"seq\":1,\"tick\":101,\"wall_elapsed_ms\":1683.3,\"kind\":\"cost-spike\",\"subject\":1,\"related\":\"\",\"label\":\"10.000\",\"channel\":\"60\",\"pos_x\":0,\"pos_y\":0,\"vel_x\":0,\"vel_y\":0,\"expected_x\":0,\"expected_y\":0,\"amount\":1,"
                + "\"detail\":\"spikes-in-window=1;window-ticks=60;tick=100;brain-ms=10.000;fence-ms=5.000;q1-ms=1.900;median-ms=2.000;q3-ms=2.100;tree=decide=9.800/0.100/1/4000|decide.prepare=9.200/0.200/1/3000|decide.prepare.combat=9.000/9.000/1/3000|decide.course=0.500/0.000/1/1000|decide.course.search=0.500/0.500/1/1000\"}\n");
            Session session = Session.Load(current);

            var measure = new MeasureWhereTheTimeGoes();
            Require(measure.Needs.All(name => session.Has(name)) && measure.Missing(session) == null, "a 0.48.0 capture carrying the profile was declined: " + measure.Missing(session));
            var rows = measure.Rows(session).ToDictionary(row => row.Case, row => row);
            double Value(string name) => rows.TryGetValue(name, out var row) ? row.Value ?? double.NaN : double.NaN;
            Require(Value("time/spikes") == 2, $"two ticks above their own fence should read 2 spikes, and read {Value("time/spikes")}");
            double brainTotal = 198 * 2.0 + 2 * 10.0;
            Require(Math.Abs(Value("time/share/decide.course.search") - 100.0 * (198 * 1.5 + 2 * 0.5) / brainTotal) < 1e-9,
                $"the search's share of the brain is its summed self time over the summed brain time; read {Value("time/share/decide.course.search")}");
            Require(Math.Abs(Value("time/spike-share/decide.prepare.combat") - 90.0) < 1e-9 && Value("time/ordinary-share/decide.prepare.combat") == 0,
                $"combat held 18 of the 20 spike-tick milliseconds and none of the ordinary ones; read {Value("time/spike-share/decide.prepare.combat")} against {Value("time/ordinary-share/decide.prepare.combat")}");
            Require(Math.Abs(Value("time/alloc-share/decide.course.snapshot.assistance") - 50.0) < 1e-9,
                $"1,000 of every 2,000 bytes is half the brain's allocation; read {Value("time/alloc-share/decide.course.snapshot.assistance")}");
            Require(!rows.Keys.Any(name => name.Contains("record.row", StringComparison.Ordinal)),
                "the recorder's own subtree was shared against the brain's time, where it belongs against record_ms");
            Require(rows.Values.All(row => row.Tags?.Contains(AICompanion.Tools.Ledger.EmitLedgerRows.TimedTag) == true
                    && row.Tags.Contains(AICompanion.Tools.Ledger.EmitLedgerRows.SampledTag)),
                "a row of the section profile was filed without the timed and sampled tags");

            string block = DescribeWhereTheTimeGoes.Of(session, current);
            Require(block.Contains("tick 100  10.00 ms against a 5.00 ms fence", StringComparison.Ordinal)
                    && block.Contains("decide.prepare.combat 9.00", StringComparison.Ordinal)
                    && block.Contains("90.0% against   0.0%  decide.prepare.combat", StringComparison.Ordinal),
                "the report block did not name the spike from its sidecar tree or set its dominant section against the ordinary ticks: " + block);
            Require(block.Contains("where the time goes  200 brain tick(s)", StringComparison.Ordinal),
                "the report block counted the downed ticks as brain ticks: " + block);
            Require(block.Contains("3 section(s) past the profiler's capacity", StringComparison.Ordinal)
                    && block.Contains("1 scope(s) left open", StringComparison.Ordinal),
                "the report block did not print the closing line's profiler overflow and imbalance counts: " + block);

            // A 0.47.0 capture has none of the columns: it is declined by the schema that first writes them, by name.
            string older = Path.Combine(directory, "2026-09-23_00-00-00-000.tsv");
            File.WriteAllText(older, "# schema=0.47.0\ntick\twall_elapsed_ms\tbrain_ms\tbrain_fresh\trecord_ms\tgc0\tgc1\tgc2\n1\t16\t2.00\t1\t0.50\t0\t0\t0\n");
            Session old = Session.Load(older);
            Require(measure.Missing(old) is { } why && why.Contains("written from schema 0.48.0", StringComparison.Ordinal) && why.Contains("0.47.0", StringComparison.Ordinal),
                "a 0.47.0 capture was not declined by the schema that first writes the profile: " + measure.Missing(old));
            Require(DescribeWhereTheTimeGoes.Of(old, older).Contains("where the time goes  unrecorded", StringComparison.Ordinal),
                "the report block read a 0.47.0 capture instead of saying the profile is unrecorded");

            // The producer pin: the names read here are the ones the recorder writes, by literal, in the recorder's
            // own files — which this project does not compile.
            string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
            string events = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordGodsEyeEvents.cs"));
            Require(telemetry.Contains("\\ttick_alloc_bytes\\tbrain_alloc_bytes\\tsections\\tsections_other_ms\\tcost_fence_ms\\talloc_sections", StringComparison.Ordinal)
                    && telemetry.Contains("brain-ms=", StringComparison.Ordinal) && telemetry.Contains("fence-ms=", StringComparison.Ordinal)
                    && telemetry.Contains(";tree=", StringComparison.Ordinal) && events.Contains("\"cost-spike\"", StringComparison.Ordinal),
                "the recorder no longer writes the section profile's columns, or the cost-spike occurrence's fields, under the names this reader reads");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void TheFrameLedgerSplitsTheUpdateAndSeparatesDrawsFromUpdates()
    {
        string file = Path.GetTempFileName();
        try
        {
            // One row per update. `frame` of -1 is the producer's "no previous update to measure from".
            Session Capture(Func<int, (double Frame, int Draws)> shape, int rows = 400)
            {
                var trace = new StringBuilder("# schema=0.45.0\n"
                    + "tick\twall_elapsed_ms\tframe_ms\tdraws\toverlay_ms\tinspector_ms\tengine_ms\tbrain_ms\trecord_ms\n");
                double wall = 0;
                for (int tick = 0; tick < rows; tick++)
                {
                    (double frame, int draws) = shape(tick);
                    wall += frame < 0 ? 0 : frame;
                    trace.Append(FormattableString.Invariant(
                        $"{tick}\t{wall:0.00}\t{frame:0.00}\t{draws}\t0.40\t1.10\t{Math.Max(0d, frame - 11.5):0.00}\t9.00\t1.00\n"));
                }
                File.WriteAllText(file, trace.ToString());
                return Session.Load(file);
            }

            // Above the threshold: every interval overruns, so the check fires once, as Potential, with
            // the split in its detail rather than only the share.
            Finding[] slow = new TheFrameFitsTheEnginesTimestep().Run(Capture(_ => (25.0, 1))).ToArray();
            Require(slow.Length == 1 && slow[0].Severity == Severity.Potential,
                "a capture whose every interval overruns must report exactly one potential finding");
            Require(slow[0].Detail.Contains("brain 9.00 ms a frame", StringComparison.Ordinal)
                    && slow[0].Detail.Contains("inspector 1.10 ms a frame", StringComparison.Ordinal)
                    && slow[0].Detail.Contains("engine ", StringComparison.Ordinal),
                $"the finding must carry the split beside the share, not the share alone: {slow[0].Detail}");
            Require(slow[0].Detail.Contains("25%", StringComparison.Ordinal),
                "the finding must state the threshold it was judged against so it can be argued with");

            // Below it: a capture comfortably inside the timestep says nothing at all.
            Require(new TheFrameFitsTheEnginesTimestep().Run(Capture(_ => (10.0, 1))).ToArray().Length == 0,
                "a capture inside the engine's own timestep must produce no finding");

            // The unmeasured first interval is excluded, not counted as the fastest frame in the file.
            // With every other row at 25 ms the share is 100%, and a -1 counted as a frame would be 99.7%.
            Finding[] withUnmeasured = new TheFrameFitsTheEnginesTimestep()
                .Run(Capture(tick => (tick == 0 ? -1.0 : 25.0, 1))).ToArray();
            Require(withUnmeasured.Length == 1 && withUnmeasured[0].Title.Contains("100.0%", StringComparison.Ordinal),
                $"an unmeasured interval must be excluded rather than counted as a fast frame: {(withUnmeasured.Length == 0 ? "no finding" : withUnmeasured[0].Title)}");

            // The measure, and the distinction the columns exist for. Half the updates carry no draw,
            // which is the engine catching up, so frames a second must come out below updates a second.
            Session mixed = Capture(tick => (10.0, tick % 2 == 0 ? 1 : 0));
            var rows = new MeasureTheFrame().Rows(mixed).ToList();
            double Value(string name) => rows.Single(r => r.Case == "frame/" + name).Value ?? -1;
            Require(Math.Abs(Value("updates-per-second") - 2.0 * Value("frames-per-second")) < 0.5,
                $"half the updates carrying no draw must halve frames a second against updates a second;"
                + $" updates={Value("updates-per-second"):0.0} frames={Value("frames-per-second"):0.0}");
            Require(Value("overrun-share") == 0, $"a 10 ms interval is inside the budget; overrun-share={Value("overrun-share")}");
            Require(Math.Abs(Value("median-ms") - 10.0) < 0.01, $"the median interval must be the one written; median={Value("median-ms")}");
            // 9.00 brain of a 10.00 interval is 90%, and the shares are taken over the summed interval
            // rather than as a mean of per-row shares, which would weight a catch-up update like a hitch.
            Require(Math.Abs(Value("brain-share") - 90.0) < 0.5, $"brain-share={Value("brain-share")}");
            Require(Math.Abs(Value("inspector-share") - 11.0) < 0.5, $"inspector-share={Value("inspector-share")}");

            // **The brain column is read one row back, and a fixture with a constant brain cost cannot
            // tell.** `frame_ms` is anchored at PostUpdateEverything, so a row written inside update N
            // carries the interval that closed at the end of update N−1 and the brain cost inside it is
            // the *previous* row's — which is exactly what `FrameCost.RemainderMilliseconds` is handed
            // when the producer computes `engine_ms`. Every fixture above holds brain_ms at 9.00, so
            // both phases give the same share and the reader summed this row's brain against the
            // previous update's interval for as long as it existed. This one alternates 20 and 2 with a
            // 25 ms interval, over three rows whose first is unmeasured:
            //
            //   row 0   frame -1   brain 20      excluded: no interval
            //   row 1   frame 25   brain  2      its interval covers update 0, whose brain was 20
            //   row 2   frame 25   brain  2      its interval covers update 1, whose brain was  2
            //
            // The right phase sums 22 over a 50 ms interval and reads 44%; the row's own brain sums 4
            // and reads 8%. Nothing else in the file separates those two numbers.
            var phased = new StringBuilder("# schema=0.45.0\n"
                + "tick\twall_elapsed_ms\tframe_ms\tdraws\toverlay_ms\tinspector_ms\tengine_ms\tbrain_ms\trecord_ms\n"
                + "0\t0.00\t-1.00\t1\t0.00\t0.00\t0.00\t20.00\t0.00\n"
                + "1\t25.00\t25.00\t1\t0.00\t0.00\t5.00\t2.00\t0.00\n"
                + "2\t50.00\t25.00\t1\t0.00\t0.00\t23.00\t2.00\t0.00\n");
            File.WriteAllText(file, phased.ToString());
            var phasedRows = new MeasureTheFrame().Rows(Session.Load(file)).ToList();
            double Phased(string name) => phasedRows.Single(r => r.Case == "frame/" + name).Value ?? -1;
            Require(Math.Abs(Phased("brain-share") - 44.0) < 0.01,
                $"the brain share must be taken from the row before each interval, which is 44% here; reading the row's own gives 8%. brain-share={Phased("brain-share"):0.00}");
            // The same phase in the check's own split, which is a second copy of the arithmetic.
            Finding[] phasedFinding = new TheFrameFitsTheEnginesTimestep().Run(Session.Load(file)).ToArray();
            Require(phasedFinding.Length == 1 && phasedFinding[0].Detail.Contains("brain 11.00 ms a frame (44.0%)", StringComparison.Ordinal),
                $"the check's split must read the same phase as the measure: {(phasedFinding.Length == 0 ? "no finding" : phasedFinding[0].Detail)}");

            // **The predecessor is the previous update, not the previous line of the file.** The two
            // are the same until a row is dropped, and then the shift reads a brain cost belonging to
            // an update two or more ticks back while the arithmetic still looks fine. The fixture above
            // with its middle row removed is the whole test: ticks 0 and 2 survive, row 2's interval
            // covers update 1 whose cost is not in the file, and attributing row 0's brain of 20 to it
            // would read 80% of 25 ms. With the gap excluded there is no attributable pair at all, so
            // the share is 0 over an empty set rather than a confident wrong number.
            File.WriteAllText(file, "# schema=0.45.0\n"
                + "tick\twall_elapsed_ms\tframe_ms\tdraws\toverlay_ms\tinspector_ms\tengine_ms\tbrain_ms\trecord_ms\n"
                + "0\t0.00\t-1.00\t1\t0.00\t0.00\t0.00\t20.00\t0.00\n"
                + "2\t25.00\t25.00\t1\t0.00\t0.00\t5.00\t2.00\t0.00\n");
            var gapped = new MeasureTheFrame().Rows(Session.Load(file)).ToList();
            double Gapped(string name) => gapped.Single(r => r.Case == "frame/" + name).Value ?? -1;
            Require(Gapped("brain-share") == 0,
                "a row whose own predecessor update is missing from the file was still attributed a brain cost — the shift reads the "
                + $"previous *line*, which across a dropped tick belongs to another update entirely. brain-share={Gapped("brain-share"):0.00}");
            string gappedNote = gapped.Single(r => r.Case == "frame/brain-share").Message;
            Require(gappedNote.Contains("0 of 1 measured row(s)", StringComparison.Ordinal),
                "the share's own sentence must say how many pairs the gap cost, or a zero share reads as a brain that cost nothing: "
                + gappedNote);

            // The producer pin. The five columns are written by a file this project does not compile,
            // so a rename there leaves every row above passing against a capture nobody writes.
            string recorder = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
            Require(recorder.Contains("\\tframe_ms\\tdraws\\toverlay_ms\\tinspector_ms\\tengine_ms", StringComparison.Ordinal),
                "the recorder no longer writes the frame ledger's five columns in the order this reader names them");
            string ledger = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "MeasureFrameCost.cs"));
            Require(ledger.Contains("FrameMilliseconds = 1000d / 60d", StringComparison.Ordinal),
                "the producer's own frame budget moved away from the engine's timestep, so this reader's share means something else");
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// The two wirings of the decision audit that fail silently in play, read back off a capture's own
    /// closing line.
    ///
    /// <para>Neither can be caught by a headless row, because headlessly the source is installed by the
    /// fixture and the hook is driven directly. What a capture can say is how many decisions reached
    /// the audit and how many of those read a frozen observation, and the two failures are exactly the
    /// two zeroes: decisions recorded with nothing audited is the hook gone from
    /// <c>RecordCourseTrace.Record</c>, and everything audited with nothing read is
    /// <c>ReadLiveCourseForAudit.Install</c> never having run. The third case here is the one the check
    /// is worth nothing without — a wired session says nothing at all.</para>
    /// </summary>
    /// <summary>
    /// The Diagnostics guide's "the capture schema is X" sentence against the constant it describes.
    ///
    /// <para>That sentence went stale across a version bump and a reviewer found it, which is the same
    /// failure this whole folder is careful about in the other direction: a number in prose that no
    /// instrument checks is a claim, and every version bump is an invitation for it to drift. The
    /// guide's own text says the constant is the only authority; this makes that enforceable rather
    /// than advisory, so the next bump reddens here instead of shipping a lie in the file a stranger
    /// reads first.</para>
    /// </summary>
    private static void TheGuideQuotesTheSchemaConstantItDocuments()
    {
        string recorder = Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs");
        string guide = Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "CLAUDE.md");
        var constant = Regex.Match(File.ReadAllText(recorder), @"Schema\s*=\s*""(?<v>\d+\.\d+\.\d+)""");
        Require(constant.Success, $"no Schema constant found in {recorder}, so the guide has nothing to be checked against");
        var quoted = Regex.Match(File.ReadAllText(guide), @"The capture schema is \*\*(?<v>\d+\.\d+\.\d+)\*\*");
        Require(quoted.Success, $"{guide} no longer states the capture schema in the shape this row reads");
        Require(quoted.Groups["v"].Value == constant.Groups["v"].Value,
            $"the guide says the capture schema is {quoted.Groups["v"].Value} and the recorder's constant is"
                + $" {constant.Groups["v"].Value}; the constant is the authority and the sentence is wrong");

        // **A guide may not quote this tool's coverage line, because both of its numbers move under it
        // and neither moves for a reason that guide is about.** The denominator changes with every
        // check the tool gains and the numerator with every schema a capture predates, so the sentence
        // is stale by the next lane and reads as authority while it lies. Two were, and neither was
        // caught by reading: `Tools/SessionReport/CLAUDE.md` said a capture printed "40 of 45 checks
        // ran" where the same change's own reader printed 41 of 46, and said another printed "32 of 32"
        // where it prints 35 of 46 — the second wrong for long enough that nobody knows when. The
        // figure is printed at the top of every run, so the guide points at the run.
        foreach (string file in Directory.EnumerateFiles(Path.Combine("Tools", "SessionReport"), "CLAUDE.md", SearchOption.AllDirectories))
        {
            var coverage = Regex.Match(File.ReadAllText(file), @"\d+ of \d+ checks? ran");
            Require(!coverage.Success,
                $"{file} quotes this tool's own coverage line as '{coverage.Value}'. Both of its numbers move — the denominator with "
                + "every check added, the numerator with every schema a capture predates — so a quoted figure is a claim that goes stale "
                + "silently. Describe which checks skip and why, and let the reader print the count.");
        }
    }

    private static void TheAuditsOwnWiringIsWitnessedByTheCapture()
    {
        object Field(string kind, string text) => new { Kind = kind, Text = text };
        string Decision(int seq, long tick) => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d,
            kind = "course-course-decision", subject = 0, related = "", label = "", channel = "",
            pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f, expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
            payload_kind = ReadCourseDecisions.Kind, payload_version = 1, phase = "brain",
            observation_ordinal = 1L, receipt_watermark = 0L,
            payload = new { Kind = ReadCourseDecisions.Kind, Version = 1, Fields = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["reason"] = Field("text", "course-published"),
                ["activity"] = Field("text", "keep-company"),
                ["settled"] = Field("flag", "true"),
                ["purpose"] = Field("text", ""),
                ["steps"] = Field("integer", "0"),
                ["orders-priced"] = Field("integer", "1"),
                ["orders-refused"] = Field("integer", "0"),
                ["search-exhausted"] = Field("flag", "true"),
                ["release-reason"] = Field("text", ""),
                ["facts"] = Field("integer", "7"),
            } } });
        string Marker(int seq, string kind) => JsonSerializer.Serialize(new { v = 1, seq, tick = 0, wall_elapsed_ms = 0d,
            kind, subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
            expected_x = 0f, expected_y = 0f, amount = 0, detail = "" });

        var check = new TheDecisionAuditRanOnTheDecisionsTheCaptureHolds();
        void Drive(string name, string schema, string closing, bool frameColumn, Action<Session> assert)
        {
            string tsv = Path.GetTempFileName(), events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
            try
            {
                string header = frameColumn ? "tick\tframe_ms\n1\t25.00\n" : "tick\n1\n";
                File.WriteAllText(tsv, $"# schema={schema}\n{header}{closing}");
                File.WriteAllLines(events, new[] { Marker(0, "session"), Decision(1, 1), Decision(2, 2), Marker(3, "session-end") });
                assert(Session.Load(tsv));
            }
            finally { File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
        }

        Drive("hook gone", "0.45.0", "# closing=world-unload;rows=1;decisions-audited=0;audit-observations-read=0\n", true, session =>
        {
            Finding[] findings = check.Run(session).ToArray();
            Require(check.Missing(session) == null, $"a 0.45.0 capture with both counts must be gradeable; {check.Missing(session)}");
            Require(findings.Length == 1 && findings[0].Severity == Severity.Definitive
                    && findings[0].Detail.Contains("RecordCourseTrace.Record", StringComparison.Ordinal),
                $"two recorded decisions and nothing audited must name the hook; got {findings.Length} finding(s)");
        });

        Drive("source never installed", "0.45.0", "# closing=world-unload;rows=1;decisions-audited=2;audit-observations-read=0\n", true, session =>
        {
            Finding[] findings = check.Run(session).ToArray();
            Require(findings.Length == 1 && findings[0].Detail.Contains("ReadLiveCourseForAudit.Install", StringComparison.Ordinal),
                $"everything audited and nothing read must name the installer rather than the hook; got {findings.Length} finding(s)");
        });

        Drive("wired", "0.45.0", "# closing=world-unload;rows=1;decisions-audited=2;audit-observations-read=2\n", true, session =>
            Require(check.Run(session).ToArray().Length == 0, "a wired session must produce no finding at all"));

        // An older capture's trailer carries neither count, and reading a missing count as zero would
        // report every capture written before 0.45.0 as an audit that never ran. It skips by name.
        Drive("older capture", "0.44.0", "# closing=world-unload;rows=1\n", false, session =>
        {
            Require(check.Missing(session) != null,
                "a capture whose recorder never wrote the counts must skip by name rather than read their absence as zero");
            Require(check.Run(session).ToArray().Length == 0, "a skipped check must still produce nothing when asked");
        });
    }

    /// <summary>
    /// The contradiction six independent readings of the 22 September 2026 capture each found by hand
    /// and no check asked for: a domain the census admitted usable whose every order the same decision
    /// refused for want of an observed target.
    ///
    /// <para>Two things are pinned rather than asserted, because a check whose literals drift reports a
    /// clean run for ever. The two refusal strings are read out of the binders that write them, and the
    /// domain sets are read out of the recorder's own <c>RefusalFor</c>, which is the same mapping in
    /// the other direction — so a rename in either file reddens this before it silences the check.</para>
    ///
    /// <para>The rest of the group is the join, and every arm is a way the join can be wrong: an
    /// admission and a refusal in one record is the observed contradiction and is Definitive; an
    /// admission carried forward to a later decision is the same reading with an inference in it and is
    /// counted apart; a domain admitted with nothing usable is not a contradiction however many orders
    /// were refused; and a refusal reason no binder in the table owns is not attributed to anything.</para>
    /// </summary>
    /// <summary>
    /// A capture nobody played is not read as play.
    ///
    /// <para><c>Tools/WorldRun</c> drives the mod's own recorder, so a world run writes a capture in
    /// exactly the format a playtest writes — every column, every occurrence, a normal closure — and
    /// nothing in the rows tells the two apart. The preamble's <c>synthetic=</c> line is the only thing
    /// that does, and a reader ignoring it reports "0m 38s of play" about a session nobody played and
    /// pins before-numbers against a replay of the very capture they were taken from.</para>
    ///
    /// <para>The producer name is read as written rather than matched against <c>world-run</c>, so a
    /// second harness writing a different producer is refused as play too. The arm below uses one.</para>
    /// </summary>
    /// <summary>
    /// The decision table: one row per decision, folded where consecutive decisions say the same thing,
    /// with the census, the ordered course and the cost joined to each.
    ///
    /// <para>Its two joins are what the arms below exist for, and both were wrong in the first version.
    /// <b>A decision's payload is not on its own `choice_tick`</b>: `choice_id` advances on the tick a
    /// decision is reached and the payload is written when an outcome is traced, which on the 22
    /// September 2026 capture puts 2,340 payloads on 1,364 ticks and the decision reached at tick 5 at
    /// tick 6 — a lookup keyed on the decision tick missed about half of them and printed dashes where
    /// the numbers belong. And <b>a decision that traced no payload is not a difference</b>: treating
    /// that absence as a distinguishing value split the table into a hundred runs, a third of them one
    /// undescribed decision each, which is the per-tick log the page exists to replace.</para>
    /// </summary>
    /// <summary>
    /// The behaviour parity table's two claims about things outside this folder, each pinned against
    /// the file that owns it.
    ///
    /// <para>The table maps every row of the README's Behaviour By Behaviour specification to the
    /// fixture cases that grade it headlessly. Both halves of that mapping can rot without a symptom:
    /// a renamed behaviour row leaves a mapping pointing at nothing, and a renamed fixture case leaves
    /// a row claiming coverage that no instrument provides. Neither would change a single number in a
    /// report, which is exactly the failure a coverage table exists to prevent and would therefore
    /// commit itself.</para>
    ///
    /// <para>The check side needs no pin: the mapping names check <em>types</em>, so renaming a check
    /// class is a compile error here.</para>
    /// </summary>
    private static void TheBehaviourParityTableStillNamesRealBehavioursAndRealFixtures()
    {
        string readme = File.ReadAllText("README.md");
        var specified = WriteBehaviourParity.BehavioursIn(readme);
        Require(specified.Count >= 30,
            $"the README's Behaviour By Behaviour table parsed as {specified.Count} row(s); it held 32 on 22 September 2026, so either the parse broke or the specification shrank");
        foreach (string mapped in WriteBehaviourParity.MappedBehaviours)
            Require(specified.Contains(mapped, StringComparer.Ordinal),
                $"the parity table maps '{mapped}', which the README's specification no longer names — the row was renamed or removed and the mapping points at nothing");

        // Every case name the table claims must be a case some instrument registers. The registration
        // tables are literals in source, so the pin is a substring search across the instruments rather
        // than a run of them: a run would need the suite, and a name that no longer registers is a
        // claim about coverage rather than about a failure.
        //
        // **This tool's own tree is excluded, and leaving it in made the pin useless.** The parity
        // table's literals live under `Tools/SessionReport/Write/`, so a search across all of `Tools/`
        // finds every name in the table's own source and passes whatever the instruments register — a
        // mutation inventing the case "gathering beside the player is cooperative rather than competing
        // xx" was green. A pin that includes the thing being pinned is asserting that a file contains
        // its own contents.
        string reader = "Tools" + Path.DirectorySeparatorChar + "SessionReport" + Path.DirectorySeparatorChar;
        string sources = string.Concat(Directory.EnumerateFiles("Tools", "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains(reader, StringComparison.Ordinal))
            .Select(File.ReadAllText));
        foreach (string fixture in WriteBehaviourParity.NamedFixtures)
            Require(sources.Contains("\"" + fixture + "\"", StringComparison.Ordinal),
                $"the parity table names the fixture case '{fixture}', which no instrument under Tools/ registers any more — the row claims coverage nothing provides");

        // **A check that ran and found nothing is three answers and only one of them is agreement.**
        // Before the row's witness existed this table said the play agreed with Self-preservation on a
        // session holding zero damage events and zero downings, and said the same of Recovering when it
        // cannot follow and Breaking containers — thirteen agreements that were a function of which
        // checks ran rather than of the play, identical on a capture four days older. Each arm below
        // drives the real page over a one-behaviour specification, varying only the witness's own
        // evidence, because a fixture that holds the witness constant cannot tell the three apart.
        string spec = Path.GetTempFileName();
        try
        {
            void Parity(string behaviour, string columns, string rows, string expected, string why)
            {
                File.WriteAllText(spec, $"# Behaviour By Behaviour\n\n| **{behaviour}** — the gloss the parse discards |\n");
                string tsv = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(tsv, $"# schema=0.44.0\ntick\t{columns}\n{rows}");
                    string page = WriteBehaviourParity.Of(Session.Load(tsv), Array.Empty<Finding>(),
                        Array.Empty<(string, string)>(), spec);
                    string row = page.Split('\n').FirstOrDefault(l => l.Contains(behaviour, StringComparison.Ordinal)
                        && l.StartsWith("  ", StringComparison.Ordinal)) ?? "";
                    Require(row.TrimEnd().EndsWith(expected, StringComparison.Ordinal),
                        $"'{behaviour}' read as something other than '{expected}' — {why}: '{row.Trim()}'");
                }
                finally { File.Delete(tsv); }
            }

            Parity("Self-preservation", "npc_hit", "1\t\n2\t\n", "not exercised",
                "its checks ran and found nothing on a session in which the companion was never hit, and a session with nothing to grade "
                + "is not a session the play agreed with");
            Parity("Self-preservation", "npc_hit", "1\t\n2\tzombie\n", "play agrees",
                "its checks ran silent on a session that does hold the behaviour, which is the one case the word `agrees` is earned by");
            // A behaviour whose row declares no witness stays in the third value rather than being
            // promoted, which is what makes declaring one worth doing.
            Parity("Finding a route", "npc_hit", "1\t\n2\t\n", "silent",
                "no witness is declared for it, so nothing in the capture says whether the behaviour occurred and the row must not claim "
                + "either that it did or that it did not");
        }
        finally { File.Delete(spec); }
    }

    private static void TheCourseTimelineIsOneRowPerDecisionAndFoldsWhatRepeats()
    {
        object Field(string kind, string text) => new { Kind = kind, Text = text };
        string Marker(int seq, string kind) => JsonSerializer.Serialize(new { v = 1, seq, tick = 0, wall_elapsed_ms = 0d,
            kind, subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
            expected_x = 0f, expected_y = 0f, amount = 0, detail = "" });
        // **`settled` and `release` vary together here because the producer never writes them on one
        // record.** `DecideCourseEachTick` traces a release on the tick after publication and that
        // record is unsettled, while the settled record holds the numbers; on the 22 September 2026
        // capture the two are disjoint over all 2,340 payloads. A fixture that wrote every payload
        // settled with an empty release — which all four in this file did — cannot tell a reader that
        // picks the right record from one that prints a dash on every row of the table, and the second
        // is what shipped.
        string Payload(int seq, long tick, string activity, long steps, long priced, long refused,
            bool settled = true, string release = "")
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "course-course-decision",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
                payload_kind = ReadCourseDecisions.Kind, payload_version = 1, phase = "brain",
                observation_ordinal = tick, receipt_watermark = 0L,
                payload = new { Kind = ReadCourseDecisions.Kind, Version = 1, Fields = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["reason"] = Field("text", "course-published"),
                    ["activity"] = Field("text", activity),
                    ["settled"] = Field("flag", settled ? "true" : "false"),
                    ["purpose"] = Field("text", activity),
                    ["steps"] = Field("integer", steps.ToString(CultureInfo.InvariantCulture)),
                    ["orders-priced"] = Field("integer", priced.ToString(CultureInfo.InvariantCulture)),
                    ["orders-refused"] = Field("integer", refused.ToString(CultureInfo.InvariantCulture)),
                    ["search-exhausted"] = Field("flag", "true"),
                    ["release-reason"] = Field("text", release),
                    ["facts"] = Field("integer", "7"),
                } } });
        string Admission(int seq, long tick, long combat)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "decision",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0,
                detail = $"scores=;{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}combat=usable:{combat},unknown:0,unusable:0,reason:-;" });

        string tsv = Path.GetTempFileName(), events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
        try
        {
            // Ten ticks, six decisions, and every rule the fold has.
            //   1–3  decision 1, retained; its numbers are traced at tick 2 rather than at its own
            //        choice_tick of 1, and its release at tick 3 on an unsettled record
            //   4    decision 2, tracing nothing at all
            //   5–6  decision 3, saying what decision 1 said, but under a census that has moved
            //   7    decision 4, identical to 3 and tracing no release
            //   8–9  decision 5, identical again, releasing `next-use-invalid:…`
            //   10   decision 6, identical again, releasing `course-complete`
            // So 1 and 2 fold; 3, 4 and 5 fold because a span that traced no release is not a decision
            // that was not released; and 6 splits off 5 because two reasons that are both there and
            // differ are a difference. Three rows.
            var rows = new StringBuilder("# schema=0.44.0\n"
                + "tick\tchoice_id\tchoice_tick\taction\tdecide_ms\ttask_order\ttask_order_runner_up\n"
                + "1\t1\t1\tcombat\t4.00\t-\t-\n"
                + "2\t1\t1\tcombat\t6.00\t-\t-\n"
                + "3\t1\t1\tcombat\t5.00\t-\t-\n"
                + "4\t2\t4\tcombat\t9.00\t-\t-\n"
                + "5\t3\t5\tcombat\t7.00\t-\t-\n"
                + "6\t3\t5\tcombat\t7.00\t-\t-\n"
                + "7\t4\t7\tcombat\t7.00\t-\t-\n"
                + "8\t5\t8\tcombat\t7.00\t-\t-\n"
                + "9\t5\t8\tcombat\t7.00\t-\t-\n"
                + "10\t6\t10\tcombat\t7.00\t-\t-\n");
            File.WriteAllText(tsv, rows.ToString());
            File.WriteAllLines(events, new[]
            {
                Marker(0, "session"),
                Admission(1, 1, 3),
                Payload(2, 2, "combat", 2, 7, 6),
                Payload(3, 3, "combat", 2, 7, 6, settled: false, release: "course-complete"),
                Admission(4, 5, 1),
                Payload(5, 5, "combat", 2, 7, 6),
                Payload(6, 7, "combat", 2, 7, 6),
                Payload(7, 8, "combat", 2, 7, 6),
                Payload(8, 8, "combat", 2, 7, 6, settled: false, release: "next-use-invalid:accepted-use-not-present"),
                Payload(9, 10, "combat", 2, 7, 6),
                Payload(10, 10, "combat", 2, 7, 6, settled: false, release: "course-complete"),
                Marker(11, "session-end"),
            });

            string page = WriteCourseTimeline.Of(Session.Load(tsv), fullTimeline: true);
            Require(page.Contains("6 decision(s) over 10 tick(s)", StringComparison.Ordinal),
                "the page did not count decisions by their identity rather than by ticks: " + page);
            // The payload traced at tick 2 belongs to the decision reached at tick 1, and finding it is
            // what puts numbers rather than dashes on the row. Asserted before the undescribed count,
            // because a join keyed on the decision tick moves both and only this one names the cause.
            string[] lines = page.Split('\n');
            string opening = lines.FirstOrDefault(l => l.StartsWith("  1", StringComparison.Ordinal)
                && (l.Length > 3 && (l[3] == '–' || l[3] == ' '))) ?? "";
            Require(opening.Contains("7/6", StringComparison.Ordinal),
                "the row opening at tick 1 carries no numbers, so the payload traced inside that decision's span was not joined to it — "
                + $"a join keyed on `choice_tick` finds nothing at tick 1, because the trace landed at tick 2: '{opening.Trim()}'");
            // The numbers and the release come off different records of the same span, and a reader that
            // takes both from the settled one prints a dash here while the producer wrote a reason.
            Require(opening.Contains("course-complete@3", StringComparison.Ordinal),
                "the row opening at tick 1 carries no release reason, so the released column was read off the settled record — "
                + $"the settled record is the one with the numbers and the release rides on the unsettled one: '{opening.Trim()}'");
            string middle = lines.FirstOrDefault(l => l.StartsWith("  5", StringComparison.Ordinal)) ?? "";
            Require(middle.Contains("next-use-invalid:accepted-use-not-present@8", StringComparison.Ordinal),
                "the run opening at tick 5 lost the release its third decision traced — a run whose representative traced no release must "
                + $"take the one a later member did, or the reason is dropped from the page entirely: '{middle.Trim()}'");
            Require(page.Contains("1 decision(s) traced no payload of their own", StringComparison.Ordinal),
                "a decision with no traced payload was not reported as undescribed, or more of them were undescribed than the fixture holds: " + page);
            Require(page.Contains("combat=3", StringComparison.Ordinal) && page.Contains("combat=1", StringComparison.Ordinal),
                "the census admission standing when each decision ran was not carried onto its row: " + page);
            int dataRows = lines.Count(line => line.Length > 2 && line.StartsWith("  ", StringComparison.Ordinal)
                && char.IsAsciiDigit(line[2]));
            Require(dataRows == 3, $"the table printed {dataRows} data row(s) rather than three — the undescribed decision must fold into the "
                + "run it sits in, the census moving must split one, a decision that traced no release must fold with one that did, and two "
                + $"releases that are both there and differ must split: {page}");

            // A capture whose `choice_id` is the family chooser's declines by name rather than drawing a
            // table of a brain it did not run.
            File.WriteAllText(tsv, rows.ToString().Replace("# schema=0.44.0", "# schema=0.42.0", StringComparison.Ordinal));
            string older = WriteCourseTimeline.Of(Session.Load(tsv), fullTimeline: true);
            Require(older.Contains("unavailable", StringComparison.Ordinal) && older.Contains("0.43.0", StringComparison.Ordinal),
                "a capture from before the course owned `choice_id` was drawn as a course timeline: " + older);
        }
        finally { File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
    }

    private static void ASyntheticCaptureIsNotReadAsPlay()
    {
        string file = Path.GetTempFileName();
        try
        {
            string Describe(string preamble)
            {
                File.WriteAllText(file, preamble + "tick\taction\n1\tkeep-company\n600\tkeep-company\n");
                return DescribeSession.Of(Session.Load(file));
            }

            string played = Describe("# schema=0.45.0\n");
            Require(played.Contains("of play at sixty a tick", StringComparison.Ordinal) && !played.Contains("synthetic", StringComparison.Ordinal),
                "an ordinary capture must still read as play: " + played);

            string replayed = Describe("# schema=0.45.0\n# synthetic=world-run;source-capture=2026-09-22_10-05-56-125;note=nobody played this\n");
            Require(!replayed.Contains("of play at sixty a tick", StringComparison.Ordinal),
                "a synthetic capture still claimed its ticks were play: " + replayed);
            Require(replayed.Contains("synthetic world-run replaying 2026-09-22_10-05-56-125", StringComparison.Ordinal),
                "a synthetic capture did not name what it is a replay of: " + replayed);
            Require(replayed.Contains("of replayed ticks at sixty a tick, not of play", StringComparison.Ordinal),
                "the span was dropped rather than restated as what it is: " + replayed);

            // A harness that is not the world run must be refused as play just as hard, so the marker is
            // read for whatever producer it names rather than matched against one.
            var other = DescribeSession.Synthetic(new Dictionary<string, string>(StringComparer.Ordinal)
                { ["synthetic"] = "some-other-harness;source-capture=elsewhere" });
            Require(other is { Producer: "some-other-harness", SourceCapture: "elsewhere" },
                "a synthetic marker naming a producer this reader has not heard of was not read");
            Require(DescribeSession.Synthetic(new Dictionary<string, string>(StringComparer.Ordinal)) is null,
                "a capture with no synthetic marker was read as synthetic");
            // A marker with no source named is still a refusal; the source is what is unknown, not the fact.
            Require(DescribeSession.Synthetic(new Dictionary<string, string>(StringComparer.Ordinal)
                { ["synthetic"] = "world-run" }) is { SourceCapture: "unnamed" },
                "a synthetic marker with no source capture was treated as absent rather than as an unnamed source");

            // **The marker the world run actually writes, verbatim, because the first version of this
            // parser was wrong on it and right on every fixture.** The note runs to the end of the line
            // and has a semicolon inside it, so a reader splitting the whole value on `;` and calling
            // any segment without an `=` the producer reports "  it is the world run replaying the
            // source capture's player track, hostiles and drops" as the name of the harness — which is
            // what the report printed the first time it met a real one.
            var real = DescribeSession.Synthetic(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["synthetic"] = "world-run;source-capture=2026-09-22_10-05-56-125;note=nobody played this; "
                    + "it is the world run replaying the source capture's player track, hostiles and drops",
            });
            Require(real is { Producer: "world-run", SourceCapture: "2026-09-22_10-05-56-125" },
                $"the marker the world run actually writes was misparsed: producer '{real?.Producer}', source '{real?.SourceCapture}'");
            Require(real!.Note.StartsWith("nobody played this;", StringComparison.Ordinal)
                    && real.Note.EndsWith("hostiles and drops", StringComparison.Ordinal),
                $"the note was cut at its own semicolon rather than read to the end of the line: '{real.Note}'");
        }
        finally { File.Delete(file); }
    }

    private static void ACensusAdmissionMustSurviveItsOwnBinder()
    {
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(parts));
        string combat = Source("Companion", "Brain", "Activities", "Combat", "CombatCourseOpportunity.cs");
        string assistance = Source("Companion", "Brain", "Infrastructure", "Selection", "Opportunities", "BindAssistanceOpportunity.cs");
        string audit = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "AuditDecisionContracts.cs");
        string recorder = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs");

        Require(combat.Contains("\"target-capture-missing\"", StringComparison.Ordinal),
            "combat no longer refuses `target-capture-missing`; the census-against-binder check is reading a string nobody writes");
        Require(assistance.Contains("Refuse(\"assistance-target-unresolved\")", StringComparison.Ordinal),
            "the assistance binder no longer refuses `assistance-target-unresolved`; the census-against-binder check is reading a string nobody writes");
        foreach (string domain in ACensusAdmissionSurvivesItsBinder.DomainsBehind["assistance-target-unresolved"])
            Require(audit.Contains($"\"{domain}\"", StringComparison.Ordinal) && assistance.Contains($"\"{domain}\"", StringComparison.Ordinal),
                $"the assistance domain '{domain}' is named by neither the binder nor the recorder's own refusal map; the reader's table has drifted from the producer's");
        Require(audit.Contains("\"combat\" => CombatNotObserved", StringComparison.Ordinal),
            "the recorder's own refusal map no longer sends the combat domain to combat's refusal; the reader's table mirrors that map and has drifted from it");
        Require(recorder.Contains(ACensusAdmissionSurvivesItsBinder.AdmittedPrefix, StringComparison.Ordinal),
            $"the recorder no longer writes `{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}`, which is the only place a census admission reaches a capture");
        Require(audit.Contains($"\"{ACensusAdmissionSurvivesItsBinder.TripwireViolation}\"", StringComparison.Ordinal),
            $"the recorder's own tripwire kind `{ACensusAdmissionSurvivesItsBinder.TripwireViolation}` is gone; the cross-check reads a record nobody writes");

        object Field(string kind, string text) => new { Kind = kind, Text = text };
        string Marker(int seq, string kind) => JsonSerializer.Serialize(new { v = 1, seq, tick = 0, wall_elapsed_ms = 0d,
            kind, subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
            expected_x = 0f, expected_y = 0f, amount = 0, detail = "" });

        // The `decision` occurrence's detail, in the producer's own shape: the admission entry's values
        // are comma-separated key:value pairs inside a semicolon-separated field, which is why the check
        // parses them itself rather than through ReadGodsEyeEvents.Field.
        string Admission(int seq, long tick, params (string Domain, long Usable)[] domains)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "decision",
                subject = 0, related = "", label = "keep-company", channel = "WithPlayer", pos_x = 0f, pos_y = 0f,
                vel_x = 0f, vel_y = 0f, expected_x = 0f, expected_y = 0f, amount = 0,
                detail = "scores=;" + string.Concat(domains.Select(d =>
                    $"{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}{d.Domain}=usable:{d.Usable},unknown:0,unusable:0,reason:-;")) });

        string Refusal(int seq, long tick, params (string Reason, long Count)[] refusals)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["reason"] = Field("text", "published-course-holds-no-step"),
                ["activity"] = Field("text", "keep-company"),
                ["settled"] = Field("flag", "true"),
                ["purpose"] = Field("text", ""),
                ["steps"] = Field("integer", "0"),
                ["orders-priced"] = Field("integer", "1"),
                ["orders-refused"] = Field("integer", refusals.Sum(r => r.Count).ToString(CultureInfo.InvariantCulture)),
                ["search-exhausted"] = Field("flag", "true"),
                ["release-reason"] = Field("text", ""),
                ["facts"] = Field("integer", "7"),
            };
            foreach ((string reason, long count) in refusals)
                fields[ReadCourseDecisions.RefusalPrefix + reason] = Field("integer", count.ToString(CultureInfo.InvariantCulture));
            return JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "course-course-decision",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
                payload_kind = ReadCourseDecisions.Kind, payload_version = 1, phase = "brain",
                observation_ordinal = tick, receipt_watermark = 0L,
                payload = new { Kind = ReadCourseDecisions.Kind, Version = 1, Fields = fields } });
        }

        string Violation(int seq, long tick, string signature)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "course-contract-violation",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
                payload_kind = "contract-violation", payload_version = 1, phase = "brain",
                observation_ordinal = tick, receipt_watermark = 0L,
                payload = new { Kind = "contract-violation", Version = 1, Fields = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["violation"] = Field("text", ACensusAdmissionSurvivesItsBinder.TripwireViolation),
                    ["detail"] = Field("text", "the census admitted and the binder refused"),
                    ["signature"] = Field("text", signature),
                } } });

        void Drive(string schema, string[] records, Action<Session> assert)
        {
            string tsv = Path.GetTempFileName(), events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
            try
            {
                File.WriteAllText(tsv, $"# schema={schema}\ntick\n1\n# end=fixture;rows=1\n");
                var lines = new List<string> { Marker(0, "session") };
                lines.AddRange(records);
                lines.Add(Marker(records.Length + 1, "session-end"));
                File.WriteAllLines(events, lines);
                assert(Session.Load(tsv));
            }
            finally { File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
        }

        Finding[] Run(Session session) => new ACensusAdmissionSurvivesItsBinder().Run(session).ToArray();

        // One decision carrying both halves, then two carrying only the refusal against the admission
        // carried forward. The Definitive grade is earned by the first; the other two are the inference
        // and must be counted apart rather than folded into it.
        Drive("0.44.0", new[]
        {
            Admission(1, 100, ("combat", 3)),
            Refusal(2, 100, ("target-capture-missing", 12)),
            Refusal(3, 101, ("target-capture-missing", 12)),
            Refusal(4, 102, ("target-capture-missing", 12)),
        }, session =>
        {
            Finding[] found = Run(session);
            Require(found.Length == 1, $"the census-against-binder check reported {found.Length} finding(s) rather than one per contradicting domain");
            Require(found[0].Severity == Severity.Definitive, $"a contradiction observed in one record was graded {found[0].Severity}");
            Require(found[0].Rows == 3, $"the finding counted {found[0].Rows} decision(s) rather than the three the fixture holds");
            Require(found[0].Detail.Contains("1 of those records carried the admission and the refusal in one record", StringComparison.Ordinal),
                "the finding did not separate the observed contradiction from the ones read against a carried admission: " + found[0].Detail);
            Require(found[0].Detail.Contains("refused 36 order(s)", StringComparison.Ordinal),
                "the finding lost the refusal total it is counting: " + found[0].Detail);
            Require(found[0].FirstTick == 100 && found[0].LastTick == 102, "the finding lost the span its decisions covered");
        });

        // **The same contradiction with no decision carrying both halves is an inference, and the grade
        // is the only thing that says so.** Definitive drives the process exit code and the sentence
        // "something in this session is wrong by construction", and this check's whole design rests on
        // the distinction: the admission is carried forward across a retained course because the
        // producer's `Admitted` list persists, which is a reading of the producer rather than a record
        // of the tick. A sentinel planted `bool observed = true` in place of the gate on 22 September
        // 2026 and the suite stayed green at 55 groups, because every arm here held an observed record.
        //
        // The fixture also holds the second half: the admission is published on a tick no payload
        // shares, which is what 230 of that capture's 468 admission ticks look like. It must still be
        // carried, or nearly half the census the finding's numbers rest on is silently the previous one.
        Drive("0.44.0", new[]
        {
            Admission(1, 100, ("combat", 3)),
            Refusal(2, 101, ("target-capture-missing", 12)),
            Refusal(3, 102, ("target-capture-missing", 12)),
        }, session =>
        {
            Finding[] found = Run(session);
            Require(found.Length == 1, $"the check reported {found.Length} finding(s) on a carried-only contradiction rather than one");
            Require(found[0].Severity == Severity.Potential,
                $"a contradiction no single record carries was graded {found[0].Severity} — every decision here was read against an "
                + "admission carried forward from an earlier occurrence, so the finding is an inference and grading it Definitive "
                + "asserts by construction what the record only implies");
            Require(found[0].Detail.Contains("0 of those records carried the admission and the refusal in one record", StringComparison.Ordinal),
                "the finding claimed an observed record on a fixture that holds none: " + found[0].Detail);
            Require(found[0].Rows == 2, $"the carried-only finding counted {found[0].Rows} decision(s) rather than the two the fixture holds");
        });

        // **A retained course traces a record per tick, so records are not decisions.** Where the
        // capture carries `choice_id` the finding counts identities and names the records beside them;
        // the three records here are two decisions, and saying "3 decision(s)" is the inflation every
        // hand reading of the 22 September 2026 capture inherited — its 842 combat records are 555
        // decisions, and its 6,299 refusals are a tally republished rather than distinct orders.
        {
            string tsv = Path.GetTempFileName(), events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
            try
            {
                File.WriteAllText(tsv, "# schema=0.44.0\ntick\tchoice_id\n100\t7\n101\t7\n102\t8\n");
                File.WriteAllLines(events, new[]
                {
                    Marker(0, "session"),
                    Admission(1, 100, ("combat", 3)),
                    Refusal(2, 100, ("target-capture-missing", 12)),
                    Refusal(3, 101, ("target-capture-missing", 12)),
                    Refusal(4, 102, ("target-capture-missing", 12)),
                    Marker(5, "session-end"),
                });
                Finding[] found = Run(Session.Load(tsv));
                Require(found.Length == 1, $"the check reported {found.Length} finding(s) on the identity-keyed fixture rather than one");
                Require(found[0].Detail.Contains("2 decision(s), traced over 3 record(s)", StringComparison.Ordinal),
                    "the finding counted traced records as decisions — three records under two `choice_id` values are two decisions, and "
                    + $"conflating them is how a republished refusal tally reads as fresh orders: {found[0].Detail}");
                Require(found[0].Detail.Contains("as recorded", StringComparison.Ordinal)
                    && found[0].Detail.Contains("upper bound on distinct orders", StringComparison.Ordinal),
                    "the finding stated its refusal total without saying it counts refusals as traced, so a reader takes a republished "
                    + $"tally for a count of distinct orders: {found[0].Detail}");
            }
            finally { File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
        }

        // A census that admitted nothing usable is not contradicted by any number of refusals, which is
        // the whole load-bearing half: refusals alone are a search doing its job.
        Drive("0.44.0", new[]
        {
            Admission(1, 100, ("combat", 0)),
            Refusal(2, 100, ("target-capture-missing", 12)),
        }, session => Require(Run(session).Length == 0,
            "a domain admitted with nothing usable was reported as contradicting its binder"));

        // A refusal no binder in the table owns is attributed to nothing.
        Drive("0.44.0", new[]
        {
            Admission(1, 100, ("combat", 3)),
            Refusal(2, 100, ("budget-cut", 12)),
        }, session => Require(Run(session).Length == 0,
            "a refusal reason outside the reader's own binder table was attributed to a domain anyway"));

        // Two assistance domains usable at once: the reason names the binder, not the site, so the
        // refusal cannot be attributed to one of them and the finding has to say so.
        Drive("0.44.0", new[]
        {
            Admission(1, 100, ("collect-target", 4), ("light-target", 2)),
            Refusal(2, 100, ("assistance-target-unresolved", 16)),
        }, session =>
        {
            Finding[] found = Run(session);
            Require(found.Length == 2, $"two usable assistance domains behind one refusal produced {found.Length} finding(s) rather than one each");
            Require(found.All(f => f.Detail.Contains("cannot be attributed to", StringComparison.Ordinal)),
                "an unattributable refusal was reported as if it named its domain");
        });

        // The recorder's own tripwire, from the other side of the decision: agreement is stated, and a
        // domain it fired on that this reader found nothing in is its own finding.
        Drive("0.45.0", new[]
        {
            Admission(1, 100, ("combat", 3)),
            Refusal(2, 100, ("target-capture-missing", 12)),
            Violation(3, 100, "combat:target-capture-missing:Unresolved"),
        }, session =>
        {
            Finding[] found = Run(session);
            Require(found.Length == 1 && found[0].Detail.Contains("the two instruments agree", StringComparison.Ordinal),
                "the reader did not state its agreement with the recorder's own tripwire: " + string.Join(" | ", found.Select(f => f.Title)));
        });
        Drive("0.45.0", new[]
        {
            Admission(1, 100, ("combat", 3)),
            Refusal(2, 100, ("target-capture-missing", 12)),
            Violation(3, 100, "collect-target:assistance-target-unresolved:Unresolved"),
        }, session =>
        {
            Finding[] found = Run(session);
            Require(found.Any(f => f.Title.Contains("tripwire named", StringComparison.Ordinal)),
                "the tripwire naming a domain this reader found nothing in was not reported as the instruments disagreeing");
            Require(found.Any(f => f.Detail.Contains("did **not** fire for combat", StringComparison.Ordinal)),
                "a finding whose domain the tripwire never named claimed corroboration it does not have");
        });

        // A capture older than the two fields skips by name through the runner rather than reading a
        // clean run, and the runner is what has to skip it.
        Drive("0.41.0", new[] { Admission(1, 100, ("combat", 3)), Refusal(2, 100, ("target-capture-missing", 12)) },
            session => Require(Program.Evaluate(session).Skipped.Any(s => s.Name == new ACensusAdmissionSurvivesItsBinder().Name),
                "a capture from before the census admissions existed was graded rather than skipped by name"));
    }

    /// <summary>
    /// The effect check reads what the recorder's effect contract wrote, and the pot witness reads the
    /// Container count from 0.47.0: both pinned against their producers by path, then driven on a
    /// synthetic capture in each direction — an off-step effect named with the recorder's own total, an
    /// unjudged effect named as a wiring fault, a capture of bound effects quiet, and a 0.46.0 capture
    /// skipped by name rather than graded clean.
    /// </summary>
    private static void EveryEffectMustBeTheAcceptedStep()
    {
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(parts));
        string events = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordGodsEyeEvents.cs");
        string audit = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "AuditDecisionContracts.cs");
        string recorder = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs");
        Require(audit.Contains("binding-verdict=", StringComparison.Ordinal) && events.Contains("verdict.Fields", StringComparison.Ordinal),
            "the effect recorders no longer append the step's `binding-verdict`, so the effect check reads a field nobody writes");
        foreach (string kind in new[] { EveryEffectWasTheAcceptedStep.WithoutBinding, EveryEffectWasTheAcceptedStep.OffBinding })
            Require(audit.Contains($"\"{kind}\"", StringComparison.Ordinal),
                $"the recorder's effect contract no longer names `{kind}`; the effect check reads a kind nobody writes");
        Require(recorder.Contains("container-usable:", StringComparison.Ordinal),
            "the recorder no longer writes `container-usable:`, so the pot witness reads nothing from a 0.47.0 capture");

        var containers = ACensusAdmissionSurvivesItsBinder.ReadContainerAdmissions(
            $"scores=;{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}collect-target=usable:5,unknown:0,unusable:0,reason:-,container-usable:2;");
        Require(containers.TryGetValue("collect-target", out long pots) && pots == 2 && containers.Count == 1,
            "the Container count inside a 0.47.0 admission entry was misread");
        Require(ACensusAdmissionSurvivesItsBinder.ReadContainerAdmissions(
                $"{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}pot-target=usable:1,unknown:0,unusable:0,reason:-;").Count == 0,
            "an admission entry with no Container count was read as zero pots rather than as a capture that cannot say");
        Require(ACensusAdmissionSurvivesItsBinder.ReadAdmissions(
                $"{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}collect-target=usable:5,unknown:0,unusable:0,reason:-,container-usable:2;")["collect-target"].Usable == 5,
            "the extra Container value disturbed the usable count the census check reads");

        object Field(string kind, string text) => new { Kind = kind, Text = text };
        string Marker(int seq, string kind) => JsonSerializer.Serialize(new { v = 1, seq, tick = 0, wall_elapsed_ms = 0d,
            kind, subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
            expected_x = 0f, expected_y = 0f, amount = 0, detail = "" });
        string Effect(int seq, long tick, string kind, string verdict, long step)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind, subject = 0, related = "7000001",
                label = "pickaxe", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 26 * 16 + 8f, expected_y = 59 * 16 + 8f, amount = 0,
                detail = $"effect=Damaged;binding-id={step};binding-origin=activity;binding-verdict={verdict}" });
        string Violation(int seq, long tick, string kind, long total)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "course-contract-violation",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
                payload_kind = "contract-violation", payload_version = 1, phase = "brain",
                observation_ordinal = tick, receipt_watermark = 0L,
                payload = new { Kind = "contract-violation", Version = 1, Fields = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["violation"] = Field("text", kind),
                    ["signature"] = Field("text", "tool-effect:pickaxe:mine"),
                    ["occurrences-total"] = Field("integer", total.ToString(CultureInfo.InvariantCulture)),
                } } });

        void Drive(string schema, string[] records, Action<Session> assert, string closing = "")
        {
            string tsv = Path.GetTempFileName(), sidecar = Path.ChangeExtension(tsv, null) + "-events.jsonl";
            try
            {
                File.WriteAllText(tsv, $"# schema={schema}\ntick\n1\n{closing}# end=fixture;rows=1\n");
                var lines = new List<string> { Marker(0, "session") };
                lines.AddRange(records);
                lines.Add(Marker(records.Length + 1, "session-end"));
                File.WriteAllLines(sidecar, lines);
                assert(Session.Load(tsv));
            }
            finally { File.Delete(tsv); if (File.Exists(sidecar)) File.Delete(sidecar); }
        }

        Finding[] Run(Session session) => new EveryEffectWasTheAcceptedStep().Run(session).ToArray();

        // One strike beside its step, one on it, one unjudged, and the recorder's own total of four for the
        // off-step kind: the finding is graded on the kind and counts the recorder's total, because the
        // coalescing keeps fewer occurrences than it counts.
        Drive("0.47.0", new[]
        {
            Effect(1, 100, "tool-effect", EveryEffectWasTheAcceptedStep.OffBinding, 41),
            Effect(2, 101, "tool-effect", "bound", 41),
            Effect(3, 102, "world-interaction", "unaudited", 0),
            Violation(4, 100, EveryEffectWasTheAcceptedStep.OffBinding, 4),
        }, session =>
        {
            Finding[] found = Run(session);
            Finding? off = found.FirstOrDefault(f => f.Title.Contains("beside the accepted step", StringComparison.Ordinal));
            Require(off != null && off.Severity == Severity.Definitive && off.Rows == 4,
                $"an off-step strike with a recorder total of four was not one Definitive finding of four: {string.Join(" | ", found.Select(f => f.Title + " rows=" + f.Rows))}");
            Require(off!.Detail.Contains("tile 26,59", StringComparison.Ordinal) && off.Detail.Contains("under step 41", StringComparison.Ordinal),
                "the off-step finding lost where the effect landed or the step it was judged against: " + off.Detail);
            Require(found.Any(f => f.Title.Contains("went unjudged", StringComparison.Ordinal)),
                "an effect the contract could not judge was not reported as the reader's wiring fault");
            Require(!found.Any(f => f.Title.Contains("with no accepted step", StringComparison.Ordinal)),
                "a kind that never fired was reported");
        });

        // A capture that closed carries the contract's own count of judged effects, and the finding's
        // denominator is that count rather than the occurrences that reached the sidecar.
        Drive("0.47.0", new[] { Effect(1, 100, "tool-effect", EveryEffectWasTheAcceptedStep.OffBinding, 41) },
            session => Require(Run(session).Any(f => f.Detail.Contains("of the 9 effect(s) the contract judged", StringComparison.Ordinal)),
                "the closing line's `effects-audited` was not the finding's denominator"),
            closing: "# closing=world-unload;rows=1;decisions-audited=3;audit-observations-read=3;effects-audited=9\n");

        // The quiet half: bound effects only, nothing named.
        Drive("0.47.0", new[] { Effect(1, 100, "tool-effect", "bound", 41), Effect(2, 101, "pickup", "not-claimed", 0) },
            session => Require(Run(session).Length == 0, "a capture whose every effect was bound reported a finding"));

        // A capture from before the step rode on its effects skips by name rather than reading clean.
        Drive("0.46.0", new[] { Effect(1, 100, "tool-effect", EveryEffectWasTheAcceptedStep.OffBinding, 41) },
            session => Require(Program.Evaluate(session).Skipped.Any(s => s.Name == new EveryEffectWasTheAcceptedStep().Name),
                "a capture from before effect occurrences named their step was graded rather than skipped by name"));
    }

    private static void CourseDecisionsAreReadCheckedAndNarrated()
    {
        // The producer's own shape, from `CourseTracePayload`: a kind, a version and a field map whose
        // values are a kind and a text. Written out by hand rather than by serialising the producer's
        // type, so a change to that type fails this test instead of travelling silently through it.
        object Field(string kind, string text) => new { Kind = kind, Text = text };
        string Decision(int seq, long tick, string reason, string activity, string purpose, long steps,
            long priced, long refused, bool exhausted, params (string Reason, long Count)[] refusals)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["reason"] = Field("text", reason),
                ["activity"] = Field("text", activity),
                ["settled"] = Field("flag", "true"),
                ["purpose"] = Field("text", purpose),
                ["steps"] = Field("integer", steps.ToString(CultureInfo.InvariantCulture)),
                ["orders-priced"] = Field("integer", priced.ToString(CultureInfo.InvariantCulture)),
                ["orders-refused"] = Field("integer", refused.ToString(CultureInfo.InvariantCulture)),
                ["search-exhausted"] = Field("flag", exhausted ? "true" : "false"),
                ["release-reason"] = Field("text", ""),
                ["facts"] = Field("integer", "7"),
            };
            foreach ((string name, long count) in refusals)
                fields[ReadCourseDecisions.RefusalPrefix + name] = Field("integer", count.ToString(CultureInfo.InvariantCulture));
            return JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = 0d, kind = "course-course-decision",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
                payload_kind = ReadCourseDecisions.Kind, payload_version = 1, phase = "brain",
                observation_ordinal = 1L, receipt_watermark = 0L,
                payload = new { Kind = ReadCourseDecisions.Kind, Version = 1, Fields = fields } });
        }
        string Marker(int seq, string kind) => JsonSerializer.Serialize(new { v = 1, seq, tick = 0, wall_elapsed_ms = 0d,
            kind, subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
            expected_x = 0f, expected_y = 0f, amount = 0, detail = "" });

        void Drive(string name, string[] decisions, Action<Session, string> assert)
        {
            string tsv = Path.GetTempFileName(), events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
            try
            {
                File.WriteAllText(tsv, "# schema=0.41.0\ntick\n1\n# end=fixture;rows=1;diagnostics-incomplete=False\n");
                var lines = new List<string> { Marker(0, "session") };
                lines.AddRange(decisions);
                lines.Add(Marker(decisions.Length + 1, "session-end"));
                File.WriteAllLines(events, lines);
                assert(Session.Load(tsv), tsv);
            }
            finally { File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
        }

        // Clean: three decisions. The first two are the same course kept on consecutive ticks and must
        // coalesce; the third is a different decision and must not. A publish tick and a retained tick
        // are deliberately *not* one run even on one course, because "it decided this" and "it kept
        // deciding this" are the two things a reader of a capture is trying to tell apart.
        string[] clean =
        {
            Decision(1, 100, ReadCourseDecisions.RetainedReason, "mine", "mine", 2, 40, 12, true, ("no-route", 8), ("outvalued", 4)),
            Decision(2, 101, ReadCourseDecisions.RetainedReason, "mine", "mine", 2, 0, 0, true),
            Decision(3, 160, "course-complete", "keep-company", "", 0, 5, 3, false, ("budget-cut", 3)),
        };
        Drive("clean", clean, (session, path) =>
        {
            CourseDecisionLog log = ReadCourseDecisions.From(ReadGodsEyeEvents.Read(path));
            Require(log.Decisions.Count == 3 && log.Unreadable == 0,
                $"the payload parser read {log.Decisions.Count} decision(s) and {log.Unreadable} unreadable, expected 3 and 0");
            CourseDecision first = log.Decisions[0];
            Require(first.Reason == ReadCourseDecisions.RetainedReason && first.Activity == "mine" && first.Purpose == "mine"
                && first.Settled && first.Steps == 2 && first.OrdersPriced == 40 && first.OrdersRefused == 12
                && first.SearchExhausted && first.Facts == 7 && first.Refusals.Count == 2,
                "a well-formed decision did not round-trip through the payload parser");
            Require(log.Decisions[2].What == "keep-company",
                "an empty order must read as its activity rather than as a blank, because an empty course is companionship");
            Require(!new EveryCourseDecisionAccountsForItsOwnSearch().Run(session)
                    .Any(f => f.Severity == Severity.Definitive),
                "the contracts check fired on a record that breaks none of them");
            string narration = DescribeCourseDecisions.Of(session, fullTimeline: true);
            // Two consecutive decisions on one course coalesce; the third is its own run.
            Require(narration.Contains("3 decision(s) in 2 run(s)", StringComparison.Ordinal),
                "the narration did not coalesce two ticks of one decision into a single run: " + narration);
            Require(narration.Contains("100–101", StringComparison.Ordinal) && narration.Contains("cut", StringComparison.Ordinal),
                "the narration lost either the coalesced tick range or the cut marker: " + narration);
            var rows = new MeasureCourseWork().Rows(session).ToList();
            double Value(string stem) => rows.First(r => r.Case == "course/" + stem).Value ?? double.NaN;
            Require(Math.Abs(Value("retained") - 200.0 / 3) < 1e-9, $"retained share read {Value("retained")}, expected two in three");
            Require(Math.Abs(Value("bound-step") - 200.0 / 3) < 1e-9, $"bound-step share read {Value("bound-step")}, expected two in three");
            Require(Math.Abs(Value("search-exhausted") - 200.0 / 3) < 1e-9, $"exhausted share read {Value("search-exhausted")}");
            // The refusal split is a share of what was tallied, not of what was refused, and the two
            // differ whenever the producer publishes only its largest reasons.
            Require(Math.Abs(Value("refusal/no-route") - 100.0 * 8 / 15) < 1e-9, $"no-route share read {Value("refusal/no-route")}");
        });

        // Mutation one: a tally accounting for more orders than the same record says it refused.
        Drive("over-counted tally", new[] { Decision(1, 100, "course-published", "mine", "mine", 2, 40, 5, true, ("no-route", 9)) },
            (session, _) => Require(new EveryCourseDecisionAccountsForItsOwnSearch().Run(session)
                    .Any(f => f.Severity == Severity.Definitive && f.Title.Contains("more orders than", StringComparison.Ordinal)),
                "a refusal tally exceeding its own refused total was accepted"));

        // Mutation two: a bound purpose on a course the same record says is empty.
        Drive("bound step, no steps", new[] { Decision(1, 100, "course-published", "mine", "mine", 0, 40, 5, true) },
            (session, _) => Require(new EveryCourseDecisionAccountsForItsOwnSearch().Run(session)
                    .Any(f => f.Severity == Severity.Definitive && f.Title.Contains("no steps", StringComparison.Ordinal)),
                "a purpose bound against a course with no steps was accepted"));

        // Mutation three: a decision with no reason, which is a record that can be attributed to nothing.
        Drive("unnamed reason", new[] { Decision(1, 100, "", "mine", "mine", 2, 40, 5, true) },
            (session, _) => Require(new EveryCourseDecisionAccountsForItsOwnSearch().Run(session)
                    .Any(f => f.Severity == Severity.Definitive && f.Title.Contains("without a reason", StringComparison.Ordinal)),
                "a decision carrying no reason was accepted"));

        // A capture new enough to hold decisions and holding none is a statement, and the check must
        // say so rather than reporting the clean run it never measured.
        Drive("no decisions", Array.Empty<string>(), (session, _) =>
        {
            Require(new EveryCourseDecisionAccountsForItsOwnSearch().Run(session)
                    .Any(f => f.Severity == Severity.Potential && f.Title.Contains("no course decision", StringComparison.Ordinal)),
                "an empty course record read as a clean run");
            Require(new MeasureCourseWork().Rows(session).Any(r => r.Verdict == "skipped"),
                "the measure reported numbers over a capture holding no decision");
        });
    }

    private static void CourseReaderRejectsMixedAndDigestOnlySnapshots()
    {
        var context = new CourseTraceContext(1, "brain", 1, 1, 1, 1, 1, "fixture", 1, 0, "world", "source", "policy", "config");
        string value = "actual", digest = CourseDecisionSnapshotCoverage.Digest(value);
        CourseDecisionSnapshot good = CourseDecisionSnapshot.Create(context, "model", "scheduler", "random",
            new[] { new CourseManifestEntry("fact", 1, digest, "expected") }, new[] { new CourseManifestEntry("fact", 1, digest, "captured", value) });
        CourseDecisionSnapshot bad = CourseDecisionSnapshot.Create(context, "model", "scheduler", "random",
            new[] { new CourseManifestEntry("fact", 1, digest, "expected") }, new[] { new CourseManifestEntry("fact", 1, digest, "captured") });
        string Line(int seq, string kind, object? snapshot) => JsonSerializer.Serialize(new { v = 1, seq, tick = 1, wall_elapsed_ms = 0d, kind, subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f, expected_x = 0f, expected_y = 0f, amount = 0, detail = "", payload_kind = "course-decision-snapshot", payload_version = 1, phase = "brain", observation_ordinal = 1L, receipt_watermark = 0L, snapshot });
        void Check(string name, object?[] snapshots, bool complete, bool gap = false,
            string? footer = "# end=fixture;rows=1;diagnostics-incomplete=False\n")
        {
            string tsv = Path.GetTempFileName(), events = Path.ChangeExtension(tsv, null) + "-events.jsonl";
            try
            {
                File.WriteAllText(tsv, "# schema=0.41.0\ntick\n1\n" + footer);
                var lines = new List<string> { Line(0, "session", null) };
                int sequence = gap ? 2 : 1;
                foreach (var snapshot in snapshots) lines.Add(Line(sequence++, "course-decision-snapshot", snapshot));
                lines.Add(Line(sequence, "session-end", null));
                File.WriteAllLines(events, lines);
                var result = ReadCourseChronicle.Read(Session.Load(tsv), tsv);
                Require(result.Coverage == (complete ? "exact-input-complete" : "explanatory-partial"),
                    name + ": " + result.Coverage + "; " + string.Join("; ", result.Problems));
            }
            finally { File.Delete(tsv); if (File.Exists(events)) File.Delete(events); }
        }
        System.Text.Json.Nodes.JsonNode Corrupt(Action<System.Text.Json.Nodes.JsonNode> edit)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(good))!;
            edit(node); return node;
        }
        Check("complete writer-format snapshot", new object?[] { good }, true);
        Check("missing transport closure", new object?[] { good }, false, footer: null);
        Check("transport timeout", new object?[] { good }, false, footer: "# end=fixture;rows=1;diagnostics-incomplete=True\n");
        Check("wrong terminal row count", new object?[] { good }, false, footer: "# end=fixture;rows=2;diagnostics-incomplete=False\n");
        Check("corrected terminal status", new object?[] { good }, false,
            footer: "# end=fixture;rows=1;diagnostics-incomplete=False\n# end=fixture;rows=1;diagnostics-incomplete=True\n");
        Check("mixed complete and digest-only", new object?[] { good, bad }, false);
        Check("null snapshot", new object?[] { good, null }, false);
        Check("scalar snapshot", new object?[] { good, "invalid" }, false);
        Check("array snapshot", new object?[] { good, new[] { 1 } }, false);
        Check("sequence gap", new object?[] { good }, false, gap: true);
        Check("null manifest entry", new object?[] { Corrupt(n => n["CapturedReads"]![0] = null) }, false);
        Check("null fact key", new object?[] { Corrupt(n => n["CapturedReads"]![0]!["Key"] = null) }, false);
        Check("bad value digest", new object?[] { Corrupt(n => n["CapturedReads"]![0]!["Value"] = "changed") }, false);
        Check("duplicate expected key", new object?[] { Corrupt(n => n["ExpectedReads"]!.AsArray().Add(n["ExpectedReads"]![0]!.DeepClone())) }, false);
        Check("duplicate captured key", new object?[] { Corrupt(n => n["CapturedReads"]!.AsArray().Add(n["CapturedReads"]![0]!.DeepClone())) }, false);
        Check("wrong source tick", new object?[] { Corrupt(n => n["Context"]!["SourceTick"] = 2) }, false);
        Check("tampered scheduler", new object?[] { Corrupt(n => n["SchedulerState"] = "changed") }, false);
        Check("tampered policy", new object?[] { Corrupt(n => n["Context"]!["PolicyFingerprint"] = "changed") }, false);
        Check("unsupported snapshot version", new object?[] { Corrupt(n => n["PayloadVersion"] = 2) }, false);
    }

    /// <summary>
    /// A body with a route ahead of it that does not move, and the three neighbouring shapes that
    /// must stay quiet.
    ///
    /// The first case is built to fail if the check reads the wrong velocity column, and that is its
    /// whole reason for existing. Its <c>npc_vel</c> is zero while its <c>desired_vel</c> is 2 px a
    /// tick — the exact record a body pressed into a wall writes, because the motor sets the NPC's
    /// velocity from the displacement the circle contact allowed. A check reading <c>npc_vel</c> for
    /// "driven" would grade that stretch Potential instead of Definitive and report a frozen body as
    /// a body at rest, so the assertion is on the grade rather than on a finding merely existing.
    /// </summary>
    private static void AFrozenBodyWithARouteAheadIsReportedAndARestingOneIsNot()
    {
        string file = Path.GetTempFileName();
        try
        {
            Session Write(string desired, string velocity, float remaining, bool moving, string reflex = "-")
            {
                var text = new StringBuilder("# text_columns=npc_px,npc_vel,desired_vel,reflex,action,wall_normal\n");
                text.AppendLine("tick\twall_elapsed_ms\tnpc_px\tnpc_vel\tdesired_vel\troute_points\troute_remaining_px\treflex\taction\ttouched_wall\twall_normal\tclearance\tpinned");
                for (int i = 0; i < 90; i++)
                    text.AppendLine($"{i}\t{i * 16}\t{500 + (moving ? i : 0)},1280\t{velocity}\t{desired}\t5\t{remaining:0.0}\t{reflex}\tkeep-company\t1\t-1.00,0.00\t0.0\t{i}");
                File.WriteAllText(file, text.ToString());
                return Session.Load(file);
            }

            var driven = new TheBodyMovesWhenDriven().Run(Write("2.00,0.00", "0.00,0.00", 200f, moving: false)).ToList();
            Require(driven.Any(f => f.Severity == Severity.Definitive),
                "a body asked for 2 px a tick that never moved was not reported as driven — the check is reading the contact's "
                + "allowed velocity rather than the steering's request, which is zero on exactly the ticks this exists to catch");

            Require(new TheBodyMovesWhenDriven().Run(Write("0.00,0.00", "0.00,0.00", 200f, moving: false))
                    .All(f => f.Severity == Severity.Potential),
                "a body asked for nothing was graded definitive, which is a body at rest reported as a stall");

            Require(!new TheBodyMovesWhenDriven().Run(Write("2.00,0.00", "2.00,0.00", 200f, moving: true)).Any(),
                "a body that was moving was reported as frozen");

            // Inside the navigator's arrival radius there is nowhere left to be, so an unchanged
            // position is an arrival rather than a stall.
            Require(!new TheBodyMovesWhenDriven().Run(Write("2.00,0.00", "0.00,0.00", 6f, moving: false)).Any(),
                "a body holding station inside the arrival radius was called a stall");

            // A reflex owning the body is somebody else holding it, which is not this check's subject.
            Require(!new TheBodyMovesWhenDriven().Run(Write("2.00,0.00", "0.00,0.00", 200f, moving: false, reflex: "dodge")).Any(),
                "a reflex holding the body was charged to the route");
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// The two rules of the stillness and motion measures that the capture they are pinned on cannot
    /// exercise: a gap in the ticks ends a still run and refuses a pair, and so does a dead or downed
    /// row. That capture holds 5,019 consecutive ticks, so a measure that ignored gaps reproduces
    /// every pin, and only a fixture with a gap in it can tell.
    ///
    /// Every assertion is on a number that moves when a rule is dropped, not on a number that merely
    /// exists. Ignoring the gap joins two five-tick stretches into a second run; ignoring death lets
    /// the dead rows extend the last stretch, puts their 500 px distance at the p90 rank, and adds two
    /// right-angle turns to a set that otherwise holds one straight pair.
    /// </summary>
    private static void StillnessAndMotionPairsBreakAtAGapAndAtDeath()
    {
        string file = Path.GetTempFileName();
        try
        {
            var text = new StringBuilder("# text_columns=state,npc_px,npc_vel,player_px,player_vel,control_source,desired_vel\n");
            text.AppendLine("tick\tstate\tnpc_px\tnpc_vel\tplayer_px\tplayer_vel\tcontrol_source\tdesired_vel");
            void Row(long tick, string state, string velocity, string source = "hold")
                => text.AppendLine($"{tick}\t{state}\t0,0\t{velocity}\t{(state == "up" ? "30.00,40.00" : "300.00,400.00")}\t3.00,0.00\t{source}\t0.00,0.00");
            for (long t = 0; t <= 11; t++) Row(t, "up", "0.00,0.00");            // the one real run, twelve ticks
            Row(12, "up", "2.00,0.00");
            for (long t = 13; t <= 17; t++) Row(t, "up", "0.00,0.00");           // five, then ticks 18..29 are missing
            for (long t = 30; t <= 34; t++) Row(t, "up", "0.00,0.00");           // five more across the gap
            for (long t = 35; t <= 39; t++) Row(t, "player-dead", "0.00,0.00");  // still, but dead
            for (long t = 40; t <= 44; t++) Row(t, "up", "0.00,0.00", "combat-spacing");
            Row(50, "up", "2.00,0.00");
            Row(51, "up", "2.00,0.00");                                          // one straight pair
            Row(60, "up", "0.00,2.00");                                          // a right angle, but across a gap
            Row(61, "downed", "-2.00,0.00");
            Row(62, "up", "0.00,-2.00");
            File.WriteAllText(file, text.ToString());
            Session session = Session.Load(file);

            var rows = new IMeasure[] { new MeasureStillness(), new MeasureDistanceWhilePlayerMoves(), new MeasureMotionSmoothness(), new MeasureSafetyShare() }
                .SelectMany(measure => measure.Rows(session)).ToDictionary(row => row.Case, StringComparer.Ordinal);
            double Value(string name) => rows.TryGetValue(name, out var row) && row.Value is { } v ? v : double.NaN;

            Require(Value("stillness/runs") == 1 && Value("stillness/longest-run") == 12,
                $"expected one still run of twelve ticks and found {Value("stillness/runs")} with the longest {Value("stillness/longest-run")} — "
                + "a gap in the ticks or a dead row is being counted as part of a stretch of stillness");
            // 32 alive rows with the player moving; the orb is still on 27 of them.
            Require(Math.Abs(Value("stillness/share-still-while-player-moves") - 100.0 * 27 / 32) < 1e-9 && Value("stillness/player-moving-ticks") == 32,
                $"the still-while-moving share read {Value("stillness/share-still-while-player-moves")} over {Value("stillness/player-moving-ticks")} ticks; a dead row is in the denominator");
            Require(Value("stillness/still-by-owner/hold") == 22 && Value("stillness/still-by-owner/combat-spacing") == 5,
                "still ticks were charged to the wrong control source");
            Require(Value("distance/while-player-moves-p90") == 50,
                $"the p90 distance read {Value("distance/while-player-moves-p90")} where every alive row is 50 px; a dead row's distance reached the percentile");
            // The count is read off the p90 row, whose message names it as "over N pairs"; a pair across
            // the gap or either side of the downing would make it three and put a right angle at p99.
            Require(Value("smoothness/heading-change-p99") == 0 && rows["smoothness/heading-change-p90"].Message!.Contains("over 1 pairs", StringComparison.Ordinal),
                $"the heading set is not the one straight pair: {rows["smoothness/heading-change-p90"].Message} — a pair across a gap or a downing was measured as a turn");
            Require(rows["smoothness/speed-change-p90"].Message!.Contains("over 3 pairs", StringComparison.Ordinal),
                $"the speed-change set should hold the two changes around tick 12 and the straight pair: {rows["smoothness/speed-change-p90"].Message}");
            Require(Math.Abs(Value("safety/share-of-ticks/combat-spacing") - 100.0 * 5 / 32) < 1e-9 && Value("safety/share-of-ticks/survival-escape") == 0,
                "the safety shares are not over alive ticks, or a response with no ticks did not file its zero");
        }
        finally { File.Delete(file); }
    }

    private static void DecisionContractsDistinguishStallsFromProgress()
    {
        string file = Path.GetTempFileName();
        try
        {
            var row = new System.Collections.Generic.Dictionary<string, string>
            {
                ["tick"] = "0", ["wall_elapsed_ms"] = "0", ["action"] = "walk-with", ["request"] = "WithPlayer",
                ["brain_fresh"] = "1", ["recovery_active"] = "0", ["follow_objective_valid"] = "0",
                // The destination tile's centre is 31*16+8, 79*16+8 = 504,1272, and the body sits at
                // 500,1280 — inside the twelve-pixel arrival radius, so the older-schema branch of the
                // contradiction agrees with the recorded Arrived rather than contradicting it.
                ["spot"] = "31,79", ["route_points"] = "0", ["control"] = "desired=0.00,0.00",
                ["npc_px"] = "500,1280", ["desired_vel"] = "0.00,0.00",
                ["follow_reason"] = "follow-horizontal-gap", ["nav_status"] = "Arrived", ["fire"] = "no-arc",
                ["control_source"] = "travel", ["liquid"] = "water", ["hurting"] = "1", ["attack_value"] = "25",
                ["weapon"] = "bow", ["exp_bow"] = "5", ["exp_knife"] = "50"
            };
            Session Write(bool moving = false)
            {
                var text = new StringBuilder("# text_columns=action,request,spot,control,follow_reason,nav_status,fire,control_source,weapon,npc_px,desired_vel,liquid\n");
                text.AppendLine(string.Join('\t', row.Keys));
                for (int i = 0; i < 130; i++)
                {
                    row["tick"] = i.ToString(); row["wall_elapsed_ms"] = (i * 16).ToString();
                    row["npc_px"] = $"{500 + (moving ? i : 0)},1280";
                    text.AppendLine(string.Join('\t', row.Values));
                }
                File.WriteAllText(file, text.ToString()); return Session.Load(file);
            }
            Session stalled = Write();
            Require(new ArrivalDoesNotStrandFollowing().Run(stalled).Any(), "arrival contradiction was missed");
            Require(!new ColumnsHoldWhatTheyClaim().Run(stalled).Any(), "declared text columns were called malformed numbers");
            Require(!new TheChosenWeaponIsTheBetterOne().Run(stalled).Any(), "outcome-aware attack was judged by damage alone");
            Require(MultiRunReport.Of(new[] { file }).Contains("destination while following remained unsatisfied"), "multi-run output omitted its definitive findings");
            row["action"] = "keep-company";
            Require(new ArrivalDoesNotStrandFollowing().Run(Write()).Any(), "new companionship reunion lost arrival diagnosis");
            row["request"] = "Hold";
            Require(!new ArrivalDoesNotStrandFollowing().Run(Write()).Any(), "resting company was mistaken for failed reunion");
            row["request"] = "WithPlayer";
            row["recovery_active"] = "1";
            Require(!new ArrivalDoesNotStrandFollowing().Run(Write()).Any(), "flight was called a normal arrival failure");
            row["recovery_active"] = "0"; row["action"] = "hunt";
            Require(new HuntingProducesAnOutcome().Run(Write()).Any(), "ineffective hunt was missed");
            row["action"] = "combat";
            Require(new HuntingProducesAnOutcome().Run(Write()).Any(), "ineffective combat was missed");
            row["action"] = "hunt";
            Require(!new HuntingProducesAnOutcome().Run(Write(moving: true)).Any(), "travelling hunt was called stalled");
            row["fire"] = "cooldown";
            Require(!new HuntingProducesAnOutcome().Run(Write()).Any(), "weapon cooldown was called an ineffective hunt");
        }
        finally { File.Delete(file); }
    }

    private static void ObservedTransitionsStayChronological()
    {
        string file = Path.GetTempFileName();
        try
        {
            const string phase = "in_ai_after_contact_before_engine_move";
            File.WriteAllText(file, "# schema=0.34.0\n# started_utc=2026-09-09T18:25:59.0000000Z\n"
                + "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tliquid\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n"
                + $"10\t0\t{phase}\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-32,0\t0,0\t100\tdry\tdesired=0.00,0.00\ttravel\tup\twalk-with\twith-player\t-\n"
                + $"11\t16\t{phase}\t8,-4\t2,0\t1\twater\t100\t-\t-\talive\tmove\tslope-lower-right\t-24,0\t1,0\t100\twater\tdesired=1.00,0.00\ttravel\tup\twalk-with\twith-player\t0,0\n"
                + $"12\t33\t{phase}\t16,-8\t2,0\t1\twater\t90\tdamage=10;source=npc:Zombie;direction=1;knockback=4.00\tdamage=10;direction=-1;knockback=3.00;source=unrecorded\talive\tattack\tslope-lower-right\t-16,0\t2,0\t90\twater\tdesired=2.00,0.00\tseeking-destination\tup\tguard\tguard\t0,0\n");
            string report = Chronicle.Of(Session.Load(file), full: true);
            Require(report.Contains("schema 0.34.0"), "metadata was not read");
            Require(report.Contains($"samples are {phase}"), "sample phase was not preserved");
            Require(report.Contains("player entered water"), "liquid transition missing");
            Require(report.Contains("companion entered water"), "the companion's own liquid transition, which is what a body hurt on contact turns on, was missing");
            Require(report.Contains("player ascended a slope"), "bounded slope inference missing");
            Require(report.Contains("player hit event damage=10;source=npc:Zombie"), "exact hook event missing");
            Require(report.Contains("companion hit event damage=10;direction=-1;knockback=3.00;source=unrecorded"), "companion hit event missing");
            Require(report.IndexOf("player entered water", StringComparison.Ordinal) < report.IndexOf("player hit event", StringComparison.Ordinal), "events lost their time order");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void OldRecordsDeclareChronologyMissing()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "tick\tstate\n10\tup\n");
            string report = Chronicle.Of(Session.Load(file), full: false);
            Require(report.StartsWith("chronology  unavailable", StringComparison.Ordinal), "old record was treated as a clean chronology");
            Require(report.Contains("wall_elapsed_ms"), "coverage omission did not name the missing columns");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void NonMonotonicWallTimeIsRejected()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tliquid\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n"
                + "1\t20\tin_ai_after_contact_before_engine_move\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t0,0\t0,0\t100\tdry\tdesired=0.00,0.00\tidle\tup\twander\thold\t-\n"
                + "2\t10\tin_ai_after_contact_before_engine_move\t1,0\t1,0\t1\twater\t100\t-\t-\talive\tmove\tslope-lower-left\t1,0\t1,0\t100\tdry\tdesired=1.00,0.00\ttravel\tup\twander\thold\t-\n");
            string report = Chronicle.Of(Session.Load(file), full: false);
            Require(report.Contains("not monotonic"), "out-of-order wall time produced a false chronological account");
            Require(!report.Contains("player entered water"), "events leaked after chronology was rejected");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void AHoldIsNotCalledHesitationAndProgressSurvivesIt()
    {
        string file = Path.GetTempFileName();
        try
        {
            const string phase = "in_ai_after_contact_before_engine_move";
            File.WriteAllText(file, "# schema=0.34.0\n# started_utc=2026-09-09T18:25:59.0000000Z\n"
                + "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tliquid\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n"
                + $"1\t0\t{phase}\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-30,0\t0,0\t100\tdry\tdesired=0.00,0.00\ttravel\tup\twalk-with\twith-player\t0,0\n"
                + $"2\t20\t{phase}\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-20,0\t1,0\t100\tdry\tdesired=1.00,0.00\ttravel\tup\twalk-with\twith-player\t0,0\n"
                + $"3\t40\t{phase}\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-10,0\t1,0\t100\tdry\tdesired=1.00,0.00\ttravel\tup\twalk-with\twith-player\t0,0\n"
                + $"4\t60\t{phase}\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-10,0\t0,0\t100\tdry\tdesired=0.00,0.00\ttravel\tup\twalk-with\twith-player\t0,0\n");
            string report = Chronicle.Of(Session.Load(file), full: true);
            // Tile 0,0's centre is 8,8, so a body at -20,0 is 29.1 px away and at -10,0 is 19.7. The
            // numbers are written out because they are what pins the tile-centre convention: under the
            // walker's floor conversion the same rows read 32.2 and 24.1.
            Require(report.Contains("net progress toward its recorded spot: 29.1->19.7 px"), "successful approach must convert the recorded destination tile to its centre in world pixels");
            Require(!report.Contains("hesitation", StringComparison.OrdinalIgnoreCase), "a neutral hold was mislabelled as failure");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void FollowingDiagnosisUsesNavigatorProgressRatherThanDistance()
    {
        string file = Path.GetTempFileName();
        try
        {
            // `route_remaining_px` sits beside the tick estimate deliberately: every fixture below
            // that must fire holds it constant, so the firing is earned by the absence of both
            // progress signals rather than by the one the walker happened to have.
            const string header = "tick\trequest\tfollow_objective_valid\tfollow_dx\tfollow_dy\tfollow_reason\troute_search_id\troute_attempt_id\troute_remaining_ticks\troute_remaining_px\troute_index\taction\trecovery_active\tnpc_px\tplayer_px\tplayer_vel\twall_elapsed_ms\tbrain_fresh\n";
            var rows = new StringBuilder(header);
            for (int tick = 0; tick <= 120; tick++)
            {
                // The companion first walks away around a C-turn, so the Euclidean gap grows.
                // Its route identity stays stable while completed steps rise and ETA falls.
                rows.Append(tick).Append("\tWithPlayer\t0\t").Append(100 + tick).Append("\t0\tC-turn\t7\t11\t")
                    .Append(240 - tick).Append("\t400\t").Append(tick / 30).Append("\twalk-with\t0\t")
                    .Append(tick).Append(",0\t0,0\t1,0\t").Append(tick * 16).Append('\n');
            }
            File.WriteAllText(file, WithFreshDecisions(rows));
            Session session = Session.Load(file);
            Require(!new FollowingMakesRouteProgress().Run(session).Any(), "a progressing C-turn was labelled as an unsatisfied follow failure");
            File.WriteAllText(file, "tick\tnpc_px\tplayer_px\tplayer_vel\taction\twall_elapsed_ms\tbrain_fresh\n"
                + "0\t0,0\t900,0\t1,0\twalk-with\t0\t1\n"
                + "1\t1,0\t901,0\t1,0\twalk-with\t17\t1\n"
                + "2\t2,0\t902,0\t1,0\twalk-with\t34\t1\n");
            Require(new FollowingRespondsAfterDeparture().Run(Session.Load(file)).Count() == 1,
                "one distant following episode must not emit a fresh latency report every frame");

            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 120; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t").Append(100 + tick).Append("\t0\twrong-floor\t7\t11\t240\t400\t0\twalk-with\t0\t")
                    .Append(tick).Append(",0\t0,0\t1,0\t").Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows));
            string finding = new FollowingMakesRouteProgress().Run(Session.Load(file)).Single().Title;
            Require(finding.Contains("wrong-direction or wrong-floor", StringComparison.Ordinal), "moving away without route progress did not retain its wrong-floor diagnosis");
            File.WriteAllText(file, WithFreshDecisions(rows).Replace("walk-with", "keep-company"));
            Require(new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(), "new companionship reunion lost route-progress diagnosis");
            File.WriteAllText(file, WithFreshDecisions(rows).Replace("walk-with", "keep-company").Replace("WithPlayer", "Hold"));
            Require(!new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(), "resting company was called a stalled follow route");

            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 240; tick++)
            {
                int completed = tick < 10 ? tick : 10;
                rows.Append(tick).Append("\tWithPlayer\t0\t").Append(100 + tick).Append("\t0\twrong-floor\t7\t11\t")
                    .Append(240 - Math.Min(tick, 10)).Append("\t400\t").Append(completed).Append("\twalk-with\t0\t")
                    .Append(tick).Append(",0\t0,0\t1,0\t").Append(tick * 16).Append('\n');
            }
            File.WriteAllText(file, WithFreshDecisions(rows));
            Finding delayed = new FollowingMakesRouteProgress().Run(Session.Load(file)).Single();
            Require(delayed.FirstTick == 10 && delayed.Rows == 231, "one early completed step hid the later prolonged no-progress follow window");

            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 240; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t100\t0\trecovery\t7\t11\t240\t400\t0\twalk-with\t1\t")
                    .Append(tick).Append(",0\t0,0\t1,0\t").Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows));
            Require(!new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(), "recovery flight inherited stale WithPlayer/walk-with state as an ordinary follow failure");

            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 240; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t900\t0\twrong-floor\t7\t11\t240\t400\t0\twalk-with\t0\t0,0\t900,0\t1,0\t")
                    .Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows, fresh: false));
            Require(!new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(),
                "sticky follow fields during downing must not become a new follow-stall diagnosis");
            Require(!new FollowingRespondsAfterDeparture().Run(Session.Load(file)).Any(f => f.Title.Contains("was first selected", StringComparison.Ordinal)),
                "sticky downed action fields must not count as a fresh follow response");

            // A straight flight across open space. Route smoothing skips every raw corner between
            // the ends into one segment, so `route_index` cannot move for the whole crossing while
            // the body closes 4 px a tick — the exact record an orb flying at the player writes,
            // and a false positive for any rule that reads only the index.
            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 130; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t").Append(800 - tick * 4).Append("\t0\ttoo-far\t7\t11\t240\t")
                    .Append(800 - tick * 4).Append("\t0\twalk-with\t0\t").Append(tick * 4).Append(",0\t800,0\t0,0\t")
                    .Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows));
            Require(!new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(),
                "a body closing along one smoothed segment was called a follow that made no route progress");

            // The mirror, and the reason the search identity guards the pixel rule: the body never
            // moves, and the only fall in remaining distance is a replan at tick 60 adopting a
            // shorter route. Crediting that would read a planner's arithmetic as travel.
            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 130; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t800\t0\twrong-floor\t").Append(tick < 60 ? 7 : 8)
                    .Append("\t11\t240\t").Append(tick < 60 ? 800 : 400).Append("\t0\twalk-with\t0\t0,0\t800,0\t0,0\t")
                    .Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows));
            Require(new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(),
                "a replan shortening the route was credited as the body having travelled");

            static string WithFreshDecisions(StringBuilder source, bool fresh = true)
            {
                string[] lines = source.ToString().TrimEnd('\n').Split('\n');
                return lines[0] + "\n" + string.Join("\n", lines.Skip(1).Select(line => line + (fresh ? "\t1" : "\t0"))) + "\n";
            }
        }
        finally { File.Delete(file); }
    }

    private static void SustainedRequestedMovementWithoutObservedProgressIsReported()
    {
        string file = Path.GetTempFileName();
        try
        {
            var trace = new StringBuilder();
            trace.Append("tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tliquid\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n");
            for (int tick = 0; tick <= 120; tick++)
            {
                // The body does not move for the whole run while the motor is asked for 4 px a tick
                // toward a fixed destination, which is the shape the inference exists to name. There is
                // one position now, so there is no second body for this fixture to disagree with.
                trace.Append(tick).Append('\t').Append(tick * 16).Append("\tin_ai_after_contact_before_engine_move\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t")
                    .Append("-20,0\t1,0\t100\tdry\tdesired=4.00,0.00\ttravel\tup\twalk-with\twith-player\t100,0\n");
            }
            File.WriteAllText(file, trace.ToString());
            string report = Chronicle.Of(Session.Load(file), full: true);
            Require(report.Contains("inferred lack of progress", StringComparison.Ordinal), "sustained movement request without body movement was not reported");
            Require(report.Contains("ticks 0..120", StringComparison.Ordinal), "lack-of-progress evidence did not preserve its tick interval");
            Require(!report.Contains("net progress toward its recorded spot", StringComparison.Ordinal), "a body that never moved was credited with progress");
            foreach (string owner in new[] { "combat-reflex", "reflex", "survival-escape", "follow-recovery-flight", "travel-recovery-clearance", "unrecognised-owner" })
            {
                File.WriteAllText(file, trace.ToString().Replace("\ttravel\t", $"\t{owner}\t", StringComparison.Ordinal));
                Require(!Chronicle.Of(Session.Load(file), full: true).Contains("inferred lack of progress", StringComparison.Ordinal),
                    $"{owner} movement was charged to the retained ordinary activity");
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void DowningDoesNotProveAvoidability()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "tick\tstate\n0\tup\n120\tdowned\n240\tdowned\n");
            Finding[] findings = new TheCompanionStaysUp().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 2 && findings.All(f => f.Severity != Severity.Definitive),
                "a downing observation was promoted to proof of avoidable failure");
            Require(findings.Any(f => f.Severity == Severity.Potential && f.FirstTick == 120),
                "the downing transition must remain visible for investigation");
            Require(findings.All(f => !f.Detail.Contains("half-minute", StringComparison.Ordinal)),
                "sparse samples were presented as thirty seconds of observed context");
        }
        finally { File.Delete(file); }
    }

    /// <summary>The columns a 0.35.0 capture carries for the two player-reference checks, and one row of them.</summary>
    private static readonly string[] ReferenceColumns =
    {
        "tick", "action", "torch_reference", "torch_reference_light", "torch_reference_dark", "torch_reference_stage",
        "hunt_offer", "near_threat", "player_vel", "hunt_raw", "hunt_fin", "hunt_time", "keep-company_fin", "place-torches_funnel", "hunt_funnel",
    };

    private static string ReferenceCapture(int rows, Func<int, System.Collections.Generic.Dictionary<string, string>> row)
    {
        string file = Path.GetTempFileName();
        var text = new StringBuilder("# schema=0.35.0\n# text_columns=action,torch_reference,torch_reference_dark,torch_reference_stage,hunt_offer,player_vel,place-torches_funnel,hunt_funnel\n")
            .Append(string.Join('\t', ReferenceColumns)).Append('\n');
        for (int tick = 0; tick < rows; tick++)
        {
            var cells = row(tick);
            cells["tick"] = tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
            text.Append(string.Join('\t', ReferenceColumns.Select(c => cells.TryGetValue(c, out string? v) ? v : "-"))).Append('\n');
        }
        File.WriteAllText(file, text.ToString());
        return file;
    }

    private static System.Collections.Generic.Dictionary<string, string> Quiet() => new()
    {
        ["action"] = "keep-company", ["torch_reference"] = "-", ["torch_reference_light"] = "-", ["torch_reference_dark"] = "-",
        ["torch_reference_stage"] = "cursor-offers-nothing", ["hunt_offer"] = "NoOpportunity:no-eligible-target", ["near_threat"] = "-",
        ["player_vel"] = "0.00,0.00", ["hunt_raw"] = "0.00", ["hunt_fin"] = "0.00", ["hunt_time"] = "1.000", ["keep-company_fin"] = "0.30",
        ["place-torches_funnel"] = "lit", ["hunt_funnel"] = "-",
    };

    /// <summary>
    /// Both player-reference checks run through the report's own evaluation, so a check dropped from the registry turns this
    /// red as surely as a check that stopped finding. Each finds its stretch on the capture built for it and names what it
    /// claims — lighting's refusing stage, hunting's factors — and neither fires on the cases that differ in the one fact
    /// its rule turns on: a lit tile, a stretch too short, the companion lighting, a tile that keeps moving; a hostile too far,
    /// a player walking, a hunt that was not usable.
    /// </summary>
    private static void ThePlayersReferenceChecksFindWhatTheyClaim()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            string Capture(int rows, Func<int, System.Collections.Generic.Dictionary<string, string>> row)
            {
                string file = ReferenceCapture(rows, row);
                files.Add(file);
                return file;
            }
            System.Collections.Generic.Dictionary<string, string> DarkTile(int tick, string dark = "dark", string action = "keep-company", string tile = "21,59")
            {
                var cells = Quiet();
                cells["action"] = action; cells["torch_reference"] = tile; cells["torch_reference_light"] = "0.050";
                cells["torch_reference_dark"] = dark; cells["torch_reference_stage"] = "stand-unreachable";
                return cells;
            }
            System.Collections.Generic.Dictionary<string, string> NearHunt(int tick, string near = "12.0", string velocity = "0.10,0.00", string offer = "Usable:reachable-firing-position")
            {
                var cells = Quiet();
                cells["hunt_offer"] = offer; cells["near_threat"] = near; cells["player_vel"] = velocity;
                cells["hunt_raw"] = "0.55"; cells["hunt_fin"] = "0.21"; cells["hunt_time"] = "0.420"; cells["hunt_funnel"] = "offered";
                return cells;
            }

            string torchName = new TorchesGoWhereHisCursorWould().Name, huntName = new HuntsWorthTakingAreTaken().Name;
            var (torchFindings, _, _) = Program.Evaluate(Session.Load(Capture(300, t => DarkTile(t))));
            Finding? unlit = torchFindings.FirstOrDefault(f => f.Check == torchName);
            Require(unlit != null && unlit.Detail.Contains("stand-unreachable", StringComparison.Ordinal) && unlit.Rows == 300,
                "a dark tile his cursor offered for five seconds while keeping company won was not reported, or lost its refusing stage");
            foreach (var (label, file) in new[]
            {
                ("a lit tile", Capture(300, t => DarkTile(t, dark: "lit"))),
                ("a tile only a carried light outshines", Capture(300, t => DarkTile(t, dark: "carried"))),
                ("a tile the sky lights", Capture(300, t => DarkTile(t, dark: "sky"))),
                ("two seconds", Capture(120, t => DarkTile(t))),
                ("the companion lighting", Capture(300, t => DarkTile(t, action: "place-torches"))),
                ("a tile that keeps moving", Capture(300, t => DarkTile(t, tile: $"{21 + t / 60},59"))),
            })
                Require(!new TorchesGoWhereHisCursorWould().Run(Session.Load(file)).Any(), $"the cursor check fired on {label}");

            var (huntFindings, _, _) = Program.Evaluate(Session.Load(Capture(300, t => NearHunt(t))));
            Finding? idle = huntFindings.FirstOrDefault(f => f.Check == huntName);
            Require(idle != null && idle.Detail.Contains("hunt_time 0.420", StringComparison.Ordinal) && idle.Detail.Contains("hunt_fin", StringComparison.Ordinal),
                "a usable hunt near an idle player while keeping company won was not reported with hunting's factors");
            string combatFile = Path.GetTempFileName(); files.Add(combatFile);
            var combatTrace = new StringBuilder("# schema=0.39.0\n# text_columns=action,combat_offer,player_vel,combat_funnel\n"
                + "tick\taction\tcombat_offer\tnear_threat\tplayer_vel\tcombat_raw\tcombat_fin\tcombat_time\tkeep-company_fin\tcombat_funnel\n");
            for (int tick = 0; tick < 300; tick++)
                combatTrace.AppendLine($"{tick}\tkeep-company\tUsable:reachable-firing-position\t12.0\t0.10,0.00\t0.55\t0.21\t0.420\t0.30\toffered");
            File.WriteAllText(combatFile, combatTrace.ToString());
            Finding? combatIdle = new HuntsWorthTakingAreTaken().Run(Session.Load(combatFile)).FirstOrDefault();
            Require(combatIdle != null && combatIdle.Detail.Contains("combat_time 0.420", StringComparison.Ordinal) && combatIdle.Detail.Contains("combat_fin", StringComparison.Ordinal),
                "a usable fight near an idle player under the merged stance's columns was not reported with combat's factors");
            foreach (var (label, file) in new[]
            {
                ("a hostile thirty tiles away", Capture(300, t => NearHunt(t, near: "30.0"))),
                ("a walking player", Capture(300, t => NearHunt(t, velocity: "3.00,0.00"))),
                ("a hunt that was not usable", Capture(300, t => NearHunt(t, offer: "Unresolved:firing-position-undecided"))),
            })
                Require(!new HuntsWorthTakingAreTaken().Run(Session.Load(file)).Any(), $"the hunt check fired on {label}");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>The description names each activity's funnel with the share of rows its furthest candidate stopped at each stage.</summary>
    private static void TheFunnelSectionTalliesEachActivity()
    {
        string file = ReferenceCapture(200, t =>
        {
            var cells = Quiet();
            cells["place-torches_funnel"] = t < 150 ? "stand-unreachable" : "offered";
            return cells;
        });
        try
        {
            string description = DescribeSession.Of(Session.Load(file));
            Require(description.Contains("funnel    place-torches: stand-unreachable 75.0%  offered 25.0%", StringComparison.Ordinal),
                $"the description did not tally lighting's funnel by stage: {description}");
            Require(description.Contains("funnel    hunt: - 100.0%", StringComparison.Ordinal),
                $"the description skipped an activity whose funnel looked at nothing: {description}");
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// The check that could not run between schema 0.40.0 and 0.45.0, on the evidence it reads now.
    ///
    /// <para>It used to parse the arsenal's bounded shortlist of rejected weapon-target pairs out of
    /// `target_evidence`, and the weapon block's rename took that column away, so it has been a named
    /// skip on every capture since and the shortlist it parsed is written nowhere. 0.45.0 gives the two
    /// names back with the course's meaning and the check asks the question the 22 September capture
    /// raised: a fight held while every target the census admitted read as not observed to the binder
    /// that had to use it.</para>
    ///
    /// <para><b>The dangerous case is the old capture, and it is asserted through the runner rather
    /// than through the check.</b> A pre-0.40.0 capture carries both names holding the arsenal's format,
    /// and a predicate written for the new one would parse it confidently as something else. The check
    /// names `frame_ms` in its `Needs` as the witness and never reads it, so such a capture reaches the
    /// coverage block as a named skip; asking the check directly would prove nothing about that, which
    /// is why the last case here goes through `Program.Evaluate`.</para>
    /// </summary>
    private static void CombatHoldingUnobservableTargetsIsFoundAndOldCapturesSkip()
    {
        string file = Path.GetTempFileName();
        try
        {
            Finding[] Read(string evidence, string age = "combat=40", string fire = "not-fighting",
                string action = "combat", int rows = 301)
            {
                var trace = new StringBuilder("# schema=0.45.0\n"
                    + "tick\taction\tbrain_fresh\tfire\ttarget_evidence_age\ttarget_evidence\tframe_ms\n");
                for (int tick = 0; tick < rows; tick++)
                    trace.AppendLine($"{tick}\t{action}\t1\t{fire}\t{age}\t{evidence}\t25.00");
                File.WriteAllText(file, trace.ToString());
                return new CombatHeldTargetsItsBinderCouldNotSee().Run(Session.Load(file)).ToArray();
            }

            const string unseen = "combat=combat-target:8000001:0:Unresolved:0/3";
            Finding[] findings = Read(unseen);
            Require(findings.Length == 1 && findings[0].Severity == Severity.Potential,
                $"a sustained fight over targets the binder cannot see must be one potential finding; got {findings.Length}");
            Require(findings[0].Detail.Contains("not `Observed`", StringComparison.Ordinal)
                    && findings[0].Detail.Contains("-1 is a fact key this recorder never saw observed", StringComparison.Ordinal),
                $"the finding must say what the evidence and the age mean: {findings[0].Detail}");

            // The universal claim is universal. One domain reading Observed beside one that does not is
            // a census and a binder agreeing about something, and the older version of this check made
            // exactly this mistake in the other direction — an existential match read as a universal.
            Require(Read(unseen + "|collect-target=collect-target:7:0:Observed:4/4").Length == 0,
                "one observed domain beside an unobserved one must not read as every target unobserved");

            foreach (string quiet in new[] { "", "-", "Unresolved", "combat=", "combat=k:Unresolved",
                         "combat=k:Unresolved:0/3|", "combat=k:Unresolved:x/3", "7:12:0:0:weapon=1:outside-reach",
                         // An evidence level no producer writes. It was on the accepted list once, which
                         // widened the predicate against a string that can only come from a corrupt cell.
                         "combat=combat-target:8000001:0:no-snapshot:0/3" })
                Require(Read(quiet).Length == 0,
                    $"a dash, an absence or a shape this producer does not write must be refused rather than matched: '{quiet}'");

            // The levels the producer can write, pinned from the producer rather than from this list:
            // every `FactEvidence` member short of Observed, plus the audit's own `absent`.
            foreach (string level in new[] { "Unresolved", "Missing", "Modelled", "absent" })
                Require(Read($"combat=combat-target:8000001:0:{level}:0/3").Length == 1,
                    $"a level the audit writes must be read as unobserved evidence: '{level}'");
            // `FactEvidence` lives in `Selection/`, which SessionReport does not compile, so the members
            // are read out of the producer's own source. A member added there and not here would make
            // this predicate silently refuse a real recording, which is the same class of failure as an
            // accepted value nobody writes and is why both directions are pinned.
            string dependencies = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure",
                "Selection", "Courses", "TrackCourseDependencies.cs"));
            var declaration = Regex.Match(dependencies, @"enum FactEvidence\s*\{(?<members>[^}]*)\}");
            Require(declaration.Success, "FactEvidence is no longer declared where this row reads it from");
            foreach (string member in declaration.Groups["members"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string level = member.Trim();
                if (level.Length == 0 || level == "Observed") continue;
                Require(Read($"combat=combat-target:8000001:0:{level}:0/3").Length == 1,
                    $"the producer writes an evidence level this check refuses: '{level}'");
            }

            Require(Read(unseen, fire: "fired").Length == 0 && Read(unseen, fire: "cooldown").Length == 0,
                "a fight that is firing or reloading is a fight working");
            Require(Read(unseen, action: "keep-company").Length == 0, "keeping company is not a fight held");
            Require(Read(unseen, rows: 299).Length == 0, "a stretch under the inspection threshold must stay quiet");
            Require(Read(unseen, action: "hunt").Length == 1, "the pre-merge stance label must still be read as a fight");

            // The witness. An older capture carries both names holding the arsenal's own format and no
            // `frame_ms`, and must reach the coverage block as a skip rather than this predicate.
            var old = new StringBuilder("# schema=0.39.0\ntick\taction\tbrain_fresh\tfire\ttarget_evidence_age\ttarget_evidence\n");
            for (int tick = 0; tick < 301; tick++)
                old.AppendLine($"{tick}\thunt\t1\tno-target\t0\t7:12:0:0:weapon=1:outside-reach");
            File.WriteAllText(file, old.ToString());
            var (findingsOnOld, skipped, _) = Program.Evaluate(Session.Load(file));
            Require(skipped.Any(s => s.Name == new CombatHeldTargetsItsBinderCouldNotSee().Name),
                "a capture from before the columns changed meaning must skip this check by name, not be parsed by it");
            Require(!findingsOnOld.Any(f => f.Check == new CombatHeldTargetsItsBinderCouldNotSee().Name),
                "the arsenal's own evidence format was read as the course's");
        }
        finally { File.Delete(file); }
    }

    private static void EmptyHeaderOnlySessionIsReadable()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "# schema=0.9.0\n# started_utc=2026-09-09T18:25:59.0000000Z\n"
                + "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tliquid\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n");
            Session session = Session.Load(file);
            Require(session.Count == 0, "header-only session was not retained as an empty session");
            Require(Chronicle.Of(session, full: false).Contains("no samples were written"), "empty session did not explain its lack of chronology");
            Require(DescribeSession.Of(session).Contains("rows      0"), "empty session description indexed a missing row");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void RecorderChronologyContractUsesActualLifeColumn()
    {
        string source = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
        Require(source.Contains("\\tplayer_life\\tplayer_hit\\tnpc_hit\\tplayer_state", StringComparison.Ordinal), "recorder header lost the hit-event sequence consumed by Chronicle");
        Require(source.Contains("\\tdir\\tlife\\tself_danger", StringComparison.Ordinal), "recorder no longer writes the actual companion life column");
        // The body columns Chronicle and the movement checks read, pinned as the sequence the recorder
        // writes them in. A rename here is the failure this file exists to turn into a red: a reader
        // addressing a column the producer stopped writing reports a clean run it never measured.
        Require(source.Contains("\\tnpc_tile\\tnpc_px\\tnpc_vel\\ttouched_wall\\twall_normal\\twet\\tliquid\\tclearance\\tmoved\\tpinned", StringComparison.Ordinal),
            "the recorder's body line lost the centre, velocity, wall contact, liquid, clearance or pinned columns the reader's movement checks are built on");
        Require(source.Contains("\\troute_points\\troute_index\\troute_search_id\\troute_attempt_id\\troute_remaining_ticks\\troute_remaining_px\\tlookahead", StringComparison.Ordinal),
            "the recorder's route line no longer names the points, segment index and remaining length the movement and follow checks read");
        Require(source.Contains("\\tbrain_fresh\\tdesired_vel", StringComparison.Ordinal),
            "the recorder stopped writing the steering's requested velocity, which is the only column that says a stalled body was being driven");
        Require(!source.Contains("\\tbreath\\t", StringComparison.Ordinal) && !source.Contains("\\tdiverge\\t", StringComparison.Ordinal)
                && !source.Contains("\\tedge_n\\t", StringComparison.Ordinal) && !source.Contains("observed_left", StringComparison.Ordinal),
            "a walking-body column came back to the recorder without this reader gaining a check that reads it");
        Require(!source.Contains("npc_life", StringComparison.Ordinal), "recorder contract invented an npc_life column it does not write");
    }

    private static void RecorderCapturesFreshNavigationEvidence()
    {
        string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
        string events = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordGodsEyeEvents.cs"));
        Require(telemetry.Contains("brain.LastTick == Main.GameUpdateCount", StringComparison.Ordinal), "recorder does not distinguish an old brain action from this tick's action");
        Require(telemetry.Contains("RecordNavigationEvidence", StringComparison.Ordinal), "recorder does not sample navigation evidence at the diagnostics boundary");
        Require(events.Contains("search-id=", StringComparison.Ordinal) && events.Contains("attempt-id=", StringComparison.Ordinal)
                && events.Contains("freshness=", StringComparison.Ordinal) && events.Contains("progress=", StringComparison.Ordinal),
            "navigation occurrence omits causal identity, freshness or bounded progress reason");
    }

    private static void RecorderLifecycleAndReservationContractsArePresent()
    {
        string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
        string events = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordGodsEyeEvents.cs"));
        Require(telemetry.Contains("FileMode.CreateNew", StringComparison.Ordinal) && telemetry.Contains("ReserveSessionPath", StringComparison.Ordinal),
            "recorder can still overwrite a same-second run instead of reserving an attempt-specific file");
        Require(telemetry.Contains("WriteMetadata();", StringComparison.Ordinal) && telemetry.Contains("PostUpdateEverything", StringComparison.Ordinal)
                && telemetry.Contains("LoadWorldData(TagCompound tag)", StringComparison.Ordinal) && telemetry.Contains("SaveWorldData(TagCompound tag)", StringComparison.Ordinal),
            "zero-tick metadata or lifecycle callback evidence is missing from the recorder contract");
        Require(telemetry.Contains("RecordLifecycle", StringComparison.Ordinal) && telemetry.Contains("outer-load=unobservable", StringComparison.Ordinal),
            "lifecycle evidence no longer states the boundary between this callback and Terraria's outer load");

        // The preamble's world and capability lines, pinned as producer literals because nothing in
        // this suite executes them. No fixture opens a recorder session — they reach in and call
        // Close — so WriteMetadata never runs headless, and a green suite says nothing about
        // whether these two lines are written. What a pin does catch is the likelier failure by far:
        // somebody renaming or dropping a line that a reader downstream is about to depend on.
        // What it cannot catch is the behaviour, and that is a real gap, closed only by a playtest
        // or by a fixture that opens a session.
        Require(telemetry.Contains("# capabilities=", StringComparison.Ordinal) && telemetry.Contains("# world=", StringComparison.Ordinal),
            "the capture no longer declares which world and which movement kits produced it, so an old capture replayed against a mined-through world reads as a regression in everything");
        Require(telemetry.Contains("MovementCapabilities.Basic", StringComparison.Ordinal) && telemetry.Contains("rocketBoots", StringComparison.Ordinal),
            "the capability line no longer reads both kits from their own sources, and an inferred ability is a heuristic running underneath the thing being measured");
        Require(!telemetry.Contains("identity.GetHashCode()", StringComparison.Ordinal) && telemetry.Contains("14695981039346656037UL", StringComparison.Ordinal),
            "the world hash is no longer FNV-1a: string.GetHashCode() is randomised per process, so a hash taken from it differs between two captures of one world and agrees with nothing, including itself tomorrow");
    }

    private static void EventSiblingReportsCountsAndCorruption()
    {
        string file = Path.GetTempFileName();
        string events = Path.ChangeExtension(file, null) + "-events.jsonl";
        try
        {
            File.WriteAllText(events, "{\"v\":1,\"seq\":0,\"tick\":0,\"wall_elapsed_ms\":0,\"kind\":\"session\",\"subject\":0,\"related\":\"\",\"label\":\"\",\"channel\":\"\",\"pos_x\":0,\"pos_y\":0,\"vel_x\":0,\"vel_y\":0,\"expected_x\":0,\"expected_y\":0,\"amount\":0,\"detail\":\"\"}\n{\"v\":1,\"seq\":1,\"tick\":7,\"wall_elapsed_ms\":12.5,\"kind\":\"shot\",\"subject\":1,\"related\":\"\",\"label\":\"bow\",\"channel\":\"\",\"pos_x\":1,\"pos_y\":2,\"vel_x\":0,\"vel_y\":0,\"expected_x\":3,\"expected_y\":4,\"amount\":1,\"detail\":\"\"}\nnot-json\n");
            string report = DescribeGodsEyeEvents.Of(file);
            Require(report.Contains("1 occurrence record"), "event sibling did not count the valid occurrence");
            Require(report.Contains("1 malformed line"), "event sibling silently accepted corrupt JSONL");
            Require(report.Contains("end=missing"), "an interrupted event stream must not claim normal closure");
            var lines = new System.Collections.Generic.List<string>();
            void Add(string kind, int subject = 0, string channel = "", double wall = 0, string related = "", string detail = "test", string label = "knife")
                => lines.Add(System.Text.Json.JsonSerializer.Serialize(new {
                    v = 1, seq = lines.Count, tick = lines.Count, wall_elapsed_ms = wall, kind, subject, related,
                    label, channel, pos_x = 5, pos_y = 6, vel_x = 2, vel_y = -1,
                    expected_x = 20, expected_y = 30, amount = 0, detail }));
            Add("session");
            Add("lifecycle", detail: "observed=ModSystem.OnWorldLoad;outer-load=unobservable", label: "world-entry");
            Add("navigation-state", 1, "WithPlayer", 50, detail: "freshness=stale-or-not-executed;controls=move=3.50;search-id=9;attempt-id=11;search-pending=False;search-expansions=22;progress=idle;experience-routes=0");
            Add("shot", 1, "projectile=1000001", 100, "enemy-1");
            Add("projectile-terrain-hit", 1000001, wall: 200);
            Add("shot", 1, "projectile=1000002", 300, "enemy-2");
            Add("projectile-enemy-hit", 1000002, wall: 400, related: "enemy-2");
            Add("projectile-terrain-hit", 1000002, wall: 500);
            Add("tool-effect", 1, "attempt=4", 550, detail: "effect=NoObservedChange;before-damage=0;after-damage=0;yield=unobserved", label: "pickaxe");
            Add("activity-state", 1, "Suspended", 560, detail: "activity-id=2;phase=Suspended;reason=projectile", label: "mine");
            Add("attempt-outcome", 1, "Interrupted", 561, detail: "attempt-id=3;activity-id=2;status=Interrupted;cause=projectile;productive-effects=1", label: "mine");
            Add("attempt-outcome", 1, "Complete:Shared", 562, detail: "attempt-id=4;activity-id=2;status=Complete;attribution=Shared;cause=tracked-vein-observed-clear;productive-effects=2", label: "mine");
            Add("attempt-outcome", 1, "Complete:Companion", 563, detail: "attempt-id=5;activity-id=3;status=Complete;attribution=Companion;cause=tracked-vein-observed-clear;productive-effects=4", label: "mine");
            Add("method-assessment", 1, "not-established", 570, detail: "choice-id=1;choice-phase=pre-activation;reason=no-arc;native-effect=unobserved", label: "guard");
            Add("method-assessment", 1, "not-established", 580, detail: "choice-id=2;choice-phase=pre-activation;reason=no-arc;native-effect=unobserved", label: "guard");
            Add("method-assessment", 1, "admitted", 590, detail: "choice-id=3;choice-phase=pre-activation;reason=clear-arc;native-effect=unobserved", label: "guard");
            for (int i = 0; i < 30; i++) Add("decision", 1, "WithPlayer", 1000 + i * 30000);
            Add("session-end", wall: 902000);
            File.WriteAllLines(events, lines);
            report = DescribeGodsEyeEvents.Of(file);
            Require(report.Contains("projectile 1000001 intended target enemy-1"), "a reused projectile slot lost its first shot identity");
            Require(!report.Contains("projectile 1000002 intended target enemy-2"), "a piercing shot hitting terrain after an enemy is not a blocked shot");
            Require(report.Contains("00:14:30"), "default causal summary omitted the end of a long run");
            Require(report.Contains("end=normal close"), "normal recorder closure must be visible");
            Require(report.Contains("tool pickaxe, attempt=4; effect=NoObservedChange") && report.Contains("yield=unobserved"),
                "the ordinary report must expose a tool attempt without inventing progress or yield");
            Require(report.Contains("activity mine; activity-id=2;phase=Suspended;reason=projectile"),
                "ordinary report output must expose suspension separately from tool results");
            Require(report.Contains("attempt mine, Interrupted: 1 attempt(s); latest attempt-id=3")
                && report.Contains("attempt mine, Complete:Shared: 1 attempt(s); latest attempt-id=4")
                && report.Contains("attempt mine, Complete:Companion: 1 attempt(s); latest attempt-id=5"),
                "an interrupted attempt must stay visible beside later completions, and a shared completion must not fold into the companion's own");
            Require(report.Contains("method guard, not-established: 2 assessment(s)")
                && report.Contains("method guard, admitted: 1 assessment(s)")
                && report.Contains("choice-id=2;choice-phase=pre-activation;reason=no-arc;native-effect=unobserved"),
                "a later admitted method must not erase earlier rejection evidence in the same summary window");
            Require(report.Contains("lifecycle world-entry: observed=ModSystem.OnWorldLoad;outer-load=unobservable", StringComparison.Ordinal), "lifecycle callback evidence was not surfaced with its outer-load limit");
            Require(report.Contains("freshness=stale-or-not-executed"), "reader discarded freshness that prevents a stale action becoming a fictional stall");
            string full = DescribeGodsEyeEvents.Of(file, true);
            Require(full.Contains("projectile-enemy-hit subject=1000002"), "full event trace discarded native contact details");
            Require(full.Contains("choice-id=1;choice-phase=pre-activation;reason=no-arc"),
                "full event output must preserve assessments omitted from the latest-per-result summary");
            Require(full.Contains("velocity=2.0,-1.0") && full.Contains("channel=projectile=1000001"), "full event trace must retain launch controls and the projectile link");
            File.AppendAllText(events, "{\"v\":1,\"kind\":\"shot\"}\n");
            Require(DescribeGodsEyeEvents.Of(file).Contains("1 malformed line"), "missing occurrence fields must not default to valid zero values");
        }
        finally
        {
            File.Delete(file);
            File.Delete(events);
        }
    }

    private static void MultiRunAndHtmlKeepEverySelectedRun()
    {
        string first = Path.GetTempFileName();
        string second = Path.GetTempFileName();
        string html = Path.Combine(Path.GetTempPath(), $"aic-playtest-{Guid.NewGuid():N}.html");
        const string header = "tick\twall_elapsed_ms\tplayer_px\n";
        try
        {
            File.WriteAllText(first, header + "10\t0\t1,2\n");
            File.WriteAllText(second, header + "20\t100\t3,4\n");
            string multi = MultiRunReport.Of(new[] { first, second });
            Require(multi.Contains(Path.GetFileName(first), StringComparison.Ordinal)
                    && multi.Contains(Path.GetFileName(second), StringComparison.Ordinal),
                "multi-run output omitted a selected session");
            WritePlaytestHtml.Write(html, new[] { first, second });
            string output = File.ReadAllText(html);
            Require(output.Contains("Recorded actor timeline", StringComparison.Ordinal), "HTML timeline did not identify itself");
            Require(output.Contains("Blank/unrecorded terrain and gaps are unknown", StringComparison.Ordinal), "HTML timeline claimed terrain it did not record");
            Require(output.Contains("\"PlayerX\":1", StringComparison.Ordinal) && output.Contains("\"PlayerX\":3", StringComparison.Ordinal),
                "HTML timeline omitted observations from one selected run");
            string sidecar = Path.ChangeExtension(second, null) + "-events.jsonl";
            try
            {
                using (var writer = File.CreateText(sidecar))
                {
                    for (int tick = 0; tick < 6000; tick++)
                        writer.WriteLine($"{{\"kind\":\"projectile-terrain-hit\",\"tick\":{tick},\"wall_elapsed_ms\":{tick}}}");
                    writer.WriteLine("{\"kind\":\"player-damage\",\"tick\":6001,\"wall_elapsed_ms\":6001,\"amount\":17}");
                    writer.WriteLine("{broken");
                }
                WritePlaytestHtml.Write(html, new[] { first, second });
                output = File.ReadAllText(html);
                string data = output.Split("<script id=\"data\" type=\"application/json\">")[1].Split("</script>")[0];
                using var json = System.Text.Json.JsonDocument.Parse(data);
                var run = json.RootElement.GetProperty("Runs")[1];
                Require(run.GetProperty("Events").GetProperty("player-damage")[0].GetProperty("amount").GetInt32() == 17,
                    "late rare damage must survive cosmetic event overflow");
                Require(run.GetProperty("Coverage").GetProperty("Malformed").GetInt32() == 1,
                    "HTML must expose malformed source evidence");
                Require(run.GetProperty("Events").GetProperty("projectile-terrain-hit").GetArrayLength() <= 256,
                    "HTML event retention must have a finite bound");
            }
            finally { File.Delete(sidecar); }
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(html);
        }
    }

    private static void MultiRunRetainsDefinitiveExit()
    {
        string healthy = Path.GetTempFileName(), faulty = Path.GetTempFileName(), empty = Path.GetTempFileName();
        try
        {
            File.WriteAllText(healthy, "tick\n1\n2\n");
            File.WriteAllText(faulty, "tick\n2\n1\n");
            Require(!MultiRunReport.HasDefinitive(new[] { healthy }), "healthy multi-run fixture became definitive");
            Require(MultiRunReport.HasDefinitive(new[] { healthy, faulty }), "multi-run suppressed a definitive ordinary-report fault");
            File.WriteAllText(empty, "");
            Require(MultiRunReport.HasDefinitive(new[] { healthy, empty }), "an unreadable selected capture became a clean multi-run verdict");
            Require(MultiRunReport.Of(new[] { healthy, empty }).Contains("continuous samples=unreadable", StringComparison.Ordinal),
                "multi-run omitted an unreadable selected capture instead of reporting its coverage gap");
        }
        finally { File.Delete(healthy); File.Delete(faulty); File.Delete(empty); }
    }

    private static void MultiRunFolderKeepsFirstAndLastRuns()
    {
        string folder = Path.Combine(Path.GetTempPath(), "aic-session-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string first = Path.Combine(folder, "first.tsv");
            string middle = Path.Combine(folder, "middle.tsv");
            string last = Path.Combine(folder, "last.tsv");
            File.WriteAllText(first, "tick\n1\n");
            File.WriteAllText(middle, "tick\n2\n");
            File.WriteAllText(last, "tick\n3\n");
            string[] selected = Program.ResolveAll(new[] { folder });
            Require(selected.Length == 3 && selected.Contains(first) && selected.Contains(last),
                "multi-run folder resolution discarded the first or last selected capture");
            string report = MultiRunReport.Of(selected);
            Require(report.Contains("first.tsv", StringComparison.Ordinal) && report.Contains("last.tsv", StringComparison.Ordinal),
                "multi-run report omitted the first or last selected capture after folder expansion");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>
    /// A comparison is judged once on its own offer column. The ineligible hunt column beside a
    /// usable mine selection is there so a check reading any offer column fails; the retained second
    /// row is there so a check counting rows rather than comparisons fails.
    /// </summary>
    private static void ASelectedActivityMustHaveCarriedAnEligibleOffer()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            Finding[] Read(string mineOffer, bool firstRowFresh = true, string selected = "mine")
            {
                string file = Path.GetTempFileName(); files.Add(file);
                File.WriteAllText(file, "# schema=0.21.0\n# text_columns=action,mine_offer,hunt_offer\n"
                    + "tick\taction\tchoice_id\tchoice_fresh\tmine_offer\thunt_offer\n"
                    + "0\t-\t0\t1\tnot-compared\tnot-compared\n"
                    + $"1\t{selected}\t1\t{(firstRowFresh ? 1 : 0)}\t{mineOffer}\tKnownUnusable:no-firing-position\n"
                    + $"2\t{selected}\t1\t0\t{mineOffer}\tKnownUnusable:no-firing-position\n"
                    + "3\tmine\t2\t1\tUsable:proven-pose\tKnownUnusable:no-firing-position\n");
                return new SelectedActivitiesHadAnEligibleOffer().Run(Session.Load(file)).ToArray();
            }

            Finding[] contradiction = Read("KnownUnusable:no-mineable-ore");
            Require(contradiction.Length == 1 && contradiction[0].Severity == Severity.Definitive,
                "a selection whose own offer was known-unusable was not exactly one definitive finding");
            Require(contradiction[0].FirstTick == 1 && contradiction[0].LastTick == 2 && contradiction[0].Rows == 2,
                "one comparison's retained rows were not folded into its single contradiction");
            Require(Read("Usable:proven-pose").Length == 0, "another activity's ineligible offer was charged to the selected one");
            Require(Read("Unresolved:approach-undecided").Length == 0, "an unresolved offer, which may carry value, was called a contradiction");
            foreach (string absent in new[] { "Deferred:family-preparation-allowance-spent", "not-compared", "PolicyForbidden:work-disabled", "NoOpportunity:no-ore" })
                Require(Read(absent).Length == 1, $"a selection carrying '{absent}' was not reported");
            Finding[] retainedOnly = Read("KnownUnusable:no-mineable-ore", firstRowFresh: false);
            Require(retainedOnly.Length == 1 && retainedOnly[0].Detail.Contains("no fresh row", StringComparison.Ordinal),
                "a comparison whose fresh row was not captured lost its contradiction or hid that it was read from a retained row");
            Finding[] noColumn = Read("Usable:proven-pose", selected: "guard");
            Require(noColumn.Length == 1 && noColumn[0].Severity == Severity.Potential,
                "a selected activity with no offer column was judged instead of reported as unmeasured");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>
    /// The report prints one line per class of finding with its count, never one per occurrence.
    ///
    /// <para>The 22 September 2026 capture closed on "1089 definitive issue(s)" under a header reading
    /// "DEFINITIVE ISSUES (8)": the header counted folded lines and the closing line counted raw
    /// findings, and the eight real findings of that session were underneath 1,089 repetitions of two.
    /// The three arms here are the three ways that can go wrong — a class not folding, a class folding
    /// that should not, and one check's many classes flooding the report — and the fourth asserts that
    /// the count a folded line carries is the occurrence count rather than the line count.</para>
    /// </summary>
    private static void FindingsFoldToOneLinePerClassCarryingItsCount()
    {
        Finding One(string check, string title, int tick, string? cls = null)
            => new(Severity.Definitive, check, title, "detail", tick, tick, 1, cls);

        var repeated = Enumerable.Range(1, 300).Select(t => One("one check", "the same contradiction", t)).ToArray();
        var folded = Program.Fold(repeated);
        Require(folded.Count == 1, $"300 occurrences of one class printed {folded.Count} line(s) rather than one");
        Require(folded[0].Rows == 300, $"the folded line carried {folded[0].Rows} row(s) rather than the 300 it folded");
        Require(folded[0].FirstTick == 1 && folded[0].LastTick == 300, "the folded line lost the span its occurrences covered");
        Require(folded[0].Title.Contains("300×", StringComparison.Ordinal), "the folded line's title does not carry its count");

        // Two findings of one check whose titles differ only in a number are one class and must not be
        // folded at two, because two paragraphs of real numbers beat one paragraph of a count.
        var pair = Program.Fold(new[]
        {
            One("one check", "projectile type 1 lands a median 21 updates late", 5),
            One("one check", "projectile type 3 lands a median 12 updates late", 9),
        });
        Require(pair.Count == 2, $"two occurrences of one class folded into {pair.Count} line(s) rather than staying two");

        // Digits are masked, so a class differing only in its numbers folds; a name is not a digit, so a
        // class differing in a word does not.
        Require(Program.ClassOf(One("c", "525 ticks with nothing fired", 1)) == Program.ClassOf(One("c", "238 ticks with nothing fired", 1)),
            "two findings differing only in a count were read as two classes");
        Require(Program.ClassOf(One("c", "the selected combat carried a Deferred offer", 1)) != Program.ClassOf(One("c", "the selected collect carried a Deferred offer", 1)),
            "two findings differing in an activity name were read as one class");

        // A declared class overrides the title, which is how a check that fires per tick folds even
        // when its title carries the identity that changed.
        Require(Program.Fold(new[] { One("c", "from combat to keep-company", 1, "the activity changed"),
                                     One("c", "from collect to keep-company", 2, "the activity changed"),
                                     One("c", "from mine to keep-company", 3, "the activity changed"),
                                     One("c", "from chop to keep-company", 4, "the activity changed") }).Count == 1,
            "four occurrences sharing a declared class did not fold to one line");

        // Ten genuinely different classes from one check are capped, so no check can flood the report.
        var many = Enumerable.Range(1, 10).SelectMany(n => Enumerable.Range(0, 4)
            .Select(k => One("one check", $"class {(char)('a' + n)} fired", n * 10 + k))).ToArray();
        var capped = Program.Fold(many);
        Require(capped.Count == 7, $"ten classes from one check printed {capped.Count} line(s) rather than six and a tail");
        Require(capped.Sum(f => f.Rows) == 40, "the capped report lost occurrences rather than counting them");
    }

    private static void ASelectionChangesOnlyWithANewComparison()
    {
        string file = Path.GetTempFileName();
        try
        {
            Finding[] Read(string rows)
            {
                File.WriteAllText(file, "# text_columns=action\ntick\taction\tchoice_id\n" + rows);
                return new ARetainedChoiceKeepsItsSelection().Run(Session.Load(file)).ToArray();
            }
            Finding[] moved = Read("0\t-\t0\n1\tmine\t1\n2\thunt\t1\n");
            Require(moved.Length == 1 && moved[0].Severity == Severity.Definitive && moved[0].FirstTick == 1,
                "a label that changed under one comparison identity was not reported");
            Require(Read("0\t-\t0\n1\tmine\t1\n2\thunt\t2\n").Length == 0, "a label changed by a new comparison was reported");
            Require(Read("1\tmine\t5\n2\t-\t0\n3\t-\t0\n").Length == 0, "a respawned brain restarting its identities was reported");
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// The two checks whose producer the course brain replaced, graded by the schema the capture
    /// declares rather than by the column names, which did not move.
    ///
    /// <para>Both rules restate the family chooser's guarantees, and both were still Definitive on the
    /// 22 September 2026 capture: 875 findings that a selected keep-company carried a
    /// <c>not-compared</c> offer, and 214 that an activity changed under one comparison identity. Since
    /// schema 0.44.0 <c>&lt;activity&gt;_offer</c> is the course's census admission and
    /// <c>ReadCourseWorthPerActivity</c> writes <c>not-compared</c> for an activity the course mints no
    /// domain for; since 0.43.0 <c>choice_id</c> is the course's decision identity, which a changing
    /// activity does not contradict. Neither column changed name or position, so the schema line is the
    /// only witness there is and every arm below turns on it.</para>
    ///
    /// <para>The skip arms go through <see cref="Program.Evaluate"/> rather than asking the check,
    /// because asking the check proves nothing about the runner — the same reason the revived combat
    /// check's own coverage arm does.</para>
    /// </summary>
    /// <summary>
    /// A capture written after the family chooser was deleted parses, and every reader of a column
    /// that went with it declines by name instead of answering out of nothing.
    ///
    /// <para><b>This is the one case a floor gate cannot express, and every other schema gate in this
    /// tool is a floor.</b> `SchemaAtLeast` asks "is this capture new enough to carry the evidence",
    /// which a 0.46.0 capture satisfies for every question ever asked of it — including the questions
    /// whose evidence 0.46.0 is exactly what removed. The failure mode is not a crash: a reader of a
    /// deleted column finds an empty string or no rows, computes a number from it, and publishes a
    /// clean-looking zero. `MeasureHuntKnownUnusableShare` is the live instance — it reads the decision
    /// board's `factors:` breakdown, which went with the chooser, and would report a share of 0 over
    /// several hundred decisions on a row whose direction is "down", landing in the committed ledger as
    /// an improvement nobody made.</para>
    /// </summary>
    private static void TheRetiredChooserColumnsDeclineByNameAtTheirSchema()
    {
        string file = Path.GetTempFileName(), events = Path.ChangeExtension(file, null) + "-events.jsonl";
        try
        {
            // A 0.46.0 row carries none of `<activity>_funnel`, `<activity>_time` or
            // `<family>_prepared`/`_deferred`/`_prepare_ms`, and its decision occurrences carry no
            // `factors:` entry. Everything else about the row is unchanged, which is the producer's own
            // claim — "a 0.46.0 capture reads as absent columns rather than as shifted ones".
            // Two hundred rows, because the players-reference check wants three seconds of an idle
            // player beside a usable hunt before it says anything, and a fixture shorter than its own
            // threshold proves only that a check can stay quiet.
            Session Load(string schema, string extraColumns = "", string extraCells = "")
            {
                var text = new StringBuilder(
                    $"# schema={schema}\n# text_columns=action,combat_offer,keep-company_offer,player_vel{extraColumns.Replace("\t", ",")}\n"
                    + $"tick\taction\tchoice_id\tchoice_fresh\tcombat_offer\tkeep-company_offer\tnear_threat\tplayer_vel\tcombat_raw\tcombat_fin\tkeep-company_fin{extraColumns}\n");
                for (int tick = 1; tick <= 200; tick++)
                    text.Append($"{tick}\tkeep-company\t7\t{(tick == 1 ? 1 : 0)}\tUsable:3\tnot-compared\t4.0\t0.0,0.0\t0.80\t0.20\t0.50{extraCells}\n");
                File.WriteAllText(file, text.ToString());
                // One decision occurrence with no `factors:` entry, which is what 0.46.0 writes.
                File.WriteAllLines(events, new[]
                {
                    JsonSerializer.Serialize(new { v = 1, seq = 0, tick = 0, wall_elapsed_ms = 0d, kind = "session",
                        subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                        expected_x = 0f, expected_y = 0f, amount = 0, detail = "" }),
                    JsonSerializer.Serialize(new { v = 1, seq = 1, tick = 1, wall_elapsed_ms = 0d, kind = "decision",
                        subject = 0, related = "", label = "keep-company", channel = "WithPlayer", pos_x = 0f, pos_y = 0f,
                        vel_x = 0f, vel_y = 0f, expected_x = 0f, expected_y = 0f, amount = 0,
                        detail = $"scores=;{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}combat=usable:3,unknown:0,unusable:0,reason:-;" }),
                    JsonSerializer.Serialize(new { v = 1, seq = 2, tick = 3, wall_elapsed_ms = 0d, kind = "session-end",
                        subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                        expected_x = 0f, expected_y = 0f, amount = 0, detail = "" }),
                });
                return Session.Load(file);
            }

            // It parses at all, and as the schema it says it is. A header the loader silently read as
            // unversioned would make every gate below answer for the wrong reason.
            Session retired = Load("0.46.0");
            Require(retired.Count == 200 && retired.Metadata.TryGetValue("schema", out string? read) && read == "0.46.0",
                $"a 0.46.0 header did not parse as 200 rows at its own schema: {retired.Count} row(s), schema '{(retired.Metadata.TryGetValue("schema", out string? s) ? s : "absent")}'");
            Require(!CompletedTransferClaimsWereReceived.SchemaBelow(retired, CompletedTransferClaimsWereReceived.ChooserColumnsRetired)
                    && CompletedTransferClaimsWereReceived.SchemaBelow(Load("0.44.0"), CompletedTransferClaimsWereReceived.ChooserColumnsRetired),
                "the retirement gate does not separate a 0.46.0 capture from a 0.44.0 one, so nothing below is testing what it claims");

            // The measure whose evidence is gone declines by name rather than reporting a zero share.
            var share = new MeasureHuntKnownUnusableShare();
            Require(share.Missing(Load("0.44.0")) is null,
                "the hunt-offer measure declined on a capture that predates the removal, so it has stopped grading the captures it is kept for");
            string? declined = share.Missing(retired);
            Require(declined is not null && declined.Contains("factors:", StringComparison.Ordinal)
                    && declined.Contains("0.46.0", StringComparison.Ordinal),
                "the hunt-offer measure did not decline by name on a capture whose `factors:` breakdown was removed — it reads that field "
                + "for every decision, so without a gate it reports a share of zero, which on a row whose direction is `down` is a clean "
                + $"number for a question nobody asked: {declined ?? "it ran"}");

            // The census block says the funnel is retired rather than emitting nothing. A section that
            // silently disappears is the absence this header exists to make visible.
            string header = DescribeSession.Of(retired);
            Require(header.Contains("funnel    retired at schema 0.46.0", StringComparison.Ordinal),
                "the session header printed no funnel line at all on a 0.46.0 capture; a reader who knows that line scrolls for it and "
                + $"concludes the recorder dropped it, which is the silent absence the census exists to stop:\n{header}");
            Require(!DescribeSession.Of(Load("0.44.0")).Contains("funnel    retired", StringComparison.Ordinal),
                "the header claimed the funnel was retired on a capture from before it was");

            // The check whose *primary* evidence survives must not skip, and must name the two
            // decorations that went rather than printing a shorter sentence than it used to.
            var reference = new HuntsWorthTakingAreTaken();
            Finding[] referenceFindings = reference.Run(retired).ToArray();
            Require(referenceFindings.Length == 1,
                $"the players-reference check reported {referenceFindings.Length} finding(s) on a 0.46.0 capture that holds its stretch — "
                + "its offer, raw and final columns all survive 0.46.0, so it must still ask its question rather than skipping for want of a decoration");
            Require(referenceFindings[0].Detail.Contains("combat_time retired at 0.46.0", StringComparison.Ordinal),
                $"the finding dropped `combat_time` from its factor list without saying so, so two findings from two schemas read as factors "
                + $"that moved: {referenceFindings[0].Detail}");
            Require(referenceFindings[0].Detail.Contains("combat_funnel` was the family chooser's", StringComparison.Ordinal),
                $"the finding lost its funnel sentence silently rather than naming what took it: {referenceFindings[0].Detail}");

            // **A removed column planted back into a 0.46.0 header.** The reader must prefer the real
            // column over the retirement notice — the notice is what it says when the column is absent,
            // not a claim about the schema — and this is the arm that reddens if the assertions above
            // are keyed on the schema rather than on what the capture actually carries.
            Session planted = Load("0.46.0", "\tcombat_funnel", "\tstand-unreachable");
            string plantedHeader = DescribeSession.Of(planted);
            Require(plantedHeader.Contains("funnel    combat: stand-unreachable", StringComparison.Ordinal)
                    && !plantedHeader.Contains("funnel    retired", StringComparison.Ordinal),
                "a 0.46.0 capture that does carry a funnel column was told its funnel was retired instead of being shown it — the notice "
                + $"is for an absent column and must never stand in front of a present one:\n{plantedHeader}");
        }
        finally { File.Delete(file); if (File.Exists(events)) File.Delete(events); }
    }

    private static void TheCourseErasChecksReplaceTheChoosersAtTheirSchema()
    {
        string file = Path.GetTempFileName();
        try
        {
            Session Load(string schema, string rows)
            {
                File.WriteAllText(file,
                    $"# schema={schema}\n# text_columns=action,keep-company_offer,combat_offer\n"
                    + "tick\taction\tchoice_id\tchoice_fresh\tkeep-company_offer\tcombat_offer\n" + rows);
                return Session.Load(file);
            }

            // Keeping company selected while its own offer reads the word the course writes for an
            // activity it mints no domain for.
            const string companyRows = "1\tkeep-company\t7\t1\tnot-compared\tUsable:-\n2\tkeep-company\t7\t0\tnot-compared\tUsable:-\n";
            var offer = new SelectedActivitiesHadAnEligibleOffer();
            Require(offer.Run(Load("0.43.0", companyRows)).Count() == 1,
                "a not-compared offer under the chooser's own schema was not reported");
            Require(!offer.Run(Load("0.44.0", companyRows)).Any(),
                "a not-compared offer was still a contradiction on a capture whose offer column is the course's census admission");

            // What stays a contradiction at that schema: a bound step in a domain the same decision's
            // census had itself proved unusable.
            Finding[] unusable = offer.Run(Load("0.44.0",
                "1\tcombat\t7\t1\tnot-compared\tKnownUnusable:no-admissible-target\n")).ToArray();
            Require(unusable.Length == 1 && unusable[0].Severity == Severity.Definitive,
                "a selected activity whose own census read KnownUnusable was not reported on a course capture");

            // The activity changing inside one identity: the chooser's rule on an older capture, the
            // course's on a newer one, and the runner is what decides which.
            const string flickerRows = "1\tcombat\t7\t1\tnot-compared\tUsable:-\n2\tkeep-company\t7\t0\tnot-compared\tUsable:-\n"
                + "3\tcombat\t7\t0\tnot-compared\tUsable:-\n4\tkeep-company\t7\t0\tnot-compared\tUsable:-\n";
            string Chooser = new ARetainedChoiceKeepsItsSelection().Name, Course = new TheBoundActivityHoldsWhileOneDecisionRuns().Name;

            var older = Program.Evaluate(Load("0.42.0", flickerRows));
            Require(older.Skipped.Any(s => s.Name == Course) && !older.Skipped.Any(s => s.Name == Chooser),
                "on a chooser-era capture the course rule ran and the chooser rule was skipped, which is backwards");
            Require(older.Findings.Count(f => f.Check == Chooser && f.Severity == Severity.Definitive) == 3,
                "the chooser rule did not report each label move on a capture it still grades");

            var newer = Program.Evaluate(Load("0.44.0", flickerRows));
            Require(newer.Skipped.Any(s => s.Name == Chooser) && !newer.Skipped.Any(s => s.Name == Course),
                "on a course-era capture the chooser rule was not skipped by name, or the course rule did not run");
            Finding[] bound = newer.Findings.Where(f => f.Check == Course).ToArray();
            Require(bound.Length == 1, $"the course rule reported {bound.Length} finding(s) rather than one for the whole session");
            Require(bound[0].Rows == 3, $"the course rule counted {bound[0].Rows} transition(s) rather than the three the fixture holds");
            Require(bound[0].Severity == Severity.Potential,
                $"the course rule graded the change {bound[0].Severity} — the row cannot say whether the decision was settled, so it is not a contradiction");
            Require(bound[0].Detail.Contains("combat→keep-company 2×", StringComparison.Ordinal),
                "the course rule did not name which activity change it counted");
            Require(!newer.Findings.Any(f => f.Check == Chooser),
                "a skipped check still contributed findings");
        }
        finally { File.Delete(file); }
    }

    private sealed record FixtureEvent(long Tick, string Kind, string Label, string Channel, int Amount, string Detail);

    private static readonly string[] IdentityColumns = { "tick", "wall_elapsed_ms", "action", "choice_id", "choice_fresh", "control_grant_id",
        "activity_attempt_id", "attempt_end_id", "attempt_end_activity_id", "attempt_end_activity", "attempt_end_tick", "mine_offer", "chop_offer" };

    private static FixtureEvent Grant(long tick, long id, long activity, long attempt, string phase, string requested, string hand, string? applied = null)
        => new(tick, "control-grant", applied ?? requested, hand, 0,
            $"grant-id={id};grant-tick={tick};activity-id={activity};attempt-id={attempt};activity-phase={phase};requested-owner={requested};applied-owner={applied ?? requested};"
            + $"requested-controls=desired=0.00,0.00;applied-controls=desired=0.00,0.00;hand={hand};motor-applications=1;scope=ai-phase-before-engine;hand-effect=unobserved");

    private static FixtureEvent Outcome(long tick, long attempt, long activity, string name, string family, long start, long end, string status, string attribution, string cause, int effects)
        => new(tick, "attempt-outcome", name, attribution == "NotApplicable" ? status : status + ":" + attribution, effects,
            $"attempt-id={attempt};activity-id={activity};family={family};start-tick={start};end-tick={end};status={status};attribution={attribution};cause={cause};productive-effects={effects};effect-scope=companion-credited-tool-or-interaction-effects;interruption-is-not-failure=true");

    // The tool-local attempt= is 1 on both strikes on purpose: a join by that number would invent
    // attempt 1 and leave attempts 5 and 8 with no effects. A null activityAttempt writes the
    // pre-0.25.0 channel, which carries no attempt of its own and must join through rows.
    private static FixtureEvent Strike(long tick, string tool, long toolAttempt, long choice, long activity, string effect, long? activityAttempt = null)
        => new(tick, "tool-effect", tool, $"attempt={toolAttempt};choice-id={choice};activity-id={activity}" + (activityAttempt is long a ? $";activity-attempt-id={a}" : ""), effect == "Damaged" ? 10 : 0,
            $"observation-tick={tick};tool-item=1;effect={effect};before-present=True;after-present={(effect == "Removed" ? "False" : "True")};damage-scope=tool-owned-hit-table;yield=unobserved");

    /// <summary>The same capture as an older producer wrote it: every strike loses the attempt it named.</summary>
    private static void AsPre025Strikes(System.Collections.Generic.List<FixtureEvent> events)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Kind == "tool-effect")
                events[i] = events[i] with { Channel = events[i].Channel.Split(";activity-attempt-id=")[0] };
    }

    /// <summary>
    /// A consistent capture: mining attempt 5 (ticks 10..20) interrupted by escape; mining attempt 6
    /// (25..30) completed and replaced by chopping attempt 7 (30..31) under a new activity; chopping
    /// resumed as attempt 8, which struck and was suspended by recovery inside tick 32, so no row or
    /// grant carries it; a downed hold applied as recovery clearance at 40; attempt 9 still open when
    /// capture ended.
    /// </summary>
    private static (System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> Rows, System.Collections.Generic.List<FixtureEvent> Events) IdentityScenario()
    {
        var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>();
        for (long t = 10; t <= 41; t++)
        {
            bool fresh = !(t is >= 20 and <= 24 || t is >= 33 and <= 40);
            long choice = t < 20 ? t - 9 : t < 25 ? 10 : t <= 32 ? t - 14 : t <= 40 ? 18 : 19;
            string action = t < 30 ? "mine" : t <= 40 ? "chop" : "mine";
            long open = t < 20 ? 5 : t < 25 ? 0 : t < 30 ? 6 : t == 30 ? 7 : t == 41 ? 9 : 0;
            (long Id, long Activity, string Name, long Tick) end = t < 20 ? (0L, 0L, "none", -1L) :t < 30 ? (5L, 2L, "mine", 20L) : t == 30 ? (6L, 2L, "mine", 30L) : t == 31 ? (7L, 3L, "chop", 31L) : (8L, 3L, "chop", 32L);
            rows.Add(new()
            {
                ["tick"] = t.ToString(), ["wall_elapsed_ms"] = (t * 16).ToString(), ["action"] = action, ["choice_id"] = choice.ToString(),
                ["choice_fresh"] = fresh ? "1" : "0", ["control_grant_id"] = t.ToString(), ["activity_attempt_id"] = open.ToString(),
                ["attempt_end_id"] = end.Id.ToString(), ["attempt_end_activity_id"] = end.Activity.ToString(), ["attempt_end_activity"] = end.Name,
                ["attempt_end_tick"] = end.Tick.ToString(), ["mine_offer"] = "Usable:proven-pose", ["chop_offer"] = "Unresolved:approach-undecided",
            });
        }
        var events = new System.Collections.Generic.List<FixtureEvent>
        {
            Grant(10, 1, 2, 5, "Executing", "travel", "WorkTool"),
            Strike(12, "pickaxe", 1, 3, 2, "Damaged", 5),
            Grant(15, 6, 2, 5, "Executing", "hold", "WorkTool"),
            Outcome(20, 5, 2, "mine", "Gathering", 10, 20, "Interrupted", "NotApplicable", "survival-escape", 1),
            Grant(20, 11, 2, 0, "Suspended", "survival-escape", "Available"),
            Grant(25, 16, 2, 6, "Executing", "travel", "WorkTool"),
            Strike(27, "pickaxe", 2, 13, 2, "Removed", 6),
            Outcome(30, 6, 2, "mine", "Gathering", 25, 30, "Complete", "Companion", "tracked-vein-observed-clear", 1),
            Grant(30, 21, 3, 7, "Executing", "travel", "Available"),
            Outcome(31, 7, 3, "chop", "Gathering", 30, 31, "Interrupted", "NotApplicable", "combat-reflex", 0),
            Grant(31, 22, 3, 0, "Suspended", "combat-reflex", "Available"),
            Strike(32, "axe", 1, 18, 3, "Damaged", 8),
            Outcome(32, 8, 3, "chop", "Gathering", 32, 32, "Interrupted", "NotApplicable", "follow-recovery-flight", 1),
            Grant(32, 23, 3, 0, "Suspended", "follow-recovery-flight", "Available"),
            Grant(40, 31, 3, 0, "Suspended", "downed", "Unavailable", applied: "travel-recovery-clearance"),
            Grant(41, 32, 4, 9, "Executing", "travel", "Available"),
        };
        return (rows, events);
    }

    /// <summary>Writes a TSV and, unless <paramref name="events"/> is null, its sidecar, to a fresh temporary stem so the sidecar cache can never serve an earlier fixture.</summary>
    private static string WriteIdentitySession(System.Collections.Generic.List<string> files, string[] columns,
        System.Collections.Generic.IEnumerable<System.Collections.Generic.IReadOnlyDictionary<string, string>> rows, System.Collections.Generic.IEnumerable<FixtureEvent>? events,
        string schema = "0.21.0", bool closed = true)
    {
        string tsv = Path.GetTempFileName(); files.Add(tsv);
        var text = new StringBuilder($"# schema={schema}\n# text_columns=")
            .Append(string.Join(',', columns.Where(c => c is "action" or "attempt_end_activity" || c.EndsWith("_offer", StringComparison.Ordinal)))).Append('\n')
            .Append(string.Join('\t', columns)).Append('\n');
        foreach (var row in rows) text.Append(string.Join('\t', columns.Select(c => row[c]))).Append('\n');
        File.WriteAllText(tsv, text.ToString());
        if (events == null) return tsv;
        string sidecar = ReadGodsEyeEvents.PathFor(tsv); files.Add(sidecar);
        string Json(int seq, long tick, string kind, string label, string channel, int amount, string detail)
            => System.Text.Json.JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = tick * 16.0, kind, subject = 1, related = "", label, channel,
                pos_x = 0, pos_y = 0, vel_x = 0, vel_y = 0, expected_x = 0, expected_y = 0, amount, detail });
        var ordered = events.OrderBy(e => e.Tick).ToList();
        var lines = new System.Collections.Generic.List<string> { Json(0, 0, "session", "", "", 0, "schema=1") };
        foreach (FixtureEvent e in ordered) lines.Add(Json(lines.Count, e.Tick, e.Kind, e.Label, e.Channel, e.Amount, e.Detail));
        if (closed) lines.Add(Json(lines.Count, ordered.Count == 0 ? 0 : ordered[^1].Tick, "session-end", "", "", ordered.Count, "normal-close"));
        File.WriteAllLines(sidecar, lines);
        return tsv;
    }

    private static void AttemptEvidenceJoinsByIdentityAndDisagreementsAreDefinitive()
    {
        var files = new System.Collections.Generic.List<string>();
        string html = Path.Combine(Path.GetTempPath(), $"aic-identity-{Guid.NewGuid():N}.html");
        try
        {
            string Write(Action<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>, System.Collections.Generic.List<FixtureEvent>>? mutate = null)
            {
                var (rows, events) = IdentityScenario();
                mutate?.Invoke(rows, events);
                return WriteIdentitySession(files, IdentityColumns, rows, events);
            }
            Finding[] Contradictions(string tsv) => new AttemptIdentitiesAgreeAcrossRecords().Run(Session.Load(tsv)).ToArray();

            string consistent = Write();
            Session session = Session.Load(consistent);
            Require(Contradictions(consistent).Length == 0,
                "a consistent capture, including an attempt that opened and closed inside one tick with no row or grant naming it, produced identity findings: "
                + string.Join(" | ", Contradictions(consistent).Select(f => f.Title)));
            Require(!new SelectedActivitiesHadAnEligibleOffer().Run(session).Any() && !new ARetainedChoiceKeepsItsSelection().Run(session).Any(),
                "the consistent capture's selections were reported");
            var (_, skipped, _) = Program.Evaluate(session);
            Require(!skipped.Any(s => s.Name == new AttemptIdentitiesAgreeAcrossRecords().Name || s.Name == new ControlGrantsAreCompatible().Name),
                "the identity checks were skipped on a capture that carries their evidence");

            string[] view = JoinAttemptEvidence.Describe(consistent, session, full: true).Split('\n');
            string Line(long attempt) => view.SingleOrDefault(l => l.StartsWith($"  attempt {attempt}  ", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"the identity view has no line for attempt {attempt}");
            Require(Line(5).Contains("mine (Gathering) activity 2  ticks 10..20  Interrupted", StringComparison.Ordinal)
                && Line(5).Contains("grants 2 (travel/WorkTool×1, hold/WorkTool×1)", StringComparison.Ordinal)
                && Line(5).Contains("tool effects 1 (Damaged 1)", StringComparison.Ordinal),
                "attempt 5 did not carry its own conclusion, grants and strike together");
            Require(Line(6).Contains("Complete:Companion", StringComparison.Ordinal) && Line(6).Contains("tool effects 1 (Removed 1)", StringComparison.Ordinal),
                "attempt 6 lost its completion or its removal");
            Require(Line(8).Contains("tool effects 1 (Damaged 1)", StringComparison.Ordinal) && Line(8).Contains("grants 0", StringComparison.Ordinal),
                "a strike inside an attempt that opened and closed on one tick was not joined through that tick's closed-attempt row");
            Require(Line(9).Contains("outcome unrecorded", StringComparison.Ordinal) && Line(9).Contains("activity 4", StringComparison.Ordinal),
                "an attempt still open at capture end was hidden or given an outcome");
            Require(!view.Any(l => l.StartsWith("  attempt 1  ", StringComparison.Ordinal) || l.StartsWith("  attempt 2  ", StringComparison.Ordinal)),
                "a tool's own attempt counter was treated as an attempt identity");
            Require(view.Any(l => l.Contains("0 tool effect(s) no attempt contains", StringComparison.Ordinal)),
                "joined strikes were counted as unjoined");

            // With every outcome present the interval fallback reaches the same attempts, so the
            // row routes are only proven when an outcome occurrence is lost: both strikes must still
            // join through the row at their tick, the open one and the one closed on that tick.
            // These are pre-0.25.0 strikes, because a strike naming its attempt never consults a row.
            string lost = Write((_, e) => { AsPre025Strikes(e); e.RemoveAll(x => x.Kind == "attempt-outcome" && (x.Detail.StartsWith("attempt-id=5;") || x.Detail.StartsWith("attempt-id=8;"))); });
            string[] lostView = JoinAttemptEvidence.Describe(lost, Session.Load(lost), full: true).Split('\n');
            string LostLine(long attempt) => lostView.SingleOrDefault(l => l.StartsWith($"  attempt {attempt}  ", StringComparison.Ordinal)) ?? "";
            Require(LostLine(5).Contains("outcome unrecorded", StringComparison.Ordinal) && LostLine(5).Contains("tool effects 1", StringComparison.Ordinal),
                "a strike on a row naming its open attempt was not joined once that attempt's outcome was lost");
            Require(LostLine(8).Contains("outcome unrecorded", StringComparison.Ordinal) && LostLine(8).Contains("tool effects 1", StringComparison.Ordinal),
                "a strike inside an attempt closed on its own tick was not joined through that row once the outcome was lost");

            // And the fallback alone: with the strike's row missing, only attempt 5's recorded
            // interval under activity 2 contains tick 12.
            string rowless = Write((r, e) => { AsPre025Strikes(e); r.RemoveAll(x => x["tick"] == "12"); });
            string[] rowlessView = JoinAttemptEvidence.Describe(rowless, Session.Load(rowless), full: true).Split('\n');
            Require(rowlessView.Any(l => l.StartsWith("  attempt 5  ", StringComparison.Ordinal) && l.Contains("tool effects 1 (Damaged 1) joined by outcome interval×1", StringComparison.Ordinal))
                && rowlessView.Any(l => l.Contains("0 tool effect(s) no attempt contains", StringComparison.Ordinal)),
                "a strike with no row at its tick was not joined to the single outcome interval of its activity that contains it");

            // A strike that names its attempt needs neither the row nor the outcome. Losing both
            // leaves an older strike with nothing to join through, and must not cost this one.
            void LoseRowAndOutcome(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> r, System.Collections.Generic.List<FixtureEvent> e)
            {
                r.RemoveAll(x => x["tick"] == "12");
                e.RemoveAll(x => x.Kind == "attempt-outcome" && x.Detail.StartsWith("attempt-id=5;"));
            }
            string bare = Write(LoseRowAndOutcome);
            string[] bareView = JoinAttemptEvidence.Describe(bare, Session.Load(bare), full: true).Split('\n');
            Require(bareView.Any(l => l.StartsWith("  attempt 5  ", StringComparison.Ordinal) && l.Contains("tool effects 1 (Damaged 1) joined by its own attempt id×1", StringComparison.Ordinal))
                && bareView.Any(l => l.Contains("0 tool effect(s) no attempt contains", StringComparison.Ordinal)),
                "a strike naming its own attempt was not joined once its row and its attempt's outcome were both lost");
            string bareOld = Write((r, e) => { AsPre025Strikes(e); LoseRowAndOutcome(r, e); });
            Require(JoinAttemptEvidence.Describe(bareOld, Session.Load(bareOld), full: true).Contains("1 tool effect(s) no attempt contains", StringComparison.Ordinal),
                "an older strike with no row and no containing outcome was joined anyway, so the case above does not prove the producer route");
            Require(Line(6).Contains("joined by its own attempt id×1", StringComparison.Ordinal) && Line(8).Contains("joined by its own attempt id×1", StringComparison.Ordinal),
                "a strike carrying its attempt joined through a row or an interval instead of by the identity it names");
            string old = Write((_, e) => AsPre025Strikes(e));
            Require(Contradictions(old).Length == 0 && JoinAttemptEvidence.Describe(old, Session.Load(old), full: true).Contains("0 tool effect(s) no attempt contains", StringComparison.Ordinal),
                "the same consistent capture written by an older producer, whose strikes name no attempt, was reported or lost its joins");

            WritePlaytestHtml.Write(html, new[] { consistent });
            string data = File.ReadAllText(html).Split("<script id=\"data\" type=\"application/json\">")[1].Split("</script>")[0];
            using (var json = System.Text.Json.JsonDocument.Parse(data))
            {
                var attempts = json.RootElement.GetProperty("Runs")[0].GetProperty("Attempts").EnumerateArray().ToArray();
                Require(attempts.Any(a => a.GetProperty("Attempt").GetInt64() == 8 && a.GetProperty("Start").GetInt64() == 32
                        && a.GetProperty("Text").GetString()!.Contains("tool effects 1", StringComparison.Ordinal)),
                    "the HTML timeline did not carry the identity view's attempt placed at its own tick");
            }

            void Fires(string title, Action<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>, System.Collections.Generic.List<FixtureEvent>> mutate, string failure)
            {
                Finding[] found = Contradictions(Write(mutate));
                Require(found.Any(f => f.Severity == Severity.Definitive && f.Title.Contains(title, StringComparison.Ordinal)),
                    failure + (found.Length == 0 ? " (nothing fired)" : " (fired instead: " + string.Join(" | ", found.Select(f => f.Title)) + ")"));
            }
            Fires("strictly increasing", (_, e) => e.Add(Outcome(33, 6, 2, "mine", "Gathering", 25, 30, "Complete", "Companion", "again", 1)),
                "a second outcome for one attempt was not reported");
            Fires("attempt 5 is named under two activities", (_, e) => e[2] = Grant(15, 6, 3, 5, "Executing", "hold", "WorkTool"),
                "a grant naming attempt 5 under another activity was not reported");
            Fires("attempt 5 is named under two activities", (r, _) => { foreach (var row in r.Where(x => x["attempt_end_id"] == "5")) row["attempt_end_activity"] = "chop"; },
                "the rows' conclusion naming attempt 5 as another activity was not reported");
            Fires("outside its recorded ticks 25..30", (_, e) => e.Add(Grant(22, 13, 2, 6, "Executing", "travel", "WorkTool")),
                "a grant under attempt 6 issued before it began was not reported");
            Fires("rows inside attempt 5 name a different open attempt", (r, _) => r.Single(x => x["tick"] == "15")["activity_attempt_id"] = "7",
                "a row inside attempt 5 naming another open attempt was not reported");
            Fires("comparison their own tick's row does not", (_, e) => e[1] = Strike(12, "pickaxe", 1, 9, 2, "Damaged", 5),
                "a strike naming a comparison its tick's row does not was not reported");
            Fires("struck with no attempt open", (_, e) => e[1] = Strike(12, "pickaxe", 1, 3, 2, "Damaged", 0),
                "a strike naming attempt zero was not reported");
            // Attempt 42 has no outcome and no grant, so only the row can contradict it.
            Fires("an attempt their own tick's row does not", (_, e) => e[1] = Strike(12, "pickaxe", 1, 3, 2, "Damaged", 42),
                "a strike naming an attempt its tick's row neither holds open nor closed on that tick was not reported");
            Fires("naming attempt 5 lies outside its recorded ticks 10..20", (_, e) => e.Add(Strike(21, "pickaxe", 3, 10, 2, "Damaged", 5)),
                "a strike naming attempt 5 after that attempt ended was not reported");
            Fires("attempt 7 is named under two activities", (_, e) => e[1] = Strike(12, "pickaxe", 1, 3, 2, "Damaged", 7),
                "a strike naming chopping's attempt under the mining activity was not reported");
            Require(!Contradictions(Write((_, e) => e[1] = Strike(12, "pickaxe", 1, 3, 2, "Damaged", 0))).Any(f => f.Title.Contains("an attempt their own tick's row does not", StringComparison.Ordinal)),
                "a strike naming no attempt was also judged against its row, counting one contradiction twice");
        }
        finally
        {
            foreach (string file in files) File.Delete(file);
            File.Delete(html);
        }
    }

    private static void ControlGrantRulesJudgeTheRequestedOwner()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            Finding[] Read(Action<System.Collections.Generic.List<FixtureEvent>>? mutate = null)
            {
                var (rows, events) = IdentityScenario();
                mutate?.Invoke(events);
                return new ControlGrantsAreCompatible().Run(Session.Load(WriteIdentitySession(files, IdentityColumns, rows, events))).ToArray();
            }
            Finding[] clean = Read();
            Require(clean.Length == 0, "consistent grants, including a downed request applied as recovery clearance, were reported: " + string.Join(" | ", clean.Select(f => f.Title)));

            int Index(System.Collections.Generic.List<FixtureEvent> e, long tick) => e.FindIndex(x => x.Kind == "control-grant" && x.Tick == tick);
            void Fires(string title, Action<System.Collections.Generic.List<FixtureEvent>> mutate, string failure)
            {
                Finding[] found = Read(mutate);
                Require(found.Any(f => f.Severity == Severity.Definitive && f.Title.Contains(title, StringComparison.Ordinal)),
                    failure + (found.Length == 0 ? " (nothing fired)" : " (fired instead: " + string.Join(" | ", found.Select(f => f.Title)) + ")"));
            }
            Fires("under a safety, recovery or downed request", e => e[Index(e, 20)] = Grant(20, 11, 2, 0, "Suspended", "survival-escape", "WorkTool"),
                "a work tool granted under escape was not reported");
            Fires("with no attempt open", e => e[Index(e, 25)] = Grant(25, 16, 2, 0, "Executing", "travel", "WorkTool"),
                "a work tool granted with no attempt was not reported");
            Fires("unavailable hand and the downed request came apart", e => e[Index(e, 40)] = Grant(40, 31, 3, 0, "Suspended", "downed", "Available", applied: "travel-recovery-clearance"),
                "a downed request with a usable hand was not reported");
            Fires("unavailable hand and the downed request came apart", e => e[Index(e, 41)] = Grant(41, 32, 4, 9, "Executing", "travel", "Unavailable"),
                "an unavailable hand outside downing was not reported");
            Fires("carried an open attempt", e => e[Index(e, 31)] = Grant(31, 22, 3, 7, "Executing", "combat-reflex", "Available"),
                "a combat reflex grant carrying the attempt it suspended was not reported");
            Fires("outside the Executing phase", e => e[Index(e, 41)] = Grant(41, 32, 4, 9, "Selected", "travel", "Available"),
                "an open attempt in the Selected phase was not reported");
            Fires("two control finalisations", e => e.Insert(Index(e, 25) + 1, Grant(25, 17, 2, 6, "Executing", "hold", "WorkTool")),
                "a second finalisation in one engine tick was not reported");
            Require(Read(e => e.Insert(Index(e, 25) + 1, Grant(25, 2, 2, 6, "Executing", "hold", "WorkTool"))).Length == 0,
                "a falling grant id, which is a new brain restarting its identities, was called a second finalisation");
            Finding[] unknown = Read(e => e.Add(Grant(26, 18, 2, 0, "Executing", "future-owner", "Available")));
            Require(unknown.Length == 1 && unknown[0].Severity == Severity.Oddity && unknown[0].Detail.Contains("future-owner", StringComparison.Ordinal),
                "an owner the reader has no rule for was judged or hidden instead of named as an oddity");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>
    /// Collection attempt 3 (ticks 10..16) claims ten of item 699 and its drop arrives as two pickups naming it, six and four,
    /// beside an incidental pickup of five of the same item during other work. The incidental pickup is the trap: a join by
    /// item type alone would cover a lost pickup with it, so every case that loses a pickup keeps it in.
    /// </summary>
    private static void ACompletedTransferClaimNeedsItsReceivedQuantity()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            string[] columns = { "tick", "wall_elapsed_ms", "action", "choice_id", "choice_fresh", "control_grant_id",
                "activity_attempt_id", "attempt_end_id", "attempt_end_activity_id", "attempt_end_activity", "attempt_end_tick" };
            var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>();
            for (long t = 10; t <= 16; t++)
                rows.Add(new()
                {
                    ["tick"] = t.ToString(), ["wall_elapsed_ms"] = (t * 16).ToString(), ["action"] = t < 16 ? "collect" : "keep-company",
                    ["choice_id"] = t < 16 ? "1" : "2", ["choice_fresh"] = t is 10 or 16 ? "1" : "0", ["control_grant_id"] = t.ToString(),
                    ["activity_attempt_id"] = t < 16 ? "3" : "0", ["attempt_end_id"] = t < 16 ? "0" : "3", ["attempt_end_activity_id"] = t < 16 ? "0" : "1",
                    ["attempt_end_activity"] = t < 16 ? "none" : "collect", ["attempt_end_tick"] = t < 16 ? "-1" : "16",
                });
            FixtureEvent Pickup(long tick, int type, int amount, long attempt)
                => new(tick, "pickup", type.ToString(), "player-stacks-or-companion-bag", amount, $"stack={amount};collection-attempt-id={attempt}");
            FixtureEvent Claim(string status, string attribution, int type, int quantity)
                => new(16, "attempt-outcome", "collect", attribution == "NotApplicable" ? status : status + ":" + attribution, 0,
                    $"attempt-id=3;activity-id=1;family=NearbyAssistance;start-tick=10;end-tick=16;status={status};attribution={attribution};cause=drop-collected;productive-effects=0;"
                    + $"effect-scope=companion-credited-tool-or-interaction-effects;interruption-is-not-failure=true;claimed-yield-type={type};claimed-yield-quantity={quantity}");
            System.Collections.Generic.List<FixtureEvent> Events() => new() { Pickup(12, 699, 5, 0), Pickup(14, 699, 6, 3), Pickup(15, 699, 4, 3), Claim("Complete", "Companion", 699, 10) };
            string Write(Action<System.Collections.Generic.List<FixtureEvent>>? mutate = null, string schema = "0.26.0", bool closed = true)
            {
                var events = Events();
                mutate?.Invoke(events);
                return WriteIdentitySession(files, columns, rows, events, schema, closed);
            }
            Finding[] Found(Action<System.Collections.Generic.List<FixtureEvent>>? mutate = null, bool closed = true)
                => new CompletedTransferClaimsWereReceived().Run(Session.Load(Write(mutate, closed: closed))).ToArray();
            void LoseTheLastPickup(System.Collections.Generic.List<FixtureEvent> e) => e.RemoveAll(x => x.Kind == "pickup" && x.Tick == 15);

            string clean = Write();
            Require(Found().Length == 0, "a claim its own pickups delivered exactly was reported: " + string.Join(" | ", Found().Select(f => f.Title)));
            Require(!Program.Evaluate(Session.Load(clean)).Skipped.Any(s => s.Name == new CompletedTransferClaimsWereReceived().Name),
                "the transfer check was skipped on a 0.26.0 capture that carries claimed yields");
            string attemptLine = JoinAttemptEvidence.Describe(clean, Session.Load(clean), full: true).Split('\n').SingleOrDefault(l => l.StartsWith("  attempt 3  ", StringComparison.Ordinal)) ?? "";
            Require(attemptLine.Contains("claimed 10 of item 699; its 2 pickup(s) delivered 10", StringComparison.Ordinal),
                "the identity view did not put attempt 3's claim beside what its own pickups delivered: " + attemptLine);
            Require(JoinAttemptEvidence.Describe(clean, Session.Load(clean), full: true).Contains("1 incidental pickup(s)", StringComparison.Ordinal),
                "the incidental pickup was attached to an attempt or not counted");

            void Fires(string delivered, Action<System.Collections.Generic.List<FixtureEvent>> mutate, string failure)
            {
                Finding[] found = Found(mutate);
                Require(found.Any(f => f.Severity == Severity.Definitive && f.Title.StartsWith("a completed transfer claim with no matching received quantity", StringComparison.Ordinal)
                        && f.Title.Contains(delivered, StringComparison.Ordinal)),
                    failure + (found.Length == 0 ? " (nothing fired)" : " (fired instead: " + string.Join(" | ", found.Select(f => $"{f.Severity} {f.Title}")) + ")"));
            }
            Fires("claimed 10 of item 699 and its pickups delivered 6", LoseTheLastPickup,
                "a claim of ten with one of its two pickups lost was not reported, although the incidental pickup of the same item would cover it by type");
            Fires("delivered 6", e => e[2] = Pickup(15, 700, 4, 3), "a pickup of another item under the attempt was counted toward its claim");
            Fires("delivered 6", e => e[2] = Pickup(15, 699, 4, 4), "a pickup naming another attempt was counted toward this one's claim");
            Fires("delivered 6", e => { LoseTheLastPickup(e); e[^1] = Claim("Partial", "NotApplicable", 699, 10); }, "a partial transfer claim above its pickups was not reported");
            Require(Found(e => { LoseTheLastPickup(e); e[^1] = Claim("Partial", "NotApplicable", 699, 6); }).Length == 0,
                "a partial claim its pickups delivered was reported");
            Require(Found(e => e[^1] = Claim("Complete", "Shared", 699, 8)).Length == 0,
                "a claim below what arrived was reported, but the transfer ledger can only undercount, so that is not a contradiction");
            Require(Found(e => { LoseTheLastPickup(e); e[^1] = Claim("Invalid", "NotApplicable", 0, 0); }).Length == 0,
                "an attempt claiming no yield was held to one");
            Finding[] unclosed = Found(LoseTheLastPickup, closed: false);
            Require(unclosed.Length == 1 && unclosed[0].Severity == Severity.Potential && unclosed[0].Detail.Contains("never closed", StringComparison.Ordinal),
                "a short claim over a stream that never closed was not downgraded to potential with the reason: " + string.Join(" | ", unclosed.Select(f => $"{f.Severity} {f.Detail}")));

            string Skip(string tsv) => Program.Evaluate(Session.Load(tsv)).Skipped.SingleOrDefault(s => s.Name == new CompletedTransferClaimsWereReceived().Name).Missing ?? "";
            Require(Skip(Write(LoseTheLastPickup, schema: "0.25.0")).Contains("schema 0.26.0", StringComparison.Ordinal),
                "a capture older than claimed yields was judged, or skipped without naming the schema that introduced them");
            Require(Skip(WriteIdentitySession(files, columns, rows, events: null, schema: "0.26.0")).Contains("-events.jsonl", StringComparison.Ordinal),
                "a capture with no sidecar was judged, or skipped without naming it");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>
    /// One family at a time, a claimed arrival held against the region its destination was admitted against: a follow
    /// arrival inside the admitted region, a tool stand inside its own reach box and a firing arrival that still solves
    /// report nothing; the same captures with the body or the stand moved out, or the arc gone, report the contract by name.
    ///
    /// The follow family carries one case that flips rather than moves, and it is the case worth reading here. Following
    /// used to be admitted against either the region or the request's anchor, so a body parked at a far-off anchor was
    /// clean; the anchor stopped widening acceptance when the region gained its own growth, so that same capture is now
    /// the contradiction. The fixture asserts the firing rather than the silence, because a check still admitting the
    /// anchor would pass a silence assertion and fail this one.
    /// </summary>
    private static void AClaimedArrivalMustLieInsideItsSuccessRegion()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            string[] columns = { "tick", "wall_elapsed_ms", "region_kind", "region_revision", "region_terrain", "region_anchor_px", "region_player_px", "region_comfort",
                "region_work_tile", "region_reach", "region_arrival", "npc_px", "touched_wall", "spot", "fire" };
            System.Collections.Generic.Dictionary<string, string> Row(long t, string kind, string arrival, float bodyX, float bodyY, string anchor = "-", string player = "-",
                string comfort = "-", string tile = "-", string reach = "-", string fire = "none")
                => new()
                {
                    ["tick"] = t.ToString(), ["wall_elapsed_ms"] = (t * 16).ToString(), ["region_kind"] = kind, ["region_revision"] = "7", ["region_terrain"] = "3",
                    ["region_anchor_px"] = anchor, ["region_player_px"] = player, ["region_comfort"] = comfort, ["region_work_tile"] = tile, ["region_reach"] = reach,
                    // The orb's centre, which is the point the region and the reach box are both handed.
                    ["region_arrival"] = arrival,
                    ["npc_px"] = bodyX.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + bodyY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["touched_wall"] = "0", ["spot"] = "25,60", ["fire"] = fire,
                };
            Finding[] Found(System.Collections.Generic.IEnumerable<System.Collections.Generic.Dictionary<string, string>> rows, string schema = "0.27.0")
                => new ClaimedArrivalsStayInsideTheirSuccessRegion().Run(Session.Load(WriteIdentitySession(files, columns, rows, events: null, schema))).ToArray();
            System.Collections.Generic.IEnumerable<System.Collections.Generic.Dictionary<string, string>> Many(int count, Func<long, System.Collections.Generic.Dictionary<string, string>> row)
                => Enumerable.Range(0, count).Select(i => row(100 + i));
            string Describe(Finding[] found) => found.Length == 0 ? " (nothing fired)" : " (fired: " + string.Join(" | ", found.Select(f => $"{f.Severity} {f.Title}")) + ")";
            void Clean(Finding[] found, string failure) => Require(found.Length == 0, failure + Describe(found));
            void Fires(Finding[] found, Severity severity, string title, string failure)
                => Require(found.Length == 1 && found[0].Severity == severity && found[0].Title.StartsWith(title, StringComparison.Ordinal), failure + Describe(found));

            // Following: admitted against a region centred 400,800 with a 64 by 48 comfort box, anchor beside it.
            const string comfort = "64.00,48.00", here = "400.00,800.00";
            System.Collections.Generic.Dictionary<string, string> Follow(long t, float x, string arrival = "inside", string player = here, string box = comfort, string anchor = here)
                => Row(t, "follow-comfort", arrival, x, 800, anchor: anchor, player: player, comfort: box);
            Clean(Found(Many(5, t => Follow(t, 430))), "a follow arrival thirty pixels from the admitted region centre was reported");
            // The flipped case: the body sits on its anchor and five hundred pixels outside the region it
            // was admitted against. Acceptance stopped widening to the anchor, so this is the contradiction.
            Fires(Found(new[] { Follow(100, 900, arrival: "outside", player: here, anchor: "900.00,800.00") }), Severity.Definitive,
                "claimed purpose arrival outside its declared success region: following",
                "a follow arrival parked on its anchor and far outside the admitted region was passed, which means acceptance is still widening to the anchor");
            Clean(Found(Many(5, t => Follow(t, 480, arrival: "-"))), "a body outside the comfort box with no arrival claimed was judged as an arrival");
            Fires(Found(new[] { Follow(100, 480, arrival: "outside") }), Severity.Definitive, "claimed purpose arrival outside its declared success region: following",
                "a follow arrival eighty pixels from the admitted region centre was not Definitive on its first sample");
            Fires(Found(new[] { Follow(100, 415, arrival: "outside", box: "10.00,10.00") }), Severity.Potential, "claimed purpose arrival outside its declared success region: following",
                "a follow arrival outside a comfort box narrower than the arrival radius was not downgraded to Potential");

            // Tool reach: tile 25,59 (centre 408,952), reach 5 by 4, a stand at 380,960 whose eye is inside the box.
            System.Collections.Generic.Dictionary<string, string> Tool(long t, float feetX, string arrival, string stand = "380.00,960.00", string reach = "5,4")
                => Row(t, "tool-reach", arrival, feetX, 960, anchor: stand, tile: "25,59", reach: reach);
            Clean(Found(Many(40, t => Tool(t, 200 + t, "-"))), "a walk toward a stand inside its box was reported");
            Clean(Found(Many(10, t => Tool(t, 380, "inside"))), "a claimed arrival that settled within the half second was reported as a stall");
            Fires(Found(Many(40, t => Tool(t, 380, "inside"))), Severity.Potential, "claimed arrival inside the tool's reach box while its activity still asked for the stand",
                "a held arrival inside the box, on rows whose activity still asked for its stand, was not reported as a Potential line-of-reach failure");
            Fires(Found(Many(40, t => Tool(t, 500, "outside"))), Severity.Potential, "claimed purpose arrival outside its declared success region: the navigator stopped outside the tool's reach box",
                "a held arrival ninety-two pixels from the tile centre, outside an eighty-eight pixel box, was not reported as the navigator's slack");
            Fires(Found(new[] { Tool(100, 200, "-", stand: "520.00,960.00") }), Severity.Definitive, "a tool stand declared outside its own working region",
                "a stand outside its own reach box under one reach was not Definitive");
            Fires(Found(new[] { Tool(100, 200, "-", stand: "520.00,960.00"), Tool(101, 200, "-", reach: "7,4") }), Severity.Potential, "a tool stand declared outside its own working region",
                "a stand outside its box in a capture whose reach changed was not downgraded to Potential");

            // Firing: arrival where no arc now solves is Potential only, and a position that still fires is clean.
            System.Collections.Generic.Dictionary<string, string> Fire(long t, string fire) => Row(t, "firing-position", "undeclared", 300, 800, anchor: "600.00,700.00", fire: fire);
            Clean(Found(Many(40, t => Fire(t, "fired"))), "an arrival at a firing position that fires was reported");
            Fires(Found(Many(40, t => Fire(t, "no-arc"))), Severity.Potential, "claimed arrival at an admitted firing position from which no arc solves",
                "a held arrival at a firing position with no arc was not reported");
            Clean(Found(Many(40, t => Row(t, "meeting-place", "undeclared", 300, 800, anchor: "600.00,800.00"))), "a meeting place, which declares no region, was judged");

            string old = Path.GetTempFileName(); files.Add(old);
            File.WriteAllText(old, "# schema=0.26.0\n# text_columns=action\ntick\twall_elapsed_ms\tspot\tfire\n1\t16\t25,60\tnone\n");
            string missing = Program.Evaluate(Session.Load(old)).Skipped.SingleOrDefault(s => s.Name == new ClaimedArrivalsStayInsideTheirSuccessRegion().Name).Missing ?? "";
            Require(missing.Contains("region_kind", StringComparison.Ordinal), "a capture older than success regions was judged, or skipped without naming the region columns: '" + missing + "'");

            // The geometry above restates the producer; these are the literals it rests on.
            string Source(params string[] parts) => File.ReadAllText(Path.Combine(parts));
            string navigator = Source("Companion", "Brain", "Infrastructure", "Movement", "Steering", "Navigator.cs");
            string access = Source("Companion", "Brain", "Infrastructure", "Interactions", "FindToolAccess.cs");
            string region = Source("Companion", "Brain", "Infrastructure", "Position", "DeclareSuccessRegion.cs");
            string telemetry = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs");
            // The reservation moved when following became a view over the player's intent region: the
            // objective hands the navigator's radius to the region and the region subtracts it from
            // both half-extents. Two literals rather than one, because either half alone can be true
            // while the slack is not actually reserved — an objective that passes the radius to a
            // region that ignores it reserves nothing.
            // Acceptance reserves the settle radius since the orb began hovering around a reached spot, so the pin holds the
            // navigator's expression for it and the hover radius it adds, beside the arrival radius itself.
            Require(navigator.Contains($"ArriveDistance = {ClaimedArrivalsStayInsideTheirSuccessRegion.ArriveDistance:0}f", StringComparison.Ordinal)
                    && navigator.Contains("SettleRadius = ArriveDistance + Weights.HoverRadiusPixels + 4f", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Infrastructure", "Selection", "BehaviourWeights.cs").Contains("HoverRadiusPixels = 16f", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Infrastructure", "Position", "FollowPlayerObjective.cs").Contains("Region.Accepts(centre, Movement.Navigator.SettleRadius)", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Infrastructure", "Observation", "ObservePlayerIntentRegion.cs").Contains("HalfSize.X - arrivalSlack", StringComparison.Ordinal),
                "the navigator's arrival radius, or follow acceptance reserving it, no longer matches what the follow rule assumes");
            // The eye is the centre for this body, so the tool rule measures the reach box on the centre
            // with no vertical offset at all. The zero is pinned rather than dropped: a non-zero eye
            // returning here would silently shift every tool-arrival verdict by its height, and nothing
            // else in this file would notice.
            Require(access.Contains("EyeHeight => 0f", StringComparison.Ordinal)
                    && access.Contains("reachX * 16f + 8f", StringComparison.Ordinal) && access.Contains("reachY * 16f + 8f", StringComparison.Ordinal),
                "the tool reach box no longer measures from the body's centre with the extents the tool rule recomputes");
            // Since the course took the tick the stand that reaches the positioner is `ExecuteCourseBinding.RequestFor`'s
            // (the activity's own request is discarded), so that is the site pinned for every tile purpose; mining's and
            // chopping's own requests still name their tile and are pinned beside it, because the recorder's region
            // columns are filled from whichever request the tick carried.
            Require(Source("Companion", "Brain", "Infrastructure", "Selection", "ExecuteCourseBinding.cs").Contains("return PositionRequest.ExactAt(pose, work);", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Infrastructure", "Selection", "ExecuteCourseBinding.cs").Contains("return PositionRequest.ExactAt(centre, work);", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Activities", "Gathering", "MineOre.cs").Contains("PositionRequest.ExactAt(pose, b.Tile)", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Activities", "Gathering", "ChopTree.cs").Contains("PositionRequest.ExactAt(pose, b.Bottom)", StringComparison.Ordinal),
                "a tool stand no longer declares the work tile its reach box is judged against");
            // `Contains` is handed the body's centre and the follow arm reads the region alone; a second
            // reference reappearing there would widen acceptance behind this check's back.
            Require(region.Contains("public bool? Contains(Vector2 feet)", StringComparison.Ordinal)
                    && region.Contains("SuccessRegionKind.FollowComfort => Near(feet, PlayerFeet)", StringComparison.Ordinal)
                    && telemetry.Contains("region.Contains(npc.Center)", StringComparison.Ordinal),
                "the follow region admits something other than its own box, or the recorder judges an arrival on a point other than the orb's centre");
            // "firing-position" retired with the positioner's scored firing stands: no new capture
            // writes it, while the firing rows above keep grading it where an old capture carries it.
            Require(new[] { "\"follow-comfort\"", "\"tool-reach\"", "\"meeting-place\"", "\"undeclared\"" }.All(name => region.Contains(name, StringComparison.Ordinal))
                    && !region.Contains("\"firing-position\"", StringComparison.Ordinal)
                    && telemetry.Contains("controlGrant?.RequestedOwner == \"travel\"", StringComparison.Ordinal)
                    && telemetry.Contains("region_kind\\tregion_revision\\tregion_tick\\tregion_terrain\\tregion_anchor_px\\tregion_player_px\\tregion_comfort\\tregion_work_tile\\tregion_reach\\tregion_arrival", StringComparison.Ordinal),
                "the region names, the arrival-claim gate or the region column order the rule reads has changed");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    private static void IdentityChecksSkipOldAndPartialCapturesByName()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            string old = Path.GetTempFileName(); files.Add(old);
            File.WriteAllText(old, "# schema=0.10.0\n# text_columns=action\ntick\taction\tchoice_id\tchoice_fresh\n1\tmine\t1\t1\n2\tmine\t1\t0\n");
            Session oldSession = Session.Load(old);
            var (oldFindings, oldSkipped, _) = Program.Evaluate(oldSession);
            string Reason(System.Collections.Generic.List<(string Name, string Missing)> skipped, ICheck check) => skipped.SingleOrDefault(s => s.Name == check.Name).Missing ?? "";
            Require(Reason(oldSkipped, new SelectedActivitiesHadAnEligibleOffer()).Contains("_offer", StringComparison.Ordinal),
                "an old schema without offer columns passed the offer check instead of skipping it by name");
            Require(Reason(oldSkipped, new AttemptIdentitiesAgreeAcrossRecords()).Contains("activity_attempt_id", StringComparison.Ordinal)
                && Reason(oldSkipped, new ControlGrantsAreCompatible()).Contains("control_grant_id", StringComparison.Ordinal),
                "an old schema without attempt and grant columns did not name them when skipping the identity checks");
            Require(!oldFindings.Any(f => f.Check == new SelectedActivitiesHadAnEligibleOffer().Name), "a skipped check still produced findings");
            Require(JoinAttemptEvidence.Describe(old, oldSession, false).StartsWith("attempts  unavailable — this session predates attempt identity", StringComparison.Ordinal),
                "the identity view read an old schema as having no attempts rather than no attempt identity");

            var (rows, _) = IdentityScenario();
            string noSidecar = WriteIdentitySession(files, IdentityColumns, rows, events: null);
            Session partial = Session.Load(noSidecar);
            var (_, partialSkipped, _) = Program.Evaluate(partial);
            Require(Reason(partialSkipped, new AttemptIdentitiesAgreeAcrossRecords()).Contains("-events.jsonl", StringComparison.Ordinal)
                && Reason(partialSkipped, new ControlGrantsAreCompatible()).Contains("-events.jsonl", StringComparison.Ordinal),
                "a capture missing its sidecar passed the event-based identity checks instead of skipping them by name");
            Require(Reason(partialSkipped, new SelectedActivitiesHadAnEligibleOffer()).Length == 0,
                "the row-only offer check was skipped for a missing sidecar it does not read");
            Require(JoinAttemptEvidence.Describe(noSidecar, partial, false).Contains("no -events.jsonl sidecar", StringComparison.Ordinal),
                "the identity view did not say its sidecar was missing");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    private static void MultiRunStatesProvenanceBeforeAnyRun()
    {
        string a = Path.GetTempFileName(), b = Path.GetTempFileName(), c = Path.GetTempFileName();
        try
        {
            const string recorded = "# schema=0.10.0\n# started_utc=2026-09-11T18:30:04.0000000Z\n# terraria=v1.4.4.9;tml_assembly=1.4.4.9;runtime=8.0.0;os=Unix\n# mods=ModLoader@2026.7.3.0;AICompanion@0.15.0\ntick\n1\n";
            File.WriteAllText(a, recorded);
            File.WriteAllText(c, recorded);
            File.WriteAllText(b, "# schema=0.21.0\n# mods=ModLoader@2026.7.3.0;AICompanion@0.22.17\ntick\n1\n");
            string differing = MultiRunReport.Of(new[] { a, b });
            string an = Path.GetFileName(a), bn = Path.GetFileName(b);
            Require(differing.Contains($"differs     schema: 0.10.0 ({an}) · 0.21.0 ({bn})", StringComparison.Ordinal)
                && differing.Contains("differs     mods:", StringComparison.Ordinal),
                "runs recorded by different schemas and mod builds were not reported as differing");
            Require(differing.Contains("shared      tml_assembly=1.4.4.9 among the 1 run(s) that record it", StringComparison.Ordinal)
                && differing.Contains($"unrecorded  tml_assembly in {bn}", StringComparison.Ordinal),
                "the loader assembly packed inside the terraria line was not read, or its absence in one run was filled in");
            Require(!differing.Contains("tml_assembly=1.4.4.9;runtime", StringComparison.Ordinal), "the packed provenance line was compared as one opaque value");
            Require(differing.IndexOf("differs", StringComparison.Ordinal) < differing.IndexOf("\nrun  ", StringComparison.Ordinal),
                "provenance was stated after the runs it qualifies");
            string same = MultiRunReport.Of(new[] { a, c });
            Require(!same.Contains("differs", StringComparison.Ordinal) && same.Contains("shared      schema=0.10.0", StringComparison.Ordinal),
                "identically recorded runs were reported as differing");
        }
        finally { File.Delete(a); File.Delete(b); File.Delete(c); }
    }

    /// <summary>
    /// A 0.28.0 capture names its source revision and configuration before its rows and its closure after them. The
    /// trailer must be read as metadata rather than as a ragged row, from the whole file and from the tail alone; a
    /// missing end marker must be an interrupted capture and a wrong row count a contradiction; an older capture skips
    /// the closure check by name; and a multi-run report must refuse a join across runs whose code differs or is unproven,
    /// while a configuration difference alone is stated without refusing.
    /// </summary>
    private static void ACaptureStatesItsSourceAndWhetherItClosed()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            const string configuration = "character;mining=Opportunistic;chopping=Opportunistic;hunting=true;pot_breaking=true;torch_placement=true;distance_mode=Standard;inspector=true;record_telemetry=true";
            string Write(string schema, string trailer, string source = $"# source_revision={revision};tree=clean\n", string config = $"# config={configuration}\n")
            {
                string path = Path.GetTempFileName(); files.Add(path);
                File.WriteAllText(path, $"# schema={schema}\n{source}{config}tick\twall_elapsed_ms\n1\t16\n2\t32\n{trailer}");
                return path;
            }
            Finding[] Found(string path) => new TheCaptureWasClosed().Run(Session.Load(path)).ToArray();
            string Describe(Finding[] found) => found.Length == 0 ? " (nothing fired)" : " (fired: " + string.Join(" | ", found.Select(f => $"{f.Severity} {f.Title}")) + ")";

            string closed = Write("0.28.0", "# end=world-unload;rows=2\n");
            Session session = Session.Load(closed);
            Require(session.Count == 2 && session.Ragged == 0 && session.Metadata.TryGetValue("end", out string? end) && end == "world-unload;rows=2",
                $"the closing trailer was not read as metadata beside two whole rows (rows {session.Count}, ragged {session.Ragged})");
            Require(Found(closed).Length == 0, "a normally closed capture holding the rows its end marker names was reported" + Describe(Found(closed)));
            var metadata = Session.ReadMetadata(closed);
            Require(metadata.TryGetValue("end", out string? tail) && tail == "world-unload;rows=2" && metadata.TryGetValue("source_revision", out string? recorded) && recorded == $"{revision};tree=clean",
                "reading metadata without the rows missed the closing trailer or the source revision");
            Require(DescribeSession.Of(session).Contains($"capture   source {revision} tree clean; closed world-unload;rows=2", StringComparison.Ordinal),
                "the session summary did not state the capture's source and closure: " + DescribeSession.Of(session));

            string cut = Write("0.28.0", "");
            Finding[] interrupted = Found(cut);
            Require(interrupted.Length == 1 && interrupted[0].Severity == Severity.Potential && interrupted[0].Title == "interrupted capture: the recording has no end marker",
                "a 0.28.0 capture without its end marker was not reported as an interrupted capture" + Describe(interrupted));
            Require(DescribeSession.Of(Session.Load(cut)).Contains("no end marker: interrupted capture", StringComparison.Ordinal), "the session summary did not call a capture without an end marker interrupted");
            Finding[] miscounted = Found(Write("0.28.0", "# end=world-unload;rows=5\n"));
            Require(miscounted.Length == 1 && miscounted[0].Severity == Severity.Definitive && miscounted[0].Title == "the end marker names 5 row(s) and the file holds 2",
                "an end marker naming rows the file does not hold was not Definitive" + Describe(miscounted));

            string old = Write("0.27.0", "", source: "", config: "");
            var (oldFindings, oldSkipped, _) = Program.Evaluate(Session.Load(old));
            Require((oldSkipped.SingleOrDefault(s => s.Name == new TheCaptureWasClosed().Name).Missing ?? "").Contains("schema 0.28.0", StringComparison.Ordinal)
                    && !oldFindings.Any(f => f.Check == new TheCaptureWasClosed().Name),
                "a capture older than end markers was called interrupted, or skipped without naming the schema");
            Require(DescribeSession.Of(Session.Load(old)).Contains("source revision and closure unrecorded", StringComparison.Ordinal), "an old capture's summary did not say its source and closure are unrecorded");

            string sameBuild = Write("0.28.0", "# end=world-unload;rows=2\n");
            string joined = MultiRunReport.Of(new[] { closed, sameBuild });
            Require(joined.Contains($"joinable    every run records the clean source revision {revision}", StringComparison.Ordinal) && !joined.Contains("refused", StringComparison.Ordinal),
                "two runs recording one clean source revision were not reported joinable: " + joined);
            string otherCode = Write("0.28.0", "# end=world-unload;rows=2\n", source: "# source_revision=fedcba9876543210fedcba9876543210fedcba98;tree=clean\n");
            string differing = MultiRunReport.Of(new[] { closed, otherCode });
            Require(differing.Contains("differs     source_revision:", StringComparison.Ordinal)
                    && differing.Contains("refused     cross-run joins: the runs were recorded by different code or loaders (source_revision)", StringComparison.Ordinal),
                "runs recorded from different source revisions were not refused a cross-run join: " + differing);
            string unknownA = Write("0.28.0", "# end=world-unload;rows=2\n", source: "# source_revision=unknown;tree=unknown\n");
            string unknownB = Write("0.28.0", "# end=world-unload;rows=2\n", source: "# source_revision=unknown;tree=unknown\n");
            string unproven = MultiRunReport.Of(new[] { unknownA, unknownB });
            Require(unproven.Contains("do not record a clean source revision", StringComparison.Ordinal) && unproven.Contains(Path.GetFileName(unknownA), StringComparison.Ordinal),
                "runs agreeing only that their source is unknown were joined as one build: " + unproven);
            string reconfigured = Write("0.28.0", "# end=world-unload;rows=2\n", config: $"# config={configuration.Replace("pot_breaking=true", "pot_breaking=false", StringComparison.Ordinal)}\n");
            string configured = MultiRunReport.Of(new[] { closed, reconfigured });
            Require(configured.Contains("differs     pot_breaking: true", StringComparison.Ordinal) && configured.Contains("joinable", StringComparison.Ordinal),
                "a configuration difference was not stated, or refused a join that only differing code refuses: " + configured);
            string newStance = Write("0.28.0", "# end=world-unload;rows=2\n", config: $"# config={configuration.Replace("hunting=true", "combat=true", StringComparison.Ordinal)}\n");
            string crossStance = MultiRunReport.Of(new[] { closed, newStance });
            Require(crossStance.Contains("unrecorded  hunting in ", StringComparison.Ordinal) && crossStance.Contains("unrecorded  combat in ", StringComparison.Ordinal) && crossStance.Contains("joinable", StringComparison.Ordinal),
                "a stance-key change across runs was not stated per run with the join kept: " + crossStance);

            // The producer literals these rules rest on.
            string project = File.ReadAllText("AICompanion.csproj");
            string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
            string flush = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "FlushDiagnosticRecords.cs"));
            Require(project.Contains("git rev-parse HEAD", StringComparison.Ordinal) && project.Contains("BeforeTargets=\"GetAssemblyAttributes\"", StringComparison.Ordinal)
                    && project.Contains("<_Parameter1>SourceRevision</_Parameter1>", StringComparison.Ordinal) && project.Contains("<_Parameter1>SourceTree</_Parameter1>", StringComparison.Ordinal),
                "the build no longer stamps the source revision and tree state the recorder reads");
            Require(telemetry.Contains("QueueDiagnosticRecords.TryEnqueueTsv($\"# source_revision={SourceProvenance}\");", StringComparison.Ordinal)
                    && telemetry.Contains("$\"character;mining={Mining};chopping={Chopping};combat=", StringComparison.Ordinal)
                    && telemetry.Split("# end=").Length == 1 && telemetry.Contains("diagnosticWriter.Stop(TimeSpan.FromMilliseconds(100), reason, rowsWritten);", StringComparison.Ordinal)
                    && flush.Split("# end=").Length == 2 && flush.Contains("tsv.WriteLine($\"# end={endReason}", StringComparison.Ordinal),
                "the recorder's source line, configuration shape or single end-marker writer has changed");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>
    /// A 0.29.0 capture states what recording cost, what it did not keep and the bounds of what it keeps. The first row
    /// carries no cost because a row cannot time its own write; any dropped occurrence is Potential and names the tick the
    /// drops began, not the tick they were noticed; an older capture skips the check by naming a missing column and says
    /// its cost is unrecorded rather than zero.
    /// </summary>
    private static void RecordingStatesItsCostWhatItDroppedAndWhatItKeeps()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            const string header = "tick\twall_elapsed_ms\trecord_ms\tevents_written\tevents_dropped\tevents_coalesced\tterrain_evictions\n";
            string Write(string schema, string rows, string columns = header, string retention = "")
            {
                string path = Path.GetTempFileName(); files.Add(path);
                File.WriteAllText(path, $"# schema={schema}\n{retention}{columns}{rows}");
                return path;
            }
            string Describe(Finding[] found) => found.Length == 0 ? " (nothing fired)" : " (fired: " + string.Join(" | ", found.Select(f => $"{f.Severity} {f.Title}")) + ")";

            string healthy = Write("0.29.0", "1\t16\t-\t3\t0\t0\t0\n2\t32\t0.020\t4\t0\t128\t0\n3\t48\t0.040\t5\t0\t128\t1\n",
                retention: "# retention=rows=one-per-companion-ai-tick;events=every-occurrence-offered;terrain-snapshots-remembered=8192\n");
            Session session = Session.Load(healthy);
            Require(session["record_ms"].Unparsed == 0, "the first row's absent cost was counted as an unparsed number");
            Finding[] clean = new NoOccurrenceWasDropped().Run(session).ToArray();
            Require(clean.Length == 0, "a capture that dropped nothing was reported" + Describe(clean));
            string summary = DescribeSession.Of(session);
            Require(summary.Contains("recording 0.020 p50, 0.020 p95, 0.040 max ms per row over 2 measured row(s); by the last row 5 occurrence(s) written, 0 dropped, 128 contact(s) coalesced, 1 terrain snapshot(s) evicted", StringComparison.Ordinal)
                    && summary.Contains("retention rows=one-per-companion-ai-tick;events=every-occurrence-offered;terrain-snapshots-remembered=8192", StringComparison.Ordinal),
                "the summary did not state recording cost over the measured rows only, the closing totals, or the retention statement: " + summary);

            Finding[] dropped = new NoOccurrenceWasDropped().Run(Session.Load(Write("0.29.0", "1\t16\t-\t3\t0\t0\t0\n2\t32\t0.020\t4\t2\t0\t0\n3\t48\t0.040\t4\t7\t0\t0\n"))).ToArray();
            Require(dropped.Length == 1 && dropped[0].Severity == Severity.Potential && dropped[0].Title == "the occurrence stream stopped: 7 occurrence(s) dropped from tick 2"
                    && dropped[0].Detail.Contains("having written 4 record(s)", StringComparison.Ordinal),
                "drops were not one Potential naming the closing count and the tick they began" + Describe(dropped));

            string old = Write("0.28.0", "1\t16\n", columns: "tick\twall_elapsed_ms\n");
            var (oldFindings, oldSkipped, _) = Program.Evaluate(Session.Load(old));
            Require((oldSkipped.SingleOrDefault(s => s.Name == new NoOccurrenceWasDropped().Name).Missing ?? "").Contains("events_dropped", StringComparison.Ordinal)
                    && !oldFindings.Any(f => f.Check == new NoOccurrenceWasDropped().Name),
                "a capture older than loss counts was judged, or skipped without naming the column");
            Require(DescribeSession.Of(Session.Load(old)).Contains("recording cost and loss counts unrecorded (written from schema 0.29.0)", StringComparison.Ordinal),
                "an old capture's summary did not say its cost and loss are unrecorded");

            // The producer literals these rules rest on.
            string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
            string events = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordGodsEyeEvents.cs"));
            // Record is timed on raw clock ticks since 0.48.0, so the recorder's own profiler section sits inside
            // `record_ms` exactly; the pin follows the literal rather than the old Stopwatch field.
            Require(telemetry.IndexOf("long recordStarted = Stopwatch.GetTimestamp();", StringComparison.Ordinal) > telemetry.IndexOf("public static void Record(CompanionNPC companion)", StringComparison.Ordinal)
                    && telemetry.Contains("lastRecordMs = (Stopwatch.GetTimestamp() - recordStarted) * BrainSections.MillisecondsPerTimestamp;", StringComparison.Ordinal)
                    && telemetry.Contains("\\trecord_ms\\tevents_written\\tevents_dropped\\tevents_coalesced\\tterrain_evictions", StringComparison.Ordinal)
                    && telemetry.Contains("events-dropped={GodsEyeEvents.Dropped}", StringComparison.Ordinal) && telemetry.Contains("# retention=", StringComparison.Ordinal),
                "the recorder no longer times Record, writes the loss columns in order, restates them on closure, or states its retention");
            Require(events.Contains("if (!Accepting()) return;", StringComparison.Ordinal) && !events.Contains("if (!Active) return;", StringComparison.Ordinal)
                    && events.Contains("disabled = true;", StringComparison.Ordinal) && events.Contains("Coalesced += cosmeticContacts;", StringComparison.Ordinal),
                "an occurrence producer bypasses the drop count, or a stopped stream or coalesced contact is no longer counted");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>
    /// The consistent identity capture, damaged the ways a real capture is damaged, read by every check and every reader a
    /// report or playtest page runs. None may throw, none may call damage a contradiction — a kill leaves rows and occurrences
    /// flushed at different moments, a corrupt cell is not a record that disagrees, a respawned brain restarts its identities,
    /// an old producer never wrote the columns — and each must say what it could no longer measure.
    /// </summary>
    private static void ADamagedCaptureReducesCoverageAndInventsNoContradiction()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            string Capture(Func<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>, System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>>? rowsEdit = null,
                Action<System.Collections.Generic.List<FixtureEvent>>? eventsEdit = null, string schema = "0.29.0", bool closed = true, bool ended = true, string[]? columns = null)
            {
                var (rows, events) = IdentityScenario();
                rows = rowsEdit?.Invoke(rows) ?? rows;
                eventsEdit?.Invoke(events);
                string tsv = WriteIdentitySession(files, columns ?? IdentityColumns, rows, events, schema, closed);
                if (ended) File.AppendAllText(tsv, $"# end=world-unload;rows={rows.Count}\n");
                return tsv;
            }
            (Finding[] Findings, System.Collections.Generic.List<(string Name, string Missing)> Skipped, string Text) Report(string tsv)
            {
                Session session = Session.Load(tsv);
                var (findings, skipped, _) = Program.Evaluate(session);
                string html = Path.Combine(Path.GetTempPath(), $"aic-damaged-{Guid.NewGuid():N}.html"); files.Add(html);
                WritePlaytestHtml.Write(html, new[] { tsv });
                // Every reader the ordinary report prints, including the two pages added on 22 September
                // 2026: a page that throws on a cut row takes the whole report down with it, and a
                // damaged capture is the ordinary case rather than the exotic one. The parity table is
                // pointed at the repository's own README, which is what a run from the root sees.
                string text = DescribeSession.Of(session) + DescribeGodsEyeEvents.Of(tsv, true) + JoinAttemptEvidence.Describe(tsv, session, true)
                    + Chronicle.Of(session, true) + MultiRunReport.Of(new[] { tsv })
                    + WriteCourseTimeline.Of(session, true)
                    + WriteBehaviourParity.Of(session, findings, skipped)
                    // The one-tick explanation and its window form read the same damaged rows and sidecar.
                    + ExplainOneTick.Of(session, 20) + ExplainOneTick.Window(session, 0, 40);
                // A tick's picture over the same damage: a refusal naming what is missing is the one allowed outcome
                // besides a picture, because `--pictures` prints that refusal on the finding's line; anything else escaping is a defect.
                string picture = Path.Combine(Path.GetTempPath(), $"aic-damaged-{Guid.NewGuid():N}.png"); files.Add(picture);
                try { DrawTickPicture.Draw(session, 20, picture); } catch (InvalidDataException) { }
                return (findings.ToArray(), skipped, text);
            }
            void NoContradiction(string variant, Finding[] findings)
                => Require(!findings.Any(f => f.Severity == Severity.Definitive),
                    $"{variant}: damage to the capture was reported as a contradiction: " + string.Join(" | ", findings.Where(f => f.Severity == Severity.Definitive).Select(f => $"{f.Check}: {f.Title}")));
            bool Has(Finding[] findings, string title) => findings.Any(f => f.Severity == Severity.Potential && f.Title.StartsWith(title, StringComparison.Ordinal));
            string Skip(System.Collections.Generic.List<(string Name, string Missing)> skipped, ICheck check) => skipped.SingleOrDefault(s => s.Name == check.Name).Missing ?? "";
            System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> Upto(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> rows, long tick)
                => rows.Where(r => long.Parse(r["tick"]) <= tick).ToList();

            var whole = Report(Capture());
            NoContradiction("the whole capture", whole.Findings);
            Require(!Has(whole.Findings, "interrupted capture") && Skip(whole.Skipped, new AttemptIdentitiesAgreeAcrossRecords()).Length == 0,
                "the whole capture was called interrupted, or its identity check did not run, so the damaged variants below would prove nothing");

            // Killed mid-write: the last row cut inside its cells, the sidecar cut inside a line, no closure on either.
            string killed = Capture(eventsEdit: e => e.RemoveAll(x => x.Tick > 32), closed: false, ended: false);
            string[] killedRows = File.ReadAllLines(killed);
            killedRows[^1] = killedRows[^1][..4];
            File.WriteAllLines(killed, killedRows);
            File.AppendAllText(ReadGodsEyeEvents.PathFor(killed), "{\"v\":1,\"seq\":");
            var interrupted = Report(killed);
            NoContradiction("a capture killed mid-write", interrupted.Findings);
            Require(Has(interrupted.Findings, "interrupted capture: the recording has no end marker") && Has(interrupted.Findings, "some rows held the wrong number of cells")
                    && interrupted.Text.Contains("1 malformed line", StringComparison.Ordinal) && interrupted.Text.Contains("end=missing", StringComparison.Ordinal),
                "a capture killed mid-write did not state the missing closure, the cut row and the cut occurrence line");

            // The two streams flush separately, so a kill can leave either one ahead of the other.
            var sidecarAhead = Report(Capture(rowsEdit: rows => Upto(rows, 27), closed: false, ended: false));
            NoContradiction("occurrences written past the last row", sidecarAhead.Findings);
            Require(Has(sidecarAhead.Findings, "interrupted capture"), "a capture whose rows stop before its occurrences did not say it was interrupted");
            var rowsAhead = Report(Capture(eventsEdit: e => e.RemoveAll(x => x.Tick > 20), closed: false, ended: false));
            NoContradiction("rows written past the last occurrence", rowsAhead.Findings);
            Require(rowsAhead.Text.Contains("end=missing", StringComparison.Ordinal), "a sidecar that stopped before the rows did not say it never closed");

            // Corrupt cells and a damaged occurrence stream: one unreadable open attempt inside attempt 5, one unreadable
            // comparison on a strike's tick, a lost occurrence and a garbage line.
            string corrupt = Capture(rowsEdit: rows =>
            {
                rows.First(r => r["tick"] == "15")["activity_attempt_id"] = "garbled";
                rows.First(r => r["tick"] == "12")["choice_id"] = "garbled";
                // Attempt 8's strike at 32 is right only because its row names attempt 8 closed on that tick.
                rows.First(r => r["tick"] == "32")["attempt_end_id"] = "garbled";
                return rows;
            });
            string sidecar = ReadGodsEyeEvents.PathFor(corrupt);
            var occurrences = File.ReadAllLines(sidecar).ToList();
            occurrences[5] = "not-json";
            occurrences.RemoveAt(3);
            File.WriteAllLines(sidecar, occurrences);
            var malformed = Report(corrupt);
            NoContradiction("unreadable cells and a damaged occurrence stream", malformed.Findings);
            Require(Has(malformed.Findings, "the column activity_attempt_id held 1 cell(s) that are not a number") && Has(malformed.Findings, "the column choice_id held 1 cell(s) that are not a number")
                    && Has(malformed.Findings, "the column attempt_end_id held 1 cell(s) that are not a number")
                    && malformed.Text.Contains("1 malformed line", StringComparison.Ordinal),
                "unreadable cells or a garbage occurrence line were not named as reduced coverage");

            // A respawned companion builds a new brain: comparison, grant and activity identities restart, attempt identities continue.
            var reused = Report(Capture(rowsEdit: rows =>
            {
                for (long t = 42; t <= 50; t++)
                {
                    long open = t is >= 43 and <= 47 ? 10 : 0;
                    bool concluded = t >= 48;
                    rows.Add(new()
                    {
                        ["tick"] = t.ToString(), ["wall_elapsed_ms"] = (t * 16).ToString(), ["action"] = "mine", ["choice_id"] = "1", ["choice_fresh"] = t == 42 ? "1" : "0",
                        ["control_grant_id"] = (t - 41).ToString(), ["activity_attempt_id"] = open.ToString(), ["attempt_end_id"] = concluded ? "10" : "0",
                        ["attempt_end_activity_id"] = concluded ? "1" : "0", ["attempt_end_activity"] = concluded ? "mine" : "none", ["attempt_end_tick"] = concluded ? "48" : "-1",
                        ["mine_offer"] = "Usable:proven-pose", ["chop_offer"] = "Unresolved:approach-undecided",
                    });
                }
                return rows;
            }, eventsEdit: e =>
            {
                e.Add(Grant(43, 2, 1, 10, "Executing", "travel", "WorkTool"));
                e.Add(Strike(45, "pickaxe", 1, 1, 1, "Damaged", 10));
                e.Add(Outcome(48, 10, 1, "mine", "Gathering", 43, 48, "Complete", "Companion", "tracked-vein-observed-clear", 1));
            }));
            NoContradiction("a respawned brain restarting its comparison, grant and activity identities", reused.Findings);

            string[] oldColumns = { "tick", "wall_elapsed_ms", "action", "choice_id", "choice_fresh", "mine_offer", "chop_offer" };
            var old = Report(Capture(schema: "0.20.0", columns: oldColumns, ended: false));
            NoContradiction("a capture older than attempt identity", old.Findings);
            Require(Skip(old.Skipped, new AttemptIdentitiesAgreeAcrossRecords()).Contains("activity_attempt_id", StringComparison.Ordinal)
                    && Skip(old.Skipped, new ControlGrantsAreCompatible()).Contains("control_grant_id", StringComparison.Ordinal)
                    && Skip(old.Skipped, new TheCaptureWasClosed()).Contains("schema 0.28.0", StringComparison.Ordinal),
                "an old capture ran or silently passed a check whose evidence it never recorded");

            // A sidecar the recorder never opened reads exactly like a stream in which nothing happened.
            string unopened = Capture();
            File.WriteAllText(ReadGodsEyeEvents.PathFor(unopened), "");
            var empty = Report(unopened);
            NoContradiction("a sidecar holding no session record", empty.Findings);
            foreach (ICheck check in new ICheck[] { new AttemptIdentitiesAgreeAcrossRecords(), new ControlGrantsAreCompatible(), new CompletedTransferClaimsWereReceived() })
                Require(Skip(empty.Skipped, check).Contains("no session record", StringComparison.Ordinal),
                    $"'{check.Name}' ran over a sidecar the recorder never opened, so an empty stream would read as a clean one; skipped as '{Skip(empty.Skipped, check)}'");
        }
        finally { foreach (string file in files) File.Delete(file); }
    }

    /// <summary>
    /// The identity rules restate producer facts, so the literals they rest on are pinned here: a
    /// renamed owner, a reordered grant payload or a new eligibility name fails this test rather than
    /// quietly turning a rule into one that can never fire.
    /// </summary>
    private static void IdentityRulesStillMatchTheProducer()
    {
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(parts));
        string tick = Source("Companion", "Brain", "CoordinateBrainTick.cs");
        string events = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordGodsEyeEvents.cs");
        string telemetry = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs");
        string offers = Source("Companion", "Brain", "Activities", "ClassifyOffersAndAttempts.cs");
        string owner = Source("Companion", "Brain", "Infrastructure", "Selection", "OwnCurrentActivity.cs");
        // Every ordinary owner is assigned in the coordinator as a literal: Navigate's three as `owner = "…"`, and the evade
        // step's as `movementOwner = "evade"`, which bends the job's own controls after Navigate returns.
        foreach (string ordinary in ControlGrantsAreCompatible.OrdinaryOwners)
            Require(tick.Contains($"owner = \"{ordinary}\"", StringComparison.Ordinal) || tick.Contains($"Owner = \"{ordinary}\"", StringComparison.Ordinal),
                $"the ordinary movement owner '{ordinary}' is no longer issued by the coordinator");
        Require(tick.Contains("\"downed\", HandGrant.Unavailable", StringComparison.Ordinal) && tick.Contains("HandGrant.WorkTool : HandGrant.Available", StringComparison.Ordinal)
                && tick.Contains("\"follow-recovery-flight\", RecoveryVelocity", StringComparison.Ordinal),
            "the coordinator's downed, work-tool or recovery grant no longer has the shape the grant rules assume");
        // The safety measure files a row per owner in a fixed set, and since 15 September 2026 every owner in it is retired: no
        // safety response takes the body. combat-spacing and combat-reflex went when safety became a layer on the job, and
        // survival-escape when every liquid became air to the orb, which removed the escape and the file that issued it. The
        // pinned first orb play holds the first two, so all three stay counted and classified for older captures, and each must
        // be declared retired and issued nowhere by the coordinator, so a revived response is classified on purpose rather than
        // by an old list. The safety folder is checked too, because a revived response would be written there first.
        string safetyFolder = string.Concat(Directory.GetFiles(Path.Combine("Companion", "Brain", "SharedBehaviours", "Safety"), "*.cs")
            .Select(File.ReadAllText));
        foreach (string measured in MeasureSafetyShare.SafetyOwners)
        {
            Require(ControlGrantsAreCompatible.SuspendingOwners.Contains(measured),
                $"the safety measure counts '{measured}', which the grant rules no longer call suspending");
            Require(MeasureSafetyShare.RetiredOwners.Contains(measured),
                $"the safety measure counts '{measured}' as live, but no safety response takes the body any more; declare it retired or name the response that issues it");
            Require(!tick.Contains($"\"{measured}\"", StringComparison.Ordinal) && !safetyFolder.Contains($"\"{measured}\"", StringComparison.Ordinal),
                $"'{measured}' is declared retired, but the coordinator or the safety folder issues it again; classify the revived response on purpose");
        }
        // The evade row counts the ordinary owner the coordinator names a bent tick with, so that name must still be ordinary.
        Require(ControlGrantsAreCompatible.OrdinaryOwners.Contains(MeasureSafetyShare.EvadeOwner),
            $"the safety measure counts '{MeasureSafetyShare.EvadeOwner}' ticks, which the grant rules no longer call an ordinary owner");
        Require(events.Contains("grant-id={id};grant-tick={tick};activity-id={activityId};attempt-id={attemptId};activity-phase={activityPhase};requested-owner={requestedOwner}", StringComparison.Ordinal)
                && events.Contains("attempt={outcome.Attempt};choice-id={choiceId};activity-id={activityId};activity-attempt-id={activityAttemptId}", StringComparison.Ordinal)
                && new[] { Source("Companion", "Brain", "Activities", "Gathering", "MineOre.cs"), Source("Companion", "Brain", "Activities", "Gathering", "ChopTree.cs") }
                    .All(striker => striker.Contains("owner.AttemptOpen ? owner.AttemptId : 0", StringComparison.Ordinal))
                && events.Contains("attempt-id={attemptId};activity-id={activityId};family={family};start-tick={startTick};end-tick={endTick}", StringComparison.Ordinal)
                && events.Contains("attemptId <= lastAttemptRecorded", StringComparison.Ordinal),
            "an occurrence payload or the outcome cursor the identity join reads has changed");
        // The offer column's producer moved from the retired chooser's eligibility to the course's own
        // three-valued census admission at schema 0.44.0, and this pin moved with it rather than being
        // relaxed. What the eligibility check parses is unchanged — `<verdict>:<reason>` with
        // `not-compared` for an activity nothing minted an opportunity for — so the two literals pinned
        // here are the ones that decide that shape: the verdict ladder that writes the part before the
        // colon, and the default that stands when no domain answered. This pin firing is what caught the
        // change rather than a capture quietly failing to parse months later, which is the whole reason
        // producer literals are pinned at all.
        // The producer moved again in the same afternoon, from an inline block in the recorder to the
        // shared `ReadCourseWorthPerActivity` the recorder, the overlay and the inspector all read, so the
        // pin follows it to its new file rather than being loosened to something both would satisfy. The
        // shape the eligibility check parses is unchanged — `<verdict>:<reason>`, with `not-compared` for
        // an activity nothing minted an opportunity for — and these are the two literals that decide it:
        // the verdict ladder that writes the part before the colon, and the one constant the default word
        // now lives in, so a rename of that word fails here instead of in a capture months later.
        string worth = Source("Companion", "Brain", "Infrastructure", "Diagnostics", "ReadCourseWorthPerActivity.cs");
        Require(worth.Contains("admitted.Usable > 0 ? \"Usable\"", StringComparison.Ordinal)
                && worth.Contains("public const string NotCompared = \"not-compared\"", StringComparison.Ordinal)
                && telemetry.Contains("worth.Offer + \":\" + worth.OfferReason", StringComparison.Ordinal),
            "the offer column format the eligibility check parses has changed");
        Require(offers.Contains("NoOpportunity, PolicyForbidden, KnownUnusable, Unresolved, Usable, Deferred }", StringComparison.Ordinal),
            "the offer eligibility names have changed");
        Require(owner.Contains("private static long nextAttemptId", StringComparison.Ordinal),
            "attempt identities are no longer process-wide, which the identity rules key on");
        string npc = Source("Companion", "CharacterBody", "CompanionNPC.cs");
        string collection = Source("Companion", "Brain", "Activities", "NearbyAssistance", "CollectNearbyItems.cs");
        Require(events.Contains("interruption-is-not-failure=true;claimed-yield-type={claimedYieldType};claimed-yield-quantity={claimedYieldQuantity}", StringComparison.Ordinal)
                && events.Contains("stack={item.stack};collection-attempt-id={collectionAttemptId}", StringComparison.Ordinal),
            "the claimed-yield or pickup payload the transfer check reads has changed");
        Require(npc.Contains("collect.ClaimsDrop(item) ? owner.AttemptId : 0", StringComparison.Ordinal) && npc.IndexOf("ClaimsDrop(item)", StringComparison.Ordinal) < npc.IndexOf("Bag.Collect(item, player)", StringComparison.Ordinal)
                && collection.Contains("AttemptAttribution.Shared, drop.Type, received)", StringComparison.Ordinal) && collection.Contains("AttemptAttribution.NotApplicable, drop.Type, received)", StringComparison.Ordinal)
                && collection.Contains("drop.Bag.TransferredSince(drop.TransferMark, drop.Item)", StringComparison.Ordinal),
            "a pickup no longer names its collection attempt before the transfer, or collection no longer claims what its own drop's transfers delivered");
    }

    /// <summary>
    /// The two travel questions, over three captures that separate the three things a report must never confuse: a
    /// capture carrying journeys and stops, the same capture stamped one schema older, and a capture of clean travel.
    ///
    /// The middle one is the case that matters most and it is the reason this fixture exists rather than a single happy
    /// path. Every capture on disk today predates these occurrences, so the skip path is the only one a real session can
    /// exercise, and a skip that quietly returned no findings would be indistinguishable from a clean run — which is the
    /// failure the coverage block exists to prevent and the failure this folder has already paid for once.
    ///
    /// The third asks the opposite question of the first: a session that travelled well must produce no stop finding and
    /// must still say so, because "no stop was recorded" over real travel is a result and over no travel is silence.
    /// </summary>
    private static void TravelIsReadPerJourneyAndSkippedByNameOnAnOlderCapture()
    {
        var files = new System.Collections.Generic.List<string>();
        try
        {
            // A distinct stem per variant: the event reader caches the last sidecar it read while the path, write time
            // and length are unchanged, so rewriting one in place can serve a stale log to the next variant.
            string Capture(string schema, string rows, string events)
            {
                string stem = Path.Combine(Path.GetTempPath(), $"aic-travel-{Guid.NewGuid():N}");
                string tsv = stem + ".tsv";
                files.Add(tsv);
                files.Add(stem + "-events.jsonl");
                File.WriteAllText(tsv, $"# schema={schema}\n# started_utc=2026-09-13T18:00:00.0000000Z\n"
                    + "# text_columns=action,control_source,nav_status\n"
                    + "tick\taction\tcontrol_source\tnav_status\tstops_per_minute\troute_speed_mean\n" + rows);
                File.WriteAllText(stem + "-events.jsonl", events);
                return tsv;
            }

            int sequence = 0;
            string Event(string json) => json.Replace("\"seq\":0", $"\"seq\":{sequence++}", StringComparison.Ordinal) + "\n";
            string Session0() => Event("{\"v\":1,\"seq\":0,\"tick\":0,\"wall_elapsed_ms\":0,\"kind\":\"session\",\"subject\":0,\"related\":\"\",\"label\":\"\",\"channel\":\"\",\"pos_x\":0,\"pos_y\":0,\"vel_x\":0,\"vel_y\":0,\"expected_x\":0,\"expected_y\":0,\"amount\":0,\"detail\":\"\"}");
            string Episode(long tick, string kind, string outcome, int planned, int actual, string player, int downed = 0)
                => Event($"{{\"v\":1,\"seq\":0,\"tick\":{tick},\"wall_elapsed_ms\":{tick * 16},\"kind\":\"route-episode\",\"subject\":1,\"related\":\"\",\"label\":\"{kind}\",\"channel\":\"{outcome}\",\"pos_x\":0,\"pos_y\":0,\"vel_x\":0,\"vel_y\":0,\"expected_x\":0,\"expected_y\":0,\"amount\":{actual},\"detail\":\"start-tick={tick - actual - downed};end-tick={tick};outcome={outcome};planned-ticks={planned};actual-ticks={actual};downed-ticks={downed};player-ticks={player};straight-tiles=10.00;path-tiles=14.00;mean-speed-px-per-tick=1.20\"}}");
            string Stop(long tick, int ticks, string reason)
                => Event($"{{\"v\":1,\"seq\":0,\"tick\":{tick},\"wall_elapsed_ms\":{tick * 16},\"kind\":\"stop\",\"subject\":1,\"related\":\"\",\"label\":\"{reason}\",\"channel\":\"{reason}\",\"pos_x\":0,\"pos_y\":0,\"vel_x\":0,\"vel_y\":0,\"expected_x\":0,\"expected_y\":0,\"amount\":{ticks},\"detail\":\"start-tick={tick - ticks};end-tick={tick};ticks={ticks};reason={reason};against-wall-throughout=True;same-segment-throughout=True;replanned-during=False;fastest-px-per-tick=0.10;threshold-px-per-tick=0.60;threshold-ticks=3;scope=ordinary-travel-owner-with-an-executable-or-direct-route\"}}");

            var travelling = new StringBuilder();
            for (int tick = 1; tick <= 120; tick++)
                travelling.AppendLine($"{tick}\twalk-with\ttravel\tExecutable\t1.50\t2.40");

            // Both stamps are derived from the gate rather than written out, because the mod's schema keeps moving and a
            // fixture holding a literal 0.31.0 reads as *older* than the gate the moment the gate passes it: the captures
            // that are meant to run would skip, and the assertions on their findings would fail for a reason that has
            // nothing to do with what they test.
            string first = TravelEvidence.First.ToString();
            string before = new Version(TravelEvidence.First.Major, TravelEvidence.First.Minor - 1, 0).ToString();

            string busy = Capture(first, travelling.ToString(),
                Session0()
                + Episode(60, "WithPlayer", "reached", 40, 180, "50")
                + Episode(90, "WithPlayer", "abandoned", 20, 40, "-")
                + Episode(110, "Exact", "reached", 30, 33, "-")
                // A journey the body died inside: 599 ticks of the clock belong to the death and are outside every
                // figure, so this must not read as a journey that took 632 ticks against 30 proven.
                + Episode(760, "Exact", "reached", 30, 33, "-", downed: 599)
                // The producer's two attributed reasons, in its own precedence: a route replaced under a
                // still body, and a body pressed against a wall for the whole stop. Anything it cannot
                // attribute becomes `other`, which is the one the pass line is named for.
                + Stop(30, 12, "against-wall")
                + Stop(70, 4, "during-replan"));
            Session busySession = Session.Load(busy);

            var journeys = new JourneysTakeTheTimeTheyWereProven();
            Require(journeys.Missing(busySession) == null, $"a capture at schema {first} with a sidecar refused the journey check");
            var byKind = journeys.Run(busySession).ToList();
            Require(byKind.Count == 2, $"three journeys over two request kinds produced {byKind.Count} finding(s) instead of one per kind");
            Finding follow = byKind.Single(f => f.Title.StartsWith("WithPlayer", StringComparison.Ordinal));
            // 220 taken against 60 proven, and the one journey the trail covered took 180 against the player's 50.
            Require(follow.Title.Contains("3.67x", StringComparison.Ordinal), $"the ratio of taken to proven ticks was not reported: {follow.Title}");
            Require(follow.Detail.Contains("3.60x", StringComparison.Ordinal), $"the player comparison was not formed over the journeys his trail covered: {follow.Detail}");
            Require(follow.Detail.Contains("tick 60", StringComparison.Ordinal), "the worst journeys were not named with their ticks");
            Require(byKind.All(f => f.Severity == Severity.Oddity), "a travel baseline was graded above an oddity without a defect behind it");
            // The two Exact journeys took 33 ticks each against 30 proven; the death inside the second contributes none
            // of its 599 ticks to that ratio, and the finding says the ticks exist rather than leaving a silent gap
            // between the reported duration and the tick stamps a reader can subtract for themselves.
            Finding exact = byKind.Single(f => f.Title.StartsWith("Exact", StringComparison.Ordinal));
            Require(exact.Title.Contains("1.10x", StringComparison.Ordinal),
                $"a death inside a journey reached the ratio of taken to proven ticks: {exact.Title}");
            Require(exact.Detail.Contains("1 of them held a downing", StringComparison.Ordinal)
                    && exact.Detail.Contains("599 tick(s)", StringComparison.Ordinal),
                $"a journey holding a death did not disclose the ticks left out of every figure: {exact.Detail}");

            var stopping = new TheBodyStopsOnItsOwnRoute();
            Require(stopping.Missing(busySession) == null, $"a capture at schema {first} with a sidecar refused the stop check");
            Finding stopped = stopping.Run(busySession).Single();
            Require(stopped.Title.Contains("2 stops", StringComparison.Ordinal) && stopped.Title.Contains("120 ticks", StringComparison.Ordinal),
                $"the stop count or the travel it is measured against was wrong: {stopped.Title}");
            // Two stops over 120 ticks of travel is one a second, which is 60 a minute.
            Require(stopped.Title.Contains("60.00 a minute", StringComparison.Ordinal), $"the rate per minute of travel was wrong: {stopped.Title}");
            Require(stopped.Detail.Contains("against-wall 1", StringComparison.Ordinal) && stopped.Detail.Contains("during-replan 1", StringComparison.Ordinal),
                $"the split by reason was not reported: {stopped.Detail}");
            Require(stopped.Detail.Contains("reads 1.50", StringComparison.Ordinal), "the recorder's own running rate was not printed beside the reader's");

            // The same evidence one schema older. Both must skip by name rather than run and find nothing.
            sequence = 0;
            string old = Capture(before, travelling.ToString(),
                Session0() + Episode(60, "WithPlayer", "reached", 40, 180, "50") + Stop(30, 12, "against-wall"));
            Session oldSession = Session.Load(old);
            foreach (ICheckCoverage check in new ICheckCoverage[] { journeys, stopping })
                Require(check.Missing(oldSession)?.Contains(first, StringComparison.Ordinal) == true,
                    "a capture older than the occurrences ran the travel check instead of skipping it by the schema that first wrote them");
            var (_, skipped, _) = Program.Evaluate(oldSession);
            Require(skipped.Any(s => s.Name == journeys.Name) && skipped.Any(s => s.Name == stopping.Name),
                "the runner did not report the travel checks in its coverage block on an older capture");

            // Clean travel: journeys that landed on their proven ticks, and no stop at all.
            sequence = 0;
            string clean = Capture(first, travelling.ToString(),
                Session0() + Episode(60, "WithPlayer", "reached", 55, 58, "56"));
            Session cleanSession = Session.Load(clean);
            Finding quiet = stopping.Run(cleanSession).Single();
            Require(quiet.Title.Contains("never stopped", StringComparison.Ordinal) && quiet.Detail.Contains("clean result over real travel", StringComparison.Ordinal),
                $"a session with real travel and no stops did not say so: {quiet.Title}");
            Require(journeys.Run(cleanSession).Single().Title.Contains("1.05x", StringComparison.Ordinal),
                "a journey that landed near its proven ticks was not reported at its real ratio");
        }
        finally
        {
            foreach (string file in files)
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    /// <summary>
    /// The weapon-knowledge check grades per-type timing and per-weapon pairing, and stays quiet
    /// where both are inside tolerance. Six bow shots landing a tick from their predicted impact
    /// produce nothing; six musket shots landing forty updates late produce a timing finding that
    /// names the standing law; six grenade shots with one flight on record produce a pairing finding.
    /// A volley sibling's shot-event without a shot record must not count as an unpaired shot.
    /// </summary>
    private static void WeaponKnowledgeCalibrationIsGraded()
    {
        string file = Path.GetTempFileName();
        string events = Path.ChangeExtension(file, null) + "-events.jsonl";
        try
        {
            var lines = new System.Collections.Generic.List<string>();
            void Add(string kind, int subject = 0, string related = "", string label = "", string channel = "",
                int amount = 0, string detail = "", long tick = 0)
                => lines.Add(System.Text.Json.JsonSerializer.Serialize(new
                {
                    v = 1,
                    seq = lines.Count,
                    tick,
                    wall_elapsed_ms = (double)tick,
                    kind,
                    subject,
                    related,
                    label,
                    channel,
                    pos_x = 0,
                    pos_y = 0,
                    vel_x = 0,
                    vel_y = 0,
                    expected_x = 0,
                    expected_y = 0,
                    amount,
                    detail
                }));
            Add("session");
            Add("npc-spawn", subject: 7, related: "3", label: "Zombie", tick: 1);
            Add("npc-spawn", subject: 8, related: "3", label: "Zombie", tick: 2);
            Add("flight-law", related: "2", label: "1", channel: "predictable", amount: 12,
                detail: "residual=0.0100;evidence=12;terms=gravity@14:0.10", tick: 3);
            Add("flight-law", related: "1", label: "2", channel: "unpredictable", amount: 4,
                detail: "residual=3.5840;evidence=4;terms=drag", tick: 4);
            for (int i = 0; i < 6; i++)
            {
                Add("shot", subject: 1, related: "7", label: "Wooden Bow", channel: $"projectile={101 + i}",
                    detail: "expected-flight-ticks=30;sequence-value=1.000;sequence-kills=0;sequence-prevented-harm=0.000",
                    tick: 10 + i);
                Add("shot-event", subject: 101 + i, label: "1", channel: "piercespent", amount: 1,
                    detail: "death=piercespent;walls=0;first-wall=-;hits=1;first-hit=type=3;tick=31;damage=10;children=0;child-types=-",
                    tick: 50 + i);
            }
            for (int i = 0; i < 6; i++)
            {
                Add("shot", subject: 1, related: "8", label: "Musket", channel: $"projectile={201 + i}",
                    detail: "expected-flight-ticks=30;sequence-value=1.000;sequence-kills=0;sequence-prevented-harm=0.000",
                    tick: 100 + i);
                Add("shot-event", subject: 201 + i, label: "2", channel: "piercespent", amount: 1,
                    detail: "death=piercespent;walls=1;first-wall=tick=20;in=9.0,0.0;out=9.0,0.0;hits=1;first-hit=type=3;tick=70;damage=8;children=0;child-types=-",
                    tick: 200 + i);
            }
            for (int i = 0; i < 6; i++)
            {
                Add("shot", subject: 1, related: "7", label: "Grenade", channel: $"projectile={301 + i}",
                    detail: "expected-flight-ticks=40;sequence-value=1.000;sequence-kills=0;sequence-prevented-harm=0.000",
                    tick: 300 + i);
                if (i == 0)
                    Add("shot-event", subject: 301, label: "3", channel: "expired", amount: 1,
                        detail: "death=expired;walls=0;first-wall=-;hits=1;first-hit=type=3;tick=42;damage=20;children=0;child-types=-",
                        tick: 350);
            }
            // A volley sibling: a flight on record with no shot of its own, which must not read as a lost pairing.
            Add("shot-event", subject: 399, label: "3", channel: "expired", amount: 0,
                detail: "death=expired;walls=0;first-wall=-;hits=0;first-hit=-;children=0;child-types=-", tick: 351);
            Add("session-end", tick: 400);
            File.WriteAllLines(events, lines);
            File.WriteAllText(file, "tick\n1\n");
            Finding[] findings = new WeaponKnowledgeIsCalibrated().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 2, $"expected the musket timing and grenade pairing findings, got {findings.Length}");
            Finding timing = findings.Single(f => f.Title.Contains("type 2", StringComparison.Ordinal));
            Require(timing.Severity == Severity.Potential, "a calibration miss is a pattern to look at, not a contradiction in the record");
            Require(timing.Title.Contains("40 updates", StringComparison.Ordinal), $"timing title lost its median gap: {timing.Title}");
            Require(timing.Detail.Contains("Law revision 1 (unpredictable", StringComparison.Ordinal),
                $"timing finding does not name the standing law: {timing.Detail}");
            Require(timing.Detail.Contains("6 of 6 first hits", StringComparison.Ordinal),
                $"timing finding lost its aimed share: {timing.Detail}");
            Finding pairing = findings.Single(f => f.Title.Contains("Grenade", StringComparison.Ordinal));
            Require(pairing.Title.Contains("1 of 6", StringComparison.Ordinal), $"pairing title lost its count: {pairing.Title}");
            Require(findings.All(f => !f.Title.Contains("type 1", StringComparison.Ordinal) && !f.Title.Contains("Wooden Bow", StringComparison.Ordinal)),
                "a calibrated type inside tolerance produced a finding");
        }
        finally
        {
            File.Delete(file);
            File.Delete(events);
        }
    }

    private static void CombatAuditSidecarIsReadBack()
    {
        string session = Path.GetTempFileName();
        string sidecar = Path.ChangeExtension(session, null) + "-combat-audit.json";
        try
        {
            object Sweep(string name, bool changed)
                => new { Objective = 0, Name = name, Factor = 2.0, Changed = changed };
            // A sidecar written before the hold and cut verdicts existed carries no such keys, so the
            // pre-hold decision is a dictionary: the reader must say "predates" rather than defaulting
            // the verdicts to held and uncut.
            var preHold = new System.Collections.Generic.Dictionary<string, object?>
            {
                ["Tick"] = 400L,
                ["Trigger"] = "commit",
                ["PlanId"] = 5,
                ["Reproduced"] = true,
                ["Diffs"] = Array.Empty<string>(),
                ["OnFront"] = true,
                ["Regret"] = 0.02,
                ["Generator"] = "none",
                ["Capped"] = false,
                ["GridStands"] = 1500,
                ["Sweep"] = new[] { Sweep("prevention", true), Sweep("prevention", true) },
            };
            var root = new
            {
                Capture = Path.ChangeExtension(session, null) + "-events.jsonl",
                Decisions = new object[]
                {
                    new
                    {
                        Tick = 100L, Trigger = "commit", PlanId = 3, Reproduced = true,
                        Diffs = Array.Empty<string>(), OnFront = true, Regret = 0.0, Generator = "none",
                        Capped = false, GridStands = 1500,
                        Sweep = new[]
                        {
                            Sweep("damage", true), Sweep("damage", true),
                            Sweep("prevention", false), Sweep("prevention", false),
                        },
                        Holds = true, HoldReason = "", Cut = false,
                    },
                    new
                    {
                        Tick = 200L, Trigger = "rescore", PlanId = 3, Reproduced = false,
                        Diffs = new[] { "damage 12.0 != 10.0" }, OnFront = false, Regret = 0.22,
                        Generator = "retreat", Capped = true, GridStands = 2000,
                        Sweep = new[] { Sweep("damage", true), Sweep("damage", true) },
                        Holds = false, HoldReason = "stall", Cut = true,
                    },
                    new
                    {
                        Tick = 300L, Trigger = "mark", PlanId = -1, Reproduced = false,
                        Diffs = new[] { "snapshot carries no committed plan; replay offers waiting" },
                        OnFront = true, Regret = 0.0, Generator = "none",
                        Capped = false, GridStands = 1500,
                        Sweep = Array.Empty<object>(),
                        Holds = true, HoldReason = "", Cut = false,
                    },
                    preHold,
                },
                Knowledge = new[]
                {
                    new
                    {
                        Type = "WoodenArrowFriendly", Shots = 10, Paired = 8,
                        PredictedHits = 8, MatchedHits = 7,
                        DamagePredicted = 120.0, DamageLanded = 118.0,
                        MeanTickError = 1.2, WallSurprise = 1, MeanFactor = 1.01,
                    },
                },
            };
            File.WriteAllText(sidecar, System.Text.Json.JsonSerializer.Serialize(root));
            string text = string.Join("\n",
                DescribeCombatAudit.Describe(session).Select(r => r.Text));
            Require(text.Contains("4 decisions audited; 2 replayed exactly, 3 on the exhaustive front, " +
                "mean regret 0.06", StringComparison.Ordinal), $"summary line wrong:\n{text}");
            Require(text.Contains("tick 200 (rescore, plan #3): replay DIVERGED — damage 12.0 != 10.0.",
                StringComparison.Ordinal), $"divergence lost its diff:\n{text}");
            Require(text.Contains("off the exhaustive front, regret 0.22, missing generator retreat " +
                "(grid capped at 2000", StringComparison.Ordinal), $"off-front lost its attribution:\n{text}");
            Require(text.Contains("commitment RELEASED (stall)", StringComparison.Ordinal),
                $"release lost its reason:\n{text}");
            Require(text.Contains("search CUT by budget", StringComparison.Ordinal), $"cut not named:\n{text}");
            Require(text.Contains("tick 300 (mark, no plan): the mark holds nothing",
                StringComparison.Ordinal), $"mark not excused:\n{text}");
            Require(text.Contains("holds: 1 of 3 committed hold (1 ungraded, sidecar predates the hold " +
                "audit); releases: stall x1.", StringComparison.Ordinal), $"hold grade wrong:\n{text}");
            Require(text.Contains("cuts: 1 cut by budget (1 sidecar entries predate the cut verdict).",
                StringComparison.Ordinal), $"cut grade wrong:\n{text}");
            Require(text.Contains("offered moves change search outcomes: damage 4/4, prevention 2/4.",
                StringComparison.Ordinal), $"sweep tally wrong:\n{text}");
            Require(text.Contains("WoodenArrowFriendly: 8/10 shots paired, 7/8 predicted hits landed, " +
                "tick error 1.2, wall surprise 1, damage landed 0.98 of predicted, residual factor 1.01.",
                StringComparison.Ordinal), $"calibration wrong:\n{text}");
        }
        finally
        {
            File.Delete(session);
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }
    }

    private static void MissingCombatAuditReadsAsUnmeasured()
    {
        string session = Path.GetTempFileName();
        try
        {
            string text = string.Join("\n",
                DescribeCombatAudit.Describe(session).Select(r => r.Text));
            Require(text.Contains("unmeasured rather than clean", StringComparison.Ordinal),
                $"missing sidecar not named:\n{text}");
        }
        finally
        {
            File.Delete(session);
        }
    }

    private static void CorruptCombatAuditReadsAsUnreadable()
    {
        string session = Path.GetTempFileName();
        string sidecar = Path.ChangeExtension(session, null) + "-combat-audit.json";
        try
        {
            File.WriteAllText(sidecar, "not-json{");
            string text = string.Join("\n",
                DescribeCombatAudit.Describe(session).Select(r => r.Text));
            Require(text.Contains("unreadable", StringComparison.Ordinal)
                && text.Contains("unmeasured, not clean", StringComparison.Ordinal),
                $"corrupt sidecar not named:\n{text}");
        }
        finally
        {
            File.Delete(session);
            File.Delete(sidecar);
        }
    }

    private static void ForeignCombatAuditIsNamed()
    {
        string session = Path.GetTempFileName();
        string sidecar = Path.ChangeExtension(session, null) + "-combat-audit.json";
        try
        {
            File.WriteAllText(sidecar,
                "{\"Capture\":\"Other-events.jsonl\",\"Decisions\":[],\"Knowledge\":[]}");
            string text = string.Join("\n",
                DescribeCombatAudit.Describe(session).Select(r => r.Text));
            Require(text.Contains("the sidecar was written for Other, not for this capture",
                StringComparison.Ordinal), $"foreign sidecar not named:\n{text}");
        }
        finally
        {
            File.Delete(session);
            File.Delete(sidecar);
        }
    }

    private static void EagernessFiresWhenDangerStandsUnfought()
    {
        string file = Path.GetTempFileName();
        try
        {
            var body = new StringBuilder();
            body.AppendLine("tick\taction\tdanger\tstate\tbrain_fresh\tplayer_hit\tnear_threat\tweapon_reach\tcombat_fin");
            for (int i = 0; i < 70; i++)
                body.AppendLine($"{i}\tkeep-company\t0.50\tup\t1\t-\t5.0\t20.0\t0.80");
            File.WriteAllText(file, body.ToString());
            Finding[] findings = new CombatIsEagerWhenHeIsInDanger().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 1, $"expected one eagerness finding, got {findings.Length}");
            Require(findings[0].Severity == Severity.Potential, "eagerness is a pattern to look at, not a contradiction in the record");
            Require(findings[0].Title.Contains("70 ticks", StringComparison.Ordinal)
                && findings[0].Title.Contains("keep-company", StringComparison.Ordinal),
                $"eagerness title lost its stretch: {findings[0].Title}");
            Require(findings[0].Detail.Contains("combat scored 0.80", StringComparison.Ordinal),
                $"eagerness detail lost combat's score: {findings[0].Detail}");

            // The three excuses: combat current, danger below the brain's own unsafe line, and
            // combat disabled in the preamble. Each reads clean on the same shape.
            body = new StringBuilder();
            body.AppendLine("tick\taction\tdanger\tstate\tbrain_fresh");
            for (int i = 0; i < 70; i++)
                body.AppendLine($"{i}\tcombat\t0.50\tup\t1");
            for (int i = 70; i < 140; i++)
                body.AppendLine($"{i}\tkeep-company\t0.10\tup\t1");
            File.WriteAllText(file, body.ToString());
            findings = new CombatIsEagerWhenHeIsInDanger().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 0, $"a fought danger and a safe stretch must read clean, got {findings.Length}");

            File.WriteAllText(file, "# config=character;mining=Ask;chopping=Ask;combat=false;pot_breaking=true\n"
                + "tick\taction\tdanger\tstate\tbrain_fresh\n"
                + string.Concat(Enumerable.Range(0, 70).Select(i => $"{i}\tkeep-company\t0.90\tup\t1\n")));
            findings = new CombatIsEagerWhenHeIsInDanger().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 0, "a capture with combat disabled must not grade eagerness");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void NotFightingFiresBesideATargetInRange()
    {
        string file = Path.GetTempFileName();
        try
        {
            var body = new StringBuilder();
            body.AppendLine("tick\tfire\tnear_threat\tweapon_reach\taction\tstate\tbrain_fresh\tplan_value\tweapon");
            for (int i = 0; i < 70; i++)
                body.AppendLine($"{i}\tnot-fighting\t5.0\t20.0\tmine\tup\t1\t0.500\tWooden Bow");
            File.WriteAllText(file, body.ToString());
            Finding[] findings = new NotFightingMeansNothingToShoot().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 1, $"expected one not-fighting finding, got {findings.Length}");
            Require(findings[0].Severity == Severity.Potential, "not-fighting beside a target is a pattern to look at, not a contradiction in the record");
            Require(findings[0].Title.Contains("70 ticks not fighting", StringComparison.Ordinal),
                $"not-fighting title lost its stretch: {findings[0].Title}");
            Require(findings[0].Detail.Contains("offered a plan worth 0.500", StringComparison.Ordinal)
                && findings[0].Detail.Contains("selection still ran something else", StringComparison.Ordinal),
                $"not-fighting detail lost the selection loss: {findings[0].Detail}");

            // Fired, out of range, unmeasured, and downed all read clean on the same shape.
            body = new StringBuilder();
            body.AppendLine("tick\tfire\tnear_threat\tweapon_reach\taction\tstate\tbrain_fresh\tplan_value\tweapon");
            for (int i = 0; i < 70; i++)
                body.AppendLine($"{i}\tfired\t5.0\t20.0\tcombat\tup\t1\t0.500\tWooden Bow");
            for (int i = 70; i < 140; i++)
                body.AppendLine($"{i}\tnot-fighting\t25.0\t20.0\tmine\tup\t1\t0.000\t-");
            for (int i = 140; i < 210; i++)
                body.AppendLine($"{i}\tnot-fighting\t-\t20.0\tmine\tup\t1\t0.000\t-");
            for (int i = 210; i < 280; i++)
                body.AppendLine($"{i}\tnot-fighting\t5.0\t20.0\tmine\tdowned\t1\t0.000\t-");
            File.WriteAllText(file, body.ToString());
            findings = new NotFightingMeansNothingToShoot().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 0, $"fired, far, unmeasured and downed rows must read clean, got {findings.Length}");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void CommittedPlanWasPerformedFiresWhenTheStandIsNeverReached()
    {
        string file = Path.GetTempFileName();
        try
        {
            var body = new StringBuilder();
            body.AppendLine("tick\taction\tplan_id\tplan_stand\tnpc_px\tfire");
            for (int i = 0; i < 130; i++)
                body.AppendLine($"{i}\tcombat\t1\t800,400\t100,400\tcooldown");
            File.WriteAllText(file, body.ToString());
            Finding[] findings = new TheCommittedPlanWasPerformed().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 1, $"expected one unperformed-plan finding, got {findings.Length}");

            body = new StringBuilder();
            body.AppendLine("tick\taction\tplan_id\tplan_stand\tnpc_px\tfire");
            for (int i = 0; i < 130; i++)
                body.AppendLine($"{i}\tcombat\t1\t800,400\t800,400\tfired");
            File.WriteAllText(file, body.ToString());
            findings = new TheCommittedPlanWasPerformed().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 0, $"arriving and firing must read as performed, got {findings.Length}");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void CombatFlickerFiresOnANewPlanEveryTick()
    {
        string file = Path.GetTempFileName();
        try
        {
            var body = new StringBuilder();
            body.AppendLine("tick\taction\tplan_id");
            for (int i = 0; i < 400; i++)
                body.AppendLine($"{i}\tcombat\t{1 + i / 2}");
            File.WriteAllText(file, body.ToString());
            Finding[] findings = new CombatDoesNotFlicker().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 1, $"expected one flicker finding, got {findings.Length}");

            body = new StringBuilder();
            body.AppendLine("tick\taction\tplan_id");
            for (int i = 0; i < 400; i++)
                body.AppendLine($"{i}\tcombat\t1");
            File.WriteAllText(file, body.ToString());
            findings = new CombatDoesNotFlicker().Run(Session.Load(file)).ToArray();
            Require(findings.Length == 0, $"one held plan must read clean, got {findings.Length}");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// <c>--explain</c> on a five-tick synthetic capture whose every value is distinctive, so each line of
    /// the explanation can be traced to the one cell or occurrence that holds it.
    ///
    /// <para>The decision fixture varies <c>settled</c> and <c>release-reason</c> together, because the
    /// producer never writes both on one record and a fixture writing every payload settled cannot tell a
    /// reader that picks the right record from one that picks the wrong one. The leaders fixture puts the
    /// newest <c>decision</c> occurrence on a still-deciding tick that names none, because that is what a
    /// real capture does on most ticks. The hostile fixture spawns a hostile, damages it under its spawn
    /// identity with no slot in the damage record, and kills it, so the slot has to be learned from the
    /// spawn and the death has to retire it. And the capture carries no frame, profile, allocation or fence
    /// column at all, so every one of those must be named absent rather than printed blank.</para>
    /// </summary>
    private static void ExplainingATickPrintsWhatTheRecordHoldsAndNamesWhatItLacks()
    {
        object Field(string kind, string text) => new { Kind = kind, Text = text };
        string Event(int seq, long tick, string kind, string detail, string label = "", int subject = 0, float x = 0, float y = 0)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = tick * 16.0, kind, subject, related = "", label, channel = "",
                pos_x = x, pos_y = y, vel_x = 0f, vel_y = 0f, expected_x = 0f, expected_y = 0f, amount = 0, detail });
        string Payload(int seq, long tick, bool settled, string release)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = tick * 16.0, kind = "course-course-decision",
                subject = 0, related = "", label = "", channel = "", pos_x = 0f, pos_y = 0f, vel_x = 0f, vel_y = 0f,
                expected_x = 0f, expected_y = 0f, amount = 0, detail = "",
                payload_kind = ReadCourseDecisions.Kind, payload_version = 1, phase = "brain", observation_ordinal = tick, receipt_watermark = 0L,
                payload = new { Kind = ReadCourseDecisions.Kind, Version = 1, Fields = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["reason"] = Field("text", settled ? "course-published" : "deciding"),
                    ["activity"] = Field("text", "combat"),
                    ["settled"] = Field("flag", settled ? "true" : "false"),
                    ["purpose"] = Field("text", settled ? "fire" : ""),
                    ["steps"] = Field("integer", settled ? "3" : "0"),
                    ["orders-priced"] = Field("integer", settled ? "9" : "0"),
                    ["orders-refused"] = Field("integer", settled ? "5" : "0"),
                    ["search-exhausted"] = Field("flag", "true"),
                    ["release-reason"] = Field("text", release),
                    ["facts"] = Field("integer", "41"),
                    ["refused:target-capture-missing"] = Field("integer", settled ? "5" : "0"),
                } } });

        string tsv = Path.Combine(Path.GetTempPath(), $"aic-explain-{Guid.NewGuid():N}.tsv");
        string events = ReadGodsEyeEvents.PathFor(tsv);
        try
        {
            File.WriteAllText(tsv, "# schema=0.44.0\n"
                + "tick\taction\tchoice_id\tchoice_tick\tnpc_px\tplayer_px\tintent_region\tspot\tlookahead\tbrain_ms\tdecide_ms\tthreats\n"
                + "1\tkeep-company\t1\t1\t1000,500\t1080,560.00\t1100,480;200,100\t62,31\t-\t3.50\t2.00\t0\n"
                + "2\tkeep-company\t1\t1\t1004,500\t1082,560.00\t1100,480;200,100\t62,31\t-\t3.60\t2.10\t1\n"
                + "3\tcombat\t2\t3\t1008,502\t1084,560.00\t1100,480;200,100\t70,30\t1020.50,490.25\t7.25\t6.00\t1\n"
                + "4\tcombat\t2\t3\t1012,504\t1086,560.00\t1100,480;200,100\t70,30\t1024.50,490.25\t7.75\t6.50\t1\n"
                + "5\tcombat\t2\t3\t1016,506\t1088,560.00\t1100,480;200,100\t70,30\t1028.50,490.25\t8.00\t6.75\t0\n");
            File.WriteAllLines(events, new[]
            {
                Event(0, 0, "session", ""),
                Event(1, 1, "npc-spawn", "slot=3", label: "Zombie", subject: 7, x: 1200, y: 500),
                // The combat census holds slot 3, which is the record's only witness that the zombie is hostile;
                // the bunny in slot 8 is spawned and never censused, so it is placed but not called a hostile.
                Event(2, 1, "npc-spawn", "slot=8", label: "Bunny", subject: 9, x: 900, y: 540),
                Event(3, 2, "candidate-funnel", "counts=offered=1;entries=npc3:3@75,31:outvalued>offered[plan=1;weighted=0.5]", label: "combat"),
                Event(4, 2, "decision", "scores=;course:combat=value:0.123,useful:0.456,harm:0.010,gap:0.020;"
                    + $"{ACensusAdmissionSurvivesItsBinder.AdmittedPrefix}combat=usable:2,unknown:1,unusable:0,reason:-;course-refused:target-capture-missing=5"),
                Event(5, 3, "decision", "scores=;course-decision=deciding,activity=combat;"),
                Event(6, 3, "npc-damage", "raw=8;effective=8;life-now=40", label: "Zombie", subject: 7, x: 1210, y: 505),
                Payload(7, 4, settled: true, release: ""),
                Payload(8, 5, settled: false, release: "course-complete"),
                Event(9, 5, "npc-death", "slot=3", label: "Zombie", subject: 7, x: 1215, y: 505),
                Event(10, 6, "session-end", "normal-close"),
            });
            Session session = Session.Load(tsv);
            string page = ExplainOneTick.Of(session, 4);
            void Says(string expected, string why)
                => Require(page.Contains(expected, StringComparison.Ordinal), $"--explain {why}: expected \"{expected}\" in\n{page}");

            Says("row 4 of 5", "did not locate the row");
            Says("ticks 3–5 (the rows carrying this decision identity); 2 course-decision payload(s)", "did not span the decision by its identity");
            // The numbers come off the settled record at tick 4 and the release off the unsettled one at tick 5.
            Says("at tick 4: reason course-published · activity combat · settled · bound step fire · steps 3 · orders priced 9, refused 5", "read the decision's numbers off the wrong record");
            Says("released  course-complete at tick 5", "lost the release, which rides on a different record from the numbers");
            Says("refused   target-capture-missing 5", "dropped the payload's refusal tally");
            Says("from the decision occurrence at tick 2 (2 tick(s) old", "did not date the leaders it read");
            Says("the latest occurrence, at tick 3, names none", "hid that the newest occurrence carried no leader");
            Says("combat         value 0.123 · useful 0.456 · harm 0.010 · gap 0.020", "did not print the leader's value terms");
            Says("combat         usable 2 · unknown 1 · unusable 0", "did not print the census admission");
            Says("npc_px 1012,504", "did not print the companion's recorded centre");
            Says("player_px 1086,560.00", "did not print the player's recorded feet");
            Says("centre 1100,480, half 200×100 px, so x 900..1300 and y 380..580; the orb is inside", "misread the intent region");
            Says("lookahead 1024.50,490.25", "did not print the steering target");
            Says("slot 3 Zombie at 1210,505 (npc-damage, 1 tick(s) old)", "did not place the damaged hostile by its spawn's slot");
            Says("hostiles  1 placed", "did not count the censused zombie as the one hostile");
            Says("other npcs 1 placed", "did not keep the never-censused bunny apart from the hostiles");
            Says("slot 8 Bunny at 900,540 (npc-spawn, 3 tick(s) old)", "did not place the bunny where it spawned");
            Says("brain_ms 7.75", "did not print the tick's brain cost");
            Says("sections  (absent)", "printed the absent section profile as something other than absent");
            foreach (string column in new[] { "frame_ms", "sections", "tick_alloc_bytes", "cost_fence_ms", "control_source" })
                Require(System.Text.RegularExpressions.Regex.IsMatch(page, @"^absent .*\b" + column + @"\b", System.Text.RegularExpressions.RegexOptions.Multiline),
                    $"--explain did not name {column} in its closing absent line:\n{page}");
            Require(!System.Text.RegularExpressions.Regex.IsMatch(page, @"^absent .*\bnpc_px\b", System.Text.RegularExpressions.RegexOptions.Multiline),
                "--explain named a column the capture carries as absent");

            // The death at tick 5 retires the hostile.
            string afterDeath = ExplainOneTick.Of(session, 5);
            Require(afterDeath.Contains("hostiles  0 placed", StringComparison.Ordinal) && !afterDeath.Contains("Zombie at", StringComparison.Ordinal),
                "a hostile the record saw die was still placed");
            // A tick past the last row explains the last row and says so; a tick before the first refuses.
            Require(ExplainOneTick.Of(session, 9).Contains("the capture holds no row at tick 9", StringComparison.Ordinal), "a tick past the capture was explained as if it were recorded");
            Require(ExplainOneTick.Of(session, 0).Contains("before the capture's first row", StringComparison.Ordinal), "a tick before the capture was explained");

            string window = ExplainOneTick.Window(session, 1, 5);
            Require(window.Contains("choice_id 1 → 2", StringComparison.Ordinal) && window.Contains("action keep-company → combat", StringComparison.Ordinal)
                    && window.Contains("threats 0 → 1", StringComparison.Ordinal),
                "the window form did not name what moved at the ticks it changed:\n" + window);
            Require(window.Contains("4 of 5 row(s) changed something", StringComparison.Ordinal),
                "the window form counted changed ticks wrongly:\n" + window);
            Require(window.Contains("never reported as changing: request", StringComparison.Ordinal),
                "the window form did not name a watched column the capture lacks:\n" + window);
            Require(Program.TryTicks("1800-1830", out long a, out long b) && a == 1800 && b == 1830 && Program.TryTicks("7", out a, out b) && a == 7 && b == 7
                    && !Program.TryTicks("30-10", out _, out _) && !Program.TryTicks("-5", out _, out _) && !Program.TryTicks("x", out _, out _),
                "the tick argument accepted a malformed range or refused a well-formed one");
        }
        finally
        {
            File.Delete(tsv);
            File.Delete(events);
        }
    }

    /// <summary>
    /// The PNG writer, checked against the format rather than against itself. The CRC is first held to the
    /// published check value of the CRC-32 PNG uses (<c>"123456789"</c> → <c>CBF43926</c>) and to the IEND
    /// chunk's constant CRC (<c>AE 42 60 82</c>), both of which are facts of the standard and not of this code;
    /// only then is it trusted to verify every chunk. The image data is inflated by the framework's own
    /// <see cref="System.IO.Compression.ZLibStream"/>, every scanline must open with filter byte 0, and the
    /// pixels must come back exactly.
    /// </summary>
    private static void APngRoundTripsThroughAStandardInflateWithEveryCrcRight()
    {
        Require(EncodePng.Crc32(Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926u,
            $"the CRC-32 is not the one PNG names: check value {EncodePng.Crc32(Encoding.ASCII.GetBytes("123456789")):X8}, expected CBF43926");
        byte[] pixels = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30, 40, 50, 60, 70, 80, 90 };
        byte[] png = EncodePng.Encode(3, 2, pixels);
        var (width, height, rgb, chunks) = DecodePngForTest(png);
        Require(width == 3 && height == 2, $"IHDR read back as {width}×{height}, expected 3×2");
        Require(rgb.SequenceEqual(pixels), "the pixels did not survive the round trip: " + string.Join(",", rgb));
        Require(chunks.SequenceEqual(new[] { "IHDR", "IDAT", "IEND" }), "the chunks were " + string.Join(",", chunks));
        Require(png[^4] == 0xAE && png[^3] == 0x42 && png[^2] == 0x60 && png[^1] == 0x82,
            $"the IEND chunk's CRC is not the standard's AE426082: {png[^4]:X2}{png[^3]:X2}{png[^2]:X2}{png[^1]:X2}");
        bool refused = false;
        try { EncodePng.Encode(3, 2, new byte[5]); } catch (ArgumentException) { refused = true; }
        Require(refused, "an RGB buffer of the wrong length was encoded rather than refused");
    }

    /// <summary>
    /// A PNG decoded the long way: the signature, every chunk's CRC over its type and data, IHDR's fields,
    /// the concatenated IDAT inflated, and each scanline's filter byte required to be 0 — this writer's only
    /// filter, so anything else is the writer being wrong rather than a filter to undo.
    /// </summary>
    internal static (int Width, int Height, byte[] Rgb, List<string> Chunks) DecodePngForTest(byte[] png)
    {
        // A helper rather than a group: it throws on its own so the run's group count names only real groups.
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Require(png.Take(8).SequenceEqual(EncodePng.Signature), "the PNG signature is wrong");
        int at = 8, width = 0, height = 0;
        var chunks = new List<string>();
        using var idat = new MemoryStream();
        while (at < png.Length)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            uint crc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Require(crc == EncodePng.Crc32(png.AsSpan(at + 4, 4 + length)), $"the {type} chunk's CRC does not cover its type and data");
            chunks.Add(type);
            if (type == "IHDR")
            {
                Require(length == 13, $"IHDR is {length} bytes, not 13");
                width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at + 8));
                height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at + 12));
                Require(png[at + 16] == 8 && png[at + 17] == 2 && png[at + 18] == 0 && png[at + 19] == 0 && png[at + 20] == 0,
                    "IHDR is not 8-bit truecolour, deflate, filter method 0, no interlace");
            }
            if (type == "IDAT") idat.Write(png, at + 8, length);
            at += 12 + length;
        }
        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) zlib.CopyTo(inflated);
        byte[] raw = inflated.ToArray();
        int stride = width * 3;
        Require(raw.Length == height * (stride + 1), $"the inflated image is {raw.Length} bytes, expected {height * (stride + 1)} for {width}×{height} with a filter byte a row");
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            Require(raw[y * (stride + 1)] == 0, $"scanline {y} opens with filter {raw[y * (stride + 1)]}, not 0");
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, rgb, y * stride, stride);
        }
        return (width, height, rgb, chunks);
    }

    /// <summary>
    /// A tick's picture on a synthetic capture whose one terrain snapshot is sixteen tiles square — air above
    /// row 34, rock from it — so the window around the bodies is part known and part not, and each pixel read
    /// back has one right colour: the orb's centre is the orb, a known rock tile is rock, a known air tile is
    /// air, a tile no snapshot reached is the unknown grey or its hatch and never rock, the region's left edge
    /// is the region's outline, and the censused hostile is a hostile. The known-tile count must be the
    /// snapshot's 256, which is what says the snapshot landed where its pixel origin puts it.
    /// </summary>
    private static void ATickPictureDrawsWhatTheRecordPlacedAndLeavesTheRestUnknown()
    {
        string Event(int seq, long tick, string kind, string detail, string label = "", float x = 0, float y = 0)
            => JsonSerializer.Serialize(new { v = 1, seq, tick, wall_elapsed_ms = tick * 16.0, kind, subject = 0, related = "", label, channel = "",
                pos_x = x, pos_y = y, vel_x = 0f, vel_y = 0f, expected_x = 0f, expected_y = 0f, amount = 0, detail });
        string tiles = string.Concat(Enumerable.Range(0, 16).Select(r => new string(24 + r < 34 ? '.' : '#', 16)));
        string tsv = Path.Combine(Path.GetTempPath(), $"aic-picture-{Guid.NewGuid():N}.tsv");
        string events = ReadGodsEyeEvents.PathFor(tsv), png = Path.ChangeExtension(tsv, ".png");
        try
        {
            File.WriteAllText(tsv, "# schema=0.44.0\n"
                + "tick\twall_elapsed_ms\taction\tnpc_px\tplayer_px\tintent_region\tspot\tlookahead\n"
                + "1\t16\tkeep-company\t990,500\t1100,544.00\t1050,480;100,60\t64,30\t-\n"
                + "2\t32\tkeep-company\t995,500\t1100,544.00\t1050,480;100,60\t64,30\t-\n"
                + "3\t48\tkeep-company\t1000,500\t1100,544.00\t1050,480;100,60\t64,30\t-\n");
            File.WriteAllLines(events, new[]
            {
                Event(0, 0, "session", ""),
                Event(1, 1, ReconstructTerrainWindow.Kind, $"width=16;height=16;clipped=0;tiles={tiles}", "local-world", 896, 384),
                Event(2, 2, "candidate-funnel", "counts=offered=1;entries=npc4:3@66,32:outvalued>offered[plan=1;weighted=0.5]", "combat"),
                Event(3, 4, "session-end", "normal-close"),
            });
            PictureSummary summary = DrawTickPicture.Draw(Session.Load(tsv), 3, png);
            var (width, _, rgb, _) = DecodePngForTest(File.ReadAllBytes(png));
            Rgb At(double worldX, double worldY)
            {
                int x = (int)(DrawTickPicture.MapLeft + (worldX / 16 - summary.OriginX) * summary.PixelsPerTile);
                int y = (int)(DrawTickPicture.MapTop + (worldY / 16 - summary.OriginY) * summary.PixelsPerTile);
                int i = (y * width + x) * 3;
                return new Rgb(rgb[i], rgb[i + 1], rgb[i + 2]);
            }
            Rgb TileCentre(int tx, int ty) => At(tx * 16 + 8, ty * 16 + 8);

            Require(summary.KnownTiles == 256, $"the picture knew {summary.KnownTiles} tiles where the one snapshot holds 256 inside the window");
            Require(At(1000, 500) == DrawTickPicture.CompanionFill, $"the orb's centre is {At(1000, 500)}, not the orb");
            Require(TileCentre(58, 37) == DrawTickPicture.Solid, $"a known rock tile is {TileCentre(58, 37)}, not rock");
            Require(TileCentre(58, 26) == DrawTickPicture.Air, $"a known air tile is {TileCentre(58, 26)}, not air");
            Rgb unknown = TileCentre(summary.OriginX + 1, 37);
            Require(unknown == DrawTickPicture.Unknown || unknown == DrawTickPicture.UnknownHatch,
                $"a tile no snapshot reached is {unknown}; unknown must never be drawn as rock or air");
            Require(At(950.5, 480) == DrawTickPicture.RegionLine, $"the region's left edge is {At(950.5, 480)}, not its outline");
            Require(At(66 * 16 + 8, 32 * 16 + 8) == DrawTickPicture.HostileFill, $"the censused hostile is {At(66 * 16 + 8, 32 * 16 + 8)}, not a hostile");
            Require(summary.Hostiles == 1 && summary.Terrain.Contains("1 snapshot(s)", StringComparison.Ordinal),
                "the picture's summary does not say what it drew: " + summary.Describe());

            // Every character the picture writes must be a glyph of its own, and a character the font lacks must not
            // look like a digit: the first world-run picture printed its terrain note's ';' as a hollow box, which is a 0.
            string written = string.Concat(DrawTickPicture.LegendOrder) + summary.Terrain
                + "tick of schema synthetic action request nav hostiles other npcs drops off picture tiles x y terrain known from the combat snapshot at ticks old; no terrain-snapshot occurrence reached this window; 0123456789 ( ) % . , : - / _ = > ×";
            string undrawn = new string(written.Where(ch => !Raster.Draws(ch)).Distinct().ToArray());
            Require(undrawn.Length == 0, $"the picture writes characters its font cannot draw: \"{undrawn}\"");
            string Rendered(char ch) { var r = new Raster(8, 12, DrawTickPicture.Background); r.Text(0, 0, ch.ToString(), DrawTickPicture.Ink); return Convert.ToHexString(r.Pixels); }
            string missing = Rendered('§');
            Require("0123456789".All(digit => Rendered(digit) != missing), "a character the font lacks is drawn exactly like a digit");
        }
        finally
        {
            File.Delete(tsv);
            File.Delete(events);
            File.Delete(png);
        }
    }

    /// <summary>
    /// The assertion every group makes, which is also how the run knows which groups ran.
    ///
    /// <para>The caller name is recorded because the count this file printed was a literal, and on
    /// 21 September 2026 it read 44 against 46 groups: it had gone stale twice without anybody
    /// noticing, which is what a number maintained by hand beside the thing it counts does. Worse
    /// than the wrong number is what a literal cannot catch at all — a group written and never added
    /// to <see cref="Run"/>, which reports as coverage in the file and executes never. Counting the
    /// groups that actually asserted something, and comparing that against the groups declared,
    /// catches both and cannot itself go stale.</para>
    /// </summary>
    private static void Require(bool condition, string message,
        [System.Runtime.CompilerServices.CallerMemberName] string group = "")
    {
        Ran.Add(group);
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>Every group that reached at least one assertion this run.</summary>
    private static readonly HashSet<string> Ran = new(StringComparer.Ordinal);

    /// <summary>
    /// The groups this file declares: every private, static, parameterless void method except the
    /// helpers those groups call. A declared group that never asserted is either unwired or a helper,
    /// and the run says which names they are rather than only that the counts differ.
    /// </summary>
    private static string DeclaredButSilent()
    {
        var declared = typeof(ChronicleTests)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
            .Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        declared.ExceptWith(Ran);
        return declared.Count == 0 ? "" : " " + declared.Count + " declared group(s) asserted nothing: "
            + string.Join(", ", declared.OrderBy(n => n, StringComparer.Ordinal));
    }
}
