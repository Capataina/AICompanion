extern alias live;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using live::AICompanion.Companion.CharacterBody;
using Inputs = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs;
using DecisionClock = live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.DecisionClock;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// A capture played back as the input it recorded rather than as a scene to re-simulate: the world put back tick for
/// tick from its `replay-inputs`, the brain asked the same tick again, and its decision compared with the one the
/// capture holds.
///
/// Nothing here runs a hostile's AI, moves a drop or times a search. Every NPC and dropped item is *placed* from the
/// record before the tick — its type, position, velocity, life, AI fields and buffs — and a slot the record does not
/// hold is emptied; the player is placed with his inventory and the companion's gear; the world's own terrain edits
/// are written at their ticks; the light scanner is handed the seed it had; `Main.rand` is handed the state the tick
/// started from on a tick that drew; and every question the brain asks the wall clock is answered from the tape the
/// play recorded, so a search is cut at the expansion where the play cut it whatever this machine's speed. The
/// companion's own body, its edits and its decisions are the only things computed, and they are what is compared.
///
/// A tick is reproduced when the body stood where the play's did before the tick, and the decision after it — the
/// course's activity, reason and bound step, the activity holding the body, the movement request, the motor's
/// controls and the velocity handed to the body — is the play's to the character. The first tick that is not is
/// reported with both sides and with every other disagreement found at that tick, in the order the tick meets them,
/// because the earliest one is the best lead on which input the record is missing.
/// </summary>
internal static class ReproduceTheCapture
{
    /// <summary>An input the reproduction can be told to leave out, so a run can show which inputs a window depends
    /// on — and so the self-consistency verdict can be shown to fail when one is missing.</summary>
    internal enum DroppedInput { None, Actors, Clock, Random, Light, Player, Edits }

    internal sealed record Divergence(int Index, ulong Tick, IReadOnlyList<string> Disagreements, string Recorded, string Replayed);

    internal sealed record Result(
        string Capture,
        int Ticks,
        int Reproduced,
        Divergence? First,
        int DivergedTicks,
        int ClockMismatchTicks,
        int RandomMismatchTicks,
        int OpsMismatchTicks,
        int EditMismatchTicks,
        int BodyMismatchTicks,
        int TerrainMismatchTicks,
        long RecordedCharacters,
        double Seconds,
        DroppedInput Dropped);

    public static Result Run(ReadReplayInputs.Record record, int maxTicks, DroppedInput dropped, string? recordTo, bool printEach, int seed)
    {
        IReadOnlyList<ReadReplayInputs.Frame> frames = record.Frames;
        if (maxTicks > 0 && frames.Count > maxTicks) frames = frames.Take(maxTicks).ToList();
        ReadReplayInputs.Frame opening = frames[0];

        // What the recording pass did before its first tick, in its order: the census cleared, every random source
        // pinned, the clock set one tick before the first recorded one (the first tick advances it), the light
        // services built, the companion attached, the light engine warmed, and the recorder attached last.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Reset();
        PrepareTheHeadlessEngine.PinEveryRandomSource(seed);
        PrepareTheHeadlessEngine.StartTheWorldClockAt(opening.Tick - 1);
        PrepareTheHeadlessEngine.PrepareLightServices();

        var player = new Dictionary<string, string>(opening.PlayerChanges, StringComparer.Ordinal);
        // The time of day before the warm-up, because the warm-up lights the sky from it.
        var world = new Dictionary<string, string>(opening.WorldChanges, StringComparer.Ordinal);
        Inputs.ApplyWorld(world);
        Vector2 playerPosition = new(Inputs.ParseFloat(player["x"]), Inputs.ParseFloat(player["y"]));
        CompanionNPC companion = PrepareTheHeadlessEngine.AttachCompanion(opening.Companion.Position, playerPosition);
        Player livePlayer = Main.player[0];
        string inventory = opening.Inventory ?? "", gear = opening.Gear ?? "";
        Inputs.ApplyPlayer(livePlayer, player, inventory, gear);
        companion.NPC.position = opening.Companion.Position;
        companion.NPC.velocity = opening.Companion.Velocity;
        companion.NPC.life = opening.Companion.Life;
        if (RunTheWorld.DriveLight)
            PrepareTheHeadlessEngine.WarmTheLightEngine(livePlayer.Bottom.ToTileCoordinates(), RunTheWorld.LightHalfWidth, RunTheWorld.LightHalfHeight);
        if (recordTo != null)
        {
            AttachTheRecorder.Open(recordTo, record.Capture);
            Console.WriteLine("RECORDER " + AttachTheRecorder.Describe());
        }

        // The companion's own edits this tick, heard the way the recorder hears them.
        var edited = new List<Point>();
        var editedSeen = new HashSet<Point>();
        bool insideTheTick = false;
        Action<int, int>? recorderObserver = TerrainChanges.EditObserved;
        TerrainChanges.EditObserved = (x, y) =>
        {
            recorderObserver?.Invoke(x, y);
            if (insideTheTick && editedSeen.Add(new Point(x, y))) edited.Add(new Point(x, y));
        };

        var npcs = new Dictionary<int, Dictionary<string, string>>();
        var items = new Dictionary<int, Dictionary<string, string>>();
        int reproduced = 0, diverged = 0, clockTicks = 0, randomTicks = 0, opsTicks = 0, editTicks = 0, bodyTicks = 0, terrainTicks = 0;
        long characters = 0;
        Divergence? first = null;
        var clock = Stopwatch.StartNew();
        try
        {
            for (int index = 0; index < frames.Count; index++)
            {
                ReadReplayInputs.Frame frame = frames[index];
                characters += frame.Characters;
                var disagreements = new List<string>();

                // The body before the tick is the previous tick's output, so a disagreement here is a divergence the
                // decision string did not carry — the contact, or the engine's half of the move.
                if (index > 0 && (companion.NPC.position != frame.Companion.Position || companion.NPC.velocity != frame.Companion.Velocity
                    || companion.NPC.life != frame.Companion.Life))
                {
                    bodyTicks++;
                    disagreements.Add(string.Create(CultureInfo.InvariantCulture,
                        $"body before the tick: recorded {Pose(frame.Companion.Position, frame.Companion.Velocity, frame.Companion.Life)}, replayed {Pose(companion.NPC.position, companion.NPC.velocity, companion.NPC.life)}"));
                }

                if (dropped != DroppedInput.Player)
                {
                    foreach ((string key, string value) in frame.PlayerChanges) player[key] = value;
                    if (frame.Inventory != null) inventory = frame.Inventory;
                    if (frame.Gear != null) gear = frame.Gear;
                    Inputs.ApplyPlayer(livePlayer, player, inventory, gear);
                }
                if (dropped != DroppedInput.Actors || index == 0)
                {
                    Place(frame.Npcs, npcs, Main.npc, (slot, fields) => Inputs.ApplyNpc(Main.npc[slot], fields), slot => Main.npc[slot].active = false);
                    Place(frame.Items, items, Main.item, (slot, fields) => Inputs.ApplyItem(Main.item[slot], fields), slot => Main.item[slot].active = false);
                }
                if (frame.EditsLost > 0)
                {
                    terrainTicks++;
                    disagreements.Add($"terrain: the recorder dropped {frame.EditsLost} world edit(s) before this tick, because too many waited for a companion tick, so this world lacks them");
                }
                if (dropped != DroppedInput.Edits)
                {
                    // Every tile first, then the announcements in the play's order and number, because each moves the
                    // edit log's revision once; a reframed neighbour is written and never announced, as in the play.
                    foreach (ReadReplayInputs.TileEdit edit in frame.WorldEdits) Inputs.ApplyTile(edit.X, edit.Y, edit.State);
                    foreach (ReadReplayInputs.TileEdit edit in frame.WorldEdits)
                        if (edit.Announced) TerrainChanges.Changed(edit.X, edit.Y);
                }
                foreach ((string key, string value) in frame.WorldChanges) world[key] = value;
                Inputs.ApplyWorld(world);
                // The tiles are an input nobody places whole, so they are checked where the play took a digest: a
                // disagreement here names the terrain before any decision drifts on it.
                if (frame.Terrain is { } digest)
                {
                    int colon = digest.IndexOf(':');
                    string[] corner = digest[..colon].Split(',');
                    string replayed = Inputs.HashTerrain(int.Parse(corner[0], CultureInfo.InvariantCulture), int.Parse(corner[1], CultureInfo.InvariantCulture));
                    if (replayed != digest[(colon + 1)..])
                    {
                        terrainTicks++;
                        disagreements.Add($"terrain: the tiles in the window at {digest[..colon]} hashed {digest[(colon + 1)..]} in the play and {replayed} here");
                    }
                }

                PrepareTheHeadlessEngine.StartTheWorldClockAt(frame.Tick);
                if (RunTheWorld.DriveLight)
                {
                    if (dropped != DroppedInput.Light && index > 0 && frames[index - 1].LightSeed is { } seedBefore)
                        Inputs.RestoreLightScannerSeed(seedBefore);
                    PrepareTheHeadlessEngine.DriveLightOnce(livePlayer.Bottom.ToTileCoordinates(), RunTheWorld.LightHalfWidth, RunTheWorld.LightHalfHeight);
                }
                if (dropped != DroppedInput.Random)
                {
                    if (frame.Random is { } state && state != "unreadable") Inputs.RestoreRandom("rand", state);
                    if (frame.GenRandom is { } genState && genState != "unreadable") Inputs.RestoreRandom("grand", genState);
                }
                string randomBefore = Inputs.CurrentRandomFingerprint("rand"), genRandomBefore = Inputs.CurrentRandomFingerprint("grand");
                if (dropped != DroppedInput.Clock) DecisionClock.Replay(frame.Clock);

                edited.Clear();
                editedSeen.Clear();
                insideTheTick = true;
                try
                {
                    companion.AI();
                }
                catch (Exception failure)
                {
                    throw new InvalidOperationException($"the reproduction threw at recorded tick {frame.Tick} (frame {index} of {frames.Count})", failure);
                }
                finally
                {
                    insideTheTick = false;
                }
                string decision = Inputs.DescribeDecision(companion);

                if (DecisionClock.Replaying && (DecisionClock.ReplayOverruns > 0 || DecisionClock.ReplayUnasked > 0))
                {
                    clockTicks++;
                    disagreements.Add($"decision clock: the play asked {Asked(frame.Clock)} deadline questions and this tick asked "
                        + $"{Asked(frame.Clock) - DecisionClock.ReplayUnasked + DecisionClock.ReplayOverruns}");
                }
                DecisionClock.StopReplaying();

                bool randomDisagreed = false;
                foreach ((string name, string before, string? recordedEnd) in new[]
                {
                    ("Main.rand", randomBefore, frame.RandomEnd),
                    ("WorldGen.genRand", genRandomBefore, frame.GenRandomEnd),
                })
                {
                    string after = Inputs.CurrentRandomFingerprint(name == "Main.rand" ? "rand" : "grand");
                    if (recordedEnd is { } end ? end == after : after == before) continue;
                    randomDisagreed = true;
                    disagreements.Add(recordedEnd is null
                        ? $"random: this tick drew from {name} and the play's did not"
                        : $"random: {name} ended the play's tick at {recordedEnd} and this one at {after}");
                }
                if (randomDisagreed) randomTicks++;

                var brain = companion.Brain;
                string ops = brain.LastTick == Main.GameUpdateCount && brain.LastAllowance is { } allowance
                    ? $"{allowance.OperationsUsed.ToString(CultureInfo.InvariantCulture)}.{(allowance.Cut ? "1" : "0")}.{(allowance.FirstCutSubsystem.Length == 0 ? "-" : allowance.FirstCutSubsystem)}"
                    : "-";
                if (ops != frame.Ops)
                {
                    opsTicks++;
                    disagreements.Add($"allowance: the play spent {frame.Ops} and this tick {ops}");
                }

                string edits = string.Join("|", edited.Select(tile => $"{tile.X},{tile.Y},{Inputs.DescribeTile(tile.X, tile.Y)}"));
                string recordedEdits = string.Join("|", frame.CompanionEdits.Select(edit => $"{edit.X},{edit.Y},{edit.State}"));
                if (edits != recordedEdits)
                {
                    editTicks++;
                    disagreements.Add($"companion's edits: the play made [{recordedEdits}] and this tick [{edits}]");
                }

                if (decision != frame.Decision) disagreements.Add("decision");

                PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);

                if (disagreements.Count == 0) reproduced++;
                else
                {
                    diverged++;
                    first ??= new Divergence(index, frame.Tick, disagreements, frame.Decision, decision);
                }
                if (printEach)
                    Console.WriteLine($"  {frame.Tick} {(disagreements.Count == 0 ? "same" : "DIFFERS " + string.Join(" / ", disagreements))} :: {decision}");
            }
        }
        finally
        {
            DecisionClock.StopReplaying();
            TerrainChanges.EditObserved = recorderObserver;
            if (recordTo != null) AttachTheRecorder.Close();
        }
        clock.Stop();
        return new Result(record.Capture, frames.Count, reproduced, first, diverged, clockTicks, randomTicks, opsTicks, editTicks, bodyTicks,
            terrainTicks, characters, clock.Elapsed.TotalSeconds, dropped);
    }

    /// <summary>Accumulate this tick's slot changes and make the live array hold exactly what the record holds.</summary>
    private static void Place<T>(IReadOnlyList<ReadReplayInputs.SlotChange> changes, Dictionary<int, Dictionary<string, string>> current,
        T[] live, Action<int, IReadOnlyDictionary<string, string>> apply, Action<int> empty) where T : Entity
    {
        foreach (ReadReplayInputs.SlotChange change in changes)
        {
            if (change.Fields == null) { current.Remove(change.Slot); continue; }
            if (!current.TryGetValue(change.Slot, out var fields)) current[change.Slot] = fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in change.Fields) fields[key] = value;
        }
        for (int slot = 0; slot < live.Length; slot++)
        {
            // The companion is never one of the recorded actors: it is what the reproduction computes.
            if (live[slot] == null || live[slot] is NPC { ModNPC: CompanionNPC }) continue;
            if (current.TryGetValue(slot, out var fields)) apply(slot, fields);
            else if (live[slot].active) empty(slot);
        }
    }

    private static int Asked(string tape)
        => tape.Length == 0 ? 0 : tape.Split('.').Sum(part => int.Parse(part, CultureInfo.InvariantCulture));

    private static string Pose(Vector2 position, Vector2 velocity, int life)
        => string.Create(CultureInfo.InvariantCulture, $"{position.X:R},{position.Y:R} v {velocity.X:R},{velocity.Y:R} life {life}");

    /// <summary>What a result says in one sentence, for the rows and the console.</summary>
    public static string Describe(Result result)
    {
        string share = string.Create(CultureInfo.InvariantCulture, $"{result.Reproduced} of {result.Ticks} ticks reproduced");
        if (result.First is not { } first) return $"{share}; every tick's decision and body matched the play's";
        return share + string.Create(CultureInfo.InvariantCulture,
            $"; first divergence at recorded tick {first.Tick} (frame {first.Index}): {string.Join(" / ", first.Disagreements)}; ")
            + $"recorded decision [{first.Recorded}] against replayed [{first.Replayed}]; "
            + string.Create(CultureInfo.InvariantCulture,
                $"disagreeing ticks by kind: clock {result.ClockMismatchTicks}, random {result.RandomMismatchTicks}, allowance {result.OpsMismatchTicks}, "
                + $"companion edits {result.EditMismatchTicks}, body {result.BodyMismatchTicks}, terrain {result.TerrainMismatchTicks}")
            + (result.Dropped == DroppedInput.None ? "" : $"; input deliberately left out: {result.Dropped}");
    }
}
