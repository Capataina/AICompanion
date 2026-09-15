#nullable enable

using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>Why the evade layer did what it did on one tick.</summary>
public enum EvadeReason
{
    /// <summary>No predicate this tick, so nothing was tested.</summary>
    Off,
    /// <summary>The job's own flight stayed clear of hits for the whole lookahead, and its controls went out untouched.</summary>
    Kept,
    /// <summary>The job's own flight met a predicted hit.</summary>
    Hit,
}

/// <summary>Which candidate a bent tick flew.</summary>
public enum EvadeChoice { Job, JobHeading, Heading, Stop }

/// <summary>
/// One tick of the evade layer, for the record: the reason, the tick of the lookahead at which the job's own flight met a hit
/// (-1 for never), the candidate chosen, and how many candidates each refusal removed — going nowhere, and being more dangerous
/// than the least dangerous by more than the tolerance.
/// </summary>
public readonly record struct EvadeVerdict(EvadeReason Reason, int HitTick, EvadeChoice Choice, int RefusedNowhere, int RefusedDanger)
{
    public static readonly EvadeVerdict Off = new(EvadeReason.Off, -1, EvadeChoice.Job, 0, 0);

    public bool Bent => Reason is EvadeReason.Hit;
}

/// <summary>
/// Safety on top of the job: the controls the job asked for, bent away from a predicted hit only when
/// the job's own flight would meet one. The owner ruled on 15 September 2026 that avoiding harm is not a job of
/// its own — whatever the companion is doing, guarding, following or mining, it keeps doing, and getting
/// out of harm's way rides on that motion. So nothing here suspends an activity or owns the body; it takes
/// the controls the tick already produced and returns controls.
///
/// <para><b>The keep test flies the job, not one velocity.</b> The job's controls for this tick are applied first, and from
/// the second tick of the lookahead on the body asks whatever the job's own steering would ask from where the simulation has
/// put it — a route steered along from the simulated state, a direct line eased toward its goal, a hover pursuing its target —
/// through a forecast the movement boundary builds without touching the navigator or the hover. The first build held the
/// tick's one velocity in a straight line instead, which is a different flight whenever the job curves. In the third play of
/// the orb (capture 2026-09-15_13-16-33-496, tick 18,607) a route descending along the top of a pool read as flying into the
/// pool, the job was refused with no hit anywhere, and the body was held against a slope for 2,491 ticks. With no forecast
/// the job's controls are held for the lookahead, which is the old reading and is only ever a fallback.</para>
///
/// <para>The choice among candidates is context steering (Andrew Fray, GDC 2013 AI Summit; Game AI Pro 2, chapter 18): every
/// candidate heading is scored for danger and for interest, danger masks, and interest chooses among what danger leaves.
/// Danger is how soon a heading meets the predicted-collision predicate when the body is run forward through
/// <see cref="OrbPace.Step"/> and the contact. Interest is agreement with where the job was going, so of the headings that stay
/// clear the one that keeps doing the job wins, and the body curves around a hit rather than breaking off. A stop is the last
/// candidate and wins only when it is safer than every moving heading by more than the danger tolerance, because the owner
/// asked that the orb never simply stand still.</para>
///
/// <para><b>A heading that goes nowhere is a stop.</b> The contact holds a body out of a wall, so a heading straight into one
/// reads as safe and dry for the whole lookahead while the body does not move, and interest then prefers it to every heading
/// that moves — that is the other half of the 2,491-tick pin. A candidate whose simulated body ends the lookahead less than
/// <see cref="NowhereDistance"/> from where it began is scored exactly as the stop is, so it can only ever tie with the stop,
/// and a tie goes to the stop's zero request rather than a request that shoves the body into the wall at full speed.</para>
///
/// <para><b>Liquid plays no part in the choice.</b> Every liquid is air to the body, by the owner's ruling of 15 September 2026,
/// so a heading into a pool is scored exactly as a heading into open air and a dodge is free to use a pool as space. Before the
/// ruling water and lava were a mask over the candidates, compared apart from hits because a hit and a touch of lava were not
/// the same size of mistake; the mask went with the hurt it guarded against, and a body cornered over lava now dodges into it.</para>
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
    /// How far a candidate's simulated body must end from where it began to count as going somewhere: the body's own radius.
    /// A displacement smaller than the body cannot carry it clear of anything a hit or a shot occupies, and a free heading
    /// covers an order of magnitude more — a candidate bursts, so from rest its speed grows by the turn authority each tick,
    /// which at the fallback pace is 0.48 × 20 × 21 / 2 ≈ 100 px over the twenty-tick lookahead, capped near the end by the
    /// 9 px speed. A body the contact is holding against a wall moves by a pixel or two of settling.
    /// </summary>
    public static float NowhereDistance => CircleContact.Radius;

    /// <summary>
    /// The controls to apply this tick: <paramref name="wanted"/> unchanged when the job's own flight — the wanted controls
    /// first, then <paramref name="forecast"/> from each simulated state, or the wanted controls held when there is no
    /// forecast — stays clear of hits for the whole lookahead; otherwise the candidate that stays clear of hits within the
    /// tolerance of the best, goes somewhere, and agrees most with the job, at full speed on a burst.
    /// </summary>
    public static Controls Bend(OrbState live, Controls wanted, Func<OrbState, int, bool> unsafeAtTick, ITileWorld world,
        out EvadeVerdict verdict, Func<OrbState, Controls>? forecast = null)
    {
        int horizon = Weights.DodgeLookaheadTicks;
        Flight job = Fly(live, wanted, forecast ?? (_ => wanted), unsafeAtTick, world, horizon);
        if (job.HitTick < 0)
        {
            verdict = new EvadeVerdict(EvadeReason.Kept, -1, EvadeChoice.Job, 0, 0);
            return wanted;
        }

        Vector2 preference = wanted.Desired.LengthSquared() > 0.01f ? Vector2.Normalize(wanted.Desired)
            : live.Velocity.LengthSquared() > 0.01f ? Vector2.Normalize(live.Velocity) : Vector2.Zero;
        int headings = Weights.EvadeHeadings;
        float speed = OrbPace.MaxSpeed;
        // Candidates: the evenly spaced headings, the job's own heading, and a stop.
        int count = headings + 2, jobHeading = headings, stop = headings + 1;
        Span<Vector2> desires = stackalloc Vector2[count];
        Span<float> interest = stackalloc float[count];
        for (int i = 0; i < headings; i++)
        {
            float a = i * MathF.Tau / headings;
            Vector2 direction = new(MathF.Cos(a), MathF.Sin(a));
            desires[i] = direction * speed;
            interest[i] = Vector2.Dot(direction, preference);
        }
        desires[jobHeading] = preference * speed;
        interest[jobHeading] = 1f;
        desires[stop] = Vector2.Zero;
        interest[stop] = -2f;

        // Each candidate's danger and whether it is still in the running. A candidate that goes nowhere is scored as the stop,
        // which the stop itself already is, so it leaves the running and the stop stands for it.
        Span<float> danger = stackalloc float[count];
        Span<bool> open = stackalloc bool[count];
        int refusedNowhere = 0, refusedDanger = 0;
        for (int i = 0; i < count; i++)
        {
            if (i == jobHeading && preference == Vector2.Zero) continue;
            var ask = new Controls(desires[i], Burst: true);
            Flight flight = Fly(live, ask, _ => ask, unsafeAtTick, world, horizon);
            danger[i] = flight.HitTick < 0 ? 0f : 1f - (flight.HitTick - 1) / (float)horizon;
            open[i] = true;
            if (i != stop && flight.Displacement < NowhereDistance)
            {
                open[i] = false;
                refusedNowhere++;
            }
        }
        float least = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (open[i]) least = MathF.Min(least, danger[i]);

        int best = open[stop] ? stop : -1;
        for (int i = 0; i < count; i++)
        {
            if (i == stop || !open[i]) continue;
            if (danger[i] > least + Weights.EvadeDangerTolerance) { refusedDanger++; continue; }
            if (best < 0) { best = i; continue; }
            if (best == stop) { if (danger[i] <= danger[stop] + Weights.EvadeDangerTolerance) best = i; continue; }
            if (interest[i] > interest[best]) best = i;
        }
        if (best < 0) best = stop;
        EvadeChoice choice = best == stop ? EvadeChoice.Stop : best == jobHeading ? EvadeChoice.JobHeading : EvadeChoice.Heading;
        verdict = new EvadeVerdict(EvadeReason.Hit, job.HitTick, choice, refusedNowhere, refusedDanger);
        return new Controls(desires[best], Burst: true);
    }

    private readonly record struct Flight(int HitTick, float Displacement);

    /// <summary>
    /// The body run forward for the lookahead through the motor's law and the contact: <paramref name="first"/> on the first
    /// tick and <paramref name="next"/>'s answer from each simulated state after it. Returns the first tick the predicate
    /// fires (-1 for never) and how far the body ends from where it began.
    /// </summary>
    private static Flight Fly(OrbState live, Controls first, Func<OrbState, Controls> next, Func<OrbState, int, bool> unsafeAtTick, ITileWorld world, int horizon)
    {
        Vector2 centre = live.Centre, velocity = live.Velocity;
        int hit = -1;
        Controls ask = first;
        for (int tick = 1; tick <= horizon; tick++)
        {
            if (tick > 1) ask = next(new OrbState(centre, velocity));
            velocity = OrbPace.Step(velocity, ask.Desired, ask.Burst);
            Vector2 moved = centre + velocity;
            CircleContact.Resolve(world, ref moved, ref velocity);
            centre = moved;
            if (hit < 0 && unsafeAtTick(new OrbState(centre, velocity), tick)) hit = tick;
        }
        return new Flight(hit, Vector2.Distance(centre, live.Centre));
    }
}
