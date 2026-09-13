extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;
using Encounter = live::AICompanion.Companion.Brain.WorldObservation.EncounterSense;
using Threats = live::AICompanion.Companion.Brain.WorldObservation.ThreatSense;
using Evaluate = live::AICompanion.Companion.Brain.BehaviourSelection.EvaluatePreparedActivities;
using Prepared = live::AICompanion.Companion.Brain.BehaviourSelection.PreparedActivity;
using Comparison = live::AICompanion.Companion.Brain.BehaviourSelection.ActivityComparisonContext;
using Eligibility = live::AICompanion.Companion.Brain.Behaviours.OfferEligibility;
using Policy = live::AICompanion.Companion.Brain.Behaviours.Work.WorkPolicy;
using Weights = live::AICompanion.Companion.Brain.BehaviourSelection.Weights;
using LimitPlanningWork = live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork;

/// <summary>
/// Proposal 1's boss and event context, proven at three depths. The observation must say the world is
/// about one thing from native facts alone — a boss flag at any depth, the Old One's Army at any depth, a
/// moon or eclipse only at the surface, an invasion only where the game would spawn its enemies — and fall
/// back, marked unrecognised, to reachable hostiles weighing more than the game's own spawn cap, counted the
/// way the game counts them. The fallback rows are real NPC records run through the real threat observer,
/// because the defect they exist for (a worm's segments and an ordinary crowd reading as an event) cannot
/// appear in hand-built threat lists. The shared evaluator must charge that context once, to optional
/// non-combat work only. And the live brain, in one mining scene that differs only in the event flag and
/// the player's depth, must stop mining exactly when the event reaches it.
/// </summary>
internal static class VerifyEncounterContext
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo MaxSpawns = typeof(NPC).GetField("maxSpawns", PrivateStatic)!;
    private static readonly FieldInfo ActiveRangeX = typeof(NPC).GetField("activeRangeX", PrivateStatic)!;
    private static readonly FieldInfo ActiveRangeY = typeof(NPC).GetField("activeRangeY", PrivateStatic)!;

    /// <summary>
    /// The cap NPC.SpawnNPC computes for a pre-hardmode player between one screen below the surface line and
    /// one screen below the rock layer, with this mod's companion up: maxSpawns starts at defaultMaxSpawns 5
    /// (NPC.cs:86463), becomes (int)(5 × 1.7f) = 8 in that band (86508–86509), and CompanionSpawnRate doubles
    /// it to 16 through NPCLoader.EditSpawnRate (86904). The spawner cannot run headless, so the fixture
    /// writes the value it would have computed.
    /// </summary>
    private const int CaveCap = 16;

    private const int FirstHostileSlot = 40;
    private const int LastHostileSlot = 79;
    private const int FloorRow = 90;

    public static void Run()
    {
        bool bloodMoon = Main.bloodMoon, eclipse = Main.eclipse, pumpkin = Main.pumpkinMoon, snow = Main.snowMoon;
        bool dd2 = DD2Event.Ongoing;
        int invasion = Main.invasionType, invasionSize = Main.invasionSize, invasionDelay = Main.invasionDelay;
        double invasionX = Main.invasionX;
        double surface = Main.worldSurface;
        object? cap = MaxSpawns.GetValue(null), rangeX = ActiveRangeX.GetValue(null), rangeY = ActiveRangeY.GetValue(null);
        bool unbounded = LimitPlanningWork.Unbounded;
        try
        {
            TheObservationReadsNativeFactsAndFallsBackToSpawnPressure();
            TheEvaluatorChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork();
            TheLiveBrainStopsMiningOnlyWhereTheEventReachesIt();
        }
        finally
        {
            Main.bloodMoon = bloodMoon; Main.eclipse = eclipse; Main.pumpkinMoon = pumpkin; Main.snowMoon = snow;
            DD2Event.Ongoing = dd2;
            Main.invasionType = invasion; Main.invasionSize = invasionSize; Main.invasionDelay = invasionDelay;
            Main.invasionX = invasionX;
            Main.worldSurface = surface;
            MaxSpawns.SetValue(null, cap); ActiveRangeX.SetValue(null, rangeX); ActiveRangeY.SetValue(null, rangeY);
            LimitPlanningWork.Unbounded = unbounded;
            ClearHostiles();
        }
    }

    private static void ClearWorldEvents()
    {
        Main.bloodMoon = Main.eclipse = Main.pumpkinMoon = Main.snowMoon = false;
        DD2Event.Ongoing = false;
        Main.invasionType = 0; Main.invasionSize = 0; Main.invasionDelay = 0;
    }

    private static void ClearHostiles()
    {
        for (int slot = FirstHostileSlot; slot <= LastHostileSlot; slot++)
            if (Main.npc[slot] != null) Main.npc[slot].active = false;
    }

    private static NPC Hostile(int slot, int type, Vector2 bottom)
    {
        NPC npc = Main.npc[slot];
        npc.SetDefaults(type);
        npc.whoAmI = slot; npc.active = true; npc.velocity = Vector2.Zero;
        npc.Bottom = bottom;
        return npc;
    }

    private static Vector2 OnFloor(int column) => new(column * 16 + 8, FloorRow * 16);

    /// <summary>Zombies standing on the open floor, one per column from column 30, in consecutive slots.</summary>
    private static void Crowd(int count, int firstSlot = FirstHostileSlot)
    {
        for (int i = 0; i < count; i++) Hostile(firstSlot + i, NPCID.Zombie, OnFloor(30 + i));
    }

    private readonly record struct Row(float Intensity, string Source, bool Recognised, float Weight, int Cap, int Records, int Reaching)
    {
        public override string ToString() => $"({Intensity:0.###}, {Source}, {(Recognised ? "recognised" : "inferred")}, weight {Weight:0.##}/cap {Cap}, records {Records}, reaching {Reaching})";
    }

    private static Row Observe(ActionContextLike scene, int ticks = 1, Threats? threats = null, Encounter? sense = null)
    {
        threats ??= new Threats();
        sense ??= new Encounter();
        for (int t = 0; t < ticks; t++)
        {
            threats.Update(scene.Player, scene.Companion);
            sense.Update(scene.Player, threats);
        }
        int reaching = threats.Threats.Count(t => t.CanReachEither);
        return new Row(sense.Intensity, sense.Source, sense.Recognised, sense.SpawnWeight, sense.SpawnCap, threats.Threats.Count, reaching);
    }

    private readonly record struct ActionContextLike(Player Player, NPC Companion);

    private static ActionContextLike BuildObservationScene()
    {
        ClearWorldEvents();
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Disabled, TileID.Copper, new Point(25, FloorRow - 1));
        Main.tile[25, FloorRow - 1].ClearEverything();
        ClearHostiles();
        MaxSpawns.SetValue(null, CaveCap);
        return new ActionContextLike(ctx.Player, ctx.Npc);
    }

    private static void TheObservationReadsNativeFactsAndFallsBackToSpawnPressure()
    {
        var scene = BuildObservationScene();
        float feet = scene.Player.position.Y;
        const double SurfaceLine = 120, BelowSurface = 40;
        Require(feet <= SurfaceLine * 16 && feet > BelowSurface * 16,
            $"the scene's depth premise needs the player above surface line {SurfaceLine} and below line {BelowSurface}; feet y={feet}");

        Main.worldSurface = SurfaceLine;
        var none = Observe(scene);
        Require(none is { Intensity: 0f, Source: "none", Recognised: false }, $"a quiet surface with no hostiles must be no encounter; got {none}");

        Main.bloodMoon = true;
        var moonUp = Observe(scene);
        Main.worldSurface = BelowSurface;
        var moonDown = Observe(scene);
        Require(moonUp is { Intensity: 1f, Source: "event:blood-moon", Recognised: true }, $"a blood moon over a surface player is a recognised encounter; got {moonUp}");
        Require(moonDown is { Intensity: 0f, Source: "none", Recognised: false },
            $"a blood moon does not reach a player underground, where the game spawns none of it; got {moonDown}");
        ClearWorldEvents();

        DD2Event.Ongoing = true;
        var armyDown = Observe(scene);
        Require(armyDown is { Intensity: 1f, Source: "event:old-ones-army", Recognised: true }, $"the Old One's Army is recognised at any depth; got {armyDown}");
        ClearWorldEvents();

        Hostile(FirstHostileSlot, NPCID.EyeofCthulhu, OnFloor(40) - new Vector2(0, 160));
        var bossDown = Observe(scene);
        Require(bossDown is { Intensity: 1f, Source: "boss", Recognised: true }, $"an observed boss is a recognised encounter underground as well; got {bossDown}");
        ClearHostiles();

        TheInvasionCountsOnlyWhereTheGameSpawnsIt(scene);
        TheFallbackIsWeightAboveTheGamesSpawnCap(scene);
        Console.WriteLine($"  encounter sense rows: moon surface {moonUp}, moon underground {moonDown}, army {armyDown}, boss {bossDown}");
    }

    /// <summary>
    /// NPC.SpawnNPC sends invasion enemies to a player only when the invasion has arrived (no delay left), still
    /// has enemies to send, the player is above one screen below the surface line, and the player is within
    /// 3000 px of the invasion's position (NPC.cs:86425–86431). A flag set anywhere else is not an encounter.
    /// The world here is too narrow to separate the town-NPC branch from the band, so that branch is not rowed.
    /// </summary>
    private static void TheInvasionCountsOnlyWhereTheGameSpawnsIt(ActionContextLike scene)
    {
        float feet = scene.Player.position.Y;
        double oneScreenBelow = Math.Floor((feet - NPC.sHeight / 2f) / 16f);
        double deeper = Math.Floor((feet - NPC.sHeight - 64f) / 16f);
        Require(oneScreenBelow > 0 && deeper > 0 && feet > oneScreenBelow * 16 && feet < oneScreenBelow * 16 + NPC.sHeight
            && feet >= deeper * 16 + NPC.sHeight,
            $"the invasion depth premise needs a surface line the player is below by less than a screen and one it is below by more; feet={feet} sHeight={NPC.sHeight} lines {oneScreenBelow}/{deeper}");
        double here = scene.Player.position.X / 16.0;

        (float, string) Invasion(int size, int delay, double line, double x)
        {
            ClearWorldEvents();
            Main.invasionType = InvasionID.GoblinArmy; Main.invasionSize = size; Main.invasionDelay = delay; Main.invasionX = x;
            Main.worldSurface = line;
            var row = Observe(scene);
            ClearWorldEvents();
            return (row.Intensity, row.Source);
        }

        // The size, delay and band rows stand at the surface, where a gate reading only the invasion flag would
        // recognise them, so each fails on that gate; the depth rows stand below the surface line, where a
        // surface-only gate would miss the screen of depth the spawner still sends invaders to.
        const double SurfaceLine = 120;
        var atSurface = Invasion(size: 40, delay: 0, SurfaceLine, here);
        var arrived = Invasion(size: 40, delay: 0, oneScreenBelow, here);
        var empty = Invasion(size: 0, delay: 0, SurfaceLine, here);
        var delayed = Invasion(size: 40, delay: 2, SurfaceLine, here);
        var tooDeep = Invasion(size: 40, delay: 0, deeper, here);
        var elsewhere = Invasion(size: 40, delay: 0, SurfaceLine, here + 400);
        Console.WriteLine($"  encounter invasion rows: at the surface {atSurface}, a screen below it {arrived}, no enemies left {empty}, still delayed {delayed}, over a screen deep {tooDeep}, 400 tiles away {elsewhere}");
        Require(atSurface == (1f, "event:invasion"), $"an arrived invasion with enemies left, at the player on the surface, is recognised; got {atSurface}");
        Require(arrived == (1f, "event:invasion"), $"an arrived invasion with enemies left, within a screen of the surface and at the player, is recognised; got {arrived}");
        Require(empty == (0f, "none"), $"an invasion with no enemies left to send spawns nothing and is no encounter; got {empty}");
        Require(delayed == (0f, "none"), $"an invasion still on its arrival delay spawns nothing yet and is no encounter; got {delayed}");
        Require(tooDeep == (0f, "none"), $"an invasion does not spawn for a player more than a screen below the surface; got {tooDeep}");
        Require(elsewhere == (0f, "none"), $"an invasion does not spawn for a player outside its 3000 px band; got {elsewhere}");
    }

    private static void TheFallbackIsWeightAboveTheGamesSpawnCap(ActionContextLike scene)
    {
        int window = (int)Weights.EncounterPressureTicks;
        Main.worldSurface = 40;

        // A Giant Worm as its AI builds it (NPC.cs:54657–54669): the head owns realLife and every segment
        // points at it. Bodies and tail do not despawn to inactivity, so NPC.CheckActive returns before adding
        // their slots (83781, 83669–83734): the game counts this worm as its head alone.
        NPC head = Hostile(FirstHostileSlot, NPCID.GiantWormHead, OnFloor(34) - new Vector2(0, 48));
        head.realLife = head.whoAmI; head.ai[3] = head.whoAmI;
        for (int i = 1; i <= 7; i++)
        {
            NPC segment = Hostile(FirstHostileSlot + i, i == 7 ? NPCID.GiantWormTail : NPCID.GiantWormBody, OnFloor(34 + i) - new Vector2(0, 48));
            segment.realLife = head.whoAmI; segment.ai[3] = head.whoAmI;
        }
        var worm = Observe(scene, ticks: window * 2);
        ClearHostiles();
        Require(worm.Records == 8 && worm.Reaching == 8,
            $"the worm scene must put all eight records in front of the observer, all able to reach, or it tests nothing; got {worm}");
        Require(MathF.Abs(worm.Weight - 1f) < 1e-5f && worm is { Intensity: 0f, Source: "none" },
            $"one Giant Worm with its segments weighs one spawn slot, as the game counts it, and is no encounter underground; got {worm}");

        Crowd(CaveCap);
        var atCap = Observe(scene, ticks: window * 2);
        ClearHostiles();
        Require(atCap.Reaching == CaveCap && MathF.Abs(atCap.Weight - CaveCap) < 1e-5f && atCap.Cap == CaveCap,
            $"the busy-cave premise needs exactly the cap's weight of reachable hostiles; got {atCap}");
        Require(atCap is { Intensity: 0f, Source: "none" },
            $"an ordinary crowd at the spawn cap is what ordinary spawning produces and must never read as an event; got {atCap}");

        Crowd(CaveCap + 1);
        var over = new Encounter();
        var overThreats = new Threats();
        var half = Observe(scene, ticks: window / 2, threats: overThreats, sense: over);
        var sustained = Observe(scene, ticks: window * 2 - window / 2, threats: overThreats, sense: over);
        Main.npc[FirstHostileSlot + CaveCap].active = false;
        var thinned = Observe(scene, threats: overThreats, sense: over);
        ClearHostiles();
        Require(half.Source == "observed-pressure" && !half.Recognised && MathF.Abs(half.Intensity - .5f) < .01f,
            $"one hostile over the cap for half the window must read as a half-strength inferred encounter; got {half}");
        Require(sustained is { Intensity: 1f, Source: "observed-pressure", Recognised: false },
            $"weight sustained over the cap with no native flag must reach full strength and stay marked unrecognised; got {sustained}");
        Require(thinned is { Intensity: 0f, Source: "none" } && MathF.Abs(thinned.Weight - CaveCap) < 1e-5f,
            $"the first tick the crowd thins back to the cap, the inferred encounter must end completely; got {thinned}");

        Crowd(CaveCap);
        for (int x = 70; x <= 78; x++)
        for (int y = 83; y < FloorRow; y++)
        {
            Tile tile = Main.tile[x, y];
            tile.ClearEverything();
            tile.HasTile = x == 70 || x == 78 || y == 83;
            tile.TileType = TileID.Dirt;
        }
        live::AICompanion.Companion.Brain.SharedMovementSystem.AStar.InvalidateEdges();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
        Hostile(FirstHostileSlot + CaveCap, NPCID.Zombie, OnFloor(74));
        var walled = Observe(scene, ticks: window * 2);
        ClearHostiles();
        for (int x = 70; x <= 78; x++)
        for (int y = 83; y < FloorRow; y++)
            Main.tile[x, y].ClearEverything();
        live::AICompanion.Companion.Brain.SharedMovementSystem.AStar.InvalidateEdges();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
        Require(walled.Records == CaveCap + 1 && walled.Reaching == CaveCap,
            $"the walled-off premise needs the sealed zombie observed and unable to reach either actor; got {walled}");
        Require(walled is { Intensity: 0f, Source: "none" } && MathF.Abs(walled.Weight - CaveCap) < 1e-5f,
            $"a hostile walled off from both actors must not add to the pressure weight; got {walled}");

        // Four enemies each declaring five spawn slots, as a modded heavy spawn declares itself: fewer records
        // than any small count threshold, and more weight than the cap. The weight, not the count, decides.
        for (int i = 0; i < 4; i++) Hostile(FirstHostileSlot + i, NPCID.Zombie, OnFloor(30 + i)).npcSlots = 5f;
        var heavy = Observe(scene, ticks: window * 2);
        ClearHostiles();
        Require(heavy.Reaching == 4 && MathF.Abs(heavy.Weight - 20f) < 1e-5f && heavy is { Intensity: 1f, Source: "observed-pressure" },
            $"four reachable hostiles weighing twenty slots against a cap of sixteen must be sustained pressure; got {heavy}");

        // The game counts an NPC only when its active range, centred on it, touches the player's hitbox. With
        // that range shrunk, the same over-cap crowd a few tiles away is outside it and weighs nothing.
        object? rangeX = ActiveRangeX.GetValue(null), rangeY = ActiveRangeY.GetValue(null);
        ActiveRangeX.SetValue(null, 64); ActiveRangeY.SetValue(null, 64);
        Crowd(CaveCap + 1);
        Row outOfRange;
        try { outOfRange = Observe(scene, ticks: window * 2); }
        finally { ActiveRangeX.SetValue(null, rangeX); ActiveRangeY.SetValue(null, rangeY); ClearHostiles(); }
        Require(outOfRange.Reaching == CaveCap + 1 && outOfRange.Weight == 0f && outOfRange is { Intensity: 0f, Source: "none" },
            $"hostiles outside the game's active range of the player must not count toward its cap; got {outOfRange}");

        Console.WriteLine($"  encounter pressure rows (cap {CaveCap}): worm {worm}, at cap {atCap}, half window over {half}, sustained over {sustained}, thinned {thinned}, walled {walled}, heavy {heavy}, out of range {outOfRange}");
    }

    private static void TheEvaluatorChargesAnEncounterOnceAndOnlyToOptionalNonCombatWork()
    {
        var board = new[]
        {
            new Prepared(0, "mine", .8f, 60, IsExcursion: true, HasTarget: true, IsFollowing: false, IsIncumbent: false, Eligibility.Usable),
            new Prepared(1, "hunt", .3f, 60, IsExcursion: true, HasTarget: true, IsFollowing: false, IsIncumbent: false, Eligibility.Usable, ServesEncounter: true),
            new Prepared(2, "keep-company", .2f, 0, IsExcursion: false, HasTarget: false, IsFollowing: true, IsIncumbent: false, Eligibility.Usable),
        };
        Comparison Context(float urgency, float encounter, bool stranded = false)
            => new(urgency, stranded, float.PositiveInfinity, 30, 600, 1, false, 1, 0, encounter);

        var calm = Evaluate.Evaluate(board, Context(0, 0));
        var full = Evaluate.Evaluate(board, Context(0, 1));
        var half = Evaluate.Evaluate(board, Context(0, .5f));
        var urgentOnly = Evaluate.Evaluate(board, Context(.6f, 0));
        var both = Evaluate.Evaluate(board, Context(.6f, .6f));
        var stranded = Evaluate.Evaluate(board, Context(0, 1, stranded: true));
        var invalid = Evaluate.Evaluate(board, Context(0, 1.5f));

        Require(calm[0].Final > calm[1].Final && calm[0].Final > calm[2].Final,
            $"with no encounter the fixture must start with mining ahead, or the pair proves nothing; mine={calm[0].Final} hunt={calm[1].Final} keep={calm[2].Final}");
        Require(full[0].Final == 0f && full[1].Final == calm[1].Final && full[2].Final == calm[2].Final,
            $"a full encounter must remove mining's value and leave hunting and keeping company exactly as they were; mine={full[0].Final} hunt={full[1].Final}/{calm[1].Final} keep={full[2].Final}/{calm[2].Final}");
        Require(MathF.Abs(half[0].Protection - .5f) < 1e-5f, $"a half-strength encounter halves optional work; protection={half[0].Protection}");
        Require(MathF.Abs(both[0].Protection - urgentOnly[0].Protection) < 1e-5f && MathF.Abs(both[0].Protection - .4f) < 1e-5f,
            $"urgency and an equal encounter are one danger read twice, so mining must pay 0.4 once, not 0.16; both={both[0].Protection} urgency-only={urgentOnly[0].Protection}");
        Require(MathF.Abs(both[1].Protection - .4f) < 1e-5f,
            $"combat still pays the player's urgency during an encounter, and only that; hunt protection={both[1].Protection}");
        Require(stranded[0].Protection == 1f, $"a stranded companion's optional work is not charged for an encounter either; protection={stranded[0].Protection}");
        Require(invalid[0].Error == "invalid-encounter", $"an intensity outside 0..1 is an adapter defect and must be refused; error='{invalid[0].Error}'");
        Console.WriteLine($"  encounter evaluation rows: calm mine {calm[0].Final:0.###} hunt {calm[1].Final:0.###} keep {calm[2].Final:0.###}; full mine {full[0].Final:0.###} hunt {full[1].Final:0.###} keep {full[2].Final:0.###}; urgency+encounter protection {both[0].Protection:0.###}");
    }

    private static (string? Chosen, float Mine, string Source) MiningScene(bool bloodMoon, bool playerOnSurface)
    {
        ClearWorldEvents();
        ClearHostiles();
        var (_, ctx) = VerifyOreWork.SetUp(Policy.Opportunistic, TileID.Copper, new Point(25, 89));
        Main.bloodMoon = bloodMoon;
        Main.worldSurface = playerOnSurface ? 120 : 40;
        for (int t = 0; t < 3; t++) VerifyCompanionLifecycle.TickWithOneControlGrant(ctx.Companion);
        var chooser = ctx.Companion.Brain.Chooser;
        float mine = 0f;
        foreach (var score in chooser.LastScores)
            if (score.Action.Name == "mine") mine = score.Final;
        return (chooser.Current?.Name, mine, ctx.Companion.Brain.Senses.Encounter.Source);
    }

    /// <summary>
    /// The three scenes run the whole brain, so the planning allowances are lifted for them: under live
    /// wall-clock allowances how far a search got, and so which activity won, depends on machine load.
    /// </summary>
    private static void TheLiveBrainStopsMiningOnlyWhereTheEventReachesIt()
    {
        LimitPlanningWork.Unbounded = true;
        var quiet = MiningScene(bloodMoon: false, playerOnSurface: true);
        var moonUp = MiningScene(bloodMoon: true, playerOnSurface: true);
        var moonDown = MiningScene(bloodMoon: true, playerOnSurface: false);
        Console.WriteLine($"  encounter live rows: quiet {quiet}, blood moon surface {moonUp}, blood moon underground {moonDown}");
        Require(quiet.Chosen == "mine" && quiet.Mine > 0f,
            $"the quiet scene must choose the reachable ore, or the pair proves nothing; got {quiet}");
        Require(moonUp.Source == "event:blood-moon" && moonUp.Mine == 0f && moonUp.Chosen != "mine",
            $"a blood moon over the player must stop the same mining job through the shared evaluator; got {moonUp}");
        Require(moonDown.Source == "none" && moonDown.Chosen == "mine" && MathF.Abs(moonDown.Mine - quiet.Mine) < 1e-4f,
            $"the same blood moon with the player underground must leave mining exactly as the quiet scene valued it; quiet={quiet} underground={moonDown}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("encounter context: " + message);
    }
}
