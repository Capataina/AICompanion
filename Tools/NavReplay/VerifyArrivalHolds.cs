#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Tools.Ledger;

/// <summary>
/// A body the navigator has called arrived stays arrived, game-free: it does not coast back out
/// of the arrival radius on its own momentum and start a new search.
///
/// This is the positioning contract's foundation rather than the positioner's own fixture. The
/// positioner needs the senses, which need the game, so it is exercised by the native suite; what
/// can be shown here is the property every destination it hands out depends on. The navigator
/// declares arrival inside its radius and answers with no desired velocity, and the motor then
/// decays the velocity by the acceleration each tick — the same law this loop integrates, because
/// the motor is the adapter this project cannot compile. If the steering brakes too late, the
/// body arrives with momentum, coasts out of the radius, the next tick replans, and every
/// destination becomes a replan cycle that a world run would read as jitter at each goal and blame
/// on positioning.
///
/// The measurement is the number of ticks the body stays arrived after the first arrival, and the
/// search identity across them; the pass line is a whole hold with the identity unchanged, and the
/// body at rest — under the settled threshold — by the end of it, because the settled streak reads
/// exactly that.
/// </summary>
internal static class VerifyArrivalHolds
{
    private const int Width = 60, FreeRows = 6;
    // Long enough for the settled streak and the positioner's rescore to pass several times over.
    private const int HoldTicks = 120;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        var corridor = Corridor();
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        MovementQueries.World = corridor;
        FreeSpaceSearch.WorldOverride = corridor;
        ClearanceField.Shared.Invalidate();
        try
        {
            Vector2 start = CornerGraph.ToWorld(new Point(2, 2)), goal = CornerGraph.ToWorld(new Point(Width - 2, 2));
            var navigator = new Navigator();
            Vector2 centre = start, velocity = Vector2.Zero;
            int ticks = 0, firstArrival = -1, heldFor = 0;
            long searchAtArrival = -1;
            float fastestAfterArrival = 0f, speedAtEnd = float.NaN, farthestAfterArrival = 0f;
            while (ticks < 3000)
            {
                Controls controls = navigator.MoveTo(new OrbState(centre, velocity), goal);
                // The motor's law: accelerate toward the desired velocity by at most the acceleration.
                Vector2 change = controls.Desired - velocity;
                float length = change.Length();
                if (length > OrbPace.Acceleration) change *= OrbPace.Acceleration / length;
                velocity += change;
                centre += velocity;
                CircleContact.Resolve(corridor, ref centre, ref velocity);
                ticks++;
                if (navigator.Arrived && firstArrival < 0)
                {
                    firstArrival = ticks;
                    searchAtArrival = navigator.SearchId;
                }
                if (firstArrival >= 0)
                {
                    if (!navigator.Arrived || navigator.SearchId != searchAtArrival) break;
                    heldFor++;
                    fastestAfterArrival = MathF.Max(fastestAfterArrival, velocity.Length());
                    farthestAfterArrival = MathF.Max(farthestAfterArrival, Vector2.Distance(centre, goal));
                    if (heldFor >= HoldTicks) { speedAtEnd = velocity.Length(); break; }
                }
            }

            EmitLedgerRows.Measure("nav-replay", "NavReplay", "arrival-held-ticks", heldFor, "ticks", "up",
                message: $"arrived at tick {firstArrival}, held {heldFor} ticks, search {searchAtArrival}, fastest after arrival {fastestAfterArrival:0.00} px/tick, farthest {farthestAfterArrival:0.0} px");

            int failures = 0;
            if (firstArrival < 0)
                failures += Fail($"the body never arrived: {ticks} ticks, {Vector2.Distance(centre, goal):0} px from the goal, status {navigator.Status}");
            else
            {
                if (heldFor < HoldTicks)
                    failures += Fail($"arrival was lost after {heldFor} ticks: arrived={navigator.Arrived}, search {searchAtArrival} -> {navigator.SearchId}, {Vector2.Distance(centre, goal):0.0} px from the goal");
                if (farthestAfterArrival > Navigator.ArriveDistance)
                    failures += Fail($"the body coasted {farthestAfterArrival:0.0} px from the goal after arriving, past the {Navigator.ArriveDistance:0} px radius");
                if (!(speedAtEnd <= AICompanion.Companion.Brain.Infrastructure.Selection.Weights.SettledSpeedPx))
                    failures += Fail($"the body must be at rest by the end of the hold; it was moving at {speedAtEnd:0.00} px/tick against the settled threshold of {AICompanion.Companion.Brain.Infrastructure.Selection.Weights.SettledSpeedPx:0.00}");
            }
            if (failures == 0)
                Console.WriteLine($"arrival holds: arrived at tick {firstArrival}, held {heldFor} ticks on one search, at rest at {speedAtEnd:0.00} px/tick");
            return failures;
        }
        finally
        {
            LimitPlanningWork.Unbounded = false;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   arrival holds: {what}");
        return 1;
    }

    private static TextTileWorld Corridor()
    {
        var rows = new System.Collections.Generic.List<string>();
        for (int y = 0; y < FreeRows + 2; y++)
            rows.Add(y == 0 || y == FreeRows + 1 ? new string('#', Width + 2) : "#" + new string('.', Width) + "#");
        return new TextTileWorld(0, 0, rows);
    }
}
