extern alias live;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.CharacterBody;
using Inputs = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs;
using Queue = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.QueueDiagnosticRecords;

/// <summary>
/// What the replay recorder costs the game thread on a crowded world, measured on the recorder alone.
///
/// `RecordTelemetry` is on by default, so every player pays the `replay-inputs` recorder on every tick, and a player with many
/// mods runs worlds whose NPC table is full and whose floor is covered in drops. The scenes the world run otherwise plays hold
/// a dozen hostiles, so the recorder's cost there says nothing about the table it walks. This fills the table instead —
/// every NPC slot but the companion's and every item slot — and calls the recorder's two halves directly around a tick that
/// runs no brain, so the figure is the recorder's and nothing else's: once with every entity standing still, which is what a
/// recorder that formats only what changed should find nearly free, and once with every entity moving every tick, which is
/// the worst a real frame can ask of it.
///
/// Each tick waits for the writer's worker to drain the queue before the next is timed, because a recorder called in a tight
/// loop outruns a disk that a real frame at sixty a second does not, and a refused line is a different cost from a written one.
/// The figures are measures and never a pass line; a crowded scene's allocation per tick is filed beside its time because a
/// collection is paid for by whoever allocated.
/// </summary>
internal static class MeasureTheReplayRecorder
{
    public const string StillCase = "replay-input recording per tick, 199 NPCs and 400 drops standing still";
    public const string MovingCase = "replay-input recording per tick, 199 NPCs and 400 drops all moving";
    public const string StillBytesCase = "replay-input recording allocation per tick, 199 NPCs and 400 drops standing still";
    public const string MovingBytesCase = "replay-input recording allocation per tick, 199 NPCs and 400 drops all moving";
    public const string ReadersCase = "the replay recorder's one-call readers agree with its field tables";

    private static readonly int[] NpcTypes = { NPCID.Zombie, NPCID.BlueSlime, NPCID.DemonEye, NPCID.Skeleton };
    private static readonly int[] ItemTypes = { ItemID.DirtBlock, ItemID.StoneBlock, ItemID.Wood, ItemID.Torch, ItemID.Gel };

    public static int Run(string world, int ticks, string suite, string? recordTo)
    {
        LoadTheSavedWorld.Load(world);
        PrepareTheHeadlessEngine.PinEveryRandomSource(1);
        PrepareTheHeadlessEngine.StartTheWorldClockAt(1000);
        var feet = new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f);
        CompanionNPC companion = PrepareTheHeadlessEngine.AttachCompanion(feet - new Vector2(0f, 48f), feet);
        string folder = recordTo ?? Path.Combine(Path.GetTempPath(), "aicompanion-recorder-cost",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture));

        int failures = 0;
        foreach (bool moving in new[] { false, true })
        {
            FillTheTables(companion, feet);
            AttachTheRecorder.Open(folder, "recorder-cost");
            try
            {
                var (milliseconds, bytes, characters) = TimeTheRecorder(companion, ticks, moving);
                // The recorder's one-call readers exist only for speed; a field they read differently from the table would
                // leave a change unrecorded, so both scenes' last state is checked against the per-field definition — after
                // every field of every entity has been given a value distinct from its neighbours' and its slot's, because a
                // freshly filled table holds life equal to its maximum and a dozen false flags, and a reader that swapped two
                // such fields would agree on it.
                GiveEveryFieldItsOwnValue();
                if (Inputs.WholeReadersAgree(out string disagreement))
                    EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, ReadersCase + (moving ? " after every entity moved" : " on the filled table"),
                        "every live NPC's and item's raw values read the same through the one-call reader and through each field's table entry",
                        mode: "recorder-cost");
                else
                {
                    failures++;
                    EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, ReadersCase + (moving ? " after every entity moved" : " on the filled table"),
                        disagreement, mode: "recorder-cost");
                }
                string scene = moving ? "every NPC's position, velocity and first AI value and every drop's position changed each tick"
                    : "nothing changed after the first tick";
                string message = FormattableString.Invariant(
                    $"{ticks} ticks after 30 warm-up ticks; {scene}; mean {milliseconds.Average():0.0000} ms, p50 {Percentile(milliseconds, 0.5):0.0000}, p99 {Percentile(milliseconds, 0.99):0.0000}, of which the top half (the inputs) {topHalf * 1000.0 / Stopwatch.Frequency / ticks:0.0000}; mean {bytes.Average():0} bytes allocated and {characters.Average():0} characters written a tick; the recorder's two halves called directly, no brain, the writer drained between ticks");
                Console.WriteLine($"COST {(moving ? "moving" : "still")}: {message}");                string[] tags = { EmitLedgerRows.TimedTag, EmitLedgerRows.SampledTag };
                EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, moving ? MovingCase : StillCase, milliseconds.Average(), "ms",
                    direction: "lower", mode: "recorder-cost", tags: tags, message: message);
                EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, moving ? MovingBytesCase : StillBytesCase, bytes.Average(), "bytes",
                    direction: "lower", mode: "recorder-cost", tags: new[] { EmitLedgerRows.SampledTag }, message: message);
            }
            finally
            {
                AttachTheRecorder.Close();
            }
        }
        failures += FlushABurstOfEdits(companion, feet, folder, suite);
        failures += RefuseOneLine(companion, feet, folder, suite);
        if (recordTo == null) Directory.Delete(folder, recursive: true);
        return failures;
    }

    public const string BurstCase = "a companion-less burst of world edits flushes as one bounded line the queue accepts";
    public const string BurstCostCase = "the first replay-input line after a companion-less burst of 20,000 edits";

    /// <summary>
    /// A stretch with no companion ticking while the player edits the world — the session opens at world load, before any
    /// companion exists — collapsed into its worst case: 20,000 distinct tiles announced, 5,000 of them twice more, and
    /// then one companion tick. The recorder must bound what it kept, write the line in one bounded piece and name what it
    /// could not keep as `edits-lost`, and the queue must take the line, because a refused line costs every frame after it.
    /// </summary>
    private static int FlushABurstOfEdits(CompanionNPC companion, Vector2 feet, string folder, string suite)
    {
        FillTheTables(companion, feet);
        AttachTheRecorder.Open(folder, "recorder-cost-burst");
        try
        {
            Point origin = feet.ToTileCoordinates() + new Point(-100, -80);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long announcing = Stopwatch.GetTimestamp();
            for (int index = 0; index < 20000; index++)
                live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Changed(origin.X + index % 200, origin.Y + index / 200);
            for (int repeat = 0; repeat < 2; repeat++)
                for (int index = 0; index < 5000; index++)
                    live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Changed(origin.X + index % 200, origin.Y + index / 200);
            double announceMs = (Stopwatch.GetTimestamp() - announcing) * 1000.0 / Stopwatch.Frequency;
            long allocatedWhileAnnouncing = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            PrepareTheHeadlessEngine.AdvanceTheWorldClock();
            long charactersBefore = CharactersWritten();
            long refusedBefore = LinesRefused();
            long started = Stopwatch.GetTimestamp();
            Inputs.BeforeTheCompanionTick(companion);
            Inputs.AfterTheCompanionTick(companion);
            double flushMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            WaitForTheWriter();
            long characters = CharactersWritten() - charactersBefore;
            long refused = LinesRefused() - refusedBefore;
            string written = LastLine();
            int lost = written.Split(';').FirstOrDefault(part => part.StartsWith("edits-lost=", StringComparison.Ordinal)) is { } part
                ? int.Parse(part["edits-lost=".Length..], CultureInfo.InvariantCulture) : 0;
            string edits = written.Split(';').FirstOrDefault(part => part.StartsWith("edits=", StringComparison.Ordinal)) ?? "";
            int entries = edits.Length > 6 ? edits.Split('|').Length : 0;
            // The bound the recorder's own snapshot cap implies for the edits, at a generous sixty characters an entry. The
            // rest of the line is this scene's full keyframe of 600 entities, which the cost cases above already price.
            long bound = (long)live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs.MaximumSnapshotTiles * 60;
            string message = FormattableString.Invariant(
                $"20,000 distinct tiles announced plus 10,000 repeats with no companion ticking ({announceMs:0.0} ms and {allocatedWhileAnnouncing:0} bytes to hear them), then one tick: {characters} characters written in {flushMs:0.00} ms, {edits.Length} of them the edits' {entries} entries, edits-lost={lost}, {refused} line(s) refused; the edits may hold at most {bound} characters");
            Console.WriteLine($"BURST {message}");
            EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, BurstCostCase, flushMs, "ms", direction: "lower", mode: "recorder-cost",
                tags: new[] { EmitLedgerRows.TimedTag, EmitLedgerRows.SampledTag }, message: message);
            bool bounded = characters > 0 && edits.Length <= bound && refused == 0 && lost > 0
                && entries <= live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs.MaximumSnapshotTiles;
            if (bounded)
            {
                EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, BurstCase, message, mode: "recorder-cost");
                return 0;
            }
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, BurstCase, message, mode: "recorder-cost");
            return 1;
        }
        finally
        {
            AttachTheRecorder.Close();
        }
    }

    public const string RefusedLineCase = "a refused replay-inputs line leaves a gap the reader names and a keyframe after it";

    /// <summary>
    /// The writer's queue refusing one line, forced by switching its admission off for exactly one line: the recorder must
    /// number past the lost line, start the next from nothing (`key=1`), and the reader must refuse the capture naming the
    /// gap rather than reproduce frames that are deltas against a line nobody wrote.
    /// </summary>
    private static int RefuseOneLine(CompanionNPC companion, Vector2 feet, string folder, string suite)
    {
        FillTheTables(companion, feet);
        string telemetry = AttachTheRecorder.Open(folder, "recorder-cost-refusal");
        var accepting = typeof(Queue).GetField("accepting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException("QueueDiagnosticRecords.accepting is gone; the refusal case cannot make the queue refuse a line");
        var lines = new List<string>();
        long refusedBefore = LinesRefused();
        try
        {
            for (int tick = 0; tick < 4; tick++)
            {
                PrepareTheHeadlessEngine.AdvanceTheWorldClock();
                MoveEverything(tick);
                Inputs.BeforeTheCompanionTick(companion);
                if (tick == 1) accepting.SetValue(null, false);
                try { Inputs.AfterTheCompanionTick(companion); }
                finally { if (tick == 1) accepting.SetValue(null, true); }
                lines.Add(LastLine());
                WaitForTheWriter();
            }
        }
        finally
        {
            AttachTheRecorder.Close();
        }
        long refused = LinesRefused() - refusedBefore;
        string capture = Directory.GetFiles(telemetry, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        string? refusal = ReadReplayInputs.Read(capture).Refusal;
        bool keyframeAfter = lines[2].Contains(";n=2;", StringComparison.Ordinal) && lines[2].Contains(";key=1", StringComparison.Ordinal);
        bool deltaBefore = lines[1].Contains(";n=1;", StringComparison.Ordinal) && !lines[1].Contains(";key=1", StringComparison.Ordinal);
        bool deltaAfter = lines[3].Contains(";n=3;", StringComparison.Ordinal) && !lines[3].Contains(";key=1", StringComparison.Ordinal);
        bool named = refusal != null && refusal.Contains("n=1..1", StringComparison.Ordinal);
        string message = FormattableString.Invariant(
            $"4 ticks with the queue refusing the second line: {refused} line(s) refused; the third line {(keyframeAfter ? "is" : "is NOT")} n=2 with key=1 ({lines[2].Length} characters against {lines[3].Length} for the delta after it); the fourth {(deltaAfter ? "is" : "is NOT")} a delta again; the reader {(named ? "refused naming n=1..1" : "did not name the gap")}: {refusal ?? "no refusal"}");
        Console.WriteLine($"REFUSAL {message}");
        if (refused == 1 && keyframeAfter && deltaBefore && deltaAfter && named)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, RefusedLineCase, message, mode: "recorder-cost");
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, RefusedLineCase, message, mode: "recorder-cost");
        return 1;
    }

    /// <summary>The line the recorder last built, read from its reused buffer, so the burst case can read what it wrote.</summary>
    private static string LastLine()
        => typeof(Inputs).GetField("line", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.GetValue(null)?.ToString()
            ?? throw new MissingFieldException("ReplayInputs.line is gone; the burst case cannot read the line it measures");

    private static long LinesRefused()
        => (long)(typeof(Inputs).GetProperty("LinesRefused", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) ?? throw new MissingMemberException("ReplayInputs.LinesRefused is gone; the burst case cannot tell a refused line"));

    /// <summary>Every NPC slot but the companion's and every item slot occupied, far from the body, each entity distinct.</summary>
    private static void FillTheTables(CompanionNPC companion, Vector2 feet)
    {
        for (int slot = 0; slot < Main.maxNPCs; slot++)
        {
            if (ReferenceEquals(Main.npc[slot], companion.NPC)) continue;
            Main.npc[slot] ??= new NPC();
            NPC npc = Main.npc[slot];
            npc.SetDefaults(NpcTypes[slot % NpcTypes.Length]);
            npc.whoAmI = slot;
            npc.position = feet + new Vector2(slot * 24f, -800f);
            npc.velocity = new Vector2(0.5f, 0f);
            npc.active = true;
        }
        for (int slot = 0; slot < Main.maxItems; slot++)
        {
            Main.item[slot] ??= new Item();
            Item item = Main.item[slot];
            item.SetDefaults(ItemTypes[slot % ItemTypes.Length]);
            item.stack = 1 + slot % 7;
            item.whoAmI = slot;
            item.position = feet + new Vector2(slot * 12f, -1200f);
            item.active = true;
        }
    }

    private static (List<double> Milliseconds, List<double> Bytes, List<double> Characters) TimeTheRecorder(CompanionNPC companion, int ticks, bool moving)
    {
        var milliseconds = new List<double>(ticks);
        var bytes = new List<double>(ticks);
        var characters = new List<double>(ticks);
        topHalf = 0;
        for (int tick = -30; tick < ticks; tick++)
        {
            PrepareTheHeadlessEngine.AdvanceTheWorldClock();
            if (moving && tick > -30) MoveEverything(tick);
            long charactersBefore = CharactersWritten();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            Inputs.BeforeTheCompanionTick(companion);
            long halfway = Stopwatch.GetTimestamp();
            Inputs.AfterTheCompanionTick(companion);
            long elapsed = Stopwatch.GetTimestamp() - started;
            if (tick >= 0) topHalf += halfway - started;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            WaitForTheWriter();
            if (tick < 0) continue;
            milliseconds.Add(elapsed * 1000.0 / Stopwatch.Frequency);
            bytes.Add(allocated);
            characters.Add(CharactersWritten() - charactersBefore);
        }
        return (milliseconds, bytes, characters);
    }

    /// <summary>
    /// Every field the recorder reads off an NPC or a drop set to a value no other field of that entity holds, and each
    /// boolean to both values across the table, so the reader check can tell any two fields apart.
    /// </summary>
    private static void GiveEveryFieldItsOwnValue()
    {
        foreach (NPC npc in Main.npc)
        {
            if (npc == null || !npc.active || npc.ModNPC is CompanionNPC) continue;
            int slot = npc.whoAmI, value = slot * 100;
            float Next() => ++value + 0.25f;
            bool Flag(int field) => (slot + field) % 2 == 0;
            live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.AssumeGeneration(npc, ++value);
            live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.AssumeShot(npc, value, (uint)(++value), ++value);
            npc.position = new Vector2(Next(), Next());
            npc.velocity = new Vector2(Next(), Next());
            npc.oldPosition = new Vector2(Next(), Next());
            npc.life = ++value; npc.lifeMax = ++value;
            npc.direction = ++value; npc.directionY = ++value; npc.spriteDirection = ++value;
            for (int index = 0; index < 4; index++) { npc.ai[index] = Next(); npc.localAI[index] = Next(); }
            npc.target = ++value;
            npc.noGravity = Flag(1); npc.noTileCollide = Flag(2); npc.collideX = Flag(3); npc.collideY = Flag(4);
            npc.wet = Flag(5); npc.lavaWet = Flag(6); npc.honeyWet = Flag(7);
            npc.damage = ++value; npc.defense = ++value; npc.knockBackResist = Next();
            npc.friendly = Flag(8); npc.dontTakeDamage = Flag(9);
            npc.realLife = ++value; npc.width = ++value; npc.height = ++value; npc.scale = Next();
            npc.frame.Y = ++value; npc.timeLeft = ++value; npc.justHit = Flag(10);
            npc.immune[Main.myPlayer] = ++value;
            for (int index = 0; index < npc.buffType.Length; index++) { npc.buffType[index] = ++value; npc.buffTime[index] = ++value; }
        }
        foreach (Item item in Main.item)
        {
            if (item == null || !item.active) continue;
            int slot = item.whoAmI, value = slot * 100;
            item.stack = ++value;
            item.position = new Vector2(++value + 0.5f, ++value + 0.5f);
            item.velocity = new Vector2(++value + 0.5f, ++value + 0.5f);
            item.noGrabDelay = ++value; item.keepTime = ++value; item.beingGrabbed = slot % 2 == 0;
            item.playerIndexTheItemIsReservedFor = ++value; item.timeSinceItemSpawned = ++value; item.wet = slot % 2 == 1;
        }
    }

    /// <summary>Timestamps spent in the top half, the inputs, over the timed ticks of the scene being measured.</summary>
    private static long topHalf;

    private static void MoveEverything(int tick)
    {
        float step = (tick % 2 == 0) ? 0.75f : -0.5f;
        foreach (NPC npc in Main.npc)
        {
            if (npc == null || !npc.active || npc.ModNPC is CompanionNPC) continue;
            npc.position += new Vector2(step, step * 0.5f);
            npc.velocity = new Vector2(step, -step);
            npc.ai[0] += 1f;
        }
        foreach (Item item in Main.item)
        {
            if (item == null || !item.active) continue;
            item.position += new Vector2(0f, step);
        }
    }

    /// <summary>The characters the recorder reports writing this session, through its public closing counters when it has them.</summary>
    private static long CharactersWritten()
        => (long)(typeof(Inputs).GetProperty("CharactersWritten", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(null) ?? 0L);

    /// <summary>Until the writer's worker has written or dropped everything enqueued, bounded at two seconds.</summary>
    private static void WaitForTheWriter()
    {
        var waited = Stopwatch.StartNew();
        while (Queue.Written + Queue.Dropped < Queue.Enqueued && waited.ElapsedMilliseconds < 2000) Thread.Sleep(1);
    }

    private static double Percentile(List<double> values, double quantile)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)(quantile * sorted.Count))];
    }
}
