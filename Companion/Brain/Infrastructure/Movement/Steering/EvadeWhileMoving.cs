#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// Safety on top of the job: the controls the job asked for, bent away from a predicted hit only when
/// following them would meet one. The owner ruled on 15 September 2026 that avoiding harm is not a job of
/// its own — whatever the companion is doing, guarding, following or mining, it keeps doing, and getting
/// out of harm's way rides on that motion. So nothing here suspends an activity or owns the body; it takes
/// the controls the tick already produced and returns controls.
///
/// <para>The shape is context steering (Andrew Fray, GDC 2013 AI Summit; Game AI Pro 2, chapter 18): every
/// candidate heading is scored for danger and for interest, danger masks, and interest chooses among what
/// danger leaves. Danger is how soon a heading meets the predicted-collision predicate when the body is run
/// forward through <see cref="OrbPace.Step"/> and the contact, so a heading into a wall that would stop the
/// body under a falling shot reads as dangerous as a heading into the shot. Interest is agreement with where
/// the job was going, so of the headings that stay clear the one that keeps doing the job wins, and the
/// body curves around a hit rather than breaking off. A stop is the last candidate and wins only when it is
/// safer than every moving heading by more than the danger tolerance, because the owner asked that the orb
/// never simply stand still. Water and lava the body is not immune to are a mask rather than a danger to be traded:
/// while any candidate stays dry for the whole horizon, a candidate that reaches forbidden liquid is not a candidate at
/// all, so a body that cannot avoid both a hit and the liquid takes the hit. The contact the simulation runs pushes out
/// of solid tiles only, and a dodge scored against it alone flew a body squeezed between two shots twenty-five pixels
/// into lava; counting liquid as one more "unsafe at tick N" fixed that and then failed the same way at three times the
/// player's speed, where the fleeing body reached a corner, found the lava a tick later than the shot, and chose the
/// lava (15 September 2026). Being hit and being in lava are not the same size of mistake, so they are not scored on
/// one scale.</para>
///
/// <para>This replaced two things that took the body away from the job: combat spacing, a search for a
/// low-exposure cell that suspended the activity and, in the first play of the orb, parked it beside
/// zombies for thirteen seconds while the player walked away because the route to a spot was never dropped
/// when the spot stopped being safe; and the collision reflex's takeover, which suspended the activity and
/// snapped to one of eight headings at full speed. The reflex's forward simulation survives as the danger
/// reading; its ownership of the body does not.</para>
/// </summary>
public static class EvadeWhileMoving
{
    /// <summary>
    /// The controls to apply this tick: <paramref name="wanted"/> unchanged when running it forward stays
    /// clear for the whole horizon, otherwise the heading that stays clear longest and agrees most with it,
    /// at the body's full speed on a burst.
    /// </summary>
    public static Controls Bend(OrbState live, Controls wanted, Func<OrbState, int, bool> unsafeAtTick, ITileWorld world, out bool bent)
    {
        bent = false;
        int horizon = Weights.DodgeLookaheadTicks;
        if (SafeTicks(live, wanted.Desired, wanted.Burst, unsafeAtTick, world, horizon, out _) >= horizon) return wanted;

        Vector2 preference = wanted.Desired.LengthSquared() > 0.01f ? Vector2.Normalize(wanted.Desired)
            : live.Velocity.LengthSquared() > 0.01f ? Vector2.Normalize(live.Velocity) : Vector2.Zero;
        int headings = Weights.EvadeHeadings;
        float speed = OrbPace.MaxSpeed;
        // Candidates: the evenly spaced headings, the job's own heading, and a stop.
        int count = headings + 2;
        Span<Vector2> desires = stackalloc Vector2[count];
        Span<float> danger = stackalloc float[count];
        Span<float> interest = stackalloc float[count];
        for (int i = 0; i < headings; i++)
        {
            float a = i * MathF.Tau / headings;
            Vector2 direction = new(MathF.Cos(a), MathF.Sin(a));
            desires[i] = direction * speed;
            interest[i] = Vector2.Dot(direction, preference);
        }
        desires[headings] = preference * speed;
        interest[headings] = preference == Vector2.Zero ? 0f : 1f;
        desires[headings + 1] = Vector2.Zero;
        interest[headings + 1] = -2f;

        Span<bool> wet = stackalloc bool[count];
        bool anyDry = false;
        for (int i = 0; i < count; i++)
        {
            if (i == headings && preference == Vector2.Zero) { danger[i] = float.PositiveInfinity; wet[i] = true; continue; }
            danger[i] = 1f - SafeTicks(live, desires[i], burst: true, unsafeAtTick, world, horizon, out wet[i]) / (float)horizon;
            anyDry |= !wet[i];
        }
        // Liquid masks: when some candidate stays dry, every candidate that reaches forbidden liquid is out of the running,
        // and only the dry ones are compared by danger and interest. When none stays dry there is nothing to mask with and
        // the ordinary comparison stands.
        float least = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (float.IsFinite(danger[i]) && !(anyDry && wet[i])) least = MathF.Min(least, danger[i]);
        int stop = headings + 1;
        int best = anyDry && wet[stop] ? -1 : stop;
        for (int i = 0; i < count; i++)
        {
            if (i == stop || !float.IsFinite(danger[i]) || (anyDry && wet[i])) continue;
            if (danger[i] > least + Weights.EvadeDangerTolerance) continue;
            if (best < 0) { best = i; continue; }
            if (best == stop) { if (danger[i] <= danger[stop] + Weights.EvadeDangerTolerance) best = i; continue; }
            if (interest[i] > interest[best]) best = i;
        }
        if (best < 0) best = stop;
        bent = true;
        return new Controls(desires[best], Burst: true);
    }

    /// <summary>How many ticks the body stays clear of the predicate, and of any liquid that hurts it, when it asks for
    /// <paramref name="desired"/> every tick; <paramref name="wet"/> says whether it is liquid it meets inside the horizon,
    /// which is checked for the whole horizon even after a predicted hit, because a hit ends the danger reading and not the
    /// flight.</summary>
    private static int SafeTicks(OrbState live, Vector2 desired, bool burst, Func<OrbState, int, bool> unsafeAtTick, ITileWorld world, int horizon, out bool wet)
    {
        Vector2 centre = live.Centre, velocity = live.Velocity;
        LiquidImmunity immunity = OrbTerrain.Immunity;
        int safe = horizon;
        wet = false;
        for (int tick = 1; tick <= horizon; tick++)
        {
            velocity = OrbPace.Step(velocity, desired, burst);
            Vector2 next = centre + velocity;
            CircleContact.Resolve(world, ref next, ref velocity);
            centre = next;
            if (safe == horizon && unsafeAtTick(new OrbState(centre, velocity), tick)) safe = tick - 1;
            if (CircleContact.Touches(centre, (x, y) => OrbTerrain.WetWall(world, x, y, immunity)))
            {
                wet = true;
                return Math.Min(safe, tick - 1);
            }
        }
        return safe;
    }
}
