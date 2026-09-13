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
            AMoveKindThatMostlyFailsIsReportedNotOnlyOneThatAlwaysDoes();
            HuntRangeEvidenceDoesNotInventUniversalFailure();
            DowningDoesNotProveAvoidability();
            ASelectedActivityMustHaveCarriedAnEligibleOffer();
            ASelectionChangesOnlyWithANewComparison();
            AttemptEvidenceJoinsByIdentityAndDisagreementsAreDefinitive();
            ACompletedTransferClaimNeedsItsReceivedQuantity();
            ControlGrantRulesJudgeTheRequestedOwner();
            IdentityChecksSkipOldAndPartialCapturesByName();
            MultiRunStatesProvenanceBeforeAnyRun();
            IdentityRulesStillMatchTheProducer();
            Console.WriteLine("Chronicle self-tests passed (26 assertion groups).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Chronicle self-test failed: {error.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The check that asks whether an offered move is ever made used to fire only at zero, and a
    /// session where 30 of 281 jumps completed therefore read clean. These cases pin the four
    /// corners of the replacement: a mostly-failing kind is reported, a healthy kind is not, an
    /// interrupted kind is not (an interruption is somebody else taking the body, not the move
    /// failing), and a handful of attempts is too few to call a rate.
    /// </summary>
    private static void AMoveKindThatMostlyFailsIsReportedNotOnlyOneThatAlwaysDoes()
    {
        string file = Path.GetTempFileName();
        try
        {
            Session Write(params (string Kind, string Outcome, int Count)[] moves)
            {
                var text = new StringBuilder("# text_columns=edge_kind,edge_outcome,next_kind\n");
                text.AppendLine("tick\twall_elapsed_ms\tedge_n\tedge_kind\tedge_outcome\tnext_kind");
                int tick = 0, edge = 0;
                foreach (var move in moves)
                    for (int i = 0; i < move.Count; i++, tick++)
                        text.AppendLine($"{tick}\t{tick * 16}\t{++edge}\t{move.Kind}\t{move.Outcome}\t{move.Kind}");
                File.WriteAllText(file, text.ToString());
                return Session.Load(file);
            }

            bool Fires(Session s, string kind) =>
                new EveryMoveOfferedGetsMade().Run(s).Any(f => f.Title.StartsWith(kind, StringComparison.Ordinal));

            // 3 completed against 27 faulted: never zero, and still a broken move.
            Require(Fires(Write(("Jump", "None", 3), ("Jump", "Misland", 27)), "Jump"),
                "a move kind completing 3 of 30 was passed because it completed more than none");

            // The shape that used to be the only one caught, and it keeps its Definitive grade.
            Require(new EveryMoveOfferedGetsMade().Run(Write(("Jump", "Misland", 27)))
                    .Any(f => f.Severity == Severity.Definitive),
                "a move kind that never completed lost its definitive grade");

            // Walk is interrupted constantly and faults never; counting interruptions as failures
            // would report the healthiest move in the session.
            Require(!Fires(Write(("Walk", "None", 25), ("Walk", "Interrupted", 60)), "Walk"),
                "interruptions were counted as faults and reported a healthy move kind");

            // Too few endings for a rate to mean anything.
            Require(!Fires(Write(("Drop", "None", 1), ("Drop", "Misland", 3)), "Drop"),
                "four attempts were treated as a measurable completion rate");
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
                ["spot"] = "31,79", ["path_steps"] = "0", ["control"] = "move=0.00;jump=0;scale=1.00",
                ["observed_left"] = "500", ["observed_bottom"] = "1280", ["npc_width"] = "20",
                ["follow_reason"] = "follow-horizontal-gap", ["nav_status"] = "Arrived", ["fire"] = "no-arc",
                ["control_source"] = "travel", ["breath"] = "0.60u", ["attack_value"] = "25",
                ["weapon"] = "bow", ["exp_bow"] = "5", ["exp_knife"] = "50"
            };
            Session Write(bool moving = false)
            {
                var text = new StringBuilder("# text_columns=action,request,spot,control,follow_reason,nav_status,fire,control_source,weapon\n");
                text.AppendLine(string.Join('\t', row.Keys));
                for (int i = 0; i < 130; i++)
                {
                    row["tick"] = i.ToString(); row["wall_elapsed_ms"] = (i * 16).ToString();
                    row["observed_left"] = (500 + (moving ? i : 0)).ToString();
                    text.AppendLine(string.Join('\t', row.Values));
                }
                File.WriteAllText(file, text.ToString()); return Session.Load(file);
            }
            Session stalled = Write();
            Require(new ArrivalDoesNotStrandFollowing().Run(stalled).Any(), "arrival contradiction was missed");
            Require(new SubmergedMotionGetsExplained().Run(stalled).Any(), "submerged stationary body was missed");
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
            File.WriteAllText(file, "# schema=0.9.0\n# started_utc=2026-09-09T18:25:59.0000000Z\n"
                + "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tnpc_support\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n"
                + "10\t0\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-32,0\t0,0\t100\tsolid\tnone\tnavigator\tup\twalk-with\twith-player\t-\n"
                + "11\t16\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t8,-4\t2,0\t1\twater\t100\t-\t-\talive\tmove\tslope-lower-right\t-24,0\t1,0\t100\tsolid\tmove-right\tnavigator\tup\twalk-with\twith-player\t0,0\n"
                + "12\t33\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t16,-8\t2,0\t1\twater\t90\tdamage=10;source=npc:Zombie;direction=1;knockback=4.00\tdamage=10;direction=-1;knockback=3.00;source=unrecorded\talive\tattack\tslope-lower-right\t-16,0\t2,0\t90\tsolid\tjump-right\treflex\tup\tguard\tguard\t0,0\n");
            string report = Chronicle.Of(Session.Load(file), full: true);
            Require(report.Contains("schema 0.9.0"), "metadata was not read");
            Require(report.Contains("samples are observed_before_ai;request_after_ai;npc_px_after_helpers"), "sample phase was not preserved");
            Require(report.Contains("player entered water"), "liquid transition missing");
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
            File.WriteAllText(file, "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tnpc_support\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n"
                + "1\t20\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t0,0\t0,0\t100\tsolid\tnone\tidle\tup\twander\thold\t-\n"
                + "2\t10\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t1,0\t1,0\t1\twater\t100\t-\t-\talive\tmove\tslope-lower-left\t1,0\t1,0\t100\tsolid\tmove\tnavigation\tup\twander\thold\t-\n");
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
            File.WriteAllText(file, "# schema=0.9.0\n# started_utc=2026-09-09T18:25:59.0000000Z\n"
                + "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tnpc_support\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n"
                + "1\t0\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-30,0\t0,0\t100\tsolid\tnone\tnavigation\tup\twalk-with\twith-player\t0,0\n"
                + "2\t20\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-20,0\t1,0\t100\tsolid\tmove\tnavigation\tup\twalk-with\twith-player\t0,0\n"
                + "3\t40\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-10,0\t1,0\t100\tsolid\tmove\tnavigation\tup\twalk-with\twith-player\t0,0\n"
                + "4\t60\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t-10,0\t0,0\t100\tsolid\tnone\tnavigation\tup\twalk-with\twith-player\t0,0\n");
            string report = Chronicle.Of(Session.Load(file), full: true);
            Require(report.Contains("net progress toward its recorded spot: 32.2->24.1 px"), "successful approach must convert the recorded feet tile to world pixels");
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
            const string header = "tick\trequest\tfollow_objective_valid\tfollow_dx\tfollow_dy\tfollow_reason\troute_search_id\troute_attempt_id\troute_remaining_ticks\tpath_at\taction\trecovery_active\tnpc_px\tplayer_px\tplayer_vel\twall_elapsed_ms\tbrain_fresh\n";
            var rows = new StringBuilder(header);
            for (int tick = 0; tick <= 120; tick++)
            {
                // The companion first walks away around a C-turn, so the Euclidean gap grows.
                // Its route identity stays stable while completed steps rise and ETA falls.
                rows.Append(tick).Append("\tWithPlayer\t0\t").Append(100 + tick).Append("\t0\tC-turn\t7\t11\t")
                    .Append(240 - tick).Append('\t').Append(tick / 30).Append("\twalk-with\t0\t")
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
                rows.Append(tick).Append("\tWithPlayer\t0\t").Append(100 + tick).Append("\t0\twrong-floor\t7\t11\t240\t0\twalk-with\t0\t")
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
                    .Append(240 - Math.Min(tick, 10)).Append('\t').Append(completed).Append("\twalk-with\t0\t")
                    .Append(tick).Append(",0\t0,0\t1,0\t").Append(tick * 16).Append('\n');
            }
            File.WriteAllText(file, WithFreshDecisions(rows));
            Finding delayed = new FollowingMakesRouteProgress().Run(Session.Load(file)).Single();
            Require(delayed.FirstTick == 10 && delayed.Rows == 231, "one early completed step hid the later prolonged no-progress follow window");

            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 240; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t100\t0\trecovery\t7\t11\t240\t0\twalk-with\t1\t")
                    .Append(tick).Append(",0\t0,0\t1,0\t").Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows));
            Require(!new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(), "recovery flight inherited stale WithPlayer/walk-with state as an ordinary follow failure");

            rows.Clear();
            rows.Append(header);
            for (int tick = 0; tick <= 240; tick++)
                rows.Append(tick).Append("\tWithPlayer\t0\t900\t0\twrong-floor\t7\t11\t240\t0\twalk-with\t0\t0,0\t900,0\t1,0\t")
                    .Append(tick * 16).Append('\n');
            File.WriteAllText(file, WithFreshDecisions(rows, fresh: false));
            Require(!new FollowingMakesRouteProgress().Run(Session.Load(file)).Any(),
                "sticky follow fields during downing must not become a new follow-stall diagnosis");
            Require(!new FollowingRespondsAfterDeparture().Run(Session.Load(file)).Any(f => f.Title.Contains("was first selected", StringComparison.Ordinal)),
                "sticky downed action fields must not count as a fresh follow response");

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
            trace.Append("tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tnpc_px\tnpc_vel\tlife\tnpc_support\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\tobserved_left\tobserved_bottom\tobserved_vel\tobserved_ground\tobserved_wet\tobserved_mobility\tnpc_width\n");
            for (int tick = 0; tick <= 120; tick++)
            {
                // npc_px deliberately changes as an after-helper compatibility value. The body
                // observed at AI entry does not; Chronicle must use the latter for this finding.
                trace.Append(tick).Append('\t').Append(tick * 16).Append("\tobserved_before_ai;request_after_ai;npc_px_after_helpers\t0,0\t0,0\t1\tdry\t100\t-\t-\talive\tidle\tsolid\t")
                    .Append(tick).Append(",0\t1,0\t100\tsolid\tmove=4.00;jump=0;scale=1.00;fall=0;descend=0\tnavigation\tup\twalk-with\twith-player\t100,0\t-20\t0\t1,0\t1\t0\tair=0;latched=0;dash=0\t20\n");
            }
            File.WriteAllText(file, trace.ToString());
            string report = Chronicle.Of(Session.Load(file), full: true);
            Require(report.Contains("inferred lack of progress", StringComparison.Ordinal), "sustained movement request without entry-state movement was not reported");
            Require(report.Contains("ticks 0..120", StringComparison.Ordinal), "lack-of-progress evidence did not preserve its tick interval");
            Require(!report.Contains("net progress toward its recorded spot", StringComparison.Ordinal), "after-helper npc_px was used as actual motion");
            foreach (string owner in new[] { "combat-reflex", "reflex", "survival-escape", "follow-recovery-flight", "travel-recovery-clearance", "unrecognised-owner" })
            {
                File.WriteAllText(file, trace.ToString().Replace("\tnavigation\t", $"\t{owner}\t", StringComparison.Ordinal));
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
                + "tick\twall_elapsed_ms\tsample_phase\tplayer_px\tplayer_vel\tplayer_ground\tplayer_liquid\tplayer_life\tplayer_hit\tnpc_hit\tplayer_state\tplayer_activity\tplayer_support\tlife\tnpc_support\tcontrol\tcontrol_source\tstate\taction\trequest\tspot\n");
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
        string source = File.ReadAllText(Path.Combine("Companion", "Brain", "BehaviourDiagnostics", "RecordBrainTelemetry.cs"));
        Require(source.Contains("\\tplayer_life\\tplayer_hit\\tnpc_hit\\tplayer_state", StringComparison.Ordinal), "recorder header lost the hit-event sequence consumed by Chronicle");
        Require(source.Contains("\\tdir\\tlife\\tbreath", StringComparison.Ordinal), "recorder no longer writes the actual companion life column");
        Require(!source.Contains("npc_life", StringComparison.Ordinal), "recorder contract invented an npc_life column it does not write");
    }

    private static void RecorderCapturesFreshNavigationEvidence()
    {
        string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "BehaviourDiagnostics", "RecordBrainTelemetry.cs"));
        string events = File.ReadAllText(Path.Combine("Companion", "Brain", "BehaviourDiagnostics", "RecordGodsEyeEvents.cs"));
        Require(telemetry.Contains("brain.LastTick == Main.GameUpdateCount", StringComparison.Ordinal), "recorder does not distinguish an old brain action from this tick's action");
        Require(telemetry.Contains("RecordNavigationEvidence", StringComparison.Ordinal), "recorder does not sample navigation evidence at the diagnostics boundary");
        Require(events.Contains("search-id=", StringComparison.Ordinal) && events.Contains("attempt-id=", StringComparison.Ordinal)
                && events.Contains("freshness=", StringComparison.Ordinal) && events.Contains("progress=", StringComparison.Ordinal),
            "navigation occurrence omits causal identity, freshness or bounded progress reason");
    }

    private static void RecorderLifecycleAndReservationContractsArePresent()
    {
        string telemetry = File.ReadAllText(Path.Combine("Companion", "Brain", "BehaviourDiagnostics", "RecordBrainTelemetry.cs"));
        string events = File.ReadAllText(Path.Combine("Companion", "Brain", "BehaviourDiagnostics", "RecordGodsEyeEvents.cs"));
        Require(telemetry.Contains("FileMode.CreateNew", StringComparison.Ordinal) && telemetry.Contains("ReserveSessionPath", StringComparison.Ordinal),
            "recorder can still overwrite a same-second run instead of reserving an attempt-specific file");
        Require(telemetry.Contains("WriteMetadata();", StringComparison.Ordinal) && telemetry.Contains("PostUpdateEverything", StringComparison.Ordinal)
                && telemetry.Contains("LoadWorldData(TagCompound tag)", StringComparison.Ordinal) && telemetry.Contains("SaveWorldData(TagCompound tag)", StringComparison.Ordinal),
            "zero-tick metadata or lifecycle callback evidence is missing from the recorder contract");
        Require(telemetry.Contains("RecordLifecycle", StringComparison.Ordinal) && telemetry.Contains("outer-load=unobservable", StringComparison.Ordinal),
            "lifecycle evidence no longer states the boundary between this callback and Terraria's outer load");
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
            + $"requested-controls=move=0.00;jump=0;scale=1.00;fall=0;descend=0;applied-controls=move=0.00;jump=0;scale=1.00;fall=0;descend=0;hand={hand};motor-applications=1;scope=ai-phase-before-engine;hand-effect=unobserved");

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
    /// The identity rules restate producer facts, so the literals they rest on are pinned here: a
    /// renamed owner, a reordered grant payload or a new eligibility name fails this test rather than
    /// quietly turning a rule into one that can never fire.
    /// </summary>
    private static void IdentityRulesStillMatchTheProducer()
    {
        string Source(params string[] parts) => File.ReadAllText(Path.Combine(parts));
        string tick = Source("Companion", "Brain", "CoordinateBrainTick.cs");
        string safety = Source("Companion", "Brain", "SharedSafety", "ChooseSafetyResponse.cs");
        string events = Source("Companion", "Brain", "BehaviourDiagnostics", "RecordGodsEyeEvents.cs");
        string telemetry = Source("Companion", "Brain", "BehaviourDiagnostics", "RecordBrainTelemetry.cs");
        string offers = Source("Companion", "Brain", "Behaviours", "ClassifyOffersAndAttempts.cs");
        string owner = Source("Companion", "Brain", "BehaviourSelection", "OwnCurrentActivity.cs");
        foreach (string ordinary in ControlGrantsAreCompatible.OrdinaryOwners)
            Require(tick.Contains($"owner = \"{ordinary}\"", StringComparison.Ordinal), $"the ordinary movement owner '{ordinary}' is no longer issued by the coordinator");
        Require(tick.Contains("\"downed\", HandGrant.Unavailable", StringComparison.Ordinal) && tick.Contains("HandGrant.WorkTool : HandGrant.Available", StringComparison.Ordinal)
                && tick.Contains("\"follow-recovery-flight\", RecoveryVelocity", StringComparison.Ordinal),
            "the coordinator's downed, work-tool or recovery grant no longer has the shape the grant rules assume");
        Require(safety.Contains("\"survival-escape\"", StringComparison.Ordinal) && safety.Contains("\"combat-reflex\"", StringComparison.Ordinal)
                && safety.Contains("Begin(ctx, \"combat-spacing\")", StringComparison.Ordinal),
            "a safety owner the grant rules classify is no longer issued");
        Require(events.Contains("grant-id={id};grant-tick={tick};activity-id={activityId};attempt-id={attemptId};activity-phase={activityPhase};requested-owner={requestedOwner}", StringComparison.Ordinal)
                && events.Contains("attempt={outcome.Attempt};choice-id={choiceId};activity-id={activityId};activity-attempt-id={activityAttemptId}", StringComparison.Ordinal)
                && new[] { Source("Companion", "Brain", "PurposeFamilies", "Gathering", "MineOre.cs"), Source("Companion", "Brain", "PurposeFamilies", "Gathering", "ChopTree.cs") }
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
        string collection = Source("Companion", "Brain", "PurposeFamilies", "NearbyAssistance", "CollectNearbyItems.cs");
        Require(events.Contains("interruption-is-not-failure=true;claimed-yield-type={claimedYieldType};claimed-yield-quantity={claimedYieldQuantity}", StringComparison.Ordinal)
                && events.Contains("stack={item.stack};collection-attempt-id={collectionAttemptId}", StringComparison.Ordinal),
            "the claimed-yield or pickup payload the transfer check reads has changed");
        Require(npc.Contains("collect.ClaimsDrop(item) ? owner.AttemptId : 0", StringComparison.Ordinal) && npc.IndexOf("ClaimsDrop(item)", StringComparison.Ordinal) < npc.IndexOf("Bag.Collect(item, player)", StringComparison.Ordinal)
                && collection.Contains("AttemptAttribution.Shared, drop.Type, received)", StringComparison.Ordinal) && collection.Contains("AttemptAttribution.NotApplicable, drop.Type, received)", StringComparison.Ordinal)
                && collection.Contains("drop.Bag.TransferredSince(drop.TransferMark, drop.Item)", StringComparison.Ordinal),
            "a pickup no longer names its collection attempt before the transfer, or collection no longer claims what its own drop's transfers delivered");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
