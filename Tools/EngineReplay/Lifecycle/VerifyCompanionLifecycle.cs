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
        // Occupy cosmetic slots: StrikeNPC still applies real damage without attempting
        // to measure a damage popup using fonts that a headless simulation never loads.
        for (int i = 0; i < Main.combatText.Length; i++) Main.combatText[i] = new CombatText { active = true };
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
        var companionPlayer = new live::AICompanion.Companion.PlayerIntegration.CompanionPlayer();
        // Register one template, as tModLoader does. Registering every fixture instance turns
        // ContentInstance<T>.Instance into null once the loader sees multiple templates.
        if (ModContent.GetInstance<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>() == null)
            ContentInstance.Register(companionPlayer);
        typeof(ModPlayer).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(companionPlayer, Main.player[0]);
        typeof(Player).GetField("modPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Main.player[0], new ModPlayer[] { companionPlayer });
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] = new Projectile { whoAmI = i, active = false };
        for (int i = 0; i < Main.item.Length; i++) Main.item[i] = new Item { whoAmI = i, active = false };
        var companion = new CompanionNPC();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        var npc = new NPC();
        typeof(ModNPC).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(companion, npc);
        // Native damage calls NPCLoader through the NPC's reverse attachment. Entity alone
        // runs direct AI correctly but bypasses the companion's CheckDead at lethal damage.
        typeof(NPC).GetProperty("ModNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(npc, companion);
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
        // Every full-brain fixture lifts the live tick's wall-clock planning allowances so its verdict cannot follow
        // machine load; this one's assertions never wait on a search, and it lifts them so that stays true as it grows.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = true;
        try { return RunWithPlanningLifted(); }
        finally { live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false; }
    }

    private static int RunWithPlanningLifted()
    {
        var companion = Create();
        companion.HoldItem(Terraria.ID.ItemID.Torch);
        TickWithOneControlGrant(companion);
        Require(companion.Brain.Senses.Tick == 1, "player death must not suspend the companion brain");
        Require(companion.HeldItemType == Terraria.ID.ItemID.None,
            "an unclaimed previous-tick torch must not survive hand resolution");
        Set(companion.Torch, "<Lit>k__BackingField", true);
        companion.NPC.wet = true;
        companion.Torch.Update(companion.Brain.Senses.Light, companion.NPC, true);
        Require(!companion.Torch.Shown, "ordinary torches must not remain visible underwater");
        companion.CheckDead();
        int tick = companion.Brain.Senses.Tick;
        TickWithOneControlGrant(companion);
        Require(companion.IsDowned && companion.Brain.Senses.Tick == tick && !companion.Torch.Shown,
            "the companion's own downed state suspends decisions and hides its torch");
        VerifyWorldMemory();
        VerifyRoutePersistence.Run();
        Console.WriteLine("companion lifecycle: real NPC AI continues with a dead player and clears stale hand ownership");
        return 0;
    }

    private static void VerifyWorldMemory()
    {
        var owner = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        var memory = live::AICompanion.Companion.Brain.Infrastructure.Movement.RememberExecutedRoutes.World;
        owner.OnWorldLoad();
        var entry = new live::AICompanion.Companion.Brain.Infrastructure.Movement.BodyState(150, 176, 0, 0, true,
            Capabilities: live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementCapabilities.Basic);
        var end = entry with { Left = 166 };
        var step = new live::AICompanion.Companion.Brain.Infrastructure.Movement.NavStep(end.FeetTile,
            live::AICompanion.Companion.Brain.Infrastructure.Movement.MoveKind.Walk, entry.FeetTile, Ticks: 10);
        memory.Record(live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World, step, entry, end,
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

    internal static void TickWithOneControlGrant(CompanionNPC companion)
    {
        long before = companion.Motor.ControlApplications;
        long priorGrant = companion.Brain.ControlGrants.Last?.Id ?? 0;
        companion.AI();
        var grant = companion.Brain.ControlGrants.Last;
        var presentation = companion.Brain.Presentation;
        Require(presentation.Tick == Main.GameUpdateCount && presentation.ActivityId == companion.Brain.Chooser.Activity.Id
            && presentation.Family == companion.Brain.Chooser.Current?.Family
            && presentation.Activity == companion.Brain.Chooser.Current?.Name
            && presentation.Phase == companion.Brain.Chooser.Activity.Phase
            && presentation.Downed == companion.IsDowned && presentation.Recovering == companion.Brain.FollowRecovery.Active
            && presentation.SafetyActive == companion.Brain.Safety.Active,
            "presentation must publish one completed activity/control state, including early returns");
        Require(companion.Motor.ControlApplications == before + 1,
            $"one AI invocation must apply exactly one movement packet; applied={companion.Motor.ControlApplications - before}");
        Require(grant is { } result && result.Id == priorGrant + 1 && result.Tick == Main.GameUpdateCount
            && result.MotorApplications == 1 && result.AppliedOwner == companion.Motor.ControlSource
            && result.AppliedMovement == companion.Motor.AppliedControls,
            "the completed grant must describe the current motor application, including early returns");
        if (companion.IsDowned)
            Require(grant!.Value.Hand == live::AICompanion.Companion.Brain.Infrastructure.Grants.HandGrant.Unavailable,
                "downed controls must revoke the hand grant");
    }

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
