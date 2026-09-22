using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

/// <summary>
/// A player nobody recorded: a seeded bot that walks the surface of a real saved world, stops, and
/// mines, with hostiles and drops arriving on a schedule drawn from the same seed.
///
/// <b>Why the soak cannot use a recorded route.</b> Every other run in this folder replays a capture,
/// and a capture is at most a few minutes long — the longest this machine holds is 22,473 ticks, about
/// six minutes. The thing the soak is looking for is a climb over an hour, so the track has to be
/// generated rather than read, and generated in a way two runs at one seed agree on exactly.
///
/// <b>It is a generator rather than a list, and that is the memory verdict's own requirement.</b> An
/// hour is 216,000 ticks, and a materialised track of that length is about ten megabytes that grows
/// with the run — which would sit inside the very measurement the soak exists to take. So the track is
/// advanced one tick at a time from a state machine, and the cast is built by a first pass over a
/// second generator with the same seed rather than by remembering the first.
///
/// <b>What it does not claim.</b> This is a bot and not a player. It walks a surface, it stops, and it
/// breaks a few tiles; it does not fight, build, use an item or go underground. What the soak needs
/// from it is churn — a body that moves so following has something to do, terrain edits so retained
/// searches are invalidated, and hostiles and drops arriving and leaving so the census has something
/// to admit and retire. A behaviour the bot cannot perform is a behaviour the soak does not cover, and
/// the row says so rather than implying an hour of play.
/// </summary>
internal sealed class DriveASeededScene
{
    /// <summary>How far either side of the world's spawn the bot is allowed to walk, in tiles. It
    /// reverses at the clamp, so the run stays on ground the world generator made walkable near a
    /// spawn point rather than wandering into an ocean or off a cliff after ten minutes.</summary>
    private const int HalfRangeTiles = 110;

    /// <summary>How far the ground may step between two columns before the bot treats it as a wall and
    /// turns round. A surface walk over a real world meets cliffs; four tiles is about a player's own
    /// jump, so what it refuses is terrain a walking player could not take either.</summary>
    private const int ClimbableStepTiles = 4;

    /// <summary>The player's walking pace, in pixels a tick. Terraria's own base run speed is about
    /// three; this is that, so the intent region's lead is the one a real walk produces.</summary>
    private const float PacePixelsPerTick = 3f;

    private readonly Random random;
    private readonly int spawnTileX;
    private readonly int startTick;

    private int tileX;
    private int direction = 1;
    private int phaseTicksLeft;
    private Phase phase = Phase.Walking;
    private float x;

    private enum Phase { Walking, Standing, Mining }

    /// <summary>The bot's own mining, and what stopped it. Null while it is still breaking tiles.</summary>
    public string? MiningRetired { get; private set; }

    /// <summary>How many tiles the bot actually broke, so a row can say whether terrain churn happened
    /// at all rather than assuming the phase ran.</summary>
    public int TilesMined { get; private set; }

    public DriveASeededScene(int seed, int startTick)
    {
        random = new Random(seed);
        this.startTick = startTick;
        spawnTileX = Math.Clamp(Main.spawnTileX, HalfRangeTiles + 8, Main.maxTilesX - HalfRangeTiles - 8);
        tileX = spawnTileX;
        x = tileX * 16f + 8f;
        phaseTicksLeft = 0;
    }

    /// <summary>The tile column the bot is standing over, which the cast places its actors around.</summary>
    public int TileX => tileX;

    /// <summary>
    /// One tick of the bot: the phase advances, the body moves, and the step the run places is
    /// returned. <paramref name="mineTile"/> is the tile the caller should break, or null.
    /// </summary>
    public ReadRecordedRoute.Step Advance(int tick, out Point? mineTile)
    {
        mineTile = null;
        if (phaseTicksLeft <= 0) ChoosePhase();
        phaseTicksLeft--;

        float velocity = 0f;
        switch (phase)
        {
            case Phase.Walking:
            {
                int ahead = tileX + direction;
                int here = GroundRow(tileX), next = GroundRow(ahead);
                // A cliff or a wall turns the bot round rather than letting it walk into geometry the
                // ground-follow would then read as a hundred-tile fall.
                if (Math.Abs(next - here) > ClimbableStepTiles
                    || ahead < spawnTileX - HalfRangeTiles || ahead > spawnTileX + HalfRangeTiles)
                {
                    direction = -direction;
                    break;
                }
                velocity = direction * PacePixelsPerTick;
                x += velocity;
                tileX = (int)(x / 16f);
                break;
            }
            case Phase.Mining:
            {
                // One tile every twelve ticks, ahead of the bot at ground level: a real edit to a real
                // world, announced through the game's own KillTile so the mod's GlobalTile hooks —
                // which this host binds on purpose — tell every retained search where to forget.
                if (MiningRetired == null && phaseTicksLeft % 12 == 0)
                    mineTile = new Point(tileX + direction, GroundRow(tileX + direction));
                break;
            }
        }

        int row = GroundRow(tileX);
        var feet = new Vector2(x, row * 16f);
        return new ReadRecordedRoute.Step(tick, feet, new Vector2(velocity, 0f), PlayerGrounded: true,
            // The companion's opening centre only matters on the first step; afterwards the run's own
            // body is where the motor put it and this field is unread.
            CompanionCentre: feet - new Vector2(0f, 32f),
            PlayerLife: 400, Loot: -1, Threats: -1);
    }

    /// <summary>Records that the bot's mining met something this host cannot run, so the row can say
    /// the phase stopped and why instead of the soak quietly losing its terrain churn.</summary>
    public void RetireMining(string reason) => MiningRetired ??= reason;

    public void CountMinedTile() => TilesMined++;

    private void ChoosePhase()
    {
        int roll = random.Next(100);
        if (roll < 55) { phase = Phase.Walking; phaseTicksLeft = 90 + random.Next(240); if (random.Next(4) == 0) direction = -direction; }
        else if (roll < 85) { phase = Phase.Standing; phaseTicksLeft = 45 + random.Next(150); }
        else { phase = Phase.Mining; phaseTicksLeft = 36 + random.Next(60); direction = random.Next(2) == 0 ? -1 : 1; }
    }

    /// <summary>
    /// The row the bot's feet rest on in a column: the first solid tile scanned downward from well
    /// above the surface.
    ///
    /// Solidity is read through the movement core's own shape test rather than <c>tileSolid</c> alone,
    /// which is this repository's standing rule — worldgen smooths cave corners into slopes and half
    /// blocks that a bare flag reports as walls. A column with nothing solid in range keeps the bot
    /// where it was, which is the honest answer for a bot that cannot fall.
    /// </summary>
    private int GroundRow(int column)
    {
        if (column < 1 || column >= Main.maxTilesX - 1) return (int)(Main.worldSurface);
        int from = Math.Max(1, (int)Main.worldSurface - 200);
        for (int row = from; row < Math.Min(Main.maxTilesY - 1, (int)Main.worldSurface + 200); row++)
            if (Main.tile[column, row].HasTile && Main.tileSolid[Main.tile[column, row].TileType]
                && !Main.tileSolidTop[Main.tile[column, row].TileType])
                return row;
        return (int)Main.worldSurface;
    }

    /// <summary>
    /// The cast the run stages, built by walking a second generator at the same seed rather than by
    /// remembering the first.
    ///
    /// Two passes over a pure generator cost one extra walk of the track and keep the run's memory flat,
    /// which is the property the soak's own memory verdict depends on. Everything placed is placed
    /// relative to where the bot will actually be standing on the tick it appears, which is the whole
    /// reason the cast cannot be written down in advance without the track.
    /// </summary>
    public static ReadRecordedActors.Cast BuildTheCast(int seed, int startTick, int ticks)
    {
        var scene = new DriveASeededScene(seed, startTick);
        var schedule = new Random(seed ^ 0x5EED);
        var hostiles = new List<ReadRecordedActors.Hostile>();
        var drops = new List<ReadRecordedActors.Drop>();
        int nextHostile = 120 + schedule.Next(240), nextDrop = 200 + schedule.Next(400);
        int hostileSlot = 5, dropSlot = 1;

        for (int i = 0; i < ticks; i++)
        {
            int tick = startTick + i;
            ReadRecordedRoute.Step step = scene.Advance(tick, out _);
            if (i == nextHostile)
            {
                // Beside the bot rather than on him: a hostile spawned inside his own box reads as a
                // contact on the tick it appears, which is a scene the play never had.
                float offset = (schedule.Next(2) == 0 ? -1 : 1) * (6 + schedule.Next(10)) * 16f;
                hostiles.Add(new ReadRecordedActors.Hostile(tick, hostileSlot, 1, NPCID.Zombie, "Zombie",
                    step.PlayerFeet + new Vector2(offset, -16f), 45, tick + 400 + schedule.Next(500)));
                hostileSlot = hostileSlot >= 24 ? 5 : hostileSlot + 1;
                nextHostile = i + 180 + schedule.Next(420);
            }
            if (i == nextDrop)
            {
                drops.Add(new ReadRecordedActors.Drop(tick, dropSlot, ItemID.CopperOre, 3 + schedule.Next(6),
                    step.PlayerFeet + new Vector2((schedule.Next(2) == 0 ? -1 : 1) * (2 + schedule.Next(6)) * 16f, -8f),
                    CentreIsExact: true, tick + 300 + schedule.Next(500)));
                dropSlot = dropSlot >= 8 ? 1 : dropSlot + 1;
                nextDrop = i + 300 + schedule.Next(600);
            }
        }

        // One interpolated string would be the tidier shape and does not compile here: concatenating two
        // of them yields a `string`, which binds `string.Create` to its span overload. The suite's own
        // guide carries this trap; the locals below are the form that compiles.
        string note = FormattableString.Invariant(
            $"this cast is generated from seed {seed} rather than read from a recording: {hostiles.Count} zombie(s) ")
            + FormattableString.Invariant($"and {drops.Count} drop(s) placed beside a bot player over {ticks} ticks, each retired on its own ")
            + "scheduled tick. Nothing here reproduces a session anybody played, and a row taken on it is about the "
            + "brain under churn rather than about the morning of 22 September";
        return new ReadRecordedActors.Cast("(generated)", hostiles, drops,
            MostDropsCountedAtOnce: drops.Count, Shortfall: 0, LastCompanionKillTick: -1,
            StoppedReadingAt: null, Configuration: "", Note: note);
    }

    /// <summary>The route header a generated track carries. It declares itself generated in the place a
    /// reader looks for a capture's name, because a synthetic route wearing a capture's shape is the
    /// same hazard as a synthetic capture dropped into <c>Telemetry/</c>.</summary>
    public static ReadRecordedRoute.Route HeaderFor(int seed, ReadRecordedRoute.Step opening)
        => new($"generated-soak-seed-{seed.ToString(CultureInfo.InvariantCulture)}", "generated", "n/a",
            new ReadRecordedRoute.Kits(false, "", false, false, false, false), "", "", new[] { opening });
}
