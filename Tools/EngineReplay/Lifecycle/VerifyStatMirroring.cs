extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// The companion's toughness is the player's, by the owner's ruling of 14 September 2026: its maximum life and defence
/// mirror the local player's every tick, so a life crystal or an armour change reaches it on the next tick and nothing is
/// ever equipped on it. Three facts, through the real AI entry point: a raise in maximum life carries current life up by
/// the same amount (a crystal is neither a heal to full nor a wound), defence follows the player's exactly, and a cut in
/// maximum life clamps current life to the new ceiling without killing. The mirror itself predates the ruling; this row
/// is the proof the ruling asked for.
/// </summary>
internal static class VerifyStatMirroring
{
    private const int FloorRow = 80;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int failed = 0;
        // The rows tick the whole brain; the wall-clock planning allowance would make their verdict depend on machine load.
        LimitPlanningWork.Unbounded = true;
        try
        {
            failed += RunOneRow.Case("a life crystal and an armour change reach the companion on the next tick, and a cut in maximum life clamps its life", LifeAndDefenceFollowThePlayer, "stat mirroring");
        }
        finally { LimitPlanningWork.Unbounded = false; }
        Console.WriteLine(failed == 0
            ? "stat mirroring: maximum life and defence track the player's, a raise carries current life with it, a cut clamps it"
            : $"stat mirroring: {failed} case(s) failed");
        return failed;
    }

    private static void LifeAndDefenceFollowThePlayer()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[TileID.Stone] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile floor = Main.tile[x, FloorRow];
            floor.ClearEverything();
            floor.HasTile = true;
            floor.TileType = TileID.Stone;
        }
        var companion = VerifyCompanionLifecycle.Create();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        companion.NPC.position = new Vector2(50 * 16 + 8 - companion.NPC.width / 2f, FloorRow * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.statDefense = Player.DefenseStat.Default;
        player.velocity = Vector2.Zero;
        player.Center = companion.NPC.Center + new Vector2(24f, 0f);

        Tick(companion);
        Require(companion.NPC.lifeMax == 100 && companion.NPC.defense == 0,
            $"premise: the companion starts at the player's 100 life and 0 defence; lifeMax={companion.NPC.lifeMax} defense={companion.NPC.defense}");
        int lifeBefore = companion.NPC.life;
        Require(lifeBefore >= 1 && lifeBefore <= 100, $"premise: current life sits inside the ceiling; life={lifeBefore}");

        // A life crystal and an armour set, as the player sees them.
        player.statLifeMax2 = 120;
        player.statDefense = Player.DefenseStat.Default + 13;
        Tick(companion);
        Require(companion.NPC.lifeMax == 120, $"a life crystal raises the companion's maximum by the player's twenty; lifeMax={companion.NPC.lifeMax}");
        Require(companion.NPC.life == lifeBefore + 20,
            $"a raise in maximum life carries current life up by the same amount, neither a heal to full nor a wound; life {lifeBefore} -> {companion.NPC.life}");
        Require(companion.NPC.defense == 13, $"the player's armour defence is the companion's defence; defense={companion.NPC.defense}");

        // The armour comes off and the ceiling drops below current life.
        player.statLifeMax2 = 60;
        player.statDefense = Player.DefenseStat.Default + 4;
        Tick(companion);
        Require(companion.NPC.lifeMax == 60 && companion.NPC.life >= 1 && companion.NPC.life <= 60,
            $"a cut in maximum life clamps current life to the new ceiling without killing; lifeMax={companion.NPC.lifeMax} life={companion.NPC.life}");
        Require(companion.NPC.defense == 4, $"defence follows the player down as readily as up; defense={companion.NPC.defense}");
        Console.WriteLine($"MEASURE stat mirroring: life {lifeBefore}/100 -> {lifeBefore + 20}/120 -> {companion.NPC.life}/60; defence 0 -> 13 -> 4, each on the next tick");
    }

    private static void Tick(CompanionNPC companion) => VerifyCompanionLifecycle.TickWithOneControlGrant(companion);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
