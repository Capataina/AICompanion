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
/// body curves around a hit rather than breaking off. A stop is the last candidate and wins only on
/// strictly less danger, because the owner asked that the orb never simply stand still.</para>
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
        if (SafeTicks(live, wanted.Desired, wanted.Burst, unsafeAtTick, world, horizon) >= horizon) return wanted;

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

        float least = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (i == headings && preference == Vector2.Zero) { danger[i] = float.PositiveInfinity; continue; }
            danger[i] = 1f - SafeTicks(live, desires[i], burst: true, unsafeAtTick, world, horizon) / (float)horizon;
            least = MathF.Min(least, danger[i]);
        }
        int best = headings + 1;
        for (int i = 0; i < count; i++)
        {
            bool eligible = i == headings + 1 ? danger[i] < least + 1e-4f : danger[i] <= least + Weights.EvadeDangerTolerance;
            if (!eligible) continue;
            bool stopIsBest = best == headings + 1;
            if (stopIsBest && i != headings + 1 && danger[i] <= danger[best] + Weights.EvadeDangerTolerance) { best = i; continue; }
            if (!stopIsBest && interest[i] > interest[best]) best = i;
        }
        bent = true;
        return new Controls(desires[best], Burst: true);
    }

    /// <summary>How many ticks the body stays clear of the predicate when it asks for <paramref name="desired"/> every tick.</summary>
    private static int SafeTicks(OrbState live, Vector2 desired, bool burst, Func<OrbState, int, bool> unsafeAtTick, ITileWorld world, int horizon)
    {
        Vector2 centre = live.Centre, velocity = live.Velocity;
        for (int tick = 1; tick <= horizon; tick++)
        {
            velocity = OrbPace.Step(velocity, desired, burst);
            Vector2 next = centre + velocity;
            CircleContact.Resolve(world, ref next, ref velocity);
            centre = next;
            if (unsafeAtTick(new OrbState(centre, velocity), tick)) return tick - 1;
        }
        return horizon;
    }
}
