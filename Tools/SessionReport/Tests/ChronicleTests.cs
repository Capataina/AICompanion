#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;

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
            HuntRangeEvidenceDoesNotInventUniversalFailure();
            DowningDoesNotProveAvoidability();
            ASelectedActivityMustHaveCarriedAnEligibleOffer();
            ASelectionChangesOnlyWithANewComparison();
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
            // Last, because it writes a chronicle and an events sibling into the temp directory and
            // the multi-run cases above read that directory for runs to join.
            Console.WriteLine("Chronicle self-tests passed (35 assertion groups).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Chronicle self-test failed: {error.Message}");
            return 1;
        }
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
                ("two seconds", Capture(120, t => DarkTile(t))),
                ("the companion lighting", Capture(300, t => DarkTile(t, action: "place-torches"))),
                ("a tile that keeps moving", Capture(300, t => DarkTile(t, tile: $"{21 + t / 60},59"))),
            })
                Require(!new TorchesGoWhereHisCursorWould().Run(Session.Load(file)).Any(), $"the cursor check fired on {label}");

            var (huntFindings, _, _) = Program.Evaluate(Session.Load(Capture(300, t => NearHunt(t))));
            Finding? idle = huntFindings.FirstOrDefault(f => f.Check == huntName);
            Require(idle != null && idle.Detail.Contains("hunt_time 0.420", StringComparison.Ordinal) && idle.Detail.Contains("hunt_fin", StringComparison.Ordinal),
                "a usable hunt near an idle player while keeping company won was not reported with hunting's factors");
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

    private static void HuntRangeEvidenceDoesNotInventUniversalFailure()
    {
        string file = Path.GetTempFileName();
        try
        {
            Finding[] Read(string evidence, int age = 0, string fire = "no-target")
            {
                var trace = new StringBuilder("tick\taction\tbrain_fresh\tfire\ttarget_evidence_age\ttarget_evidence\n");
                for (int tick = 0; tick < 301; tick++)
                    trace.AppendLine($"{tick}\thunt\t1\t{fire}\t{age}\t{evidence}");
                File.WriteAllText(file, trace.ToString());
                return new HuntingHadAWeaponThatCouldReach().Run(Session.Load(file)).ToArray();
            }

            const string distant = "7:12:0:0:weapon=1:outside-reach";
            Require(Read("7:12:0:0:weapon=0:no-clear-trajectory|" + distant).Length == 0,
                "one weapon outside reach turned a mixed rejection set into a universal range failure");
            Require(Read("7:12:10:weapon=0:kills=0:harm=0:value=10|" + distant).Length == 0,
                "an accepted attack pair was ignored beside a range rejection");
            foreach (string malformed in new[] { "", "-", "outside-reach", distant + "|", distant + "|truncated", "x:12:0:0:weapon=1:outside-reach" })
                Require(Read(malformed).Length == 0, "missing or malformed evidence became an all-pairs claim");
            Require(Read(distant, age: 100).Length == 0, "retained old evidence became a fresh range observation");
            Require(Read(distant, fire: "fired").Length == 0 && Read(distant, fire: "cooldown").Length == 0,
                "successful shooting or cooldown became failed pursuit");
            Finding[] findings = Read("7:12:0:0:weapon=0:outside-reach|" + distant);
            Require(findings.Length == 1 && findings[0].Severity == Severity.Potential,
                "a bounded range-only interval must remain a potential issue, not proof of impossible pursuit");
            Require(findings[0].Detail.Contains("recorded", StringComparison.Ordinal),
                "range diagnosis lost its bounded evidence qualification");
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
            Require(Source("Companion", "Brain", "Activities", "Gathering", "MineOre.cs").Contains("PositionRequest.ExactAt(t.StandPosition, t.Tile)", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Activities", "Gathering", "ChopTree.cs").Contains("ExactAt(t.StandPosition, t.Bottom)", StringComparison.Ordinal)
                    && Source("Companion", "Brain", "Activities", "NearbyAssistance", "PerformNearbyWorldWork.cs").Contains("ExactAt(stand, tile)", StringComparison.Ordinal),
                "a tool stand no longer declares the work tile its reach box is judged against");
            // `Contains` is handed the body's centre and the follow arm reads the region alone; a second
            // reference reappearing there would widen acceptance behind this check's back.
            Require(region.Contains("public bool? Contains(Vector2 feet)", StringComparison.Ordinal)
                    && region.Contains("SuccessRegionKind.FollowComfort => Near(feet, PlayerFeet)", StringComparison.Ordinal)
                    && telemetry.Contains("region.Contains(npc.Center)", StringComparison.Ordinal),
                "the follow region admits something other than its own box, or the recorder judges an arrival on a point other than the orb's centre");
            Require(new[] { "\"follow-comfort\"", "\"tool-reach\"", "\"firing-position\"", "\"meeting-place\"", "\"undeclared\"" }.All(name => region.Contains(name, StringComparison.Ordinal))
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

            // The producer literals these rules rest on.
            string project = File.ReadAllText("AICompanion.csproj");
            string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "Infrastructure", "Diagnostics", "RecordBrainTelemetry.cs"));
            Require(project.Contains("git rev-parse HEAD", StringComparison.Ordinal) && project.Contains("BeforeTargets=\"GetAssemblyAttributes\"", StringComparison.Ordinal)
                    && project.Contains("<_Parameter1>SourceRevision</_Parameter1>", StringComparison.Ordinal) && project.Contains("<_Parameter1>SourceTree</_Parameter1>", StringComparison.Ordinal),
                "the build no longer stamps the source revision and tree state the recorder reads");
            Require(telemetry.Contains("writer.WriteLine($\"# source_revision={SourceProvenance}\");", StringComparison.Ordinal)
                    && telemetry.Contains("$\"character;mining={Mining};chopping={Chopping};hunting=", StringComparison.Ordinal)
                    && telemetry.Split("# end=").Length == 2 && telemetry.Contains("writer?.WriteLine($\"# end={reason};rows={rowsWritten};", StringComparison.Ordinal),
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
            Require(telemetry.IndexOf("recordClock.Restart();", StringComparison.Ordinal) > telemetry.IndexOf("public static void Record(CompanionNPC companion)", StringComparison.Ordinal)
                    && telemetry.Contains("lastRecordMs = recordClock.Elapsed.TotalMilliseconds;", StringComparison.Ordinal)
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
                string text = DescribeSession.Of(session) + DescribeGodsEyeEvents.Of(tsv, true) + JoinAttemptEvidence.Describe(tsv, session, true)
                    + Chronicle.Of(session, true) + MultiRunReport.Of(new[] { tsv });
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
        Require(telemetry.Contains("offer = s.Eligibility + \":\" + s.EligibilityReason", StringComparison.Ordinal) && telemetry.Contains("string offer = \"not-compared\"", StringComparison.Ordinal),
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
