extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using KeepCompany = live::AICompanion.Companion.Brain.Activities.NearbyAssistance.KeepCompany;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;

/// <summary>
/// Being with the player is being inside his region with a way to him that stays inside it. The owner ruled on 15 September
/// 2026, after a door fixture found a companion standing inside the player's region on the far side of a sealed wall and
/// keeping him company from there, that a body inside the region's box but cut off from him counts as outside, so rejoining
/// sends it round.
///
/// <para>One row, its pass lines declared before the first run, through the whole brain and the native body with only keeping
/// company offered. The player stands on a floor east of a wall that runs from the top of the world into the ground; the only
/// way past it is a trench whose passage lies deeper than the region reaches, so the pocket the companion starts in is sealed
/// off from the player inside his region's box and connected to him only outside it. The companion starts inside the box, on
/// the far side of the wall.</para>
/// <list type="bullet">
/// <item>premise: after the first tick the region's box holds the body and reaches west past the wall;</item>
/// <item>the sense reads the body as not with the player on that first tick;</item>
/// <item>within six hundred ticks the body crosses to the player's side of the wall, and on some tick after that the sense
/// reads it as with him while it is on his side.</item>
/// </list>
/// </summary>
internal static class VerifyWithThePlayerNeedsAWayToHim
{
    private const int FloorRow = 80, WallColumn = 50, PlayerColumn = 62, CompanionColumn = 47, TrenchLeft = 45, TrenchRight = 55,
        PassageTop = 87, PassageBottom = 89, Ticks = 600;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        LimitPlanningWork.Unbounded = true;
        try
        {
            ABodyCutOffInsideTheRegionComesRound();
            Console.WriteLine("with the player: a body inside the region's box but cut off from him by a sealed wall is outside it and comes round");
            return 0;
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"RED with the player needs a way to him: {e.Message}");
            return 1;
        }
        finally { LimitPlanningWork.Unbounded = false; }
    }

    private static void ABodyCutOffInsideTheRegionComesRound()
    {
        var companion = Scene();
        var brain = companion.Brain;
        Player player = Main.player[0];
        int crossed = -1, withHimOnHisSide = -1;
        bool firstInside = true;
        string firstRegion = "";
        for (int tick = 0; tick < Ticks; tick++)
        {
            player.velocity = Vector2.Zero;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            VerifyResponsiveFollowing.AdvanceNative(companion);
            Vector2 body = companion.NPC.Center;
            var region = brain.Senses.Intent.Region;
            if (tick == 0)
            {
                firstInside = brain.Senses.Intent.Inside;
                firstRegion = $"region centre {region.Centre} half {region.HalfSize}; body {body}";
                // The passage must lie below everything the sense's bounded flood may enter, which is the box grown by the settle
                // radius and a tile; otherwise the two sides meet inside it and the scene proves nothing about a sealed pocket.
                float floodBottom = region.Centre.Y + region.HalfSize.Y + live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator.SettleRadius + 16f;
                // The sense snaps its bounds outward to whole tiles, and a corner on the passage's top edge needs the row above free too.
                float floodBottomRow = MathF.Ceiling(floodBottom / 16f);
                firstRegion += $"; bounded flood bottom row {floodBottomRow}, passage top row {PassageTop}";
                Require(region.Contains(body) && region.Centre.X - region.HalfSize.X < WallColumn * 16f && body.X < WallColumn * 16f
                    && floodBottomRow + 1 < PassageTop,
                    $"the premise: the region's box must hold the body and reach west past the wall, with the body on the far side and the passage below it; {firstRegion}");
            }
            if (crossed < 0 && body.X > (WallColumn + 2) * 16f) crossed = tick;
            if (withHimOnHisSide < 0 && crossed >= 0 && brain.Senses.Intent.Inside && body.X > (WallColumn + 1) * 16f) withHimOnHisSide = tick;
        }
        string ledger = $"with him on the first tick {firstInside}; crossed the wall at tick {crossed}; with him on his side from tick {withHimOnHisSide}; "
            + $"{firstRegion}; end body {companion.NPC.Center} inside {brain.Senses.Intent.Inside} action {brain.LastAction?.Name} owner {brain.ControlGrants.Last?.AppliedOwner}";
        Console.WriteLine($"MEASURE with the player needs a way to him: {ledger}");
        Require(!firstInside, $"a body inside the region's box but cut off from the player by a sealed wall must not be read as with him; {ledger}");
        Require(crossed >= 0 && withHimOnHisSide >= 0, $"a body cut off from the player inside his region must come round and be with him on his side; {ledger}");
    }

    private static live::AICompanion.Companion.CharacterBody.CompanionNPC Scene()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[TileID.Stone] = true;
        for (int x = 5; x < 95; x++) Solid(x, FloorRow);
        // The wall runs from the top of the world down into the ground, so a flying body cannot go over it and the ground under
        // the region is not a way past it.
        for (int y = 0; y < PassageTop; y++) Solid(WallColumn, y);
        // The trench: the floor opens between its walls, drops to the passage, and the passage runs under the wall's foot. Its
        // rows lie below everything the region reaches, so inside the box the two sides do not meet.
        for (int x = TrenchLeft + 1; x < TrenchRight; x++)
        {
            if (x != WallColumn) Main.tile[x, FloorRow].ClearEverything();
            Solid(x, PassageBottom + 1);
        }
        for (int y = FloorRow; y <= PassageBottom + 1; y++) { Solid(TrenchLeft, y); Solid(TrenchRight, y); }

        var companion = VerifyCompanionLifecycle.Create();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.velocity = Vector2.Zero;
        player.position = new Vector2(PlayerColumn * 16 + 8 - player.width / 2f, FloorRow * 16 - player.height);
        companion.NPC.Center = new Vector2(CompanionColumn * 16 + 8, FloorRow * 16 - 40);
        companion.NPC.velocity = Vector2.Zero;
        var brain = companion.Brain;
        var company = brain.Actions.OfType<KeepCompany>().Single();
        brain.Actions.RemoveAll(a => !ReferenceEquals(a, company));
        return companion;
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.ClearEverything();
        tile.HasTile = true;
        tile.TileType = TileID.Stone;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
