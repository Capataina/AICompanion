#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using Weights = AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// The accompanying target is continuous: it starts where the body is whenever the walk begins again, and it never moves faster
/// than the walk itself, whatever the region's lead does. A sentinel's review of the company lane found both broken. The walk's
/// place in the box outlived every request that was not accompanying, so the first target after a job was 434 px from the body and
/// the body sprinted at 9 px a tick; and closing the rear of the box when the player's lead crossed its threshold moved the target
/// 200 px in one tick. Both are the owner's third-play complaint of follow spots jumping, reached by new mechanisms.
///
/// <para>Every scene drives <see cref="CoordinateMovement"/> the way the brain tick does, in an open room with the box standing
/// still, so every motion of the target is the walk's own. The pass lines were declared before the first run:</para>
/// <list type="bullet">
/// <item>after the body has done something else for three hundred ticks — hovered at a job, travelled to a place, or been held
/// by a downing — the first accompanying target is within one walk step of the body;</item>
/// <item>a body entering the walk from beyond the box's open part, where the latch still counts it with the player, gets a first
/// target within one walk step of it;</item>
/// <item>while the player turns round, the lead sweeping through zero a pixel a tick or flipping in one tick, the target never
/// moves further in one tick than the walk's step with the open box's edge easing at the walk's speed across it: the step times
/// the square root of two.</item>
/// </list>
/// </summary>
internal static class VerifyAccompanyingTargetIsContinuous
{
    private static readonly Vector2 Centre = new(60 * 16, 30 * 16), HalfSize = new(300f, 120f);

    /// <summary>No floor on where the tour may draw a leg. Every row here is about the *continuity* of the
    /// target — one step from the body after an interruption, never faster than the walk — and bounding the
    /// part legs come from would change which places those rows are measuring the steps between. The box's own
    /// bottom edge is how the parameter spells "none".</summary>
    private static readonly float NoFloor = Centre.Y + HalfSize.Y;
    private const float Tolerance = 0.01f;

    public static int Run()
    {
        LimitPlanningWork.Unbounded = true;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        int failures = 0;
        try
        {
            var world = Room();
            MovementQueries.World = world;
            FreeSpaceSearch.WorldOverride = world;
            ClearanceField.Shared.Invalidate();
            foreach (Interruption kind in Enum.GetValues<Interruption>())
                failures += ResumesFromTheBody(world, kind);
            failures += EntersFromBeyondTheOpenPart(world);
            failures += TurningRoundMovesTheTargetAtTheWalksPace(world, sweep: true);
            failures += TurningRoundMovesTheTargetAtTheWalksPace(world, sweep: false);
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

    private enum Interruption { HoverAtAJob, TravelToAPlace, HeldWhileDowned }

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

    /// <summary>
    /// Walk until the target is well right of the box's centre, then do something else for three hundred ticks with the body on the
    /// box's left, then accompany again. The premise is that the walk really was on the right, or a stale offset would sit near the
    /// body by chance and the row would pass on nothing.
    /// </summary>
    private static int ResumesFromTheBody(ITileWorld world, Interruption kind)
    {
        var movement = new CoordinateMovement();
        Vector2 centre = Centre, velocity = Vector2.Zero;
        int tick = 0;
        for (; tick < 6000; tick++)
        {
            Step(world, ref centre, ref velocity, movement.Accompany(new OrbState(centre, velocity), Centre, HalfSize, Vector2.Zero, NoFloor, _ => false));
            if (tick > 100 && movement.Navigator.Hover.LastTarget.X > Centre.X + 150f) break;
        }
        if (movement.Navigator.Hover.LastTarget.X <= Centre.X + 150f)
            return Fail($"{kind} premise: the walk must reach the right of the box before the interruption, or a stale place proves nothing; it reached {movement.Navigator.Hover.LastTarget - Centre}");

        Vector2 elsewhere = Centre + new Vector2(-250f, 60f);
        if (kind != Interruption.TravelToAPlace) { centre = elsewhere; velocity = Vector2.Zero; }
        for (int i = 0; i < 300; i++)
        {
            var live = new OrbState(centre, velocity);
            Controls controls = kind switch
            {
                Interruption.HoverAtAJob => movement.HoverHere(live),
                Interruption.TravelToAPlace => movement.MoveTo(live, elsewhere),
                _ => movement.Hold(live, "downed"),
            };
            Step(world, ref centre, ref velocity, controls);
        }

        Controls first = movement.Accompany(new OrbState(centre, velocity), Centre, HalfSize, Vector2.Zero, NoFloor, _ => false);
        float gap = Vector2.Distance(centre, movement.Navigator.Hover.LastTarget);
        Vector2 last = movement.Navigator.Hover.LastTarget;
        float worst = 0f;
        Step(world, ref centre, ref velocity, first);
        for (int i = 0; i < 90; i++)
        {
            Step(world, ref centre, ref velocity, movement.Accompany(new OrbState(centre, velocity), Centre, HalfSize, Vector2.Zero, NoFloor, _ => false));
            worst = MathF.Max(worst, Vector2.Distance(last, movement.Navigator.Hover.LastTarget));
            last = movement.Navigator.Hover.LastTarget;
        }
        float step = Weights.AccompanyWanderSpeedPx;
        if (gap > step + Tolerance || worst > step * MathF.Sqrt(2f) + Tolerance)
            return Fail($"{kind}: the first target after the interruption is {gap:0.00} px from the body (at most one step, {step}), and the target's largest one-tick move over the next 90 ticks is {worst:0.00} px (at most {step * MathF.Sqrt(2f):0.00})");
        Console.WriteLine($"accompanying target is continuous: after {kind} the first target was {gap:0.00} px from the body and moved at most {worst:0.00} px a tick");
        return 0;
    }

    /// <summary>
    /// A travelling player's box, rear closed, and a body entering the walk behind the open part and beyond the box's inset, where the
    /// inside latch still counts it with the player. A walk that clamps the body's place into the open part before it starts puts
    /// the first target hundreds of pixels ahead of the body.
    /// </summary>
    private static int EntersFromBeyondTheOpenPart(ITileWorld world)
    {
        var movement = new CoordinateMovement();
        Vector2 lead = new(60f, 0f);
        Vector2 centre = Centre + new Vector2(-(HalfSize.X + Navigator.SettleRadius * 0.5f), 0f), velocity = Vector2.Zero;
        movement.Accompany(new OrbState(centre, velocity), Centre, HalfSize, lead, NoFloor, _ => false);
        float gap = Vector2.Distance(centre, movement.Navigator.Hover.LastTarget);
        if (gap > Weights.AccompanyWanderSpeedPx + Tolerance)
            return Fail($"entering from beyond the open part: the first target is {gap:0.00} px from the body (at most one step, {Weights.AccompanyWanderSpeedPx}); target {movement.Navigator.Hover.LastTarget - Centre}, body {centre - Centre}");
        Console.WriteLine($"accompanying target is continuous: a body entering behind the open part got a first target {gap:0.00} px away");
        return 0;
    }

    /// <summary>
    /// Walk with the box leading right until the target sits in the part a left lead closes, then turn the player round. The premise
    /// is that the target really sits in the part that closes, or nothing is clamped and the row proves nothing.
    /// </summary>
    private static int TurningRoundMovesTheTargetAtTheWalksPace(ITileWorld world, bool sweep)
    {
        var movement = new CoordinateMovement();
        Vector2 centre = Centre, velocity = Vector2.Zero;
        float closesFrom = (HalfSize.X - Navigator.SettleRadius) * Weights.AccompanyRearShare;
        int tick = 0;
        for (; tick < 8000; tick++)
        {
            Step(world, ref centre, ref velocity, movement.Accompany(new OrbState(centre, velocity), Centre, HalfSize, new Vector2(60f, 0f), NoFloor, _ => false));
            if (tick > 100 && movement.Navigator.Hover.LastTarget.X > Centre.X + closesFrom + 100f) break;
        }
        string name = sweep ? "the lead sweeping through zero" : "the lead flipping in one tick";
        if (movement.Navigator.Hover.LastTarget.X <= Centre.X + closesFrom + 100f)
            return Fail($"{name} premise: the target must sit well inside the part a left lead closes; it reached {movement.Navigator.Hover.LastTarget - Centre}");
        Vector2 last = movement.Navigator.Hover.LastTarget;
        float worst = 0f, worstLead = 0f;
        for (int s = 0; s <= 240; s++)
        {
            float lead = sweep ? MathF.Max(-60f, 60f - s) : -60f;
            Step(world, ref centre, ref velocity, movement.Accompany(new OrbState(centre, velocity), Centre, HalfSize, new Vector2(lead, 0f), NoFloor, _ => false));
            float moved = Vector2.Distance(last, movement.Navigator.Hover.LastTarget);
            if (moved > worst) { worst = moved; worstLead = lead; }
            last = movement.Navigator.Hover.LastTarget;
        }
        float bound = Weights.AccompanyWanderSpeedPx * MathF.Sqrt(2f) + Tolerance;
        if (worst > bound)
            return Fail($"{name}: the target moved {worst:0.00} px in one tick at lead {worstLead} (at most {bound:0.00})");
        bool closed = last.X <= Centre.X + closesFrom + Tolerance;
        if (!closed)
            return Fail($"{name}: after 240 ticks the target must be inside the part a left lead leaves open (x at most {closesFrom:0.0} from the centre); it is at {last - Centre}");
        Console.WriteLine($"accompanying target is continuous: with {name} the target moved at most {worst:0.00} px a tick and ended inside the open part");
        return 0;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   accompanying target is continuous: {what}");
        return 1;
    }
}
