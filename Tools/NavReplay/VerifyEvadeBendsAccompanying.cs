#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using Weights = AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Keeping the player company is a walk across a moving box, and the evade layer's keep test has to fly that walk forward
/// rather than some other steering. The accompanying motion joined the movement boundary after the keep test learned to fly
/// the job's own steering, and until it declared itself a producer the test flew whichever request had produced an earlier
/// tick — at best a drift pursuing a target held still, which is a body slowing onto a point the walk has already left.
///
/// <para>Two scenes, their pass lines declared before the first run, each driven through <see cref="CoordinateMovement"/>
/// the way the brain tick drives it — <see cref="CoordinateMovement.Accompany"/>, then <see cref="CoordinateMovement.Evade"/>,
/// then the motor's law and the contact — in an open room with a box moving right at a walking player's pace:</para>
/// <list type="bullet">
/// <item>with the predicate active and predicting no hit anywhere, every tick's applied controls are the walk's own and no
/// tick is bent;</item>
/// <item>with a hit waiting on the walk's own path, placed where the first scene's body entered it well past the lookahead,
/// the two runs are identical until the first tick the verdict reports a hit; that tick's predicted hit lands within three
/// ticks of where the unbent body really entered it; that tick is bent; and the bent body never enters the hit.</item>
/// </list>
/// </summary>
internal static class VerifyEvadeBendsAccompanying
{
    private const int Ticks = 240, WidthTiles = 120, HeightTiles = 40, PredictionSlackTicks = 3;
    private const float HitRadius = 14f, BoxSpeed = 2f;
    private static readonly Vector2 BoxStart = new(30 * 16, 20 * 16), HalfSize = new(300f, 120f), Lead = new(60f, 0f);

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        int failures = 0;
        try
        {
            var world = Room();
            MovementQueries.World = world;
            FreeSpaceSearch.WorldOverride = world;
            ClearanceField.Shared.Invalidate();

            Walk keep = Fly(world, (_, _) => false);
            if (keep.BentTicks > 0 || keep.ChangedTicks > 0)
                failures += Fail($"no hit keeps the walk: bent {keep.BentTicks} ticks and changed the walk's controls on {keep.ChangedTicks} (both must be none)");
            else
                Console.WriteLine($"evade keeps accompanying: {Ticks} ticks with the predicate active and no hit, never bent, every tick the walk's own controls");

            // The hit is where the unbent body was well past the lookahead, and "entered" is the first tick it came within reach.
            Vector2 hit = keep.Centres[150];
            int entered = keep.Centres.FindIndex(c => Vector2.Distance(c, hit) < HitRadius);
            if (entered <= Weights.DodgeLookaheadTicks + 5)
                return failures + Fail($"premise: the unbent body must reach the hit later than the lookahead ({Weights.DodgeLookaheadTicks} ticks) or the prediction has no room; it entered at tick {entered}");

            bool Unsafe(OrbState state, int _) => Vector2.Distance(state.Centre, hit) < HitRadius;
            Walk bend = Fly(world, Unsafe);
            int first = bend.FirstHitTick;
            if (first < 0)
                return failures + Fail($"a hit on the walk's path must be predicted at some tick; the unbent body entered it at tick {entered}");
            for (int t = 0; t < first; t++)
                if (Vector2.Distance(bend.Centres[t], keep.Centres[t]) > 1e-3f)
                    return failures + Fail($"premise: the two runs must be identical until the first predicted hit at tick {first}; they parted at tick {t}");
            int predicted = first + bend.FirstHitAhead - 1;
            float nearest = float.MaxValue;
            foreach (Vector2 c in bend.Centres) nearest = MathF.Min(nearest, Vector2.Distance(c, hit));
            if (Math.Abs(predicted - entered) > PredictionSlackTicks || !bend.BentAtFirstHit || nearest < HitRadius)
            {
                failures += Fail($"a hit on the walk's path: first predicted at tick {first} for {bend.FirstHitAhead} ticks ahead, so entry at tick {predicted} "
                    + $"against the unbent body's {entered} (within {PredictionSlackTicks}); bent that tick {bend.BentAtFirstHit}; nearest the bent body came {nearest:0.0} px (at least {HitRadius})");
                for (int t = Math.Max(0, first - 4); t < Math.Min(bend.Trace.Count, first + 12); t++) Console.WriteLine("      " + bend.Trace[t]);
            }
            else
                Console.WriteLine($"evade bends accompanying: the hit first predicted at tick {first} for entry at tick {predicted}, the unbent body entered at {entered}; bent {bend.BentTicks} ticks, nearest {nearest:0.0} px");
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
        return failures;
    }

    private static TextTileWorld Room()
    {
        var rows = new List<string>();
        for (int y = 0; y < HeightTiles; y++)
        {
            char[] row = new char[WidthTiles];
            for (int x = 0; x < WidthTiles; x++)
                row[x] = y == 0 || y == HeightTiles - 1 || x == 0 || x == WidthTiles - 1 ? '#' : '.';
            rows.Add(new string(row));
        }
        return new TextTileWorld(0, 0, rows);
    }

    private readonly record struct Walk(List<Vector2> Centres, int BentTicks, int ChangedTicks, int FirstHitTick, int FirstHitAhead, bool BentAtFirstHit, List<string> Trace);

    private static Walk Fly(ITileWorld world, Func<OrbState, int, bool> unsafeAtTick)
    {
        var movement = new CoordinateMovement();
        Vector2 centre = BoxStart, velocity = Vector2.Zero;
        var centres = new List<Vector2>(Ticks);
        var trace = new List<string>(Ticks);
        int bent = 0, changed = 0, firstHit = -1, firstAhead = -1;
        bool bentAtFirst = false;
        for (int tick = 0; tick < Ticks; tick++)
        {
            Vector2 box = BoxStart + new Vector2(BoxSpeed * tick, 0f);
            var live = new OrbState(centre, velocity);
            Controls job = movement.Accompany(live, box, HalfSize, Lead, _ => false);
            Controls controls = movement.Evade(live, job, unsafeAtTick, out bool bentThisTick);
            if (bentThisTick) bent++;
            if (controls.Desired != job.Desired || controls.Burst != job.Burst) changed++;
            if (firstHit < 0 && movement.LastEvade.Reason == EvadeReason.Hit)
            {
                firstHit = tick;
                firstAhead = movement.LastEvade.HitTick;
                bentAtFirst = bentThisTick;
            }
            velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
            centre += velocity;
            CircleContact.Resolve(world, ref centre, ref velocity);
            centres.Add(centre);
            trace.Add($"tick {tick,3}: walk {job.Desired.X:0.00},{job.Desired.Y:0.00} asked {controls.Desired.X:0.00},{controls.Desired.Y:0.00}{(bentThisTick ? " bent" : "")} {movement.LastEvade.Reason} ahead {movement.LastEvade.HitTick} -> {centre.X:0.0},{centre.Y:0.0}");
        }
        return new Walk(centres, bent, changed, firstHit, firstAhead, bentAtFirst, trace);
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   evade bends accompanying: {what}");
        return 1;
    }
}
