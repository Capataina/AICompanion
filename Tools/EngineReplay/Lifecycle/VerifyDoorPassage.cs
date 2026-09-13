extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using DoorOpener = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Doors.DoorOpener;
// Every movement-core type comes from the live mod: EngineReplay also compiles its own copy of that core, and its
// statics (revision, edge cache, planning allowance, grid world) are not the ones the live brain and door interaction use.
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;
using NavGrid = live::AICompanion.Companion.Brain.Infrastructure.Movement.NavGrid;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Reachability = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability;
using AStar = live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;

/// <summary>
/// A25 on native tiles: Terraria's own door helper decides whether a door opens, the companion's door interaction
/// gets it through a closed door that is the only passage, a door the game refuses to open stays a wall, and opening
/// or closing a door announces a terrain change the route edge cache hears at once. The door helper changes tiles
/// through neither KillTile nor PlaceInWorld, so without that announcement the planner keeps the old door's edges
/// until the cache ages them out.
/// </summary>
internal static class VerifyDoorPassage
{
    private const int FloorRow = 80, DoorX = 50, WestX = 38, EastX = 62;
    /// <summary>The locked Lihzahrd temple door's first frame row: WorldGen.OpenDoor refuses that style however the
    /// swing columns look, which is the one native door a body can press against and still not open.</summary>
    private const short LockedDoorFrameY = 594;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int failed = 0;
        failed += VerifyMovementFailures.Case("the native door helper opens toward open space and refuses a blocked or locked door", NativeOpeningFollowsTheSwingColumns, "doors");
        failed += VerifyMovementFailures.Case("opening and closing a door reaches the route edge cache at once", DoorToggleInvalidatesCachedEdges, "doors");
        LimitPlanningWork.Unbounded = true;
        try
        {
            failed += VerifyMovementFailures.Case("a closed door that is the only passage is opened and walked through", OpenableDoorIsWalkedThrough, "doors");
            failed += VerifyMovementFailures.Case("a locked door with no detour is refused without pressing against it", LockedDoorIsRefused, "doors");
            failed += VerifyMovementFailures.Case("a locked door with a detour is routed around and never opened", LockedDoorIsRoutedAround, "doors");
            MeasureOpenableDoorBesideADetour();
        }
        finally { LimitPlanningWork.Unbounded = false; }
        Console.WriteLine(failed == 0
            ? "doors: native opening, announced toggles, a door walked through, a locked door refused and routed around pass"
            : $"doors: {failed} case(s) failed");
        return failed;
    }

    // ── cases ────────────────────────────────────────────────────────────────────────────────

    private static void NativeOpeningFollowsTheSwingColumns()
    {
        var cases = new (string Name, bool Locked, bool BlockWest, bool BlockEast, int ExpectedSwing)[]
        {
            ("open both sides", false, false, false, 1),
            ("east side solid", false, false, true, -1),
            ("both sides solid", false, true, true, 0),
            ("locked style, both sides open", true, false, false, 0),
        };
        foreach (var c in cases)
        {
            BuildWorld();
            PlaceDoor(c.Locked);
            for (int row = FloorRow - 3; row < FloorRow; row++)
            {
                if (c.BlockWest) Solid(DoorX - 1, row);
                if (c.BlockEast) Solid(DoorX + 1, row);
            }
            bool east = WorldGen.OpenDoor(DoorX, FloorRow - 2, 1);
            bool west = !east && WorldGen.OpenDoor(DoorX, FloorRow - 2, -1);
            int swing = east ? 1 : west ? -1 : 0;
            Require(swing == c.ExpectedSwing,
                $"{c.Name}: the door helper swung {swing}, expected {c.ExpectedSwing}; columns [{Column(DoorX - 1)}] [{Column(DoorX)}] [{Column(DoorX + 1)}]");
            bool open = Main.tile[DoorX, FloorRow - 2].TileType == TileID.OpenDoor;
            Require(open == (swing != 0), $"{c.Name}: the door tile must be open exactly when the helper reported a swing");
        }
    }

    /// <summary>
    /// The walker is asked across a closed door (no), so that answer's edges are cached. The real door interaction then
    /// opens the door for a body walking into it, and the same question is asked again with the cache clock unmoved: only
    /// an announced terrain change can make the answer yes. Closing it behind the body must turn it back to no.
    /// </summary>
    private static void DoorToggleInvalidatesCachedEdges()
    {
        BuildWorld();
        for (int row = 50; row < FloorRow - 3; row++) Solid(DoorX, row);
        PlaceDoor(locked: false);
        var companion = VerifyCompanionLifecycle.Create();
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
        TerrainChanges.Reset();
        AStar.CacheEdges = true;
        Point west = new(WestX, FloorRow - 1), east = new(EastX, FloorRow - 1);
        Require(MovementQueries.WalkerReach(west, east) == Reachability.Reach.No,
            "premise: a closed door in a full-height wall must be no passage to the walker");

        NPC npc = companion.NPC;
        npc.direction = 1;
        npc.velocity = new Vector2(3f, 0f);
        npc.Bottom = new Vector2((DoorX - 1) * 16 + 8, FloorRow * 16);
        var doors = new DoorOpener();
        int revision = TerrainChanges.Revision;
        doors.Tick(npc);
        Require(Main.tile[DoorX, FloorRow - 2].TileType == TileID.OpenDoor, "premise: the door interaction must open a door the body walks into");
        // The walker is asked before the revision is read, so a missing announcement fails on its consequence (the cached
        // closed-door edges answering for an open door) rather than on the counter alone.
        var afterOpen = MovementQueries.WalkerReach(west, east);
        Require(afterOpen == Reachability.Reach.Yes,
            $"the opened door must be a passage on the next question, not after the edge cache ages out; reach={afterOpen}");
        Require(TerrainChanges.Revision != revision, "opening a door must announce a terrain change");

        npc.Bottom = new Vector2((DoorX + 3) * 16 + 8, FloorRow * 16);
        npc.velocity = Vector2.Zero;
        revision = TerrainChanges.Revision;
        doors.Tick(npc);
        Require(Main.tile[DoorX, FloorRow - 2].TileType == TileID.ClosedDoor, "premise: the door interaction must close the door behind a body that is through");
        var afterClose = MovementQueries.WalkerReach(west, east);
        Require(afterClose == Reachability.Reach.No,
            $"the closed door must be a wall again on the next question; reach={afterClose}");
        Require(TerrainChanges.Revision != revision, "closing a door must announce a terrain change");
    }

    private static void OpenableDoorIsWalkedThrough()
    {
        var run = Follow(locked: false, trench: false);
        Require(run.Opened >= 0 && run.Crossed > run.Opened,
            $"the companion must open the door and walk through it to the player; {run}");
        Require(run.LowestFeetRow == FloorRow - 1, $"with no other route the body stays on the floor; {run}");
    }

    /// <summary>
    /// A locked door must be exactly a wall: the same scene with a stone tile in the doorway is the reference, and the
    /// door may not be opened, passed, or pressed against for longer than that wall is. Where the body waits and how
    /// often following touches the wall are the follow behaviour's own and are not asserted here.
    /// </summary>
    private static void LockedDoorIsRefused()
    {
        var run = Follow(locked: true, trench: false);
        var wall = Follow(locked: true, trench: false, stoneInTheDoorway: true);
        Console.WriteLine($"MEASURE doors locked door against a wall in the same doorway: door {run}; wall {wall}");
        Require(run.Opened < 0 && run.Crossed < 0 && wall.Crossed < 0, $"a locked door must never open or be passed; door {run}; wall {wall}");
        // Twenty ticks of slack: the door interaction's own failed attempt costs nothing in collision, so a real difference
        // means the door was treated as something other than a wall.
        Require(run.PushTicks <= wall.PushTicks + 20,
            $"a locked door must not be pressed against for longer than a wall in the same place; door {run}; wall {wall}");
    }

    private static void LockedDoorIsRoutedAround()
    {
        var run = Follow(locked: true, trench: true);
        Require(run.Opened < 0, $"a locked door must never open; {run}");
        Require(run.Crossed >= 0 && run.LowestFeetRow > FloorRow - 1,
            $"the companion must reach the player through the passage under the wall; {run}");
    }

    /// <summary>
    /// Printed and not asserted. The planner models a closed door as the solid tile Terraria's collision reads, so beside
    /// any other route it walks around a door it could open; only where no other route exists does walking into the door
    /// open it. Asserting today's answer would lock that in, and asserting the other answer would need the planner to
    /// model door opening, which is shared movement's representation to change.
    /// </summary>
    private static void MeasureOpenableDoorBesideADetour()
    {
        var run = Follow(locked: false, trench: true);
        Console.WriteLine($"MEASURE doors openable door beside a detour: {run} (a door is a wall to the planner, so the detour is taken while one exists)");
    }

    // ── world and driver ─────────────────────────────────────────────────────────────────────

    private readonly record struct FollowRun(int Opened, int Crossed, int PushTicks, int LowestFeetRow, float FinalFeetX)
    {
        public override string ToString()
            => $"opened={Opened} crossed={Crossed} pushTicks={PushTicks} lowestFeetRow={LowestFeetRow} finalFeetX={FinalFeetX:0.0}";
    }

    private static FollowRun Follow(bool locked, bool trench, bool stoneInTheDoorway = false)
    {
        BuildWorld();
        for (int row = 50; row < FloorRow - 3; row++) Solid(DoorX, row);
        PlaceDoor(locked);
        if (stoneInTheDoorway)
            for (int row = FloorRow - 3; row < FloorRow; row++) Solid(DoorX, row);
        if (trench)
        {
            // Three open rows under the wall: the floor drops to row 83 between columns 46 and 54.
            for (int x = 46; x <= 54; x++)
            {
                Main.tile[x, FloorRow].ClearEverything();
                Solid(x, FloorRow + 3);
            }
            for (int y = FloorRow + 1; y <= FloorRow + 3; y++) { Solid(45, y); Solid(55, y); }
        }
        var companion = VerifyCompanionLifecycle.Create();
        // Native CloseDoor's EmptyTile walks every player slot, and Create populates only the first.
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
        TerrainChanges.Reset();
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.position = new Vector2(EastX * 16, FloorRow * 16 - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(WestX * 16, FloorRow * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        int opened = -1, crossed = -1, pushTicks = 0, lowest = 0;
        for (int tick = 0; tick < 900; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            VerifyResponsiveFollowing.AdvanceNative(companion);
            if (opened < 0 && Main.tile[DoorX, FloorRow - 2].TileType == TileID.OpenDoor) opened = tick;
            if (crossed < 0 && companion.NPC.Center.X > (DoorX + 2) * 16) crossed = tick;
            if (companion.NPC.collideX) pushTicks++;
            lowest = Math.Max(lowest, (int)(companion.NPC.Bottom.Y / 16) - 1);
        }
        return new FollowRun(opened, crossed, pushTicks, lowest, companion.NPC.Bottom.X / 16);
    }

    private static void BuildWorld()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        // The headless tile table seeds almost nothing; these are the live game's values for the tiles used here.
        Main.tileSolid[TileID.Stone] = true;
        Main.tileSolid[TileID.ClosedDoor] = true;
        Main.tileSolid[TileID.OpenDoor] = false;
        foreach (FieldInfo field in typeof(TileLoader).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            if (field.Name.StartsWith("Hook") && field.FieldType.IsArray && field.GetValue(null) == null)
                field.SetValue(null, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
        for (int x = 5; x < 95; x++) Solid(x, FloorRow);
        TerrainChanges.Reset();
        NavGrid.World = new GameTileWorld();
    }

    private static void PlaceDoor(bool locked)
    {
        for (int row = 0; row < 3; row++)
        {
            Tile door = Main.tile[DoorX, FloorRow - 3 + row];
            door.ClearEverything();
            door.HasTile = true;
            door.TileType = TileID.ClosedDoor;
            door.TileFrameX = 0;
            door.TileFrameY = (short)((locked ? LockedDoorFrameY : 0) + row * 18);
        }
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.ClearEverything();
        tile.HasTile = true;
        tile.TileType = TileID.Stone;
    }

    private static string Column(int x) => string.Join(",", Enumerable.Range(FloorRow - 3, 3).Select(y =>
        Main.tile[x, y].HasTile ? $"{Main.tile[x, y].TileType}/{Main.tile[x, y].TileFrameX}/{Main.tile[x, y].TileFrameY}" : "-"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
