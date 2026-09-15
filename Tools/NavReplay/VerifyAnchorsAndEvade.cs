#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;

/// <summary>
/// Two properties the first build of the hover and the evade layer lacked, each found by an independent review on
/// 15 September 2026 and each reproduced there as a number before it was fixed.
///
/// <para>A hover anchor never survives the request that made it, and is never somewhere the body cannot fly straight
/// to. The navigator kept its wait anchor across an interrupted wait and across the body being carried elsewhere, and
/// the next unreachable goal pulled the body 230 px back toward the old place and held it at zero velocity against the
/// wall between. The rows here carry a body across a wall mid-wait, and then interrupt it and hand it a new unreachable
/// goal, and require it to hover where it now is and never sit still.</para>
///
/// <para>A dodge is free to use the lava below it. Until 15 September 2026 this row asked the opposite: a dodge never flew
/// the body into water or lava, because both hurt it, and the scene — a two-row gap over lava with shots closing from both
/// sides — required a cornered body to take the hit rather than the lava. The owner ruled that day that every liquid is air
/// to the companion, so the lava below the gap is the one heading clear of both shots, and the row now requires the dodge to
/// take it: the evade bends the body, the body enters the lava, and no tick of its flight meets a shot.</para>
/// </summary>
internal static class VerifyAnchorsAndEvade
{
    // The session reader's still speed and run, the same threshold the arrival row and the idle-company rows use.
    private const float StillSpeed = 0.3f;
    private const int StillRunTicks = 10;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        int failures = 0;
        try
        {
            failures += AStaleWaitAnchorNeverPullsTheBodyBack();
            failures += AnEvadeUsesTheLavaBelowTheShots();
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

    private static void Plug(TextTileWorld world)
    {
        MovementQueries.World = world;
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
    }

    /// <summary>
    /// Two chambers either side of a thick wall, and the goals two corners of a sealed pocket inside the wall, so each goal
    /// is proven unreachable from both chambers and the navigator waits in whichever the body is in. The goals were a
    /// one-tile hollow no orb fits until 15 September 2026; the navigator now plans a goal with no free corner beside it
    /// to the free corner nearest it, which made the hollow a place to fly to rather than an absence to wait on.
    /// </summary>
    private static int AStaleWaitAnchorNeverPullsTheBodyBack()
    {
        const int width = 60, height = 12;
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++)
            {
                bool border = y == 0 || y == height - 1 || x == 0 || x == width - 1;
                bool wall = x is >= 26 and <= 33;
                bool pocket = x is >= 28 and <= 31 && y is >= 3 and <= 6;
                row[x] = border || (wall && !pocket) ? '#' : '.';
            }
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);

        // Two corners of the pocket, 32 px apart, so the second is a new goal rather than a drift of the first.
        Vector2 firstGoal = new(29 * 16, 5 * 16), secondGoal = new(31 * 16, 5 * 16);
        var navigator = new Navigator();
        Vector2 centre = new(10 * 16 + 8, 6 * 16), velocity = Vector2.Zero;
        int failures = 0;

        // Wait on the first goal long enough for the search to exhaust and the hover to anchor.
        for (int tick = 0; tick < 240; tick++) Tick(navigator, world, firstGoal, ref centre, ref velocity);
        if (navigator.Status != Navigator.ExecutionStatus.Unreachable)
            failures += Fail($"premise: the hollow in the wall must be unreachable so the navigator waits; status {navigator.Status}");

        // Carried across the wall, as recovery flight or an escape carries it, with the same goal still held.
        Vector2 carried = new(50 * 16 + 8, 6 * 16);
        centre = carried;
        velocity = Vector2.Zero;
        var carriedRun = Watch(navigator, world, firstGoal, ref centre, ref velocity, carried, 240);
        if (carriedRun.Farthest > Navigator.SettleRadius || carriedRun.LongestStill >= StillRunTicks)
            failures += Fail($"carried across the wall, the body must hover where it now is: drifted {carriedRun.Farthest:0.0} px from where it was put (settle radius {Navigator.SettleRadius:0}), longest still run {carriedRun.LongestStill} ticks");

        // Interrupted, then handed a different unreachable goal, which is the sequence the review reproduced.
        navigator.Interrupt(new OrbState(centre, velocity));
        Vector2 settled = centre;
        var secondRun = Watch(navigator, world, secondGoal, ref centre, ref velocity, settled, 240);
        if (secondRun.Farthest > Navigator.SettleRadius || secondRun.LongestStill >= StillRunTicks)
            failures += Fail($"interrupted and given a new unreachable goal, the body must hover where it is: drifted {secondRun.Farthest:0.0} px (settle radius {Navigator.SettleRadius:0}), longest still run {secondRun.LongestStill} ticks");

        if (failures == 0)
            Console.WriteLine($"anchors: carried across a wall it drifted at most {carriedRun.Farthest:0.0} px, after an interrupt and a new goal {secondRun.Farthest:0.0} px, never still for {StillRunTicks} ticks");
        return failures;
    }

    private static (float Farthest, int LongestStill) Watch(Navigator navigator, ITileWorld world, Vector2 goal, ref Vector2 centre, ref Vector2 velocity, Vector2 origin, int ticks)
    {
        float farthest = 0f;
        int still = 0, longest = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            Tick(navigator, world, goal, ref centre, ref velocity);
            farthest = MathF.Max(farthest, Vector2.Distance(centre, origin));
            still = velocity.Length() < StillSpeed ? still + 1 : 0;
            longest = Math.Max(longest, still);
        }
        return (farthest, longest);
    }

    private static void Tick(Navigator navigator, ITileWorld world, Vector2 goal, ref Vector2 centre, ref Vector2 velocity)
    {
        Controls controls = navigator.MoveTo(new OrbState(centre, velocity), goal);
        velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
        centre += velocity;
        CircleContact.Resolve(world, ref centre, ref velocity);
    }

    /// <summary>
    /// A ceiling, a two-row gap the body fits in, four rows of lava and a floor. Two shots fill the gap's whole height and
    /// close from either side, so every heading along the gap meets one sooner or later and only the lava below is out of
    /// their way. Pass line, declared before the first run under the ruling: the evade bends the body at least once, the body
    /// touches the lava on at least one tick — the premise that the heading clear of both shots was taken — and on no tick of
    /// the sixty does the body's box meet either shot.
    /// </summary>
    private static int AnEvadeUsesTheLavaBelowTheShots()
    {
        const int width = 40;
        var rows = new List<string> { new('#', width), new('.', width), new('.', width), new('L', width), new('L', width), new('L', width), new('L', width), new('#', width) };
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);

        float middle = width * 8f;
        Vector2 centre = new(middle, 32f), velocity = Vector2.Zero;
        const float gapTop = 16f, gapBottom = 48f, shotHalf = 8f, shotSpeed = 8f, start = 200f;
        int bentTicks = 0, lavaTicks = 0, hitTicks = 0, firstHit = -1;
        float deepest = 0f;
        // Every tick's body, ask and bend, printed around the first hit on failure: the same instrument the arrival row prints
        // when its still run trips, because a count of hit ticks says that the dodge went wrong and not how.
        var trace = new List<string>();
        for (int now = 0; now < 60; now++)
        {
            int at = now;
            bool Unsafe(OrbState state, int ahead)
            {
                float travelled = shotSpeed * (at + ahead);
                var body = new Rectangle((int)(state.Centre.X - CircleContact.Radius), (int)(state.Centre.Y - CircleContact.Radius), (int)(CircleContact.Radius * 2), (int)(CircleContact.Radius * 2));
                foreach (float x in new[] { middle - start + travelled, middle + start - travelled })
                    if (body.Intersects(new Rectangle((int)(x - shotHalf), (int)gapTop, (int)(shotHalf * 2), (int)(gapBottom - gapTop))))
                        return true;
                return false;
            }
            Controls controls = EvadeWhileMoving.Bend(new OrbState(centre, velocity), Controls.None, Unsafe, world, out EvadeVerdict verdict);
            bool bent = verdict.Bent;
            if (bent) bentTicks++;
            Vector2 before = centre, velocityBefore = velocity;
            velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
            centre += velocity;
            CircleContact.Resolve(world, ref centre, ref velocity);
            bool wet = CircleContact.Touches(centre, (x, y) => world.Lava(x, y));
            // Where the shots are on the tick the body arrived at its new centre: one tick past now, asked of the predicate the
            // evade itself reads, so a hit here is exactly a hit the evade's own reading would call one.
            bool hit = Unsafe(new OrbState(centre, velocity), 1);
            float nearShot = MathF.Min(MathF.Abs(before.X - (middle - start + shotSpeed * now)), MathF.Abs(before.X - (middle + start - shotSpeed * now)));
            trace.Add($"tick {now,2}: at {before.X:0.0},{before.Y:0.0} v {velocityBefore.X:0.00},{velocityBefore.Y:0.00} asked {controls.Desired.X:0.00},{controls.Desired.Y:0.00}{(controls.Burst ? " burst" : "")}{(bent ? " bent" : "")} -> {centre.X:0.0},{centre.Y:0.0}{(wet ? " in lava" : "")}{(hit ? " HIT" : "")}, nearest shot {nearShot:0} px");
            if (wet)
            {
                lavaTicks++;
                deepest = MathF.Max(deepest, centre.Y + CircleContact.Radius - gapBottom);
            }
            if (hit)
            {
                hitTicks++;
                if (firstHit < 0) firstHit = now;
            }
        }

        int failures = 0;
        if (bentTicks == 0)
            failures += Fail("premise: the closing shots must make the evade bend the body at least once, or this row proves nothing about where a dodge goes");
        if (lavaTicks == 0)
            failures += Fail($"premise: the only heading clear of both shots is down into the lava, and the body never entered it over {bentTicks} bent ticks; a liquid is still being treated as something to avoid");
        if (hitTicks > 0)
        {
            failures += Fail($"a dodge with the lava free to use still met a shot: {hitTicks} ticks inside a shot, first at tick {firstHit}, over {bentTicks} bent ticks and {lavaTicks} ticks in the lava");
            for (int i = Math.Max(0, firstHit - 20); i < Math.Min(trace.Count, firstHit + 6); i++) Console.WriteLine("      " + trace[i]);
        }
        if (failures == 0)
            Console.WriteLine($"evade: bent {bentTicks} ticks between two closing shots, used the lava below on {lavaTicks} ticks ({deepest:0.0} px deep) and was never hit");
        return failures;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   anchors and evade: {what}");
        return 1;
    }
}
