#nullable enable

using System;
using System.IO;
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
            EmptyHeaderOnlySessionIsReadable();
            RecorderChronologyContractUsesActualLifeColumn();
            Console.WriteLine("Chronicle self-tests passed (7 assertion groups).");
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
        string source = File.ReadAllText(Path.Combine("Brain", "BehaviourDiagnostics", "RecordBrainTelemetry.cs"));
        Require(source.Contains("\\tplayer_life\\tplayer_hit\\tnpc_hit\\tplayer_state", StringComparison.Ordinal), "recorder header lost the hit-event sequence consumed by Chronicle");
        Require(source.Contains("\\tdir\\tlife\\tbreath", StringComparison.Ordinal), "recorder no longer writes the actual companion life column");
        Require(!source.Contains("npc_life", StringComparison.Ordinal), "recorder contract invented an npc_life column it does not write");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
