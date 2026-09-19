extern alias live;

using System.Globalization;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        preferences.PotBreaking = false;
        try
        {
            for (int tick = 0; tick < 900 && live::AICompanion.Companion.Brain.Infrastructure.Observation.LootSense.IsWorldDrop(drop); tick++)
                VerifyOreWork.AdvanceBrain(ctx);
            for (int tick = 0; tick < 30; tick++) VerifyOreWork.AdvanceBrain(ctx);
            // Then the player moves along the floor far enough that the companion is outside his region, so the whole brain
            // rejoins under a follow destination and writes that destination's admitted region. Rejoining hands over to
            // accompanying as the body enters the region, before the navigator reports arrival, because acceptance reserved
            // the arrival radius inside it; so this reads regions, not arrival claims.
            // Column 75, not 45. Restated on 15 September 2026: fifteen tiles was well inside the region once the region
            // always held the player and grew to a grown half-width of about 375 px, so the companion was already with him,
            // accompanied him from inside, and no follow destination was ever admitted. The premise below is what keeps the
            // scene about rejoining.
            ctx.Player.position.X = 75 * 16f;
            VerifyOreWork.AdvanceBrain(ctx);
            Require(!ctx.Companion.Brain.Senses.Intent.Inside,
                $"the premise: after the player moves the companion must be outside his region, or the scene is accompanying rather than rejoining; body {ctx.Npc.Center} region {ctx.Companion.Brain.Senses.Intent.Region.Centre} half {ctx.Companion.Brain.Senses.Intent.Region.HalfSize}");
            for (int tick = 1; tick < 600; tick++) VerifyOreWork.AdvanceBrain(ctx);
        }
        finally
        {
            live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
            preferences.PotBreaking = potBreaking;
            recorder.OnWorldUnload();
            Main.item[slot] = previous;
        }
        Require(!live::AICompanion.Companion.Brain.Infrastructure.Observation.LootSense.IsWorldDrop(drop),
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
        SuccessRegionsAreWhatTheirRowsClaim(capture, ore: null, followRegions: true);
        ACaptureNamesItsSourceConfigurationAndClosure(capture, potBreakingTurnedOff: true);
    }

    /// <summary>
    /// The real recorder's preamble names the revision this build was stamped from and the configuration the session
    /// started under, a preference changed after the world loaded is recorded as a configuration occurrence carrying the
    /// changed value, and the file ends with the world-unload end marker stating exactly the rows it holds.
    /// </summary>
    private static void ACaptureNamesItsSourceConfigurationAndClosure(Capture capture, bool potBreakingTurnedOff)
    {
        string? source = capture.Preamble.FirstOrDefault(line => line.StartsWith("# source_revision=", StringComparison.Ordinal));
        Require(source is not null, "the recorder wrote no source revision line: " + string.Join(" | ", capture.Preamble));
        string[] stamp = source!["# source_revision=".Length..].Split(';');
        // This host builds the mod from a git checkout, so an unknown revision here means the stamp is not reaching the
        // assembly rather than that git is absent.
        Require(stamp.Length == 2 && stamp[0].Length == 40 && stamp[0].All(Uri.IsHexDigit) && stamp[1] is "tree=clean" or "tree=dirty",
            $"the source revision line does not name a forty-digit revision and a clean or dirty tree: {source}");
        string? config = capture.Preamble.FirstOrDefault(line => line.StartsWith("# config=character;", StringComparison.Ordinal));
        Require(config is not null && config.Contains(";pot_breaking=", StringComparison.Ordinal) && config.Contains(";record_telemetry=true", StringComparison.Ordinal),
            "the recorder wrote no configuration snapshot with the preferences and switches: " + string.Join(" | ", capture.Preamble));
        var changes = capture.Events.Where(e => e.Kind == "configuration").ToList();
        if (potBreakingTurnedOff)
            Require(changes.Count == 1 && changes[0].Detail.Contains(";pot_breaking=false;", StringComparison.Ordinal),
                $"turning pot breaking off after the world loaded must be one configuration occurrence carrying the new value; recorded {changes.Count}: {string.Join(" | ", changes.Select(c => c.Detail))}");
        else
            Require(changes.Count == 0, "a scene that changed no preference recorded configuration changes: " + string.Join(" | ", changes.Select(c => c.Detail)));
        Require(capture.Trailer.Length == 3 && capture.Trailer[0].StartsWith($"# closing=world-unload;rows={capture.Rows.Count};", StringComparison.Ordinal)
                && capture.Trailer[1].StartsWith("# diagnostics=", StringComparison.Ordinal)
                && capture.Trailer[2].StartsWith($"# end=world-unload;rows={capture.Rows.Count};", StringComparison.Ordinal),
            $"a world unload must end the file with one end marker naming its {capture.Rows.Count} rows; trailer: {string.Join(" | ", capture.Trailer)}");
        string closing = capture.Trailer[0]["# closing=".Length..];
        using var diagnostics = JsonDocument.Parse(capture.Trailer[1]["# diagnostics=".Length..]);
        long written = diagnostics.RootElement.GetProperty("LegacyEvent").GetProperty("Written").GetInt64();
        Require(written == capture.Events.Count && Capture.Long(Capture.Field(closing, "events-offered")) == written
                && Capture.Field(closing, "events-dropped") == "0"
                && Capture.Field(closing, "terrain-evictions") == "0" && Capture.Long(Capture.Field(closing, "events-coalesced")) >= 0,
            $"the end marker must count exactly the {capture.Events.Count} occurrence(s) its sidecar holds, with none dropped or evicted: {capture.Trailer[0]}");
        Require(Capture.Field(capture.Trailer[2], "diagnostics-incomplete") == "False"
                && Capture.Field(capture.Trailer[2], "diagnostics-failure") == ""
                && Capture.Field(capture.Trailer[2], "diagnostics-dropped") == "0",
            "a normal native capture must complete without diagnostic loss or writer failure");
        int lastRow = capture.Rows.Count - 1;
        Require(capture.Text(0, "record_ms") == "-"
                && Enumerable.Range(1, lastRow).All(r => double.TryParse(capture.Text(r, "record_ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && ms >= 0)
                && capture.Long(lastRow, "events_written") <= written && capture.Text(lastRow, "events_dropped") == "0",
            $"the first row must carry no cost and every later row a non-negative one, and the running totals must not pass the closing ones; first {capture.Text(0, "record_ms")}, last written {capture.Text(lastRow, "events_written")}");
        string? retention = capture.Preamble.FirstOrDefault(line => line.StartsWith("# retention=", StringComparison.Ordinal));
        string[] bounds = { "terrain-snapshots-remembered", "terrain-captures-per-tick", "recent-attempt-outcomes", "cargo-transfer-ledger",
            "cosmetic-contacts-per-summary", "inspector-traces", "session-map-tiles", "plan-dump-every-ticks", "flush-every-ticks" };
        Require(retention is not null && bounds.All(bound => Capture.Long(Capture.Field(retention["# retention=".Length..], bound)) > 0),
            "the recorder wrote no retention statement carrying every bound as a positive count: " + retention);
        Console.WriteLine($"attempt evidence producers: capture names source {stamp[0][..12]} ({stamp[1]}), {changes.Count} configuration change(s), and closes with {capture.Trailer[0]}");
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
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        var recorder = new BrainTelemetry(); VerifyObservationLifecycle.Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try
        {
            for (int tick = 0; tick < 900 && Main.tile[ore.X, ore.Y].HasTile; tick++) VerifyOreWork.AdvanceBrain(ctx);
            // Long enough for the next comparison to conclude the finished attempt.
            for (int tick = 0; tick < 30; tick++) VerifyOreWork.AdvanceBrain(ctx);
        }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
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
        SuccessRegionsAreWhatTheirRowsClaim(capture, ore);
        ACaptureNamesItsSourceConfigurationAndClosure(capture, potBreakingTurnedOff: false);
    }

    /// <summary>
    /// The region columns of a real capture: every tool-reach row names the scene's ore and a stand inside its own reach
    /// box, every recorded arrival verdict is what the row's own geometry says, and a changed destination tile always
    /// arrives with a new destination revision. SessionReport's rule recomputes this geometry; this proves the producer
    /// writes geometry it can recompute.
    /// </summary>
    private static void SuccessRegionsAreWhatTheirRowsClaim(Capture capture, Point? ore, bool followRegions = false)
    {
        static (float X, float Y)? PairOf(string cell)
        {
            string[] parts = cell.Split(',');
            return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ? (x, y) : null;
        }
        float Number(int row, string name) => float.Parse(capture.Text(row, name), NumberStyles.Float, CultureInfo.InvariantCulture);
        int toolRows = 0, toolClaims = 0, followRows = 0, followClaims = 0, revisions = 0;
        for (int row = 0; row < capture.Rows.Count; row++)
        {
            string kind = capture.Text(row, "region_kind");
            string arrival = capture.Text(row, "region_arrival");
            // The body point is the orb's centre, read from the one column that carries it. `observed_left`,
            // `observed_bottom` and `npc_width` were the walker's body box, reconstructed here into a feet
            // point; they are not columns any more, and their absence is deliberate rather than an omission —
            // there is one body and one contact, a circle about this centre, so there is no box to write and
            // SessionReport's own reader asserts the names are gone. The recorder writes the centre because it
            // is the point the navigator steers and the intent region measures, which is the same point both
            // geometries below have to be judged against.
            var centreOf = PairOf(capture.Text(row, "npc_px"))
                ?? throw new InvalidOperationException($"row at tick {capture.Text(row, "tick")} carries no body centre: npc_px={capture.Text(row, "npc_px")}");
            Vector2 body = new(centreOf.X, centreOf.Y);
            if (row > 0 && capture.Text(row, "spot") != capture.Text(row - 1, "spot"))
            {
                Require(capture.Text(row, "region_revision") != capture.Text(row - 1, "region_revision"),
                    $"the destination changed from {capture.Text(row - 1, "spot")} to {capture.Text(row, "spot")} at tick {capture.Text(row, "tick")} under one revision {capture.Text(row, "region_revision")}");
                revisions++;
            }
            if (kind == "tool-reach")
            {
                toolRows++;
                Point work = ore ?? throw new InvalidOperationException($"a scene with no tool target wrote a tool-reach row at tick {capture.Text(row, "tick")}: {capture.Text(row, "region_work_tile")}");
                Require(capture.Text(row, "region_work_tile") == $"{work.X},{work.Y}", $"a tool-reach row at tick {capture.Text(row, "tick")} names work tile {capture.Text(row, "region_work_tile")}, not the scene's ore {work}");
                var stand = PairOf(capture.Text(row, "region_anchor_px"));
                var reach = PairOf(capture.Text(row, "region_reach"));
                Require(stand is not null && reach is not null, $"a tool-reach row at tick {capture.Text(row, "tick")} has no stand or reach: {capture.Text(row, "region_anchor_px")} {capture.Text(row, "region_reach")}");
                Require(live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess.InReachBox(new Vector2(stand!.Value.X, stand.Value.Y), work, (int)reach!.Value.X, (int)reach.Value.Y),
                    $"the stand {capture.Text(row, "region_anchor_px")} declared at tick {capture.Text(row, "tick")} lies outside its own reach box around {work}");
                if (arrival != "-")
                {
                    toolClaims++;
                    bool inside = live::AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess.InReachBox(body, work, (int)reach.Value.X, (int)reach.Value.Y);
                    Require(arrival == (inside ? "inside" : "outside"), $"the arrival at tick {capture.Text(row, "tick")} is recorded {arrival}, but the body centre {body} is {(inside ? "inside" : "outside")} the reach box");
                }
            }
            else if (kind == "follow-comfort")
            {
                followRows++;
                var player = PairOf(capture.Text(row, "region_player_px"));
                var anchor = PairOf(capture.Text(row, "region_anchor_px"));
                var comfort = PairOf(capture.Text(row, "region_comfort"));
                Require(player is not null && anchor is not null && comfort is { X: > 0, Y: > 0 },
                    $"a follow-comfort row at tick {capture.Text(row, "tick")} lacks well-formed admission references: player {capture.Text(row, "region_player_px")} anchor {capture.Text(row, "region_anchor_px")} comfort {capture.Text(row, "region_comfort")}");
                if (arrival == "-") continue;
                followClaims++;
                bool Near((float X, float Y) centre) => MathF.Abs(body.X - centre.X) <= comfort!.Value.X + .01f && MathF.Abs(body.Y - centre.Y) <= comfort.Value.Y + .01f;
                bool inside = Near(player!.Value) || Near(anchor!.Value);
                Require(arrival == (inside ? "inside" : "outside"), $"the follow arrival at tick {capture.Text(row, "tick")} is recorded {arrival}, but its geometry says {(inside ? "inside" : "outside")}");
            }
        }
        if (ore is not null) Require(toolRows > 0, "the mining scene walked from outside reach and wrote no tool-reach region");
        if (followRegions && followRows == 0)
        {
            // Name the link that did not happen: no reunion selected, a request other than following, or a player the
            // harness put back.
            string states = string.Join("; ", Enumerable.Range(0, capture.Rows.Count)
                .GroupBy(r => $"{capture.Text(r, "action")}|{capture.Text(r, "request")}|{capture.Text(r, "region_kind")}|{capture.Text(r, "nav_status")}|{capture.Text(r, "control_request_owner")}|follow-valid={capture.Text(r, "follow_objective_valid")}")
                .Select(g => $"{g.Key}×{g.Count()}"));
            int last = capture.Rows.Count - 1;
            throw new InvalidOperationException("the player moved beyond the companion's region and the recorder wrote no follow-comfort region; "
                + $"rows by action|request|region|nav|owner: {states}; last row tick {capture.Text(last, "tick")} player {capture.Text(last, "player_px")} body {capture.Text(last, "npc_px")} spot {capture.Text(last, "spot")}");
        }
        Console.WriteLine($"attempt evidence producers: {toolRows} tool-reach row(s) name the scene's ore and a stand inside its box; {followRows} follow-comfort row(s) carry their admission references; {toolClaims} tool and {followClaims} follow arrival verdict(s) match their geometry; {revisions} destination change(s) each advanced the revision");
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
