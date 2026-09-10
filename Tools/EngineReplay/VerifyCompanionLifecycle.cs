extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;

internal static class VerifyCompanionLifecycle
{
    public static CompanionNPC Create()
    {
        Main.rand = new Terraria.Utilities.UnifiedRandom(1);
        foreach (int item in new[] { Terraria.ID.ItemID.WoodenBow, Terraria.ID.ItemID.WoodenArrow, Terraria.ID.ItemID.ThrowingKnife,
            Terraria.ID.ItemID.CopperPickaxe, Terraria.ID.ItemID.CopperAxe })
        {
            var sample = new Item();
            sample.SetDefaults(item);
            Terraria.ID.ContentSamples.ItemsByType[item] = sample;
        }
        foreach (int type in new[] { Terraria.ID.ProjectileID.WoodenArrowFriendly, Terraria.ID.ProjectileID.ThrowingKnife })
        {
            var sample = new Projectile();
            sample.SetDefaults(type);
            Terraria.ID.ContentSamples.ProjectilesByType[type] = sample;
        }
        Lighting.Initialize();
        Terraria.ID.TorchID.Initialize();
        Main.Map = new Terraria.Map.WorldMap(Main.maxTilesX, Main.maxTilesY);
        Terraria.Map.MapHelper.Initialize();
        Main.myPlayer = 0;
        Main.player[0] = new Player { active = true, dead = true, statLifeMax2 = 100,
            position = new Vector2(400, 1400) };
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] = new Projectile { whoAmI = i, active = false };
        for (int i = 0; i < Main.item.Length; i++) Main.item[i] = new Item { whoAmI = i, active = false };
        var companion = new CompanionNPC();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
        var npc = new NPC();
        typeof(ModNPC).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(companion, npc);
        companion.SetDefaults();
        npc.position = new Vector2(400, 1398);
        // Rendering and first-tick logging require the loader/graphics services; the real AI,
        // breath, observations, chooser and motor remain active in this headless fixture.
        Set(companion, "loggedFirstTick", true);
        Set(companion.Body, "rendererFailed", true);
        return companion;
    }

    public static int Run()
    {
        var companion = Create();
        companion.HoldItem(Terraria.ID.ItemID.Torch);
        companion.AI();
        Require(companion.Brain.Senses.Tick == 1, "player death must not suspend the companion brain");
        Require(companion.HeldItemType == Terraria.ID.ItemID.None,
            "an unclaimed previous-tick torch must not survive hand resolution");
        Set(companion.Torch, "<Lit>k__BackingField", true);
        companion.NPC.wet = true;
        companion.Torch.Update(companion.Brain.Senses.Light, companion.NPC, true);
        Require(!companion.Torch.Shown, "ordinary torches must not remain visible underwater");
        companion.CheckDead();
        int tick = companion.Brain.Senses.Tick;
        companion.AI();
        Require(companion.IsDowned && companion.Brain.Senses.Tick == tick && !companion.Torch.Shown,
            "the companion's own downed state suspends decisions and hides its torch");
        VerifyWorldMemory();
        VerifyRoutePersistence.Run();
        Console.WriteLine("companion lifecycle: real NPC AI continues with a dead player and clears stale hand ownership");
        return 0;
    }

    private static void VerifyWorldMemory()
    {
        var owner = new live::AICompanion.Companion.Brain.SharedMovementSystem.ResetTerrainChanges();
        var memory = live::AICompanion.Companion.Brain.SharedMovementSystem.RememberExecutedRoutes.World;
        owner.OnWorldLoad();
        var entry = new live::AICompanion.Companion.Brain.SharedMovementSystem.BodyState(150, 176, 0, 0, true,
            Capabilities: live::AICompanion.Companion.Brain.SharedMovementSystem.MovementCapabilities.Basic);
        var end = entry with { Left = 166 };
        var step = new live::AICompanion.Companion.Brain.SharedMovementSystem.NavStep(end.FeetTile,
            live::AICompanion.Companion.Brain.SharedMovementSystem.MoveKind.Walk, entry.FeetTile, Ticks: 10);
        memory.Record(live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World, step, entry, end,
            new Rectangle(150, 134, 36, 42));
        Require(memory.Count == 1, "archive lifecycle fixture must contain an entry");
        var firstWorld = new Terraria.ModLoader.IO.TagCompound();
        owner.SaveWorldData(firstWorld);
        owner.OnWorldUnload();
        owner.OnWorldLoad();
        owner.LoadWorldData(new Terraria.ModLoader.IO.TagCompound());
        Require(memory.Count == 0, "an unrelated world must not inherit route memory");
        owner.OnWorldUnload();
        owner.OnWorldLoad();
        owner.LoadWorldData(firstWorld);
        Require(memory.Count == 1, "world-owned route memory must survive clear-then-load lifecycle");
        owner.OnWorldUnload();
    }

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
