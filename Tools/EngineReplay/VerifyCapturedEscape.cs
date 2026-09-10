extern alias live;
#nullable enable

using System;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// Native regressions for the control-search failure observed at capture 23-50-24-14964.  The
/// captured body was wet under an awning, so a stationary jump cannot change the clearance
/// state. The full tile window drives production survival and the live time budget; mirrored
/// synthetic awnings isolate sideways clearance. Native collision executes every chosen input.
/// </summary>
internal static class VerifyCapturedEscape
{
    public static int Run()
    {
        int failed = 0;
        for (int repeat = 0; repeat < 3; repeat++) failed += VerifyCapturedPoolEscape();
        failed += VerifyAwning("captured-right-awning", mirrored: false);
        failed += VerifyAwning("mirrored-left-awning", mirrored: true);
        return failed;
    }

    private static int VerifyCapturedPoolEscape()
    {
        BuildCapturedPool();
        TerrainChanges.Reset();
        NavGrid.World = new GameTileWorld();
        // Exact captured NPC box, translated by the fixture's five-tile origin.
        BodyState live = new(5 * 16f + (60540f - 3704 * 16f), 5 * 16f + (9072f - 446 * 16f), 0f, 0f, true, Wet: true,
            Capabilities: MovementCapabilities.Basic);
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        var survival = new live::AICompanion.Companion.Brain.Behaviours.Survival.SurviveAction();
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBreath).GetProperty("Breath")!.SetValue(companion.Breath, 30);
        for (int tick = 0; tick < 210; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.NPC.position = new Vector2(live.Left, live.Bottom - BodyPhysics.Height);
            companion.NPC.velocity = new Vector2(live.Vx, live.Vy);
            companion.NPC.wet = live.Wet;
            companion.NPC.collideX = live.CollideX;
            companion.NPC.stairFall = live.StairFall;
            companion.Motor.Track();
            companion.Breath.Update(companion.NPC);
            companion.Brain.Senses.Update(companion.NPC, Main.player[0], companion.Breath);
            var context = new live::AICompanion.Companion.Brain.Behaviours.ActionContext(companion, companion.Brain.Senses);
            survival.Execute(context);
            bool chosen = survival.TryEscape(context, out var input, out bool pending);
            if (!chosen && !pending) throw new InvalidOperationException($"production escape has no control or pending work at {tick}");
            var controls = new Controls(input.MoveX, Jump: input.Jump, FallThrough: input.FallThrough, Descend: input.Descend);
            live = VerifyEngineMotion.RunEngine(live, controls);
            if (HeadDry(live))
            {
                Console.WriteLine($"PASS production captured-pool escape: head dry after {tick + 1} ticks, breath {companion.Breath.Breath}, final {live.Feet}");
                return 0;
            }
        }
        Console.WriteLine($"FAIL captured-pool escape: head still submerged when initial breath expires, final {live}; air target {survival.AirTarget}");
        DumpNativeControls(live);
        return 1;
    }

    private static void DumpNativeControls(BodyState state)
    {
        foreach (Controls controls in new[] { new Controls(-BodyPhysics.WalkSpeed), Controls.None, new Controls(BodyPhysics.WalkSpeed), new Controls(-BodyPhysics.WalkSpeed, Jump: true), new Controls(0f, Jump: true), new Controls(BodyPhysics.WalkSpeed, Jump: true) })
        {
            BodyState at = state;
            for (int tick = 0; tick < 4; tick++) at = VerifyEngineMotion.RunEngine(at, controls);
            Console.WriteLine($"captured-pool four ticks {controls}: {at}");
        }
    }

    private static bool HeadDry(BodyState state)
    {
        int head = (int)MathF.Floor((state.Bottom - BodyPhysics.Height) / 16f);
        int x = (int)MathF.Floor(state.CentreX / 16f);
        return !NavGrid.World.Water(x, head) && !NavGrid.World.Lava(x, head);
    }

    private static int VerifyAwning(string name, bool mirrored)
    {
        BuildAwning(mirrored);
        TerrainChanges.Reset();
        NavGrid.World = new GameTileWorld();

        // The body begins at the wall-side end of a water-filled low passage.  Its dimensions
        // match the captured NPC (20 x 42); its feet are on the floor and its head is under
        // liquid.  Only a sideways move opens the route to the dry passage.
        BodyState live = new(
            Left: (mirrored ? 45 : 54) * 16f,
            Bottom: 70 * 16f,
            Vx: 0f,
            Vy: 0f,
            OnGround: true,
            Wet: true,
            Capabilities: MovementCapabilities.Basic);
        float drySide = (mirrored ? 56 : 45) * 16f;
        Func<BodyState, bool> escaped = state => !state.Wet
            && (mirrored ? state.CentreX > drySide : state.CentreX < drySide);
        Func<BodyState, float> heuristic = state => mirrored ? -state.CentreX : state.CentreX;
        var search = new SearchControlSequences();

        for (int tick = 0; tick < 240; tick++)
        {
            if (!search.TryChoose(NavGrid.World, live, escaped, heuristic,
                    MovementCapabilities.Basic, Weights.EscapeSearchWork, 0d, out Controls controls))
            {
                Console.WriteLine($"FAIL native escape {name}: no certified control at tick {tick}, state {live}");
                return 1;
            }
            live = VerifyEngineMotion.RunEngine(live, controls);
            if (escaped(live))
            {
                Console.WriteLine($"PASS native escape {name}: dry after {tick + 1} ticks, final {live.Feet}");
                return 0;
            }
        }
        Console.WriteLine($"FAIL native escape {name}: still wet after 240 ticks, final {live}");
        return 1;
    }

    private static void BuildAwning(bool mirrored)
    {
        Main.maxTilesX = 100;
        Main.maxTilesY = 100;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null, new object[] { (ushort)100, (ushort)100 }, null)!;
        Main.tileSolid[1] = true;

        for (int x = 10; x < 90; x++)
            Solid(x, 70);
        for (int x = 45; x <= 55; x++)
        for (int y = 64; y < 70; y++)
            Main.tile[x, y].LiquidAmount = byte.MaxValue;
        for (int x = 45; x <= 55; x++)
            Solid(x, 62);

        int wall = mirrored ? 44 : 56;
        for (int y = 62; y < 70; y++)
            Solid(wall, y);
    }

    private static void BuildCapturedPool()
    {
        string[] lines = File.ReadAllLines(CapturePath());
        if (lines.Length != 204 || !lines[0].StartsWith("tick 14964 follow failure", StringComparison.Ordinal))
            throw new InvalidOperationException("captured-pool fixture has an unexpected header or dimensions");
        Main.maxTilesX = Main.maxTilesY = 212;
        Main.worldSurface = 50;
        Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new object[] { (ushort)212, (ushort)212 }, null)!;
        Main.tileSolid[1] = true;
        for (int y = 0; y < 201; y++)
        for (int x = 0; x < 202; x++)
            CapturedTile(5 + x, 5 + y, lines[3 + y][x]);
    }

    private static string CapturePath()
    {
        for (string? at = Directory.GetCurrentDirectory(); at != null; at = Directory.GetParent(at)?.FullName)
        {
            string path = Path.Combine(at, "Tools", "Scenarios", "captured-pool-23-50-24-14964.txt");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("captured-pool fixture", "Tools/Scenarios/captured-pool-23-50-24-14964.txt");
    }

    private static void CapturedTile(int x, int y, char glyph)
    {
        if (glyph == '~') { Main.tile[x, y].LiquidAmount = byte.MaxValue; return; }
        if (glyph is not ('#' or '/' or '\\' or '<' or '>' or '_')) return;
        Tile tile = Main.tile[x, y];
        tile.HasTile = true; tile.TileType = 1;
        tile.Slope = glyph switch { '\\' => (Terraria.ID.SlopeType)1, '/' => (Terraria.ID.SlopeType)2, '<' => (Terraria.ID.SlopeType)3, '>' => (Terraria.ID.SlopeType)4, _ => 0 };
        tile.IsHalfBlock = glyph == '_';
    }

    private static void Solid(int x, int y)
    {
        Tile tile = Main.tile[x, y];
        tile.HasTile = true;
        tile.TileType = 1;
    }
}
