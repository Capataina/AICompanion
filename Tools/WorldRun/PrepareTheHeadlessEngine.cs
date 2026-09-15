extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using live::AICompanion.Companion.CharacterBody;

/// <summary>
/// Everything that must be true before the first tick of a world run, in the order it must be true.
///
/// The fixture suites each build their own scene from a handful of tiles and a companion, and the
/// helpers that do it are spread across <c>EngineReplay</c>. This is the same job for a different
/// world: real terrain that is already loaded, a light engine that has to be driven, and a body
/// that has to be dropped into a place the world actually has. It is a separate copy rather than a
/// call into <c>EngineReplay</c> because the two projects do not reference one another, and the
/// duplication is named in this folder's guide as something the kit should eventually own once.
///
/// The ordering that is not negotiable: the world is loaded first, because the tile tables and the
/// tilemap it builds are what everything else reads; the companion is attached second, because its
/// navigation grid binds to the loaded world; and the light engine is cycled last, because its
/// first readable frame is four calls away and a brain that samples light before then is reading an
/// empty map rather than a dark world.
/// </summary>
internal static class PrepareTheHeadlessEngine
{
    /// <summary>
    /// How many <c>ProcessArea</c> calls the light engine needs before any brightness it reports
    /// means anything.
    ///
    /// <c>LightingEngine.ProcessArea</c> advances one of four phases per call — minimap export,
    /// scene metrics, tile scan, then blur and present — so a full cycle is four calls and only the
    /// last of them makes the scanned light readable. Measured on a real world: brightness at a
    /// surface tile reads 0.000, 0.000, 0.000, then 0.686.
    /// </summary>
    public const int LightPhasesPerCycle = 4;

    /// <summary>
    /// Attaches a real companion to the loaded world, at a position the caller has chosen from the
    /// world rather than from a fixture's imagination.
    ///
    /// This mirrors <c>VerifyCompanionLifecycle.Create</c>, whose comments explain each step, with
    /// two differences that matter here. It never allocates <see cref="Main.tile"/>, because the
    /// world is already in it. And the navigation grid, terrain revision and edge cache are bound
    /// or cleared *after* the world is loaded, because a grid built against an empty tilemap
    /// answers every question about a world it has never seen.
    /// </summary>
    /// <summary>
    /// Every per-slot entity array the engine dereferences without checking for null, filled with
    /// fresh inactive instances.
    ///
    /// The authority is <c>Main.Initialize_Entities</c>, which is what a launched game runs and
    /// which nothing here runs: it allocates the arrays *and* an object in every slot, and the
    /// engine's own code then reads <c>Main.gore[i].active</c> or <c>Main.dust[i].type</c> with no
    /// guard, because in a real process there is always something there. Any slot left null is a
    /// null reference thrown from deep inside a game path, thousands of ticks into a run, the first
    /// time a behaviour reaches an effect nobody thought about.
    ///
    /// That is not hypothetical, and how the list was arrived at is the part worth keeping. Five of
    /// these seven were filled one at a time as each crash was met during construction, which makes
    /// a list only as complete as the code paths that happened to run. Gore was found by the first
    /// whole-capture run — chopping a tree reaches
    /// <c>WorldGen.KillTile → ShakeTree → TreeGrowFX → Gore.NewGore</c>, which walks all 601 slots
    /// looking for a free one, and no window short enough for the verify script had ever let the
    /// companion finish a tree. Player was found by the run after that, and it is the more useful
    /// of the two: it had been excluded on purpose and with a reason — the stand-in is built by
    /// hand and 256 real players are expensive — and the reasoning was simply wrong, because
    /// <c>Player.FindClosest</c> walks the whole array and <c>WorldGen.KillTile_DropBait</c> reaches
    /// it on any tile break. Reasoning about which slots the engine touches is what failed twice,
    /// so the method ends by walking every array and naming any null slot rather than waiting for a
    /// stack trace to.
    ///
    /// The fill is a reset rather than a top-up, because it runs once per attached companion and an
    /// excursion that inherited the last one's live projectiles would not be the run it claims. The
    /// players are the exception and are topped up, for the reason given at that line.
    /// </summary>
    private static void FillEveryEntitySlotTheEngineDereferences()
    {
        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] = new Projectile { whoAmI = i, active = false };
        for (int i = 0; i < Main.item.Length; i++) Main.item[i] = new Item { whoAmI = i, active = false };
        for (int i = 0; i < Main.dust.Length; i++) Main.dust[i] = new Dust { dustIndex = i, active = false };
        for (int i = 0; i < Main.gore.Length; i++) Main.gore[i] = new Gore { active = true };
        // Every slot taken, so a native strike's damage popup finds none free and returns rather
        // than measuring text with fonts nothing here loads.
        for (int i = 0; i < Main.combatText.Length; i++) Main.combatText[i] = new CombatText { active = true };
        // All 256 players exist because Player.FindClosest walks the whole array and reads .active
        // off every element, which WorldGen.KillTile_DropBait reaches on any tile break that could
        // drop bait — slot zero is then replaced by the stand-in the caller builds. These are a
        // top-up rather than a reset, unlike everything above: nothing in this host ever makes a
        // second player active, so an inactive slot cannot carry state into a later run, and a
        // Player is an expensive object to allocate 256 of on every excursion.
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player { whoAmI = i, active = false };

        foreach ((string name, System.Collections.IList slots) in new (string, System.Collections.IList)[]
                 { ("npc", Main.npc), ("projectile", Main.projectile), ("item", Main.item),
                   ("dust", Main.dust), ("gore", Main.gore), ("combatText", Main.combatText),
                   ("player", Main.player) })
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] == null)
                    throw new InvalidOperationException($"Main.{name}[{i}] is null after the headless fill; "
                        + "the engine dereferences these slots without a guard, so a run would throw from inside a game path");
    }

    public static CompanionNPC AttachCompanion(Vector2 centre, Vector2 playerFeet)
    {
        Main.myPlayer = 0;
        FillEveryEntitySlotTheEngineDereferences();
        foreach (int item in new[] { ItemID.WoodenBow, ItemID.WoodenArrow, ItemID.ThrowingKnife, ItemID.CopperPickaxe, ItemID.CopperAxe,
            ItemID.CopperBroadsword, ItemID.WandofSparking, ItemID.FlintlockPistol, ItemID.MusketBall, ItemID.WoodYoyo, ItemID.GoldPickaxe })
        {
            var sample = new Item();
            sample.SetDefaults(item);
            ContentSamples.ItemsByType[item] = sample;
        }
        foreach (int type in new[] { ProjectileID.WoodenArrowFriendly, ProjectileID.ThrowingKnife, ProjectileID.Bullet, ProjectileID.WandOfSparkingSpark, ProjectileID.WoodYoyo })
        {
            var sample = new Projectile();
            sample.SetDefaults(type);
            ContentSamples.ProjectilesByType[type] = sample;
        }

        Main.player[0] = new Player { active = true, dead = false, statLifeMax2 = 100 };
        Main.player[0].Bottom = playerFeet;
        var companionPlayer = new live::AICompanion.Companion.PlayerIntegration.CompanionPlayer();
        // One template registered, as the loader does. A fresh template per instance makes
        // ContentInstance<T>.Instance null once more than one exists.
        if (ModContent.GetInstance<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>() == null)
            ContentInstance.Register(companionPlayer);
        typeof(ModPlayer).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(companionPlayer, Main.player[0]);
        typeof(Player).GetField("modPlayers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Main.player[0], new ModPlayer[] { companionPlayer });
        // The gear the world run's companion holds: the arsenal and the tools read their weapons and
        // power from these slots, so an empty gear would run a whole world with nothing in its hands.
        companionPlayer.Gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        companionPlayer.Gear.Slots[1].SetDefaults(ItemID.ThrowingKnife);
        companionPlayer.Gear.Slots[2].SetDefaults(ItemID.CopperPickaxe);
        companionPlayer.Gear.Slots[3].SetDefaults(ItemID.CopperAxe);

        // No recorder is attached and none is started. That is deliberate rather than an omission:
        // a world run reports through ledger rows, and a recorder left running would drop a
        // synthetic session into Telemetry/ beside the real captures, where nothing in the file
        // would tell a later reader that no one ever played it.
        var companion = new CompanionNPC();
        var npc = new NPC();
        typeof(ModNPC).GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(companion, npc);
        // Both directions of the attachment. Entity alone lets direct AI calls work while native
        // lethal damage bypasses the companion's own death handling.
        typeof(NPC).GetProperty("ModNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(npc, companion);
        companion.SetDefaults();
        npc.Center = centre;
        npc.velocity = Vector2.Zero;
        // First-tick logging wants loader services this host does not have.
        Set(companion, "loggedFirstTick", true);

        ForgetEverythingLearnedAboutTheWorld();
        return companion;
    }

    /// <summary>
    /// Returns every process-wide thing the brain remembers between ticks to the state a freshly
    /// entered world leaves it in.
    ///
    /// This is the reset the plan's kit asks for, and it is the difference between two passes of a
    /// determinism check and one pass run twice. Route memory, the terrain revision and the edge
    /// cache are all static and all survive a companion being thrown away, so a second pass that
    /// skipped this would start from everything the first pass learned and agree with it for
    /// reasons that have nothing to do with the run being deterministic.
    ///
    /// The terrain log is cleared through the mod's own world-load path rather than by reaching into
    /// its fields, so it clears whatever that path clears today; the world the core reads is rebound
    /// to the loaded tiles, the clearance field forgets every chunk it built, and the search's world
    /// override — a headless tool's hook, never set here — is cleared in case a caller left it.
    /// </summary>
    public static void ForgetEverythingLearnedAboutTheWorld()
    {
        var world = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        world.OnWorldLoad();
        world.LoadWorldData(new Terraria.ModLoader.IO.TagCompound());
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World =
            new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch.WorldOverride = null;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.ClearanceField.Shared.Invalidate();
    }

    /// <summary>
    /// Pins every source of randomness a run can reach, so that two runs of one route are comparable
    /// at all.
    ///
    /// Two of them, and the second is the one nobody would look for. The game's shared generator is
    /// the obvious one. The other lives inside the light scanner: its per-tile random is derived
    /// from an instance field seeded with <c>FastRandom.CreateWithRandomSeed()</c>, which is a fresh
    /// <c>Guid</c> per process — so two runs in two processes light the same world slightly
    /// differently, and a lighting decision sitting near its threshold would flip for reasons no
    /// reader could ever trace. Pinned by reflection because the field is private and there is no
    /// other way to reach it.
    /// </summary>
    public static void PinEveryRandomSource(int seed)
    {
        Main.rand = new Terraria.Utilities.UnifiedRandom(seed);

        object engine = typeof(Lighting).GetField("NewEngine", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
        FieldInfo scannerField = engine.GetType().GetField("_tileScanner", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("LightingEngine._tileScanner is gone; the light scan's seed can no longer be pinned and two runs would light the world differently");
        object scanner = scannerField.GetValue(engine)!;
        FieldInfo randomField = scanner.GetType().GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("TileLightScanner._random is gone; the light scan's seed can no longer be pinned");
        randomField.SetValue(scanner, new Terraria.Utilities.FastRandom((ulong)seed));
    }

    /// <summary>
    /// Brings the light engine up to a readable frame around a point, before anything asks it a
    /// question.
    ///
    /// Without this the first brain tick reads an empty light map, every tile in the world looks
    /// unlit, and the lighting behaviour spends its opening ticks reacting to an instrument rather
    /// than to a world. Two full cycles rather than one because the first cycle's blur runs over a
    /// scan taken before the area was ever presented.
    /// </summary>
    public static void WarmTheLightEngine(Point centre, int halfWidth, int halfHeight)
    {
        for (int call = 0; call < LightPhasesPerCycle * 2; call++)
            DriveLightOnce(centre, halfWidth, halfHeight);
    }

    /// <summary>One phase of the light engine, which is what a running game does per frame.</summary>
    public static void DriveLightOnce(Point centre, int halfWidth, int halfHeight)
    {
        int x0 = Math.Max(1, centre.X - halfWidth), x1 = Math.Min(Main.maxTilesX - 2, centre.X + halfWidth);
        int y0 = Math.Max(1, centre.Y - halfHeight), y1 = Math.Min(Main.maxTilesY - 2, centre.Y + halfHeight);
        if (x1 <= x0 || y1 <= y0) return;
        Lighting.LightTiles(x0, x1, y0, y1);
    }

    /// <summary>
    /// The engine services the light cycle's four phases need, none of which are lighting.
    ///
    /// The minimap export and the scene-metrics scan are two of the four, so both have to survive or
    /// the cycle never reaches the blur that makes light readable, and the profiler is written to on
    /// the way out of every phase.
    /// </summary>
    public static void PrepareLightServices()
    {
        TimeLogger.Initialize();
        Main.Map ??= new Terraria.Map.WorldMap(Main.maxTilesX, Main.maxTilesY);
        Terraria.Map.MapHelper.Initialize();
        Main.SceneMetrics ??= new SceneMetrics();
        Lighting.Initialize();
    }

    /// <summary>
    /// Advances the world clock by one tick.
    ///
    /// The brain keys caches on this counter, so a run that never moved it would let every
    /// tick-aged answer live forever — which reads as a brain that never rethinks anything.
    /// </summary>
    /// <summary>
    /// Puts the world clock at a stated tick, which every pass does before its first tick.
    ///
    /// Without it the clock only ever counts up, so a second pass in the same process begins its
    /// route thousands of ticks later in world time than the first did — and the brain has a family
    /// of guards shaped `Main.GameUpdateCount - someStoredTick &lt; N` whose stored tick is zero in a
    /// freshly attached brain. At a clock near zero that arithmetic reads "this happened just now";
    /// at a clock in the tens of thousands the same zero reads "this happened long ago", so the two
    /// passes take different branches from state that is otherwise identical.
    ///
    /// That is what the first whole-capture determinism row caught: two passes of the 13:27 capture
    /// disagreed at step 2640, walking opposite directions from positions 0.3 px apart, and the
    /// whole disagreement reproduced exactly across two separate processes — which is what rules
    /// out the wall clock and names carried process state instead. Every window before it agreed
    /// because none was long enough to reach a guard of that shape.
    ///
    /// The clock starts at the capture's own first tick rather than at zero, so world time in the
    /// run means the same thing as the tick column in the recording.
    /// </summary>
    public static void StartTheWorldClockAt(ulong tick)
    {
        FieldInfo field = typeof(Main).GetField("GameUpdateCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? typeof(Main).GetField("_gameUpdateCount", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria's game-update counter cannot be reached, so two passes could not be started at one world time");
        field.SetValue(null, field.FieldType == typeof(uint) ? (object)(uint)tick : tick);
    }

    public static void AdvanceTheWorldClock()
    {
        FieldInfo field = typeof(Main).GetField("GameUpdateCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? typeof(Main).GetField("_gameUpdateCount", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria's game-update counter cannot be reached, so tick-aged brain caches would never expire");
        ulong next = Main.GameUpdateCount + 1;
        field.SetValue(null, field.FieldType == typeof(uint) ? (object)(uint)next : next);
    }

    /// <summary>
    /// Finishes a tick the way the engine finishes it for a no-gravity, no-tile-collide NPC, after
    /// the brain and motor have already applied their controls: <c>NPC.UpdateNPC_Inner</c> skips
    /// gravity and <c>UpdateCollision</c> for such a body and adds the velocity to the position,
    /// after zeroing a horizontal velocity smaller than 0.005 px (`Terraria.NPC.cs`, the
    /// <c>velocity.X &lt; 0.005</c> guard just above the <c>noTileCollide</c> branch), and nothing
    /// else that moves it. The motor has already resolved contact on that displacement, so this is
    /// the whole of what the engine contributes to the orb's motion, in the mod and here alike; the
    /// snap is reproduced so a run here and a live capture cannot drift by a sub-pixel a tick.
    /// </summary>
    public static void AdvanceTheNativeBody(CompanionNPC companion)
    {
        NPC npc = companion.NPC;
        if (npc.velocity.X < 0.005f && npc.velocity.X > -0.005f)
            npc.velocity.X = 0f;
        npc.oldPosition = npc.position;
        npc.position += npc.velocity;
    }

    private static void Set(object target, string field, object value)
        => (target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException($"{target.GetType().Name}.{field} is gone"))
           .SetValue(target, value);
}
