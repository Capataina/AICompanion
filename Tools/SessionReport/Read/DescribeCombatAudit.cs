#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// The report's Combat decisions section: the CombatAudit sidecar beside the capture, read back into
/// prose. The sidecar is the audit's own verdict records (Program.Sidecar: capture path, one decision
/// per combat-snapshot, one calibration row per projectile type), so this section adds no judgement of
/// its own — it names the replay divergences with their generator attributions, grades the hold and
/// the weight sweep, and states plainly when the sidecar is missing, corrupt, or written for another
/// capture. A missing sidecar is "unmeasured, not clean" rather than silence, because a combat section
/// that vanishes when the audit never ran would read as a clean fight.
/// </summary>
public static class DescribeCombatAudit
{
    public sealed record Row(string Kind, string Text);

    public static string Of(string sessionPath)
    {
        var text = new StringBuilder("combat decisions\n");
        foreach (Row row in Describe(sessionPath))
            text.Append("  ").Append(row.Text).Append('\n');
        return text.ToString();
    }

    public static List<Row> Describe(string sessionPath)
    {
        var rows = new List<Row>();
        string sidecar = Path.ChangeExtension(sessionPath, null) + "-combat-audit.json";
        if (!File.Exists(sidecar))
        {
            rows.Add(new Row("combat-audit",
                "No combat-audit sidecar beside the capture, so the fight is unmeasured rather than clean: " +
                "run CombatAudit --write over the capture's events and re-render the report."));
            return rows;
        }
        JsonDocument root;
        try
        {
            root = JsonDocument.Parse(File.ReadAllText(sidecar));
        }
        catch (Exception error)
        {
            rows.Add(new Row("combat-audit",
                $"The combat-audit sidecar is unreadable ({error.GetType().Name}), so the fight is " +
                "unmeasured, not clean."));
            return rows;
        }
        using (root)
        {
            string own = StripEvents(Path.GetFileNameWithoutExtension(sessionPath));
            string foreign = StripEvents(Path.GetFileNameWithoutExtension(Str(root.RootElement, "Capture")));
            if (foreign.Length > 0 && !string.Equals(foreign, own, StringComparison.OrdinalIgnoreCase))
                rows.Add(new Row("combat-audit",
                    $"note: the sidecar was written for {foreign}, not for this capture, so its verdicts " +
                    "describe another fight."));
            List<JsonElement> decisions = Arr(root.RootElement, "Decisions");
            List<JsonElement> knowledge = Arr(root.RootElement, "Knowledge");
            int exact = 0;
            int onFront = 0;
            double regret = 0.0;
            foreach (JsonElement d in decisions)
            {
                if (Bool(d, "Reproduced") == true)
                    exact++;
                if (Bool(d, "OnFront") == true)
                    onFront++;
                regret += Num(d, "Regret") ?? 0.0;
            }
            rows.Add(new Row("combat-audit",
                $"{decisions.Count} decisions audited; {exact} replayed exactly, {onFront} on the exhaustive " +
                $"front, mean regret {(decisions.Count == 0 ? 0.0 : regret / decisions.Count):0.00}."));
            foreach (JsonElement d in decisions)
                DescribeDecision(d, rows);
            DescribeHolds(decisions, rows);
            DescribeCuts(decisions, rows);
            DescribeSweep(decisions, rows);
            foreach (JsonElement row in knowledge)
                DescribeCalibration(row, rows);
        }
        return rows;
    }

    private static void DescribeDecision(JsonElement d, List<Row> rows)
    {
        long tick = (long)(Num(d, "Tick") ?? 0.0);
        string trigger = Str(d, "Trigger");
        if (trigger.Length == 0)
            trigger = "commit";
        int plan = (int)(Num(d, "PlanId") ?? -1.0);
        string plans = plan < 0 ? "no plan" : "plan #" + plan.ToString(CultureInfo.InvariantCulture);
        if (trigger == "mark" && plan < 0)
        {
            rows.Add(new Row("combat-audit",
                $"tick {tick} ({trigger}, {plans}): the mark holds nothing — no committed plan to replay " +
                "or hold, so the audit grades only that the snapshot restores."));
            return;
        }
        if (Bool(d, "Reproduced") == false)
            rows.Add(new Row("combat-audit",
                $"tick {tick} ({trigger}, {plans}): replay DIVERGED — " +
                string.Join("; ", Arr(d, "Diffs").Select(e => e.GetString() ?? "")) + "."));
        if (Bool(d, "OnFront") == false)
        {
            string capped = Bool(d, "Capped") == true
                ? $" (grid capped at {(int)(Num(d, "GridStands") ?? 0.0)}, so the front is a lower bound)"
                : "";
            string generator = Str(d, "Generator");
            rows.Add(new Row("combat-audit",
                $"tick {tick} ({trigger}, {plans}): off the exhaustive front, regret " +
                $"{Num(d, "Regret") ?? 0.0:0.00}, missing generator " +
                $"{(generator.Length == 0 ? "none" : generator)}{capped}."));
        }
        if (Bool(d, "Holds") == false)
            rows.Add(new Row("combat-audit",
                $"tick {tick} ({trigger}, {plans}): commitment RELEASED ({Str(d, "HoldReason")}) " +
                "before the snapshot's tick — the fight moved on without a rescore."));
        if (Bool(d, "Cut") == true)
            rows.Add(new Row("combat-audit",
                $"tick {tick} ({trigger}, {plans}): search CUT by budget — the committed plan is the best " +
                "finished candidate, not the best candidate."));
    }

    private static void DescribeHolds(List<JsonElement> decisions, List<Row> rows)
    {
        List<JsonElement> committed = decisions.Where(d => (int)(Num(d, "PlanId") ?? -1.0) >= 0).ToList();
        if (committed.Count == 0)
            return;
        int held = committed.Count(d => Bool(d, "Holds") == true);
        int ungraded = committed.Count(d => Bool(d, "Holds") == null);
        var releases = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonElement d in committed)
        {
            if (Bool(d, "Holds") != false)
                continue;
            string reason = Str(d, "HoldReason");
            if (reason.Length == 0)
                reason = "?";
            releases[reason] = releases.TryGetValue(reason, out int n) ? n + 1 : 1;
        }
        string tail = releases.Count == 0 ? "no releases"
            : "releases: " + string.Join(", ", releases.Select(kv => $"{kv.Key} x{kv.Value}"));
        string older = ungraded == 0 ? "" : $" ({ungraded} ungraded, sidecar predates the hold audit)";
        rows.Add(new Row("combat-audit",
            $"holds: {held} of {committed.Count} committed hold{older}; {tail}."));
    }

    private static void DescribeCuts(List<JsonElement> decisions, List<Row> rows)
    {
        List<JsonElement> graded = decisions.Where(d => Bool(d, "Cut") != null).ToList();
        if (graded.Count == 0)
            return;
        int cut = graded.Count(d => Bool(d, "Cut") == true);
        int older = decisions.Count(d => Bool(d, "Cut") == null);
        string tail = older == 0 ? "" : $" ({older} sidecar entries predate the cut verdict)";
        rows.Add(new Row("combat-audit",
            $"cuts: {cut} cut by budget{tail}."));
    }

    private static void DescribeSweep(List<JsonElement> decisions, List<Row> rows)
    {
        List<JsonElement> swept = decisions.Where(d => Has(d, "Sweep")).ToList();
        if (swept.Count == 0)
            return;
        var changed = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonElement d in swept)
            foreach (JsonElement move in Arr(d, "Sweep"))
            {
                if (Math.Abs((Num(move, "Factor") ?? 0.0) - 2.0) > 1e-6)
                    continue;
                string name = Str(move, "Name");
                if (name.Length == 0)
                    name = "?";
                total[name] = total.TryGetValue(name, out int t) ? t + 1 : 1;
                if (Bool(move, "Changed") == true)
                    changed[name] = changed.TryGetValue(name, out int c) ? c + 1 : 1;
            }
        if (total.Count == 0)
            return;
        rows.Add(new Row("combat-audit",
            "offered moves change search outcomes: " +
            string.Join(", ", total.Select(kv =>
                $"{kv.Key} {(changed.TryGetValue(kv.Key, out int c) ? c : 0)}/{kv.Value}")) + "."));
    }

    private static void DescribeCalibration(JsonElement row, List<Row> rows)
    {
        string type = Str(row, "Type");
        if (type.Length == 0)
            type = "?";
        int shots = (int)(Num(row, "Shots") ?? 0.0);
        int paired = (int)(Num(row, "Paired") ?? 0.0);
        int predicted = (int)(Num(row, "PredictedHits") ?? 0.0);
        int matched = (int)(Num(row, "MatchedHits") ?? 0.0);
        double damagePredicted = Num(row, "DamagePredicted") ?? 0.0;
        double damageLanded = Num(row, "DamageLanded") ?? 0.0;
        double ratio = damagePredicted <= 0 ? double.NaN : damageLanded / damagePredicted;
        double factor = Num(row, "MeanFactor") ?? double.NaN;
        rows.Add(new Row("combat-audit",
            $"{type}: {paired}/{shots} shots paired, {matched}/{predicted} predicted hits landed, " +
            $"tick error {Num(row, "MeanTickError") ?? 0.0:0.0}, wall surprise " +
            $"{(int)(Num(row, "WallSurprise") ?? 0.0)}, damage landed " +
            $"{(double.IsNaN(ratio) ? "unpredicted" : ratio.ToString("0.00"))} of predicted, residual factor " +
            $"{(double.IsNaN(factor) ? "unobserved" : factor.ToString("0.00"))}."));
    }

    private static string StripEvents(string stem)
        => stem.EndsWith("-events", StringComparison.Ordinal) ? stem[..^"-events".Length] : stem;

    private static bool Has(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out _);

    private static string Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement p)
            && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static bool? Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement p)
            && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
            ? p.GetBoolean() : null;

    private static double? Num(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement p)
            && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    private static List<JsonElement> Arr(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement p)
            && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().ToList() : new List<JsonElement>();
}
