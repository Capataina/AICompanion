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
/// <para>The target is kept on this side of every wall the planner knows: a target whose straight line from the spot is
/// not clear for the circle is mirrored off the wall
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
        return new Controls(Pursue(live.Centre, target, targetMotion, world));
    }

    /// <summary>
    /// The pursuit alone, with nothing about the wander advanced: the target's motion plus a correction toward it, capped
    /// under the settled threshold and turned off any wall it would press into. The evade layer's keep test flies a hover
    /// through this against the last target held still, which is a fair forecast over a lookahead because the hover's own
    /// speed cap keeps the target's path around the spot shorter than the body.
    /// </summary>
    public static Vector2 Pursue(Vector2 centre, Vector2 target, Vector2 targetMotion, ITileWorld world)
    {
        Vector2 desired = targetMotion + (target - centre) * Weights.HoverGain;
        if (desired.LengthSquared() > Weights.HoverSpeedPx * Weights.HoverSpeedPx)
            desired = Vector2.Normalize(desired) * Weights.HoverSpeedPx;
        return OffTheWall(centre, desired, world);
    }

    /// <summary>
    /// The controls that keep the body moving about a box that is itself moving — the player's intent region, as a centre,
    /// a half-size and the lead that leans it — rather than drifting around one spot. It is the same wander generalised:
    /// the target is a point walking across the box on a heading whose turning rate is itself a random walk through zero —
    /// signed and without the spot's floor, because a floor forces curvature and the heading closes loops — reflected at the box's edges
    /// the way the spot's target is mirrored off a wall, so the body eases to the front, falls back, rises and dips
    /// through the whole box and never flies to a place and stops. The owner ruled on 15 September 2026 that inside the
    /// region there is no place to be, only the region to move about in.
    ///
    /// <para>The target lives in the box's own frame: its offset from the centre is what walks, and the world target is
    /// the centre plus that offset. So the box's motion reaches the pursuit through the target's own motion, and nothing
    /// adds the region's velocity again — adding it as well would carry the body the region's travel twice.</para>
    ///
    /// <para>The pursuit is capped at the body's top speed, not at the hover speed. The hover's cap sits under the settled
    /// threshold so a body drifting about a spot still read as at rest, and that reason is gone with arrival; a body
    /// capped at a pixel and a fifth a tick inside a box moving at a running player's pace falls out of its back, is
    /// sent home as outside, re-enters, and falls out again.</para>
    ///
    /// <para>While the box leads, the rear share of it is closed to the target, so a travelling player has the companion
    /// level or ahead on his side rather than trailing as a policy. The box's own top is the ceiling: the region sits
    /// mostly above the player by construction, and a second ceiling under its top would cut away the part of it the
    /// owner put there. A target is refused where the circle cannot sweep to it from the last target, and wherever
    /// <paramref name="refused"/> says, which is the tiles the player is asking for; a refused target turns the walk
    /// round, and where every turn is refused the target jumps outward to the nearest allowed place. The flood
    /// refuses nothing here: the swept test already keeps the target in free space the body can fly to, and a place an
    /// unfinished flood has not reached is not an absence.</para>
    /// </summary>
    public Controls Across(OrbState live, Vector2 centre, Vector2 halfSize, Vector2 lead, Func<Vector2, bool> refused, ITileWorld world)
    {
        float inset = Navigator.SettleRadius;
        Vector2 room = new(MathF.Max(0f, halfSize.X - inset), MathF.Max(0f, halfSize.Y - inset));
        float minX = -room.X, maxX = room.X;
        if (lead.X > Weights.AccompanyLeadPixels) minX = -room.X * Weights.AccompanyRearShare;
        else if (lead.X < -Weights.AccompanyLeadPixels) maxX = room.X * Weights.AccompanyRearShare;
        Vector2 InBox(Vector2 offset) => new(Math.Clamp(offset.X, minX, maxX), Math.Clamp(offset.Y, -room.Y, room.Y));

        // A box that jumped — a teleport, a respawn, a new region — is a new place, and the walk starts from where the
        // body sits in it rather than from an offset that described somewhere else.
        float jump = OrbPace.MaxSpeed * Weights.AccompanyJumpTicks;
        if (acrossCentre is not Vector2 was || Vector2.DistanceSquared(was, centre) > jump * jump)
        {
            acrossOffset = InBox(live.Centre - centre);
            previousTarget = null;
        }
        acrossCentre = centre;
        Anchor = centre;

        // The turning rate walks through zero rather than keeping a floor. The spot hover's floor exists so its target never
        // parks on an ellipse; here a floor forces curvature, and with one the heading closed loops and an idle companion
        // measured from -0.03 to 0.81 of the room across the box over six hundred ticks, never reaching its left third.
        float jitter = ((float)random.NextDouble() * 2f - 1f) * Weights.AccompanyTurnJitter;
        acrossRate = Math.Clamp(acrossRate + jitter, -Weights.AccompanyTurnRateMaximum, Weights.AccompanyTurnRateMaximum);
        acrossHeading += acrossRate;

        Vector2 step = new(MathF.Cos(acrossHeading) * Weights.AccompanyWanderSpeedPx,
            MathF.Sin(acrossHeading) * Weights.AccompanyWanderSpeedPx * Weights.HoverVerticalShare);
        Vector2 next = acrossOffset + step;
        // Reflection at the box reverses the axis's part of the heading and the direction of turning together, for the
        // reason the spot's target does: reflecting a circular path reverses its angular velocity.
        if (next.X < minX || next.X > maxX) { acrossHeading = MathF.PI - acrossHeading; acrossRate = -acrossRate; }
        if (next.Y < -room.Y || next.Y > room.Y) { acrossHeading = -acrossHeading; acrossRate = -acrossRate; }
        next = InBox(next);

        Vector2 from = previousTarget ?? live.Centre;
        bool Free(Vector2 point) => !refused(point) && CircleContact.SweptClear(world, from, point, OrbTerrain.Wall);
        Vector2 target = centre + next;
        if (!Free(target))
        {
            // A blocked step turns the walk: round first, then across to either side, then back past either side, so a target
            // pressed against a wall slides off it rather than stopping there.
            bool turned = false;
            foreach (float turn in Turns)
            {
                float heading = acrossHeading + turn;
                Vector2 attempt = InBox(acrossOffset + new Vector2(MathF.Cos(heading),
                    MathF.Sin(heading) * Weights.HoverVerticalShare) * Weights.AccompanyWanderSpeedPx);
                if (!Free(centre + attempt)) continue;
                acrossHeading = heading;
                acrossRate = -acrossRate;
                next = attempt;
                target = centre + attempt;
                turned = true;
                break;
            }
            // Nowhere a step away is allowed: the body is boxed in, or standing in the tiles the player is asking for. The target
            // then jumps to the nearest allowed place the body can fly straight to, looked for outward along both axes, because a
            // target left on the body asks for no motion and the body stops where it is — on the tile the player is building on,
            // or in a one-body-tall passage he is walking down. A step is a pixel and a half and a refused tile is sixteen, so
            // every turned step of a body standing in the footprint is refused too, and without this the body stays put.
            if (!turned)
            {
                target = live.Centre;
                next = live.Centre - centre;
                float reachOut = MathF.Max(room.X, room.Y) * 2f;
                for (float distance = EscapeStepPixels; distance <= reachOut && target == live.Centre; distance += EscapeStepPixels)
                {
                    foreach (Vector2 axis in Axes)
                    {
                        Vector2 point = live.Centre + axis * distance;
                        Vector2 offset = point - centre;
                        if (InBox(offset) != offset || refused(point) || !CircleContact.SweptClear(world, live.Centre, point, OrbTerrain.Wall))
                            continue;
                        target = point;
                        next = offset;
                        break;
                    }
                }
            }
        }
        acrossOffset = next;
        LastTarget = target;
        Vector2 targetMotion = previousTarget is Vector2 last ? target - last : Vector2.Zero;
        LastTargetMotion = targetMotion;
        previousTarget = target;
        return new Controls(PursueAcross(live.Centre, target, targetMotion, world));
    }

    /// <summary>How far the accompanying target moved on the last call to <see cref="Across"/>, so the evade layer's keep
    /// test can carry the target on along the walk while it flies the pursuit forward.</summary>
    public Vector2 LastTargetMotion { get; private set; }

    /// <summary>
    /// The accompanying pursuit alone, with nothing about the walk advanced: the target's motion plus a correction toward it,
    /// capped at the body's top speed and turned off any wall it would press into. The evade layer's keep test flies this
    /// against the last target carried on by its last motion each simulated tick, because the accompanying target moves at
    /// the region's pace and a target held still would forecast a body slowing to it that the real walk never flies.
    /// </summary>
    public static Vector2 PursueAcross(Vector2 centre, Vector2 target, Vector2 targetMotion, ITileWorld world)
    {
        Vector2 desired = targetMotion + (target - centre) * Weights.AccompanyGain;
        if (desired.LengthSquared() > OrbPace.MaxSpeed * OrbPace.MaxSpeed)
            desired = Vector2.Normalize(desired) * OrbPace.MaxSpeed;
        return OffTheWall(centre, desired, world);
    }

    private Vector2 acrossOffset;
    private Vector2? acrossCentre;
    private float acrossHeading, acrossRate;

    /// <summary>The turns a blocked step tries, in order: round, across to either side, then back past either side.</summary>
    private static readonly float[] Turns = { MathF.PI, MathF.PI / 2f, -MathF.PI / 2f, 3f * MathF.PI / 4f, -3f * MathF.PI / 4f, MathF.PI / 4f, -MathF.PI / 4f };

    /// <summary>The directions an escape from a refused place looks along, outward from the body.</summary>
    private static readonly Vector2[] Axes = { new(1f, 0f), new(-1f, 0f), new(0f, -1f), new(0f, 1f) };

    /// <summary>How far apart the escape's samples are: half a tile, so a gap the circle fits through is not stepped over.</summary>
    private const float EscapeStepPixels = 8f;

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
