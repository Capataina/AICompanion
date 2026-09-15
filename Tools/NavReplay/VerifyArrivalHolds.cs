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
    // "Still" is the session reader's own threshold for a body that is not moving, and a run of it this long is the
    // stop the owner saw in play; the hover's floor keeps a drifting body above it.
    private const float StillSpeed = 0.3f;
    private const int StillRunTicks = 10;
    // The ramp from rest to nine tenths of the cap, declared before the easing change: well over the twelve ticks the single
    // shared acceleration took, which read as a snap, and inside a second so a departure still reads as a departure.
    private const int MinimumRampTicks = 24, MaximumRampTicks = 60;

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
            int stillTicks = 0, longestStill = 0, rampTicks = -1;
            var trace = new System.Collections.Generic.Queue<string>();
            string? stillTrace = null;
            float settled = AICompanion.Companion.Brain.Infrastructure.Selection.Weights.SettledSpeedPx;
            while (ticks < 3000)
            {
                Controls controls = navigator.MoveTo(new OrbState(centre, velocity), goal);
                // The motor's own law, from the one place it is written.
                velocity = OrbPace.Step(velocity, controls.Desired, controls.Burst);
                centre += velocity;
                CircleContact.Resolve(corridor, ref centre, ref velocity);
                ticks++;
                // Easing in: the ticks from rest to nine tenths of the cap on the way out, which is the speed-change
                // rate made visible and the number a change to it moves.
                if (rampTicks < 0 && velocity.Length() >= 0.9f * OrbPace.MaxSpeed) rampTicks = ticks;
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
                    stillTicks = velocity.Length() < StillSpeed ? stillTicks + 1 : 0;
                    // The last ticks before a still run trips, kept so a failure says what the hover was asking for.
                    trace.Enqueue($"t{ticks} centre {centre.X:0.0},{centre.Y:0.0} vel {velocity.X:0.00},{velocity.Y:0.00} desired {controls.Desired.X:0.00},{controls.Desired.Y:0.00} target {navigator.Hover.LastTarget.X:0.0},{navigator.Hover.LastTarget.Y:0.0} to-goal {Vector2.Distance(centre, goal):0.0}");
                    if (trace.Count > 24) trace.Dequeue();
                    if (stillTicks == StillRunTicks && stillTrace == null) stillTrace = string.Join(" | ", trace);
                    longestStill = Math.Max(longestStill, stillTicks);
                    if (heldFor >= HoldTicks) { speedAtEnd = velocity.Length(); break; }
                }
            }

            EmitLedgerRows.Measure("nav-replay", "NavReplay", "arrival-held-ticks", heldFor, "ticks", "up",
                message: $"arrived at tick {firstArrival}, held {heldFor} ticks, search {searchAtArrival}, fastest after arrival {fastestAfterArrival:0.00} px/tick, farthest {farthestAfterArrival:0.0} px, longest still run {longestStill}");
            EmitLedgerRows.Measure("nav-replay", "NavReplay", "ticks-to-cap-from-rest", rampTicks, "ticks", "none",
                message: $"from rest to nine tenths of {OrbPace.MaxSpeed:0.0} px/tick at a speed change of {OrbPace.SpeedChange:0.000} and a turn authority of {OrbPace.Turn:0.000}");

            int failures = 0;
            if (firstArrival < 0)
                failures += Fail($"the body never arrived: {ticks} ticks, {Vector2.Distance(centre, goal):0} px from the goal, status {navigator.Status}");
            else
            {
                if (heldFor < HoldTicks)
                    failures += Fail($"arrival was lost after {heldFor} ticks: arrived={navigator.Arrived}, search {searchAtArrival} -> {navigator.SearchId}, {Vector2.Distance(centre, goal):0.0} px from the goal");
                if (farthestAfterArrival > Navigator.SettleRadius)
                    failures += Fail($"the body drifted {farthestAfterArrival:0.0} px from the goal after arriving, past the {Navigator.SettleRadius:0} px settle radius");
                if (!(speedAtEnd <= settled))
                    failures += Fail($"the hovering body must still read as at rest by the end of the hold; it was moving at {speedAtEnd:0.00} px/tick against the settled threshold of {settled:0.00}");
                if (longestStill >= StillRunTicks)
                    failures += Fail($"the body sat under {StillSpeed:0.0} px/tick for {longestStill} ticks after arriving; the orb is never strictly standing still; the ticks up to it: {stillTrace}");
            }
            // The ramp is the owner's "slower acceleration" as a pass line declared before the change: well over the fifth of a
            // second the single shared acceleration took, and under a second so a departure is still a departure.
            if (rampTicks is < MinimumRampTicks or > MaximumRampTicks)
                failures += Fail($"from rest the body reached nine tenths of its cap in {rampTicks} ticks; eased motion wants between {MinimumRampTicks} and {MaximumRampTicks}");
            if (failures == 0)
                Console.WriteLine($"arrival holds: eased to the cap in {rampTicks} ticks, arrived at tick {firstArrival}, held {heldFor} ticks on one search, never still for {StillRunTicks} ticks, reading at rest at {speedAtEnd:0.00} px/tick");
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
