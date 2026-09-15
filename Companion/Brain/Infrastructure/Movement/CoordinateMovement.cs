#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The movement entry point for the brain. It owns the navigator's retained route and the hold's anchor, so callers hand
/// it a live body and an intent rather than manipulating routes or searches independently. Every caller supplies intent
/// here; only the motor applies what comes back.
///
/// <para>A retained state search lived here too — a flood toward the nearest place a predicate accepted, allowed through
/// liquid the body stood in — and its only caller was the environmental escape. Every liquid became air to the orb on
/// 15 September 2026, the escape had nothing left to leave, and the search went with it.</para>
/// </summary>
public sealed class CoordinateMovement
{
    public Navigator Navigator { get; } = new();

    public Controls MoveTo(OrbState live, Vector2 goal)
    {
        holdAnchor = null;
        Produced(Producer.Navigator);
        return Navigator.MoveTo(live, goal);
    }

    /// <summary>
    /// Stop wanting anything, and ask the motor for nothing. This is for owners that take the body away from
    /// the brain — downing, recovery flight — and for nothing the brain chooses: a brain that holds hovers
    /// (<see cref="HoverHere"/>), because the orb is never strictly standing still.
    /// </summary>
    /// <param name="preemptedBy">Null when the brain released the request itself; otherwise the owner
    /// that took the body (downing, recovery flight), so the interrupted attempt is scored as
    /// pre-empted rather than cancelled.</param>
    public Controls Hold(OrbState live, string? preemptedBy = null)
    {
        holdAnchor = null;
        Produced(Producer.None);
        Navigator.Interrupt(live, preemptedBy == null ? AttemptEnding.Cancelled : AttemptEnding.Preempted, preemptedBy ?? "released");
        return Controls.None;
    }

    /// <summary>
    /// The brain's hold: release whatever route was held and drift around the place the hold began. The
    /// place is taken once, when the hold starts, so a body still carrying momentum glides back to where it
    /// was told to stay rather than anchoring wherever the momentum has taken it by now.
    /// </summary>
    public Controls HoverHere(OrbState live)
    {
        Produced(Producer.Hover);
        Navigator.Interrupt(live, AttemptEnding.Cancelled, "released");
        return Navigator.Hover.Around(live, HoldAnchor(live), MovementQueries.World);
    }

    /// <summary>
    /// Keeping the player company from inside his region: whatever route was held is released, because inside the region
    /// there is no place to go, and the body moves about the box the region is — its centre, half-size and lead — by
    /// <see cref="HoverAroundSpot.Across"/>. Plain numbers and a refusal test rather than the region itself, so the
    /// movement core keeps no reference to the senses it is fed by.
    /// </summary>
    public Controls Accompany(OrbState live, Vector2 centre, Vector2 halfSize, Vector2 lead, Func<Vector2, bool> refused)
    {
        holdAnchor = null;
        Produced(Producer.Accompany);
        Navigator.Interrupt(live, AttemptEnding.Completed, "accompanying");
        return Navigator.Hover.Across(live, centre, halfSize, lead, refused, MovementQueries.World);
    }

    /// <summary>A missing chosen place does not cancel a travel intention: aim at the anchor itself until <paramref name="arrived"/> says the body is there, and hover once it is.</summary>
    public Controls SeekDestination(OrbState live, Vector2 anchor, Func<Vector2, bool> arrived)
    {
        if (arrived(live.Centre))
        {
            Produced(Producer.Hover);
            Navigator.Interrupt(live, AttemptEnding.Completed, "objective-satisfied");
            // Anchored once, where the objective was first met: an anchor taken from the body every tick moves with the
            // body, and the drift around it becomes a slow wander away from the place it arrived.
            return Navigator.Hover.Around(live, HoldAnchor(live), MovementQueries.World);
        }
        holdAnchor = null;
        Produced(Producer.Navigator);
        return Navigator.MoveTo(live, anchor);
    }

    /// <summary>
    /// Safety on top of the job: the tick's controls, bent away from a predicted hit when the job's own flight would
    /// meet one. <paramref name="bent"/> says whether they were, so the record can name the tick, and
    /// <see cref="LastEvade"/> says why.
    /// </summary>
    public Controls Evade(OrbState live, Controls wanted, Func<OrbState, int, bool>? unsafeAtTick, out bool bent)
    {
        if (unsafeAtTick == null)
        {
            LastEvade = EvadeVerdict.Off;
            retreat = 0f;
            spent = false;
            bent = false;
            return wanted;
        }
        Controls controls = EvadeWhileMoving.Bend(live, wanted, unsafeAtTick, MovementQueries.World, out EvadeVerdict verdict, ForecastJob());
        // A bend is a dodge only while it leaves the job somewhere to go. The lookahead is all the layer can see, and a body fleeing
        // ahead of a threat that keeps coming reads clear for every one of those ticks however far it flees, so on the tick it
        // decides, a corridor's retreat and a turn from a single passing shot look the same and the stateless layer cannot tell
        // them apart. What tells them apart is where the bends have taken the body: its net retreat along the way the job asks,
        // counted on every tick the layer runs and paid back by every tick the body makes way again. Once that exceeds a lookahead
        // of full-speed flight, the bends are carrying the body away from the job further than any single dodge needs to, which is
        // postponing the hit rather than avoiding it, so the job takes the body back and keeps it until the lost way is made up.
        // A body dodging about a place it holds retreats nothing net and is never spent, however long the threat stays.
        //
        // The retreat is not forgotten on a tick whose flight reads clear. The first build forgot it there and a corridor body
        // never arrived: fleeing at full speed ahead of a slower shot, the job's forecast from that momentum flees too and reads
        // clear, so a clear tick fell between every two bends and the retreat never summed past one dodge.
        // Counted only from a bend on, so ordinary steering lag — a body carrying momentum through a route's turn — owes nothing.
        if ((verdict.Bent || retreat > 0f) && wanted.Desired.LengthSquared() > 0.01f)
            retreat =MathF.Max(0f, retreat - Vector2.Dot(live.Velocity, Vector2.Normalize(wanted.Desired)));
        if (retreat > Selection.Weights.DodgeLookaheadTicks * OrbPace.MaxSpeed) spent = true;
        else if (retreat <= 0f) spent = false;
        if (spent && verdict.Bent)
        {
            verdict = verdict with { Reason = EvadeReason.Spent, Choice = EvadeChoice.Job };
            controls = wanted;
        }
        LastEvade = verdict;
        bent = verdict.Bent;
        return controls;
    }

    // How far, net, the evade layer's bends have carried the body back against the job's asked way and not yet made up, and
    // whether that retreat has been judged postponement. A tick with no predicate or a different kind of request ends both.
    private float retreat;
    private bool spent;

    /// <summary>The evade layer's verdict on the last tick that produced controls, or <see cref="EvadeVerdict.Off"/> when it did not run.</summary>
    public EvadeVerdict LastEvade { get; private set; } = EvadeVerdict.Off;

    // Which request produced this tick's controls, so the evade layer's keep test flies the same steering. Every request
    // method sets it and clears the last verdict, so a tick whose owner never reaches Evade — a downed body, recovery
    // flight — records no verdict from an earlier tick.
    private enum Producer { None, Navigator, Hover, Accompany }
    private Producer producer;

    private void Produced(Producer by)
    {
        // A request of a different kind from the last tick's starts the hover and the walk afresh from the body. Every brain tick
        // reaches exactly one request method here, so a change of kind is exactly a tick on which the previous motion was not
        // driven, and any state that motion kept describes somewhere the body no longer is. This is the one place that rule is
        // enforced, rather than a staleness test inside each piece of state, because the state that outlived its request was found
        // three times — a wait anchor, a per-goal memory under a drifting goal, and the accompanying walk's place in the box.
        // What it cannot see is a tick on which the brain did not run at all; the walk's own jump test covers a box that moved far.
        if (by != producer)
        {
            Navigator.Hover.Release();
            retreat = 0f;
            spent = false;
        }
        producer = by;
        LastEvade = EvadeVerdict.Off;
    }

    /// <summary>
    /// What the job this tick would ask for from a hypothetical state, for the evade layer to fly forward. It reads the
    /// navigator and the hover and changes neither: a route is steered with its own copy of the segment index, a direct line
    /// is the same eased ask toward the goal, a hover pursues the target it last chose, held still for the lookahead, and
    /// keeping the player company pursues its walking target carried on by the target's last motion each simulated tick.
    /// Null when nothing forecastable produced the tick, which the layer answers by holding this tick's controls.
    /// </summary>
    private Func<OrbState, Controls>? ForecastJob()
    {
        ITileWorld world = MovementQueries.World;
        if (producer == Producer.None) return null;
        if (producer == Producer.Accompany)
        {
            // The accompanying target moves at the region's pace, so it is carried on rather than held still: held still, the
            // forecast is a body slowing onto a point the real walk has already left, and a hit the walk is flying into reads
            // as a hit it stops short of. The closure advances once per simulated tick, which is how Bend flies it.
            Vector2 target = Navigator.Hover.LastTarget, motion = Navigator.Hover.LastTargetMotion;
            return state =>
            {
                target += motion;
                return new Controls(HoverAroundSpot.PursueAcross(state.Centre, target, motion, world));
            };
        }
        Navigator.Steering steering = producer == Producer.Hover ? Navigator.Steering.Hover : Navigator.LastSteering;
        switch (steering)
        {
            case Navigator.Steering.Route when Navigator.Path is Route route:
            {
                int index = route.Index;
                return state => SteerAlongRoute.Steer(state, route, ref index, OrbPace.MaxSpeed, OrbPace.SpeedChange, out _);
            }
            case Navigator.Steering.Direct when Navigator.Goal is Vector2 goal:
                return state =>
                {
                    float distance = Vector2.Distance(state.Centre, goal);
                    return distance < 1e-3f ? Controls.None : new Controls((goal - state.Centre) / distance * OrbPace.ArrivalSpeed(distance));
                };
            case Navigator.Steering.Hover:
            {
                Vector2 target = Navigator.Hover.LastTarget;
                return state => new Controls(HoverAroundSpot.Pursue(state.Centre, target, Vector2.Zero, world));
            }
            default:
                return null;
        }
    }

    private Vector2? holdAnchor;

    /// <summary>
    /// The place a hold drifts around: taken once, where the hold began, and replaced by the body's own centre whenever the body
    /// has no clear line to it. Every anchor in movement follows the same two rules — it dies with the request that made it, and it
    /// is never somewhere the body cannot fly straight to — because a hover pulled toward a place behind a wall is a body pinned
    /// still against that wall, which is the one thing the orb is ruled never to be.
    /// </summary>
    private Vector2 HoldAnchor(OrbState live)
    {
        if (holdAnchor is Vector2 held && !CircleContact.SweptClear(MovementQueries.World, live.Centre, held, OrbTerrain.Wall))
            holdAnchor = null;
        holdAnchor ??= live.Centre;
        return holdAnchor.Value;
    }

    public void SetObstacles(IEnumerable<Rectangle> obstacles) => Navigator.Avoid = new List<Rectangle>(obstacles);
}
