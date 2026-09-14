extern alias live;

using System.Reflection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;
using Terraria;

/// <summary>
/// The engine tier's half of the kit's reset: what this process must hold before any fixture runs,
/// and what must go back to a known state between one case and the next.
///
/// Two separate jobs, and they were conflated before. <see cref="PrepareProcess"/> is the engine
/// containers and the miniature world every fixture assumes exist; it is idempotent and runs once.
/// <see cref="BeforeCase"/> is the process-wide statics a case can reach and leave changed, put back
/// so that a red names the case that went red rather than the one before it.
///
/// Both halves of every static are written, the copy this project compiles and the copy inside the
/// mod assembly, because EngineReplay compiles the movement core a second time beside the `live`
/// alias: a fixture driving the whole brain moves the live statics and a fixture driving
/// <c>CoordinateMovement</c> directly moves this project's, and a reset that named one of them would
/// leave the other holding the previous case's world.
/// </summary>
internal static class ResetProcessState
{
    private static bool prepared;

    /// <summary>
    /// The setup every fixture in this executable assumes: a save path before anything touches
    /// <c>Main</c>, headless mode, the miniature world's dimensions and tile map, and — the part
    /// that was missing — an object in every NPC and player slot.
    ///
    /// It used to be the first fourteen lines of <c>VerifyEngineMotion.Run</c>, which meant every
    /// flag in <c>Program.cs</c> skipped it, because each flag returns before that method is ever
    /// called. <c>--observation</c> is the case that showed it: standalone it died with a null
    /// reference inside <c>CompanionNPC.Find</c>, which walks <c>Main.ActiveNPCs</c> over an array
    /// whose entries no fixture had filled, while inside the default suite the same code passed
    /// because a lifecycle fixture earlier in the table had filled them. That is one bug wearing two
    /// faces — a crash when run alone and an order dependence when run in a suite — and moving the
    /// setup to where every entry point reaches it closes both.
    /// </summary>
    internal static void PrepareProcess()
    {
        if (prepared) return;
        prepared = true;
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Main.maxTilesX = 100;
        Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;
        Main.tileSolid[19] = Main.tileSolidTop[19] = true;
        FillActorSlots();
    }

    /// <summary>
    /// Every process-wide static a case can reach, put back to what a fresh process would hold.
    ///
    /// <paramref name="keepProductionAllowances"/> is true only for a case that exercises a deadline,
    /// and it is the one input: every other case runs with the millisecond allowances lifted, so the
    /// wall clock cannot decide how far a search got and a fixture cannot end up timing the machine
    /// by forgetting to lift them. The work-count limits each query carries are untouched either
    /// way — they are what still bounds the search.
    ///
    /// The route archive and the terrain log are cleared together because that is the pair the mod's
    /// own world lifecycle clears together (<c>ResetTerrainChanges.OnWorldLoad</c>), and a fixture
    /// that builds a new world while the previous world's executed routes are still remembered is
    /// planning over terrain that no longer exists.
    /// </summary>
    internal static void BeforeCase(bool keepProductionAllowances)
    {
        LimitPlanningWork.Unbounded = !keepProductionAllowances;
        LimitPlanningWork.End();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = !keepProductionAllowances;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.End();

        // Every one of these is flat in ...Infrastructure.Movement: RoutePlanning/ and
        // MovementExecution/ are folders that the files inside do not turn into namespaces, so a
        // qualified name built from the path does not compile.
        TerrainChanges.Reset();
        RememberExecutedRoutes.World.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.RememberExecutedRoutes.World.Clear();

        BehaviourCensus.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Reset();

        ResetSearchPolicy();
        FillActorSlots();
    }

    /// <summary>
    /// The search's own switches, which are set per request in production and per scene in a
    /// fixture, so a case that sets one and returns leaves the next case searching under a rule it
    /// never asked for. <c>AllowOneWayDrops</c> is the dangerous one: it defaults to true, a
    /// collection fixture turns it off to prove a drop is refused, and a later fixture inheriting
    /// that refuses routes it should take.
    /// </summary>
    private static void ResetSearchPolicy()
    {
        AStar.AllowLava = false;
        AStar.AllowOneWayDrops = true;
        AStar.MsBudget = 0;
        AStar.TraceClosed = null;
        AStar.InvalidateEdges();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.AllowLava = false;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.AllowOneWayDrops = true;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.MsBudget = 0;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.TraceClosed = null;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.AStar.InvalidateEdges();
    }

    /// <summary>
    /// An object in every NPC and player slot, including the inactive ones.
    ///
    /// The engine treats a slot as a record that is switched off rather than as a slot that may be
    /// empty: <c>Main.ActiveNPCs</c> reads <c>npc.type</c> on its way past, native tile placement
    /// walks every player slot, and <c>WorldGen.CloseDoor</c> walks them all through
    /// <c>Collision.EmptyTile</c>. A null slot is therefore a harness defect rather than evidence
    /// about anything, and it is filled here once instead of in each fixture that happened to hit it.
    ///
    /// Slots already holding an object are left exactly as they are, because a case builds its own
    /// scene and this runs before it, not instead of it.
    /// </summary>
    private static void FillActorSlots()
    {
        if (Main.npc == null) Main.npc = new NPC[Main.maxNPCs + 1];
        for (int i = 0; i < Main.npc.Length; i++)
            Main.npc[i] ??= new NPC { whoAmI = i, active = false };
        if (Main.player == null) Main.player = new Player[Main.maxPlayers + 1];
        for (int i = 0; i < Main.player.Length; i++)
            Main.player[i] ??= new Player();
    }

    /// <summary>Registered once, from the entry point, so every case in every suite runs through it.</summary>
    internal static void Register() => EmitLedgerRows.ResetBeforeCase = BeforeCase;
}
