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
    public const int FormatVersion = 2;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Writes a field's raw value into <paramref name="into"/> (exactly <see cref="Field{T}.RawWidth"/> values) and returns
    /// true, or returns false when this subject's value cannot be carried exactly in that width, which sends the field to the
    /// formatting path. The raw value is what makes an unchanged field free: it is compared against the last tick's without
    /// formatting anything, so it must determine the text exactly — equal raw values always format to equal text.
    /// </summary>
    public delegate bool RawReader<T>(T subject, Span<long> into);

    /// <summary>
    /// One recorded field: its key, its text as written, how a replay puts it back, and its raw value for the per-tick
    /// comparison. A width of zero has no raw value and is formatted every tick, which is only right for a field on a single
    /// subject (the player's buffs), never for one repeated over the NPC or item table.
    /// </summary>
    public sealed record Field<T>(string Key, Func<T, string> Read, Action<T, string> Write, int RawWidth = 0, RawReader<T>? Raw = null);

    private static Field<T> FloatField<T>(string key, Func<T, float> get, Action<T, float> set)
        => new(key, subject => F(get(subject)), (subject, text) => set(subject, ParseFloat(text)), 1,
            (subject, into) => { into[0] = BitConverter.SingleToInt32Bits(get(subject)); return true; });

    private static Field<T> IntField<T>(string key, Func<T, int> get, Action<T, int> set)
        => new(key, subject => I(get(subject)), (subject, text) => set(subject, ParseInt(text)), 1,
            (subject, into) => { into[0] = get(subject); return true; });

    private static Field<T> BoolField<T>(string key, Func<T, bool> get, Action<T, bool> set)
        => new(key, subject => B(get(subject)), (subject, text) => set(subject, ParseBool(text)), 1,
            (subject, into) => { into[0] = get(subject) ? 1 : 0; return true; });

    // Shortest round-trip text, so a replayed float is the recorded float to the bit.
    public static string F(float value) => value.ToString("R", Invariant);
    public static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, Invariant);
    public static string I(int value) => value.ToString(Invariant);
    public static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, Invariant);
    private static string B(bool value) => value ? "1" : "0";
    private static bool ParseBool(string text) => text == "1";

    /// <summary>The game's NPC buff slot count (<c>NPC.maxBuffs</c>, 20 in tModLoader 1.4.4), the raw width of an NPC's buffs.
    /// Declared above the tables because a static initialiser runs in the order it is written, and the table reads it.</summary>
    private static readonly int NpcBuffWidth = NPC.maxBuffs;

    /// <summary>
    /// What the brain reads off a hostile, a critter or any other NPC it can see. `t` is first and is applied
    /// first, because setting a type resets every other field.
    /// </summary>
    public static readonly Field<NPC>[] NpcFields =
    {
        IntField<NPC>("t", n => n.type, (n, type) => { if (!n.active || n.type != type) n.SetDefaults(type); }),
        // The entity's own idea of its slot, recorded rather than assumed: the census names a target by it, and an entity
        // the engine spawned through a path that never set it carries whatever the slot's last occupant left.
        IntField<NPC>("wh", n => n.whoAmI, (n, v) => n.whoAmI = v),
        // The slot's spawn generation and the last shot a hostile fired, which the game's own spawn hooks feed between
        // companion ticks (`HostileAttackSources`) and combat keys its targets and prices recent damage on. A replay that
        // does not run those hooks is handed both; a new generation also forgets the slot's observed motion, as the
        // spawn hook does, so a reused slot is not predicted from its last occupant's path.
        IntField<NPC>("gen", Observation.HostileAttackSources.Generation, (n, generation) =>
        {
            if (Observation.HostileAttackSources.Generation(n) == generation) return;
            Observation.HostileAttackSources.Spawn(n);
            Observation.HostileAttackSources.AssumeGeneration(n, generation);
        }),
        new("sh", n => Observation.HostileAttackSources.ExportShot(n) is { } shot ? $"{shot.Tick}.{shot.Damage}" : "-", (n, v) =>
        {
            if (v == "-") { Observation.HostileAttackSources.AssumeShot(n, -1, 0, 0); return; }
            string[] parts = v.Split('.');
            Observation.HostileAttackSources.AssumeShot(n, Observation.HostileAttackSources.Generation(n),
                uint.Parse(parts[0], Invariant), ParseInt(parts[1]));
        }, 2, (n, into) =>
        {
            var shot = Observation.HostileAttackSources.ExportShot(n);
            into[0] = shot.HasValue ? 1 : 0;
            into[1] = shot is { } value ? ((long)value.Tick << 32) | (uint)value.Damage : 0;
            return true;
        }),
        FloatField<NPC>("x", n => n.position.X, (n, v) => n.position.X = v),
        FloatField<NPC>("y", n => n.position.Y, (n, v) => n.position.Y = v),
        FloatField<NPC>("vx", n => n.velocity.X, (n, v) => n.velocity.X = v),
        FloatField<NPC>("vy", n => n.velocity.Y, (n, v) => n.velocity.Y = v),
        FloatField<NPC>("ox", n => n.oldPosition.X, (n, v) => n.oldPosition.X = v),
        FloatField<NPC>("oy", n => n.oldPosition.Y, (n, v) => n.oldPosition.Y = v),
        IntField<NPC>("l", n => n.life, (n, v) => n.life = v),
        IntField<NPC>("lm", n => n.lifeMax, (n, v) => n.lifeMax = v),
        IntField<NPC>("d", n => n.direction, (n, v) => n.direction = v),
        IntField<NPC>("dy", n => n.directionY, (n, v) => n.directionY = v),
        IntField<NPC>("sd", n => n.spriteDirection, (n, v) => n.spriteDirection = v),
        FloatField<NPC>("a0", n => n.ai[0], (n, v) => n.ai[0] = v),
        FloatField<NPC>("a1", n => n.ai[1], (n, v) => n.ai[1] = v),
        FloatField<NPC>("a2", n => n.ai[2], (n, v) => n.ai[2] = v),
        FloatField<NPC>("a3", n => n.ai[3], (n, v) => n.ai[3] = v),
        FloatField<NPC>("l0", n => n.localAI[0], (n, v) => n.localAI[0] = v),
        FloatField<NPC>("l1", n => n.localAI[1], (n, v) => n.localAI[1] = v),
        FloatField<NPC>("l2", n => n.localAI[2], (n, v) => n.localAI[2] = v),
        FloatField<NPC>("l3", n => n.localAI[3], (n, v) => n.localAI[3] = v),
        IntField<NPC>("tg", n => n.target, (n, v) => n.target = v),
        BoolField<NPC>("ng", n => n.noGravity, (n, v) => n.noGravity = v),
        BoolField<NPC>("nt", n => n.noTileCollide, (n, v) => n.noTileCollide = v),
        BoolField<NPC>("cx", n => n.collideX, (n, v) => n.collideX = v),
        BoolField<NPC>("cy", n => n.collideY, (n, v) => n.collideY = v),
        BoolField<NPC>("w", n => n.wet, (n, v) => n.wet = v),
        BoolField<NPC>("lw", n => n.lavaWet, (n, v) => n.lavaWet = v),
        BoolField<NPC>("hw", n => n.honeyWet, (n, v) => n.honeyWet = v),
        IntField<NPC>("dm", n => n.damage, (n, v) => n.damage = v),
        IntField<NPC>("df", n => n.defense, (n, v) => n.defense = v),
        FloatField<NPC>("kb", n => n.knockBackResist, (n, v) => n.knockBackResist = v),
        BoolField<NPC>("fr", n => n.friendly, (n, v) => n.friendly = v),
        BoolField<NPC>("dd", n => n.dontTakeDamage, (n, v) => n.dontTakeDamage = v),
        IntField<NPC>("rl", n => n.realLife, (n, v) => n.realLife = v),
        IntField<NPC>("wd", n => n.width, (n, v) => n.width = v),
        IntField<NPC>("ht", n => n.height, (n, v) => n.height = v),
        FloatField<NPC>("sc", n => n.scale, (n, v) => n.scale = v),
        IntField<NPC>("fy", n => n.frame.Y, (n, v) => n.frame.Y = v),
        IntField<NPC>("tl", n => n.timeLeft, (n, v) => n.timeLeft = v),
        BoolField<NPC>("jh", n => n.justHit, (n, v) => n.justHit = v),
        IntField<NPC>("im", n => n.immune[Main.myPlayer], (n, v) => n.immune[Main.myPlayer] = v),
        // Every buff slot's type and time, one value each, so a buff ticking down is seen without formatting the list; an NPC
        // whose buff array is not the game's usual length is formatted instead, because the width cannot carry it exactly.
        new("bf", DescribeBuffs, ApplyBuffs, NpcBuffWidth, (n, into) =>
        {
            if (n.buffType.Length != NpcBuffWidth || n.buffTime.Length != NpcBuffWidth) return false;
            for (int index = 0; index < NpcBuffWidth; index++) into[index] = ((long)n.buffType[index] << 32) | (uint)n.buffTime[index];
            return true;
        }),
    };

    /// <summary>What the brain reads off a dropped item: where it lies, what it is, and whether it can be taken.</summary>
    public static readonly Field<Item>[] ItemFields =
    {
        IntField<Item>("t", i => i.type, (i, type) => { if (!i.active || i.type != type) i.SetDefaults(type); }),
        // A drop's census target is `item:{whoAmI}`, and a drop the engine spawned outside the ordinary path can carry a
        // `whoAmI` that is not its slot: measured 24 September 2026, the course bound a mined dirt drop lying in slot 5 as `item:0`.
        IntField<Item>("wh", i => i.whoAmI, (i, v) => i.whoAmI = v),
        IntField<Item>("st", i => i.stack, (i, v) => i.stack = v),
        FloatField<Item>("x", i => i.position.X, (i, v) => i.position.X = v),
        FloatField<Item>("y", i => i.position.Y, (i, v) => i.position.Y = v),
        FloatField<Item>("vx", i => i.velocity.X, (i, v) => i.velocity.X = v),
        FloatField<Item>("vy", i => i.velocity.Y, (i, v) => i.velocity.Y = v),
        IntField<Item>("gd", i => i.noGrabDelay, (i, v) => i.noGrabDelay = v),
        IntField<Item>("kt", i => i.keepTime, (i, v) => i.keepTime = v),
        BoolField<Item>("bg", i => i.beingGrabbed, (i, v) => i.beingGrabbed = v),
        IntField<Item>("rs", i => i.playerIndexTheItemIsReservedFor, (i, v) => i.playerIndexTheItemIsReservedFor = v),
        IntField<Item>("ts", i => i.timeSinceItemSpawned, (i, v) => i.timeSinceItemSpawned = v),
        BoolField<Item>("w", i => i.wet, (i, v) => i.wet = v),
    };

    /// <summary>
    /// What the hit prediction reads off a hostile projectile (<c>ObserveProjectiles</c>, which the evade layer and the
    /// navigator's unsafe ticks read inside the companion's tick): its box, its velocity with its extra updates, and its
    /// damage. Only a projectile that sense would consider — hostile, with damage — is written, and a replay places it
    /// with exactly these fields rather than through <c>SetDefaults</c>, so a modded projectile needs no content loaded.
    /// </summary>
    public static readonly Field<Projectile>[] ProjectileFields =
    {
        IntField<Projectile>("t", p => p.type, (p, v) => p.type = v),
        FloatField<Projectile>("x", p => p.position.X, (p, v) => p.position.X = v),
        FloatField<Projectile>("y", p => p.position.Y, (p, v) => p.position.Y = v),
        IntField<Projectile>("wd", p => p.width, (p, v) => p.width = v),
        IntField<Projectile>("ht", p => p.height, (p, v) => p.height = v),
        FloatField<Projectile>("vx", p => p.velocity.X, (p, v) => p.velocity.X = v),
        FloatField<Projectile>("vy", p => p.velocity.Y, (p, v) => p.velocity.Y = v),
        IntField<Projectile>("eu", p => p.extraUpdates, (p, v) => p.extraUpdates = v),
        IntField<Projectile>("dm", p => p.damage, (p, v) => p.damage = v),
    };

    /// <summary>Whether a projectile is one the hit prediction reads, and so one the record carries.</summary>
    public static bool IsRecordedProjectile(Projectile? projectile) => projectile != null && projectile.active && projectile.hostile && projectile.damage > 0;

    /// <summary>
    /// What the brain reads off the player. His inventory and the companion's gear are their own fields on the line
    /// rather than rows here, because each is a list that changes rarely and is written whole when it does.
    /// </summary>
    public static readonly Field<Player>[] PlayerFields =
    {
        FloatField<Player>("x", p => p.position.X, (p, v) => p.position.X = v),
        FloatField<Player>("y", p => p.position.Y, (p, v) => p.position.Y = v),
        FloatField<Player>("vx", p => p.velocity.X, (p, v) => p.velocity.X = v),
        FloatField<Player>("vy", p => p.velocity.Y, (p, v) => p.velocity.Y = v),
        IntField<Player>("l", p => p.statLife, (p, v) => p.statLife = v),
        IntField<Player>("lm", p => p.statLifeMax2, (p, v) => p.statLifeMax2 = v),
        IntField<Player>("lmb", p => p.statLifeMax, (p, v) => p.statLifeMax = v),
        IntField<Player>("mn", p => p.statMana, (p, v) => p.statMana = v),
        IntField<Player>("mm", p => p.statManaMax2, (p, v) => p.statManaMax2 = v),
        BoolField<Player>("dead", p => p.dead, (p, v) => p.dead = v),
        IntField<Player>("sel", p => p.selectedItem, (p, v) => p.selectedItem = v),
        IntField<Player>("anim", p => p.itemAnimation, (p, v) => p.itemAnimation = v),
        IntField<Player>("it", p => p.itemTime, (p, v) => p.itemTime = v),
        IntField<Player>("dir", p => p.direction, (p, v) => p.direction = v),
        FloatField<Player>("gv", p => p.gravDir, (p, v) => p.gravDir = v),
        BoolField<Player>("im", p => p.immune, (p, v) => p.immune = v),
        IntField<Player>("imt", p => p.immuneTime, (p, v) => p.immuneTime = v),
        BoolField<Player>("li", p => p.longInvince, (p, v) => p.longInvince = v),
        FloatField<Player>("end", p => p.endurance, (p, v) => p.endurance = v),
        FloatField<Player>("ra", p => p.runAcceleration, (p, v) => p.runAcceleration = v),
        BoolField<Player>("w", p => p.wet, (p, v) => p.wet = v),
        BoolField<Player>("lw", p => p.lavaWet, (p, v) => p.lavaWet = v),
        BoolField<Player>("hw", p => p.honeyWet, (p, v) => p.honeyWet = v),
        BoolField<Player>("sw", p => p.shimmerWet, (p, v) => p.shimmerWet = v),
        new("mt", p => I(p.mount.Active ? p.mount.Type : -1), ApplyMount, 1, (p, into) => { into[0] = p.mount.Active ? p.mount.Type : -1; return true; }),
        new("ctl", DescribeControls, ApplyControls, 1, (p, into) =>
        {
            into[0] = (p.controlLeft ? 1 : 0) | (p.controlRight ? 2 : 0) | (p.controlUp ? 4 : 0) | (p.controlDown ? 8 : 0)
                | (p.controlJump ? 16 : 0) | (p.controlUseItem ? 32 : 0) | (p.controlUseTile ? 64 : 0);
            return true;
        }),
        IntField<Player>("ttx", _ => Player.tileTargetX, (_, v) => Player.tileTargetX = v),
        IntField<Player>("tty", _ => Player.tileTargetY, (_, v) => Player.tileTargetY = v),
        // Formatted every tick rather than compared raw: the player's buff count grows with every mod that adds buffs, and one
        // subject's list is cheap where the NPC table's would not be.
        new("bf", DescribePlayerBuffs, ApplyPlayerBuffs),
    };

    /// <summary>
    /// What the brain reads off the world rather than off an entity: the time of day, which the light engine turns into
    /// sky light and the light sense reads, and the events the encounter context names (a blood moon, an eclipse, an
    /// invasion on its way). The subject is ignored; the fields are the game's statics.
    /// </summary>
    public static readonly Field<object?>[] WorldFields =
    {
        BoolField<object?>("day", _ => Main.dayTime, (_, v) => Main.dayTime = v),
        new("time", _ => Main.time.ToString("R", Invariant), (_, v) => Main.time = double.Parse(v, NumberStyles.Float, Invariant), 1,
            (_, into) => { into[0] = BitConverter.DoubleToInt64Bits(Main.time); return true; }),
        BoolField<object?>("blood", _ => Main.bloodMoon, (_, v) => Main.bloodMoon = v),
        BoolField<object?>("eclipse", _ => Main.eclipse, (_, v) => Main.eclipse = v),
        IntField<object?>("inv", _ => Main.invasionType, (_, v) => Main.invasionType = v),
        IntField<object?>("invd", _ => Main.invasionDelay, (_, v) => Main.invasionDelay = v),
        IntField<object?>("invs", _ => Main.invasionSize, (_, v) => Main.invasionSize = v),
        // The rest of what `ObserveEncounterContext` reads (the pumpkin and frost moons, slime rain, where an invasion
        // stands) and the screen size `ObserveLight` sizes its window from, which in play is the player's own screen.
        BoolField<object?>("pm", _ => Main.pumpkinMoon, (_, v) => Main.pumpkinMoon = v),
        BoolField<object?>("fm", _ => Main.snowMoon, (_, v) => Main.snowMoon = v),
        BoolField<object?>("sr", _ => Main.slimeRain, (_, v) => Main.slimeRain = v),
        new("ivx", _ => Main.invasionX.ToString("R", Invariant), (_, v) => Main.invasionX = double.Parse(v, NumberStyles.Float, Invariant), 1,
            (_, into) => { into[0] = BitConverter.DoubleToInt64Bits(Main.invasionX); return true; }),
        IntField<object?>("scw", _ => Main.screenWidth, (_, v) => Main.screenWidth = v),
        IntField<object?>("sch", _ => Main.screenHeight, (_, v) => Main.screenHeight = v),
    };

    // ---- session state: what was last written per slot, so each line carries only what moved ----

    private static bool sessionOpen;
    private static bool recordingThisTick;
    private static bool insideCompanionTick;
    /// <summary>
    /// What was last written for one subject: each field's raw value and its text. A field whose raw value is unchanged is
    /// skipped without being formatted, which is what makes a still world nearly free to record — measured 24 September 2026
    /// on a full NPC and item table, formatting every field of every entity every tick cost 2.0 ms and 237 KB a tick with
    /// nothing moving. A field whose raw value moved is formatted and written only if its text moved too, so the line is the
    /// same one a text-only comparison would write.
    /// </summary>
    private sealed class DeltaState
    {
        public readonly long[] Raw;
        public readonly bool[] RawKnown;
        public readonly string?[] Text;
        public bool Present;
        /// <summary>Every field's raw value was read and stored on the last line, so an identical whole raw span means nothing moved.</summary>
        public bool AllRawKnown;
        public DeltaState(int rawWidth, int fields) { Raw = new long[rawWidth]; RawKnown = new bool[fields]; Text = new string?[fields]; }

        /// <summary>Forget everything written, so the next line writes every field of this subject.</summary>
        public void Forget() { Array.Clear(RawKnown); Array.Clear(Text); AllRawKnown = false; }
    }

    /// <summary>
    /// A field table with each field's offset into the raw array laid out once, and optionally one method that reads every
    /// field's raw value in that layout in a single call. The NPC and item tables have one, because they are walked for up
    /// to six hundred subjects a tick and a delegate call per field was most of the recorder's cost: measured 24 September
    /// 2026 on a full table, 1.6 ms of a 2.3 ms tick went to 199 NPCs' per-field raw reads. The per-field readers stay the
    /// definition; <see cref="WholeReadersAgree"/> is the standing check that the two say the same thing.
    /// </summary>
    private sealed class Table<T>
    {
        public readonly Field<T>[] Fields;
        public readonly int[] Offsets;
        public readonly int RawWidth;
        public readonly Func<T, long[], bool>? Whole;
        public readonly bool EveryFieldHasRaw;
        /// <summary>The raw values being read for one subject, reused for every subject: the recorder runs on the game thread only.</summary>
        public readonly long[] Scratch;
        public readonly bool[] ReadScratch;
        public Table(Field<T>[] fields, Func<T, long[], bool>? whole = null)
        {
            Whole = whole;
            EveryFieldHasRaw = Array.TrueForAll(fields, field => field.RawWidth > 0);
            Fields = fields;
            Offsets = new int[fields.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                Offsets[index] = RawWidth;
                RawWidth += fields[index].RawWidth;
            }
            Scratch = new long[RawWidth];
            ReadScratch = new bool[fields.Length];
        }
        public DeltaState NewState() => new(RawWidth, Fields.Length);
    }

    private static readonly Table<NPC> NpcTable = new(NpcFields, ReadWholeNpc);
    private static readonly Table<Item> ItemTable = new(ItemFields, ReadWholeItem);

    /// <summary>Every <see cref="NpcFields"/> raw value in table order, in one call. False for an NPC whose buff arrays are
    /// not the game's usual length, which the per-field path formats instead.</summary>
    private static bool ReadWholeNpc(NPC n, long[] into)
    {
        if (n.buffType.Length != NpcBuffWidth || n.buffTime.Length != NpcBuffWidth || into.Length != 45 + NpcBuffWidth) return false;
        into[0] = n.type;
        into[1] = n.whoAmI;
        into[2] = Observation.HostileAttackSources.Generation(n);
        var shot = Observation.HostileAttackSources.ExportShot(n);
        into[3] = shot.HasValue ? 1 : 0;
        into[4] = shot is { } value ? ((long)value.Tick << 32) | (uint)value.Damage : 0;
        into[5] = BitConverter.SingleToInt32Bits(n.position.X);
        into[6] = BitConverter.SingleToInt32Bits(n.position.Y);
        into[7] = BitConverter.SingleToInt32Bits(n.velocity.X);
        into[8] = BitConverter.SingleToInt32Bits(n.velocity.Y);
        into[9] = BitConverter.SingleToInt32Bits(n.oldPosition.X);
        into[10] = BitConverter.SingleToInt32Bits(n.oldPosition.Y);
        into[11] = n.life;
        into[12] = n.lifeMax;
        into[13] = n.direction;
        into[14] = n.directionY;
        into[15] = n.spriteDirection;
        into[16] = BitConverter.SingleToInt32Bits(n.ai[0]);
        into[17] = BitConverter.SingleToInt32Bits(n.ai[1]);
        into[18] = BitConverter.SingleToInt32Bits(n.ai[2]);
        into[19] = BitConverter.SingleToInt32Bits(n.ai[3]);
        into[20] = BitConverter.SingleToInt32Bits(n.localAI[0]);
        into[21] = BitConverter.SingleToInt32Bits(n.localAI[1]);
        into[22] = BitConverter.SingleToInt32Bits(n.localAI[2]);
        into[23] = BitConverter.SingleToInt32Bits(n.localAI[3]);
        into[24] = n.target;
        into[25] = n.noGravity ? 1 : 0;
        into[26] = n.noTileCollide ? 1 : 0;
        into[27] = n.collideX ? 1 : 0;
        into[28] = n.collideY ? 1 : 0;
        into[29] = n.wet ? 1 : 0;
        into[30] = n.lavaWet ? 1 : 0;
        into[31] = n.honeyWet ? 1 : 0;
        into[32] = n.damage;
        into[33] = n.defense;
        into[34] = BitConverter.SingleToInt32Bits(n.knockBackResist);
        into[35] = n.friendly ? 1 : 0;
        into[36] = n.dontTakeDamage ? 1 : 0;
        into[37] = n.realLife;
        into[38] = n.width;
        into[39] = n.height;
        into[40] = BitConverter.SingleToInt32Bits(n.scale);
        into[41] = n.frame.Y;
        into[42] = n.timeLeft;
        into[43] = n.justHit ? 1 : 0;
        into[44] = n.immune[Main.myPlayer];
        int[] types = n.buffType, times = n.buffTime;
        for (int index = 0; index < NpcBuffWidth; index++) into[45 + index] = ((long)types[index] << 32) | (uint)times[index];
        return true;
    }

    /// <summary>Every <see cref="ItemFields"/> raw value in table order, in one call.</summary>
    private static bool ReadWholeItem(Item i, long[] into)
    {
        into[0] = i.type;
        into[1] = i.whoAmI;
        into[2] = i.stack;
        into[3] = BitConverter.SingleToInt32Bits(i.position.X);
        into[4] = BitConverter.SingleToInt32Bits(i.position.Y);
        into[5] = BitConverter.SingleToInt32Bits(i.velocity.X);
        into[6] = BitConverter.SingleToInt32Bits(i.velocity.Y);
        into[7] = i.noGrabDelay;
        into[8] = i.keepTime;
        into[9] = i.beingGrabbed ? 1 : 0;
        into[10] = i.playerIndexTheItemIsReservedFor;
        into[11] = i.timeSinceItemSpawned;
        into[12] = i.wet ? 1 : 0;
        return true;
    }

    /// <summary>
    /// Harness-only: whether the one-call readers of the NPC and item tables write exactly what the per-field readers write,
    /// for every live NPC and item in the world, and the first disagreement when they do not. The one-call readers exist only
    /// for speed, so a field added to a table and not to its reader — or read differently there — is the drift this catches,
    /// and a recorder that drifted would compare stale raw values and silently skip a change.
    /// </summary>
    public static bool WholeReadersAgree(out string disagreement)
    {
        foreach (NPC npc in Main.npc)
            if (npc != null && npc.active && !Agree(npc, NpcTable, out disagreement)) { disagreement = $"NPC slot {npc.whoAmI}: {disagreement}"; return false; }
        foreach (Item item in Main.item)
            if (item != null && item.active && !Agree(item, ItemTable, out disagreement)) { disagreement = $"item slot {item.whoAmI}: {disagreement}"; return false; }
        disagreement = "";
        return true;
    }

    private static bool Agree<T>(T entity, Table<T> table, out string disagreement)
    {
        var whole = new long[table.RawWidth];
        var single = new long[table.RawWidth];
        if (table.Whole == null || !table.Whole(entity, whole)) { disagreement = "the one-call reader declined"; return false; }
        for (int index = 0; index < table.Fields.Length; index++)
        {
            Field<T> field = table.Fields[index];
            if (field.RawWidth == 0 || !field.Raw!(entity, single.AsSpan(table.Offsets[index], field.RawWidth))) continue;
            for (int offset = table.Offsets[index]; offset < table.Offsets[index] + field.RawWidth; offset++)
                if (whole[offset] != single[offset])
                {
                    disagreement = $"field `{field.Key}` reads {single[offset]} through its table entry and {whole[offset]} through the one-call reader";
                    return false;
                }
        }
        disagreement = "";
        return true;
    }
    private static readonly Table<Player> PlayerTable = new(PlayerFields);
    private static readonly Table<object?> WorldTable = new(WorldFields);
    private static readonly DeltaState?[] npcStates = new DeltaState?[Main.maxNPCs];
    private static readonly DeltaState?[] itemStates = new DeltaState?[Main.maxItems];
    private static readonly DeltaState playerState = PlayerTable.NewState();
    private static readonly DeltaState worldState = WorldTable.NewState();
    private static string lastInventory = "", lastGear = "";
    private static long[] lastInventoryRaw = Array.Empty<long>(), lastGearRaw = Array.Empty<long>();
    /// <summary>The line being built: the inputs half at the top of the tick, finished at the bottom, one buffer reused
    /// for the session so a tick allocates only the string it hands the writer.</summary>
    private static readonly StringBuilder line = new(4096);
    private static readonly Table<Projectile> ProjectileTable = new(ProjectileFields);
    private static readonly DeltaState?[] projectileStates = new DeltaState?[Main.maxProjectiles];
    private static string lastBag = "";
    private static long[] lastBagRaw = Array.Empty<long>();
    /// <summary>The line's ordinal this session, written as `n` on every line whether or not the queue takes it, so a lost
    /// line leaves a gap a reader can see.</summary>
    private static long lineOrdinal;
    /// <summary>The next line writes everything, as at a session's start: set when a session opens and when the queue
    /// refused a line, because every later delta was taken against what that lost line would have said.</summary>
    private static bool nextIsKeyframe;
    /// <summary>
    /// The first tick a keyframe may be attempted after a refused line. A keyframe is the whole scene, and the queue refused
    /// the last line because it was full; retrying every tick would rebuild the whole scene on every frame of exactly the
    /// pressure that caused the refusal. So after a refusal the recorder writes nothing for a second — each skipped tick
    /// still moves the ordinal, so the gap a reader sees covers them — and then tries one keyframe.
    /// </summary>
    private static ulong keyframeNotBefore;
    private const int TicksBetweenKeyframeAttempts = 60;
    private static int lastSelf = -1, lastKnowledgeRevision = int.MinValue;
    /// <summary>
    /// The tiles the world announced as edited since the last companion tick, each once, in the order of its first
    /// announcement, with how many times it was announced: each announcement moves the edit log's revision, so a replay
    /// announces a tile as many times as the play did. A tile announced a thousand times while no companion ticked is one
    /// entry, which is what keeps the next line bounded however long the stretch was.
    /// </summary>
    private static readonly Dictionary<Point, int> announcedCounts = new();
    private static readonly List<Point> announcedOrder = new();
    /// <summary>
    /// Each tile within <see cref="ReframeRadius"/> of an announced edit, as it stood when the edit was announced, so the
    /// next tick can write down every one the game changed. The game frames an unframed tile the first time a neighbour's
    /// framing reaches it, and a world read straight from its file is full of them: measured 24 September 2026 on a
    /// seeded soak, a dirt tile two columns from a broken one went from an unset frame to 18,18 with nothing announced,
    /// and a replay that wrote only the broken tile's eight neighbours disagreed with the play's terrain on the next edit.
    /// Kept as raw values rather than text, so hearing an edit allocates nothing but the dictionary's own slots.
    /// </summary>
    private static readonly Dictionary<Point, TileState> tilesBeforeEdits = new();
    private const int ReframeRadius = 3;

    /// <summary>
    /// How many tiles the pending edits may hold snapshots of, which bounds everything the next line writes about them:
    /// every entry is one of these tiles, so the line's edits never exceed this many entries of about forty characters —
    /// about 90,000 characters, well inside the one-megabyte partition the line is queued into. A compact vein of a few
    /// hundred broken tiles, or forty scattered ones, fits. An announcement whose neighbourhood would take the set past
    /// this is counted instead of kept, and the next line says how many as `edits-lost`, which a replay names as a terrain
    /// disagreement on that tick. The previous bound counted announcements rather than tiles (4,096 of them, each with a
    /// 49-tile snapshot) and could put 200,000 entries on one line, which the queue would refuse.
    /// </summary>
    public const int MaximumSnapshotTiles = 2048;
    private static int worldEditsLost;
    private static readonly List<Point> companionEdits = new();
    private static readonly HashSet<Point> companionEditsSeen = new();
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

    /// <summary>Lines the writer's queue refused this session; each leaves a gap in `n` and makes the next line a keyframe.</summary>
    internal static long LinesRefused { get; private set; }

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
        ForgetWhatWasWritten();
        announcedCounts.Clear(); announcedOrder.Clear(); tilesBeforeEdits.Clear(); companionEdits.Clear(); companionEditsSeen.Clear();
        worldEditsLost = 0;
        lineOrdinal = 0;
        LinesRefused = 0;
        keyframeNotBefore = 0;
        line.Clear();
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
        if (insideCompanionTick) { if (companionEditsSeen.Add(tile)) companionEdits.Add(tile); return; }
        // Edits wait here for the next companion tick, and a session with no companion ticking — none spawned yet, or one
        // gone — collects every edit the player makes for as long as that lasts. A tile announced again only counts; a new
        // tile is kept only while its whole neighbourhood fits under the snapshot bound, and is otherwise counted as lost,
        // so the next line is bounded and says what it could not carry rather than replaying a world that silently lacks it.
        if (announcedCounts.TryGetValue(tile, out int announced)) { announcedCounts[tile] = announced + 1; return; }
        int newTiles = 0;
        for (int dy = -ReframeRadius; dy <= ReframeRadius; dy++)
        for (int dx = -ReframeRadius; dx <= ReframeRadius; dx++)
            if (!tilesBeforeEdits.ContainsKey(new Point(x + dx, y + dy))) newTiles++;
        if (tilesBeforeEdits.Count + newTiles > MaximumSnapshotTiles) { worldEditsLost++; return; }
        announcedCounts[tile] = 1;
        announcedOrder.Add(tile);
        for (int dy = -ReframeRadius; dy <= ReframeRadius; dy++)
        for (int dx = -ReframeRadius; dx <= ReframeRadius; dx++)
        {
            var near = new Point(x + dx, y + dy);
            if (!tilesBeforeEdits.ContainsKey(near)) tilesBeforeEdits[near] = TileState.Read(near.X, near.Y);
        }
    }

    /// <summary>The top of the companion's tick: write down the world it is about to observe.</summary>
    public static void BeforeTheCompanionTick(CompanionNPC companion)
    {
        insideCompanionTick = true;
        recordingThisTick = sessionOpen && GodsEyeEvents.Active;
        if (!recordingThisTick) return;
        if (nextIsKeyframe && Main.GameUpdateCount < keyframeNotBefore)
        {
            // Waiting out the second after a refused line: this tick's line is lost with it, and counted, and its ordinal
            // taken, so the gap a reader names covers it. Pending world edits keep waiting for the keyframe.
            recordingThisTick = false;
            lineOrdinal++;
            LinesRefused++;
            return;
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var profiled = BrainSections.Enter(ReplaySection);

        line.Clear();
        line.Append("v=").Append(FormatVersion).Append(";n=").Append(lineOrdinal).Append(";tick=").Append(Main.GameUpdateCount.ToString(Invariant));
        bool keyframe = nextIsKeyframe;
        nextIsKeyframe = false;
        if (keyframe) line.Append(";key=1");
        NPC body = companion.NPC;
        line.Append(";body=").Append(F(body.position.X)).Append(',').Append(F(body.position.Y)).Append(',')
            .Append(F(body.velocity.X)).Append(',').Append(F(body.velocity.Y)).Append(',').Append(I(body.life))
            .Append(',').Append(B(companion.IsDowned));
        // The companion's own NPC slot, which a replay puts it in so every recorded actor keeps its own slot.
        if (body.whoAmI != lastSelf) { line.Append(";self=").Append(body.whoAmI); lastSelf = body.whoAmI; }
        // The weapon knowledge the save loaded and the hooks between ticks feed: a digest of all of it on a keyframe, so a
        // replay can say whether it began from the same belief, and the revision whenever it moved, so a replay can say
        // on which tick knowledge it does not carry arrived.
        if (keyframe) line.Append(";kn=").Append(DescribeKnowledge());
        if (WeaponKnowledge.KnowledgeRevision.Current != lastKnowledgeRevision)
        {
            lastKnowledgeRevision = WeaponKnowledge.KnowledgeRevision.Current;
            line.Append(";kr=").Append(lastKnowledgeRevision);
        }

        Player player = Main.LocalPlayer;
        line.Append(";player=");
        AppendDelta(line, player, PlayerTable, playerState);
        line.Append(";world=");
        AppendDelta(line, null, WorldTable, worldState);
        if (InventoryMoved(player)) { string inventory = DescribeInventory(player); if (inventory != lastInventory) { line.Append(";inv=").Append(inventory); lastInventory = inventory; } }
        if (GearMoved(player)) { string gear = DescribeGear(player); if (gear != lastGear) { line.Append(";gear=").Append(gear); lastGear = gear; } }
        // The cargo bag, which collection prices every drop against and the player can rearrange through its panel.
        if (BagMoved(companion)) { string bag = DescribeBag(companion); if (bag != lastBag) { line.Append(";bag=").Append(bag); lastBag = bag; } }

        line.Append(";npc=");
        bool first = true;
        for (int slot = 0; slot < Main.maxNPCs; slot++)
        {
            NPC npc = Main.npc[slot];
            bool present = npc != null && npc.active && !ReferenceEquals(npc, body);
            AppendSlot(line, slot, present ? npc : null, NpcTable, npcStates, ref first);
        }
        line.Append(";item=");
        first = true;
        for (int slot = 0; slot < Main.maxItems; slot++)
        {
            Item item = Main.item[slot];
            AppendSlot(line, slot, item != null && item.active ? item : null, ItemTable, itemStates, ref first);
        }
        line.Append(";proj=");
        first = true;
        for (int slot = 0; slot < Main.maxProjectiles; slot++)
        {
            Projectile projectile = Main.projectile[slot];
            AppendSlot(line, slot, IsRecordedProjectile(projectile) ? projectile : null, ProjectileTable, projectileStates, ref first);
        }

        line.Append(";edits=");
        int announced = announcedOrder.Count;
        AppendPendingEdits(line);
        if (worldEditsLost > 0) { line.Append(";edits-lost=").Append(I(worldEditsLost)); worldEditsLost = 0; }

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
        NoteTheTickStart(companion);
        timestampsSpent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        DecisionClock.StartTape();
    }

    /// <summary>The bottom of the companion's tick: what it drew, what it spent, what it edited, what it decided.</summary>
    public static void AfterTheCompanionTick(CompanionNPC companion)
    {
        insideCompanionTick = false;
        if (!recordingThisTick)
        {
            // A tick whose line is not written — no session, or one waiting to retry a keyframe — keeps none of its own
            // edits, so the next written line's `cedits` holds only that line's tick.
            companionEdits.Clear();
            companionEditsSeen.Clear();
            return;
        }
        recordingThisTick = false;
        string tape = DecisionClock.EndTape();
        if (!GodsEyeEvents.Active) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var profiled = BrainSections.Enter(ReplaySection);

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
        lineOrdinal++;
        if (GodsEyeEvents.RecordReplayInputs(companion.NPC, detail))
        {
            LinesWritten++;
            CharactersWritten += detail.Length;
        }
        else
        {
            // Every later line is a delta against this one, so a line the queue refused would silently corrupt every frame
            // after it. The ordinal already moved, so a reader sees the gap; the next line starts from nothing, so a capture
            // carries a whole scene again from there.
            LinesRefused++;
            ForgetWhatWasWritten();
            keyframeNotBefore = Main.GameUpdateCount + TicksBetweenKeyframeAttempts;
        }
        timestampsSpent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    /// <summary>
    /// The world's edits since the last companion tick: every announced tile with its announcement count (`a` once,
    /// `a3` three times), then the eight neighbours of each and every other snapshotted tile the game changed, marked `n`
    /// because only the game's framing touched them. Each entry is `x,y,state,mark`, joined by `|`.
    /// </summary>
    private static void AppendPendingEdits(StringBuilder into)
    {
        if (announcedOrder.Count == 0 && tilesBeforeEdits.Count == 0) return;
        bool firstEntry = true;
        void Entry(Point tile, string mark)
        {
            if (!firstEntry) into.Append('|');
            firstEntry = false;
            into.Append(tile.X).Append(',').Append(tile.Y).Append(',').Append(DescribeTile(tile.X, tile.Y)).Append(',').Append(mark);
        }
        foreach (Point tile in announcedOrder)
        {
            int count = announcedCounts[tile];
            Entry(tile, count == 1 ? "a" : "a" + count.ToString(Invariant));
        }
        foreach (Point tile in announcedOrder)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var neighbour = new Point(tile.X + dx, tile.Y + dy);
                if (announcedCounts.ContainsKey(neighbour) || !tilesBeforeEdits.Remove(neighbour)) continue;
                Entry(neighbour, "n");
            }
        foreach ((Point near, TileState before) in tilesBeforeEdits)
            if (!announcedCounts.ContainsKey(near) && !TileState.Read(near.X, near.Y).Equals(before)) Entry(near, "n");
        tilesBeforeEdits.Clear();
        announcedCounts.Clear();
        announcedOrder.Clear();
    }

    /// <summary>
    /// The decision a tick made, as one comparable string: the course's activity, reason and whether it settled; the
    /// bound step's opportunity, method and pose; the activity that held the body; the movement request; the controls
    /// the motor applied; and the velocity the body was handed. Identifiers minted from process-wide counters are
    /// left out on purpose, because two runs in one process do not share them and a replay compared on them would
    /// disagree on its first tick about nothing the brain decided.
    /// </summary>
    public static string DescribeDecision(CompanionNPC companion) => DescribeDecision(companion, FormatVersion);

    /// <summary>
    /// The decision digest in the shape <paramref name="format"/> wrote it, so a replay compares a capture against the
    /// digest it holds. Format 2 adds what the hand released this tick — weapon slot and item, the aim point to the pixel,
    /// the target's slot and the launch — and what the bag took: its transfer count and a fingerprint of the bag and the
    /// player's inventory after the tick. Without them a different weapon, aim or target, or a pickup that went elsewhere,
    /// left the digest unchanged whenever the one-word fire outcome and the body's motion agreed.
    /// </summary>
    public static string DescribeDecision(CompanionNPC companion, int format)
    {
        string digest = DescribeDecisionFormatOne(companion);
        if (format < 2) return digest;
        string released = companion.Combat.LastRelease is { } release && release.Tick == Main.GameUpdateCount
            ? $"{release.WeaponSlot}:{release.ItemType}:{(int)MathF.Round(release.AimPoint.X)},{(int)MathF.Round(release.AimPoint.Y)}:{release.TargetWhoAmI}:{F(release.Launch.X)},{F(release.Launch.Y)}"
            : "-";
        var bag = companion.Bag;
        return $"{digest}|{released}|{bag.TransferSequence - transfersAtTickStart}:{FingerprintItems(bag.Items)}:{FingerprintItems(Main.LocalPlayer.inventory)}";
    }

    /// <summary>The bag's transfer count when the tick began, so the digest carries this tick's transfers rather than the session's.</summary>
    private static long transfersAtTickStart;

    /// <summary>Note the bag's transfer count at the top of a tick. The recorder calls it on every recorded tick; a replay,
    /// which records nothing, calls it before each replayed tick so the two digests count transfers the same way.</summary>
    public static void NoteTheTickStart(CompanionNPC companion) => transfersAtTickStart = companion.Bag.TransferSequence;

    /// <summary>An FNV hash of every slot's type, prefix and stack, air included, so any change to what a list holds moves it.</summary>
    private static string FingerprintItems(Item[] items)
    {
        uint hash = 2166136261;
        void Mix(int value) { unchecked { hash ^= (uint)value; hash *= 16777619; } }
        foreach (Item? item in items)
        {
            if (item == null || item.IsAir) { Mix(0); continue; }
            Mix(item.type); Mix(item.prefix); Mix(item.stack);
        }
        return hash.ToString("x8", Invariant);
    }

    private static string DescribeDecisionFormatOne(CompanionNPC companion)
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

    /// <summary>The ten values <see cref="DescribeTile"/> writes, held raw so a pending edit's snapshot costs no text.</summary>
    private readonly record struct TileState(ushort Type, bool Has, int Slope, bool Half, bool Actuated, ushort Wall,
        byte LiquidAmount, int LiquidType, short FrameX, short FrameY)
    {
        public static TileState Read(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) return default;
            Tile tile = Main.tile[x, y];
            return new TileState(tile.TileType, tile.HasTile, (int)tile.Slope, tile.IsHalfBlock, tile.IsActuated, tile.WallType,
                tile.LiquidAmount, tile.LiquidType, tile.TileFrameX, tile.TileFrameY);
        }
    }

    /// <summary>
    /// A digest of the weapon knowledge the brain holds — the flight laws, volley shapes, learned attack models and effects
    /// the save loaded and play has taught since — as the FNV hash of its own export and that export's length, so a replay
    /// can say whether it began from the same belief. The export is the persistence format, so this costs one save's worth
    /// of formatting, and it is taken only on a keyframe. The export's three revision counters are left out: they are cache
    /// keys that every learner's reset advances rather than zeroes, so two processes holding identical knowledge disagree
    /// on them — measured 25 September 2026, a second reproduction in one process read the same 173-character export with
    /// different counters and a different digest.
    /// </summary>
    public static string DescribeKnowledge()
    {
        var bundle = System.Text.Json.Nodes.JsonNode.Parse(WeaponKnowledge.PersistWeaponKnowledge.ExportAll())?.AsObject();
        foreach (string counter in new[] { "KnowledgeRevision", "OutcomeRevision", "EffectsRevision" }) bundle?.Remove(counter);
        string exported = bundle?.ToJsonString() ?? "";
        uint hash = 2166136261;
        foreach (char character in exported) { unchecked { hash ^= character; hash *= 16777619; } }
        return $"{hash:x8}.{exported.Length}";
    }

    /// <summary>Whether any bag slot's type, prefix or stack moved since the last line.</summary>
    private static bool BagMoved(CompanionNPC companion)
    {
        Item[] items = companion.Bag.Items;
        bool moved = lastBagRaw.Length != items.Length * 2;
        if (moved) lastBagRaw = new long[items.Length * 2];
        for (int slot = 0; slot < items.Length; slot++)
        {
            Item? item = items[slot];
            long kind = item == null ? 0 : ((long)item.type << 32) | (uint)item.prefix, stack = item?.stack ?? 0;
            if (lastBagRaw[slot * 2] == kind && lastBagRaw[slot * 2 + 1] == stack) continue;
            lastBagRaw[slot * 2] = kind;
            lastBagRaw[slot * 2 + 1] = stack;
            moved = true;
        }
        return moved;
    }

    /// <summary>Every occupied bag slot as `slot:type:prefix:stack`, joined by `/`.</summary>
    private static string DescribeBag(CompanionNPC companion)
    {
        Item[] items = companion.Bag.Items;
        var text = new StringBuilder();
        for (int slot = 0; slot < items.Length; slot++)
        {
            Item item = items[slot];
            if (item == null || item.IsAir) continue;
            if (text.Length > 0) text.Append('/');
            text.Append(slot).Append(':').Append(item.type).Append(':').Append(item.prefix).Append(':').Append(item.stack);
        }
        return text.ToString();
    }

    /// <summary>Harness-only: the companion's bag as recorded, in <see cref="DescribeBag"/>'s shape.</summary>
    public static void ApplyBag(CompanionNPC companion, string value)
    {
        if (DescribeBag(companion) == value) return;
        var wanted = new Dictionary<int, (int Type, int Prefix, int Stack)>();
        if (value.Length > 0)
            foreach (string entry in value.Split('/'))
            {
                string[] parts = entry.Split(':');
                wanted[ParseInt(parts[0])] = (ParseInt(parts[1]), ParseInt(parts[2]), ParseInt(parts[3]));
            }
        // In place, as the inventory is, so an item the bag already held keeps its identity across the replay.
        Item[] items = companion.Bag.Items;
        for (int slot = 0; slot < items.Length; slot++)
        {
            items[slot] ??= new Item();
            Item item = items[slot];
            if (wanted.TryGetValue(slot, out var recorded))
            {
                if (item.type != recorded.Type || item.prefix != recorded.Prefix)
                {
                    item.SetDefaults(recorded.Type);
                    if (recorded.Prefix != 0) item.Prefix(recorded.Prefix);
                }
                item.stack = recorded.Stack;
            }
            else if (!item.IsAir) item.TurnToAir();
        }
    }

    /// <summary>Harness-only: make a projectile slot hold the hostile shot recorded there, with only the fields the hit
    /// prediction reads, so no content has to be loaded for its type.</summary>
    public static void ApplyProjectile(Projectile projectile, IReadOnlyDictionary<string, string> values)
    {
        foreach (var field in ProjectileFields)
            if (values.TryGetValue(field.Key, out string? value)) field.Write(projectile, value);
        projectile.hostile = true;
        projectile.friendly = false;
        projectile.active = true;
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

    /// <summary>Every subject's delta state forgotten, so the next line is a keyframe that writes the whole scene.</summary>
    private static void ForgetWhatWasWritten()
    {
        foreach (DeltaState?[] states in new[] { npcStates, itemStates, projectileStates })
            foreach (DeltaState? state in states)
                if (state != null) { state.Forget(); state.Present = false; }
        playerState.Forget();
        worldState.Forget();
        lastInventory = ""; lastGear = ""; lastBag = "";
        lastInventoryRaw = Array.Empty<long>(); lastGearRaw = Array.Empty<long>(); lastBagRaw = Array.Empty<long>();
        lastSelf = -1;
        lastKnowledgeRevision = int.MinValue;
        nextIsKeyframe = true;
    }

    /// <summary>Append each field of <paramref name="entity"/> whose value moved since <paramref name="state"/> last wrote
    /// it, as `key=value` joined by commas; true when anything was appended.</summary>
    private static bool AppendDelta<T>(StringBuilder into, T entity, Table<T> table, DeltaState state)
    {
        bool wroteAny = false;
        Field<T>[] fields = table.Fields;
        // Every raw value read into one array first, so a subject where nothing moved is settled by a single comparison of
        // the whole array, which is the common case on a crowded world and the reason this is cheap there. Plain arrays
        // rather than spans, because this assembly may be built without optimisation and a span's indexer is then a call.
        long[] now = table.Scratch;
        bool[] read = table.ReadScratch;
        bool everyRawRead = true;
        if (table.Whole != null && table.EveryFieldHasRaw && table.Whole(entity, now)) Array.Fill(read, true);
        else
            for (int index = 0; index < fields.Length; index++)
            {
                Field<T> field = fields[index];
                read[index] = field.RawWidth > 0 && field.Raw!(entity, now.AsSpan(table.Offsets[index], field.RawWidth));
                everyRawRead &= read[index];
            }
        if (everyRawRead && state.AllRawKnown && now.AsSpan().SequenceEqual(state.Raw)) return false;
        for (int index = 0; index < fields.Length; index++)
        {
            Field<T> field = fields[index];
            if (read[index])
            {
                // Element by element rather than through span slices: a changed subject walks every field here, and in an
                // unoptimised build each slice, comparison and copy is a call — measured 25 September 2026, the sliced
                // form made an every-entity-moving tick cost more than formatting every field had.
                long[] stored = state.Raw;
                int offset = table.Offsets[index], end = offset + field.RawWidth;
                bool same = state.RawKnown[index];
                for (int at = offset; same && at < end; at++) same = now[at] == stored[at];
                if (same) continue;
                for (int at = offset; at < end; at++) stored[at] = now[at];
                state.RawKnown[index] = true;
            }
            else state.RawKnown[index] = false;
            string value = field.Read(entity);
            if (state.Text[index] == value) continue;
            state.Text[index] = value;
            if (wroteAny) into.Append(',');
            into.Append(field.Key).Append('=').Append(value);
            wroteAny = true;
        }
        state.AllRawKnown = everyRawRead;
        return wroteAny;
    }

    /// <summary>One slot of an entity table: `slot:-` for a slot that emptied, `slot:key=value,…` for the fields that moved,
    /// nothing at all for an occupied slot where nothing did. The entry is appended in place and cut back when it turns out
    /// to hold nothing, so a still slot allocates nothing.</summary>
    private static void AppendSlot<T>(StringBuilder into, int slot, T? entity, Table<T> table, DeltaState?[] states, ref bool first)
        where T : class
    {
        DeltaState? state = states[slot];
        if (entity == null)
        {
            if (state == null || !state.Present) return;
            state.Present = false;
            state.Forget();
            if (!first) into.Append('|');
            into.Append(slot).Append(":-");
            first = false;
            return;
        }
        state ??= states[slot] = table.NewState();
        // A slot that was empty last tick starts from nothing, so every field is written for it.
        if (!state.Present) { state.Forget(); state.Present = true; }
        int mark = into.Length;
        if (!first) into.Append('|');
        into.Append(slot).Append(':');
        if (AppendDelta(into, entity, table, state)) first = false;
        else into.Length = mark;
    }

    /// <summary>Whether any inventory slot's type or stack moved since the last line, compared raw so an unchanged
    /// inventory is not described.</summary>
    private static bool InventoryMoved(Player player)
    {
        Item[] inventory = player.inventory;
        bool moved = lastInventoryRaw.Length != inventory.Length;
        if (moved) lastInventoryRaw = new long[inventory.Length];
        for (int slot = 0; slot < inventory.Length; slot++)
        {
            Item? item = inventory[slot];
            long raw = item == null ? 0 : ((long)item.type << 32) | (uint)item.stack;
            if (lastInventoryRaw[slot] == raw) continue;
            lastInventoryRaw[slot] = raw;
            moved = true;
        }
        return moved;
    }

    /// <summary>Whether any of the companion's gear slots moved in type, prefix or stack since the last line.</summary>
    private static bool GearMoved(Player player)
    {
        var slots = player.GetModPlayer<CompanionPlayer>().Gear.Slots;
        bool moved = lastGearRaw.Length != slots.Length * 2;
        if (moved) lastGearRaw = new long[slots.Length * 2];
        for (int slot = 0; slot < slots.Length; slot++)
        {
            Item? item = slots[slot];
            long kind = item == null ? 0 : ((long)item.type << 32) | (uint)item.prefix, stack = item?.stack ?? 0;
            if (lastGearRaw[slot * 2] == kind && lastGearRaw[slot * 2 + 1] == stack) continue;
            lastGearRaw[slot * 2] = kind;
            lastGearRaw[slot * 2 + 1] = stack;
            moved = true;
        }
        return moved;
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

    private static readonly FieldInfo? LightEngineField = typeof(Lighting).GetField("NewEngine", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    private static FieldInfo? scannerField, scannerRandomField;

    /// <summary>The light engine's tile scanner and its random field, the field lookups cached because this runs every
    /// recorded tick.</summary>
    private static ScannerAccess? LightScanner()
    {
        object? engine = LightEngineField?.GetValue(null);
        if (engine == null) return null;
        scannerField ??= engine.GetType().GetField("_tileScanner", BindingFlags.Instance | BindingFlags.NonPublic);
        object? scanner = scannerField?.GetValue(engine);
        if (scanner == null) return null;
        scannerRandomField ??= scanner.GetType().GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic);
        return scannerRandomField == null ? null : new ScannerAccess(scanner, scannerRandomField);
    }

    private static ulong? ReadLightScannerSeed()
        => LightScanner() is { } found && found.RandomField.GetValue(found.Scanner) is FastRandom random ? random.Seed : null;
}
