extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using BrainTelemetry = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.BrainTelemetry;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using MineOre = live::AICompanion.Companion.Brain.Activities.Gathering.MineOre;
using LiveMovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using LiveLimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// Records one unproductive native scene per purpose family through the real recorder and the real event writer, and
/// keeps the captures, so SessionReport can be run on each and asked which contract it names for the interval.
/// Unlike <see cref="VerifyObservationLifecycle"/> it asserts nothing about what the brain should do and deletes nothing:
/// the product is the capture folder it prints. Each scene reuses a stall an existing fixture already builds — a hop
/// take-off drowned under the walking body, a drop sealed in a box, an enemy sealed in rock under the companion, and
/// the player sealed away from the companion — so what the report says is about recorded native behaviour, not about a
/// scene tuned until the report said something.
/// </summary>
internal static class RecordEvidenceScenes
{
    private const int SceneTicks = 1200;

    public static int Run(string[] args)
    {
        // The folder travels inside the flag, because Program takes the first argument without a "--" as the tModLoader root.
        string? named = args.FirstOrDefault(a => a.StartsWith("--evidence-scenes=", StringComparison.Ordinal))?["--evidence-scenes=".Length..];
        string root = string.IsNullOrEmpty(named)
            ? Path.Combine(Path.GetTempPath(), "aic-evidence-scenes-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))
            : Path.GetFullPath(named);
        FieldInfo savePath = typeof(Terraria.Program).GetField("SavePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria save-path backing field is unavailable");
        object? priorSavePath = savePath.GetValue(null);
        // Set before anything touches Terraria.Main: its static constructor combines paths from the save path, and a null one
        // throws a type-initialisation failure that poisons every later use of Main in the process.
        savePath.SetValue(null, root);
        var preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current;
        bool potBreaking = preferences.PotBreaking;
        var priorMining = live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining;
        bool server = Main.dedServ;
        // Native PickTile runs achievement bookkeeping unless the process is a dedicated server; the planning allowances
        // are lifted so machine load cannot decide which phase a scene reaches, as the whole-brain fixtures do.
        Main.dedServ = true;
        LiveLimitPlanningWork.Unbounded = true;
        int failed = 0;
        try
        {
            failed += Scene("gathering-lost-take-off", GatheringTakeOffDrownedUnderTheWalkingBody);
            failed += Scene("collection-sealed-drop", CollectionDropSealedInABox);
            failed += Scene("combat-sealed-enemy", CombatEnemySealedInRockBelow);
            failed += Scene("following-sealed-player", FollowingPlayerSealedAway);
        }
        finally
        {
            LiveLimitPlanningWork.Unbounded = false;
            Main.dedServ = server;
            preferences.PotBreaking = potBreaking;
            live::AICompanion.Companion.Brain.Activities.WorkPolicies.Mining = priorMining;
            savePath.SetValue(null, priorSavePath);
        }
        Console.WriteLine($"evidence scenes kept under {root}");
        return failed;
    }

    /// <summary>Builds and drives one scene inside its own recorded session; the session is closed even when the scene throws.</summary>
    private static int Scene(string name, Func<ActionContext> build)
    {
        ActionContext ctx;
        try { ctx = build(); }
        catch (Exception error) { Console.WriteLine($"FAIL {name}: scene did not build: {error.Message}"); return 1; }
        var recorder = new BrainTelemetry();
        VerifyObservationLifecycle.Attach(recorder);
        recorder.OnWorldLoad();
        string path = Directory.GetFiles(BrainTelemetry.Folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).First();
        string? failure = null;
        try
        {
            for (int tick = 0; tick < SceneTicks; tick++) VerifyOreWork.AdvanceBrain(ctx);
        }
        catch (Exception error) { failure = error.ToString(); }
        finally { recorder.OnWorldUnload(); }
        var capture = Capture.Read(path);
        var outcomes = capture.Events.Where(e => e.Kind == "attempt-outcome")
            .Select(e => $"{e.Label}:{Capture.Field(e.Detail, "status")}:{Capture.Field(e.Detail, "cause")}")
            .ToList();
        var actions = capture.Rows.GroupBy(r => r[capture.Column("action")]).Select(g => $"{g.Key}={g.Count()}");
        Console.WriteLine($"{(failure is null ? "RECORDED" : "FAIL")} {name}: {path}");
        Console.WriteLine($"  {capture.Rows.Count} rows, {capture.Events.Count} occurrences; actions {string.Join(" ", actions)}");
        Console.WriteLine($"  attempt outcomes: {(outcomes.Count == 0 ? "none" : string.Join(" ", outcomes))}");
        if (failure is not null) Console.WriteLine("  scene threw: " + failure);
        return failure is null ? 0 : 1;
    }

    /// <summary>
    /// The lost take-off of <see cref="VerifyMiningHops"/>: a ceiling ore whose only face is underneath, a proven hop take-off
    /// the body has to walk to, and water poured onto it before the body arrives, so the dry jump it was proven with is gone.
    /// </summary>
    private static ActionContext GatheringTakeOffDrownedUnderTheWalkingBody()
    {
        Point ore = new(25, 52);
        var ctx = VerifyMiningHops.BuildCeilingScene(ore, slabTop: 51);
        ctx.Npc.Bottom = new Vector2(10 * 16 + 8, 60 * 16);
        var mine = ctx.Companion.Brain.Chooser.Actions.OfType<MineOre>().Single();
        VerifyOreWork.AdvanceBrain(ctx);
        if (mine.TargetStandPosition is not Vector2 takeOff)
            throw new InvalidOperationException($"the ceiling scene offered no hop take-off; status={mine.Status}");
        Point tile = LiveMovementQueries.FeetTile(takeOff);
        for (int x = tile.X - 2; x <= tile.X + 2; x++)
        {
            Tile water = Main.tile[x, tile.Y];
            water.LiquidType = LiquidID.Water;
            water.LiquidAmount = 255;
        }
        return ctx;
    }

    /// <summary>A copper drop ten tiles along the collection floor, inside a closed dirt box nothing can open or reach into.</summary>
    private static ActionContext CollectionDropSealedInABox()
    {
        live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current.PotBreaking = false;
        var ctx = VerifyCollectionContracts.SetUpFloor();
        VerifyCollectionContracts.Drop(ItemID.CopperOre, 10, new Vector2(30 * 16 + 8, 60 * 16));
        Seal(left: 28, right: 32, top: 55, bottom: 59);
        return ctx;
    }

    /// <summary>
    /// The sealed chamber of <see cref="VerifyHuntAdmissibility"/> moved onto the collection floor: solid rock under the
    /// floor with a two-tile pocket almost directly below the companion, holding a zombie no standable tile can see.
    /// </summary>
    private static ActionContext CombatEnemySealedInRockBelow()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        Main.tileSolid[TileID.Stone] = true;
        for (int x = 5; x < 95; x++)
            for (int y = 61; y <= 85; y++)
                VerifyOreWork.Place(new Point(x, y), TileID.Stone);
        for (int x = 21; x <= 23; x++)
            for (int y = 71; y <= 72; y++)
                Main.tile[x, y].ClearEverything();
        var enemy = new NPC();
        enemy.SetDefaults(NPCID.Zombie);
        LoadNpcNames(enemy);
        enemy.whoAmI = 25;
        enemy.active = true;
        enemy.velocity = Vector2.Zero;
        enemy.Bottom = new Vector2(22 * 16 + 8, 73 * 16);
        Main.npc[25] = enemy;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        return ctx;
    }

    /// <summary>The player twenty-five tiles along the floor inside a closed dirt box, so no route reaches him and ordinary following cannot arrive.</summary>
    private static ActionContext FollowingPlayerSealedAway()
    {
        var ctx = VerifyCollectionContracts.SetUpFloor();
        Main.npc[25] = new NPC();
        ctx.Player.position = new Vector2(45 * 16f, 60 * 16f - ctx.Player.height);
        ctx.Player.velocity = Vector2.Zero;
        Seal(left: 43, right: 48, top: 55, bottom: 59);
        return ctx;
    }

    /// <summary>
    /// The recorder names every sensed, requested and engaged NPC by <c>NPC.TypeName</c>, which reads Terraria's localised
    /// name table; the game always has it loaded and this headless host does not, so a recorded scene with a live NPC loads
    /// it the way the offscreen renderer does, and fails naming that limit rather than as a bare null reference.
    /// </summary>
    private static void LoadNpcNames(NPC probe)
    {
        Terraria.Localization.LanguageManager.Instance.SetLanguage("en-US");
        // Setting the language alone leaves the name table empty in this host (the combat scene failed without the next
        // call, 2026-09-13); the legacy initialiser fills it. It is undocumented, so it is looked up as required rather than
        // invoked through a null-conditional, which would pass silently if a tModLoader update removed it.
        MethodInfo initialise = typeof(Lang).GetMethod("InitializeLegacyLocalization", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Terraria.Lang.InitializeLegacyLocalization is unavailable, and the recorder needs the NPC name table it fills");
        initialise.Invoke(null, null);
        if (!Named(probe))
            throw new InvalidOperationException("this host cannot load Terraria's NPC name table, which the recorder reads for every sensed NPC");

        static bool Named(NPC npc)
        {
            try { return !string.IsNullOrEmpty(npc.TypeName); }
            catch (NullReferenceException) { return false; }
        }
    }

    /// <summary>A dirt box whose floor is the scene floor at row 60: two walls and a roof, closed on every side.</summary>
    private static void Seal(int left, int right, int top, int bottom)
    {
        for (int y = top; y <= bottom; y++)
        {
            VerifyOreWork.Place(new Point(left, y), TileID.Dirt);
            VerifyOreWork.Place(new Point(right, y), TileID.Dirt);
        }
        for (int x = left; x <= right; x++) VerifyOreWork.Place(new Point(x, top), TileID.Dirt);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
    }
}
