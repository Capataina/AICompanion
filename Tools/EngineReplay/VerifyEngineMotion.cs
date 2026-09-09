using System.Reflection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;

internal static class VerifyEngineMotion
{
    public static int Run()
    {
        typeof(Terraria.Program).GetField("SavePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.GetTempPath());
        Main.dedServ = true;
        Main.maxTilesX = 100;
        Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;
        for (int x = 5; x < 95; x++)
        {
            Tile tile = Main.tile[x, 60];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        Main.tileSolid[19] = Main.tileSolidTop[19] = true;
        int checkedCases = 0, failed = 0;
        foreach (int altitude in new[] { 0, 30 })
        foreach (int shape in Enumerable.Range(0, 7))
        foreach (int liquid in Enumerable.Range(0, 4))
        foreach (float left in new[] { 390f, 398f, 405f })
        foreach (float bottom in new[] { 951f, 960f, 925f })
        foreach (Controls controls in new[] { Controls.None, new Controls(4), new Controls(-4, Jump: true), new Controls(2, FallThrough: true, Descend: true) })
        {
            for (int x = 20; x < 35; x++)
            {
                Tile support = Main.tile[x, 60 + altitude];
                support.HasTile = true;
                support.TileType = (ushort)(shape >= 5 ? 19 : 1);
                support.Slope = (Terraria.ID.SlopeType)(shape is >= 1 and <= 4 ? shape : shape == 6 ? 1 : 0);
                for (int y = 50 + altitude; y < 60 + altitude; y++)
                {
                    Tile water = Main.tile[x, y];
                    water.LiquidAmount = liquid == 0 ? (byte)0 : (byte)255;
                    water.LiquidType = liquid == 1 ? 0 : liquid == 2 ? 2 : 3;
                }
            }
            var state = new BodyState(left, bottom + altitude * 16, 2.4f, bottom == 960 ? 0 : 1.2f, bottom == 960,
                Wet: liquid != 0, LiquidKind: liquid == 1 ? 0 : liquid);
            var scratch = Scratch();
            BodyState predicted = SimulateTerrariaBody.Step(state, controls, MovementCapabilities.Basic);
            if (Scratch() != scratch) throw new InvalidOperationException("Prediction changed Collision scratch fields");
            BodyState actual = RunEngine(state, controls);
            checkedCases++;
            if (Vector2.Distance(predicted.Feet, actual.Feet) > .001f || MathF.Abs(predicted.Vx - actual.Vx) > .001f
                || MathF.Abs(predicted.Vy - actual.Vy) > .001f || predicted.Wet != actual.Wet
                || predicted.LiquidKind != actual.LiquidKind || predicted.StairFall != actual.StairFall
                || predicted.OnGround != actual.OnGround || predicted.CollideX != actual.CollideX)
            {
                if (failed++ < 12) Console.WriteLine($"FAIL shape={shape} liquid={liquid} entry={state} controls={controls}\n predicted={predicted}\n engine={actual}");
            }
        }
        foreach (int previous in Enumerable.Range(0, 4))
        foreach (int current in Enumerable.Range(0, 4))
        {
            for (int x = 20; x < 35; x++)
            for (int y = 50; y < 60; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.LiquidAmount = current == 0 ? (byte)0 : (byte)255;
                tile.LiquidType = current == 1 ? 0 : current == 2 ? 2 : 3;
            }
            var state = new BodyState(400, 925, 3, 1, false, Wet: previous != 0, LiquidKind: previous == 1 ? 0 : previous);
            BodyState predicted = SimulateTerrariaBody.Step(state, Controls.None, MovementCapabilities.Basic);
            BodyState actual = RunEngine(state, Controls.None);
            checkedCases++;
            if (predicted != actual)
            {
                failed++;
                Console.WriteLine($"FAIL liquid transition {previous}->{current}: predicted={predicted} engine={actual}");
            }
        }
        Console.WriteLine($"engine motion: {checkedCases - failed}/{checkedCases} matched native NPC collision; {failed} mismatches; Collision scratch preserved");
        failed += VerifyObservedMotion.Run();
        failed += VerifyGodsEyeEvents.Run();
        failed += VerifyRoutes();
        failed += VerifyProjectileMotion.Run();
        failed += VerifyPersonalDanger.Run();
        return failed == 0 ? 0 : 1;
    }

    private static BodyState RunEngine(BodyState state, Controls controls)
    {
        var npc = new NPC
        {
            width = BodyPhysics.Width, height = BodyPhysics.Height, type = 0,
            aiStyle = controls.FallThrough ? 10 : -1, lavaImmune = true, wetCount = 2,
            position = new Vector2(state.Left, state.Bottom - BodyPhysics.Height), velocity = new Vector2(state.Vx, state.Vy),
            wet = state.Wet, honeyWet = state.LiquidKind == 2, shimmerWet = state.LiquidKind == 3,
            lavaWet = state.LiquidKind == 1, stairFall = state.StairFall,
            waterMovementSpeed = .5f, lavaMovementSpeed = .5f, honeyMovementSpeed = .25f, shimmerMovementSpeed = .375f
        };
        Invoke(npc, "UpdateNPC_UpdateGravity");
        BodyState driven = MovementAbilities.ApplyControls(state, controls, MovementCapabilities.Basic);
        npc.velocity = new Vector2(driven.Vx, driven.Vy);
        if (npc.velocity.Y == 0 && !controls.FallThrough)
            Collision.StepDown(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY);
        if (npc.velocity.Y >= 0)
            Collision.StepUp(ref npc.position, ref npc.velocity, npc.width, npc.height, ref npc.stepSpeed, ref npc.gfxOffY, 1, !controls.Descend, 1);
        npc.velocity.Y = MathF.Min(npc.velocity.Y + npc.gravity, npc.maxFallSpeed);
        if (MathF.Abs(npc.velocity.X) < .005f) npc.velocity.X = 0;
        Invoke(npc, "UpdateCollision");
        return driven with { Left = npc.position.X, Bottom = npc.Bottom.Y, Vx = npc.velocity.X, Vy = npc.velocity.Y,
            OnGround = npc.velocity.Y == 0, CollideX = npc.collideX, Wet = npc.wet,
            LiquidKind = npc.shimmerWet ? 3 : npc.honeyWet ? 2 : npc.lavaWet ? 1 : 0, StairFall = npc.stairFall };
    }

    private static void Invoke(NPC npc, string method) => typeof(NPC).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(npc, null);
    private static (bool, bool, bool, bool, bool, bool, bool) Scratch() =>
        (Collision.up, Collision.down, Collision.stair, Collision.stairFall, Collision.honey, Collision.shimmer, Collision.sloping);

    private static int VerifyRoutes()
    {
        int failed = 0;
        foreach (string name in new[] { "flat", "two-tile-ledge", "stairs-up", "stairs-down" })
        {
            Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new object[] { (ushort)100, (ushort)100 }, null)!;
            for (int x = 5; x < 95; x++)
            {
                Tile tile = Main.tile[x, 90]; tile.HasTile = true; tile.TileType = 1;
            }
            Point from = new(30, 89), to = new(45, 89);
            if (name == "two-tile-ledge")
            {
                for (int x = 35; x < 65; x++)
                for (int y = 88; y < 90; y++)
                {
                    Tile tile = Main.tile[x, y]; tile.HasTile = true; tile.TileType = 1;
                }
                to = new Point(40, 87);
            }
            if (name.StartsWith("stairs"))
            {
                for (int x = 35; x <= 40; x++)
                {
                    Tile tile = Main.tile[x, 89 - (x - 35)]; tile.HasTile = true; tile.TileType = 19; tile.Slope = (Terraria.ID.SlopeType)2;
                }
                to = new Point(40, 83);
                if (name == "stairs-down") (from, to) = (to, from);
            }
            TerrainChanges.Reset();
            NavGrid.World = new GameTileWorld();
            BodyPhysics.Pose? pose = NavGrid.StandAt(from.X, from.Y, false);
            if (pose == null) { Console.WriteLine($"FAIL native route {name}: no start pose"); failed++; continue; }
            BodyState live = BodyState.Standing(pose.Value);
            var movement = new CoordinateMovement();
            int tick;
            for (tick = 0; tick < 1800; tick++)
            {
                movement.Configure((uint)tick, false, true);
                Controls controls = movement.MoveTo(live, NavGrid.FeetWorld(to));
                live = RunEngine(live, controls);
                if (movement.Navigator.Arrived) break;
            }
            bool pass = movement.Navigator.Arrived;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")} native route {name}: {tick} ticks, {movement.Navigator.FaultCount} faults, final {live.FeetTile}");
            if (!pass) failed++;
        }
        return failed;
    }
}
