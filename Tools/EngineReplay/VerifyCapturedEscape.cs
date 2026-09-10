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
        failed += VerifyFullBrainAwning(false, 200);
        failed += VerifyFullBrainAwning(true, 30);
        failed += VerifyFullBrainCapturedPool();
        return failed;
    }

    private static int VerifyFullBrainCapturedPool()
    {
        BuildCapturedPool();
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].Bottom = new Vector2(2024, 1376);
        companion.NPC.position = new Vector2(1356, 2016 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero; companion.NPC.wet = true; companion.NPC.active = true;
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBreath).GetProperty("Breath")!.SetValue(companion.Breath, 40);
        int dry = 0;
        for (int tick = 0; tick < 630; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI(); VerifyResponsiveFollowing.AdvanceNative(companion);
            dry = !Collision.DrownCollision(companion.NPC.position, companion.NPC.width, companion.NPC.height, 1f) ? dry + 1 : 0;
            if (companion.IsDowned || companion.NPC.life <= 0) break;
            if (dry >= 30)
            {
                Console.WriteLine($"PASS full-brain captured pool: sustained air at {tick}, life={companion.NPC.life}, breath={companion.Breath.Breath}");
                return 0;
            }
        }
        Console.WriteLine($"FAIL full-brain captured pool: feet={companion.NPC.Bottom}, life={companion.NPC.life}, breath={companion.Breath.Breath}, action={companion.Brain.LastAction?.Name}");
        return 1;
    }

    private static int VerifyFullBrainAwning(bool mirrored, int breath)
    {
        BuildAwning(mirrored);
        var companion = VerifyCompanionLifecycle.Create();
        Main.player[0].dead = false;
        Main.player[0].position = new Vector2((mirrored ? 70 : 30) * 16, 70 * 16 - Main.player[0].height);
        companion.NPC.position = new Vector2((mirrored ? 45 : 54) * 16, 70 * 16 - companion.NPC.height);
        companion.NPC.velocity = Vector2.Zero;
        companion.NPC.wet = true;
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBreath).GetProperty("Breath")!.SetValue(companion.Breath, breath);
        int dryTicks = 0;
        for (int tick = 0; tick < 720; tick++)
        {
            VerifyObservedMotion.SetTick(Main.GameUpdateCount + 1);
            companion.AI();
            VerifyResponsiveFollowing.AdvanceNative(companion);
            dryTicks = !companion.NPC.wet ? dryTicks + 1 : 0;
            if (dryTicks >= 60)
            {
                Console.WriteLine($"PASS full-brain wet awning mirrored={mirrored} breath={breath}: stable dry exit at {tick}, action={companion.Brain.LastAction?.Name}");
                return 0;
            }
            if (companion.IsDowned) break;
        }
        Console.WriteLine($"FAIL full-brain wet awning mirrored={mirrored} breath={breath}: {companion.NPC.Bottom}, action={companion.Brain.LastAction?.Name}, goal={companion.Brain.Positioner.Chosen}, status={companion.Brain.Navigator.Status}, controls={companion.Motor.AppliedControls}");
        return 1;
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
        companion.NPC.active = true;
        // The captured row has 0.20 of the native 200-unit breath bar remaining.
        // Surviving the escape is the contract; damage-free escape from a late
        // rescue is not guaranteed, and a one-cell dry-head test was insufficient.
        typeof(live::AICompanion.Companion.CharacterBody.CompanionBreath).GetProperty("Breath")!.SetValue(companion.Breath, 40);
        var survival = new live::AICompanion.Companion.Brain.Behaviours.Survival.SurviveAction();
        if (Environment.GetEnvironmentVariable("AIC_TRACE_POOL") == "1")
        {
            var search = new SearchControlSequences();
            BodyState searched = live;
            for (int t = 0; t < 1400; t++)
            {
                bool found = search.TryChoose(NavGrid.World, searched, HeadDry,
                    s => Vector2.Distance(s.Feet, new Vector2(75 * 16, 109 * 16)), MovementCapabilities.Basic, 120, 0d, out Controls c);
                BodyState prediction = BodyMotion.Step(NavGrid.World, searched, c);
                searched = VerifyEngineMotion.RunEngine(searched, c);
                if (t < 240 && t % 8 == 0) Console.WriteLine($"PROBE tick={t} input={c.MoveX},{c.Jump} feet={searched.Feet} retained={search.RetainedTicks} pending={search.Pending} error={Vector2.Distance(prediction.Feet, searched.Feet)}");
                if (HeadDry(searched)) { Console.WriteLine($"PROBE search dry tick={t} feet={searched.Feet}"); break; }
            }
            Console.WriteLine($"PROBE search no-deadline final={searched.Feet}");
            foreach (int direction in new[] { -1, 1 })
            {
                BodyState probe = live;
                float highest = live.Bottom;
                for (int t = 0; t < 600; t++)
                {
                    probe = VerifyEngineMotion.RunEngine(probe, new Controls(direction * BodyPhysics.WalkSpeed, Jump: true));
                    highest = Math.Min(highest, probe.Bottom);
                    if (HeadDry(probe)) { Console.WriteLine($"PROBE dry direction={direction} tick={t} feet={probe.Feet}"); break; }
                }
                Console.WriteLine($"PROBE direction={direction} highest={highest} final={probe.Feet}");
            }
        }
        int maximumTicks = 40 * 7 + companion.NPC.life / 2 * 7;
        for (int tick = 0; tick < maximumTicks; tick++)
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
            if (HeadDry(live) && companion.NPC.life > 0)
            {
                Console.WriteLine($"PASS production captured-pool escape: head dry after {tick + 1} ticks, breath {companion.Breath.Breath}, final {live.Feet}");
                return 0;
            }
        }
        Console.WriteLine($"FAIL captured-pool escape: no living escape before the native breath/life allowance expired, final {live}; air target {survival.AirTarget}");
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
        return !Collision.DrownCollision(new Vector2(state.Left, state.Bottom - BodyPhysics.Height), BodyPhysics.Width, BodyPhysics.Height, 1f)
            && !Collision.LavaCollision(new Vector2(state.Left, state.Bottom - BodyPhysics.Height), BodyPhysics.Width, BodyPhysics.Height);
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
