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
        // Dust-spawning AI (a water bolt's trail) needs somewhere to put it; the game fills this at start-up.
        Main.dust ??= new Dust[6000];
        for (int i = 0; i < Main.dust.Length; i++) Main.dust[i] ??= new Dust();
        foreach (int item in new[] { Terraria.ID.ItemID.WoodenBow, Terraria.ID.ItemID.WoodenArrow, Terraria.ID.ItemID.ThrowingKnife,
            Terraria.ID.ItemID.CopperPickaxe, Terraria.ID.ItemID.CopperAxe, Terraria.ID.ItemID.CopperBroadsword, Terraria.ID.ItemID.WandofSparking,
            Terraria.ID.ItemID.FlintlockPistol, Terraria.ID.ItemID.MusketBall, Terraria.ID.ItemID.WoodYoyo, Terraria.ID.ItemID.GoldPickaxe,
            Terraria.ID.ItemID.Boomstick, Terraria.ID.ItemID.ClockworkAssaultRifle, Terraria.ID.ItemID.DemonBow,
            Terraria.ID.ItemID.DemonScythe, Terraria.ID.ItemID.CrystalBullet })
        {
            var sample = new Item();
            sample.SetDefaults(item);
            Terraria.ID.ContentSamples.ItemsByType[item] = sample;
        }
        // The demon sickle (S1's unlimited pierce), the demon scythe (S4's piercing child), the crystal
        // bullet and shard (K5's splitting pair; the shard is also K4's pass-through): a sim without its
        // type's sample reads declared pierce one and dies on its first body, which is a missing sample
        // wearing the shape of a pierce bug.
        foreach (int type in new[] { Terraria.ID.ProjectileID.WoodenArrowFriendly, Terraria.ID.ProjectileID.ThrowingKnife,
            Terraria.ID.ProjectileID.Bullet, Terraria.ID.ProjectileID.WandOfSparkingSpark, Terraria.ID.ProjectileID.WoodYoyo,
            Terraria.ID.ProjectileID.DemonSickle, Terraria.ID.ProjectileID.DemonScythe,
            Terraria.ID.ProjectileID.CrystalBullet, Terraria.ID.ProjectileID.CrystalShard })
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
        // The gear a fresh companion holds in every fixture: the arsenal enumerates weapons from these
        // slots and the tools read their power from them, so an empty gear would leave every combat
        // and work fixture testing a companion with nothing in its hands. The two weapons are the two
        // items the authored kit read, because the combat scenes were calibrated against that pair's
        // speeds — a reposition priced at the edge of the evaluation window is inside it for the knife
        // and outside it for the bow alone.
        companionPlayer.Gear.Slots[0].SetDefaults(Terraria.ID.ItemID.WoodenBow);
        companionPlayer.Gear.Slots[1].SetDefaults(Terraria.ID.ItemID.ThrowingKnife);
        companionPlayer.Gear.Slots[2].SetDefaults(Terraria.ID.ItemID.CopperPickaxe);
        companionPlayer.Gear.Slots[3].SetDefaults(Terraria.ID.ItemID.CopperAxe);
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] = new Projectile { whoAmI = i, active = false };
        for (int i = 0; i < Main.item.Length; i++) Main.item[i] = new Item { whoAmI = i, active = false };
        var companion = new CompanionNPC();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World = new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
        companion.Torch.Update(companion.Brain.Senses.Light, companion.NPC, true, companion.NPC.Center);
        Require(!companion.Torch.Shown, "ordinary torches must not remain visible underwater");
        companion.CheckDead();
        int tick = companion.Brain.Senses.Tick;
        TickWithOneControlGrant(companion);
        Require(companion.IsDowned && companion.Brain.Senses.Tick == tick && !companion.Torch.Shown,
            "the companion's own downed state suspends decisions and hides its torch");
        VerifyTheWorldLifecycleClearsTheEditRecord();
        Console.WriteLine("companion lifecycle: real NPC AI continues with a dead player and clears stale hand ownership");
        return 0;
    }

    /// <summary>
    /// What the world lifecycle still owns, now that it owns less. It used to carry the walker's
    /// archive of executed routes across a save and a load, and the rows here proved that archive
    /// was world-scoped rather than process-scoped: a second world must not inherit the first
    /// world's remembered routes. The orb has no archive — it plans from the corner graph every
    /// time and keeps nothing between worlds — so those rows have no subject and are gone with it.
    ///
    /// What remains is the terrain edit record, and it has the same world-scoping requirement for
    /// the same reason: its revision counter is what every retained search and every clearance chunk
    /// compares against, so a record carried into a second world would tell each of them that
    /// terrain they have never read is unchanged.
    /// </summary>
    private static void VerifyTheWorldLifecycleClearsTheEditRecord()
    {
        var owner = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        owner.OnWorldLoad();
        int held = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Revision;

        // An edit somewhere else leaves a consumer's own region answerable and unchanged.
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Changed(400, 400);
        Require(live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Revision != held,
            "an announced edit must move the terrain revision");
        Require(live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Edits.ChangedSince(held, (x, y) => x == 150 && y == 134)
            == live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainEditVerdict.Unchanged,
            "an edit outside a consumer's own region must leave that consumer's retained work standing");

        // Crossing a world boundary is the case no tile can be named for, so every revision taken
        // before it becomes unanswerable rather than unchanged — the safe direction, because a
        // search told Unchanged here would serve the next world terrain from the previous one.
        owner.OnWorldUnload();
        owner.OnWorldLoad();
        Require(live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Edits.ChangedSince(held, (x, y) => true)
            == live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainEditVerdict.Changed,
            "a revision taken in a previous world must never answer Unchanged in this one");
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
            && presentation.Downed == companion.IsDowned && presentation.Recovering == companion.Brain.FollowRecovery.Active,
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
