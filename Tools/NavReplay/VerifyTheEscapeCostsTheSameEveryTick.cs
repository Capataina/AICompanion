#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A player who keeps his interference footprint on the companion keeps the tour redrawing, and what this row
/// establishes is the shape of that cost: it is the same work every tick rather than work that grows.
///
/// <para>The escape — the branch that fires when no step and no turn is allowed, which is what a footprint
/// aimed at the body produces — clears the held leg, so the tour draws a fresh one on the next tick. The draw
/// costs at most four swept tests at the far edge and, when nothing there can be reached, twenty-five more on
/// the lattice that sweeps the whole box. `Steering/CLAUDE.md` used to call that bounded because the
/// footprint expires on its own window, and a sentinel showed on 22 September 2026 that it does not:
/// `ObservePlayer` refreshes `interferenceUntil` on every tick the footprint is seen, so it is a trailing
/// timeout a player holding a block renews indefinitely. The duration is his and cannot be bounded here.</para>
///
/// <para>What can be bounded, and is what actually decides whether this matters, is the work per tick. So
/// this row drives the walk under a footprint that never lifts and counts the tile reads the whole walk makes
/// — every swept test and every clearance read goes through `ITileWorld`, so counting there counts all of it
/// without instrumenting the code under test. It asserts that the per-tick count does not climb: the last
/// hundred ticks may not cost materially more than the first hundred, which is the difference between a
/// constant redraw and a leak. The absolute figure is printed so the guide can quote a measured number rather
/// than an adjective.</para>
/// </summary>
internal static class VerifyTheEscapeCostsTheSameEveryTick
{
    private const int Ticks = 600, Window = 100;
    private static readonly Vector2 BoxCentre = new(60 * 16f, 30 * 16f);
    private static readonly Vector2 HalfSize = new(261f, 105f);

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        try
        {
            var counted = new CountingWorld(ARoomWithRockInIt());
            MovementQueries.World = counted;
            MovementQueries.Hazards = Array.Empty<Rectangle>();
            FreeSpaceSearch.WorldOverride = counted;
            ClearanceField.Shared.Invalidate();

            // The unrefused arm runs first on purpose. It walks the same scene with nothing refused, which both
            // gives the free cost to compare against and builds the shared clearance field, so the refused arm
            // after it is paying for its redraws rather than for the field's one-off construction.
            (double firstFree, double lastFree, long worstFree, Vector2 endFree) = Drive(counted, null);
            (double firstHeld, double lastHeld, long worstHeld, Vector2 endHeld) = Drive(counted,
                // The footprint a player holding a block aimed at the companion produces: a refusal covering
                // where the body is and every step it could take from there, renewed every tick and never lifted.
                (body, point) => Vector2.DistanceSquared(point, body) < 64f * 64f);

            string run = $"under a footprint that never lifts, {firstHeld:0} tile reads a tick over the first {Window} and "
                + $"{lastHeld:0} over the last {Window}, worst tick {worstHeld}; with nothing refused in the same scene, "
                + $"{firstFree:0} and {lastFree:0}, worst tick {worstFree}";

            // The premise: the footprint has to have changed the walk. Both arms run the same scene from the same
            // place with the same per-instance seed, so if they end up together the refusal never bound anything
            // and everything below is a measurement of the ordinary walk wearing this row's name.
            float apart = Vector2.Distance(endFree, endHeld);
            if (apart < 16f)
                return Red($"the premise: the held footprint must change where the walk goes, or this row measures an ordinary "
                    + $"walk under another name; the two arms ended {apart:0.0} px apart. {run}");
            if (lastHeld > firstHeld * 1.25 + 5)
                return Red($"the redraw under a held footprint must cost the same work every tick rather than work that grows: {run}. "
                    + "The player decides how long he holds the block and nothing here can bound that; the code decides what each "
                    + "of those ticks costs, and that is the half this row owns.");
            // And a ceiling, declared here rather than inferred from whatever the run produces. The redraw is a
            // handful of swept tests and a lattice of twenty-five, not a search, and in this scene that measures
            // 115 tile reads a tick; the ceiling is a bit over twice that, so widening the lattice or adding a
            // sweep to the branch is caught while ordinary drift in the scene is not. The unrefused arm is
            // printed beside it as context and is deliberately not the pass line: it walks a different path
            // through the same room and builds field chunks this arm never touches, so it moves for reasons that
            // have nothing to do with the branch under test.
            const double ceiling = 250;
            if (lastHeld > ceiling)
                return Red($"the redraw under a held footprint must stay under {ceiling:0} tile reads a tick, the ceiling this row "
                    + $"declares against a measured 115: {run}");
            Console.WriteLine($"the escape costs the same every tick: {run}");
            return 0;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
    }

    /// <summary>Six hundred ticks of the accompanying walk over one scene, with the refusal the caller names or
    /// with none, reported as the tile reads a tick over the first window and over the last. The very first tick
    /// of a drive is left out of both figures: it pays for whatever the shared clearance field has not built
    /// yet, which is a cost of the field's lifetime rather than of a redraw.</summary>
    private static (double FirstEach, double LastEach, long Worst, Vector2 End) Drive(ITileWorld world, Func<Vector2, Vector2, bool>? refused)
    {
        var movement = new CoordinateMovement();
        Vector2 centre = BoxCentre, velocity = Vector2.Zero;
        float floor = BoxCentre.Y + HalfSize.Y;
        var counted = (CountingWorld)world;
        long first = 0, last = 0, worst = 0;
        for (int tick = 0; tick < Ticks; tick++)
        {
            long before = counted.Reads;
            Vector2 body = centre;
            Step(world, ref centre, ref velocity,
                movement.Accompany(new OrbState(centre, velocity), BoxCentre, HalfSize, Vector2.Zero, floor,
                    point => refused != null && refused(body, point)));
            long cost = counted.Reads - before;
            if (tick == 0) continue;
            worst = Math.Max(worst, cost);
            if (tick < Window) first += cost;
            if (tick >= Ticks - Window) last += cost;
        }
        return ((double)first / (Window - 1), (double)last / Window, worst, centre);
    }

    /// <summary>Every tile question the walk asks — the swept tests of the draw and the clearance reads that
    /// score it — reaches the world through this interface, so one counter here is the whole cost of a tick
    /// without a single line of instrumentation inside the code being measured.</summary>
    private sealed class CountingWorld : ITileWorld
    {
        private readonly ITileWorld inner;

        public CountingWorld(ITileWorld inner) => this.inner = inner;

        public long Reads { get; private set; }

        public int Revision => inner.Revision;
        public bool InWorld(int x, int y) { Reads++; return inner.InWorld(x, y); }
        public TileShape Shape(int x, int y) { Reads++; return inner.Shape(x, y); }
        public bool PassThrough(int x, int y) { Reads++; return inner.PassThrough(x, y); }
        public bool Water(int x, int y) { Reads++; return inner.Water(x, y); }
        public bool Lava(int x, int y) { Reads++; return inner.Lava(x, y); }
    }

    /// <summary>Rock through the middle of the box, so the far edge is regularly unreachable and the lattice
    /// branch — the expensive one — is the branch being priced.</summary>
    private static TextTileWorld ARoomWithRockInIt()
    {
        var rows = new List<string>();
        for (int y = 0; y < 60; y++)
        {
            char[] row = new char[120];
            for (int x = 0; x < 120; x++)
            {
                bool edge = y <= 1 || y >= 58 || x <= 1 || x >= 118;
                bool pillar = x >= 62 && x <= 66 && y >= 22 && y <= 38;
                row[x] = edge || pillar ? '#' : '.';
            }
            rows.Add(new string(row));
        }
        return new TextTileWorld(0, 0, rows);
    }

    private static void Step(ITileWorld world, ref Vector2 centre, ref Vector2 velocity, Controls controls)
    {
        velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
        centre += velocity;
        CircleContact.Resolve(world, ref centre, ref velocity);
    }

    private static int Red(string message)
    {
        Console.WriteLine("RED: " + message);
        return 1;
    }
}
