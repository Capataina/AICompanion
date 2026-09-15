#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// Safety sits on top of the job and never replaces it, and that has to hold where hazards keep coming rather than once. A
/// sentinel's review of the evade layer drove six scenes through the movement boundary the way the brain tick does — the job's
/// controls from <see cref="CoordinateMovement.MoveTo"/>, then <see cref="CoordinateMovement.Evade"/>, then the motor's law and the
/// contact — against the same scenes flown by the job alone. Five held. In the sixth, a corridor two tiles tall with a shot coming
/// down it every sixty ticks, the evade backed the body 84 px over 500 ticks and never arrived, where the job alone arrived at tick
/// 142: in a corridor no heading clears a shot except fleeing ahead of it, and fleeing only postpones the hit it avoids.
///
/// <para>The fix is <see cref="CoordinateMovement.Evade"/>'s retreat budget: once the bends have carried the body back along the
/// job's way further than a lookahead of full-speed flight, the job's controls go out until the lost way is made up. With it the
/// corridor arrives at tick 468 with 32 hits against the job alone's 42, and every other scene keeps the numbers it had before, no
/// hits in any of them. Arriving 326 ticks after the job alone is the price of the hits it still dodges, and this row does not
/// bound it. With the budget switched off the corridor reads 34 hits, never arrived, end x 84, exactly as before the fix.</para>
///
/// <para>The predicate is capped at the dodge lookahead, because the production predicate answers nothing past it
/// (<c>Reflexes.TryAssess</c>); a mechanism that read further here than the game can would pass a row the game then fails.</para>
///
/// <para>The scenes are the review's probe, kept as it was written so its numbers compare. Pass lines, declared before the fix:</para>
/// <list type="bullet">
/// <item>in every scene the evade takes no more hit ticks than the job alone and is never under the still speed for ten ticks
/// running;</item>
/// <item>in the scenes whose goal is not itself inside a hazard — the parked enemy on the line both ways, the stream across the
/// route, and the corridor — the evade arrives wherever the job alone arrives. The goal inside a standing enemy is exempt, because
/// arriving there is flying into the enemy, and the two hold scenes have nowhere to arrive.</item>
/// </list>
/// </summary>
internal static class VerifyEvadeUnderSustainedFire
{
    private const float StillSpeed = 0.3f;
    private const int StillRunTicks = 10;

    private enum Mode { Evade, JobAlone }

    private readonly record struct Outcome(int Hits, int Bent, int Arrived, int LongestStill, Vector2 End);

    private readonly record struct Scene(string Name, List<string> Rows, Vector2 Start, Vector2 Goal, Func<int, List<Rectangle>> Hazards,
        int Ticks, bool Obstacles, bool MustArrive);

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        int failures = 0;
        try
        {
            foreach (Scene scene in Scenes())
            {
                Outcome evade = Fly(scene, Mode.Evade), alone = Fly(scene, Mode.JobAlone);
                string line = $"{scene.Name}: evade hits {evade.Hits} bent {evade.Bent} arrived {evade.Arrived} still {evade.LongestStill} end {evade.End.X:0},{evade.End.Y:0}; "
                    + $"job alone hits {alone.Hits} arrived {alone.Arrived}";
                var wrong = new List<string>();
                if (evade.Hits > alone.Hits) wrong.Add("more hits than the job alone");
                if (evade.LongestStill >= StillRunTicks) wrong.Add($"still for {evade.LongestStill} ticks");
                if (scene.MustArrive && alone.Arrived >= 0 && evade.Arrived < 0) wrong.Add("never arrived where the job alone did");
                if (wrong.Count == 0)
                    Console.WriteLine($"evade under sustained fire: {line}");
                else
                {
                    Console.WriteLine($"   evade under sustained fire: {string.Join(", ", wrong)} — {line}");
                    failures++;
                }
            }
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

    private static Rectangle Body(Vector2 centre) => new((int)MathF.Floor(centre.X - 10), (int)MathF.Floor(centre.Y - 10), 21, 21);

    private static Rectangle Box(float cx, float cy, int w, int h) => new((int)(cx - w / 2f), (int)(cy - h / 2f), w, h);

    private static List<string> Room(int w, int h, Func<int, int, bool>? solid = null)
    {
        var rows = new List<string>();
        for (int y = 0; y < h; y++)
        {
            var row = new char[w];
            for (int x = 0; x < w; x++)
                row[x] = y == 0 || y == h - 1 || x == 0 || x == w - 1 || (solid?.Invoke(x, y) ?? false) ? '#' : '.';
            rows.Add(new string(row));
        }
        return rows;
    }

    private static IEnumerable<Scene> Scenes()
    {
        yield return new Scene("a floating enemy parked on the line, routed around", Room(64, 16), new(4 * 16 + 8, 8 * 16 + 8), new(58 * 16 + 8, 8 * 16 + 8),
            _ => new() { Box(30 * 16, 8 * 16 + 8, 24, 24) }, 400, true, true);
        yield return new Scene("a floating enemy parked on the line, not routed around", Room(64, 16), new(4 * 16 + 8, 8 * 16 + 8), new(58 * 16 + 8, 8 * 16 + 8),
            _ => new() { Box(30 * 16, 8 * 16 + 8, 24, 24) }, 400, false, true);
        yield return new Scene("a stream of shots across the route", Room(40, 20), new(3 * 16 + 8, 10 * 16 + 8), new(30 * 16 + 8, 10 * 16 + 8),
            t => Enumerable.Range(0, 40).Select(i => i * 20).Where(s => s <= t).Select(s => Box(38 * 16 - (t - s) * 8f, 10 * 16 + 8, 14, 14))
                .Where(r => r.X > -40).ToList(), 400, false, true);
        yield return new Scene("the goal inside a standing enemy", Room(40, 16), new(5 * 16 + 8, 8 * 16 + 8), new(20 * 16 + 8, 13 * 16),
            _ => new() { Box(20 * 16 + 8, 14 * 16 - 20, 18, 40) }, 400, false, false);
        yield return new Scene("a two-tile corridor with a shot down it every sixty ticks", Room(64, 16, (x, y) => y < 7 || y > 8), new(10 * 16 + 8, 8 * 16), new(55 * 16 + 8, 8 * 16),
            t => Enumerable.Range(0, 10).Select(i => i * 60).Where(s => s <= t).Select(s => Box(60 * 16 - (t - s) * 6f, 8 * 16, 12, 12))
                .Where(r => r.X > -40).ToList(), 500, false, true);
        yield return new Scene("a wall behind, an enemy walking in and parking", Room(40, 16), new(1 * 16 + 11, 8 * 16 + 8), new(1 * 16 + 11, 8 * 16 + 8),
            t => new() { Box(MathF.Max(3 * 16, 20 * 16 - t * 1.5f), 8 * 16 + 8, 18, 40) }, 400, false, false);
        yield return new Scene("a wall behind, a shot at its row every fifteen ticks", Room(40, 16), new(1 * 16 + 11, 8 * 16 + 8), new(1 * 16 + 11, 8 * 16 + 8),
            t => Enumerable.Range(0, 40).Select(i => i * 15).Where(s => s <= t).Select(s => Box(38 * 16 - (t - s) * 9f, 8 * 16 + 8, 14, 14))
                .Where(r => r.X > -40).ToList(), 400, false, false);
    }

    private static Outcome Fly(Scene scene, Mode mode)
    {
        var world = new TextTileWorld(0, 0, scene.Rows);
        MovementQueries.World = world;
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
        var movement = new CoordinateMovement();
        Vector2 centre = scene.Start, velocity = Vector2.Zero;
        int hits = 0, bent = 0, arrived = -1, still = 0, longest = 0;
        for (int t = 0; t < scene.Ticks; t++)
        {
            int now = t;
            if (scene.Obstacles) movement.SetObstacles(scene.Hazards(now).Select(r => { var b = r; b.Inflate(24, 24); return b; }));
            var live = new OrbState(centre, velocity);
            Controls controls = movement.MoveTo(live, scene.Goal);
            if (mode == Mode.Evade)
            {
                // The production predicate answers nothing past the dodge lookahead (Reflexes.TryAssess), so neither does this one:
                // a mechanism that read further here than the game can would pass a row the game then fails.
                controls = movement.Evade(live, controls,
                    (s, k) => k <= AICompanion.Companion.Brain.Infrastructure.Selection.Weights.DodgeLookaheadTicks
                        && scene.Hazards(now + k).Any(h => h.Intersects(Body(s.Centre))), out bool b);
                if (b) bent++;
            }
            velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
            centre += velocity;
            CircleContact.Resolve(world, ref centre, ref velocity);
            if (scene.Hazards(now + 1).Any(h => h.Intersects(Body(centre)))) hits++;
            still = velocity.Length() < StillSpeed ? still + 1 : 0;
            longest = Math.Max(longest, still);
            if (arrived < 0 && movement.Navigator.Arrived && Vector2.Distance(centre, scene.Goal) < 40f) arrived = t;
        }
        return new Outcome(hits, bent, arrived, longest, centre);
    }
}
