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

    /// <summary>
    /// Forget everything a hover or a walk kept about where it was going, so the next call starts from the body: the spot, the
    /// last target and its motion, and the walk's place in the box and the part of the box it had open. The movement boundary
    /// calls this whenever a tick's request is not the kind the tick before made, because every piece of this state describes a
    /// motion that request produced and none of it survives another request honestly — the walk's place in the box outlived a
    /// job and put the first accompanying target 434 px from the body, which the body then sprinted to.
    /// </summary>
    public void Release()
    {
        Anchor = null;
        previousTarget = null;
        acrossCentre = null;
        acrossGoal = null;
        acrossGoalWide = false;
        LastTargetMotion = Vector2.Zero;
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
    public Controls Across(OrbState live, Vector2 centre, Vector2 halfSize, Vector2 lead, float lowestLegY,
        Func<Vector2, bool> refused, ITileWorld world)
    {
        float inset = Navigator.SettleRadius;
        Vector2 room = new(MathF.Max(0f, halfSize.X - inset), MathF.Max(0f, halfSize.Y - inset));
        Vector2 wantMin = -room, wantMax = room;
        if (lead.X > Weights.AccompanyLeadPixels) wantMin.X = -room.X * Weights.AccompanyRearShare;
        else if (lead.X < -Weights.AccompanyLeadPixels) wantMax.X = room.X * Weights.AccompanyRearShare;
        // **The floor on the tour, in the box's own frame.** The region holds the player, so its lower part is
        // level with his legs and the floor he stands on, and a tour that draws its legs from the far edge of
        // the whole box therefore draws some of them at his shins — which is what the 22 September play was
        // reporting as "always too close to the floor". The caller passes the world y below which no leg may be
        // drawn; `CoordinateBrainTick` passes the top of his head, so the tour lives over him while the region
        // keeps holding him. It is a bound on the *part legs are drawn from*, never on the region, for a reason
        // recorded at the wide branch below and in this folder's guide: lifting the region itself was built and
        // reverted the same day, because the region's own membership is also combat's stand admission.
        float floorOffset = lowestLegY - centre.Y;
        wantMax.Y = MathF.Max(-room.Y, MathF.Min(room.Y, floorOffset));

        // The walk starts from the body whenever there is no walk to continue: released by another request, never begun, or a box
        // that jumped — a teleport, a respawn, a new region — so an offset describing somewhere else never places the target. It
        // starts from the body itself, unclamped, and the open part is widened to hold it: the inside latch counts a body up to the
        // settle radius beyond the edge as with the player, and a body behind a travelling player sits in the part the lead
        // closes, so clamping first put the target hundreds of pixels from the body it was about to be pursued by.
        float jump = OrbPace.MaxSpeed * Weights.AccompanyJumpTicks;
        if (acrossCentre is not Vector2 was || Vector2.DistanceSquared(was, centre) > jump * jump)
        {
            acrossOffset = live.Centre - centre;
            openMin = Vector2.Min(wantMin, acrossOffset);
            openMax = Vector2.Max(wantMax, acrossOffset);
            previousTarget = null;
            // A goal is a place in a box; a walk starting because the box jumped is in a different box,
            // so the old goal names somewhere the body is no longer near.
            acrossGoal = null;
            acrossGoalWide = false;
        }
        else
        {
            // The open part eases toward what the lead asks for at the walk's own pace, because an edge that snaps moves a target
            // standing in the part it closes by as much as the edge moved: with the rear closed at a fifth of the room, a player
            // turning round moved the target 200 px in one tick when his lead crossed the threshold.
            openMin = new Vector2(Toward(openMin.X, wantMin.X), Toward(openMin.Y, wantMin.Y));
            openMax = new Vector2(Toward(openMax.X, wantMax.X), Toward(openMax.Y, wantMax.Y));
        }
        float minX = openMin.X, maxX = openMax.X, minY = openMin.Y, maxY = openMax.Y;
        Vector2 InBox(Vector2 offset) => new(Math.Clamp(offset.X, minX, maxX), Math.Clamp(offset.Y, minY, maxY));
        acrossCentre = centre;
        Anchor = centre;

        // The target tours the box: it crosses to a place drawn from the half furthest from it, arrives,
        // and draws another. A random-walk heading was the instrument for two builds and it fails as a
        // class rather than a tuning, because it *diffuses*: the time to cover a box goes as the square
        // of its width in steps, so almost all of the path is spent re-covering ground. Both tunings are
        // that one fault seen from two sides — with a floor under the turning rate the heading closed
        // loops and six hundred idle ticks measured -0.03 to 0.81 of the room across the box, and with
        // the floor removed the same six hundred measured -0.04 to 0.36. Neither reached the left third,
        // and no jitter value does: the box is 428 px of open width and the walk moves 1.5 px a tick, so
        // six hundred ticks buy nine hundred pixels of path, two crossings if spent going somewhere and
        // nothing at all if spent wandering. A tour spends them going somewhere, and it is what README
        // asks for — "easing forward to its leading edge, letting itself fall back toward the middle,
        // rising over your head and dipping down again" is a sequence of places, not a direction.
        //
        // **While the box leads, the tour stays on the player's own side of it**, which is how covering
        // the whole space and "being level or ahead of you is the ordinary case" are both true rather
        // than traded off. The first tour drew from the open part's rear while he walked and put the
        // body more than three tiles behind him on 97 of 300 ticks; the rear share alone does not stop
        // that, because it closes a fraction of the room and the player sits inside what remains. His
        // own place in the box is the floor instead: he is carried at roughly minus the applied lead, so
        // a leg is never drawn behind that while the lead is live. With no lead there is no "ahead" to
        // be level with and the whole box is open, which is the idling case the coverage clause is
        // written for.
        // While the box leads, a leg runs between the leading edge and the middle; idle, it runs the
        // whole box. That is README's own split rather than a compromise between two of its sentences:
        // "easing forward to its leading edge, letting itself fall back toward **the middle**" is where
        // a travelling companion goes, and the coverage clause it sits inside is what an idling one
        // does, when there is no heading to be ahead of and the box is centred on the man.
        //
        // Drawn from the whole box while he walks, the tour led him on 57.9% of moving rows against the
        // two thirds `VerifyTheCompanionLeadsATravellingPlayer` requires — because the body lags its
        // target while the box advances underneath both, so legs aimed at the open part's rear finish
        // behind where they were aimed. Halving the part those legs are drawn from is what puts the
        // ordinary case ahead of him without pinning the body to one spot.
        float middle = (minX + maxX) / 2f;
        float back = lead.X > Weights.AccompanyLeadPixels ? middle : minX;
        float front = lead.X < -Weights.AccompanyLeadPixels ? middle : maxX;
        float ClearanceOf(Vector2 point) => ClearanceHeat.Combined(world, point, MovementQueries.Hazards);
        // Whether the draw below had to leave the open part to find somewhere the body can reach. It is read
        // straight after the call rather than returned beside the goal, because the goal is a `Vector2` every
        // other branch of this method also assigns and a tuple would have to be unpacked at each of them.
        bool wide = false;
        Vector2 Draw(Vector2 from)
        {
            wide = false;
            // The far *edge* rather than the far half, because README says "easing forward to its
            // leading edge, letting itself fall back toward the middle" — the extreme is the named
            // destination and the middle is what it passes through on the way back. Drawing from the far
            // half instead left each leg ending around the middle, and six hundred idle ticks then
            // reached 0.31 of the room against a third-line at 0.333: five pixels short of the far
            // third, after crossing the whole box. The share is what a leg aims past rather than a
            // distance, so it does not have to be retuned when the region grows.
            float FarEdge(float at, float low, float high)
            {
                if (high <= low) return low;
                float middle = (low + high) / 2f;
                float span = (high - low) * EdgeShare;
                (float a, float b) = at >= middle ? (low, low + span) : (high - span, high);
                return a + (float)random.NextDouble() * (b - a);
            }
            // **A leg the body has no straight way to is not a leg**, and the rear of the box is a preference
            // rather than a cage. The region is a box around the player and knows nothing about terrain, so a
            // wall, an overhang or a pillar can stand inside it and the far edge can be somewhere no walk
            // reaches; the turn machinery below then re-aims each blocked step along the clearest turn, which
            // beside a vertical face runs up and down it, and the body lives on the face. The 10:05 capture of
            // 22 September 2026 is two hundred ticks of exactly that, with `npc_px` x frozen at 55733 and y
            // sliding, at a clearance of 0.1 px, and it is what the owner called "always too close to the
            // floor… it never stayed up".
            //
            // So a draw is preferred when the circle can sweep to it from where the walk is now, and when no
            // draw at the far edge can be reached the leg is drawn from the *whole* box instead, taking the
            // furthest place the body can actually get to. The second stage is what the capture needed and the
            // first stage alone could not give it: with the box led east, the open part began at the body's own
            // column, so every place the walk was allowed to want was behind the pillar and widening the search
            // inside the open part would have found nothing either. The rear share exists to keep a travelling
            // player's companion level or ahead, which is a policy about where it would rather be; a body with
            // nowhere in the open part it can reach is not a case that policy was written for.
            Vector2 here = centre + acrossOffset;
            bool Reaches(Vector2 offset) => CircleContact.SweptClear(world, here, centre + offset, OrbTerrain.Wall);
            Vector2 best = default, reachable = default;
            float bestAir = float.MinValue, reachableAir = float.MinValue;
            for (int draw = 0; draw < GoalDraws; draw++)
            {
                Vector2 candidate = new(FarEdge(from.X, back, front), FarEdge(from.Y, minY, maxY));
                if (refused(centre + candidate)) continue;
                float air = ClearanceOf(centre + candidate);
                if (air > bestAir) { bestAir = air; best = candidate; }
                if (air > reachableAir && Reaches(candidate)) { reachableAir = air; reachable = candidate; }
            }
            if (reachableAir > float.MinValue) return reachable;

            // Nothing at the far edge the body has a way to. Sweep the whole box on a lattice — deterministic
            // rather than sampled, because a random handful in a pocket finds the one open corridor only
            // sometimes and this is the branch that has to work — and cross to the furthest reachable place,
            // ties going to the clearer one. It runs only when the first stage found nothing, which is the
            // pocket case and not the ordinary one.
            //
            // **The floor above does not bind this branch, and that is deliberate rather than an oversight.**
            // It sweeps the whole `room`, his shins included. The floor is a preference about where the tour
            // would rather live; getting out of a pocket is not a case that preference was written for, and in
            // the capture that forced this branch the only way out ran west and then *down* past his head
            // before turning back east under the rock. A floor that bound here would have re-sealed the pocket
            // the branch exists to escape, which is the same shape as the rear share being a preference rather
            // than a cage.
            wide = true;
            float far = -1f;
            for (int ix = 0; ix < WideLattice; ix++)
                for (int iy = 0; iy < WideLattice; iy++)
                {
                    Vector2 candidate = new(-room.X + (2f * room.X) * ix / (WideLattice - 1f),
                        -room.Y + (2f * room.Y) * iy / (WideLattice - 1f));
                    if (refused(centre + candidate)) continue;
                    float away = Vector2.DistanceSquared(candidate, acrossOffset);
                    float air = ClearanceOf(centre + candidate);
                    if (away < far || (away == far && air <= reachableAir) || !Reaches(candidate)) continue;
                    far = away;
                    reachableAir = air;
                    reachable = candidate;
                }
            if (far >= 0f) return reachable;

            // Nothing anywhere in the box the body has a straight way to: keep the answer this walk has always
            // given. Every draw refused is the player asking for the whole far edge; cross to it anyway and let
            // the per-step refusal turn the walk, rather than standing still.
            wide = false;
            return bestAir > float.MinValue ? best : new Vector2(FarEdge(from.X, back, front), FarEdge(from.Y, minY, maxY));
        }
        float reach = Weights.AccompanyWanderSpeedPx;
        // A goal drawn from the whole box is held against the whole box, or the check below would redraw it on
        // the very next tick for lying outside the part that could not reach it.
        float goalLowX = acrossGoalWide ? -room.X : back, goalHighX = acrossGoalWide ? room.X : front;
        float goalLowY = acrossGoalWide ? -room.Y : minY, goalHighY = acrossGoalWide ? room.Y : maxY;
        if (acrossGoal is not Vector2 goal || Vector2.DistanceSquared(acrossOffset, goal) <= reach * reach
            || goal.X < goalLowX || goal.X > goalHighX || goal.Y < goalLowY || goal.Y > goalHighY)
        {
            // A goal outside the part now open is re-drawn rather than clamped onto its edge: the part
            // closes behind a player who turns round, and a clamped goal would ride that closing edge,
            // which is the two-hundred-pixel jerk the easing of that edge exists to stop.
            acrossGoal = goal = Draw(acrossOffset);
            acrossGoalWide = wide;
            goalLowX = acrossGoalWide ? -room.X : back;
            goalHighX = acrossGoalWide ? room.X : front;
            goalLowY = acrossGoalWide ? -room.Y : minY;
            goalHighY = acrossGoalWide ? room.Y : maxY;
        }
        // While a wide goal is held the walk may use the whole box to get there. Widening the part rather than
        // exempting the target is what keeps one rule: `InBox` is still the only clamp, and the moment the leg
        // ends the part closes back to what the lead asks for.
        if (acrossGoalWide)
        {
            minX = MathF.Min(minX, -room.X); maxX = MathF.Max(maxX, room.X);
            minY = MathF.Min(minY, -room.Y); maxY = MathF.Max(maxY, room.Y);
        }
        Vector2 toGoal = goal - acrossOffset;
        // The vertical share flattens the step the way it flattened the wander: the box is three times
        // wider than it is tall, so without it the walk climbs and dives faster than it crosses.
        acrossHeading = MathF.Atan2(toGoal.Y, toGoal.X);
        Vector2 step = new(MathF.Cos(acrossHeading) * Weights.AccompanyWanderSpeedPx,
            MathF.Sin(acrossHeading) * Weights.AccompanyWanderSpeedPx * Weights.HoverVerticalShare);
        Vector2 next = InBox(acrossOffset + step);

        Vector2 from = previousTarget ?? live.Centre;
        bool Free(Vector2 point) => !refused(point) && CircleContact.SweptClear(world, from, point, OrbTerrain.Wall);
        Vector2 StepAt(float heading) => InBox(acrossOffset + new Vector2(MathF.Cos(heading),
            MathF.Sin(heading) * Weights.HoverVerticalShare) * Weights.AccompanyWanderSpeedPx);

        // The leg is followed while it is legal, and the turns are what a *blocked* step falls back on
        // rather than a rival to it.
        //
        // Clearance used to be scored against the straight step as well, and on a tour that is the
        // defect rather than the feature: in open air some turned step is almost always a little clearer
        // than straight on, so the walk turned on nearly every tick, dropped its leg each time and
        // diffused exactly as the random heading had — measured worse, in fact, at a longest still run
        // of 12 ticks against 2, because a reversing pair of turns holds the target about one point.
        // **A preference evaluated per step competes with a destination; the same preference evaluated
        // when the destination is chosen serves it.** So clearance moved into the draw above, where it
        // decides where the tour goes next, and the body still climbs off a floor and crosses away from
        // an enemy — it simply stops rewriting its heading to get there.
        Vector2 bestOffset = next;
        float bestHeading = acrossHeading;
        bool any = Free(centre + next);
        if (!any)
        {
            float bestClear = float.MinValue;
            foreach (float turn in Turns)
            {
                float heading = acrossHeading + turn;
                Vector2 attempt = StepAt(heading);
                if (!Free(centre + attempt)) continue;
                float clearance = ClearanceOf(centre + attempt);
                if (!any || clearance > bestClear)
                {
                    any = true;
                    bestClear = clearance;
                    bestOffset = attempt;
                    bestHeading = heading;
                }
            }
        }
        Vector2 target = centre + bestOffset;
        if (any)
        {
            // A turned step means the straight line to the goal was blocked or refused, so the leg is
            // re-aimed. Keeping the old goal would point every later step back at the same obstruction,
            // which is the tour's own version of the closed loop the random heading used to produce.
            //
            // **It is re-aimed along the turn rather than re-drawn from the far edge**, and the
            // difference is the whole courtesy behaviour. Re-drawing sends the next leg to whichever
            // edge is furthest from the *body*, which is frequently back across the tiles the player is
            // asking it to vacate: measured on the courtesy scene, a block aimed at the companion's own
            // tile took 53 ticks to clear it against 26 for an empty hand, when the block is supposed to
            // clear it sooner. Extending the turn to the box's edge keeps the body going the way the
            // refusal sent it, and the ordinary far-edge draw resumes once it arrives.
            if (bestHeading != acrossHeading)
            {
                Vector2 away = new(MathF.Cos(bestHeading), MathF.Sin(bestHeading) * Weights.HoverVerticalShare);
                float span = MathF.Max(goalHighX - goalLowX, goalHighY - goalLowY);
                // Clamped into whichever bounds this leg is held against, so a turn on a wide leg is not dragged
                // back into the open part the wide draw was made because the body could not reach.
                acrossGoal = new Vector2(Math.Clamp(bestOffset.X + away.X * span, goalLowX, goalHighX),
                    Math.Clamp(bestOffset.Y + away.Y * span, goalLowY, goalHighY));
            }
            acrossHeading = bestHeading;
            next = bestOffset;
        }
        else
        {
            // Nowhere a step away is allowed: the body is boxed in, or standing in the tiles the player is asking for. The target
            // then jumps to the nearest allowed place the body can fly straight to, looked for outward along both axes, because a
            // target left on the body asks for no motion and the body stops where it is — on the tile the player is building on,
            // or in a one-body-tall passage he is walking down. A step is a pixel and a half and a refused tile is sixteen, so
            // every turned step of a body standing in the footprint is refused too, and without this the body stays put.
            target = live.Centre;
            next = live.Centre - centre;
            // The escape puts the target somewhere the tour never drew, so the tour draws again from there.
            acrossGoal = null;
            acrossGoalWide = false;
            float reachOut = MathF.Max(room.X, room.Y) * 2f;
            // A place the escape may take is one no further outside the open part, on either axis, than the body already is. Exact
            // membership is the wrong test, because the inside latch lets a body sit beyond the open part: a body resting on a
            // passage floor below the inset box shares that overshoot with every place along the passage, and requiring
            // membership refused all of them, so the body stayed on the tile the player was walking into for sixty ticks.
            Vector2 body = live.Centre - centre;
            bool NoFurtherOut(Vector2 offset)
                => Beyond(offset.X, minX, maxX) <= Beyond(body.X, minX, maxX) + 1e-3f
                    && Beyond(offset.Y, minY, maxY) <= Beyond(body.Y, minY, maxY) + 1e-3f;
            for (float distance = EscapeStepPixels; distance <= reachOut && target == live.Centre; distance += EscapeStepPixels)
            {
                foreach (Vector2 axis in Axes)
                {
                    Vector2 point = live.Centre + axis * distance;
                    Vector2 offset = point - centre;
                    if (!NoFurtherOut(offset) || refused(point) || !CircleContact.SweptClear(world, live.Centre, point, OrbTerrain.Wall))
                        continue;
                    target = point;
                    next = offset;
                    break;
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
    /// <summary>Where the accompanying tour is crossing to, in the box's own frame. Null means draw one
    /// on the next call: at the start of a walk, on arrival, or when a blocked step turned it away.</summary>
    private Vector2? acrossGoal;

    /// <summary>Whether the held goal was drawn from the whole box because nothing in the part the lead leaves
    /// open could be reached. It survives between ticks with the goal it describes: the walk is allowed the
    /// whole box for as long as that leg lasts, and the part closes back to the lead's own share the moment it
    /// ends.</summary>
    private bool acrossGoalWide;

    /// <summary>How many places the tour draws from the far half before crossing to the clearest of them.
    /// Enough that a leg prefers open air over a floor or an enemy, few enough that the draw stays a bias
    /// rather than a search: a leg is a place to be, not a route to prove.</summary>
    private const int GoalDraws = 4;

    /// <summary>The side of the lattice the wide draw sweeps over the whole box when nothing at the far edge can
    /// be reached. A lattice rather than more random draws, because this is the branch that has to work: in a
    /// pocket the reachable places are a small part of the box and a random handful finds the one open corridor
    /// only sometimes, which would make the walk's escape depend on a seed. Five a side is 25 swept tests, paid
    /// once per leg and only on the legs where the ordinary draw found nothing.</summary>
    private const int WideLattice = 5;

    /// <summary>
    /// How much of the box, measured from its far edge, a tour leg is drawn from. A third keeps the
    /// destination near the edge the region actually has — "its leading edge", in README's words —
    /// while leaving enough spread that consecutive legs do not land on one point and read as a
    /// metronome. A half was tried first and ends each leg around the middle, which is a crossing that
    /// stops before it arrives anywhere.
    /// </summary>
    private const float EdgeShare = 1f / 3f;
    /// <summary>The part of the box, in its own frame, the walk may use this tick: eased toward what the lead asks for.</summary>
    private Vector2 openMin, openMax;

    /// <summary>How far a coordinate lies outside an interval, zero inside it.</summary>
    private static float Beyond(float value, float low, float high) => MathF.Max(0f, MathF.Max(low - value, value - high));

    /// <summary>One tick of an open-part edge moving toward where the lead wants it, no faster than the walk steps.</summary>
    private static float Toward(float from, float to)
        => from < to ? MathF.Min(to, from + Weights.AccompanyWanderSpeedPx) : MathF.Max(to, from - Weights.AccompanyWanderSpeedPx);

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
