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
            Console.WriteLine("Chronicle self-tests passed (14 assertion groups).");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Chronicle self-test failed: {error.Message}");
            return 1;
        }
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
            Require(report.Contains("net progress toward its recorded spot: 20.0->10.0 px"), "successful approach was not measured");
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
        }
        finally
        {
            File.Delete(file);
        }
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
            for (int i = 0; i < 30; i++) Add("decision", 1, "WithPlayer", 1000 + i * 30000);
            Add("session-end", wall: 902000);
            File.WriteAllLines(events, lines);
            report = DescribeGodsEyeEvents.Of(file);
            Require(report.Contains("projectile 1000001 intended target enemy-1"), "a reused projectile slot lost its first shot identity");
            Require(!report.Contains("projectile 1000002 intended target enemy-2"), "a piercing shot hitting terrain after an enemy is not a blocked shot");
            Require(report.Contains("00:14:30"), "default causal summary omitted the end of a long run");
            Require(report.Contains("end=normal close"), "normal recorder closure must be visible");
            Require(report.Contains("lifecycle world-entry: observed=ModSystem.OnWorldLoad;outer-load=unobservable", StringComparison.Ordinal), "lifecycle callback evidence was not surfaced with its outer-load limit");
            Require(report.Contains("freshness=stale-or-not-executed"), "reader discarded freshness that prevents a stale action becoming a fictional stall");
            string full = DescribeGodsEyeEvents.Of(file, true);
            Require(full.Contains("projectile-enemy-hit subject=1000002"), "full event trace discarded native contact details");
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
