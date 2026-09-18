extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Recording = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using LearnVolleys = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnVolleyShapes;
using Laws = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FitFlightLaws;
using FlightLaw = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FlightLaw;
using Spoof = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.SpoofOwnerInputForShots;
using Persist = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.PersistWeaponKnowledge;
using WeaponIdentity = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.WeaponIdentity;

/// <summary>
/// What the companion learns from the player's own volleys: spawn grouping (K8), laws from player-only
/// flights (K0), and the cursor a companion shot's AI reads (K6). Grouping is driven synthetically, laws
/// against the game's own <see cref="Projectile.VanillaAI"/>, and the spoof as the mechanism itself — no
/// vanilla projectile reads the cursor without a held button the companion never holds, so the spoof's
/// readers are modded and covered by the play protocol rather than by a vanilla flight.
/// </summary>
internal static class VerifyVolleyLearning
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reset()
    {
        VerifyCompanionLifecycle.Create();
        Recording.RecordProjectileFlights.Clear();
        Recording.GroupSpawnsIntoUses.Clear();
        LearnVolleys.Reset();
        Laws.Reset();
        Spoof.Clear();
    }

    /// <summary>
    /// K8: a four-pellet same-tick spread and a three-shot burst across one animation group into one use each.
    /// Grouping by tick only would cut the burst into three uses, which is the mutation this row kills.
    /// </summary>
    public static int SpreadAndBurstGroupIntoOneUseEach()
    {
        Reset();
        Vector2 shooter = new(1000f, 1000f);
        Vector2 aim = new(1400f, 1000f);
        Main.LocalPlayer.Center = shooter;

        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(100, 20, 20, ItemID.Boomstick, System.Array.Empty<int>());
        for (int pellet = 0; pellet < 4; pellet++)
        {
            var spawn = new Projectile { whoAmI = 10 + pellet, type = ProjectileID.Bullet, damage = 10, active = true };
            spawn.Center = shooter;
            spawn.velocity = new Vector2(10f, 0f).RotatedBy((pellet - 1.5f) * 0.05f);
            Recording.GroupSpawnsIntoUses.NoteSpawn(100, spawn.whoAmI, spawn, ItemID.Boomstick, ItemID.MusketBall, aim);
        }
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(101, 0, 20, ItemID.Boomstick, System.Array.Empty<int>());
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(102, 0, 20, ItemID.Boomstick, System.Array.Empty<int>());

        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(200, 12, 12, ItemID.ClockworkAssaultRifle, System.Array.Empty<int>());
        for (int shot = 0; shot < 3; shot++)
        {
            var spawn = new Projectile { whoAmI = 20 + shot, type = ProjectileID.Bullet, damage = 10, active = true };
            spawn.Center = shooter;
            spawn.velocity = new Vector2(11f, 0f);
            Recording.GroupSpawnsIntoUses.NoteSpawn(200 + shot * 4, spawn.whoAmI, spawn, ItemID.ClockworkAssaultRifle, ItemID.MusketBall, aim);
            Recording.GroupSpawnsIntoUses.NotePlayerAnimation(201 + shot * 4, 11 - shot * 4, 12, ItemID.ClockworkAssaultRifle, System.Array.Empty<int>());
        }
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(213, 0, 12, ItemID.ClockworkAssaultRifle, System.Array.Empty<int>());
        Recording.GroupSpawnsIntoUses.NotePlayerAnimation(214, 0, 12, ItemID.ClockworkAssaultRifle, System.Array.Empty<int>());

        var closed = Recording.GroupSpawnsIntoUses.ClosedUses;
        Require(closed.Count == 2, $"a spread and a burst are two uses, not {closed.Count}; grouping by tick would cut the burst into three");
        Require(closed[0].ItemType == ItemID.Boomstick && closed[0].Spawns.Count == 4 && closed[0].Complete,
            $"the spread is one complete four-pellet use; got item={closed[0].ItemType} spawns={closed[0].Spawns.Count} complete={closed[0].Complete}");
        Require(closed[1].ItemType == ItemID.ClockworkAssaultRifle && closed[1].Spawns.Count == 3 && closed[1].Complete,
            $"the burst is one complete three-shot use; got item={closed[1].ItemType} spawns={closed[1].Spawns.Count} complete={closed[1].Complete}");
        Require(LearnVolleys.HasShape(ItemID.Boomstick) && LearnVolleys.HasShape(ItemID.ClockworkAssaultRifle),
            "both uses teach their item a shape");
        Console.WriteLine("volley grouping: four pellets on one tick and three shots across an animation group into one use each");
        return 0;
    }

    /// <summary>
    /// K0: flights the player fired teach the law the companion's shot flies, and the companion's own flights
    /// teach it nothing — they were aimed by the law, so fitting on them would teach it its own aim back.
    /// Watching companion shots only — the behaviour before phase B — would leave the prior standing, which
    /// is the mutation this row kills.
    /// </summary>
    public static int ArcsAreLearnedFromThePlayersFlights()
    {
        Reset();
        Vector2 muzzle = new(1000f, 1000f);
        Main.LocalPlayer.Center = muzzle;
        Main.screenPosition = Vector2.Zero;
        Main.mouseX = 1400;
        Main.mouseY = 1000;
        var item = new Item();
        item.SetDefaults(ItemID.ThrowingKnife);

        for (int flight = 0; flight < 2; flight++)
        {
            int useId = Recording.GroupSpawnsIntoUses.OpenCompanionUse(50, ItemID.ThrowingKnife, muzzle + new Vector2(100f, 0f), muzzle, 10f, 10f);
            var aimed = new Projectile();
            aimed.SetDefaults(ProjectileID.ThrowingKnife);
            aimed.whoAmI = 20 + flight;
            aimed.active = true;
            aimed.owner = Main.myPlayer;
            aimed.Center = muzzle;
            aimed.velocity = new Vector2(10f, 0f);
            Recording.RecordProjectileFlights.NoteCompanionSpawn(aimed.whoAmI, aimed, useId,
                live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState.None);
            for (int tick = 1; tick <= 46; tick++)
            {
                aimed.VanillaAI();
                aimed.position += aimed.velocity;
                Recording.RecordProjectileFlights.NoteStep(aimed);
            }
            Recording.RecordProjectileFlights.NoteDeath(aimed);
            Recording.GroupSpawnsIntoUses.CloseCompanionUse(useId, 51);
        }
        Require(Laws.LawFor(ProjectileID.ThrowingKnife).Revision == 0,
            "two companion flights must leave the prior standing; the law learns from the player's flights only");

        for (int flight = 0; flight < 2; flight++)
        {
            var native = new Projectile();
            native.SetDefaults(ProjectileID.ThrowingKnife);
            native.whoAmI = 30 + flight;
            native.active = true;
            native.owner = Main.myPlayer;
            native.Center = muzzle;
            native.velocity = new Vector2(10f, 0f);
            Recording.RecordProjectileFlights.NoteSpawn(native.whoAmI, native,
                new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
            for (int tick = 1; tick <= 46; tick++)
            {
                native.VanillaAI();
                native.position += native.velocity;
                Recording.RecordProjectileFlights.NoteStep(native);
            }
            Recording.RecordProjectileFlights.NoteDeath(native);
        }

        FlightLaw law = Laws.LawFor(ProjectileID.ThrowingKnife);
        Require(law.Revision >= 1, "two player flights must fit a law; watching companion shots only would leave the prior standing");
        Require(law.Predictable, $"the player-taught law must predict; residual={law.ResidualPerUpdate}");
        Require(law.Gravity is { OnsetUpdate: 19 } gravity && MathF.Abs(gravity.Acceleration - 0.4f) < 0.005f,
            $"player flights recover the knife's onset and gravity; fitted {law.Gravity}");
        Require(MathF.Abs(law.Drag.Horizontal - 0.97f) < 0.005f && law.Drag.OnsetUpdate == 19,
            $"player flights recover the knife's drag; fitted {law.Drag}");
        Console.WriteLine($"volley learning: the player's knife flights teach onset 19, gravity {law.Gravity.Value.Acceleration:0.000}, drag {law.Drag.Horizontal:0.000}");
        return 0;
    }

    /// <summary>
    /// K6: during a registered companion shot's AI the mouse reads as the companion's aim point, and the
    /// player's cursor is back the moment the AI returns. Unregistered slots and player traces never arm it.
    /// </summary>
    public static int CompanionShotsReadTheCompanionsAimAsTheirCursor()
    {
        Reset();
        Main.screenPosition = new Vector2(1000f, 800f);
        Main.mouseX = 400;
        Main.mouseY = 300;
        Vector2 aim = new(1500f, 900f);
        Vector2 muzzle = new(1200f, 900f);

        int useId = Recording.GroupSpawnsIntoUses.OpenCompanionUse(50, ItemID.WoodenBow, aim, muzzle, 10f, 9f);
        var shot = new Projectile { whoAmI = 7, type = ProjectileID.WoodenArrowFriendly, active = true, penetrate = 1, timeLeft = 600 };
        shot.Center = muzzle;
        shot.velocity = new Vector2(9f, 0f);
        Recording.RecordProjectileFlights.NoteCompanionSpawn(7, shot, useId, live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState.None);
        Recording.RecordProjectileFlights.NoteCompanionAim(7, aim);

        Spoof.Enter(7);
        Require(Main.mouseX == 500 && Main.mouseY == 100,
            $"during the AI the mouse must read as the aim point; got ({Main.mouseX},{Main.mouseY}), want (500,100)");
        Require(Main.MouseWorld == aim, $"MouseWorld during the AI must be the aim; got {Main.MouseWorld}");
        Spoof.Exit(7);
        Require(Main.mouseX == 400 && Main.mouseY == 300,
            $"after the AI the player's cursor must be back; got ({Main.mouseX},{Main.mouseY})");

        Spoof.Enter(9);
        Require(Main.mouseX == 400 && Main.mouseY == 300, "an unregistered slot must leave the mouse alone");
        Spoof.Exit(9);

        var item = new Item();
        item.SetDefaults(ItemID.ThrowingKnife);
        var playerShot = new Projectile { whoAmI = 11, type = ProjectileID.ThrowingKnife, active = true, penetrate = 1, timeLeft = 600 };
        playerShot.Center = muzzle;
        playerShot.velocity = new Vector2(10f, 0f);
        Recording.RecordProjectileFlights.NoteSpawn(11, playerShot, new EntitySource_ItemUse(Main.player[Main.myPlayer], item));
        Require(Recording.RecordProjectileFlights.AimFor(11) == null, "a player trace must arm no spoof");
        Spoof.Enter(11);
        Require(Main.mouseX == 400 && Main.mouseY == 300, "a player shot's AI must run under the player's own cursor");
        Spoof.Exit(11);

        Recording.GroupSpawnsIntoUses.CloseCompanionUse(useId, 51);
        Console.WriteLine("cursor spoof: the companion's aim during its shots' AI, the player's cursor before and after");
        return 0;
    }

    /// <summary>
    /// K10: knowledge saved and loaded under shuffled numeric ids resolves to the same weapons. The
    /// bundle keys by full name; a fixture identity that maps WoodenArrowFriendly onto a different
    /// numeric id still restores the planted law there. Keying by numeric id — the file-8 mutation —
    /// would install the law on the old id and leave the shuffled one as the default.
    /// </summary>
    public static int KnowledgeSurvivesANameKeyedSaveUnderShuffledIds()
    {
        Reset();
        FlightLaw planted = FlightLaw.Default(ProjectileID.WoodenArrowFriendly) with { ResidualPerUpdate = 0.42f, Evidence = 17 };
        Laws.AssumeLaw(ProjectileID.WoodenArrowFriendly, planted);
        Vector2 shooter = new(1000f, 1000f), aim = new(1400f, 1000f);
        var use = new Recording.ProjectileUse
        {
            Id = 1,
            Shooter = Recording.Shooter.Player,
            ItemType = ItemID.WoodenBow,
            StartTick = 100,
            AimPoint = aim,
            ShooterCentre = shooter,
            BuffsAtStart = Array.Empty<int>(),
            Complete = true,
        };
        use.Spawns.Add(new Recording.UseSpawn(10, ProjectileID.WoodenArrowFriendly, 100, shooter,
            new Vector2(8f, 0f), 5, 0, shooter, aim));
        LearnVolleys.Learn(use);
        string json = Persist.Export(new int[] { ItemID.WoodenBow }, new int[] { NPCID.Zombie });
        Require(json.Contains("WoodenArrowFriendly", System.StringComparison.Ordinal),
            "the bundle must key the law by name, not by the numeric id");

        Laws.Reset();
        LearnVolleys.Reset();
        const int shuffled = 4096;
        var identity = new ShuffledArrowIdentity(shuffled);
        (int installed, int skipped) = Persist.Import(json, identity);
        Require(installed > 0, $"the named bundle must install; installed {installed}, skipped {skipped}");
        FlightLaw restored = Laws.LawFor(shuffled);
        Require(restored.ResidualPerUpdate == 0.42f && restored.Evidence == 17,
            $"the shuffled id must carry the planted law; residual {restored.ResidualPerUpdate}, evidence {restored.Evidence}");
        FlightLaw original = Laws.LawFor(ProjectileID.WoodenArrowFriendly);
        Require(original.ResidualPerUpdate != 0.42f,
            "the original numeric id must not hold the planted law after a name-keyed load");
        Console.WriteLine($"weapon knowledge: name-keyed save restored the arrow law onto id {shuffled} ({installed} installed)");
        return 0;
    }

    /// <summary>Maps the vanilla wooden arrow onto a numeric id no content owns, so a name-keyed load is observable.</summary>
    private sealed class ShuffledArrowIdentity : WeaponIdentity
    {
        private readonly int shuffled;
        public ShuffledArrowIdentity(int shuffled) => this.shuffled = shuffled;
        public override string NameOfProjectile(int id)
            => id == shuffled ? "WoodenArrowFriendly" : base.NameOfProjectile(id);
        public override int? ProjectileOfName(string name)
            => name == "WoodenArrowFriendly" ? shuffled : base.ProjectileOfName(name);
    }
}
