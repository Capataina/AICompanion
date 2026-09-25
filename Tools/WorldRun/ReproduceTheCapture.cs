extern alias live;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using live::AICompanion.Companion.CharacterBody;
using Inputs = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs;
using DecisionClock = live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.DecisionClock;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using KnowledgeRevision = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.KnowledgeRevision;

/// <summary>
/// A capture played back as the input it recorded rather than as a scene to re-simulate: the world put back tick for
/// tick from its `replay-inputs`, the brain asked the same tick again, and its decision compared with the one the
/// capture holds.
///
/// Nothing here runs a hostile's AI, moves a drop or times a search. Every NPC, dropped item and hostile projectile is
/// *placed* from the record before the tick — its type, position, velocity, life, AI fields and buffs — and a slot the
/// record does not hold is emptied; the companion is put in the NPC slot it held, so every recorded actor keeps its own;
/// the player is placed with his inventory, the companion's gear and its cargo bag; the world's own terrain edits are
/// written at their ticks; the light scanner is handed the seed it had; `Main.rand` is handed the state the tick started
/// from on a tick that drew; and every question the brain asks the wall clock is answered from the tape the play
/// recorded, so a search is cut at the expansion where the play cut it whatever this machine's speed. The companion's
/// own body, its edits and its decisions are the only things computed, and they are what is compared.
///
/// Two modes, because the two kinds of capture fail differently. <see cref="Mode.Strict"/> seeds the body once and then
/// compares the body the replay's motor produced against the play's before every tick — the self-consistency verdict's
/// mode, where every input was written by the recorder under test. <see cref="Mode.Reseat"/> puts the recorded body back
/// before every tick and compares decisions only: in play a hostile's knockback moves the body between companion ticks
/// and nothing the record carries can re-create it, so a strict replay of a played capture would diverge at the first hit
/// and count every later tick as lost, which measures the first hit rather than the decisions.
///
/// A tick is reproduced when, in the mode's terms, the decision after it — the course's activity, reason and bound step,
/// the activity holding the body, the movement request, the motor's controls, the velocity handed to the body, and from
/// format 2 what the hand released and what the bag took — is the play's to the character. The first tick that is not is
/// reported with both sides and with every other disagreement found at that tick, in the order the tick meets them,
/// because the earliest one is the best lead on which input the record is missing.
/// </summary>
internal static class ReproduceTheCapture
{
    /// <summary>An input the reproduction can be told to leave out, so a run can show which inputs a window depends
    /// on — and so the self-consistency verdict can be shown to fail when one is missing.</summary>
    internal enum DroppedInput { None, Actors, Clock, Random, Light, Player, Edits }

    internal enum Mode { Strict, Reseat }

    /// <summary>
    /// What a played capture depends on that no line of the record carries, named in every row a played capture files so
    /// its share never reads as a complete reproduction. The Diagnostics guide's audit table is the long form.
    /// </summary>
    public const string UncarriedForAPlayedCapture =
        "the world file as it stood when the session opened (this run loads the .wld named on the command line); what the game's "
        + "own hooks feed the brain between ticks — the companion's shots in flight and where they landed, and the weapon learning "
        + "they teach, the player's own tile hits and the ores he held; the player's defence, damage multipliers, armour and "
        + "accessories; terrain nobody announces (liquid flow, falling sand, growth); the light the game scanned over a screen this "
        + "host does not have; and a preference changed mid-session";

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
        int KnowledgeMismatchTicks,
        long RecordedCharacters,
        double Seconds,
        DroppedInput Dropped,
        Mode Mode,
        bool StoppedAtFirstDivergence);

    /// <summary>
    /// Why this host cannot place what a record holds, or null: a modded NPC or item type, whose defaults only its own mod
    /// can set, or a format 1 capture that records an NPC in slot 0 — format 1 does not say which slot the companion held,
    /// and a record with an actor in slot 0 says it was not there, so placing it there would displace that actor.
    /// </summary>
    public static string? Unplaceable(ReadReplayInputs.Record record)
    {
        foreach (ReadReplayInputs.Frame frame in record.Frames)
        {
            foreach (var change in frame.Npcs)
                if (change.Fields?.GetValueOrDefault("t") is { } type && int.Parse(type, CultureInfo.InvariantCulture) >= NPCID.Count)
                    return $"recorded tick {frame.Tick} places NPC type {type} in slot {change.Slot}, which is another mod's (vanilla ends at {NPCID.Count - 1}); this host loads no other mod's content, so it cannot set that NPC's defaults";
            foreach (var change in frame.Items)
                if (change.Fields?.GetValueOrDefault("t") is { } type && int.Parse(type, CultureInfo.InvariantCulture) >= ItemID.Count)
                    return $"recorded tick {frame.Tick} places item type {type} in slot {change.Slot}, which is another mod's (vanilla ends at {ItemID.Count - 1}); this host loads no other mod's content";
            // The type is the second value of an inventory (`slot:type:stack`) or bag (`slot:type:prefix:stack`) entry and the
            // first of a gear entry (`type:prefix:stack`, the four slots in order).
            foreach ((string? list, int typeAt) in new[] { (frame.Inventory, 1), (frame.Gear, 0), (frame.Bag, 1) })
                if (ModdedItemIn(list, typeAt) is { } modded)
                    return $"recorded tick {frame.Tick} puts item type {modded} in the player's inventory, the companion's gear or its bag, which is another mod's (vanilla ends at {ItemID.Count - 1}); this host loads no other mod's content";
            if (frame.Format < 2)
                foreach (var change in frame.Npcs)
                    if (change.Slot == 0 && change.Fields != null)
                        return $"{record.Capture} is format 1, which does not say which NPC slot the companion held, and recorded tick {frame.Tick} places an NPC in slot 0, so the companion was elsewhere and this reproduction cannot say where; format 2 (schema 0.50.0) records it";
        }
        return null;
    }

    /// <summary>The first item type at or above the vanilla count in a `/`-joined list whose entries hold the type at
    /// <paramref name="typeAt"/>, or null.</summary>
    private static int? ModdedItemIn(string? list, int typeAt)
    {
        if (string.IsNullOrEmpty(list)) return null;
        foreach (string entry in list.Split('/'))
        {
            string[] parts = entry.Split(':');
            if (parts.Length > typeAt && int.TryParse(parts[typeAt], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= ItemID.Count)
                return value;
        }
        return null;
    }

    /// <summary>A player save (`.tplr`) whose weapon knowledge is installed after the companion is attached, because a
    /// played capture begins from the belief its save loaded and records only a digest of it. `--knowledge-from=`.</summary>
    public static string? KnowledgeFrom;

    public static Result Run(ReadReplayInputs.Record record, int maxTicks, DroppedInput dropped, string? recordTo, bool printEach, int seed,
        Mode mode = Mode.Strict, bool stopAtFirstDivergence = false)
    {
        IReadOnlyList<ReadReplayInputs.Frame> frames = record.Frames;
        if (maxTicks > 0 && frames.Count > maxTicks) frames = frames.Take(maxTicks).ToList();
        bool inventoryWithoutPrefixes = frames.Any(frame => !string.IsNullOrEmpty(frame.Inventory)
            && frame.Inventory.Split('/').Any(entry => entry.Split(':').Length == 3));
        if (inventoryWithoutPrefixes)
            Console.WriteLine("INVENTORY the record's inventory carries no item prefixes (recorded before schema 0.51.0), so the decision digest is compared without its inventory fingerprint");
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
        // The companion's own slot, so an actor the record holds in slot 0 is placed there rather than displaced by it.
        if (opening.Self is { } openingSlot) PrepareTheHeadlessEngine.MoveTheCompanionToSlot(companion, openingSlot);
        Player livePlayer = Main.player[0];
        string inventory = opening.Inventory ?? "", gear = opening.Gear ?? "", bag = opening.Bag ?? "";
        Inputs.ApplyPlayer(livePlayer, player, inventory, gear);
        Inputs.ApplyBag(companion, bag);
        // After the attach, because attaching forgets everything learned; the first frame's digest check then says
        // whether this was the belief the play began from.
        if (KnowledgeFrom != null) Console.WriteLine("KNOWLEDGE " + LoadTheSavedKnowledge.Import(KnowledgeFrom));
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
        var projectiles = new Dictionary<int, Dictionary<string, string>>();
        int reproduced = 0, diverged = 0, clockTicks = 0, randomTicks = 0, opsTicks = 0, editTicks = 0, bodyTicks = 0, terrainTicks = 0, knowledgeTicks = 0;
        long characters = 0;
        Divergence? first = null;
        int played = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            for (int index = 0; index < frames.Count; index++)
            {
                ReadReplayInputs.Frame frame = frames[index];
                characters += frame.Characters;
                played++;
                var disagreements = new List<string>();

                if (frame.Self is { } self && self != companion.NPC.whoAmI) PrepareTheHeadlessEngine.MoveTheCompanionToSlot(companion, self);

                // The body before the tick is the previous tick's output, so in the strict mode a disagreement here is a
                // divergence the decision string did not carry — the contact, or the engine's half of the move. In the
                // reseat mode the play's body is put back instead, because what moved it between ticks is not recorded.
                if (index > 0 && mode == Mode.Reseat)
                {
                    companion.NPC.position = frame.Companion.Position;
                    companion.NPC.velocity = frame.Companion.Velocity;
                    companion.NPC.life = frame.Companion.Life;
                }
                else if (index > 0 && (companion.NPC.position != frame.Companion.Position || companion.NPC.velocity != frame.Companion.Velocity
                    || companion.NPC.life != frame.Companion.Life))
                {
                    bodyTicks++;
                    disagreements.Add(string.Create(CultureInfo.InvariantCulture,
                        $"body before the tick: recorded {Pose(frame.Companion.Position, frame.Companion.Velocity, frame.Companion.Life)}, replayed {Pose(companion.NPC.position, companion.NPC.velocity, companion.NPC.life)}"));
                }

                // The weapon knowledge is a belief the save loaded and hooks between ticks teach, and this record carries
                // only a digest of it: a disagreement is named at the tick it appears, as an input this run does not hold.
                if (frame.Knowledge is { } knowledge && Inputs.DescribeKnowledge() is var held && held != knowledge)
                {
                    knowledgeTicks++;
                    disagreements.Add($"weapon knowledge: the play held knowledge {knowledge} (digest.length) and this process holds {held}; the record carries its digest, not the knowledge");
                }
                else if (frame.KnowledgeRevision is { } revision && index > 0 && KnowledgeRevision.Current != revision)
                {
                    knowledgeTicks++;
                    disagreements.Add(string.Create(CultureInfo.InvariantCulture,
                        $"weapon knowledge: the play's revision was {revision} before this tick and this process's is {KnowledgeRevision.Current}; what the hooks learned between ticks is not recorded"));
                }

                if (dropped != DroppedInput.Player)
                {
                    foreach ((string key, string value) in frame.PlayerChanges) player[key] = value;
                    if (frame.Inventory != null) inventory = frame.Inventory;
                    if (frame.Gear != null) gear = frame.Gear;
                    if (frame.Bag != null) bag = frame.Bag;
                    Inputs.ApplyPlayer(livePlayer, player, inventory, gear);
                    Inputs.ApplyBag(companion, bag);
                }
                if (dropped != DroppedInput.Actors || index == 0)
                {
                    Place(frame.Npcs, npcs, Main.npc, (slot, fields) => Inputs.ApplyNpc(Main.npc[slot], fields), slot => Main.npc[slot].active = false);
                    Place(frame.Items, items, Main.item, (slot, fields) => Inputs.ApplyItem(Main.item[slot], fields), slot => Main.item[slot].active = false);
                    // Only a placed hostile is emptied: the companion's own shots are this run's and share the table.
                    Place(frame.Projectiles, projectiles, Main.projectile, (slot, fields) => Inputs.ApplyProjectile(Main.projectile[slot], fields),
                        slot => { if (Main.projectile[slot].hostile) Main.projectile[slot].active = false; });
                }
                if (frame.EditsLost > 0)
                {
                    terrainTicks++;
                    disagreements.Add($"terrain: the recorder dropped {frame.EditsLost} world edit(s) before this tick, because too many waited for a companion tick, so this world lacks them");
                }
                if (dropped != DroppedInput.Edits)
                {
                    // Every tile first, then the announcements in the play's number, because each moves the edit log's
                    // revision once; a reframed neighbour is written and never announced, as in the play. Format 2 writes a
                    // tile's repeated announcements as one entry with a count, so they are made back to back here: every
                    // consumer of the edit log asks whether any edit since a revision landed in its box, and all of these
                    // precede the tick that asks, so their order among themselves is not something a decision can read.
                    foreach (ReadReplayInputs.TileEdit edit in frame.WorldEdits) Inputs.ApplyTile(edit.X, edit.Y, edit.State);
                    foreach (ReadReplayInputs.TileEdit edit in frame.WorldEdits)
                        for (int announcement = 0; announcement < edit.Announcements; announcement++) TerrainChanges.Changed(edit.X, edit.Y);
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
                Inputs.NoteTheTickStart(companion);
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
                string decision = Inputs.DescribeDecision(companion, frame.Format);

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

                // A capture whose inventory entries carry no prefix cannot put back the prefixes the digest's inventory
                // fingerprint hashes, so its last field is compared only when the record holds what it is made of.
                if (inventoryWithoutPrefixes ? WithoutInventoryFingerprint(decision) != WithoutInventoryFingerprint(frame.Decision) : decision != frame.Decision)
                    disagreements.Add("decision");

                PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);

                if (disagreements.Count == 0) reproduced++;
                else
                {
                    diverged++;
                    first ??= new Divergence(index, frame.Tick, disagreements, frame.Decision, decision);
                }
                if (printEach)
                    Console.WriteLine($"  {frame.Tick} {(disagreements.Count == 0 ? "same" : "DIFFERS " + string.Join(" / ", disagreements))} :: {decision}");
                if (stopAtFirstDivergence && first != null) break;
            }
        }
        finally
        {
            DecisionClock.StopReplaying();
            TerrainChanges.EditObserved = recorderObserver;
            if (recordTo != null) AttachTheRecorder.Close();
        }
        clock.Stop();
        return new Result(record.Capture, played, reproduced, first, diverged, clockTicks, randomTicks, opsTicks, editTicks, bodyTicks,
            terrainTicks, knowledgeTicks, characters, clock.Elapsed.TotalSeconds, dropped, mode, stopAtFirstDivergence && first != null);
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

    /// <summary>A decision digest with the inventory fingerprint that ends it (`transfers:bag:inventory`) removed.</summary>
    private static string WithoutInventoryFingerprint(string digest)
    {
        int colon = digest.LastIndexOf(':');
        return colon < 0 ? digest : digest[..colon];
    }

    private static string Pose(Vector2 position, Vector2 velocity, int life)
        => string.Create(CultureInfo.InvariantCulture, $"{position.X:R},{position.Y:R} v {velocity.X:R},{velocity.Y:R} life {life}");

    /// <summary>What a result says in one sentence, for the rows and the console.</summary>
    public static string Describe(Result result)
    {
        string mode = result.Mode == Mode.Strict ? "strict: the body seeded once and compared every tick"
            : "reseat: the recorded body put back before every tick, decisions compared";
        string share = string.Create(CultureInfo.InvariantCulture, $"{result.Reproduced} of {result.Ticks} ticks reproduced ({mode})");
        if (result.First is not { } first) return $"{share}; every tick's decision{(result.Mode == Mode.Strict ? " and body" : "")} matched the play's";
        return share + string.Create(CultureInfo.InvariantCulture,
            $"; first divergence at recorded tick {first.Tick} (frame {first.Index}): {string.Join(" / ", first.Disagreements)}; ")
            + $"recorded decision [{first.Recorded}] against replayed [{first.Replayed}]; "
            + string.Create(CultureInfo.InvariantCulture,
                $"disagreeing ticks by kind: clock {result.ClockMismatchTicks}, random {result.RandomMismatchTicks}, allowance {result.OpsMismatchTicks}, "
                + $"companion edits {result.EditMismatchTicks}, body {result.BodyMismatchTicks}, terrain {result.TerrainMismatchTicks}, knowledge {result.KnowledgeMismatchTicks}")
            + (result.StoppedAtFirstDivergence ? "; stopped at the first divergence" : "")
            + (result.Dropped == DroppedInput.None ? "" : $"; input deliberately left out: {result.Dropped}");
    }
}
