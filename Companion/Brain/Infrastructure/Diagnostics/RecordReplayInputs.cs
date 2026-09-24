#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Utilities;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// What one companion tick found in the world and what it was allowed to spend, written so a replay can put the
/// world back exactly and ask the same tick again — one `replay-inputs` occurrence per companion tick, with the
/// decision the tick made beside its inputs so the replay can say where it first disagreed.
///
/// <para>The inputs are taken at the top of <c>CompanionNPC.AI</c>, before anything of the companion's runs,
/// because that is the state the tick observed: a hostile in a lower slot has already moved this frame and one in
/// a higher slot has not, and only a snapshot at the tick's own start is right about both. The decision half is
/// taken at the bottom, just before the recorder's row.</para>
///
/// <para>Everything that changes every tick is delta-encoded against the last value written for the same slot and
/// field, because a scene is mostly still: a lying drop, a player standing, a hostile's type and size. An entity
/// entry names only the fields that moved, `slot:-` retires a slot, and a session's first tick writes everything.
/// So a reader must read a session from its first `replay-inputs` line; one opened in the middle is missing
/// whatever was last written before it.</para>
///
/// <para>One field table per entity kind is both the writer's and the replay's list, so a field recorded and not
/// applied, or applied and not recorded, cannot exist: <see cref="ApplyNpc"/>, <see cref="ApplyItem"/> and
/// <see cref="ApplyPlayer"/> walk the same arrays the recorder walks. The apply half is harness-only; nothing in
/// the mod calls it.</para>
///
/// <para>The format, one `;`-separated line, is in the Diagnostics guide's replay section, which also carries the
/// audit of every input the brain reads against what this line holds.</para>
/// </summary>
public static class ReplayInputs
{
    /// <summary>The line's own format version, written first so a reader can refuse one it does not know.</summary>
    public const int FormatVersion = 1;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public sealed record Field<T>(string Key, Func<T, string> Read, Action<T, string> Write);

    // Shortest round-trip text, so a replayed float is the recorded float to the bit.
    public static string F(float value) => value.ToString("R", Invariant);
    public static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, Invariant);
    public static string I(int value) => value.ToString(Invariant);
    public static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, Invariant);
    private static string B(bool value) => value ? "1" : "0";
    private static bool ParseBool(string text) => text == "1";

    /// <summary>
    /// What the brain reads off a hostile, a critter or any other NPC it can see. `t` is first and is applied
    /// first, because setting a type resets every other field.
    /// </summary>
    public static readonly Field<NPC>[] NpcFields =
    {
        new("t", n => I(n.type), (n, v) => { int type = ParseInt(v); if (!n.active || n.type != type) n.SetDefaults(type); }),
        // The entity's own idea of its slot, recorded rather than assumed: the census names a target by it, and an entity
        // the engine spawned through a path that never set it carries whatever the slot's last occupant left.
        new("wh", n => I(n.whoAmI), (n, v) => n.whoAmI = ParseInt(v)),
        new("x", n => F(n.position.X), (n, v) => n.position.X = ParseFloat(v)),
        new("y", n => F(n.position.Y), (n, v) => n.position.Y = ParseFloat(v)),
        new("vx", n => F(n.velocity.X), (n, v) => n.velocity.X = ParseFloat(v)),
        new("vy", n => F(n.velocity.Y), (n, v) => n.velocity.Y = ParseFloat(v)),
        new("ox", n => F(n.oldPosition.X), (n, v) => n.oldPosition.X = ParseFloat(v)),
        new("oy", n => F(n.oldPosition.Y), (n, v) => n.oldPosition.Y = ParseFloat(v)),
        new("l", n => I(n.life), (n, v) => n.life = ParseInt(v)),
        new("lm", n => I(n.lifeMax), (n, v) => n.lifeMax = ParseInt(v)),
        new("d", n => I(n.direction), (n, v) => n.direction = ParseInt(v)),
        new("dy", n => I(n.directionY), (n, v) => n.directionY = ParseInt(v)),
        new("sd", n => I(n.spriteDirection), (n, v) => n.spriteDirection = ParseInt(v)),
        new("a0", n => F(n.ai[0]), (n, v) => n.ai[0] = ParseFloat(v)),
        new("a1", n => F(n.ai[1]), (n, v) => n.ai[1] = ParseFloat(v)),
        new("a2", n => F(n.ai[2]), (n, v) => n.ai[2] = ParseFloat(v)),
        new("a3", n => F(n.ai[3]), (n, v) => n.ai[3] = ParseFloat(v)),
        new("l0", n => F(n.localAI[0]), (n, v) => n.localAI[0] = ParseFloat(v)),
        new("l1", n => F(n.localAI[1]), (n, v) => n.localAI[1] = ParseFloat(v)),
        new("l2", n => F(n.localAI[2]), (n, v) => n.localAI[2] = ParseFloat(v)),
        new("l3", n => F(n.localAI[3]), (n, v) => n.localAI[3] = ParseFloat(v)),
        new("tg", n => I(n.target), (n, v) => n.target = ParseInt(v)),
        new("ng", n => B(n.noGravity), (n, v) => n.noGravity = ParseBool(v)),
        new("nt", n => B(n.noTileCollide), (n, v) => n.noTileCollide = ParseBool(v)),
        new("cx", n => B(n.collideX), (n, v) => n.collideX = ParseBool(v)),
        new("cy", n => B(n.collideY), (n, v) => n.collideY = ParseBool(v)),
        new("w", n => B(n.wet), (n, v) => n.wet = ParseBool(v)),
        new("lw", n => B(n.lavaWet), (n, v) => n.lavaWet = ParseBool(v)),
        new("hw", n => B(n.honeyWet), (n, v) => n.honeyWet = ParseBool(v)),
        new("dm", n => I(n.damage), (n, v) => n.damage = ParseInt(v)),
        new("df", n => I(n.defense), (n, v) => n.defense = ParseInt(v)),
        new("kb", n => F(n.knockBackResist), (n, v) => n.knockBackResist = ParseFloat(v)),
        new("fr", n => B(n.friendly), (n, v) => n.friendly = ParseBool(v)),
        new("dd", n => B(n.dontTakeDamage), (n, v) => n.dontTakeDamage = ParseBool(v)),
        new("rl", n => I(n.realLife), (n, v) => n.realLife = ParseInt(v)),
        new("wd", n => I(n.width), (n, v) => n.width = ParseInt(v)),
        new("ht", n => I(n.height), (n, v) => n.height = ParseInt(v)),
        new("sc", n => F(n.scale), (n, v) => n.scale = ParseFloat(v)),
        new("fy", n => I(n.frame.Y), (n, v) => n.frame.Y = ParseInt(v)),
        new("tl", n => I(n.timeLeft), (n, v) => n.timeLeft = ParseInt(v)),
        new("jh", n => B(n.justHit), (n, v) => n.justHit = ParseBool(v)),
        new("im", n => I(n.immune[Main.myPlayer]), (n, v) => n.immune[Main.myPlayer] = ParseInt(v)),
        new("bf", DescribeBuffs, ApplyBuffs),
    };

    /// <summary>What the brain reads off a dropped item: where it lies, what it is, and whether it can be taken.</summary>
    public static readonly Field<Item>[] ItemFields =
    {
        new("t", i => I(i.type), (i, v) => { int type = ParseInt(v); if (!i.active || i.type != type) i.SetDefaults(type); }),
        // A drop's census target is `item:{whoAmI}`, and a drop the engine spawned outside the ordinary path can carry a
        // `whoAmI` that is not its slot: measured 24 September 2026, the course bound a mined dirt drop lying in slot 5 as `item:0`.
        new("wh", i => I(i.whoAmI), (i, v) => i.whoAmI = ParseInt(v)),
        new("st", i => I(i.stack), (i, v) => i.stack = ParseInt(v)),
        new("x", i => F(i.position.X), (i, v) => i.position.X = ParseFloat(v)),
        new("y", i => F(i.position.Y), (i, v) => i.position.Y = ParseFloat(v)),
        new("vx", i => F(i.velocity.X), (i, v) => i.velocity.X = ParseFloat(v)),
        new("vy", i => F(i.velocity.Y), (i, v) => i.velocity.Y = ParseFloat(v)),
        new("gd", i => I(i.noGrabDelay), (i, v) => i.noGrabDelay = ParseInt(v)),
        new("kt", i => I(i.keepTime), (i, v) => i.keepTime = ParseInt(v)),
        new("bg", i => B(i.beingGrabbed), (i, v) => i.beingGrabbed = ParseBool(v)),
        new("rs", i => I(i.playerIndexTheItemIsReservedFor), (i, v) => i.playerIndexTheItemIsReservedFor = ParseInt(v)),
        new("ts", i => I(i.timeSinceItemSpawned), (i, v) => i.timeSinceItemSpawned = ParseInt(v)),
        new("w", i => B(i.wet), (i, v) => i.wet = ParseBool(v)),
    };

    /// <summary>
    /// What the brain reads off the player. His inventory and the companion's gear are their own fields on the line
    /// rather than rows here, because each is a list that changes rarely and is written whole when it does.
    /// </summary>
    public static readonly Field<Player>[] PlayerFields =
    {
        new("x", p => F(p.position.X), (p, v) => p.position.X = ParseFloat(v)),
        new("y", p => F(p.position.Y), (p, v) => p.position.Y = ParseFloat(v)),
        new("vx", p => F(p.velocity.X), (p, v) => p.velocity.X = ParseFloat(v)),
        new("vy", p => F(p.velocity.Y), (p, v) => p.velocity.Y = ParseFloat(v)),
        new("l", p => I(p.statLife), (p, v) => p.statLife = ParseInt(v)),
        new("lm", p => I(p.statLifeMax2), (p, v) => p.statLifeMax2 = ParseInt(v)),
        new("lmb", p => I(p.statLifeMax), (p, v) => p.statLifeMax = ParseInt(v)),
        new("mn", p => I(p.statMana), (p, v) => p.statMana = ParseInt(v)),
        new("mm", p => I(p.statManaMax2), (p, v) => p.statManaMax2 = ParseInt(v)),
        new("dead", p => B(p.dead), (p, v) => p.dead = ParseBool(v)),
        new("sel", p => I(p.selectedItem), (p, v) => p.selectedItem = ParseInt(v)),
        new("anim", p => I(p.itemAnimation), (p, v) => p.itemAnimation = ParseInt(v)),
        new("it", p => I(p.itemTime), (p, v) => p.itemTime = ParseInt(v)),
        new("dir", p => I(p.direction), (p, v) => p.direction = ParseInt(v)),
        new("gv", p => F(p.gravDir), (p, v) => p.gravDir = ParseFloat(v)),
        new("im", p => B(p.immune), (p, v) => p.immune = ParseBool(v)),
        new("imt", p => I(p.immuneTime), (p, v) => p.immuneTime = ParseInt(v)),
        new("li", p => B(p.longInvince), (p, v) => p.longInvince = ParseBool(v)),
        new("end", p => F(p.endurance), (p, v) => p.endurance = ParseFloat(v)),
        new("ra", p => F(p.runAcceleration), (p, v) => p.runAcceleration = ParseFloat(v)),
        new("w", p => B(p.wet), (p, v) => p.wet = ParseBool(v)),
        new("lw", p => B(p.lavaWet), (p, v) => p.lavaWet = ParseBool(v)),
        new("hw", p => B(p.honeyWet), (p, v) => p.honeyWet = ParseBool(v)),
        new("sw", p => B(p.shimmerWet), (p, v) => p.shimmerWet = ParseBool(v)),
        new("mt", p => I(p.mount.Active ? p.mount.Type : -1), ApplyMount),
        new("ctl", DescribeControls, ApplyControls),
        new("ttx", _ => I(Player.tileTargetX), (_, v) => Player.tileTargetX = ParseInt(v)),
        new("tty", _ => I(Player.tileTargetY), (_, v) => Player.tileTargetY = ParseInt(v)),
        new("bf", DescribePlayerBuffs, ApplyPlayerBuffs),
    };

    /// <summary>
    /// What the brain reads off the world rather than off an entity: the time of day, which the light engine turns into
    /// sky light and the light sense reads, and the events the encounter context names (a blood moon, an eclipse, an
    /// invasion on its way). The subject is ignored; the fields are the game's statics.
    /// </summary>
    public static readonly Field<object?>[] WorldFields =
    {
        new("day", _ => B(Main.dayTime), (_, v) => Main.dayTime = ParseBool(v)),
        new("time", _ => Main.time.ToString("R", Invariant), (_, v) => Main.time = double.Parse(v, NumberStyles.Float, Invariant)),
        new("blood", _ => B(Main.bloodMoon), (_, v) => Main.bloodMoon = ParseBool(v)),
        new("eclipse", _ => B(Main.eclipse), (_, v) => Main.eclipse = ParseBool(v)),
        new("inv", _ => I(Main.invasionType), (_, v) => Main.invasionType = ParseInt(v)),
        new("invd", _ => I(Main.invasionDelay), (_, v) => Main.invasionDelay = ParseInt(v)),
        new("invs", _ => I(Main.invasionSize), (_, v) => Main.invasionSize = ParseInt(v)),
    };

    // ---- session state: what was last written per slot, so each line carries only what moved ----

    private static bool sessionOpen;
    private static bool recordingThisTick;
    private static bool insideCompanionTick;
    private static readonly Dictionary<int, string[]> lastNpc = new();
    private static readonly Dictionary<int, string[]> lastItem = new();
    private static string[]? lastPlayer;
    private static string[]? lastWorld;
    private static string lastInventory = "";
    private static string lastGear = "";
    /// <summary>The world's edits since the last companion tick, in announcement order and with repeats, because each
    /// announcement moves the edit log's revision and a replay must move it as many times; and after them, marked as not
    /// announced, the eight neighbours of each, because the game reframes a broken tile's neighbours without announcing
    /// them and a frame is a shape to the terrain reader (a platform's top, a tree's trunk).</summary>
    private static readonly List<(Point Tile, bool Announced)> worldEdits = new();
    private static readonly HashSet<Point> worldEditsSeen = new();
    /// <summary>
    /// Each tile within <see cref="ReframeRadius"/> of an announced edit, as it stood when the edit was announced, so the
    /// next tick can write down every one the game changed. The game frames an unframed tile the first time a neighbour's
    /// framing reaches it, and a world read straight from its file is full of them: measured 24 September 2026 on a
    /// seeded soak, a dirt tile two columns from a broken one went from an unset frame to 18,18 with nothing announced,
    /// and a replay that wrote only the broken tile's eight neighbours disagreed with the play's terrain on the next edit.
    /// </summary>
    private static readonly Dictionary<Point, string> tilesBeforeEdits = new();
    private const int ReframeRadius = 3;
    private static readonly List<Point> companionEdits = new();
    private static readonly HashSet<Point> companionEditsSeen = new();
    private static string pendingInputs = "";
    /// <summary>
    /// One of the game's shared random streams, watched across the companion's tick. There are two: `Main.rand`, which
    /// the brain and the engine both draw from, and `WorldGen.genRand`, which the game's tile framing draws a frame
    /// variant from when the companion breaks or places a tile — measured 24 September 2026, a second reproduction in one
    /// process disagreed about one tick's terrain after the companion removed its own torch, because the frames around
    /// it were drawn from a stream the first pass had advanced.
    /// </summary>
    private sealed class WatchedRandom
    {
        public readonly string Key;
        public readonly Func<UnifiedRandom?> Current;
        public readonly int[] Start = new int[58], End = new int[58];
        public UnifiedRandom? StartObject;
        public bool Readable = true;
        public WatchedRandom(string key, Func<UnifiedRandom?> current) { Key = key; Current = current; }
    }

    private static readonly WatchedRandom[] watchedRandoms =
    {
        new("rand", () => Main.rand),
        new("grand", () => WorldGen.genRand),
    };

    /// <summary>The profiler's name for this file's own work, so the cost of carrying replay inputs is a share in the
    /// session reader's "where the time goes" rather than a number somebody has to take by hand. Both halves run outside
    /// the brain tick, so the top half lands in its own tick's snapshot and the bottom half in the next one's, the same
    /// way the recorder's `record` subtree does.</summary>
    private static readonly int ReplaySection = BrainSections.Register("replay-inputs");

    /// <summary>Lines written this session, for the recorder's own accounting.</summary>
    internal static long LinesWritten { get; private set; }

    /// <summary>Characters written this session, so the cost of carrying replay inputs is a number in the capture.</summary>
    internal static long CharactersWritten { get; private set; }

    /// <summary>Milliseconds this file spent on the game thread this session, both halves of every tick, so a capture
    /// states what carrying its replay inputs cost. The section profiler sees the same work, but a row names only its
    /// tick's largest sections and these are rarely among them, so a sum over rows is a lower bound; this is the whole.</summary>
    internal static double MillisecondsSpent => timestampsSpent * 1000d / System.Diagnostics.Stopwatch.Frequency;
    private static long timestampsSpent;

    /// <summary>A recording session opened: the next tick writes everything, and terrain edits start being heard.</summary>
    internal static void BeginSession()
    {
        sessionOpen = true;
        lastNpc.Clear(); lastItem.Clear(); lastPlayer = null; lastWorld = null; lastInventory = ""; lastGear = "";
        worldEdits.Clear(); worldEditsSeen.Clear(); tilesBeforeEdits.Clear(); companionEdits.Clear(); companionEditsSeen.Clear();
        pendingInputs = "";
        recordingThisTick = false;
        LinesWritten = 0;
        CharactersWritten = 0;
        timestampsSpent = 0;
        TerrainChanges.EditObserved = NoteEdit;
    }

    /// <summary>The session closed: stop hearing edits and drop any tape left running.</summary>
    internal static void EndSession()
    {
        if (!sessionOpen) return;
        sessionOpen = false;
        if (TerrainChanges.EditObserved == NoteEdit) TerrainChanges.EditObserved = null;
        if (recordingThisTick) DecisionClock.EndTape();
        recordingThisTick = false;
    }

    private static void NoteEdit(int x, int y)
    {
        var tile = new Point(x, y);
        if (insideCompanionTick) { if (companionEditsSeen.Add(tile)) companionEdits.Add(tile); }
        else
        {
            worldEdits.Add((tile, true));
            worldEditsSeen.Add(tile);
            for (int dy = -ReframeRadius; dy <= ReframeRadius; dy++)
            for (int dx = -ReframeRadius; dx <= ReframeRadius; dx++)
            {
                var near = new Point(x + dx, y + dy);
                if (!tilesBeforeEdits.ContainsKey(near)) tilesBeforeEdits[near] = DescribeTile(near.X, near.Y);
            }
        }
    }

    /// <summary>The top of the companion's tick: write down the world it is about to observe.</summary>
    public static void BeforeTheCompanionTick(CompanionNPC companion)
    {
        insideCompanionTick = true;
        recordingThisTick = sessionOpen && GodsEyeEvents.Active;
        if (!recordingThisTick) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var profiled = BrainSections.Enter(ReplaySection);

        var line = new StringBuilder(512);
        line.Append("v=").Append(FormatVersion).Append(";tick=").Append(Main.GameUpdateCount.ToString(Invariant));
        NPC body = companion.NPC;
        line.Append(";body=").Append(F(body.position.X)).Append(',').Append(F(body.position.Y)).Append(',')
            .Append(F(body.velocity.X)).Append(',').Append(F(body.velocity.Y)).Append(',').Append(I(body.life))
            .Append(',').Append(B(companion.IsDowned));

        Player player = Main.LocalPlayer;
        line.Append(";player=");
        AppendDelta(line, player, PlayerFields, ref lastPlayer, out _);
        line.Append(";world=");
        AppendDelta(line, null, WorldFields, ref lastWorld, out _);
        string inventory = DescribeInventory(player);
        if (inventory != lastInventory) { line.Append(";inv=").Append(inventory); lastInventory = inventory; }
        string gear = DescribeGear(player);
        if (gear != lastGear) { line.Append(";gear=").Append(gear); lastGear = gear; }

        line.Append(";npc=");
        bool first = true;
        for (int slot = 0; slot < Main.maxNPCs; slot++)
        {
            NPC npc = Main.npc[slot];
            bool present = npc != null && npc.active && !ReferenceEquals(npc, body);
            AppendSlot(line, slot, present ? npc : null, NpcFields, lastNpc, ref first);
        }
        line.Append(";item=");
        first = true;
        for (int slot = 0; slot < Main.maxItems; slot++)
        {
            Item item = Main.item[slot];
            AppendSlot(line, slot, item != null && item.active ? item : null, ItemFields, lastItem, ref first);
        }

        line.Append(";edits=");
        int announced = worldEdits.Count;
        for (int index = 0; index < announced; index++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var neighbour = new Point(worldEdits[index].Tile.X + dx, worldEdits[index].Tile.Y + dy);
                if (worldEditsSeen.Add(neighbour)) worldEdits.Add((neighbour, false));
            }
        foreach ((Point near, string before) in tilesBeforeEdits)
            if (!worldEditsSeen.Contains(near) && DescribeTile(near.X, near.Y) != before)
            {
                worldEditsSeen.Add(near);
                worldEdits.Add((near, false));
            }
        tilesBeforeEdits.Clear();
        for (int index = 0; index < worldEdits.Count; index++)
        {
            if (index > 0) line.Append('|');
            (Point tile, bool wasAnnounced) = worldEdits[index];
            line.Append(I(tile.X)).Append(',').Append(I(tile.Y)).Append(',').Append(DescribeTile(tile.X, tile.Y))
                .Append(',').Append(wasAnnounced ? 'a' : 'n');
        }
        worldEdits.Clear();
        worldEditsSeen.Clear();

        line.Append(";light=").Append(ReadLightScannerSeed() is { } seed ? seed.ToString(Invariant) : "-");
        // Every sixtieth tick, and on any tick the world was edited, a digest of the tiles around the body, so a replay
        // can say its terrain is not the play's rather than leave that to be inferred from a decision that drifted.
        if (Main.GameUpdateCount % TerrainDigestEveryTicks == 0 || announced > 0)
            line.Append(";terrain=").Append(DescribeTerrainAround(body.Center));
        foreach (WatchedRandom watched in watchedRandoms)
        {
            watched.StartObject = watched.Current();
            watched.Readable = ReadRandom(watched.StartObject, watched.Start);
        }
        pendingInputs = line.ToString();
        timestampsSpent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        DecisionClock.StartTape();
    }

    /// <summary>The bottom of the companion's tick: what it drew, what it spent, what it edited, what it decided.</summary>
    public static void AfterTheCompanionTick(CompanionNPC companion)
    {
        insideCompanionTick = false;
        if (!recordingThisTick) return;
        recordingThisTick = false;
        string tape = DecisionClock.EndTape();
        if (!GodsEyeEvents.Active) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var profiled = BrainSections.Enter(ReplaySection);

        var line = new StringBuilder(pendingInputs, pendingInputs.Length + 512);
        line.Append(";clock=").Append(tape);

        // A random stream is written only on a tick that drew from it, and then as the whole state the tick started
        // from: every game system shares these streams, so a replay that does not run them cannot reach the state by
        // drawing — it has to be handed it.
        foreach (WatchedRandom watched in watchedRandoms)
        {
            UnifiedRandom? now = watched.Current();
            bool drew = !ReferenceEquals(now, watched.StartObject)
                || !watched.Readable || !ReadRandom(now, watched.End) || !SameState(watched.Start, watched.End);
            if (drew && watched.Readable)
                line.Append(';').Append(watched.Key).Append('=').Append(EncodeRandom(watched.Start))
                    .Append(';').Append(watched.Key).Append("-end=").Append(RandomFingerprint(watched.End));
            else if (drew) line.Append(';').Append(watched.Key).Append("=unreadable");
        }

        var brain = companion.Brain;
        if (brain.LastTick == Main.GameUpdateCount && brain.LastAllowance is { } allowance)
            line.Append(";ops=").Append(allowance.OperationsUsed.ToString(Invariant)).Append('.').Append(B(allowance.Cut))
                .Append('.').Append(allowance.FirstCutSubsystem.Length == 0 ? "-" : allowance.FirstCutSubsystem);
        else line.Append(";ops=-");

        line.Append(";cedits=");
        for (int index = 0; index < companionEdits.Count; index++)
        {
            if (index > 0) line.Append('|');
            Point tile = companionEdits[index];
            line.Append(I(tile.X)).Append(',').Append(I(tile.Y)).Append(',').Append(DescribeTile(tile.X, tile.Y));
        }
        companionEdits.Clear();
        companionEditsSeen.Clear();

        // Last, because the decision digest carries `|` and `,` of its own and a reader takes the rest of the line.
        line.Append(";decision=").Append(DescribeDecision(companion));
        string detail = line.ToString();
        LinesWritten++;
        CharactersWritten += detail.Length;
        GodsEyeEvents.RecordReplayInputs(companion.NPC, detail);
        timestampsSpent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    /// <summary>
    /// The decision a tick made, as one comparable string: the course's activity, reason and whether it settled; the
    /// bound step's opportunity, method and pose; the activity that held the body; the movement request; the controls
    /// the motor applied; and the velocity the body was handed. Identifiers minted from process-wide counters are
    /// left out on purpose, because two runs in one process do not share them and a replay compared on them would
    /// disagree on its first tick about nothing the brain decided.
    /// </summary>
    public static string DescribeDecision(CompanionNPC companion)
    {
        var brain = companion.Brain;
        var decided = brain.Course.Last;
        string step = decided.Binding is { } binding
            ? $"{binding.Opportunity.Domain}/{binding.Opportunity.Purpose}/{binding.Opportunity.Target}/{binding.Method}/{binding.Pose.X.ToString("R", Invariant)},{binding.Pose.Y.ToString("R", Invariant)}"
            : "-";
        var request = brain.LastRequest;
        string work = request.WorkTile is { } tile ? $"{tile.X},{tile.Y}" : "-";
        return $"{decided.Activity}|{decided.Reason}|{B(decided.Settled)}|{step}|{brain.LastAction?.Name ?? "-"}|{request.Kind}"
            + $"|{F(request.Anchor.X)},{F(request.Anchor.Y)}|{request.Target?.whoAmI ?? -1}|{work}"
            + $"|{BrainTelemetry.DescribeControls(companion.Motor.AppliedControls).Replace(';', ',')}"
            + $"|{F(companion.NPC.velocity.X)},{F(companion.NPC.velocity.Y)}|{companion.Combat.LastFireOutcome}";
    }

    // ---- the apply half: harness-only ----

    /// <summary>Harness-only: make an NPC slot hold exactly what was recorded, its own `whoAmI` included.</summary>
    public static void ApplyNpc(NPC npc, IReadOnlyDictionary<string, string> values)
    {
        foreach (var field in NpcFields)
            if (values.TryGetValue(field.Key, out string? value)) field.Write(npc, value);
        npc.active = true;
    }

    /// <summary>Harness-only: make an item slot hold exactly what was recorded, its own `whoAmI` included.</summary>
    public static void ApplyItem(Item item, IReadOnlyDictionary<string, string> values)
    {
        foreach (var field in ItemFields)
            if (values.TryGetValue(field.Key, out string? value)) field.Write(item, value);
        item.active = true;
    }

    /// <summary>Harness-only: the player as recorded, his inventory and the companion's gear with him.</summary>
    /// <summary>Harness-only: the time of day and the world's events as recorded.</summary>
    public static void ApplyWorld(IReadOnlyDictionary<string, string> values)
    {
        foreach (var field in WorldFields)
            if (values.TryGetValue(field.Key, out string? value)) field.Write(null, value);
    }

    public static void ApplyPlayer(Player player, IReadOnlyDictionary<string, string> values, string inventory, string gear)
    {
        foreach (var field in PlayerFields)
            if (values.TryGetValue(field.Key, out string? value)) field.Write(player, value);
        if (DescribeInventory(player) != inventory) ApplyInventory(player, inventory);
        if (DescribeGear(player) != gear) ApplyGear(player, gear);
    }

    /// <summary>Harness-only: a tile as recorded, in <see cref="DescribeTile"/>'s shape.</summary>
    public static void ApplyTile(int x, int y, string state)
    {
        string[] parts = state.Split('.');
        if (parts.Length != 10) throw new FormatException($"a recorded tile is ten dot-separated values; got {parts.Length} in \"{state}\" at {x},{y}");
        Tile tile = Main.tile[x, y];
        tile.TileType = (ushort)ParseInt(parts[0]);
        tile.HasTile = ParseBool(parts[1]);
        // One write of the block type rather than `Slope` then `IsHalfBlock`: each setter writes the whole block type, so
        // the second would erase the first on a sloped tile. Half blocks and slopes are exclusive in the game's own enum.
        var slope = (Terraria.ID.SlopeType)ParseInt(parts[2]);
        tile.BlockType = ParseBool(parts[3]) ? Terraria.ID.BlockType.HalfBlock
            : slope == Terraria.ID.SlopeType.Solid ? Terraria.ID.BlockType.Solid : (Terraria.ID.BlockType)((int)slope + 1);
        tile.IsActuated = ParseBool(parts[4]);
        tile.WallType = (ushort)ParseInt(parts[5]);
        tile.LiquidAmount = (byte)ParseInt(parts[6]);
        tile.LiquidType = ParseInt(parts[7]);
        tile.TileFrameX = (short)ParseInt(parts[8]);
        tile.TileFrameY = (short)ParseInt(parts[9]);
    }

    /// <summary>A tile's shape, material, wall, liquid and frame, ten dot-separated values.</summary>
    /// <summary>How often a tick carries a terrain digest when nothing was edited: once a second of play.</summary>
    public const int TerrainDigestEveryTicks = 60;

    /// <summary>The window a terrain digest covers, in tiles either side of the body: wider than the reach flood's usual
    /// working radius near the body and small enough to cost well under a millisecond once a second.</summary>
    public const int TerrainDigestHalfWidth = 40, TerrainDigestHalfHeight = 30;

    /// <summary>
    /// `left,top:hash` — an FNV hash over every tile in the window around <paramref name="centre"/>, of the same ten values
    /// <see cref="DescribeTile"/> writes. The window's corner is written with it, so a replay hashes exactly the tiles the
    /// play hashed whatever its own body is doing.
    /// </summary>
    public static string DescribeTerrainAround(Vector2 centre)
    {
        int left = (int)(centre.X / 16f) - TerrainDigestHalfWidth, top = (int)(centre.Y / 16f) - TerrainDigestHalfHeight;
        return $"{left},{top}:{HashTerrain(left, top)}";
    }

    /// <summary>The digest of the window whose top-left tile is (<paramref name="left"/>, <paramref name="top"/>).</summary>
    public static string HashTerrain(int left, int top)
    {
        uint hash = 2166136261;
        void Mix(int value) { unchecked { hash ^= (uint)value; hash *= 16777619; } }
        for (int y = top; y < top + TerrainDigestHalfHeight * 2; y++)
        for (int x = left; x < left + TerrainDigestHalfWidth * 2; x++)
        {
            if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) { Mix(-1); continue; }
            Tile tile = Main.tile[x, y];
            Mix(tile.TileType); Mix(tile.HasTile ? 1 : 0); Mix((int)tile.Slope); Mix(tile.IsHalfBlock ? 1 : 0);
            Mix(tile.IsActuated ? 1 : 0); Mix(tile.WallType); Mix(tile.LiquidAmount); Mix(tile.LiquidType);
            Mix(tile.TileFrameX); Mix(tile.TileFrameY);
        }
        return hash.ToString("x8", Invariant);
    }

    public static string DescribeTile(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) return "0.0.0.0.0.0.0.0.0.0";
        Tile tile = Main.tile[x, y];
        return $"{tile.TileType}.{B(tile.HasTile)}.{(int)tile.Slope}.{B(tile.IsHalfBlock)}.{B(tile.IsActuated)}.{tile.WallType}"
            + $".{tile.LiquidAmount}.{tile.LiquidType}.{tile.TileFrameX}.{tile.TileFrameY}";
    }

    /// <summary>Harness-only: hand <c>Main.rand</c> the state a recorded tick started from.</summary>
    /// <summary>Harness-only: hand the stream named <paramref name="key"/> (`rand` or `grand`) the state a recorded tick
    /// started from.</summary>
    public static void RestoreRandom(string key, string encoded)
    {
        byte[] bytes = Convert.FromBase64String(encoded);
        if (bytes.Length != 58 * 4) throw new FormatException($"a recorded random state is 58 integers; got {bytes.Length} bytes");
        var state = new int[58];
        Buffer.BlockCopy(bytes, 0, state, 0, bytes.Length);
        var random = new UnifiedRandom(0);
        if (!WriteRandom(random, state)) throw new MissingFieldException("UnifiedRandom's inext, inextp or SeedArray is gone; a recorded random state cannot be restored");
        switch (key)
        {
            case "rand": Main.rand = random; break;
            case "grand":
                // The getter rebuilds the stream from the world seed whenever the two seed fields disagree, so they are
                // made to agree before the restored stream is put in place.
                WorldGen._genRandSeed = WorldGen._lastSeed;
                WorldGen._genRand = random;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "the recorded random streams are `rand` and `grand`");
        }
    }

    /// <summary>The fingerprint of the named stream's current state, in the shape its `-end` field is written, so a
    /// replay can say whether its tick drew as many numbers as the play's.</summary>
    public static string CurrentRandomFingerprint(string key)
    {
        var state = new int[58];
        UnifiedRandom? random = key == "grand" ? WorldGen.genRand : Main.rand;
        return ReadRandom(random, state) ? RandomFingerprint(state) : "unreadable";
    }

    /// <summary>Harness-only: the light scanner's random stream set to a recorded seed, so the next scan lights the tiles
    /// the way the play's did. False when this game's lighting mode has no scanner to set.</summary>
    public static bool RestoreLightScannerSeed(ulong seed)
    {
        if (LightScanner() is not { } found) return false;
        found.RandomField.SetValue(found.Scanner, new FastRandom(seed));
        return true;
    }

    // ---- encoding ----

    private static void AppendDelta<T>(StringBuilder line, T entity, Field<T>[] fields, ref string[]? last, out bool wroteAny)
    {
        wroteAny = false;
        last ??= new string[fields.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            string value = fields[index].Read(entity);
            if (last[index] == value) continue;
            last[index] = value;
            if (wroteAny) line.Append(',');
            line.Append(fields[index].Key).Append('=').Append(value);
            wroteAny = true;
        }
    }

    private static void AppendSlot<T>(StringBuilder line, int slot, T? entity, Field<T>[] fields, Dictionary<int, string[]> last, ref bool first)
        where T : class
    {
        if (entity == null)
        {
            if (!last.Remove(slot)) return;
            if (!first) line.Append('|');
            line.Append(I(slot)).Append(":-");
            first = false;
            return;
        }
        last.TryGetValue(slot, out string[]? written);
        // A slot that was empty last tick starts from nothing, so every field is written for it.
        var entry = new StringBuilder();
        AppendDelta(entry, entity, fields, ref written, out bool changed);
        last[slot] = written!;
        if (!changed) return;
        if (!first) line.Append('|');
        line.Append(I(slot)).Append(':').Append(entry);
        first = false;
    }

    private static string DescribeBuffs(NPC npc)
    {
        var text = new StringBuilder();
        for (int index = 0; index < npc.buffType.Length; index++)
        {
            if (npc.buffType[index] <= 0 || npc.buffTime[index] <= 0) continue;
            if (text.Length > 0) text.Append('/');
            text.Append(npc.buffType[index]).Append(':').Append(npc.buffTime[index]);
        }
        return text.ToString();
    }

    private static void ApplyBuffs(NPC npc, string value)
    {
        Array.Clear(npc.buffType);
        Array.Clear(npc.buffTime);
        if (value.Length == 0) return;
        string[] entries = value.Split('/');
        for (int index = 0; index < entries.Length && index < npc.buffType.Length; index++)
        {
            string[] pair = entries[index].Split(':');
            npc.buffType[index] = ParseInt(pair[0]);
            npc.buffTime[index] = ParseInt(pair[1]);
        }
    }

    private static string DescribePlayerBuffs(Player player)
    {
        var text = new StringBuilder();
        for (int index = 0; index < player.buffType.Length; index++)
        {
            if (player.buffType[index] <= 0 || player.buffTime[index] <= 0) continue;
            if (text.Length > 0) text.Append('/');
            text.Append(player.buffType[index]).Append(':').Append(player.buffTime[index]);
        }
        return text.ToString();
    }

    private static void ApplyPlayerBuffs(Player player, string value)
    {
        Array.Clear(player.buffType);
        Array.Clear(player.buffTime);
        if (value.Length == 0) return;
        string[] entries = value.Split('/');
        for (int index = 0; index < entries.Length && index < player.buffType.Length; index++)
        {
            string[] pair = entries[index].Split(':');
            player.buffType[index] = ParseInt(pair[0]);
            player.buffTime[index] = ParseInt(pair[1]);
        }
    }

    private static string DescribeControls(Player player)
        => $"{B(player.controlLeft)}{B(player.controlRight)}{B(player.controlUp)}{B(player.controlDown)}{B(player.controlJump)}{B(player.controlUseItem)}{B(player.controlUseTile)}";

    private static void ApplyControls(Player player, string value)
    {
        if (value.Length != 7) throw new FormatException($"recorded player controls are seven flags; got \"{value}\"");
        player.controlLeft = value[0] == '1';
        player.controlRight = value[1] == '1';
        player.controlUp = value[2] == '1';
        player.controlDown = value[3] == '1';
        player.controlJump = value[4] == '1';
        player.controlUseItem = value[5] == '1';
        player.controlUseTile = value[6] == '1';
    }

    private static void ApplyMount(Player player, string value)
    {
        int type = ParseInt(value);
        int current = player.mount.Active ? player.mount.Type : -1;
        if (type == current) return;
        if (type < 0) player.mount.Dismount(player);
        else player.mount.SetMount(type, player);
    }

    /// <summary>Every occupied inventory slot as `slot:type:stack`, joined by `/`.</summary>
    private static string DescribeInventory(Player player)
    {
        var text = new StringBuilder();
        for (int slot = 0; slot < player.inventory.Length; slot++)
        {
            Item item = player.inventory[slot];
            if (item == null || item.IsAir) continue;
            if (text.Length > 0) text.Append('/');
            text.Append(slot).Append(':').Append(item.type).Append(':').Append(item.stack);
        }
        return text.ToString();
    }

    private static void ApplyInventory(Player player, string value)
    {
        var wanted = new Dictionary<int, (int Type, int Stack)>();
        if (value.Length > 0)
            foreach (string entry in value.Split('/'))
            {
                string[] parts = entry.Split(':');
                wanted[ParseInt(parts[0])] = (ParseInt(parts[1]), ParseInt(parts[2]));
            }
        for (int slot = 0; slot < player.inventory.Length; slot++)
        {
            player.inventory[slot] ??= new Item();
            Item item = player.inventory[slot];
            if (wanted.TryGetValue(slot, out var recorded))
            {
                if (item.type != recorded.Type) item.SetDefaults(recorded.Type);
                item.stack = recorded.Stack;
            }
            else if (!item.IsAir) item.TurnToAir();
        }
    }

    /// <summary>The companion's four gear slots as `type:prefix:stack`, joined by `/`.</summary>
    private static string DescribeGear(Player player)
    {
        var slots = player.GetModPlayer<CompanionPlayer>().Gear.Slots;
        var text = new StringBuilder();
        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slot > 0) text.Append('/');
            Item? item = slots[slot];
            text.Append(item?.type ?? 0).Append(':').Append(item?.prefix ?? 0).Append(':').Append(item?.stack ?? 0);
        }
        return text.ToString();
    }

    private static void ApplyGear(Player player, string value)
    {
        string[] entries = value.Split('/');
        player.GetModPlayer<CompanionPlayer>().Gear.EditSlots(slots =>
        {
            for (int slot = 0; slot < slots.Length && slot < entries.Length; slot++)
            {
                string[] parts = entries[slot].Split(':');
                var item = new Item();
                item.SetDefaults(ParseInt(parts[0]));
                if (!item.IsAir)
                {
                    item.Prefix(ParseInt(parts[1]));
                    item.stack = ParseInt(parts[2]);
                }
                slots[slot] = item;
            }
        });
    }

    // ---- the random streams ----

    private static readonly FieldInfo? RandomNext = typeof(UnifiedRandom).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? RandomNextP = typeof(UnifiedRandom).GetField("inextp", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? RandomSeeds = typeof(UnifiedRandom).GetField("SeedArray", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool ReadRandom(UnifiedRandom? random, int[] into)
    {
        if (random == null || RandomNext == null || RandomNextP == null || RandomSeeds?.GetValue(random) is not int[] seeds || seeds.Length != 56)
            return false;
        into[0] = (int)RandomNext.GetValue(random)!;
        into[1] = (int)RandomNextP.GetValue(random)!;
        Array.Copy(seeds, 0, into, 2, 56);
        return true;
    }

    private static bool WriteRandom(UnifiedRandom random, int[] state)
    {
        if (RandomNext == null || RandomNextP == null || RandomSeeds == null) return false;
        RandomNext.SetValue(random, state[0]);
        RandomNextP.SetValue(random, state[1]);
        RandomSeeds.SetValue(random, state[2..]);
        return true;
    }

    private static bool SameState(int[] a, int[] b)
    {
        for (int index = 0; index < a.Length; index++) if (a[index] != b[index]) return false;
        return true;
    }

    private static string EncodeRandom(int[] state)
    {
        var bytes = new byte[state.Length * 4];
        Buffer.BlockCopy(state, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static string RandomFingerprint(int[] state)
    {
        uint hash = 2166136261;
        foreach (int value in state) { hash ^= (uint)value; hash *= 16777619; }
        return $"{state[0]}.{state[1]}.{hash:x8}";
    }

    private sealed record ScannerAccess(object Scanner, FieldInfo RandomField);

    private static ScannerAccess? LightScanner()
    {
        object? engine = typeof(Lighting).GetField("NewEngine", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
        object? scanner = engine?.GetType().GetField("_tileScanner", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(engine);
        FieldInfo? random = scanner?.GetType().GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic);
        return scanner == null || random == null ? null : new ScannerAccess(scanner, random);
    }

    private static ulong? ReadLightScannerSeed()
        => LightScanner() is { } found && found.RandomField.GetValue(found.Scanner) is FastRandom random ? random.Seed : null;
}
