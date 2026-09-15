#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Tools.Ledger;

/// <summary>
/// The route through a corridor sits nearer its middle than its walls, game-free, and the orb
/// steered along it stays there.
///
/// The measurement is the mean distance of the route from the corridor's mid-line, over the
/// route's interior, against the same route planned with the clearance price switched off. The
/// two searches share the corridor, the start and the goal, and the start and goal both sit one
/// tile under the top wall, so the unpriced route runs the whole way beside that wall and reads
/// as the whole half-height off the middle; the priced route has to move away from it and come
/// back at the end. Smoothing then runs on the priced route, and it is measured after smoothing
/// because that is the route the body follows — a smoother that skips from one wall-hugging end
/// to the other undoes the search, and the first smoother did exactly that.
///
/// Then the body: the steering law and the circle contact integrate the orb along the smoothed
/// route tick by tick, and its mean offset from the mid-line is measured the same way. The
/// steered body is what the player sees, so it is the one the pass line is written against.
/// </summary>
internal static class VerifyCorridorMiddle
{
    // Six free rows between the walls, so the mid-line is at corner row 4 and the start and goal
    // corners at row 2 have one tile of clearance under the top wall.
    private const int Width = 60, FreeRows = 6;
    private const float MidY = (1 + FreeRows / 2f) * 16f;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        var corridor = Corridor();
        FreeSpaceSearch.WorldOverride = corridor;
        ClearanceField.Shared.Invalidate();
        try
        {
            Point start = new(2, 2), goal = new(Width - 2, 2);
            List<Vector2> unpriced = Plan(corridor, start, goal, priceClearance: false);
            List<Vector2> priced = Plan(corridor, start, goal, priceClearance: true);
            List<Vector2> smoothed = Route.Smooth(corridor, priced);
            float unpricedOffset = MeanOffset(Interior(unpriced));
            float pricedOffset = MeanOffset(Interior(priced));
            float smoothedOffset = MeanOffset(Sampled(smoothed));

            EmitLedgerRows.Measure("nav-replay", "NavReplay", "corridor-route-mean-offset-from-middle-px", pricedOffset, "px", "down",
                message: $"priced {pricedOffset:0.0} px, unpriced {unpricedOffset:0.0} px, smoothed {smoothedOffset:0.0} px; {priced.Count} corners raw, {smoothed.Count} after smoothing");

            int failures = 0;
            if (unpricedOffset < (FreeRows / 2f - 1f) * 16f - 1f)
                failures += Fail($"the unpriced route must hug the wall it starts beside for the comparison to mean anything; it sat {unpricedOffset:0.0} px off the middle");
            if (pricedOffset >= unpricedOffset * 0.5f)
                failures += Fail($"the priced route must sit nearer the middle than the unpriced one by a clear margin: {pricedOffset:0.0} px against {unpricedOffset:0.0} px");
            if (pricedOffset > 16f)
                failures += Fail($"the priced route must run within a tile of the middle on average; it sat {pricedOffset:0.0} px off");
            if (smoothedOffset > pricedOffset + 8f)
                failures += Fail($"smoothing must not pull the route back toward the walls: {smoothedOffset:0.0} px after against {pricedOffset:0.0} px before");
            for (int i = 0; i + 1 < smoothed.Count; i++)
                if (!CircleContact.SweptClear(corridor, smoothed[i], smoothed[i + 1]))
                    failures += Fail($"the smoothed route's segment {i} is not clear of the walls");

            // The body along the smoothed route: momentum steering through the contact, tick by tick.
            var route = new Route(smoothed, 1, corridor.Revision, OrbTerrain.Immunity);
            var offsets = new List<float>();
            float minClearance = float.PositiveInfinity;
            Vector2 centre = smoothed[0], velocity = Vector2.Zero;
            int ticks = 0;
            while (Vector2.Distance(centre, route.Goal) > Navigator.ArriveDistance && ticks < 2000)
            {
                Controls controls = SteerAlongRoute.Steer(new OrbState(centre, velocity), route, OrbPace.MaxSpeed, OrbPace.Acceleration, out _);
                Vector2 change = controls.Desired - velocity;
                float length = change.Length();
                if (length > OrbPace.Acceleration) change *= OrbPace.Acceleration / length;
                velocity += change;
                centre += velocity;
                CircleContact.Resolve(corridor, ref centre, ref velocity);
                minClearance = MathF.Min(minClearance, CircleContact.Clearance(corridor, centre));
                // The interior of the flight: past the departure and before the arrival, each a corridor height long.
                if (centre.X > 16f * (2 + FreeRows) && centre.X < 16f * (Width - 2 - FreeRows)) offsets.Add(MathF.Abs(centre.Y - MidY));
                ticks++;
            }
            float steeredOffset = offsets.Count == 0 ? float.NaN : offsets.Average();
            EmitLedgerRows.Measure("nav-replay", "NavReplay", "corridor-steered-mean-offset-from-middle-px", steeredOffset, "px", "down",
                message: $"{ticks} ticks along {route.RemainingLength(smoothed[0]):0} px, minimum clearance {minClearance:0.0} px");
            Console.WriteLine($"   corridor middle: route {string.Join(" ", smoothed.Select(p => $"{p.X:0},{p.Y:0}"))}; priced {pricedOffset:0.0} px, unpriced {unpricedOffset:0.0} px, smoothed {smoothedOffset:0.0} px, steered {steeredOffset:0.0} px over {ticks} ticks, minimum clearance {minClearance:0.0} px");
            if (ticks >= 2000)
                failures += Fail("the steered body never arrived at the corridor's far end");
            if (!(steeredOffset <= 16f))
                failures += Fail($"the steered body must fly within a tile of the middle on average; it flew {steeredOffset:0.0} px off");
            if (minClearance <= 0f)
                failures += Fail($"the steered body overlapped a wall: minimum clearance {minClearance:0.0} px");
            if (failures == 0)
                Console.WriteLine($"corridor middle: priced route {pricedOffset:0.0} px off the middle against {unpricedOffset:0.0} px unpriced, {smoothedOffset:0.0} px after smoothing, body {steeredOffset:0.0} px over {ticks} ticks");
            return failures;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            ClearanceField.Shared.Invalidate();
        }
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   corridor middle: {what}");
        return 1;
    }

    private static TextTileWorld Corridor()
    {
        var rows = new List<string>();
        for (int y = 0; y < FreeRows + 2; y++)
            rows.Add(y == 0 || y == FreeRows + 1 ? new string('#', Width + 2) : "#" + new string('.', Width) + "#");
        return new TextTileWorld(0, 0, rows);
    }

    private static List<Vector2> Plan(TextTileWorld world, Point start, Point goal, bool priceClearance)
    {
        var search = new FreeSpaceSearch(world, start, goal, priceClearance: priceClearance);
        search.Advance(int.MaxValue);
        if (search.Stop != FreeSpaceSearch.StopReason.Found)
            throw new InvalidOperationException($"the corridor search did not find its goal: {search.Stop}");
        return search.RouteCorners()!.Select(CornerGraph.ToWorld).ToList();
    }

    /// <summary>The route past its departure and before its arrival, each a corridor height long, so the ends that must sit
    /// by the wall — the start and goal are there — do not count against the middle.</summary>
    private static IEnumerable<Vector2> Interior(List<Vector2> route)
        => route.Where(p => p.X > 16f * (2 + FreeRows) && p.X < 16f * (Width - 2 - FreeRows));

    /// <summary>A smoothed route is a few long segments, so it is sampled every four pixels along its interior rather than at its corners.</summary>
    private static IEnumerable<Vector2> Sampled(List<Vector2> route)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            float length = Vector2.Distance(route[i], route[i + 1]);
            for (float s = 0f; s < length; s += 4f)
            {
                Vector2 p = route[i] + (route[i + 1] - route[i]) * (s / length);
                if (p.X > 16f * (2 + FreeRows) && p.X < 16f * (Width - 2 - FreeRows)) yield return p;
            }
        }
    }

    private static float MeanOffset(IEnumerable<Vector2> points)
    {
        var list = points.ToList();
        return list.Count == 0 ? float.NaN : list.Average(p => MathF.Abs(p.Y - MidY));
    }
}
