#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A jump to any standable tile in the jump box that one of the jump profiles lands in when the
/// body's own jump is simulated tick by tick against the shapes; the edge carries the profile
/// (the velocity scale and the start speed) and the flight time. Performing it is making exactly
/// that jump: settling onto the take-off for a standing jump, backing away along the row for the
/// run-up a running jump needs and running in at the profile's own speed, jumping as the take-off
/// is crossed, and steering in the air with the rule the simulation steered with.
/// </summary>
public sealed class JumpTraversal : Traversal
{
    public override MoveKind Kind => MoveKind.Jump;

    /// <summary>The longest flight the planner follows before giving up on a landing.</summary>
    public const int MaxJumpTicks = 120;

    /// <summary>The full jump's own reach, which is the tallest climb the body has today.</summary>
    public override int ClimbTiles => NavGrid.JumpHeightTiles;

    public override IEnumerable<NavEdge> Candidates(NavNode node, BodyPhysics.Pose? here, bool lava)
    {
        foreach (var work in CandidateWork(node, here, lava))
            if (work is NavEdge edge) yield return edge;
    }

    public override IEnumerable<NavEdge?> CandidateWork(NavNode node, BodyPhysics.Pose? here, bool lava)
    {
        Point t = node.Tile;
        // A jump straight up onto the tile above needs a platform to pass through, which Fits
        // allows and a block refuses.
        if (here is not BodyPhysics.Pose fromPose || NavGrid.IsBlock(t.X, t.Y - NavGrid.BodyHeightTiles))
            yield break;
        // The take-off each running profile actually reaches out of this node, found once per
        // direction and speed rather than per landing tile, because the run-up branch of Steer
        // reads the take-off tile, the profile speed and the body, and never the landing.
        // Keyed by the profile's nominal speed, which is what the search is asked for, never by the
        // speed it turns out to deliver: the nominal is the question and the launch is the answer,
        // and naming this field after the step's old one is how the two get confused again.
        var takeOffs = new Dictionary<(int Direction, float NominalVx), Launch?>();
        float gravity = BodyMotion.GravityAt(NavGrid.World, BodyState.Standing(fromPose));
        for (int dx = -NavGrid.JumpGapTiles; dx <= NavGrid.JumpGapTiles; dx++)
        {
            // A jump that lands level or lower is only worth flying from an edge: with the next
            // tile in that direction standable, the walk reaches everything a level jump would at
            // a lower price, and the edge node past the walk offers the gap jump itself. This is
            // what keeps a flat floor from simulating sixteen jumps per node for nothing.
            bool edgeThisWay = dx != 0 && !NavGrid.IsStandable(t.X + Math.Sign(dx), t.Y, lava);
            for (int ny = t.Y - NavGrid.JumpHeightTiles; ny <= t.Y + 2; ny++)
            {
                int nx = t.X + dx;
                if ((dx == 0 && ny >= t.Y) || (ny >= t.Y && !edgeThisWay))
                    continue;
                if (NavGrid.StandAt(nx, ny, lava) is not BodyPhysics.Pose targetPose)
                    continue;
                var target = new Point(nx, ny);
                // The rise in pixels between the two poses' bottoms, rounded up to tiles, which
                // differs from the row difference on a slope or a half block by a whole scale step.
                int rise = (int)Math.Ceiling((fromPose.Bottom - targetPose.Bottom) / 16f);
                // Every profile that could land, lowest arc and fastest start first, and the first
                // that does is the edge.
                foreach ((float scale, float startVx) in JumpProfiles(rise, Math.Sign(dx), gravity))
                {
                    yield return null;
                    // The arc is proven from the take-off the performer actually reaches, not from
                    // the node's resting pose at the profile's nominal speed. A running profile is
                    // entered by backing away to a mark and running in, and how fast that run-up
                    // leaves the take-off is a fact about the floor behind it; simulating the arc
                    // at a speed the floor cannot deliver proves a jump the body never makes.
                    BodyPhysics.Pose launch = fromPose;
                    float launchVx = 0f, back = 0f, alongAtLaunch = 0f, launchRise = 0f;
                    if (startVx != 0f)
                    {
                        var key = (Math.Sign(dx), startVx);
                        if (!takeOffs.TryGetValue(key, out Launch? reached))
                            takeOffs[key] = reached = TakeOff(NavGrid.World, t, fromPose, new Point(nx, ny), scale, startVx);
                        // No take-off at all: the run-up never settles on its mark, never crosses
                        // back over the tile, or never agrees with its own throttle, so this
                        // profile does not exist from here.
                        if (reached is not Launch entry)
                            continue;
                        launch = entry.Entry.Pose;
                        launchVx = entry.Entry.Vx;
                        back = entry.Back;
                        alongAtLaunch = entry.Along;
                        // Measured against the take-off tile's own canonical stand rather than
                        // against the pose this expansion happened to start from, because the
                        // live navigator expands its first node from the body's actual pose and
                        // the performer can only ever ask the grid. A rise recorded against one
                        // reference and read against another is a band no body can satisfy.
                        launchRise = entry.Entry.Bottom - ReferenceBottom(t, lava, fromPose.Bottom);
                    }
                    if (BodyPhysics.SimulateJump(NavGrid.World, launch, scale, launchVx, nx, ny, MaxJumpTicks, out int flight) is not BodyPhysics.Pose landing)
                        continue;
                    var landed = new Point((int)Math.Floor(landing.CentreX / 16f), BodyPhysics.FeetRow(landing.Bottom));
                    if (landed != target)
                        continue;
                    // The edge records the state the arc was flown from and nothing else. The
                    // profile's nominal speed dies here on purpose: it was an input to finding the
                    // take-off, never a description of one, and every reader that inherited it
                    // meant a body the floor behind this tile cannot produce.
                    yield return new NavEdge(new NavStep(target, MoveKind.Jump, t, scale, launchVx, Ticks: flight, RunUpBack: back, LaunchAlong: alongAtLaunch, LaunchRise: launchRise), MovementCost(new NavStep(target, MoveKind.Jump, t, Ticks: flight)), 0, true);
                    break;
                }
            }
        }
    }

    /// <summary>The take-off a run-up reaches: the body on the tick it asks for the jump, how far behind the take-off that run-up started, and where its pose sat relative to the node's — along the jump's direction and in pixels below it.</summary>
    internal readonly record struct Launch(BodyState Entry, float Back, float Along, float Rise);

    /// <summary>
    /// The state the performer is in on the tick it asks for the jump, found by driving this
    /// traversal's own <see cref="Steer"/> from rest at the node's pose through the body's tick
    /// rule until it returns a jump. Null when no take-off is reached inside
    /// <see cref="RunUpTicks"/>, when the run-up leaves the ground or wedges in a shape, or when
    /// the throttle and the speed it produces never agree.
    ///
    /// This is the proof's answer to a question the profile table cannot answer: a running
    /// profile names a speed, and whether the body ever carries that speed over the take-off
    /// depends on how much floor sits behind it and where the wall behind that floor stops a
    /// twenty-pixel-wide body. Running the performer is the only way to know, because a formula
    /// over the runway length is a second rule and the two would drift, which is the defect this
    /// whole class exists to prevent.
    ///
    /// It iterates because the step the performer is handed carries the *proven* launch speed and
    /// nothing else, and that speed is also the run-up's throttle, so the answer is an input to
    /// the question. One pass at the profile's nominal speed says what the floor delivers; the
    /// next asks the same floor for exactly that, which is the run-up the performer will make.
    /// The two agree in one step wherever the body's horizontal rule is
    /// <see cref="BodyPhysics.StepVelocity"/> alone — it accelerates by a fixed step and clamps at
    /// the target, so throttling at the speed a full-throttle ramp reached climbs the identical
    /// ramp — and the loop exists for the cases where it is not alone, a run-up that meets a shape
    /// or crosses liquid. A profile that has not converged in <see cref="TakeOffPasses"/> is
    /// refused rather than offered at whichever pass ran last, because an edge nobody can
    /// reproduce is the defect, not a rounding error.
    ///
    /// The run-up branch of <see cref="Steer"/> reads the take-off tile, the proven speed and
    /// the body alone, never the landing, which is what lets one result serve every landing tile
    /// in a direction; <c>VerifyMovementContracts</c> asserts that independence so the caching
    /// cannot quietly become a lie.
    /// </summary>
    internal static Launch? TakeOff(ITileWorld world, Point from, BodyPhysics.Pose pose, Point toward, float scale, float nominalVx)
    {
        int jd = MathF.Sign(nominalVx);
        // The mark is sized once, from the profile the planner asked for, and then carried through
        // every pass and onto the step. Letting it move with the throttle would put it in the
        // fixed point too, and the run-in distance is the one thing that must not change between
        // the pass that proves the arc and the performance that repeats it.
        float back = Runway(from, MathF.Abs(nominalVx), jd);
        float throttle = nominalVx;
        for (int pass = 0; pass < TakeOffPasses; pass++)
        {
            if (RunUp(world, from, pose, toward, scale, throttle, back) is not Launch reached)
                return null;
            if (MathF.Abs(reached.Entry.Vx - throttle) <= SpeedSlack)
                return reached;
            throttle = reached.Entry.Vx;
        }
        return null;
    }

    /// <summary>One run-up: the performer driven from rest at the node's pose with a step carrying this throttle and this mark, to the tick it asks for the jump.</summary>
    private static Launch? RunUp(ITileWorld world, Point from, BodyPhysics.Pose pose, Point toward, float scale, float throttle, float back)
    {
        var performer = new JumpTraversal();
        // NaN is the "not proven yet" mark on the launch point, and it is what tells Steer to
        // commit where the body first crosses the take-off at whatever speed it has rather than
        // where a proof says it should. Gating discovery on the speed band would refuse every
        // profile whose floor cannot deliver the nominal — which is exactly the set this pass
        // exists to measure.
        var step = new NavStep(toward, MoveKind.Jump, from, scale, throttle, RunUpBack: back, LaunchAlong: float.NaN);
        performer.Begin(step);
        BodyState state = BodyState.Standing(pose);
        int jd = Direction(step);
        float takeoffX = NavGrid.FeetWorld(from).X;
        for (int tick = 1; tick <= RunUpTicks; tick++)
        {
            Controls controls = performer.Steer(state, step, null);
            if (controls.Jump)
                return new Launch(state, back, (state.CentreX - takeoffX) * jd, state.Bottom - pose.Bottom);
            state = BodyMotion.Step(world, state, controls);
            if (state.Stuck || !state.OnGround)
                return null;
        }
        return null;
    }

    /// <summary>
    /// How many run-ups a profile may need before its throttle and the speed that throttle
    /// produces agree. Two is the expected answer and the third is slack for a run-up whose
    /// velocity is not <see cref="BodyPhysics.StepVelocity"/> alone.
    /// </summary>
    private const int TakeOffPasses = 3;

    /// <summary>
    /// How long a run-up may take before the profile counts as unreachable. Backing away along a
    /// runway and running back in is the longest preparation the follower performs, and the
    /// allowance below prices it the same way: the speed divided by the acceleration, once each
    /// way, plus slack for the coast onto the mark.
    /// </summary>
    private const int RunUpTicks = 180;

    /// <summary>
    /// The jumps the body can start with for a rise of so many tiles, in the order the planner
    /// tries them: the fighter AI's hop heights adjusted to current gravity, plus the full
    /// jump, whose estimated apex clears the rise, lowest first, and for each the start speed at the walk, half of it and
    /// standing. Lowest first because the shortest flight is the cheapest edge and the arc
    /// least likely to meet a ceiling; the table alone over-jumped a four-tile rise onto a
    /// platform above it, and the walk alone hit a three-tile overhang the half-speed arc
    /// clears. A jump straight up has no run-up, so only the standing start is offered.
    /// </summary>
    public static IEnumerable<(float scale, float startVx)> JumpProfiles(int rise, int direction, float gravity = BodyPhysics.Gravity)
    {
        if (!float.IsFinite(gravity) || gravity <= 0f) yield break;
        float need = Math.Max(0, rise) * 16f;
        // Preserve the nominal hop heights under the current environment. The analytical
        // apex only proposes impulses; native simulation still decides whether they land.
        float adjustment = MathF.Sqrt(gravity / BodyPhysics.Gravity);
        float previous = 0f;
        for (int i = 0; i <= JumpScales.Length; i++)
        {
            float scale = i == JumpScales.Length ? 1f : MathF.Min(1f, JumpScales[i] * adjustment);
            if (scale <= previous) continue;
            previous = scale;
            float apex = BodyPhysics.JumpVelocity * scale;
            apex = apex * apex / (2f * gravity);
            if (apex < need)
                continue;
            if (direction == 0)
            {
                yield return (scale, 0f);
                continue;
            }
            yield return (scale, direction * BodyPhysics.WalkSpeed);
            yield return (scale, direction * BodyPhysics.WalkSpeed * 0.5f);
            yield return (scale, 0f);
        }
    }

    private static readonly float[] JumpScales =
    {
        BodyPhysics.JumpScaleForTiles(2), BodyPhysics.JumpScaleForTiles(3), BodyPhysics.JumpScaleForTiles(4), 1f,
    };

    /// <summary>
    /// A jump costs its flight time in walked tiles plus one, so a jump is taken only where the
    /// walk of the same width does not exist, and a long arc costs more than a short hop.
    /// </summary>

    // The jump edge the run-up state belongs to, and where the run-up stands: backing away from
    // the take-off, already run once (so a second arrival at the take-off jumps whatever the
    // speed, rather than backing away for ever on a runway too short for the profile), and
    // whether the jump itself has been made, which is what turns a landing elsewhere into a fault.
    private (Point From, Point Tile) runUpEdge;
    private bool backingOff;
    private bool ranUp;
    private bool jumped;
    private bool fell;

    /// <summary>Backing away to the runway mark, or running in from it and not yet in the air.</summary>
    public override bool MidMove => backingOff || (ranUp && !jumped);

    /// <summary>
    /// A jump entered is a jump judged: <see cref="Steer"/>, <see cref="Done"/> and
    /// <see cref="Check"/> all ignore the step that follows, so nothing after this move can turn
    /// an entry the macro proof refused into one it accepts, and the search may exclude the edge.
    /// The base class is conservative by default because a walk arrives at the speed its successor
    /// asks for; a jump takes off at a speed the floor behind it decides.
    /// </summary>
    public override bool EntryDependsOnNext => false;

    public override void Begin(NavStep step)
    {
        // Each attempt at the step starts with the jump not yet made; an airborne body sets it
        // again on its first steer, so a replan that returns the edge mid-flight loses nothing.
        jumped = false;
        fell = false;
        // The run-up state belongs to the edge, not the path index: a replan resets the index,
        // and a new path with a jump at the same index would otherwise inherit another jump's run-up.
        if ((step.From, step.Tile) == runUpEdge)
            return;
        runUpEdge = (step.From, step.Tile);
        backingOff = false;
        ranUp = false;
    }

    private readonly record struct ExecutionState((Point From, Point Tile) RunUpEdge, bool BackingOff, bool RanUp, bool Jumped, bool Fell);

    public override object CaptureExecutionState() => new ExecutionState(runUpEdge, backingOff, ranUp, jumped, fell);

    public override void RestoreExecutionState(object? state)
    {
        if (state is not ExecutionState saved)
            return;
        runUpEdge = saved.RunUpEdge;
        backingOff = saved.BackingOff;
        ranUp = saved.RanUp;
        jumped = saved.Jumped;
        fell = saved.Fell;
    }

    public override Controls Steer(BodyState live, NavStep step, NavStep? next)
    {
        Vector2 landing = NavGrid.FeetWorld(step.Tile);
        if (!live.OnGround)
        {
            // A rising body has jumped; a falling one may only have stepped off a kerb, unless
            // it falls faster than a kerb hop ever does, which is a body that left its take-off
            // without jumping and is judged where it lands like a body that jumped.
            if (live.Vy < 0f)
                jumped = true;
            fell |= DropTraversal.Falling(live);
            return new Controls(BodyPhysics.SteerToward(landing.X, live.CentreX, live.Vx));
        }

        Vector2 takeoff = NavGrid.FeetWorld(step.From);
        float need = step.LaunchVx;
        float vx = live.Vx;
        int jd = Direction(step);
        float along = Along(live, step, jd, takeoff);

        // A body already carrying the proven speed has nothing to gain from a run-up: it is
        // already in the state the run-up exists to reach. Sending it back to the mark anyway is
        // not merely wasteful, it is dangerous — reversing from the walk speed costs eighteen
        // ticks and about thirty pixels of forward coast, and a take-off is very often the lip of
        // the gap being jumped, so the body walks off the edge it was standing on and falls. That
        // is a physical limit rather than a hole to close here: no control sequence rescues a body
        // that close to a lip, which is why the walk before a jump now delivers the proven speed
        // and this exemption keeps it. The latch is deliberately not set for it, so a body knocked
        // out of the band a tick later still gets its run-up.
        bool speedInBand = !float.IsNaN(step.LaunchAlong) && MathF.Abs(vx - need) <= SpeedSlack;

        // The run-up, which is the whole of the entry and belongs to this traversal alone. A
        // second owner for it is what the local prefix search was, and it answered a different
        // question — "does some short control sequence make this move work" rather than
        // "reproduce the state the proof used" — so its answer was never the proof's.
        if (step.RunUpBack > 0f && jd != 0 && !ranUp && !speedInBand)
        {
            float mark = takeoff.X - jd * step.RunUpBack;
            // At the mark or further from the take-off than it, which is the side any extra floor
            // sits on. One-sided on purpose: running in from further back only means the throttle
            // clamp holds the body at the proven speed for longer, while starting nearer the
            // take-off is the one error that arrives under the proven speed.
            if ((mark - live.CentreX) * jd >= -MarkSlack && MathF.Abs(vx) < RestSpeed)
            {
                backingOff = false;
                ranUp = true;
                return new Controls(need);
            }
            // Coast onto the mark with the jump's own steering rule rather than walking through
            // it: a reversal from the walk speed takes about two tiles to stop, and when the
            // runway is capped by the floor the mark is the last standable tile behind.
            backingOff = true;
            return new Controls(BodyPhysics.SteerToward(mark, live.CentreX, vx));
        }

        // Running in. The commit is the first grounded tick at or past the take-off and nothing
        // else, which is the rule the proof committed on, because the proof *is* this code driven
        // from rest at the node's pose. Making the commit conditional on the proven state instead
        // moves it to a different tick from the proof's — the run-in crosses the take-off on one
        // tick and enters the band on another, and one acceleration step before the clamp is a
        // tick early — and two ticks apart is two launch states apart, which is a misland however
        // tight the band is. So the band is never the trigger; it is the guard, and it lives in
        // <see cref="Check"/>, where a body arriving in the wrong state ends the step instead of
        // flying an arc nobody proved.
        if (step.RunUpBack > 0f && jd != 0)
        {
            if (along < -CommitCross)
                return new Controls(need);
            return Jump(step, landing, live);
        }

        // No run-up in the proof: it launched from rest where the body stands. This is the case a
        // running profile with no floor behind its take-off lands in — the arc is a standing arc
        // filed under a running profile — and it is where the body used to jump the instant it
        // touched the take-off at whatever speed it had arrived with, which is how a body walking
        // on at 1.7 px/tick took off from a state proven at rest 390 times in one session without
        // ever leaving the ground. Now it settles onto the state first, and the allowance's
        // Timeout owns a body that never manages to.
        if (MathF.Abs(along) > MarkSlack || !InLaunchBand(live, step, jd, takeoff))
            return new Controls(BodyPhysics.SteerToward(takeoff.X, live.CentreX, vx));
        return Jump(step, landing, live);
    }

    /// <summary>How far past the take-off centre the body's centre sits, along the jump's heading; zero for a hop straight up, which has no along-axis.</summary>
    private static float Along(BodyState live, NavStep step, int jd, Vector2 takeoff)
        => jd == 0 ? 0f : (live.CentreX - takeoff.X) * jd;

    /// <summary>
    /// The body is in the state the arc was flown from, within the slack each part of that state
    /// is worth. A discovery pass has no proven launch to compare against and is always in band
    /// by construction: it is the run that decides what the band will be.
    /// </summary>
    private static bool InLaunchBand(BodyState live, NavStep step, int jd, Vector2 takeoff)
    {
        if (float.IsNaN(step.LaunchAlong))
            return true;
        if (MathF.Abs(live.Vx - step.LaunchVx) > SpeedSlack)
            return false;
        // The height the proof took off from, which a run-in over a kerb or up a slope changes and
        // the take-off tile cannot say. Without it a body that climbed differently on the way in
        // flies an arc starting a tile above or below the proven one and lands accordingly.
        if (MathF.Abs(live.Bottom - ReferenceBottom(step.From, AStar.AllowLava, live.Bottom) - step.LaunchRise) > PoseSlack)
            return false;
        float along = Along(live, step, jd, takeoff);
        // A launch with no run-up behind it was proven standing on the take-off, so the band round
        // it is symmetric: the body has to be where the proof stood, either side. Using the run-in
        // rule here instead leaves a body resting a couple of pixels short of the take-off outside
        // a band it can never re-enter — the steer that would close the gap has its own two-pixel
        // tolerance and stops inside it — so it pushes back and forth across the line until the
        // allowance runs out, which is a park rather than a jump.
        if (jd == 0 || step.RunUpBack <= 0f)
            return MathF.Abs(along - step.LaunchAlong) <= PoseSlack
                && MathF.Abs(live.CentreX - takeoff.X) <= MarkSlack;
        // A run-in commits on the first tick at or past the take-off, so its lower bound is that
        // crossing line and deliberately not the proven point less a slack: a bound below the line
        // admits a tick the proof's own rule refused — one acceleration step before the clamp,
        // which is a launch a tick early and a landing a tile out. The upper allowance is one tick
        // of travel, because the tick that crosses the line can overshoot the proof's point by the
        // distance the body covers in a tick, and that residue is irreducible at this granularity.
        return along >= -CommitCross
            && along <= step.LaunchAlong + PoseSlack + MathF.Abs(step.LaunchVx);
    }

    /// <summary>How far behind the take-off centre still counts as having crossed it, in pixels; the run-in is sampled once a tick and a tick of travel straddles the line.</summary>
    private const float CommitCross = 2f;

    /// <summary>The bottom a launch rise is measured from: the take-off tile's own stand, so the recorder and the reader use one reference whatever pose the expansion began at.</summary>
    private static float ReferenceBottom(Point tile, bool lava, float fallback)
        => NavGrid.StandAt(tile.X, tile.Y, lava)?.Bottom ?? fallback;

    /// <summary>The jump's heading: the sign of the sideways move it makes, and zero for a hop straight up, which has no run-up and no along-axis.</summary>
    private static int Direction(NavStep step) => Math.Sign(step.Tile.X - step.From.X);

    /// <summary>The jump tick: the impulse at the step's scale, steering toward the landing from where the body stands, as the simulation's first tick did.</summary>
    private Controls Jump(NavStep step, Vector2 landing, BodyState live)
    {
        jumped = true;
        return new Controls(BodyPhysics.SteerToward(landing.X, live.CentreX, live.Vx), Jump: true, JumpScale: step.JumpScale);
    }

    /// <summary>
    /// Landed and still there: on the ground, and either within the slack of the landing's feet
    /// point or covering the landing tile, because a jump is one flight and a body that has come
    /// down with the tile under it is where the flight ends; the next step's steering absorbs the
    /// rest. A body judged neither landed nor mislanded walked back to its take-off and flew again
    /// for ever, which is why covering the tile stays a way to finish.
    ///
    /// The ground test is what the covering branch was missing. Touching the landing tile while
    /// still airborne is arrival at a place the body is passing through, not arrival at a place it
    /// has reached, so the step completed and the route advanced while the companion was on its
    /// way past the ledge. Of the 37 jumps the 2026-09-11 census recorded as completed, eight had
    /// the body below its landing tile half a second later with the plan walking on regardless,
    /// and five of those were still in the air when sampled. That is the jump the player watched
    /// succeed and then slide off, and it is invisible to a completion count that never asks
    /// whether the body stayed.
    /// </summary>
    public override bool Done(BodyState live, NavStep step, NavStep? next)
        => base.Done(live, step, next) || ((jumped || fell) && live.OnGround && live.Covers(step.Tile));

    public override TraversalFault Check(BodyState live, NavStep step, int ticksOnStep)
    {
        if ((jumped || fell) && LandedElsewhere(live, step))
            return TraversalFault.Misland;
        // The run-up is finished, the body has reached the take-off, and it is not in the state
        // the arc was proven from. That is the third outcome this class exists to remove: the
        // body neither performs the move as proven nor fails, it takes off anyway on an arc
        // nobody simulated and lands where the plan never promised. Refusing it here ends the
        // step with a fault the navigator prices and strikes, so the next plan goes another way,
        // rather than leaving the macro proof to reject the same edge every tick for ever while
        // the body stands still. It is reported as a misland because that is what the arc from
        // this state does: the landing is wrong, and it is wrong before the body leaves the
        // ground, which is the cheapest moment to know it.
        // It fires only where Steer has nothing left to try. A step whose proof had no run-up is
        // still braking onto its launch and owns the Timeout instead; a body with a run-up it has
        // not made is still going to make it. What is left is a body that has run up, or arrived
        // already at the proven speed, and crossed the take-off anyway in the wrong state.
        int jd = Direction(step);
        Vector2 takeoff = NavGrid.FeetWorld(step.From);
        if (!jumped && !fell && live.OnGround && step.RunUpBack > 0f && jd != 0
            && (ranUp || MathF.Abs(live.Vx - step.LaunchVx) <= SpeedSlack)
            && Along(live, step, jd, takeoff) >= -CommitCross
            && !InLaunchBand(live, step, jd, takeoff))
            return TraversalFault.Misland;
        return base.Check(live, step, ticksOnStep);
    }

    /// <summary>
    /// A jump's proven ticks are its flight and nothing else, so the allowance has to add the
    /// preparation the follower does before it: a running profile backs away to its mark and runs
    /// in, and the motor needs the speed divided by its acceleration to reach that speed, once
    /// each way. Without this a full-speed jump whose back-off and run-in cost eighty ticks timed
    /// out against an allowance sized for a twelve-tick flight, which is what the cadence hold
    /// (MidMove) made reachable: before it, the replan cut the preparation short instead.
    /// </summary>
    protected override int Allowance(NavStep step)
        => base.Allowance(step)
           // The run-up is a round trip over the mark, so the back-off and the run-in are each
           // that distance, and the motor needs its ramp at each end. Priced from the distance
           // the step actually carries rather than from a profile speed, because a take-off with
           // no floor behind it makes no round trip and must not be given time for one.
           + (int)(4f * step.RunUpBack / BodyPhysics.WalkSpeed)
           + (int)(2f * MathF.Abs(step.LaunchVx) / BodyPhysics.Acceleration);

    /// <summary>
    /// How far behind a jump's take-off the body backs up along the take-off row, in pixels: the
    /// distance the motor needs to reach the profile's speed from rest, plus a tile to turn in,
    /// capped by the standable tiles actually there. Sized once, from the profile the planner
    /// asked for, and then carried on the step; the performer never recomputes it, because a
    /// distance derived twice is two rules for one move.
    /// </summary>
    private static float Runway(Point from, float nominalMagnitude, int direction)
        => MathF.Min(RunwayNeeded(nominalMagnitude) + 16f, RunwayPixels(from, -direction));

    /// <summary>
    /// How far from the proven launch speed a body may be and still be in the state the arc was
    /// flown from. It is a band rather than a floor: a body faster than the proof flies further
    /// than the proof, which mislands exactly as surely as one that is slower. It also decides
    /// when a take-off's throttle has agreed with the speed it produces, because those are the
    /// same question asked at the two ends of one move.
    /// </summary>
    private const float SpeedSlack = 0.4f;

    /// <summary>How far from the proven launch point the body's centre may be, in pixels; a few, because the arc's shape depends on where it starts as well as how fast.</summary>
    private const float PoseSlack = 3f;

    /// <summary>How near the runway mark counts as standing on it, in pixels.</summary>
    private const float MarkSlack = 6f;

    /// <summary>
    /// The distance the motor needs to reach a speed from rest, from its own acceleration: v²
    /// over twice the gain per tick. The parameter is a speed <em>magnitude</em> and never a
    /// signed velocity, which is why it is named one: a leftward profile handed in signed must
    /// ask for the same runway as its mirror, and once asked for 16 px where the mirror asked
    /// for 81.6 (Codex review of 7525a1b). A clamp to zero used to sit here for a caller that
    /// subtracted <see cref="SpeedSlack"/> before asking; that caller is gone, and the clamp
    /// went with it because it was the thing turning a signed speed into "no runway needed".
    /// </summary>
    private static float RunwayNeeded(float magnitude) => magnitude * magnitude / (2f * BodyPhysics.Acceleration);

    /// <summary>The standable floor behind a take-off along its row, in pixels, up to a few tiles; <paramref name="behind"/> is the direction away from the jump.</summary>
    private static float RunwayPixels(Point takeoff, int behind)
    {
        int tiles = 0;
        while (tiles < 8 && NavGrid.IsStandable(takeoff.X + behind * (tiles + 1), takeoff.Y))
            tiles++;
        return tiles * 16f;
    }
}
