extern alias live;

using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using BrainTelemetry = live::AICompanion.Companion.Brain.BehaviourDiagnostics.BrainTelemetry;
using WorkPolicy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;

/// <summary>
/// Drives the whole brain, the real recorder and the real event writer through native work, then
/// reads back the identities a report joins on. SessionReport's self-tests prove its rules against
/// fixtures written by hand; these prove the producer writes what those fixtures assume. Runs inside
/// <see cref="VerifyObservationLifecycle"/>, which redirects the save path to a temporary folder.
/// </summary>
internal static class VerifyAttemptEvidenceProducers
{
    internal static void Run()
    {
        // Native PickTile runs the client's achievement bookkeeping unless the process is a dedicated
        // server, and nothing in this headless host loads achievements; the ore-work entry sets the
        // same flag before its fixtures. Restored so the observation fixtures around this keep theirs.
        bool server = Main.dedServ;
        Main.dedServ = true;
        try
        {
            StrikesNameTheAttemptTheirRowNames();
            ACollectionClaimIsWhatItsOwnPickupsDelivered();
        }
        finally { Main.dedServ = server; }
    }

    /// <summary>
    /// A drop ten tiles along the floor, collected by the whole brain through the real contact pickup. The pickup that
    /// took it must name the collection attempt that walked to it, and that attempt's outcome must claim exactly what
    /// those pickups delivered, of the drop's own type.
    /// </summary>
    private static void ACollectionClaimIsWhatItsOwnPickupsDelivered()
    {
        var preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current;
        bool potBreaking = preferences.PotBreaking;
        const int slot = 7;
        Item previous = Main.item[slot];
        var ctx = VerifyCollectionContracts.SetUpFloor();
        Item drop = VerifyCollectionContracts.Drop(ItemID.CopperOre, 10, new Vector2(30 * 16 + 8, 60 * 16));
        var recorder = new BrainTelemetry(); VerifyObservationLifecycle.Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        preferences.PotBreaking = false;
        try
        {
            for (int tick = 0; tick < 900 && live::AICompanion.Companion.Brain.WorldObservation.LootSense.IsWorldDrop(drop); tick++)
                VerifyOreWork.AdvanceBrain(ctx);
            for (int tick = 0; tick < 30; tick++) VerifyOreWork.AdvanceBrain(ctx);
        }
        finally
        {
            live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false;
            preferences.PotBreaking = potBreaking;
            recorder.OnWorldUnload();
            Main.item[slot] = previous;
        }
        Require(!live::AICompanion.Companion.Brain.WorldObservation.LootSense.IsWorldDrop(drop),
            $"the recorded collection scene must take its drop before its evidence means anything; action={ctx.Companion.Brain.LastAction?.Name} feet={ctx.Npc.Bottom}");

        var capture = Capture.Read(path);
        var pickups = capture.Events.Where(e => e.Kind == "pickup").ToList();
        Require(pickups.Count > 0 && pickups.All(p => Capture.Long(Capture.Field(p.Detail, "collection-attempt-id")) is not null),
            $"the real contact pickup wrote {pickups.Count} pickup(s), or one without a collection attempt field");
        var claimed = pickups.Where(p => Capture.Long(Capture.Field(p.Detail, "collection-attempt-id")) > 0).ToList();
        Require(claimed.Count > 0, "the pickup of the drop collection walked to named no collection attempt: " + string.Join(" | ", pickups.Select(p => p.Detail)));
        long attempt = Capture.Long(Capture.Field(claimed[0].Detail, "collection-attempt-id"))!.Value;
        Require(claimed.All(p => Capture.Long(Capture.Field(p.Detail, "collection-attempt-id")) == attempt && p.Label == ItemID.CopperOre.ToString()),
            "the drop's pickups named more than one attempt or another item type");
        var outcome = capture.Events.FirstOrDefault(e => e.Kind == "attempt-outcome" && Capture.Long(Capture.Field(e.Detail, "attempt-id")) == attempt);
        Require(outcome.Kind != null, $"collection attempt {attempt} has no outcome after its drop was taken and thirty more ticks");
        int delivered = claimed.Sum(p => p.Amount);
        Require(Capture.Field(outcome.Detail, "status") == "Complete" && Capture.Field(outcome.Detail, "attribution") == "Companion"
            && Capture.Long(Capture.Field(outcome.Detail, "claimed-yield-type")) == ItemID.CopperOre
            && Capture.Long(Capture.Field(outcome.Detail, "claimed-yield-quantity")) == delivered && delivered == 10,
            $"collection attempt {attempt} must claim exactly the ten its own pickups delivered; outcome={outcome.Detail}; delivered={delivered}");
        Console.WriteLine($"attempt evidence producers: collection attempt {attempt} claimed {delivered} copper ore and its {claimed.Count} pickup(s) delivered exactly that");
    }

    /// <summary>
    /// Every strike names a non-zero activity attempt, the row written after the brain in that same
    /// update names it open or closed on that tick, and the attempt's own outcome, where one was
    /// written, contains the strike and names the same activity.
    /// </summary>
    private static void StrikesNameTheAttemptTheirRowNames()
    {
        Point ore = new(25, 59);
        var (_, ctx) = VerifyOreWork.SetUp(WorkPolicy.Opportunistic, TileID.Copper, ore);
        // Outside reach, so the attempt spans an approach walk as well as its strikes.
        ctx.Npc.Bottom = new Vector2(8 * 16f + 3f, ctx.Npc.Bottom.Y);
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        var recorder = new BrainTelemetry(); VerifyObservationLifecycle.Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = true;
        try
        {
            for (int tick = 0; tick < 900 && Main.tile[ore.X, ore.Y].HasTile; tick++) VerifyOreWork.AdvanceBrain(ctx);
            // Long enough for the next comparison to conclude the finished attempt.
            for (int tick = 0; tick < 30; tick++) VerifyOreWork.AdvanceBrain(ctx);
        }
        finally { live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork.Unbounded = false; }
        recorder.OnWorldUnload();
        Require(!Main.tile[ore.X, ore.Y].HasTile, "the recorded mining scene must break its ore natively before its evidence means anything");

        var capture = Capture.Read(path);
        var strikes = capture.Events.Where(e => e.Kind == "tool-effect").ToList();
        Require(strikes.Count > 0 && strikes.Any(e => e.Detail.Contains("effect=Removed", StringComparison.Ordinal)),
            $"the real producer wrote {strikes.Count} strike(s) and no removal for a native break");
        var outcomes = capture.Events.Where(e => e.Kind == "attempt-outcome").ToList();
        foreach (var strike in strikes)
        {
            long attempt = Capture.Long(Capture.Field(strike.Channel, "activity-attempt-id"))
                ?? throw new InvalidOperationException("a strike carries no activity-attempt-id: " + strike.Channel);
            Require(attempt > 0, $"a strike at tick {strike.Tick} was written with no attempt open: {strike.Channel}");
            int row = capture.RowAt(strike.Tick);
            Require(row >= 0, $"no row was written on the tick of the strike at {strike.Tick}");
            bool open = capture.Long(row, "activity_attempt_id") == attempt;
            bool closedThere = capture.Long(row, "attempt_end_id") == attempt && capture.Long(row, "attempt_end_tick") == strike.Tick;
            Require(open || closedThere, $"the strike at tick {strike.Tick} names attempt {attempt}, but its row holds '{capture.Text(row, "activity_attempt_id")}' open and last closed '{capture.Text(row, "attempt_end_id")}'");
            var outcome = outcomes.FirstOrDefault(o => Capture.Long(Capture.Field(o.Detail, "attempt-id")) == attempt);
            if (outcome.Kind == null)
            {
                Require(capture.Long(capture.Rows.Count - 1, "activity_attempt_id") == attempt,
                    $"attempt {attempt} has no outcome and is not the attempt still open when capture ended");
                continue;
            }
            Require(Capture.Long(Capture.Field(outcome.Detail, "start-tick")) <= strike.Tick && strike.Tick <= Capture.Long(Capture.Field(outcome.Detail, "end-tick")),
                $"attempt {attempt}'s outcome ({outcome.Detail}) does not contain the strike at tick {strike.Tick}");
            Require(Capture.Field(outcome.Detail, "activity-id") == Capture.Field(strike.Channel, "activity-id"),
                $"attempt {attempt}'s outcome and its strike name different activities");
        }
        Console.WriteLine($"attempt evidence producers: {strikes.Count} native strike(s) each name the attempt their row and outcome name");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

/// <summary>One recorded session read back by column name and event kind, the way SessionReport reads it.</summary>
internal sealed class Capture
{
    internal readonly record struct Event(long Sequence, long Tick, string Kind, string Label, string Channel, int Amount, string Detail);

    public required string[] Names { get; init; }
    public required List<string[]> Rows { get; init; }
    public required List<Event> Events { get; init; }
    public required string[] Trailer { get; init; }
    public required string[] Preamble { get; init; }

    internal static Capture Read(string tsv)
    {
        string[] lines = File.ReadAllLines(tsv);
        int header = Array.FindIndex(lines, l => l.TrimStart('﻿').StartsWith("tick\t", StringComparison.Ordinal));
        if (header < 0) throw new InvalidOperationException($"{tsv} has no row header");
        var rows = new List<string[]>();
        var trailer = new List<string>();
        foreach (string line in lines.Skip(header + 1))
        {
            if (line.StartsWith('#')) trailer.Add(line);
            else if (line.Length > 0) rows.Add(line.Split('\t'));
        }
        var events = new List<Event>();
        string sidecar = Path.ChangeExtension(tsv, null) + "-events.jsonl";
        if (File.Exists(sidecar))
            foreach (string line in File.ReadLines(sidecar))
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement e = document.RootElement;
                events.Add(new Event(e.GetProperty("seq").GetInt64(), e.GetProperty("tick").GetInt64(), e.GetProperty("kind").GetString()!,
                    e.GetProperty("label").GetString()!, e.GetProperty("channel").GetString()!, e.GetProperty("amount").GetInt32(), e.GetProperty("detail").GetString()!));
            }
        return new Capture { Names = lines[header].TrimStart('﻿').Split('\t'), Rows = rows, Events = events, Trailer = trailer.ToArray(), Preamble = lines.Take(header).ToArray() };
    }

    internal int Column(string name)
    {
        int index = Array.IndexOf(Names, name);
        if (index < 0) throw new InvalidOperationException("the recorder wrote no column " + name);
        return index;
    }

    internal string Text(int row, string name) => Rows[row][Column(name)];

    internal long? Long(int row, string name) => Long(Text(row, name));

    internal int RowAt(long tick)
    {
        int column = Column("tick");
        return Rows.FindIndex(r => Long(r[column]) == tick);
    }

    /// <summary>The first <c>key=value</c> for a key in a <c>;</c>-separated payload, as the report reads it.</summary>
    internal static string? Field(string payload, string key)
    {
        foreach (string part in payload.Split(';'))
            if (part.StartsWith(key + "=", StringComparison.Ordinal)) return part[(key.Length + 1)..];
        return null;
    }

    internal static long? Long(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : null;
}
