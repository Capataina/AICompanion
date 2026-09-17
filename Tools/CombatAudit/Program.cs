#nullable enable

extern alias live;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AICompanion.Tools.Ledger;
using AICompanion.Tools.SessionReport;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// The combat audit: every combat-snapshot in a capture restored and re-decided. For each decision it
/// reports whether the replay reproduces the committed plan, whether that plan is on the exhaustive
/// front with the weighted regret and the generator attribution, what the weight sweep moves, and
/// whether the commitment still holds on the snapshot's tick with the reason it ends; over the capture
/// it pairs every shot's prediction with its landed damage and reports the calibration per projectile
/// type. Verdicts print and, with --write, land beside the capture as -combat-audit.json for the
/// report's Combat decisions section; the four ledger measures file under instrument combat-audit.
/// </summary>
internal static class Program
{
    private const string Instrument = "combat-audit";

    public sealed record DecisionVerdict(long Tick, string Trigger, int PlanId, bool Reproduced,
        List<string> Diffs, bool OnFront, double Regret, string Generator, bool Capped, int GridStands,
        List<AuditWeights.SweepMove> Sweep, bool Holds, string HoldReason);
    public sealed record Sidecar(string Capture, List<DecisionVerdict> Decisions,
        List<AuditKnowledge.TypeCalibration> Knowledge);

    private static int Main(string[] args)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Application Support/Steam/steamapps/common/tModLoader");
        var libraries = Directory.GetFiles(Path.Combine(root, "Libraries"), "*.dll", SearchOption.AllDirectories);
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string? path = libraries.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                .Equals(name.Name, StringComparison.OrdinalIgnoreCase));
            return path == null ? null : context.LoadFromAssemblyPath(path);
        };
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine("usage: CombatAudit --self-test");
            Console.WriteLine("       CombatAudit <capture.tsv|events.jsonl> [--write] [--snapshot N] [--no-sweep]");
            return 2;
        }
        if (args[0] == "--self-test")
            return SelfTest.Run();
        string capture = args[0];
        string eventsPath = capture.EndsWith("-events.jsonl", StringComparison.Ordinal) ? capture
            : ReadGodsEyeEvents.PathFor(capture);
        string? tsvPath = capture.EndsWith(".tsv", StringComparison.Ordinal) ? capture : null;
        bool write = Array.IndexOf(args, "--write") >= 0;
        bool sweep = Array.IndexOf(args, "--no-sweep") < 0;
        int only = -1;
        int at = Array.IndexOf(args, "--snapshot");
        if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int parsed))
            only = parsed;
        return AuditCapture(eventsPath, tsvPath, write, only, sweep);
    }

    private static int AuditCapture(string eventsPath, string? tsvPath, bool write, int only, bool sweep)
    {
        if (!File.Exists(eventsPath))
        {
            Console.WriteLine($"combat-audit: no events at {eventsPath}");
            return 2;
        }
        GodsEyeEventLog log = ReadGodsEyeEvents.Read(eventsPath);
        var snapshots = new List<GodsEyeEvent>();
        foreach (GodsEyeEvent e in log.Events)
            if (e.kind == "combat-snapshot")
                snapshots.Add(e);
        Console.WriteLine($"combat-audit: {snapshots.Count} snapshots in {Path.GetFileName(eventsPath)}");
        Session? session = null;
        if (tsvPath != null && File.Exists(tsvPath))
        {
            try { session = Session.Load(tsvPath); }
            catch (Exception error) { Console.WriteLine($"combat-audit: telemetry unreadable: {error.Message}"); }
        }
        var decisions = new List<DecisionVerdict>();
        int index = 0;
        foreach (GodsEyeEvent snapshot in snapshots)
        {
            if (only >= 0 && index != only)
            {
                index++;
                continue;
            }
            try
            {
                RestoredDecision restored = RestoreSnapshot.Restore(snapshot.detail);
                AuditSearch.ReplayVerdict replay = AuditSearch.Replay(restored);
                AuditSearch.ExhaustiveVerdict exhaustive = AuditSearch.Exhaustive(restored);
                List<AuditWeights.SweepMove> moves = sweep ? AuditWeights.Sweep(restored) : new List<AuditWeights.SweepMove>();
                // The hold runs last: committing the shifted plan mutates the restored planner, and the
                // searches above it must read the restore as the restore left it.
                AuditHold.HoldVerdict hold = AuditHold.Revalidate(restored);
                int moves2x = 0;
                foreach (AuditWeights.SweepMove move in moves)
                    if (move.Factor == 2f && move.Changed)
                        moves2x++;
                decisions.Add(new DecisionVerdict(snapshot.tick, snapshot.channel,
                    restored.Snapshot.Plan?.Id ?? -1, replay.Reproduced, replay.Diffs,
                    exhaustive.CommittedOnFront, exhaustive.Regret, exhaustive.Generator,
                    exhaustive.Capped, exhaustive.GridStands, moves, hold.Holds, hold.Reason));
                string replayed = replay.Reproduced ? "exact" : "DIVERGED: " + string.Join("; ", replay.Diffs);
                string front = exhaustive.CommittedOnFront ? "on" : "off";
                string capped = exhaustive.Capped ? $" (grid capped at {exhaustive.GridStands})" : "";
                string swept = sweep ? $", sweep moves {moves2x}/8 at x2" : "";
                string held = hold.Holds ? "holds" : $"RELEASED {hold.Reason}";
                Console.WriteLine($"  tick {snapshot.tick} {snapshot.channel}: replay {replayed}; " +
                    $"front {front}, regret {exhaustive.Regret:0.00}, generator {exhaustive.Generator}{capped}{swept}; hold {held}");
            }
            catch (AuditException error)
            {
                Console.WriteLine($"  tick {snapshot.tick} {snapshot.channel}: unrestorable: {error.Message}");
            }
            index++;
        }
        AuditKnowledge.KnowledgeVerdict knowledge = AuditKnowledge.Audit(log.Events, session);
        foreach (AuditKnowledge.TypeCalibration row in knowledge.Types)
            Console.WriteLine($"  {row.Type}: {row.MatchedHits}/{row.PredictedHits} predicted hits landed, " +
                $"tick error {row.MeanTickError:0.0}, wall surprise {row.WallSurprise}, " +
                $"factor {(double.IsNaN(row.MeanFactor) ? "unobserved" : row.MeanFactor.ToString("0.00"))}");
        FileMeasures(decisions, knowledge, session);
        if (write)
        {
            string sidecar = Path.ChangeExtension(eventsPath, null).Replace("-events", "") + "-combat-audit.json";
            File.WriteAllText(sidecar, JsonSerializer.Serialize(new Sidecar(eventsPath, decisions, knowledge.Types)));
            Console.WriteLine($"combat-audit: wrote {sidecar}");
        }
        return 0;
    }

    private static void FileMeasures(List<DecisionVerdict> decisions,
        AuditKnowledge.KnowledgeVerdict knowledge, Session? session)
    {
        if (decisions.Count == 0)
            return;
        int onFront = 0;
        double regret = 0.0;
        foreach (DecisionVerdict decision in decisions)
        {
            if (decision.OnFront)
                onFront++;
            regret += decision.Regret;
        }
        EmitLedgerRows.Measure(Instrument, "CombatAudit", "share of decisions on the exhaustive front",
            (double)onFront / decisions.Count, "share", "up");
        EmitLedgerRows.Measure(Instrument, "CombatAudit", "mean weighted regret",
            regret / decisions.Count, "weighted", "down");
        Column? cut = session?.Find("plan_cut");
        if (cut != null && cut.Number.Length > 0)
        {
            int searches = 0, cutSearches = 0;
            foreach (float value in cut.Number)
                if (!float.IsNaN(value))
                {
                    searches++;
                    if (value != 0f)
                        cutSearches++;
                }
            if (searches > 0)
                EmitLedgerRows.Measure(Instrument, "CombatAudit", "share of searches cut by budget",
                    (double)cutSearches / searches, "share", "down");
        }
        foreach (AuditKnowledge.TypeCalibration row in knowledge.Types)
            EmitLedgerRows.Measure(Instrument, "CombatAudit", $"calibration miss rate {row.Type}",
                knowledge.MissRate(row.Type), "share", "down");
    }
}
