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
            failed += RunOneRow.Case("the Eater of Worlds credits every segment's life whichever end dies first", EaterOfWorldsIsOrderFree, "experience");
            failed += RunOneRow.Case("a fight's life is every part that has to die: the Brain's creepers and Golem's head and fists count, a respawning probe and an unhurtable hook do not", EveryPartThatMustDieCounts, "experience");
            failed += RunOneRow.Case("a boss nobody finished goes to whoever of the companion or the player struck it last, and to nobody if neither did", ABossNobodyFinishedGoesToTheLastStriker, "experience");
            failed += RunOneRow.Case("two bodies leaving on the same tick credit the latest striker, whichever body the fight saw first", TwoBodiesLeavingOnOneTick, "experience");
            failed += RunOneRow.Case("a boss whose death refused its loot and whose slot was refilled before the sweep still ends a credited fight", ARefilledSlotIsStillADeath, "experience");
            failed += RunOneRow.Case("the player's direct strikes (dash, stomp, touch) credit him, and never take a strike the companion's swing or shot already took", PlayerDirectStrikesAreHis, "experience");
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

    /// <summary>An empty NPC table, at the top of every fight row: a boss or a part left standing by an earlier row — one whose
    /// assertion aborted it halfway, above all — would hold the next row's fight open or join it, and the next row would then
    /// fail for the earlier row's reason rather than its own.</summary>
    private static void ClearArena()
    {
        for (int i = 0; i < Main.maxNPCs; i++) Main.npc[i] = new NPC { whoAmI = i };
        Credit.Reset();
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
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;

        Experience ledger = Fresh();
        NPC retinazer = Spawn(50, NPCID.Retinazer, normal), spazmatism = Spawn(51, NPCID.Spazmatism, normal);
        long whole = retinazer.lifeMax + spazmatism.lifeMax;
        Credit.Sweep(1);
        Require(Credit.FightMembers == 2, $"the Twins must be one fight of two bodies; bodies {Credit.FightMembers}");
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
        Require(ledger.BossAnchorLife == 0 && ledger.Into == 0 && Credit.FightMembers == 0, "a boss that left alive is a despawn and credits nothing");

        ledger = Fresh();
        NPC head = Spawn(60, NPCID.TheDestroyer, normal), segment = Spawn(61, NPCID.TheDestroyerBody, normal);
        segment.realLife = head.whoAmI;
        segment.lifeMax = segment.life = head.lifeMax;
        Credit.Sweep(7);
        Require(Credit.FightMembers == 1, $"the Destroyer's segments hold no life of their own and are not bodies; bodies {Credit.FightMembers}");
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

    /// <summary>A death with nobody's strike around it: the life taken from outside a strike, a debuff's last tick, and the
    /// game's own checkDead under the same client allowance the strikes use, so loot returns and the NPC still deactivates.</summary>
    private static void DebuffDeath(NPC npc)
    {
        int netMode = Main.netMode;
        Main.netMode = 1;
        try { npc.life = 0; npc.checkDead(); }
        finally { Main.netMode = netMode; }
        Require(!npc.active, $"premise: a debuff's last tick must end NPC type {npc.type}");
    }

    /// <summary>What a fresh ledger reads after crediting one whole fight, for comparing a fight's credit by hand.</summary>
    private static Experience Reference(long wholeLife, bool byCompanion)
    {
        var reference = new Experience();
        reference.CreditBossFight(wholeLife, byCompanion);
        return reference;
    }

    private static bool Matches(Experience ledger, Experience reference)
        => ledger.BossAnchorLife == reference.BossAnchorLife && ledger.Level == reference.Level && Near(ledger.Into, reference.Into);

    /// <summary>
    /// The worm of twenty segments each holding its own 150 life. The game makes the segment behind a dying one a head by
    /// changing its type in place, with no spawn, and only a head carries the boss set, so a fight that counted only boss
    /// bodies paid 3000 when the head died first and every next head joined, and 150 when the tail died first and the one
    /// head was all it ever saw. Both orders must credit every segment.
    /// </summary>
    private static void EaterOfWorldsIsOrderFree()
    {
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;
        const int Segments = 20;
        var paid = new List<string>();
        foreach (bool headFirst in new[] { true, false })
        {
            Experience ledger = Fresh();
            NPC head = Spawn(100, NPCID.EaterofWorldsHead, normal);
            var worm = new List<NPC> { head };
            for (int i = 1; i < Segments; i++)
                worm.Add(Spawn(100 + i, i == Segments - 1 ? NPCID.EaterofWorldsTail : NPCID.EaterofWorldsBody, normal, new EntitySource_Parent(head)));
            long whole = worm.Sum(s => (long)s.lifeMax);
            Credit.Sweep(20);
            ulong tick = 21;
            for (int k = 0; k < Segments; k++)
            {
                int index = headFirst ? k : Segments - 1 - k;
                StrikeWithoutHitEffect(worm[index], Striker.Companion);
                Require(!worm[index].active, $"premise: segment {index} must die");
                if (headFirst && index + 1 < Segments) worm[index + 1].type = NPCID.EaterofWorldsHead;  // the next segment leads
                Credit.Sweep(tick++);
            }
            Experience reference = Reference(whole, true);
            paid.Add($"{(headFirst ? "head first" : "tail first")} {ledger.BossAnchorLife}");
            Require(Matches(ledger, reference),
                $"the worm must credit its whole life {whole} in either order; {string.Join(", ", paid)}");
        }
        Console.WriteLine($"  Eater of Worlds of {Segments} segments: {string.Join(", ", paid)}");
    }

    private static void EveryPartThatMustDieCounts()
    {
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;
        var lines = new List<string>();

        Experience ledger = Fresh();
        NPC brain = Spawn(110, NPCID.BrainofCthulhu, normal);
        var creepers = Enumerable.Range(0, 20).Select(i => Spawn(111 + i, NPCID.Creeper, normal, new EntitySource_Parent(brain))).ToList();
        long brainWhole = brain.lifeMax + creepers.Sum(c => (long)c.lifeMax);
        Credit.Sweep(30);
        Require(brain.dontTakeDamage, "premise: the game makes the brain unhurtable while its creepers live");
        foreach (NPC creeper in creepers) StrikeWithoutHitEffect(creeper, Striker.Player);
        Credit.Sweep(31);
        brain.dontTakeDamage = false;                                         // what the brain's AI does once its creepers are gone
        StrikeWithoutHitEffect(brain, Striker.Companion);
        Credit.Sweep(32);
        Require(Matches(ledger, Reference(brainWhole, true)),
            $"the Brain of Cthulhu must credit the brain and its creepers, {brainWhole}; anchor {ledger.BossAnchorLife}");
        lines.Add($"Brain of Cthulhu {ledger.BossAnchorLife}");

        ledger = Fresh();
        NPC golem = Spawn(140, NPCID.Golem, normal);
        var parts = new[] { NPCID.GolemHead, NPCID.GolemFistLeft, NPCID.GolemFistRight }
            .Select((type, i) => Spawn(141 + i, type, normal, new EntitySource_Parent(golem))).ToList();
        foreach (NPC part in parts) part.dontTakeDamage = false;             // the body's AI lets them be hurt; nothing headless runs it
        long golemWhole = golem.lifeMax + parts.Sum(p => (long)p.lifeMax);
        Credit.Sweep(40);
        foreach (NPC part in parts) StrikeWithoutHitEffect(part, Striker.Companion);
        StrikeWithoutHitEffect(golem, Striker.Companion);
        Credit.Sweep(41);
        Require(Matches(ledger, Reference(golemWhole, true)),
            $"Golem must credit its body, head and fists, {golemWhole}; anchor {ledger.BossAnchorLife}");
        lines.Add($"Golem {ledger.BossAnchorLife} (body {golem.lifeMax}, head {parts[0].lifeMax})");

        ledger = Fresh();
        NPC destroyer = Spawn(150, NPCID.TheDestroyer, normal);
        NPC probe = Spawn(151, NPCID.Probe, normal, new EntitySource_Parent(destroyer));
        Credit.Sweep(50);
        StrikeWithoutHitEffect(probe, Striker.Companion);
        Credit.Sweep(51);
        NPC again = Spawn(151, NPCID.Probe, normal, new EntitySource_Parent(destroyer));   // launched again after one died
        Credit.Sweep(52);
        StrikeWithoutHitEffect(again, Striker.Companion);
        StrikeWithoutHitEffect(destroyer, Striker.Companion);
        Credit.Sweep(53);
        Require(Matches(ledger, Reference(destroyer.lifeMax, true)),
            $"a probe launched again after one died is endless and must add nothing to the Destroyer's {destroyer.lifeMax}; anchor {ledger.BossAnchorLife}");
        lines.Add($"Destroyer with probes {ledger.BossAnchorLife}");

        ledger = Fresh();
        NPC plantera = Spawn(160, NPCID.Plantera, normal);
        NPC hook = Spawn(161, NPCID.PlanterasHook, normal, new EntitySource_Parent(plantera));
        hook.dontTakeDamage = true;
        Credit.Sweep(60);
        StrikeWithoutHitEffect(plantera, Striker.Companion);
        Credit.Sweep(61);
        Require(Matches(ledger, Reference(plantera.lifeMax, true)),
            $"a hook nothing can hurt does not have to die and must add nothing to Plantera's {plantera.lifeMax}; anchor {ledger.BossAnchorLife}");
        lines.Add($"Plantera with an unhurtable hook {ledger.BossAnchorLife}");
        Console.WriteLine("  " + string.Join("; ", lines));
    }

    private static void ABossNobodyFinishedGoesToTheLastStriker()
    {
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;

        Experience ledger = Fresh();
        NPC eye = Spawn(52, NPCID.EyeofCthulhu, normal);
        eye.lifeMax = eye.life = 100_000_000;
        Credit.Sweep(70);
        Strike(eye, Striker.Player);
        Require(eye.active, "premise: the player's strike must not kill the eye");
        DebuffDeath(eye);
        Credit.Sweep(71);
        Require(Matches(ledger, Reference(eye.lifeMax, false)), $"a boss the player struck last and a debuff finished pays him half; anchor {ledger.BossAnchorLife}, into {ledger.Into / Unit}");

        ledger = Fresh();
        eye = Spawn(52, NPCID.EyeofCthulhu, normal);
        Credit.Sweep(72);
        DebuffDeath(eye);
        Credit.Sweep(73);
        Require(ledger.BossAnchorLife == 0 && ledger.Into == 0 && ledger.Level == 1, $"a boss neither ever struck pays nobody; anchor {ledger.BossAnchorLife}, into {ledger.Into / Unit}");

        ledger = Fresh();
        eye = Spawn(52, NPCID.EyeofCthulhu, normal);
        eye.lifeMax = eye.life = 100_000_000;
        Credit.Sweep(74);
        Strike(eye, Striker.Companion);
        eye.life = 1;
        StrikeWithoutHitEffect(eye, Striker.Other);                           // a trap lands the last blow
        Require(!eye.active, "premise: the trap's blow must end the eye");
        Credit.Sweep(75);
        Require(Matches(ledger, Reference(eye.lifeMax, true)), $"a trap's last blow after the companion's strike pays the companion in full; anchor {ledger.BossAnchorLife}");
        Console.WriteLine("  a debuff after the player's strike paid him half, a boss nobody struck paid nothing, a trap after the companion paid the companion");
    }

    /// <summary>
    /// The Twins dying in one tick, Spazmatism to the companion first and Retinazer to the player after it. The fight joined
    /// Retinazer first, so a rule that picks a body by the order the fight saw them rather than by the order of the strikes
    /// hands the credit to the wrong one; the last strike was the player's, and the credit is his half.
    /// </summary>
    private static void TwoBodiesLeavingOnOneTick()
    {
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;
        foreach ((Striker first, Striker second) in new[] { (Striker.Companion, Striker.Player), (Striker.Player, Striker.Companion) })
        {
            Experience ledger = Fresh();
            NPC retinazer = Spawn(50, NPCID.Retinazer, normal), spazmatism = Spawn(51, NPCID.Spazmatism, normal);
            long whole = retinazer.lifeMax + spazmatism.lifeMax;
            Credit.Sweep(80);
            StrikeWithoutHitEffect(spazmatism, first);
            StrikeWithoutHitEffect(retinazer, second);
            Credit.Sweep(81);
            Require(Matches(ledger, Reference(whole, second == Striker.Companion)),
                $"both twins leaving on one tick must credit the latest striker, {second}; anchor {ledger.BossAnchorLife}, into {ledger.Into / Unit}, level {ledger.Level}");
        }
        Console.WriteLine("  the Twins dying on one tick credited the later striker in both orders");
    }

    /// <summary>
    /// A boss dies to a debuff after the player's strike. A modded boss's PreKill refusing leaves exactly this state — the
    /// NPC deactivated with no life and no OnKill — and before the next sweep the slot is handed to a new spawn. A fight
    /// that read the slot saw a different NPC and dropped the boss as a despawn; the boss's own object still says it died.
    /// </summary>
    private static void ARefilledSlotIsStillADeath()
    {
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;
        Experience ledger = Fresh();
        NPC eye = Spawn(52, NPCID.EyeofCthulhu, normal);
        eye.lifeMax = eye.life = 100_000_000;
        Credit.Sweep(90);
        Strike(eye, Striker.Player);
        DebuffDeath(eye);
        NPC refill = Spawn(52, NPCID.Zombie, normal);
        Require(Main.npc[52] == refill && refill.active, "premise: the boss's slot must hold a new NPC before the sweep");
        Credit.Sweep(91);
        Require(Matches(ledger, Reference(eye.lifeMax, false)) && Credit.FightMembers == 0,
            $"a boss whose slot was refilled before the sweep must still end a credited fight; anchor {ledger.BossAnchorLife}, into {ledger.Into / Unit}, members {Credit.FightMembers}");
        Console.WriteLine("  a boss whose slot a zombie took before the sweep was credited to the player's half");
    }

    /// <summary>
    /// The player's own hooks see every strike he lands. These drive the calls in the order tModLoader makes them — the
    /// NPC's projectile hook before the player's, the companion's own swing bracket around the game's strike — because the
    /// harness loads no hooks. A direct strike (a dash, a stomp, a touch) is his; the companion's swing and the companion's
    /// shot pass through the same player hooks, owned by the local player, and stay the companion's.
    /// </summary>
    private static void PlayerDirectStrikesAreHis()
    {
        ClearArena();
        Main.GameMode = GameModeID.Normal;
        Experience.DefaultEnemyLife = () => 14;
        GameModeData normal = GameModeData.NormalMode;
        Player player = Main.player[Main.myPlayer];
        int zombieLife = new NPCProbe(NPCID.Zombie, normal).LifeMax;

        void GameStrike(NPC target)
        {
            bool paused = Main.gamePaused;
            int netMode = Main.netMode;
            Main.gamePaused = true;
            Main.netMode = 1;
            try { target.StrikeNPC(target.CalculateHitInfo(10_000_000, 1)); }
            finally { Main.gamePaused = paused; Main.netMode = netMode; }
        }

        Experience ledger = Fresh();
        NPC dashed = Spawn(40, NPCID.Zombie, normal);
        Credit.BeforePlayerStrike(player, dashed);                            // PlayerLoader.ModifyHitNPC, from ApplyDamageToNPC
        GameStrike(dashed);
        Credit.AfterStrike(dashed);                                          // PlayerLoader.OnHitNPC, from StrikeNPCDirect
        var half = new Experience();
        half.CreditEnemyKill(zombieLife, false);
        Require(!dashed.active && ledger.EnemyAnchorLife == zombieLife && Near(ledger.Into, half.Into),
            $"a zombie the player dashed through must earn him half its life; into {ledger.Into / Unit}, anchor {ledger.EnemyAnchorLife}");

        var full = new Experience();
        full.CreditEnemyKill(zombieLife, true);
        ledger = Fresh();
        NPC swung = Spawn(40, NPCID.Zombie, normal);
        Credit.BeforeStrike(swung, Striker.Companion);                        // ItemWeapon's own bracket
        Credit.BeforePlayerStrike(player, swung);                            // the player hook inside ApplyDamageToNPC
        GameStrike(swung);
        Credit.AfterStrike(swung);                                           // the player hook inside StrikeNPCDirect
        Credit.AfterStrike(swung);                                           // ItemWeapon's own bracket
        Require(!swung.active && Near(ledger.Into, full.Into),
            $"the companion's swing passes through the player's hooks and must stay the companion's; into {ledger.Into / Unit} against {full.Into / Unit}");

        ledger = Fresh();
        NPC shot = Spawn(40, NPCID.Zombie, normal);
        var projectile = new Projectile { whoAmI = 20, active = true, friendly = true, owner = Main.myPlayer };
        Main.projectile[20] = projectile;
        live::AICompanion.Companion.Weapons.TrackLandedHits.Register(20, null, ItemID.WoodenBow);
        try
        {
            Credit.BeforeStrike(shot, Credit.StrikerOf(projectile));          // NPCLoader.ModifyHitByProjectile
            Credit.BeforePlayerStrike(player, shot);                         // PlayerLoader.ModifyHitNPCWithProj, which calls ModifyHitNPC
            GameStrike(shot);
            Credit.AfterStrike(shot);
            Credit.AfterStrike(shot);
        }
        finally { live::AICompanion.Companion.Weapons.TrackLandedHits.Clear(); }
        Require(!shot.active && Near(ledger.Into, full.Into),
            $"the companion's shot passes through the player's hooks and must stay the companion's; into {ledger.Into / Unit} against {full.Into / Unit}");

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Type hooks = typeof(live::AICompanion.Companion.Progression.ObservePlayerStrikesForExperience);
        Require(hooks.GetMethod("ModifyHitNPC", Declared) != null && hooks.GetMethod("OnHitNPC", Declared) != null,
            "the player's strike hooks must be overridden, or no direct strike reaches the ledger in the game");
        Console.WriteLine("  a dash kill paid the player half; the companion's swing and shot through the player's hooks paid the companion");
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
            Require(ledger.Level == 1 && ledger.Into == 0 && ledger.EnemyAnchorLife == 0 && ledger.BossAnchorLife == 0 && Credit.FightMembers == 0,
                $"{what} must credit nothing and move no anchor; level {ledger.Level}, into {ledger.Into / Unit}, enemy {ledger.EnemyAnchorLife}, boss {ledger.BossAnchorLife}, fight bodies {Credit.FightMembers}");
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
