#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The accompanying tour draws no leg below the floor its caller names, which in the game is the top of the
/// player's head.
///
/// <para>The region holds the player — that is the whole of what it is for, and lifting it off him was built and
/// reverted on 22 September 2026 because `PlayerIntentRegion.Accepts` is also combat's stand admission, so a
/// region above his head cannot admit a melee stand beside a hostile standing where he stands. Its lower part is
/// therefore level with his legs and with the floor he is on, and the tour draws each leg from the far edge of
/// the part open to it, so some legs are drawn at his shins. In the 10:05 capture of that day the body dives at
/// eight to nine pixels a tick on eighty rows, fifty of them under `accompany`, and the owner's words were
/// "always too close to the floor… it kept literally falling onto the floor".</para>
///
/// <para>The fix is a bound on where the walk may <em>aim</em> and on nothing else, so the region, its
/// membership, the reunion pull and every combat stand are untouched. This row is the bound: an open room with
/// no terrain in it at all, a box whose lower half lies below the floor line, the body starting above the line,
/// and every tick's target required to stay at or above it. The room is deliberately empty — the moment terrain
/// enters, the escape branch may legitimately take the walk below the floor, and that exemption has its own row
/// in `VerifyAccompanyGetsPastAnObstruction`, which runs the capture's real pocket with the capture's real head
/// and must still get out.</para>
///
/// <para>The target rather than the body is what is asserted, and the distinction is the point: the body lags
/// its target and drifts around it, so a body a few pixels under the line is the hover doing its job, while a
/// <em>target</em> under the line is the tour having aimed there. The body's own worst depth is printed beside
/// it so a reader can see the difference rather than infer it.</para>
/// </summary>
internal static class VerifyAccompanyStaysAboveHisHead
{
    private const int Ticks = 600;

    /// <summary>A box whose bottom edge is well below the floor line, so an unclamped tour has somewhere to aim
    /// that the clamped one must refuse. With a half-height of 110 and the settle radius inset, the part the
    /// walk may use runs 78 px each way from the centre; the floor sits 20 px below it, so 58 px of the lower
    /// part is out of bounds and a tour that ignores the floor cannot avoid using it over six hundred ticks.</summary>
    private static readonly Vector2 BoxCentre = new(60 * 16f, 30 * 16f);
    private static readonly Vector2 HalfSize = new(275f, 110f);
    private const float FloorBelowCentre = 20f;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        try
        {
            var world = EmptyRoom();
            MovementQueries.World = world;
            MovementQueries.Hazards = Array.Empty<Rectangle>();
            FreeSpaceSearch.WorldOverride = world;
            ClearanceField.Shared.Invalidate();

            float floor = BoxCentre.Y + FloorBelowCentre;
            float room = MathF.Max(0f, HalfSize.Y - Navigator.SettleRadius);
            // The premise: the floor has to cut into the part the walk would otherwise use, or an unclamped tour
            // satisfies the row by geometry and the clamp is never exercised.
            if (floor >= BoxCentre.Y + room)
                return Red($"the premise: the floor must lie inside the part the walk may use; it sits {FloorBelowCentre:0} px "
                    + $"below the centre against a part reaching {room:0} px");

            var movement = new CoordinateMovement();
            Vector2 centre = new(BoxCentre.X, BoxCentre.Y - 40f), velocity = Vector2.Zero;
            float worstTarget = 0f, worstBody = 0f;
            int worstTargetTick = -1, ticksBelow = 0;
            float highest = centre.Y, lowest = centre.Y;
            for (int tick = 0; tick < Ticks; tick++)
            {
                Step(world, ref centre, ref velocity,
                    movement.Accompany(new OrbState(centre, velocity), BoxCentre, HalfSize, Vector2.Zero, floor, _ => false));
                float belowTarget = movement.Navigator.Hover.LastTarget.Y - floor;
                if (belowTarget > 0.01f)
                {
                    ticksBelow++;
                    if (belowTarget > worstTarget) { worstTarget = belowTarget; worstTargetTick = tick; }
                }
                worstBody = MathF.Max(worstBody, centre.Y - floor);
                highest = MathF.Min(highest, centre.Y);
                lowest = MathF.Max(lowest, centre.Y);
            }

            string run = $"{ticksBelow} of {Ticks} ticks aimed below the floor, worst {worstTarget:0.0} px at tick {worstTargetTick}; "
                + $"the body itself reached {worstBody:0.0} px below it; body spanned y {highest:0} to {lowest:0} "
                + $"about a floor at {floor:0} in a box of {BoxCentre.Y - room:0}..{BoxCentre.Y + room:0}";

            if (ticksBelow > 0)
                return Red($"the accompanying tour must draw no leg below the floor its caller names: {run}. "
                    + "In the game that floor is the top of the player's head, and the region below it is his legs and "
                    + "the ground he stands on — which is what the 22 September play was reporting as a companion that "
                    + "kept falling onto the floor.");
            // The body may drift a little under the line and that is the hover rather than the tour; it is printed
            // rather than asserted, because a bound on the body would be a bound on the drift this walk exists to have.
            if (lowest <= highest + 1f)
                return Red($"the premise: the walk must still move vertically inside what is left of the box; {run}");
            Console.WriteLine($"accompanying stays above his head: {run}");
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

    /// <summary>Empty on purpose: with no terrain inside the box, nothing can send the walk to the escape branch,
    /// so what this row measures is the ordinary draw and only the ordinary draw.</summary>
    private static TextTileWorld EmptyRoom()
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

    private static int Red(string message)
    {
        Console.WriteLine("RED: " + message);
        return 1;
    }
}
