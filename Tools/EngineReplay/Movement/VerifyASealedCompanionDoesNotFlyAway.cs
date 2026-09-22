extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Tools.Ledger;
using GameTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.GameTileWorld;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using TerrainChanges = live::AICompanion.Companion.Brain.Infrastructure.Movement.TerrainChanges;

/// <summary>
/// A companion that cannot reach the player does not travel away from him. This is `AIC-442`'s class, and
/// the fixture exists because the scene that found it could not assert it.
///
/// <para>On 21 September 2026 all three of the locked-door rows in `../Lifecycle/VerifyDoorPassage.cs` read
/// `opened=-1 crossed=293`, byte-identically for the door and for the solid-stone control, which looked exactly
/// like a companion walking through a locked temple door. It was not: the body flew from column 38 to column 8 —
/// thirty tiles *away* from a player at column 80 — until the straight-line separation finally admitted recovery
/// flight at tick 192, and recovery, which ignores terrain by design, carried it through the sealed wall. `5ddac5b`
/// and `968e448` added the premise and split the travel by phase so the door rows stopped lying; `52f90c9` found the
/// cause, an empty course's companionship anchor defaulting to `Vector2.Zero`, the world's top-left corner.</para>
///
/// <para>Two things that fix left behind. The door fixture's premise refuses a run that entered recovery, so it
/// reports the class only by *declining to report on doors* — a negative, and one that would be equally satisfied by
/// a body that simply never moved. And the class has no row of its own anywhere, so nothing would notice its return
/// through a different anchor, a different fallback or a different empty-course path. This is that row, and it is
/// deliberately not in the door file: a door has nothing to do with it, and the reason it was found there is that a
/// door scene happens to seal a companion off.</para>
///
/// <para>What it asserts is what the tree now does and what the next reader should be able to rely on: never travel
/// away. It does not assert that the body closes on the barrier, which would be a different behaviour and one this
/// tree does not have — the positioner admits no candidate inside a region that lies wholly on the player's side, so
/// the follow intent goes to `SeekDestination`, the navigator proves the anchor unreachable and hovers at a wait
/// anchor where the body stood. Whether it should instead go as near to him as free space allows is a design question
/// for the owner and is written up rather than built, because the walker's partial-progress destination — the
/// reachable tile that most reduced the objective's gap — was deleted for being a destination that could be arrived
/// at without achieving anything, and "the nearest reachable corner to the player" is that design wearing new
/// mechanics.</para>
/// </summary>
internal static class VerifyASealedCompanionDoesNotFlyAway
{
    private const int FloorRow = 80, WallColumn = 50, StartColumn = 38, PlayerColumn = 80, Ticks = 900;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        LimitPlanningWork.Unbounded = true;
        try { return Sealed(); }
        finally { LimitPlanningWork.Unbounded = false; }
    }

    private static int Sealed()
    {
        BuildWorld();
        var companion = VerifyCompanionLifecycle.Create();
        for (int i = 0; i < Main.player.Length; i++) Main.player[i] ??= new Player();
        TerrainChanges.Reset();
        MovementQueries.World = new GameTileWorld();

        Player player = Main.player[0];
        player.dead = false;
        player.active = true;
        player.statLife = player.statLifeMax2 = 100;
        player.position = new Vector2(PlayerColumn * 16f, FloorRow * 16f - player.height);
        player.velocity = Vector2.Zero;
        companion.NPC.active = true;
        companion.NPC.position = new Vector2(StartColumn * 16f, FloorRow * 16f - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;

        float startColumn = companion.NPC.Center.X / 16f;
        float startGap = Vector2.Distance(companion.NPC.Center, player.Center);
        float westmost = startColumn, widestGap = startGap;
        string westmostOwner = "none", widestOwner = "none";
        // Split by phase, because the two halves tell opposite stories and a run-wide minimum cannot separate them: a body
        // that travels west and *then* trips the recovery radius is a following defect, while one that trips it first and
        // travels west during recovery is a recovery defect. The before-recovery figures are also what makes the following
        // assertions provable when the premise below is the thing that fails — assert them first, or a mutation that ends
        // in recovery aborts the row before the assertion it was planted for ever runs.
        float westBefore = startColumn, gapBefore = startGap;
        string westBeforeOwner = "none", gapBeforeOwner = "none";
        int recoveryTicks = 0, recoveryStart = -1;
        for (int tick = 0; tick < Ticks; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            VerifyResponsiveFollowing.AdvanceNative(companion);
            if (companion.Motor.ControlSource == "follow-recovery-flight")
            {
                if (recoveryTicks == 0) recoveryStart = tick;
                recoveryTicks++;
            }
            string owner = $"{companion.Brain.LastAction?.Name ?? "none"}/{companion.Brain.LastRequest.Kind}/{companion.Motor.ControlSource}";
            float column = companion.NPC.Center.X / 16f;
            if (column < westmost) { westmost = column; westmostOwner = owner; }
            float gap = Vector2.Distance(companion.NPC.Center, player.Center);
            if (gap > widestGap) { widestGap = gap; widestOwner = owner; }
            if (recoveryTicks == 0)
            {
                if (column < westBefore) { westBefore = column; westBeforeOwner = owner; }
                if (gap > gapBefore) { gapBefore = gap; gapBeforeOwner = owner; }
            }
        }

        string run = $"start column {startColumn:0.0}, westmost {westmost:0.0} at [{westmostOwner}], before recovery {westBefore:0.0} at [{westBeforeOwner}], "
            + $"final {companion.NPC.Center.X / 16f:0.0}; start gap {startGap / 16f:0.0} tiles, widest {widestGap / 16f:0.0} at [{widestOwner}], "
            + $"before recovery {gapBefore / 16f:0.0} at [{gapBeforeOwner}]; recovery {recoveryTicks} ticks from {recoveryStart}";

        // Two tiles of slack, which is the hover's own drift about a held spot plus the settle radius, so an orb that is
        // doing nothing but hovering where it lost the way passes and a body under a directional ask does not. These run
        // before the premise on purpose: a defect that ends in recovery would otherwise abort the row on the premise and
        // leave the assertion it was planted for unproven.
        Require(westBefore >= startColumn - 2f,
            $"a companion sealed off from the player must not travel away from him: before any recovery it went from column {startColumn:0.0} "
            + $"west to {westBefore:0.0}, owned by [{westBeforeOwner}]. That is the AIC-442 class — the anchor a follow request falls back on when "
            + $"the positioner admits no candidate is read only on exactly these ticks, so a wrong one is invisible until it is the whole answer. {run}");
        Require(gapBefore <= startGap + 2f * 16f,
            $"a companion sealed off from the player must not widen the gap to him: it began {startGap / 16f:0.0} tiles away and reached "
            + $"{gapBefore / 16f:0.0} before any recovery, owned by [{gapBeforeOwner}]. {run}");

        // Then the premise. Recovery flight ignores terrain by design, so a run that entered it is measuring recovery rather
        // than following, and everything after this line would be a verdict about the wrong mechanism. The door fixture
        // learned that the expensive way and the same sentence belongs here.
        Require(recoveryTicks == 0,
            $"the premise: recovery flight ignores terrain by design, so a run that enters it is no longer measuring what following did; {run}");
        Require(westmost >= startColumn - 2f && widestGap <= startGap + 2f * 16f,
            $"with no recovery in the run the whole-run figures must match the before-recovery ones; {run}");

        EmitLedgerRows.Detail($"sealed companion: {run}");
        // Printed rather than asserted: how near the barrier the body ends up is the open design question above, and a
        // number here would lock in today's answer, which is "it holds where it lost the way".
        EmitLedgerRows.Measure(VerifyEngineMotion.Instrument, "Movement", "sealed-companion-final-distance-from-barrier",
            WallColumn - companion.NPC.Center.X / 16f, "tiles", message: "how far short of the sealed wall the body ends; not a pass line, see the class docstring");
        return 0;
    }

    /// <summary>
    /// A hundred-tile world, a floor, and a stone wall from the top of the world down to the floor at column 50. The wall
    /// runs to row zero deliberately: for a body that flies, the only sealed wall is one that reaches the world margin,
    /// and the door fixture's first orb run crossed a "wall" that stopped thirty rows above the floor by going over it.
    /// </summary>
    private static void BuildWorld()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[TileID.Stone] = true;
        foreach (FieldInfo field in typeof(TileLoader).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
            if (field.Name.StartsWith("Hook") && field.FieldType.IsArray && field.GetValue(null) == null)
                field.SetValue(null, Array.CreateInstance(field.FieldType.GetElementType()!, 0));
        for (int x = 5; x < 95; x++) Solid(x, FloorRow);
        for (int y = 0; y < FloorRow; y++) Solid(WallColumn, y);
        TerrainChanges.Reset();
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.ClearEverything();
        tile.HasTile = true;
        tile.TileType = TileID.Stone;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
