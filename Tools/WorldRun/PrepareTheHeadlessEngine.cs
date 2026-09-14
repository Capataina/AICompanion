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
    public static CompanionNPC AttachCompanion(Vector2 feet, Vector2 playerFeet)
    {
        Main.myPlayer = 0;
        // Native strikes look for a cosmetic damage slot even headless, and would otherwise measure
        // a damage popup with fonts nothing here loads.
        for (int i = 0; i < Main.combatText.Length; i++) Main.combatText[i] ??= new CombatText { active = true };
        for (int i = 0; i < Main.dust.Length; i++) Main.dust[i] ??= new Dust();
        foreach (int item in new[] { ItemID.WoodenBow, ItemID.WoodenArrow, ItemID.ThrowingKnife, ItemID.CopperPickaxe, ItemID.CopperAxe })
        {
            var sample = new Item();
            sample.SetDefaults(item);
            ContentSamples.ItemsByType[item] = sample;
        }
        foreach (int type in new[] { ProjectileID.WoodenArrowFriendly, ProjectileID.ThrowingKnife })
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

        for (int i = 0; i < Main.npc.Length; i++) Main.npc[i] = new NPC { whoAmI = i, active = false };
        for (int i = 0; i < Main.projectile.Length; i++) Main.projectile[i] = new Projectile { whoAmI = i, active = false };
        for (int i = 0; i < Main.item.Length; i++) Main.item[i] = new Item { whoAmI = i, active = false };

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
        npc.Bottom = feet;
        npc.velocity = Vector2.Zero;
        // Rendering and first-tick logging want loader and graphics services this host does not have.
        Set(companion, "loggedFirstTick", true);
        Set(companion.Body, "rendererFailed", true);

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
    /// The route archive is cleared through the mod's own world-load path rather than by reaching
    /// into its fields, so it clears whatever that path clears today.
    /// </summary>
    public static void ForgetEverythingLearnedAboutTheWorld()
    {
        var world = new live::AICompanion.Companion.Brain.Infrastructure.Movement.ResetTerrainChanges();
        world.OnWorldLoad();
        world.LoadWorldData(new Terraria.ModLoader.IO.TagCompound());
        live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid.World =
            new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.InvalidateEdges();
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
    public static void AdvanceTheWorldClock()
    {
        FieldInfo field = typeof(Main).GetField("GameUpdateCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? typeof(Main).GetField("_gameUpdateCount", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria's game-update counter cannot be reached, so tick-aged brain caches would never expire");
        ulong next = Main.GameUpdateCount + 1;
        field.SetValue(null, field.FieldType == typeof(uint) ? (object)(uint)next : next);
    }

    /// <summary>
    /// Finishes a tick with the engine's own gravity and collision, after the brain and motor have
    /// already applied their controls.
    ///
    /// This is <c>VerifyResponsiveFollowing.AdvanceNative</c>'s body, and the reason it is not
    /// <c>NPC.UpdateNPC</c> is the reason that fixture gives: the production motor has already
    /// applied the movement abilities and the step helpers, so a full engine update would apply the
    /// same controls a second time. What remains — the engine's own gravity setup, its own fall
    /// clamp and its own <c>UpdateCollision</c> — is the part this repository treats as its
    /// independent oracle, and it is the same path every native collision fixture is checked
    /// against.
    /// </summary>
    public static void AdvanceTheNativeBody(CompanionNPC companion)
    {
        NPC npc = companion.NPC;
        // Suppresses the splash visual, whose dust and audio services do not exist headless; native
        // wet detection, velocity changes and collision all still run.
        npc.wetCount = 2;
        Invoke(npc, "UpdateNPC_UpdateGravity");
        npc.velocity.Y = MathF.Min(npc.velocity.Y + npc.gravity, npc.maxFallSpeed);
        Invoke(npc, "UpdateCollision");
    }

    private static void Invoke(NPC npc, string method)
        => (typeof(NPC).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException($"NPC.{method} is gone; the native body can no longer be advanced the way every collision fixture advances it"))
           .Invoke(npc, null);

    private static void Set(object target, string field, object value)
        => (target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException($"{target.GetType().Name}.{field} is gone"))
           .SetValue(target, value);
}
