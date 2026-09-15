#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// What the body does where it has been asked to be: it drifts around the spot instead of stopping on it,
/// because the owner ruled on 15 September 2026 that the orb is never strictly standing still, whatever it
/// is doing. The drift is Reynolds' wander (Steering Behaviors For Autonomous Characters, GDC 1999): a
/// target circles the spot along a flattened ellipse, and the rate it circles at is itself a small random
/// walk, so the path turns smoothly and keeps turning, where a fresh random direction every tick would
/// twitch. The rate never falls under its floor, so the target never parks, and the direction of circling
/// reverses now and then so the drift is not a steady orbit.
///
/// <para>The body pursues the target rather than seeking it: it asks for the target's own motion plus a
/// correction toward it, capped at the hover speed. A pure seek asks for a speed proportional to the gap,
/// and the first build did exactly that — measured in NavReplay's arrival row, the body sat two to four
/// pixels from its target and slowed to a tenth of a pixel a tick every time the target doubled back
/// towards it, which is a body standing still with extra steps. The hover speed sits under the settled
/// threshold on purpose, so a hovering body still reads as at rest to the follow objective and arriving
/// somewhere is still arriving.</para>
///
/// <para>The target is kept on this side of every wall the planner knows, liquids the body is not immune to
/// included: a target whose straight line from the spot is not clear for the circle is mirrored off the wall
/// that blocked it, and a mirror reverses the direction of circling with it, because reflecting a circular
/// path reverses its angular velocity — the first build mirrored the offset without reversing the rate, and
/// the same trace showed the target bounced straight back into the ceiling it had just been reflected off.
/// The random source is seeded per instance, which keeps a headless run reproducible.</para>
/// </summary>
public sealed class HoverAroundSpot
{
    private readonly Random random;
    private float angle, rate;
    private Vector2? previousTarget;

    public HoverAroundSpot(int seed = 0x0AC0)
    {
        random = new Random(seed);
        angle = (float)(random.NextDouble() * MathF.Tau);
        rate = Weights.HoverTurnRateMinimum;
    }

    /// <summary>The spot hovered around on the last call, or null once released.</summary>
    public Vector2? Anchor { get; private set; }

    /// <summary>The point the body was steered at on the last call, for the overlay and for a fixture that has to say what the hover asked for.</summary>
    public Vector2 LastTarget { get; private set; }

    /// <summary>Forget the spot, so a later hover anchors wherever it is then asked to.</summary>
    public void Release()
    {
        Anchor = null;
        previousTarget = null;
    }

    /// <summary>The controls that keep the body drifting around <paramref name="spot"/> this tick.</summary>
    public Controls Around(OrbState live, Vector2 spot, ITileWorld world)
    {
        // A spot that jumped is a new place to hover, and the target's last position says nothing about its motion there.
        if (Anchor is not Vector2 held || Vector2.DistanceSquared(held, spot) > Weights.HoverRadiusPixels * Weights.HoverRadiusPixels)
            previousTarget = null;
        Anchor = spot;
        float jitter = ((float)random.NextDouble() * 2f - 1f) * Weights.HoverTurnJitter;
        float magnitude = Math.Clamp(MathF.Abs(rate) + jitter, Weights.HoverTurnRateMinimum, Weights.HoverTurnRateMaximum);
        float sign = rate < 0f ? -1f : 1f;
        if (random.NextDouble() < Weights.HoverReverseChance) sign = -sign;
        rate = sign * magnitude;
        angle += rate;

        Vector2 target = ClearTarget(spot, world);
        LastTarget = target;
        Vector2 targetMotion = previousTarget is Vector2 last ? target - last : Vector2.Zero;
        previousTarget = target;
        Vector2 desired = targetMotion + (target - live.Centre) * Weights.HoverGain;
        if (desired.LengthSquared() > Weights.HoverSpeedPx * Weights.HoverSpeedPx)
            desired = Vector2.Normalize(desired) * Weights.HoverSpeedPx;
        return new Controls(OffTheWall(live.Centre, desired, world));
    }

    /// <summary>
    /// The request turned off any wall it would press the body into. The target is clear of walls from the spot,
    /// not from the body, and a body lagging round the ellipse can meet a corner the target's own path never
    /// touched — on a staircase of blocks the body met a step's corner and the pursuit asked into it for fourteen
    /// ticks running, each request killed by the contact, which leaves nothing along the wall to slide with when
    /// the request points almost straight at it. So the request is tried against the contact first, and the part
    /// pointing into the net push-out is mirrored rather than removed: the body turns away at the speed it asked
    /// for instead of stalling against the step. The net push rather than one tile's normal, because an inner
    /// corner is two tiles and a mirror off either face alone sends the body into the other.
    /// </summary>
    private static Vector2 OffTheWall(Vector2 centre, Vector2 desired, ITileWorld world)
    {
        Vector2 asked = centre + desired, resolved = asked, velocity = desired;
        if (!CircleContact.Resolve(world, ref resolved, ref velocity, OrbTerrain.Wall).Touched) return desired;
        Vector2 push = resolved - asked;
        if (push.LengthSquared() < 1e-8f) return desired;
        Vector2 normal = Vector2.Normalize(push);
        float into = Vector2.Dot(desired, normal);
        return into < 0f ? desired - 2f * into * normal : desired;
    }

    /// <summary>
    /// The wander's target for this tick's angle, kept on this side of every wall: the offset as it stands; then
    /// mirrored off whichever wall blocked it — the vertical part flipped for a floor or a ceiling, the horizontal part
    /// for a side wall — reversing the direction of circling; then the opposite point of the ellipse; then half the
    /// offset; and only then the spot itself, with the circling reversed so the next tick heads back the way it came.
    /// </summary>
    private Vector2 ClearTarget(Vector2 spot, ITileWorld world)
    {
        float radius = Weights.HoverRadiusPixels, share = Weights.HoverVerticalShare;
        Vector2 offset = new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * share);
        bool Clear(Vector2 attempt) => CircleContact.SweptClear(world, spot, spot + attempt, OrbTerrain.Wall);

        if (Clear(offset)) return spot + offset;
        if (Clear(new Vector2(offset.X, -offset.Y))) { angle = -angle; rate = -rate; return spot + new Vector2(offset.X, -offset.Y); }
        if (Clear(new Vector2(-offset.X, offset.Y))) { angle = MathF.PI - angle; rate = -rate; return spot + new Vector2(-offset.X, offset.Y); }
        if (Clear(-offset)) { angle += MathF.PI; return spot - offset; }
        if (Clear(offset * 0.5f)) return spot + offset * 0.5f;
        rate = -rate;
        return spot;
    }
}
