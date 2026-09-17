#nullable enable

extern alias live;

using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using live::AICompanion.Companion.Brain.Infrastructure.Movement;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;
using live::AICompanion.Companion.CharacterBody;
using live::AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Tools.CombatAudit;

/// <summary>
/// The headless host the audit replays decisions on: a sized tile world, registered content samples,
/// and a fresh companion per snapshot. It mirrors the engine suite's process setup — the same save
/// path, the same dedicated-server flag, the same tile solidity for the vanilla ground the audit
/// stands bodies on — so a replay reads the same engine the fixtures proved the brain against. Main
/// arrays are sized per snapshot window and every brain static is reset between snapshots, because a
/// replay must read only what its snapshot carried, never the previous snapshot's flood or forecast.
/// </summary>
internal static class AuditHost
{
    internal const int Margin = 8;

    private static bool prepared;
    private static bool[]? solidDefault;
    private static bool[]? solidTopDefault;
    private static CompanionNPC? companion;
    private static CompanionPlayer? companionPlayer;

    public static CompanionNPC Companion => companion!;
    public static CompanionPlayer CompanionPlayer => companionPlayer!;

    public static void PrepareProcess()
    {
        if (prepared)
            return;
        prepared = true;
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Main.myPlayer = 0;
        Main.rand = new UnifiedRandom(12345);
        Main.combatText ??= new CombatText[100];
        for (int i = 0; i < Main.combatText.Length; i++) Main.combatText[i] = new CombatText { active = true };
        Main.dust = new Dust[6000];
        for (int i = 0; i < Main.dust.Length; i++) Main.dust[i] = new Dust();
        for (int type = 0; type < 600 && type < Main.tileSolid.Length; type++)
        {
            Main.tileSolid[type] = type == TileID.Dirt || type == TileID.Stone || type == TileID.Grass
                || type == TileID.WoodBlock || type == TileID.GrayBrick || type == TileID.Glass;
            Main.tileSolidTop[type] = type == TileID.Platforms || type == TileID.PlanterBox;
        }
        solidDefault = (bool[])Main.tileSolid.Clone();
        solidTopDefault = (bool[])Main.tileSolidTop.Clone();
    }

    /// <summary>
    /// Size the world for the snapshot's window plus a solid margin, and restore the brain's statics to
    /// a blank decision: knowledge, tracks, ledgers, budgets, caches, the flood and the dice. The margin
    /// ring reads solid, because unrecorded space is unknown space and the audit must not route through
    /// what the snapshot never saw; the window itself is restored tile by tile afterwards. Returns the
    /// pixel shift from snapshot coordinates to local coordinates.
    /// </summary>
    public static Vector2 SizeWorld(int windowX, int windowY, int windowWidth, int windowHeight)
    {
        PrepareProcess();
        int width = Math.Max(16, windowWidth + Margin * 2);
        int height = Math.Max(16, windowHeight + Margin * 2);
        Main.maxTilesX = width;
        Main.maxTilesY = height;
        Main.worldSurface = height / 2;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { (ushort)width, (ushort)height }, null)!;
        ResetStatics();
        for (int i = 0; i < Main.npc.Length; i++)
            Main.npc[i] = new NPC { whoAmI = i, active = false };
        for (int i = 0; i < Main.projectile.Length; i++)
            Main.projectile[i] = new Projectile { whoAmI = i, active = false };
        for (int i = 0; i < Main.item.Length; i++)
            Main.item[i] = new Item { whoAmI = i, active = false };
        ResetSolidity();
        MovementQueries.World = new GameTileWorld();
        return new Vector2((Margin - windowX) * 16f, (Margin - windowY) * 16f);
    }

    public static void ResetStatics()
    {
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnVolleyShapes.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FitFlightLaws.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnWallResponses.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnHitResponses.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnChildSpawns.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WeaponEffects.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.AttackLearning.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CacheSimulatedUses.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.KnowledgeRevision.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ShotOutcomes.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.Observation.HostileAttackSources.Clear();
        PredictObservedMotion.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        Main.rand = new UnifiedRandom(12345);
    }

    /// <summary>Restore the vanilla solidity tables, then overlay the snapshot's solid ids: modded tiles
    /// have no meaning headless, but their solidity was recorded per tile and the simulator reads it.</summary>
    public static void ResetSolidity()
    {
        Array.Copy(solidDefault!, Main.tileSolid, Math.Min(solidDefault!.Length, Main.tileSolid.Length));
        Array.Copy(solidTopDefault!, Main.tileSolidTop, Math.Min(solidTopDefault!.Length, Main.tileSolidTop.Length));
    }

    public static void MarkSolid(int type, bool solid, bool solidTop)
    {
        if ((uint)type < (uint)Main.tileSolid.Length && solid)
            Main.tileSolid[type] = true;
        if ((uint)type < (uint)Main.tileSolidTop.Length && solidTop)
            Main.tileSolidTop[type] = true;
    }

    public static void RegisterSample(int item)
    {
        var sample = new Item();
        sample.SetDefaults(item);
        ContentSamples.ItemsByType[item] = sample;
    }

    public static void RegisterProjectileSample(int type)
    {
        var sample = new Projectile();
        sample.SetDefaults(type);
        ContentSamples.ProjectilesByType[type] = sample;
    }

    /// <summary>A fresh companion on the restored world, with the player and his gear beside it: the same
    /// recipe the engine suite's lifecycle row proves, minus the assertions. The brain has never seen this
    /// snapshot's tick, which is the point — its senses rebuild from the restored actors and its flood
    /// grows on the restored tiles.</summary>
    public static void CreateActors()
    {
        PrepareProcess();
        Lighting.Initialize();
        TorchID.Initialize();
        Main.Map = new Terraria.Map.WorldMap(Main.maxTilesX, Main.maxTilesY);
        Terraria.Map.MapHelper.Initialize();
        Main.myPlayer = 0;
        Main.player[0] = new Player { active = true };
        companionPlayer = new CompanionPlayer();
        if (ModContent.GetInstance<CompanionPlayer>() == null)
            ContentInstance.Register(companionPlayer);
        typeof(ModPlayer).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(companionPlayer, Main.player[0]);
        typeof(Player).GetField("modPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Main.player[0], new ModPlayer[] { companionPlayer });
        companion = new CompanionNPC();
        var npc = new NPC();
        typeof(ModNPC).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(companion, npc);
        typeof(NPC).GetProperty("ModNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(npc, companion);
        companion.SetDefaults();
        npc.active = true;
    }
}
