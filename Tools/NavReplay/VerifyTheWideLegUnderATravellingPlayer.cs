#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A wide leg drawn while the player is travelling stays inside his box and does not carry the companion
/// past him.
///
/// <para>The accompanying tour keeps a travelling player's companion level with him or ahead by closing the
/// rear of the box to the target while the lead is live. A leg the body has no straight way to is drawn from
/// the <em>whole</em> box instead, which suspends that policy for as long as that leg lasts — the case the
/// pocket of the 10:05 capture forced, where every place inside the open part lay behind rock. Every scene
/// that exercised the wide draw before this one held the box still, because the capture's player was
/// standing, so what a wide leg does while the box travels was argued rather than measured.</para>
///
/// <para>This row measures it. A travelling box carries a lead, an overhang stands in the part that lead
/// leaves open so the ordinary far-edge draw can find nothing to reach, and the walk is driven for six
/// hundred ticks with the box advancing at a running player's pace. Two things are asserted and the rest is
/// printed: a wide leg must actually occur, or the row is measuring the ordinary draw under a different
/// name; and the target must stay inside the box on every tick, which is the invariant the wide draw widens
/// rather than escapes — `InBox` remains the only clamp, so a target outside the whole box means the widened
/// bounds and the leg they belong to have come apart.</para>
///
/// <para>How far the body trails the travelling player is printed rather than asserted, deliberately. The
/// rear share is a preference the wide leg is allowed to suspend, so there is no threshold here that is not
/// invented; what the number is for is that the next person to change the wide draw can see what it did to
/// trailing before and after.</para>
/// </summary>
internal static class VerifyTheWideLegUnderATravellingPlayer
{
    private const int Ticks = 600;
    /// <summary>A running player's pace, which is what the box travels at while he holds a direction key.</summary>
    private const float BoxSpeedPx = 5f;
    private static readonly Vector2 HalfSize = new(261f, 105f);
    /// <summary>Well past `Weights.AccompanyLeadPixels`, so the rear of the box is closed for the whole run and a
    /// wide leg is genuinely suspending something.</summary>
    private static readonly Vector2 Lead = new(120f, 0f);

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        try
        {
            var world = ATunnelNarrowerThanTheBox(out Vector2 start);
            MovementQueries.World = world;
            MovementQueries.Hazards = Array.Empty<Rectangle>();
            FreeSpaceSearch.WorldOverride = world;
            ClearanceField.Shared.Invalidate();

            var movement = new CoordinateMovement();
            Vector2 centre = start, velocity = Vector2.Zero;
            Vector2 box = start + new Vector2(60f, 0f);
            float floor = box.Y + HalfSize.Y;   // no floor: this row is about the lead, not the head
            int wideTicks = 0, outsideTicks = 0;
            float worstOutside = 0f, worstBehind = 0f, worstAhead = 0f;
            for (int tick = 0; tick < Ticks; tick++)
            {
                box.X += BoxSpeedPx;
                Step(world, ref centre, ref velocity,
                    movement.Accompany(new OrbState(centre, velocity), box, HalfSize, Lead, floor, _ => false));
                if (movement.Navigator.Hover.AcrossLegIsWide) wideTicks++;
                Vector2 offset = movement.Navigator.Hover.LastTarget - box;
                float outside = MathF.Max(MathF.Abs(offset.X) - HalfSize.X, MathF.Abs(offset.Y) - HalfSize.Y);
                if (outside > 0.01f) { outsideTicks++; worstOutside = MathF.Max(worstOutside, outside); }
                // Behind and ahead of the box's own centre, which travels with him: positive behind means the
                // body is west of it, positive ahead means east.
                worstBehind = MathF.Max(worstBehind, box.X - centre.X);
                worstAhead = MathF.Max(worstAhead, centre.X - box.X);
            }

            string run = $"{wideTicks} of {Ticks} ticks crossed a leg drawn from the whole box; the target left the box on "
                + $"{outsideTicks} tick(s), worst {worstOutside:0.0} px; the body reached {worstBehind:0} px behind the box's centre "
                + $"and {worstAhead:0} px ahead of it, against a box half-width of {HalfSize.X:0}";

            // The premise. A scene that produced no wide leg would satisfy everything below by never
            // exercising the thing this row exists for, and it would do so silently.
            if (wideTicks == 0)
                return Red($"the premise: the scene must produce a leg drawn from the whole box while the player travels, "
                    + $"or this row is measuring the ordinary draw under another name; {run}");
            if (outsideTicks > 0)
                return Red($"a wide leg widens the part the walk may use and never escapes the box: {run}. "
                    + "InBox is the only clamp the walk has, so a target outside the whole box means the widened bounds and "
                    + "the leg they were widened for have come apart.");
            // The one the wide draw could actually break, and the reason the rear share's suspension is worth
            // measuring at all: a leg drawn from the whole box may send the companion to the front of it, and
            // the front of a travelling player's box is as far ahead of him as the walk may ever put the body.
            if (worstAhead > HalfSize.X)
                return Red($"a leg drawn from the whole box may suspend the rear share, but it may not carry the body past the "
                    + $"front of the box the player is inside: {run}");
            Console.WriteLine($"the wide leg under a travelling player: {run}");
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

    /// <summary>A tunnel three tiles tall running the length of the world, inside a box thirteen tiles tall. Most
    /// of the box is therefore rock, so most places drawn at its far edge are inside a wall and cannot be swept
    /// to, which is what sends the draw wide — the same shape as the capture's pocket, made repeatable and given
    /// a box that travels along it.</summary>
    private static TextTileWorld ATunnelNarrowerThanTheBox(out Vector2 start)
    {
        const int width = 400, height = 60;
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++) row[x] = y >= 30 && y <= 32 && x > 1 && x < width - 2 ? '.' : '#';
            rows.Add(new string(row));
        }
        start = new Vector2(30 * 16f, 31 * 16f + 8f);
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
