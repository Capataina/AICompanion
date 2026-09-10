extern alias live;

using System.Reflection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;

using FollowPlayerObjective = live::AICompanion.Companion.Brain.PositionSelection.FollowPlayerObjective;
using RequestKind = live::AICompanion.Companion.Brain.PositionSelection.RequestKind;

/// <summary>
/// Exercises the production brain through its live alias. The player is moved directly because
/// this fixture measures the brain's response to observed motion, not Terraria's player physics;
/// companion controls still pass through the native NPC collision adapter every tick.
/// </summary>
internal static class VerifyResponsiveFollowing
{
    public static int Run()
    {
        VerifyTwoAxisObjective();
        VerifyVerticalPlayerMotionReachesTheProductionFollowAction();
        VerifyOccludedPlayerStillProvidesADestination();
        VerifyCturnCompletesThroughTheProductionBrain();
        Console.WriteLine("responsive following: vertical intent, two-axis arrival and live brain follow selection passed");
        return 0;
    }

    private static void VerifyTwoAxisObjective()
    {
        var objective = new FollowPlayerObjective(new Vector2(480, 800), new Vector2(480, 752));
        Require(!objective.IsSatisfied(new Vector2(480, 1120), locallyConnected: true),
            "a companion directly below the player on another floor must not satisfy following");
        Require(!objective.AcceptsDestination(new Vector2(480, 1120), locallyConnected: true),
            "a different-floor incumbent must not remain a valid following destination");
        Require(objective.AcceptsDestination(new Vector2(640, 752), locallyConnected: true),
            "a nearby standable point in the predicted player region remains a valid following destination");
        Require(!objective.IsSatisfied(new Vector2(480, 800), locallyConnected: false),
            "a nearby but sealed floor must not satisfy following without a local connection");
    }

    private static void VerifyVerticalPlayerMotionReachesTheProductionFollowAction()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(480, 758);
        companion.NPC.position = new Vector2(480, 1238);
        companion.NPC.velocity = Vector2.Zero;

        bool sawVerticalIntent = false;
        for (int tick = 0; tick < 12; tick++)
        {
            player.velocity = new Vector2(0, -4);
            player.position += player.velocity;
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            sawVerticalIntent |= companion.Brain.Senses.Player.Intent.Y < -1.2f;
            AdvanceNative(companion);
        }

        Require(sawVerticalIntent, "vertical-only player movement must produce travel intent");
        Require(companion.Brain.LastRequest.Kind == RequestKind.WithPlayer,
            "the production chooser must keep a vertically moving player in the follow action");
        Require(!companion.Brain.Positioner.FollowObjectiveSatisfied
            && companion.Brain.Positioner.FollowVerticalGap > 0f,
            "the live positioner must report unsatisfied vertical follow progress instead of accepting the lower floor");
    }

    private static void VerifyCturnCompletesThroughTheProductionBrain()
    {
        BuildCturn();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(70 * 16, 80 * 16 - player.height);
        companion.NPC.position = new Vector2(50 * 16, 70 * 16 - BodyPhysics.Height);
        companion.NPC.velocity = Vector2.Zero;

        bool initiallyMovedAway = false;
        for (int tick = 0; tick < 720; tick++)
        {
            // The observed player is travelling right, so the leftward first control cannot be a
            // travel-bias artefact: the only useful route leaves the pocket to the left.
            player.velocity = new Vector2(1f, 0f);
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            initiallyMovedAway |= companion.Motor.AppliedControls.MoveX < 0f;
            Require(!companion.Brain.Navigator.SearchPending || !companion.Brain.Navigator.LastPlanFailed,
                "an unfinished retained search must not be classified failed before it publishes a prefix");
            Require(companion.Brain.Navigator.Path is not { Finished: false } || !companion.Brain.Navigator.LastPlanFailed,
                "a usable route prefix must not be classified as a failed plan");
            AdvanceNative(companion);
            if (companion.Brain.Positioner.FollowObjectiveSatisfied)
                break;
        }
        Require(initiallyMovedAway,
            "the production follow route must accept the initially-away first leg of a C-turn");
        Require(companion.Brain.Positioner.FollowObjectiveSatisfied,
            "the production brain must complete the C-turn at the player's usable floor rather than hold its start pocket");
    }

    private static void VerifyOccludedPlayerStillProvidesADestination()
    {
        BuildFloor();
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.player[0];
        player.dead = false;
        player.position = new Vector2(54 * 16, 80 * 16 - player.height);
        companion.NPC.position = new Vector2(46 * 16, 80 * 16 - companion.NPC.height);
        for (int y = 77; y < 80; y++)
        {
            Tile door = Main.tile[50, y];
            door.HasTile = true;
            door.TileType = Terraria.ID.TileID.ClosedDoor;
            door.TileFrameY = (short)((y - 77) * 18);
        }
        Main.tileSolid[Terraria.ID.TileID.ClosedDoor] = true;
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        Require(!Collision.CanHitLine(companion.NPC.position, companion.NPC.width, companion.NPC.height,
            player.position, player.width, player.height), "closed-door fixture must occlude the player");
        VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
        companion.AI();
        Require(companion.Brain.LastRequest.Kind == RequestKind.WithPlayer,
            "nearby occluded player must still request following");
        Require(companion.Brain.Positioner.Chosen is Vector2 goal && goal.X > 50 * 16,
            "a closed door must not veto player-side follow destinations");
    }

    private static void BuildFloor()
    {
        Main.maxTilesX = Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 80];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
    }

    private static void BuildCturn()
    {
        BuildFloor();
        // The companion begins on the raised shelf. A ceiling and right wall make the direct
        // player direction impossible; it must walk left, drop through the opening, then return
        // right along the lower floor. This is the smallest native geometry that catches a
        // partial-result policy which rejects every first step that increases Euclidean distance.
        for (int x = 40; x <= 60; x++) Solid(x, 70);
        for (int x = 40; x <= 60; x++) Solid(x, 64);
        for (int y = 65; y <= 70; y++) Solid(60, y);
        live::AICompanion.Companion.Brain.SharedMovementSystem.TerrainChanges.Reset();
        live::AICompanion.Companion.Brain.SharedMovementSystem.NavGrid.World = new live::AICompanion.Companion.Brain.SharedMovementSystem.GameTileWorld();
    }

    private static void AdvanceNative(live::AICompanion.Companion.CharacterBody.CompanionNPC companion)
    {
        // The production motor has already applied MovementAbilities and StepUp/StepDown. Calling
        // VerifyEngineMotion.RunEngine here would apply the same controls a second time, so finish
        // this tick with Terraria's own gravity and collision phases only.
        NPC npc = companion.NPC;
        Invoke(npc, "UpdateNPC_UpdateGravity");
        npc.velocity.Y = MathF.Min(npc.velocity.Y + npc.gravity, npc.maxFallSpeed);
        Invoke(npc, "UpdateCollision");
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }

    private static void Invoke(NPC npc, string method)
        => typeof(NPC).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(npc, null);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
