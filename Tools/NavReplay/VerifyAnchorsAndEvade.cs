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
/// <para>A dodge never flies the body into water or lava it is not immune to. The evade simulation ran the contact,
/// which pushes out of solid tiles only, so a body in a two-row gap over lava with shots closing from both sides went
/// 24.9 px into the lava. The row requires that the evade actually bent the body — a scene where it never dodged would
/// satisfy "never touched lava" having tested nothing — and that the body never touched the lava.</para>
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
            failures += AnEvadeNeverFliesIntoLava();
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
    /// Two chambers either side of a thick wall, and the goal a one-tile hollow inside the wall that no orb fits, so the goal
    /// is unreachable from both chambers and the navigator waits in whichever the body is in.
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
                bool hollow = (x == 29 && y == 5) || (x == 30 && y == 2);
                row[x] = border || (wall && !hollow) ? '#' : '.';
            }
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);

        Vector2 firstGoal = new(29 * 16 + 8, 5 * 16 + 8), secondGoal = new(30 * 16 + 8, 2 * 16 + 8);
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
    /// close from either side, so every heading along the gap meets one, and the only direction that avoids both is down.
    /// </summary>
    private static int AnEvadeNeverFliesIntoLava()
    {
        const int width = 40;
        var rows = new List<string> { new('#', width), new('.', width), new('.', width), new('L', width), new('L', width), new('L', width), new('L', width), new('#', width) };
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);
        OrbTerrain.Immunity = LiquidImmunity.None;

        float middle = width * 8f;
        Vector2 centre = new(middle, 32f), velocity = Vector2.Zero;
        const float gapTop = 16f, gapBottom = 48f, shotHalf = 8f, shotSpeed = 8f, start = 200f;
        int bentTicks = 0, lavaTicks = 0;
        float deepest = 0f;
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
            Controls controls = EvadeWhileMoving.Bend(new OrbState(centre, velocity), Controls.None, Unsafe, world, out bool bent);
            if (bent) bentTicks++;
            velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
            centre += velocity;
            CircleContact.Resolve(world, ref centre, ref velocity);
            if (CircleContact.Touches(centre, (x, y) => OrbTerrain.WetWall(world, x, y, LiquidImmunity.None)))
            {
                lavaTicks++;
                deepest = MathF.Max(deepest, centre.Y + CircleContact.Radius - gapBottom);
            }
        }

        int failures = 0;
        if (bentTicks == 0)
            failures += Fail("premise: the closing shots must make the evade bend the body at least once, or this row proves nothing about where a dodge goes");
        if (lavaTicks > 0)
            failures += Fail($"a dodge flew the body into lava it is not immune to: {lavaTicks} ticks touching it, {deepest:0.0} px below the surface, over {bentTicks} bent ticks");
        if (failures == 0)
            Console.WriteLine($"evade: bent {bentTicks} ticks between two closing shots over lava and never touched it");
        return failures;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   anchors and evade: {what}");
        return 1;
    }
}
