extern alias live;

using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using HandGrant = live::AICompanion.Companion.Brain.ActivityCoordination.HandGrant;
using TerrainChanges = live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges;
using NavGrid = live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid;
using GameTileWorld = live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld;
using LimitPlanningWork = live::AICompanion.Companion.Brain.SharedMovementSystem.LimitPlanningWork;

/// <summary>
/// A26, getting up after downing, through the real companion AI entry point. The NPC owns downing and revival: a living
/// player close beside it revives it after a revive window, and it gets up on its own after a longer one. Every tick while
/// downed must still be one motor application and one control grant with the hand revoked, and the tick after revival must
/// run decisions again with a grant the downed lifecycle no longer owns. The pair differs only in where the player stands.
/// Coverage for behaviour that already held when it was written; the windows are read from the outcome, not restated here.
/// </summary>
internal static class VerifyDowningAndRevival
{
    private const int FloorRow = 80;
    private const int Watch = 900;

    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        int failed = 0;
        revivalTickPresentation = null;
        // The tick after revival runs the whole brain; with the live wall-clock planning allowances in force its verdict would
        // depend on machine load rather than on the lifecycle under test.
        LimitPlanningWork.Unbounded = true;
        try
        {
            failed += VerifyMovementFailures.Case("a player beside the downed companion revives it, and a player out of reach leaves it to get up alone later", RevivalBesideAndAlone, "downing");
        }
        finally { LimitPlanningWork.Unbounded = false; }
        if (revivalTickPresentation != null)
            Console.WriteLine($"MEASURE downing the revival tick publishes a stale presentation: {revivalTickPresentation} (a finding for the presentation owner; every other tick passes the shared check)");
        Console.WriteLine(failed == 0
            ? "downing and revival: revival beside, self-revival apart, revoked hands while downed and restored decisions after"
            : $"downing and revival: {failed} case(s) failed");
        return failed;
    }

    private static void RevivalBesideAndAlone()
    {
        var beside = DownAndWatch(playerOffsetPixels: 24f);
        var apart = DownAndWatch(playerOffsetPixels: 12 * 16f);
        Console.WriteLine($"MEASURE downing player beside: {beside}; player twelve tiles away: {apart}");
        Require(beside.RevivedAtTick > 0, $"premise: a player beside the companion must revive it inside {Watch} ticks; beside {beside}");
        Require(apart.RevivedAtTick > beside.RevivedAtTick,
            $"a player out of reach must not revive the companion as soon as one beside it does; beside {beside}; apart {apart}");
        Require(apart.RevivedAtTick > 0, $"a companion nobody stands beside must still get up on its own; apart {apart}");
    }

    private readonly record struct DownedRun(int RevivedAtTick, int DownedGrants, int HandLeakTicks, bool RestoredLife, bool DecisionsResumed, string OwnerAfter)
    {
        public override string ToString()
            => $"revivedAt={RevivedAtTick} downedGrants={DownedGrants} handLeaks={HandLeakTicks} restoredLife={RestoredLife} decisionsResumed={DecisionsResumed} ownerAfter={OwnerAfter}";
    }

    private static DownedRun DownAndWatch(float playerOffsetPixels)
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[TileID.Stone] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile floor = Main.tile[x, FloorRow];
            floor.ClearEverything();
            floor.HasTile = true;
            floor.TileType = TileID.Stone;
        }
        var companion = VerifyCompanionLifecycle.Create();
        TerrainChanges.Reset();
        NavGrid.World = new GameTileWorld();
        companion.NPC.position = new Vector2(50 * 16 + 8 - companion.NPC.width / 2f, FloorRow * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        Player player = Main.player[0];
        player.dead = false;
        player.statLife = player.statLifeMax2 = 100;
        player.velocity = Vector2.Zero;
        player.Center = companion.NPC.Center + new Vector2(playerOffsetPixels, 0f);

        Tick(companion);
        companion.NPC.life = 0;
        companion.CheckDead();
        Require(companion.IsDowned, "premise: lethal damage must down the companion rather than kill it");

        int downedGrants = 0, handLeaks = 0, revivedAt = -1;
        for (int tick = 1; tick <= Watch; tick++)
        {
            int decisionTick = companion.Brain.Senses.Tick;
            bool wasDowned = companion.IsDowned;
            Tick(companion);
            var grant = companion.Brain.ControlGrants.Last!.Value;
            if (wasDowned)
            {
                downedGrants++;
                if (grant.Hand != HandGrant.Unavailable) handLeaks++;
                Require(companion.Brain.Senses.Tick == decisionTick, $"a downed tick must not run ordinary decisions; tick {tick}");
            }
            if (!companion.IsDowned)
            {
                revivedAt = tick;
                break;
            }
        }
        if (revivedAt < 0)
            return new DownedRun(-1, downedGrants, handLeaks, false, false, "-");
        bool restoredLife = companion.NPC.life == companion.NPC.lifeMax && !companion.NPC.dontTakeDamage;
        int before = companion.Brain.Senses.Tick;
        Tick(companion);
        var after = companion.Brain.ControlGrants.Last!.Value;
        Require(!companion.IsDowned && after.AppliedOwner != "downed" && after.Hand != HandGrant.Unavailable,
            $"the tick after revival must be an ordinary grant with the hand back; owner={after.AppliedOwner} hand={after.Hand}");
        Require(downedGrants > 0 && handLeaks == 0, $"every downed tick must be one grant with the hand revoked; grants={downedGrants} leaks={handLeaks}");
        return new DownedRun(revivedAt, downedGrants, handLeaks, restoredLife, companion.Brain.Senses.Tick > before, after.AppliedOwner);
    }

    /// <summary>The published presentation on the tick the companion gets up, when it still says downed; a finding printed by
    /// Run, not a pass. The downed path publishes the tick's presentation while applying the downed controls and decides
    /// revival afterwards, so the revival tick's presentation describes the state before the NPC got up.</summary>
    private static string? revivalTickPresentation;

    private static void Tick(CompanionNPC companion)
    {
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        bool downedBefore = companion.IsDowned;
        long applications = companion.Motor.ControlApplications;
        long priorGrant = companion.Brain.ControlGrants.Last?.Id ?? 0;
        try
        {
            VerifyCompanionLifecycle.TickWithOneControlGrant(companion);
        }
        catch (InvalidOperationException error) when (downedBefore && !companion.IsDowned
            && error.Message.StartsWith("presentation must publish", StringComparison.Ordinal)
            && companion.Brain.Presentation.Downed && PresentationMatchesExceptDowned(companion))
        {
            // Only the downed flag disagrees, and only on the tick of revival. The shared check stops at presentation, so the
            // control contract it would have checked next is checked here: one motor application and one fresh grant.
            var grant = companion.Brain.ControlGrants.Last;
            Require(companion.Motor.ControlApplications == applications + 1 && grant is { } g && g.Id == priorGrant + 1
                    && g.Tick == Main.GameUpdateCount && g.MotorApplications == 1,
                $"the revival tick must still be one motor application and one grant; applications={companion.Motor.ControlApplications - applications} grant={grant}");
            revivalTickPresentation = revivalTickPresentation == null
                ? "presentation says downed=True on the tick the companion gets up"
                : "presentation says downed=True on the tick the companion gets up, on every revival watched";
        }
        catch (InvalidOperationException error)
        {
            // The shared check names no field; name every one it compares, so a failure says which state disagreed.
            var brain = companion.Brain;
            var p = brain.Presentation;
            throw new InvalidOperationException($"{error.Message} at engine tick {Main.GameUpdateCount}, downed {downedBefore}->{companion.IsDowned}: "
                + $"presentation tick={p.Tick} activityId={p.ActivityId} family={p.Family} activity={p.Activity} phase={p.Phase} downed={p.Downed} recovering={p.Recovering} safety={p.SafetyActive}; "
                + $"expected tick={Main.GameUpdateCount} activityId={brain.Chooser.Activity.Id} family={brain.Chooser.Current?.Family} activity={brain.Chooser.Current?.Name} "
                + $"phase={brain.Chooser.Activity.Phase} downed={companion.IsDowned} recovering={brain.FollowRecovery.Active} safety={brain.Safety.Active}");
        }
        VerifyResponsiveFollowing.AdvanceNative(companion);
    }

    private static bool PresentationMatchesExceptDowned(CompanionNPC companion)
    {
        var brain = companion.Brain;
        var p = brain.Presentation;
        return p.Tick == Main.GameUpdateCount && p.ActivityId == brain.Chooser.Activity.Id
            && p.Family == brain.Chooser.Current?.Family && p.Activity == brain.Chooser.Current?.Name
            && p.Phase == brain.Chooser.Activity.Phase && p.Recovering == brain.FollowRecovery.Active
            && p.SafetyActive == brain.Safety.Active;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
