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
        RebuildTheMiniatureWorld();
        Main.tileSolid[1] = true;
        Main.tileSolid[19] = Main.tileSolidTop[19] = true;
        FillActorSlots();
    }

    /// <summary>
    /// The miniature world every fixture assumes: a hundred tiles square, empty, the surface at fifty.
    /// It is rebuilt before every case rather than once per process, because a fixture that writes
    /// walls into <c>Main.tile</c> and never clears them hands that terrain to whatever runs next —
    /// the route-endings fixture's sealed pocket and open room sat across the row the projectile
    /// fixture fires along, and the projectile case failed in the suite while passing alone. A case
    /// that wants a different size builds its own map, as the pursuit scene does, and the next case
    /// gets the empty hundred-square back.
    /// </summary>
    private static void RebuildTheMiniatureWorld()
    {
        Main.maxTilesX = 100;
        Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
            new object[] { (ushort)100, (ushort)100 }, null)!;
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
        // The allowance is *installed* here, not merely cleared. Combat's tactical work and the aimer
        // borrow `LimitPlanningWork.Current`, which throws when nothing is standing; in production the
        // brain tick always has one open around every one of those calls, so a reset that only ended
        // the previous case's budget left eleven behaviour fixtures throwing
        // "Decision work must begin before borrowing its budget" the moment combat moved off its own
        // private PlanningBudget. Beginning one per case is the harness half of the production
        // contract rather than a lenience: the millisecond figure is the production constant, and the
        // lift above turns it into an infinite deadline for every case that is not about a deadline,
        // so the work-count limits each query carries stay the only bound.
        //
        // Rejected: catching the throw inside `Current` by lazily minting a budget when none is
        // standing. That reads as the smaller change and destroys the guarantee the throw exists for —
        // the plan's "no consumer can silently buy a second production deadline after exhausting the
        // first" — by making the absence of an owner indistinguishable from a fresh allowance.
        // Also rejected: wrapping each of the eleven fixtures, which fixes today's eleven and leaves
        // the twelfth fixture's author to discover the obligation through a stack trace.
        LimitPlanningWork.Unbounded = !keepProductionAllowances;
        LimitPlanningWork.Restart(live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.TotalPlanningMilliseconds);
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = !keepProductionAllowances;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Restart(
            live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.TotalPlanningMilliseconds);

        // Every one of these is flat in ...Infrastructure.Movement: Contact/, FreeSpace/ and
        // Steering/ are folders that the files inside do not turn into namespaces, so a qualified
        // name built from the path does not compile.
        TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges.Reset();

        BehaviourCensus.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Reset();

        // What the weapons have learned about each enemy scales every hit the arsenal scores, so a case that swung a sword
        // at a zombie would otherwise hand the next case a damage ratio and a push it never observed.
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.WeaponEffects.Reset();
        // What each weapon's attacks achieved scales every forecast the same way, the outcome windows hold projectile slots a
        // rebuilt world reuses, and the landed-hit ledger holds shot identities for those slots: a case inherits none of them.
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.AttackLearning.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.ShotOutcomes.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.Clear();
        // The flight recorder's open traces and grouped uses, the volley shapes they taught, the arcs the
        // traces fed and the cursor the spoof may be holding: a case inherits none of the previous case's sky.
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording.RecordProjectileFlights.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording.GroupSpawnsIntoUses.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnVolleyShapes.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.FitFlightLaws.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnWallResponses.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnHitResponses.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning.LearnChildSpawns.Reset();
        // The revision and the sim cache it keys: a case that refitted laws and a later case that reset them would
        // otherwise share revision numbers with different beliefs behind them, and the later case would read sims
        // priced under the earlier case's laws.
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.KnowledgeRevision.Reset();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CacheSimulatedUses.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CacheSimulatedUses.Enabled = true;
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CachePlannedSims.Clear();
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CachePlannedSims.Enabled = true;
        live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ApplyCompanionModifiers.Planted =
            live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.ModifierState.None;
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.SpoofOwnerInputForShots.Clear();
        // Which strike is in flight and the open boss fight are slot memories a rebuilt world reuses; a fixture that repriced
        // either slime reading restores the game's own. The companion's torches, the spots the player cleared of one and the
        // torch tiles already paid for are world memories, which a rebuilt world does not have.
        live::AICompanion.Companion.Progression.CreditKillsAndFights.Reset();
        live::AICompanion.Companion.Progression.CompanionExperience.DefaultEnemyLife = live::AICompanion.Companion.Progression.CompanionExperience.GreenSlimeLifeInThisWorld;
        live::AICompanion.Companion.Progression.CompanionExperience.NormalEnemyLife = () => live::AICompanion.Companion.Progression.CompanionExperience.GreenSlimeLife(Terraria.DataStructures.GameModeData.NormalMode);
        live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch.CompanionTorches.Clear();
        live::AICompanion.Companion.Progression.CreditWork.ForgetPaidTorches();
        // The world's light is a scan of the last case's tiles, and the engine's state would hand that scan to the next case's
        // first observation; both go back to a process that has drawn nothing.
        VerifyUsefulAssistance.ForgetEngineLight();

        // The player's own choices are a process-wide singleton — both work policies, combat, pot
        // breaking, torch placement, the distance mode and the mining list — and until now they were
        // the one reachable static no case put back. A fresh instance *is* a fresh process's value by
        // construction, because every default lives on the property initialisers, so this cannot drift
        // from what the game starts with the way a list of assignments here would.
        //
        // It is written as a reset rather than as a line in the fixture that found it, deliberately.
        // `VerifyOreWork.SetUp` sets the mining policy and never the chopping one, so its sealed-tree
        // row read whichever chopping policy the process was last left holding: run alone it found the
        // Opportunistic default and passed, and run in the suite it found a Disabled or Mimic policy
        // left by a cooperation row, took the early return above the reach block, and reported the
        // reach search as never reusing its verdict — the counter reading 81 after 20 preparations,
        // which is the branch never being reached at all rather than being reached too often. Setting
        // the chopping policy in that one row would fix that row and leave every other reader of these
        // seven fields with the same order dependence, unannounced.
        live::AICompanion.Companion.PlayerIntegration.CompanionPreferences.Current =
            new live::AICompanion.Companion.PlayerIntegration.CompanionPreferences();

        // The map before the search policy, because the policy plugs a fresh world wrapper over
        // whatever map is standing, and the wrapper has to wrap the empty one.
        RebuildTheMiniatureWorld();
        ResetSearchPolicy();
        FillActorSlots();
    }

    /// <summary>
    /// The orb's own per-process search state, which a case sets for its scene and would otherwise
    /// leave standing for the next one: the search's world override, the clearance field and the world.
    ///
    /// The world is rebuilt rather than merely reset, and the fresh object is the point. Every
    /// clearance chunk holds the world it was built over and compares by reference, so a new
    /// <c>GameTileWorld</c> invalidates the whole field without the field being told — which matters
    /// because a case that rebuilds <c>Main.tile</c> changes every tile under a chunk whose revision
    /// counter never moved. The getter also throws when nothing is plugged in, so setting it here is
    /// what lets a fixture that never names a world run at all.
    /// </summary>
    private static void ResetSearchPolicy()
    {
        FreeSpaceSearch.WorldOverride = null;
        ClearanceField.Shared.Invalidate();
        MovementQueries.World = new GameTileWorld();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch.WorldOverride = null;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.ClearanceField.Shared.Invalidate();
        live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries.World =
            new live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld();
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
