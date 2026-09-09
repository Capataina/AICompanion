#nullable enable

using AICompanion.Companion.Brain.WorldObservation;
using Microsoft.Xna.Framework;
using Terraria;
using System.Reflection;

/// <summary>Native-map checks for observed NPC forecasts and the pure return-pressure function.</summary>
internal static class VerifyObservedMotion
{
    public static int Run()
    {
        PredictObservedMotion.Clear();
        int failures = 0;
        failures += StationaryGroundedEnemyStaysGrounded();
        failures += FlyerStopsAtWallWhilePhaserCrosses();
        failures += NativePhysicsUsesCurrentNpcFields();
        failures += ObservedAccelerationChangesTheForecast();
        failures += JumpImpulseIsNotRepeated();
        failures += ForgetMakesSlotReuseFresh();
        failures += SameTickCorrectionInvalidatesForecast();
        failures += RegroupUrgencyOrdersItsInputs();
        Console.WriteLine($"observed motion: {(failures == 0 ? "all checks passed" : $"{failures} failures")}; native terrain, slot reuse, cache correction and regroup ordering exercised");
        return failures;
    }

    private static int StationaryGroundedEnemyStaysGrounded()
    {
        var npc = Npc(11, new Vector2(320f, 928f), Vector2.Zero);
        SetTick(10);
        float bottom = npc.Bottom.Y;
        Vector2 forecast = PredictObservedMotion.Predict(npc, 12);
        return Require(MathF.Abs(forecast.Y - (bottom - npc.height * .5f)) < .01f, "grounded enemy forecast fell through its floor");
    }

    private static int FlyerStopsAtWallWhilePhaserCrosses()
    {
        for (int y = 50; y < 60; y++)
        {
            Tile tile = Main.tile[30, y];
            tile.HasTile = true;
            tile.TileType = 1;
        }
        SetTick(20);
        var flyer = Npc(12, new Vector2(440f, 800f), new Vector2(8f, 0f), noGravity: true);
        Vector2 stopped = PredictObservedMotion.Predict(flyer, 8);
        SetTick(21);
        var phaser = Npc(13, new Vector2(440f, 800f), new Vector2(8f, 0f), noGravity: true, noTileCollide: true);
        Vector2 crossed = PredictObservedMotion.Predict(phaser, 8);
        for (int y = 50; y < 60; y++)
        {
            Tile tile = Main.tile[30, y];
            tile.HasTile = false;
        }
        return Require(stopped.X < 480f && crossed.X > 500f, $"flyer/phaser wall contract failed: stopped={stopped.X:0.0}, crossed={crossed.X:0.0}");
    }

    private static int NativePhysicsUsesCurrentNpcFields()
    {
        int failures = 0;
        for (int x = 18; x < 40; x++)
        for (int y = 45; y < 60; y++)
        {
            Tile tile = Main.tile[x, y];
            tile.LiquidAmount = 0;
        }
        failures += CompareNativeStep("dry custom gravity", 70, Npc(70, new Vector2(320f, 800f), new Vector2(2f, 1f)), gravity: .45f, maxFall: 4.5f);

        Tile water = Main.tile[25, 51];
        water.LiquidAmount = 255;
        water.LiquidType = 0;
        NPC wet = Npc(71, new Vector2(400f, 800f), new Vector2(4f, 1f));
        wet.wet = true;
        wet.waterMovementSpeed = .75f;
        failures += CompareNativeStep("wet custom slowdown", 71, wet, gravity: .2f, maxFall: 7f);
        water.LiquidAmount = 0;

        Tile slope = Main.tile[35, 55];
        slope.HasTile = true;
        slope.TileType = 1;
        slope.Slope = Terraria.ID.SlopeType.SlopeDownLeft;
        // NPC.UpdateCollision's slope/step policy is intentionally outside this observed-motion
        // forecast. The check bounds the native discrepancy while still exercising SlopeCollision
        // with the current non-zero gravity, rather than asserting a false exact AI prediction.
        failures += CompareNativeStep("slope with gravity", 72, Npc(72, new Vector2(544f, 848f), new Vector2(2f, 1f)), gravity: .35f, maxFall: 10f, tolerance: 1.5f);
        slope.HasTile = false;
        return failures;
    }

    private static int CompareNativeStep(string name, ulong tick, NPC npc, float gravity, float maxFall, float tolerance = .01f)
    {
        SetCurrentPhysics(npc, gravity, maxFall);
        SetTick(tick);
        PredictObservedMotion.Forget(npc.whoAmI);
        var scratch = Scratch();
        Vector2 forecast = PredictObservedMotion.Predict(npc, 1);
        int failures = Require(Scratch() == scratch, $"{name} prediction changed Collision scratch fields");
        AdvanceNative(npc);
        return failures + Require(Vector2.Distance(forecast, npc.Center) <= tolerance,
            $"{name} diverged from native current-field movement beyond {tolerance:0.00}px: forecast={forecast}, native={npc.Center}");
    }

    private static int ObservedAccelerationChangesTheForecast()
    {
        var npc = Npc(14, new Vector2(320f, 800f), new Vector2(2f, 0f), noGravity: true);
        SetTick(30);
        PredictObservedMotion.Observe(npc);
        SetTick(31);
        npc.position += npc.velocity;
        npc.velocity = new Vector2(3f, 0f);
        PredictObservedMotion.Observe(npc);
        Vector2 accelerated = PredictObservedMotion.Predict(npc, 4);
        float constantVelocity = npc.Center.X + npc.velocity.X * 4f;
        return Require(accelerated.X > constantVelocity, $"observed acceleration was ignored: forecast={accelerated.X:0.0}, constant={constantVelocity:0.0}");
    }

    private static int JumpImpulseIsNotRepeated()
    {
        var npc = Npc(15, new Vector2(600f, 800f), new Vector2(0f, -8f));
        SetTick(40);
        float centreY = npc.Center.Y;
        Vector2 forecast = PredictObservedMotion.Predict(npc, 8);
        return Require(forecast.Y > centreY - 64f, $"jump impulse was repeated across forecast: start={centreY:0.0}, forecast={forecast.Y:0.0}");
    }

    private static int ForgetMakesSlotReuseFresh()
    {
        var oldNpc = Npc(16, new Vector2(320f, 800f), new Vector2(10f, 0f), noGravity: true);
        SetTick(50);
        PredictObservedMotion.Predict(oldNpc, 8);
        PredictObservedMotion.Forget(16);
        var replacement = Npc(16, new Vector2(700f, 800f), Vector2.Zero, noGravity: true);
        SetTick(51);
        Vector2 fresh = PredictObservedMotion.Predict(replacement, 5);
        return Require(Vector2.Distance(fresh, replacement.Center) < .01f, $"slot reuse retained old forecast: replacement={replacement.Center}, forecast={fresh}");
    }

    private static int SameTickCorrectionInvalidatesForecast()
    {
        var npc = Npc(17, new Vector2(320f, 800f), new Vector2(10f, 0f), noGravity: true);
        SetTick(60);
        PredictObservedMotion.Predict(npc, 10);
        npc.position = new Vector2(700f, 800f);
        npc.velocity = Vector2.Zero;
        Vector2 corrected = PredictObservedMotion.Predict(npc, 5);
        return Require(Vector2.Distance(corrected, npc.Center) < .01f, $"same-tick correction retained stale forecast: corrected={npc.Center}, forecast={corrected}");
    }

    private static int RegroupUrgencyOrdersItsInputs()
    {
        const float calm = 160f, fullDistance = 640f, freeReturn = 60f, fullReturn = 240f;
        float calmNear = CalculateRegroupUrgency.Evaluate(80f, 10f, 0f, 0, calm, fullDistance, freeReturn, fullReturn);
        float farther = CalculateRegroupUrgency.Evaluate(320f, 10f, 0f, 0, calm, fullDistance, freeReturn, fullReturn);
        float slowReturn = CalculateRegroupUrgency.Evaluate(320f, 180f, 0f, 0, calm, fullDistance, freeReturn, fullReturn);
        float movingAway = CalculateRegroupUrgency.Evaluate(320f, 10f, 4f, 0, calm, fullDistance, freeReturn, fullReturn);
        float stalled = CalculateRegroupUrgency.Evaluate(320f, 10f, 0f, 120, calm, fullDistance, freeReturn, fullReturn);
        float far = CalculateRegroupUrgency.Evaluate(1200f, 400f, 0f, 0, calm, fullDistance, freeReturn, fullReturn);
        return Require(calmNear == 0f && farther > calmNear && slowReturn > farther && movingAway > farther && stalled > farther && far > .99f,
            $"regroup urgency lost ordering: near={calmNear:0.00}, far={farther:0.00}, slow={slowReturn:0.00}, away={movingAway:0.00}, stalled={stalled:0.00}, extreme={far:0.00}");
    }

    private static NPC Npc(int slot, Vector2 position, Vector2 velocity, bool noGravity = false, bool noTileCollide = false)
        => new()
        {
            whoAmI = slot, type = slot, active = true, width = 16, height = 32, position = position, velocity = velocity,
            noGravity = noGravity, noTileCollide = noTileCollide, lavaImmune = true, wetCount = 2,
            waterMovementSpeed = .5f, lavaMovementSpeed = .5f, honeyMovementSpeed = .25f, shimmerMovementSpeed = .375f
        };

    private static int Require(bool condition, string detail)
    {
        if (condition) return 0;
        Console.WriteLine("FAIL observed motion: " + detail);
        return 1;
    }

    private static (bool, bool, bool, bool, bool, bool, bool) Scratch() =>
        (Collision.up, Collision.down, Collision.stair, Collision.stairFall, Collision.honey, Collision.shimmer, Collision.sloping);

    private static void SetTick(ulong tick)
    {
        FieldInfo? field = typeof(Main).GetField("GameUpdateCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? typeof(Main).GetField("_gameUpdateCount", BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("EngineReplay cannot set Terraria's game-update counter for consecutive-observation fixtures");
        field.SetValue(null, field.FieldType == typeof(uint) ? (object)(uint)tick : tick);
    }

    private static void SetCurrentPhysics(NPC npc, float gravity, float maxFall)
    {
        typeof(NPC).GetProperty("gravity")!.SetValue(npc, gravity);
        typeof(NPC).GetProperty("maxFallSpeed")!.SetValue(npc, maxFall);
    }

    private static void AdvanceNative(NPC npc)
    {
        if (!npc.noGravity)
            npc.velocity.Y = MathF.Min(npc.velocity.Y + npc.gravity, npc.maxFallSpeed);
        typeof(NPC).GetMethod("UpdateCollision", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(npc, null);
    }
}
