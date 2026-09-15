extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;
using Experience = live::AICompanion.Companion.Progression.CompanionExperience;
using Credit = live::AICompanion.Companion.Progression.CreditKillsAndFights;
using Work = live::AICompanion.Companion.Progression.CreditWork;
using Striker = live::AICompanion.Companion.Progression.Striker;
using CompanionPlayer = live::AICompanion.Companion.PlayerIntegration.CompanionPlayer;

/// <summary>
/// The companion's experience as the owner ruled it on 15 September 2026: a kill earns the enemy's maximum life as the game
/// set it, the bar is priced from the strongest enemy and boss fight killed so far, each growing five percent a level since
/// it was set, and work fills a thousandth of the bar. The rows that are about who earned a kill strike real NPCs, set up by
/// the game's own SetDefaults in a named difficulty, bracketed by the same two calls the hooks make, so the game's death
/// routine decides the death; the rows that are about the pricing drive the ledger directly, with the default enemy named in
/// the row, so each number in an assertion can be derived from the ruling by hand.
/// </summary>
internal static class VerifyCompanionExperience
{
    private static readonly PropertyInfo ExperienceProperty = typeof(CompanionPlayer).GetProperty("Experience")!;
    private const double Unit = Experience.ExperiencePerLife;
    private const double Close = 1e-9;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int failed = 0;
        int mode = Main.GameMode;
        try
        {
            VerifyCompanionLifecycle.Create();
            // The game's strike reads the player's banner buffs from the scene metrics, which nothing headless creates, and a
            // hit effect writes through the dust slot Dust.NewDust returns, which nothing headless fills.
            Main.SceneMetrics ??= new SceneMetrics();
            for (int i = 0; i < Main.dust.Length; i++) Main.dust[i] ??= new Dust();
            // The miner's PickTile asks TileLoader's hook arrays, which only mod loading creates; the ore fixture's helper fills them empty.
            VerifyOreWork.InitialiseVanillaTileHooks();
            failed += RunOneRow.Case("200 companion kills of the strongest enemy make one level from the green slime default, and 400 of the player's", KillsMakeALevel, "experience");
            failed += RunOneRow.Case("normal, Expert and Master need the same number of kills per level", DifficultyChangesNothing, "experience");
            failed += RunOneRow.Case("the boss that set the anchor fills 15% again, a weaker boss moves nothing, a stronger one resets to 15%", BossAnchorFillsFifteenPercent, "experience");
            failed += RunOneRow.Case("each anchor's bar grows 5% a level since it was set, and a new anchor never lowers the requirement", GrowthAndNeverLower, "experience");
            failed += RunOneRow.Case("a boss of several bodies is credited once, at its whole life, when its last body dies", MultiBodyFightsCreditOnce, "experience");
            failed += RunOneRow.Case("levels the anchor-setting kill completes are priced at the old anchor", FirstKillFillsAtTheOldPrice, "experience");
            failed += RunOneRow.Case("1,000 companion ore breaks make a level and 2,000 of the player's, from their native calls", WorkMakesALevel, "experience");
            failed += RunOneRow.Case("critters, town NPCs, statue spawns, dummies, boss company and others' kills credit nothing", ExcludedKillsCreditNothing, "experience");
            failed += RunOneRow.Case("a registered companion shot is the companion's, the player's projectile his, a trap's and a town NPC's nobody's", StrikersAreNamedByTheShot, "experience");
            failed += RunOneRow.Case("save and load round-trip every field, and a placeholder save loads at level 1", SaveAndLoadRoundTrip, "experience");
        }
        finally
        {
            Main.GameMode = mode;
            Experience.DefaultEnemyLife = Experience.GreenSlimeLifeInThisWorld;
            Credit.Reset();
        }
        Console.WriteLine(failed == 0
            ? "experience: kills, boss fights and work credit the ledger the owner ruled, in every difficulty, and it persists"
            : $"experience: {failed} case(s) failed");
        return failed;
    }

    private static CompanionPlayer Save() => Main.player[0].GetModPlayer<CompanionPlayer>();

    private static Experience Fresh()
    {
        var ledger = new Experience();
        ExperienceProperty.SetValue(Save(), ledger);
        Credit.Reset();
        return ledger;
    }

    private static NPC Spawn(int slot, int type, GameModeData mode, IEntitySource? source = null)
    {
        var npc = new NPC();
        npc.SetDefaults(type, new NPCSpawnParams { gameModeData = mode });
        npc.whoAmI = slot;
        npc.active = true;
        npc.Center = new Vector2(50 * 16f, 30 * 16f);
        Main.npc[slot] = npc;
        Credit.Spawned(npc, source);
        return npc;
    }

    /// <summary>
    /// The game's whole strike and death, bracketed as the hooks bracket it. Two headless allowances touch neither the life
    /// taken nor the deactivation. A killing hit effect spawns gore, whose textures nothing headless loads, and the game skips
    /// gore while paused. The loot path reads the bestiary, the drop database and the achievements, none of which exist here,
    /// and NPCLoot returns on its first line for a network client while checkDead still sets active false after it (NPC.cs:84177
    /// as decompiled). StrikeNPC is called directly because it sends nothing, where Player.StrikeNPCDirect would send the strike
    /// as a client.
    /// </summary>
    private static void Strike(NPC target, Striker by)
    {
        Credit.BeforeStrike(target, by);
        bool paused = Main.gamePaused;
        int netMode = Main.netMode;
        Main.gamePaused = true;
        Main.netMode = 1;
        try { target.StrikeNPC(target.CalculateHitInfo(10_000_000, 1)); }
        finally { Main.gamePaused = paused; Main.netMode = netMode; }
        Credit.AfterStrike(target);
    }

    /// <summary>
    /// The game's death without the strike's per-type hit effect, for the bodies whose hit effect reaches graphics state nothing
    /// headless has — a boss's writes dust through slots and more, and a modded one could reach anything. These are StrikeNPC's
    /// own two death steps: take the life from the NPC that holds it, then run that NPC's checkDead (NPC.cs:92297-92307 and
    /// 92421-92427 as decompiled), under the same client allowance as <see cref="Strike"/>. The zombie rows keep the whole
    /// strike, so the full chain is proven once; these rows are about attribution and fights, which the hit effect has no part in.
    /// </summary>
    private static void StrikeWithoutHitEffect(NPC target, Striker by)
    {
        Credit.BeforeStrike(target, by);
        int netMode = Main.netMode;
        Main.netMode = 1;
        try
        {
            NPC holder = target.realLife >= 0 ? Main.npc[target.realLife] : target;
            if (!holder.immortal) holder.life -= 10_000_000;
            if (target.realLife >= 0) target.life = holder.life;
            // A town NPC's death is announced by name through the language table, which nothing headless loads; its exclusion
            // is decided at the strike's before half, so the death here is the deactivation checkDead would end in.
            if (holder.townNPC) { if (holder.life <= 0) holder.active = false; }
            else holder.checkDead();
        }
        finally { Main.netMode = netMode; }
        Credit.AfterStrike(target);
    }

    private static void KillEnemy(int type, GameModeData mode, Striker by, bool withHitEffect = true)
    {
        NPC npc = Spawn(40, type, mode);
        if (withHitEffect) Strike(npc, by); else StrikeWithoutHitEffect(npc, by);
        Require(!npc.active, $"premise: the game's death must end NPC type {type}");
    }

    private static void KillsMakeALevel()
    {
        Main.GameMode = GameModeID.Normal;
        double slime = Experience.GreenSlimeLife(GameModeData.NormalMode);
        foreach ((Striker by, int kills) in new[] { (Striker.Companion, 200), (Striker.Player, 400) })
        {
            Experience ledger = Fresh();
            Require(ledger.NeededNow == (int)Math.Round(200 * slime), $"a fresh bar must be 200 green slimes read from the game ({200 * slime}), read {ledger.NeededNow}");
            int zombieLife = new NPCProbe(NPCID.Zombie, GameModeData.NormalMode).LifeMax;
            for (int i = 1; i <= kills; i++)
            {
                KillEnemy(NPCID.Zombie, GameModeData.NormalMode, by);
                if (i == 1)
                    Require(ledger.EnemyAnchorLife == zombieLife && ledger.NeededNow == 200 * zombieLife,
                        $"the first kill must anchor the zombie ({zombieLife}) and price 200 of them; anchor {ledger.EnemyAnchorLife}, bar {ledger.NeededNow}");
                if (i == kills - 1) Require(ledger.Level == 1, $"{by}: {i} kills must not yet make a level; level {ledger.Level}, {ledger.IntoLevel}/{ledger.NeededNow}");
            }
            Require(ledger.Level == 2 && ledger.IntoLevel == 0, $"{by}: {kills} kills must make exactly one level; level {ledger.Level}, into {ledger.IntoLevel}");
            Console.WriteLine($"  {by}: {kills} kills of a {zombieLife}-life zombie made level 2 from a {200 * slime:0}-experience first bar");
        }
    }

    private static void DifficultyChangesNothing()
    {
        var counts = new List<string>();
        var lives = new HashSet<int>();
        int? first = null;
        foreach ((string name, int id, GameModeData mode) in new[] { ("normal", GameModeID.Normal, GameModeData.NormalMode), ("Expert", GameModeID.Expert, GameModeData.ExpertMode), ("Master", GameModeID.Master, GameModeData.MasterMode) })
        {
            Main.GameMode = id;
            Experience ledger = Fresh();
            int life = new NPCProbe(NPCID.Zombie, mode).LifeMax;
            lives.Add(life);
            int kills = 0;
            while (ledger.Level == 1 && kills < 2000) { KillEnemy(NPCID.Zombie, mode, Striker.Companion); kills++; }
            counts.Add($"{name}: zombie life {life}, slime bar {200 * Experience.GreenSlimeLife(mode):0}, {kills} kills");
            first ??= kills;
            Require(kills == first, $"kills per level must not depend on difficulty: {string.Join("; ", counts)}");
        }
        Require(lives.Count == 3, $"premise: the game must scale the zombie's life by difficulty: {string.Join("; ", counts)}");
        Console.WriteLine("  " + string.Join("; ", counts));
    }

    private static void BossAnchorFillsFifteenPercent()
    {
        Experience.DefaultEnemyLife = () => 14;
        Experience ledger = Fresh();
        ledger.CreditEnemyKill(45, true);                                     // the enemy bar is 200 x 45 experience
        Require(Near(ledger.Required, 200 * 45 * Unit), $"premise: a 45-life anchor prices 9000; read {ledger.Required / Unit}");
        ledger.CreditBossFight(12_000, true);                                 // completes level 1 at the old price, anchors at level 2
        Require(ledger.Level == 2 && ledger.BossAnchorLevel == 2 && Near(ledger.Required, 12_000 * Unit / 0.15),
            $"the setting fight must land in level 2 and anchor there at 12000 / 0.15; level {ledger.Level}, anchor level {ledger.BossAnchorLevel}, bar {ledger.Required / Unit}");
        double Fill(double life, bool companion = true)
        {
            double before = ledger.Into, bar = ledger.Required;
            int level = ledger.Level;
            ledger.CreditBossFight(life, companion);
            Require(ledger.Level == level, "premise: the repeated fight must not complete the level");
            return (ledger.Into - before) / bar;
        }
        double again = Fill(12_000);
        Require(Math.Abs(again - 0.15) < Close, $"the same boss again at the next level must fill 15%; filled {again:P4}");
        double bar = ledger.Required, anchor = ledger.BossAnchorLife;
        Fill(6_000);
        Require(ledger.Required == bar && ledger.BossAnchorLife == anchor && ledger.BossAnchorLevel == 2, "a weaker boss must move neither the anchor nor the bar");
        while (ledger.Level < 4) ledger.CreditBossFight(12_000, true);
        Require(Near(ledger.Required, 12_000 * Unit / 0.15 * 1.05 * 1.05), $"premise: two levels on, the boss bar is 80000 x 1.05^2; read {ledger.Required / Unit}");
        ledger.CreditBossFight(40_000, true);
        Require(ledger.BossAnchorLife == 40_000 && ledger.BossAnchorLevel == ledger.Level, "a stronger boss must reset the anchor at the current level");
        double reset = Fill(40_000);
        Require(Math.Abs(reset - 0.15) < Close, $"the stronger boss again must fill 15% after its reset; filled {reset:P4}");
        double half = Fill(40_000, companion: false);
        Require(Math.Abs(half - 0.075) < Close, $"the player's fight earns half: 7.5%; filled {half:P4}");
        Console.WriteLine($"  same boss again {again:P2}, stronger boss again {reset:P2}, the player's {half:P2}");
    }

    private static void GrowthAndNeverLower()
    {
        Experience.DefaultEnemyLife = () => 14;
        Experience ledger = Fresh();
        ledger.CreditEnemyKill(45, true);
        double anchored = 200 * 45 * Unit;
        while (ledger.Level < 4) ledger.CreditWork(true);
        Require(ledger.EnemyAnchorLevel == 1 && Near(ledger.Required, anchored * Math.Pow(1.05, 3)),
            $"three levels past the anchor the bar must be 9000 x 1.05^3 = {anchored * Math.Pow(1.05, 3) / Unit:0.###}; read {ledger.Required / Unit:0.###}");
        double before = ledger.Required;
        ledger.CreditEnemyKill(46, true);                                     // 200 x 46 = 9200 is below 10418.6
        Require(ledger.EnemyAnchorLife == 46 && ledger.EnemyAnchorLevel == 4 && ledger.Required == before,
            $"a stronger enemy whose formula is lower must anchor and leave the bar where it was; anchor {ledger.EnemyAnchorLife}@{ledger.EnemyAnchorLevel}, bar {ledger.Required / Unit:0.###}");
        while (ledger.Level < 5) ledger.CreditWork(true);
        Require(ledger.Required == before, $"the next level must not drop below the level it replaces; {ledger.Required / Unit:0.###} against {before / Unit:0.###}");
        while (ledger.Level < 7) ledger.CreditWork(true);
        double restarted = 200 * 46 * Unit * Math.Pow(1.05, 3);
        Require(Near(ledger.Required, restarted), $"three levels past the new anchor its own count must price 9200 x 1.05^3 = {restarted / Unit:0.###}; read {ledger.Required / Unit:0.###}");
        Console.WriteLine($"  level 4 bar {before / Unit:0.#}, held at level 5, level 7 bar {ledger.Required / Unit:0.#} from the restarted count");
    }

    private static void MultiBodyFightsCreditOnce()
    {
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;

        Experience ledger = Fresh();
        NPC retinazer = Spawn(50, NPCID.Retinazer, normal), spazmatism = Spawn(51, NPCID.Spazmatism, normal);
        long whole = retinazer.lifeMax + spazmatism.lifeMax;
        Credit.Sweep(1);
        Require(Credit.FightBodies == 2, $"the Twins must be one fight of two bodies; bodies {Credit.FightBodies}");
        StrikeWithoutHitEffect(retinazer, Striker.Player);
        Require(!retinazer.active, "premise: the game's death must end Retinazer");
        Credit.Sweep(2);
        Require(ledger.BossAnchorLife == 0 && ledger.Level == 1 && ledger.Into == 0, "one twin dead must credit nothing while the other lives");
        StrikeWithoutHitEffect(spazmatism, Striker.Companion);
        Require(!spazmatism.active, "premise: the game's death must end Spazmatism");
        Credit.Sweep(3);
        var reference = new Experience();
        reference.CreditBossFight(whole, true);
        Require(ledger.BossAnchorLife == whole && ledger.Level == reference.Level && Near(ledger.Into, reference.Into),
            $"the fight must be credited at the Twins' whole life {whole}, full for the companion's last blow; anchor {ledger.BossAnchorLife}, level {ledger.Level} against {reference.Level}");
        Credit.Sweep(4);
        Require(ledger.Level == reference.Level && Near(ledger.Into, reference.Into), "a fight must be credited once");

        ledger = Fresh();
        NPC eye = Spawn(52, NPCID.EyeofCthulhu, normal);
        Credit.Sweep(5);
        eye.active = false;                                                   // flew away at dawn: no death
        Credit.Sweep(6);
        Require(ledger.BossAnchorLife == 0 && ledger.Into == 0 && Credit.FightBodies == 0, "a boss that left alive is a despawn and credits nothing");

        ledger = Fresh();
        NPC head = Spawn(60, NPCID.TheDestroyer, normal), segment = Spawn(61, NPCID.TheDestroyerBody, normal);
        segment.realLife = head.whoAmI;
        segment.lifeMax = segment.life = head.lifeMax;
        Credit.Sweep(7);
        Require(Credit.FightBodies == 1, $"the Destroyer's segments hold no life of their own and are not bodies; bodies {Credit.FightBodies}");
        StrikeWithoutHitEffect(segment, Striker.Player);
        Require(!head.active, "premise: a killing strike on a segment ends the head");
        Credit.Sweep(8);
        var halfDestroyer = new Experience();
        halfDestroyer.CreditBossFight(head.lifeMax, false);
        Require(ledger.BossAnchorLife == head.lifeMax && ledger.EnemyAnchorLife == 0 && ledger.Level == halfDestroyer.Level && Near(ledger.Into, halfDestroyer.Into),
            $"the Destroyer must be one fight at its head's life {head.lifeMax}, half for the player's blow, never an enemy kill; boss {ledger.BossAnchorLife}, enemy {ledger.EnemyAnchorLife}");

        ledger = Fresh();
        NPC wyvern = Spawn(70, NPCID.WyvernHead, normal), tail = Spawn(71, NPCID.WyvernBody, normal);
        tail.realLife = wyvern.whoAmI;
        StrikeWithoutHitEffect(tail, Striker.Player);
        Require(!wyvern.active, "premise: a killing strike on a worm's segment ends its head");
        var worm = new Experience();
        worm.CreditEnemyKill(wyvern.lifeMax, false);
        Require(ledger.EnemyAnchorLife == wyvern.lifeMax && ledger.Level == worm.Level && Near(ledger.Into, worm.Into),
            $"a worm counts once at its head's life {wyvern.lifeMax}; anchor {ledger.EnemyAnchorLife}");
        Console.WriteLine($"  Twins credited once at {whole}, a despawned eye at nothing, Destroyer at {head.lifeMax}, a wyvern once at {wyvern.lifeMax}");
    }

    private static void FirstKillFillsAtTheOldPrice()
    {
        Experience.DefaultEnemyLife = () => 14;
        Experience ledger = Fresh();
        Require(ledger.NeededNow == 2800 && Near(ledger.Required, 2800 * Unit), $"premise: the default bar is 200 x 14; read {ledger.NeededNow}");
        ledger.CreditEnemyKill(10_000, true);
        double left = 10_000 - 2800 - 2940 - 3087;
        Require(ledger.Level == 4 && Near(ledger.Into, left * Unit), $"the kill must complete levels 1 to 3 at 2800, 2940 and 3087 and keep {left}; level {ledger.Level}, into {ledger.Into / Unit:0.###}");
        Require(ledger.EnemyAnchorLife == 10_000 && ledger.EnemyAnchorLevel == 4 && Near(ledger.Required, 200 * 10_000 * Unit),
            $"then anchor at level 4 and reprice to 200 x 10000; anchor {ledger.EnemyAnchorLife}@{ledger.EnemyAnchorLevel}, bar {ledger.Required / Unit:0.#}");
        Console.WriteLine($"  a 10000-life first kill made level {ledger.Level} with {ledger.IntoLevel} into a {ledger.NeededNow} bar");
    }

    private static void WorkMakesALevel()
    {
        Experience.DefaultEnemyLife = () => 14;
        // A fresh companion: the kill rows spawned into the NPC table, so whatever slot held the first one is not trusted.
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        var pick = new Item();
        pick.SetDefaults(ItemID.GoldPickaxe);
        var ore = new Point(50, 30);

        void PlaceOre()
        {
            Tile t = Main.tile[ore.X, ore.Y];
            t.ClearEverything();
            t.HasTile = true;
            t.TileType = TileID.Copper;
        }

        Experience ledger = Fresh();
        double bar = ledger.Required;
        PlaceOre();
        for (int swing = 0; swing < 400 && Main.tile[ore.X, ore.Y].HasTile; swing++)
        {
            companion.Miner.Tick();
            if (companion.Miner.Ready) companion.Miner.Swing(ore, pick);
        }
        Require(!Main.tile[ore.X, ore.Y].HasTile, "premise: the companion's native pickaxe must break the ore");
        Require(Near(ledger.Into, bar * 0.001), $"one companion ore break must fill a thousandth of the bar; filled {ledger.Into / bar:P4}");

        ledger = Fresh();
        PlaceOre();
        int selected = player.selectedItem;
        Item held = player.inventory[selected];
        player.inventory[selected] = pick;
        int animation = player.itemAnimation;
        (int x, int y) target = (Player.tileTargetX, Player.tileTargetY);
        // A headless process starts at the menu, where no tile edit is the player's; the break below happens in a world.
        bool menu = Main.gameMenu;
        try
        {
            Main.gameMenu = false;
            Player.tileTargetX = ore.X; Player.tileTargetY = ore.Y;
            player.itemAnimation = 0;
            Work.PlayerKilledTile(ore.X, ore.Y, TileID.Copper, fail: false, effectOnly: false);
            Require(ledger.Into == 0, "an ore broken while the player is not swinging (an explosion) must credit nothing");
            player.itemAnimation = 5;
            Work.PlayerKilledTile(ore.X, ore.Y, TileID.Copper, fail: true, effectOnly: false);
            Require(ledger.Into == 0, "a pickaxe hit that only cracks the ore must credit nothing");
            Player.tileTargetX = ore.X + 1;
            Work.PlayerKilledTile(ore.X, ore.Y, TileID.Copper, fail: false, effectOnly: false);
            Require(ledger.Into == 0, "an ore beside the one his cursor targets must credit nothing");
            Player.tileTargetX = ore.X;
            Work.PlayerKilledTile(ore.X, ore.Y, TileID.Copper, fail: false, effectOnly: false);
            Require(Near(ledger.Into, bar * 0.0005), $"the player's own ore break must fill half a thousandth; filled {ledger.Into / bar:P4}");
        }
        finally
        {
            player.inventory[selected] = held;
            player.itemAnimation = animation;
            Player.tileTargetX = target.x; Player.tileTargetY = target.y;
            Main.gameMenu = menu;
        }

        foreach ((bool companionWork, int breaks) in new[] { (true, 1000), (false, 2000) })
        {
            ledger = Fresh();
            double fixedBar = ledger.Required;
            for (int i = 1; i < breaks; i++) ledger.CreditWork(companionWork);
            Require(ledger.Level == 1, $"{breaks - 1} breaks must not yet make a level; filled {ledger.Into / fixedBar:P6}");
            ledger.CreditWork(companionWork);
            Require(ledger.Level == 2, $"the {breaks}th break must make the level at a fixed bar; level {ledger.Level}, filled {ledger.Into / fixedBar:P6}");
        }
        Console.WriteLine("  a native companion ore break filled 0.1% and the player's 0.05%; 1000 and 2000 breaks each made one level");
    }

    private static void ExcludedKillsCreditNothing()
    {
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;
        Experience ledger = Fresh();
        void Nothing(string what)
        {
            Credit.Sweep(9);
            Require(ledger.Level == 1 && ledger.Into == 0 && ledger.EnemyAnchorLife == 0 && ledger.BossAnchorLife == 0 && Credit.FightBodies == 0,
                $"{what} must credit nothing and move no anchor; level {ledger.Level}, into {ledger.Into / Unit}, enemy {ledger.EnemyAnchorLife}, boss {ledger.BossAnchorLife}, fight bodies {Credit.FightBodies}");
        }

        KillEnemy(NPCID.Bunny, normal, Striker.Companion, withHitEffect: false); Nothing("a critter");
        KillEnemy(NPCID.Guide, normal, Striker.Companion, withHitEffect: false); Nothing("a town NPC");
        NPC statue = Spawn(41, NPCID.Zombie, normal);
        statue.SpawnedFromStatue = true;
        Strike(statue, Striker.Companion);
        Require(!statue.active, "premise: the statue zombie must die");
        Nothing("a statue spawn");
        NPC dummy = Spawn(42, NPCID.TargetDummy, normal);
        StrikeWithoutHitEffect(dummy, Striker.Companion);
        Nothing("a target dummy");
        KillEnemy(NPCID.PrimeSaw, normal, Striker.Companion, withHitEffect: false); Nothing("a Prime arm, a boss part without the boss flag");
        NPC eye = Spawn(43, NPCID.EyeofCthulhu, normal);
        NPC spawned = Spawn(44, NPCID.Zombie, normal, new EntitySource_Parent(eye));
        Strike(spawned, Striker.Companion);
        Require(!spawned.active, "premise: the boss's spawn must die");
        eye.active = false;
        Credit.Sweep(8);
        Nothing("an enemy a boss's AI spawned");
        KillEnemy(NPCID.Zombie, normal, Striker.Other); Nothing("a zombie killed by a trap");
        NPC tough = Spawn(45, NPCID.Zombie, normal);
        tough.lifeMax = tough.life = 100_000_000;
        Strike(tough, Striker.Companion);
        Require(tough.active, "premise: a zombie with a hundred million life must survive the strike");
        Nothing("a companion strike that did not kill");
        Console.WriteLine("  critter, town NPC, statue zombie, dummy, Prime saw, a boss's spawn, a trap kill and a strike that did not kill all credited nothing");
    }

    /// <summary>
    /// Whose projectile a strike was. Every companion shot is owned by the local player, so a rule that read ownership would
    /// credit the player for the companion's kills; the arsenal's registration is what says the companion fired it.
    /// </summary>
    private static void StrikersAreNamedByTheShot()
    {
        Projectile Shot(int slot, bool friendly, bool trap = false, bool npcProj = false)
        {
            var projectile = new Projectile { whoAmI = slot, active = true, friendly = friendly, hostile = !friendly, owner = Main.myPlayer, trap = trap, npcProj = npcProj };
            Main.projectile[slot] = projectile;
            live::AICompanion.Companion.Weapons.TrackLandedHits.Forget(slot);
            return projectile;
        }
        Projectile companion = Shot(10, friendly: true);
        live::AICompanion.Companion.Weapons.TrackLandedHits.Register(10, null, ItemID.WoodenBow);
        Require(Credit.StrikerOf(companion) == Striker.Companion, "a shot the arsenal registered is the companion's, though the player owns it");
        Require(Credit.StrikerOf(Shot(11, friendly: true)) == Striker.Player, "the player's own friendly projectile is his");
        Require(Credit.StrikerOf(Shot(12, friendly: true, trap: true)) == Striker.Other, "a trap's dart is nobody's");
        Require(Credit.StrikerOf(Shot(13, friendly: true, npcProj: true)) == Striker.Other, "a town NPC's shot is nobody's");
        Require(Credit.StrikerOf(Shot(14, friendly: false)) == Striker.Other, "a hostile projectile is nobody's");
        live::AICompanion.Companion.Weapons.TrackLandedHits.Clear();
        Console.WriteLine("  a registered shot is the companion's, an unregistered friendly one the player's, a trap's, a town NPC's and a hostile's nobody's");
    }

    private static void SaveAndLoadRoundTrip()
    {
        Experience.DefaultEnemyLife = () => 14;
        Experience ledger = Fresh();
        ledger.CreditEnemyKill(45, true);
        for (int i = 0; i < 1000; i++) ledger.CreditWork(true);             // level 2, so the boss anchors above level 1
        ledger.CreditBossFight(12_000, false);
        for (int i = 0; i < 37; i++) ledger.CreditWork(true);
        Require(ledger.Level > 1 && ledger.Into > 0 && ledger.EnemyAnchorLife > 0 && ledger.BossAnchorLife > 0 && ledger.BossAnchorLevel > 1,
            "premise: every field must hold a non-default value before saving");

        // Through the game's own binary serialisation, then the character's own load under its "experience" key. The whole
        // character's SaveData is not called: it writes a boolean, whose serialiser only mod loading registers, the reason the
        // other save fixtures round-trip their own compound the same way.
        using var stream = new MemoryStream();
        TagIO.ToStream(ledger.Save(), stream);
        stream.Position = 0;
        var loaded = new CompanionPlayer();
        loaded.LoadData(new TagCompound { ["experience"] = TagIO.FromStream(stream) });
        Experience back = loaded.Experience;
        Require(back.Level == ledger.Level && back.Into == ledger.Into && back.Required == ledger.Required
            && back.EnemyAnchorLife == ledger.EnemyAnchorLife && back.EnemyAnchorLevel == ledger.EnemyAnchorLevel
            && back.BossAnchorLife == ledger.BossAnchorLife && back.BossAnchorLevel == ledger.BossAnchorLevel,
            $"every field must round-trip through the character's save; saved {Describe(ledger)}, loaded {Describe(back)}");

        var old = new TagCompound { ["experience"] = new TagCompound { ["total"] = 4500 } };
        var placeholder = new CompanionPlayer();
        placeholder.LoadData(old);
        Experience reset = placeholder.Experience;
        Require(reset.Level == 1 && reset.Into == 0 && reset.EnemyAnchorLife == 0 && reset.BossAnchorLife == 0
            && reset.Save().GetDouble("required") == 0 && reset.NeededNow == 2800, $"a placeholder save must load as a fresh level 1 priced from the default; loaded {Describe(reset)}, bar {reset.NeededNow}");
        Console.WriteLine($"  round-tripped {Describe(back)}; a placeholder total of 4500 loaded at level 1");
    }

    private static string Describe(Experience e)
        => $"level {e.Level}, into {e.Into / Unit:0.###}, bar {e.Required / Unit:0.###}, enemy {e.EnemyAnchorLife}@{e.EnemyAnchorLevel}, boss {e.BossAnchorLife}@{e.BossAnchorLevel}";

    private static bool Near(double a, double b) => Math.Abs(a - b) <= Close * Math.Max(1, Math.Abs(b));

    private readonly struct NPCProbe
    {
        public readonly int LifeMax;
        public NPCProbe(int type, GameModeData mode)
        {
            var npc = new NPC();
            npc.SetDefaults(type, new NPCSpawnParams { gameModeData = mode });
            LifeMax = npc.lifeMax;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
