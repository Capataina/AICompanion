#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using Weights = AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// The accompanying walk prefers clearer air among its own steps. A body that starts near the floor of
/// a tall box must climb; the first target is still one walk step from the body, so the heat is a
/// bias on the wander and not a teleport to the clearest corner.
/// </summary>
internal static class VerifyAccompanyPrefersClearance
{
    private const float Tolerance = 0.01f;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        try
        {
            var world = Room();
            MovementQueries.World = world;
            MovementQueries.Hazards = Array.Empty<Rectangle>();
            FreeSpaceSearch.WorldOverride = world;
            ClearanceField.Shared.Invalidate();
            var movement = new CoordinateMovement();
            Vector2 boxCentre = new(60 * 16f, 48 * 16f);
            Vector2 half = new(200f, 180f);
            Vector2 centre = new(60 * 16f, 56 * 16f), velocity = Vector2.Zero;
            float startClear = ClearanceHeat.TerrainAt(world, centre);
            Controls first = movement.Accompany(new OrbState(centre, velocity), boxCentre, half, Vector2.Zero, _ => false);
            float firstGap = Vector2.Distance(centre, movement.Navigator.Hover.LastTarget);
            if (firstGap > Weights.AccompanyWanderSpeedPx + Tolerance)
                return Fail($"the first target is {firstGap:0.00} px from the body (at most one step, {Weights.AccompanyWanderSpeedPx})");
            Step(world, ref centre, ref velocity, first);
            float bestClear = startClear;
            Vector2 best = centre;
            for (int i = 0; i < 400; i++)
            {
                Step(world, ref centre, ref velocity, movement.Accompany(new OrbState(centre, velocity), boxCentre, half, Vector2.Zero, _ => false));
                float clearance = ClearanceHeat.TerrainAt(world, centre);
                if (clearance > bestClear)
                {
                    bestClear = clearance;
                    best = centre;
                }
            }
            if (bestClear < startClear + 2f)
                return Fail($"the walk must climb off the floor; started {startClear:0.0} tiles, reached {bestClear:0.0} at {best}");
            Console.WriteLine($"accompanying prefers clearance: started {startClear:0.0} tiles, reached {bestClear:0.0} at {best}");
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

    private static TextTileWorld Room()
    {
        var rows = new List<string>();
        for (int y = 0; y < 60; y++)
        {
            char[] row = new char[120];
            for (int x = 0; x < 120; x++) row[x] = y == 0 || y == 59 || x == 0 || x == 119 ? '#' : '.';
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

    private static int Fail(string message)
    {
        Console.WriteLine("FAIL: " + message);
        return 1;
    }
}
