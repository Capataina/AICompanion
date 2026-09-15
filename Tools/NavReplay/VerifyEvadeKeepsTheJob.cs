#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The evade layer bends the job's flight only where the job's own flight would meet a hit, and a heading that takes the
/// body nowhere is never preferred to one that moves it.
///
/// <para>The third play of the orb (capture 2026-09-15_13-16-33-496) held the body pressed into a ceiling slope two rows
/// above a pool for 2,491 ticks from tick 18,607, asking for <c>-9,0</c> every tick with a flyable route to the player and
/// only harmless slimes about. Two things did it together. The keep test held the route's one velocity of that tick in a
/// straight line for the whole lookahead, and the route's down-left heading reached the pool that way although the route
/// itself curves along above it; so the job was refused. Every wet heading was then masked, every dry heading read no
/// danger, and interest picked the dry heading nearest the route's direction — straight into the slope, dry only because
/// the contact holds the body out of it, and so a body that asks for full speed and never moves.</para>
///
/// <para>The first scene is that capture's plan dump at tick 18,727, cropped. Its predicate is active and predicts no hit
/// anywhere, which is what the slimes amounted to. The second is the same scene with a hit waiting a few pixels along the
/// route, so the job is refused for a real reason and the heading into the slope still has to lose to one that moves.
/// The third is a route that descends past a pillar and runs above a floor a hit is predicted on, without touching it, which
/// must not be bent at all. The fourth is a body inside a pool with a shot closing on it, which must turn away from the shot.
/// Until 15 September 2026 the liquid in these scenes was a wall to the dodge and a hurt to the body; every liquid has been air
/// to the orb since, so the pin's pool is space the route may use, and the third scene proves its forecast with a hit.</para>
/// </summary>
internal static class VerifyEvadeKeepsTheJob
{
    // The session reader's still speed and run, the threshold the arrival, anchors and idle-company rows use.
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
            failures += TheCapturedPinFliesTheRoute();
            failures += AHitAlongTheRouteNeverPinsTheBodyToTheSlope();
            failures += ARouteRunningAboveAHitIsNotBent();
            failures += ACorneredBodyInAPoolStillTurnsFromTheShot();
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
    /// The plan dump of 2026-09-15_13-16-33-496 at tick 18,727 ("stuck, body still for 120 ticks with a path"), columns
    /// 67..130 and rows 660..687 of its window, verbatim: x 3370..3433, y 660..687 in tiles. <c>o</c> and <c>n</c> are
    /// the dump's air glyphs, N is the body's tile and G the goal's, both air. The body's centre is the dump's
    /// <c>orb 54602.0,10811.6</c> and the goal is tile 3383,678.
    /// </summary>
    private static TextTileWorld CapturedPin()
    {
        var rows = new List<string>
        {
            "oo#####._./########################<.>###############_oooooooooo",
            "oo#################################ooo######.##########ooooooooo",
            "oo################################<ooo######oo#########\\ooooooo#",
            "ooo###############################.ooo######oo###########\\oooo/#",
            "ooo################################_oo######oo#############./###",
            "ooo#############################################################",
            "oooo############################################################",
            "ooooooo##################<.#####################################",
            "#ooooooo>################oooo###################################",
            "##oooooooo##############ooooooo#################################",
            "##ooooooooo>##########<oooooooooo###############################",
            "o#oooooooooo#########<oooooooooooooo############################",
            "ooooooooooooo>######<ooooooooooooooo###########.################",
            "oooooooooooooo>#####.ooooooooooooooo#########oooo###############",
            "ooooooooooooooo######ooooooooooooooo>######<ooooo###############",
            "oooooooooooooooo####ooooooooooooooooooo##<Nnooooooo#############",
            "oooo)=ooooooooooo##ooooooooooooooooooooooonnoooooooo>###########",
            "====oooooooooooooooooooooooooooooooooooooooooooooooo.>##########",
            "oooooooooooooGooooooooooooooooooo~~~~~~~~~~~~~~~~~~~~~~#########",
            "~~~~~~~~~~~~~~~~~~~~~##########~~~~~~~~~~~~~~~~~~~~~~~~#########",
            "~~~~~~~~~~~~~~~~~~~~############_~~~~~~~~~~~~~~~~~~~~~~#########",
            "~~~~~~~~~~~~~~~~~#######################_~~~~~~~~~~~~~~#########",
            "~~~~~~~~~~~~~~~~~~########################\\~~~~~~~~~~~~#########",
            "~~~~~~~~~~~~~~~~~~#########################\\~~~~~~~~~~/#########",
            "~~~~_##~~~~~~~~~~~##########################~~~~~~~~~###########",
            "~_####<~~~~~~~~~~~#############################_~~~_############",
            "######~~~~~~~~~~~~##############################################",
            "#####<~~~~~~~~~~~~##############################################",
        };
        return new TextTileWorld(3370, 660, rows);
    }

    private static readonly Vector2 PinnedCentre = new(54602.0f, 10811.6f);
    // The capture names the goal's tile, 3383,678, which is air over the pool's row 679; the pixel is not recorded. The row
    // is about the pin, so the goal sits a tile higher, where a body hovering around it on arrival stays dry: centred on
    // the goal tile itself the navigator plans a partial route to the nearest corner, and centred on its top edge the
    // arrival hover's vertical reach dips the body toward the water.
    private static readonly Vector2 PinGoal = new(3383 * 16 + 8, 677 * 16 + 8);

    private readonly record struct Flight(int LongestStill, int BentTicks, int ArrivedTick, float Nearest, List<string> Trace);

    /// <summary>
    /// Drive the movement boundary the way the brain tick does: the job's controls from <see cref="CoordinateMovement.MoveTo"/>,
    /// then <see cref="CoordinateMovement.Evade"/>, then the motor's law and the contact. Every tick is traced so a failure
    /// prints what the body asked for and where it went.
    /// </summary>
    private static Flight Fly(ITileWorld world, Vector2 start, Vector2 goal, Func<OrbState, int, bool> unsafeAtTick, int ticks)
    {
        var movement = new CoordinateMovement();
        Vector2 centre = start, velocity = Vector2.Zero;
        int still = 0, longest = 0, bent = 0, arrived = -1;
        float nearest = Vector2.Distance(centre, goal);
        var trace = new List<string>();
        for (int tick = 0; tick < ticks; tick++)
        {
            var live = new OrbState(centre, velocity);
            Controls job = movement.MoveTo(live, goal);
            Controls controls = movement.Evade(live, job, unsafeAtTick, out bool bentThisTick);
            if (bentThisTick) bent++;
            velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
            centre += velocity;
            CircleContact.Resolve(world, ref centre, ref velocity);
            still = velocity.Length() < StillSpeed ? still + 1 : 0;
            longest = Math.Max(longest, still);
            nearest = MathF.Min(nearest, Vector2.Distance(centre, goal));
            if (arrived < 0 && movement.Navigator.Arrived) arrived = tick;
            trace.Add($"tick {tick,3}: job {job.Desired.X:0.00},{job.Desired.Y:0.00} asked {controls.Desired.X:0.00},{controls.Desired.Y:0.00}{(bentThisTick ? " bent" : "")} -> {centre.X:0.0},{centre.Y:0.0} v {velocity.X:0.00},{velocity.Y:0.00} {movement.Navigator.Status}");
        }
        return new Flight(longest, bent, arrived, nearest, trace);
    }

    private static void Print(List<string> trace, int from, int count)
    {
        for (int i = Math.Max(0, from); i < Math.Min(trace.Count, from + count); i++) Console.WriteLine("      " + trace[i]);
    }

    /// <summary>
    /// The pin itself. Pass line, declared before the fix: over three hundred ticks the body is never under the still speed
    /// for ten ticks running, and the navigator reports arrival at the goal. At 530b16d the body asks for <c>-9,0</c> into the
    /// slope and sits still from its first ticks. The pool under the pin is space the route may use since every liquid became
    /// air to the orb, so whether the body enters it is no longer part of the line.
    /// </summary>
    private static int TheCapturedPinFliesTheRoute()
    {
        var world = CapturedPin();
        Plug(world);
        var flight = Fly(world, PinnedCentre, PinGoal, (_, _) => false, 300);
        int failures = 0;
        // Never bent as well as never still: with an active predicate that predicts no hit anywhere, a bent tick can only be the
        // keep test refusing a flight for a reason that is not a hit, which is exactly what the pin was.
        if (flight.LongestStill >= StillRunTicks || flight.ArrivedTick < 0 || flight.BentTicks > 0)
        {
            failures += Fail($"the captured pin: longest still run {flight.LongestStill} ticks (must be under {StillRunTicks}), arrived at tick {flight.ArrivedTick} (must arrive), bent {flight.BentTicks} ticks (must be none), nearest {flight.Nearest:0.0} px");
            Print(flight.Trace, 0, 24);
        }
        else
            Console.WriteLine($"evade keeps the job: the captured pin flew its route and arrived at tick {flight.ArrivedTick}, longest still run {flight.LongestStill}, bent {flight.BentTicks} ticks");
        return failures;
    }

    /// <summary>
    /// The same scene with a hit predicted a little way along the route, so the job's own flight is refused for a real
    /// reason and the choice falls to the headings, one of which presses into the slope and goes nowhere. Pass line,
    /// declared before the fix: the evade bends at least once (the premise), and over two hundred ticks the body is never
    /// still for ten running.
    /// </summary>
    private static int AHitAlongTheRouteNeverPinsTheBodyToTheSlope()
    {
        var world = CapturedPin();
        Plug(world);
        float edge = PinnedCentre.X - 12f;
        var flight = Fly(world, PinnedCentre, PinGoal, (state, _) => state.Centre.X < edge, 200);
        int failures = 0;
        if (flight.BentTicks == 0)
            failures += Fail("premise: a hit along the route must make the evade bend at least once, or this row proves nothing about the headings");
        if (flight.LongestStill >= StillRunTicks)
        {
            failures += Fail($"a hit along the route: longest still run {flight.LongestStill} ticks (must be under {StillRunTicks}), bent {flight.BentTicks} ticks");
            Print(flight.Trace, 0, 24);
        }
        if (failures == 0)
            Console.WriteLine($"evade never pins: with a hit along the route it bent {flight.BentTicks} ticks and its longest still run was {flight.LongestStill}");
        return failures;
    }

    /// <summary>
    /// A room with a pillar hanging from the ceiling, the body high on the left and the goal on the right two tiles above the
    /// floor, so the route descends under the pillar and runs along above the floor without touching it. A hit is predicted
    /// wherever the body touches the floor. Holding the descent's first velocity for the lookahead drives the body into the
    /// floor, where the contact holds it and the predicate fires; the route never goes there. Pass line, declared before the
    /// first run: with that predicate active, the evade never bends, and the body arrives.
    ///
    /// <para>Until 15 September 2026 the floor here was a pool and the refusal was the liquid mask, and this was the row that
    /// proved the route forecast on a route that curves. Every liquid became air to the orb that day and the mask went, so the
    /// same geometry proves the same forecast with a predicted hit where the pool was.</para>
    /// </summary>
    private static int ARouteRunningAboveAHitIsNotBent()
    {
        const int width = 50, floorRow = 10;
        var rows = new List<string>();
        for (int y = 0; y <= floorRow; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++)
            {
                bool border = y == 0 || y == floorRow || x == 0 || x == width - 1;
                bool pillar = x is >= 20 and <= 23 && y <= 6;
                row[x] = border || pillar ? '#' : '.';
            }
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);
        // Touching the floor is a centre within half a pixel of the height the contact holds a body resting on it at.
        float touching = floorRow * 16f - CircleContact.Radius - 0.5f;
        bool OnTheFloor(OrbState state, int _) => state.Centre.Y >= touching;
        Vector2 start = new(5 * 16 + 8, 2 * 16 + 8), goal = new(40 * 16 + 8, 8 * 16 + 8);
        var flight = Fly(world, start, goal, OnTheFloor, 400);
        int failures = 0;
        if (flight.BentTicks > 0 || flight.ArrivedTick < 0)
        {
            failures += Fail($"a route running above a hit: bent {flight.BentTicks} ticks (must be none), arrived at tick {flight.ArrivedTick} (must arrive), nearest {flight.Nearest:0.0} px");
            int firstBent = flight.Trace.FindIndex(line => line.Contains(" bent"));
            Print(flight.Trace, firstBent < 0 ? 0 : firstBent - 8, 24);
        }
        else
            Console.WriteLine($"evade leaves a route running above a hit alone: arrived at tick {flight.ArrivedTick}, never bent");
        return failures;
    }

    /// <summary>
    /// A body inside a pool filling the room's whole height, and a shot of that height closing from the left while the job
    /// asks to fly left into it. The pool once left the liquid mask nothing to mask with; it is air to the body now and changes
    /// nothing, which is the point of keeping it in the scene. Pass line: the evade bends, and the heading it chooses does not
    /// fly toward the shot.
    /// </summary>
    private static int ACorneredBodyInAPoolStillTurnsFromTheShot()
    {
        const int width = 40;
        var rows = new List<string> { new('#', width) };
        for (int y = 1; y < 9; y++) rows.Add("#" + new string('~', width - 2) + "#");
        rows.Add(new string('#', width));
        var world = new TextTileWorld(0, 0, rows);
        Plug(world);
        Vector2 centre = new(20 * 16, 5 * 16);
        const float shotStart = -120f, shotSpeed = 8f, shotHalf = 8f;
        bool Unsafe(OrbState state, int ahead)
        {
            float x = centre.X + shotStart + shotSpeed * ahead;
            return MathF.Abs(state.Centre.X - x) < CircleContact.Radius + shotHalf;
        }
        var movement = new CoordinateMovement();
        Controls controls = movement.Evade(new OrbState(centre, Vector2.Zero), new Controls(new Vector2(-9f, 0f)), Unsafe, out bool bent);
        int failures = 0;
        if (!bent || controls.Desired.X < 0f)
            failures += Fail($"a cornered body in a pool: bent {bent} (must bend), asked {controls.Desired.X:0.00},{controls.Desired.Y:0.00} (must not fly toward the shot on the left)");
        else
            Console.WriteLine($"evade turns a body in a pool from the shot: asked {controls.Desired.X:0.00},{controls.Desired.Y:0.00} away from it");
        return failures;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   evade keeps the job: {what}");
        return 1;
    }
}
