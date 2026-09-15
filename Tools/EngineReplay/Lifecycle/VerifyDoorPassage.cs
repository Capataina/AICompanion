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
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Reachability = live::AICompanion.Companion.Brain.Infrastructure.Movement.Reachability;
using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using FreeSpaceSearch = live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch;
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
    /// <summary>
    /// The row the wall the door sits in starts at, and it is the top of the world rather than a comfortable
    /// height above the floor.
    ///
    /// <para>It used to start at row 50, thirty rows over the floor, because for a walking body a wall taller
    /// than a jump is a wall and nothing above that mattered. A body that flies goes over it: the first orb run
    /// of this fixture crossed the closed door and the solid-stone control with byte-identical numbers —
    /// <c>opened=-1 crossed=106 pushTicks=0</c> for both — which is the fixture reporting that a stone wall is
    /// not a wall, not a finding about doors. For a flyer the only sealed wall is one that reaches the world
    /// margin, so every scene in this file builds it from row zero.</para>
    /// </summary>
    private const int WallTop = 0;
    /// <summary>The locked Lihzahrd temple door's first frame row: WorldGen.OpenDoor refuses that style however the
    /// swing columns look, which is the one native door a body can press against and still not open.</summary>
    private const short LockedDoorFrameY = 594;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int failed = 0;
        failed += RunOneRow.Case("the native door helper opens toward open space and refuses a blocked or locked door", NativeOpeningFollowsTheSwingColumns, "doors");
        failed += RunOneRow.Case("opening and closing a door reaches the route edge cache at once", DoorToggleInvalidatesCachedEdges, "doors");
        LimitPlanningWork.Unbounded = true;
        try
        {
            MeasureOpenableDoorAsTheOnlyPassage();
            failed += RunOneRow.Case("a locked door with no detour is refused without pressing against it", LockedDoorIsRefused, "doors");
            failed += RunOneRow.Case("a locked door with a detour is routed around and never opened", LockedDoorIsRoutedAround, "doors");
            MeasureOpenableDoorBesideADetour();
        }
        finally { LimitPlanningWork.Unbounded = false; }
        Console.WriteLine(failed == 0
            ? "doors: native opening, announced toggles, a locked door refused and routed around pass; the openable door as the only passage is measured, not asserted"
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
        for (int row = WallTop; row < FloorRow - 3; row++) Solid(DoorX, row);
        PlaceDoor(locked: false);
        var companion = VerifyCompanionLifecycle.Create();
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();
        Point west = new(WestX, FloorRow - 1), east = new(EastX, FloorRow - 1);
        Require(Reaches(west, east) == Reachability.Reach.No,
            "premise: a closed door in a full-height wall must be no passage to this body");

        NPC npc = companion.NPC;
        npc.direction = 1;
        npc.velocity = new Vector2(3f, 0f);
        npc.Center = MovementQueries.HoverPoint(new Point(DoorX - 1, FloorRow - 1));
        var doors = new DoorOpener();
        int revision = TerrainChanges.Revision;
        doors.Tick(npc);
        Require(Main.tile[DoorX, FloorRow - 2].TileType == TileID.OpenDoor, "premise: the door interaction must open a door the body walks into");
        // The flood is asked before the revision is read, so a missing announcement fails on its consequence — stale free
        // space answering for a door that has moved — rather than on the counter alone.
        var afterOpen = Reaches(west, east);
        Require(afterOpen == Reachability.Reach.Yes,
            $"the opened door must be a passage on the next question, not after retained work ages out; reach={afterOpen}");
        Require(TerrainChanges.Revision != revision, "opening a door must announce a terrain change");

        npc.Center = MovementQueries.HoverPoint(new Point(DoorX + 3, FloorRow - 1));
        npc.velocity = Vector2.Zero;
        revision = TerrainChanges.Revision;
        doors.Tick(npc);
        Require(Main.tile[DoorX, FloorRow - 2].TileType == TileID.ClosedDoor, "premise: the door interaction must close the door behind a body that is through");
        var afterClose = Reaches(west, east);
        Require(afterClose == Reachability.Reach.No,
            $"the closed door must be a wall again on the next question; reach={afterClose}");
        Require(TerrainChanges.Revision != revision, "closing a door must announce a terrain change");
    }

    /// <summary>
    /// Whether the body can get from one tile to another over free space, answered by a fresh flood each time it is asked.
    /// This replaces the walker's cached-edge reach query, and the freshness is the point: the rows above assert that a door
    /// toggle is visible to the *next* question, so the query must read the world as it is now rather than answer from work
    /// retained across the toggle.
    /// </summary>
    private static Reachability.Reach Reaches(Point from, Point to)
    {
        var world = MovementQueries.World;
        Point? start = CornerGraph.NearestUsable(world, MovementQueries.HoverPoint(from), 2, requireSweep: false);
        Point? goal = CornerGraph.NearestUsable(world, MovementQueries.HoverPoint(to), 2, requireSweep: false);
        if (start == null || goal == null) return Reachability.Reach.No;
        var search = new FreeSpaceSearch(world, start.Value, goal.Value);
        // Unbounded in work as well as in time: a budget here would let a large room answer No for having run out.
        while (!search.Advance(FreeSpaceSearch.NodeLimit)) { }
        return search.Stop == FreeSpaceSearch.StopReason.Found ? Reachability.Reach.Yes
            : search.Stop == FreeSpaceSearch.StopReason.Exhausted ? Reachability.Reach.No
            : Reachability.Reach.Unknown;
    }

    /// <summary>
    /// Printed and not asserted, and the demotion is the finding rather than a concession.
    ///
    /// <para>The walker asserted this: a closed door that was the only passage got opened and walked through. It
    /// worked because a walker with nowhere else to go still walked at the wall, and <c>DoorOpener.Tick</c> opens
    /// whatever closed door is in the tile ahead of a body that is moving — <c>|velocity.X| &gt;= 0.1</c> or a
    /// horizontal collision is its entire trigger.</para>
    ///
    /// <para>Nothing about that mechanism changed. What changed is that the free-space flood treats a closed door
    /// as solid, so with the wall now sealed to the top of the world the goal is proven unreachable, the
    /// navigator asks for no travel at all, and the body never moves: <c>opened=-1 crossed=-1 pushTicks=0</c> with
    /// the body still at its start column. The opener is never offered a door because nothing ever carries the
    /// body to one. That is the limitation the root guide states as "route search still treats a closed door as a
    /// wall" and the roadmap carries as its own card; closing it means teaching the flood that an openable door is
    /// passable, which is a change to what the search searches and is not this fixture's to make.</para>
    ///
    /// <para>The row is kept as a measurement rather than deleted precisely so that the day the flood learns about
    /// doors, the numbers on this line move and somebody notices.</para>
    /// </summary>
    private static void MeasureOpenableDoorAsTheOnlyPassage()
    {
        var run = Follow(locked: false, trench: false);
        Console.WriteLine($"MEASURE doors an openable door as the only passage: {run} "
            + "(the flood proves the goal unreachable through a closed door, so the body never travels to it and the opener never sees it)");
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
        for (int row = WallTop; row < FloorRow - 3; row++) Solid(DoorX, row);
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
        // Column 80, not EastX. Restated on 15 September 2026: with the player at column 62 his region, which always holds him,
        // reached west to column 44 across the sealed wall at 50, so a companion at column 48 on the wrong side was already
        // inside it, accompanied him from there and never took the trench (finalFeetX 48.3 in every locked scene). The premise
        // on the first tick keeps the whole admitted region past the trench, so these scenes stay about crossing the wall.
        const int PlayerColumn = 80;
        player.position = new Vector2(PlayerColumn * 16, FloorRow * 16 - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.position = new Vector2(WestX * 16, FloorRow * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        int opened = -1, crossed = -1, pushTicks = 0, lowest = 0;
        for (int tick = 0; tick < 900; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            VerifyResponsiveFollowing.AdvanceNative(companion);
            if (tick == 0)
            {
                var region = companion.Brain.Senses.Intent.Region;
                float admittedWest = region.Centre.X - region.HalfSize.X + live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator.SettleRadius;
                Require(admittedWest > 56 * 16f,
                    $"the premise: no place the player's region admits may lie on the companion's side of the wall or in the trench; admitted west edge {admittedWest / 16:0.0} tiles, trench ends at column 55");
            }
            if (opened < 0 && Main.tile[DoorX, FloorRow - 2].TileType == TileID.OpenDoor) opened = tick;
            if (crossed < 0 && companion.NPC.Center.X > (DoorX + 2) * 16) crossed = tick;
            if (companion.NPC.collideX) pushTicks++;
            lowest = Math.Max(lowest, (int)(companion.NPC.Center.Y / 16));
        }
        return new FollowRun(opened, crossed, pushTicks, lowest, companion.NPC.Center.X / 16);
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
        MovementQueries.World = new GameTileWorld();
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
