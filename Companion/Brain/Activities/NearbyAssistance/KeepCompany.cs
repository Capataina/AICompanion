#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>
/// Accompany the player's activity through reunion, resting or nearby movement.
/// These methods share one ordinary purpose rather than competing as separate jobs.
/// </summary>
public sealed class KeepCompany : CompanionAction
{
    public override string Name => "keep-company";
    public override PurposeFamily Family => PurposeFamily.NearbyAssistance;
    public override bool IsExcursion => false;

    private float preparedValue;
    private bool reunite;
    private bool walking;
    private int ticksLeft = 1;
    private Vector2 goal;

    public override void Enter(in ActionContext ctx)
    {
        ResetLocalMovement();
        // A freshly entered activity has no method in flight to protect, so its first preparation
        // adopts whatever the geometry asks for rather than holding the last run's method for a
        // rescore first.
        pendingTicks = Weights.PositionRescoreTicks;
    }

    /// <summary>A meeting place belongs to an ongoing reunion; leaving company must not leave its flood
    /// and reason standing as if another activity were still heading for the player.</summary>
    public override void Exit(in ActionContext ctx)
    {
        ctx.Companion.Brain.Meeting.Release();
        base.Exit(ctx);
    }

    public override void Prepare(in ActionContext ctx)
    {
        float reunionValue = CalculateReunionValue(ctx);
        float localValue = ctx.Senses.Player.IsDead ? 0f : ctx.Stranded ? Weights.StrandedWander : Weights.WanderFloor;
        reunite = ChooseMethod(ctx, reunionValue > localValue);
        preparedValue = MathF.Max(reunionValue, localValue);
        if (ctx.Senses.Player.IsDead) Classify(OfferEligibility.NoOpportunity, "player-dead");
        else Classify(OfferEligibility.Usable, reunite ? "reunion-method" : ctx.Stranded ? "sealed-pocket-local-method" : "local-company-method");
    }
    public override float Score() => preparedValue;

    /// <summary>Company is a way of being with the player rather than a job with a finish line, so
    /// its attempts claim execution and never completion.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
        => new(AttemptStatus.Executed, reunite ? "reunion-method-executed" : "local-company-method-executed");

    /// <summary>
    /// Whether the method changes, as opposed to whether it would. A method change is a change of
    /// request kind, and a change of request kind cancels whatever the body is doing — at tick 8128
    /// of the 2026-09-14 capture the flip from reunion to local turned a WithPlayer into a Hold, and
    /// the Hold reached the navigator one tick after take-off and cut the jump. So a regime that
    /// wants the other method has to hold for a rescore, and it has to be asked for from the ground:
    /// a body in the air is passing through, and nothing it is passing through is a reason to change
    /// what it is doing. Entering the new method costs that wait; there is no wait on the wanted
    /// regime going back to the one in force, because then nothing changes.
    /// </summary>
    private bool ChooseMethod(in ActionContext ctx, bool wantsReunion)
    {
        if (ctx.Senses.Player.IsDead) { pendingTicks = 0; return reunite = wantsReunion; }
        if (wantsReunion == reunite) { pendingTicks = 0; return reunite; }
        // The wait guards a body that is already where it is meant to be. Outside the region there
        // is nothing to protect — a companion that has lost the player, or has just spawned and is
        // still falling, has no business waiting a rescore per tick it stays in the air before it
        // is allowed to go after him — so a change takes effect at once there. Inside, and only
        // inside, the airborne tick is refused: that is where tick 8127's false arrival happened.
        if (!ctx.Senses.Intent.Region.Contains(ctx.Npc.Bottom)) { pendingTicks = 0; return reunite = wantsReunion; }
        // Standing in the tiles the player is asking for is the other case with nothing to protect:
        // the body is in his way now, and a wait measured in rescores is a wait he spends walking
        // into it. The wait exists to keep a move in flight from being cancelled, and a body being
        // asked to move is not a move in flight.
        if (ctx.Senses.Player.Interference is Rectangle asked
            && PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(asked))
        { pendingTicks = 0; return reunite = wantsReunion; }
        // The motor's own grounded test, on the same body: velocity.Y exactly zero.
        if (ctx.Npc.velocity.Y != 0f) { pendingTicks = 0; return reunite; }
        if (++pendingTicks < Weights.PositionRescoreTicks) return reunite;
        pendingTicks = 0;
        return reunite = wantsReunion;
    }

    /// <summary>Starts satisfied, because before the first preparation there is no method in flight to
    /// protect: an activity whose very first tick had to wait a rescore would hold a body that had not
    /// yet been told to do anything. Enter restores it for the same reason.</summary>
    private int pendingTicks = Weights.PositionRescoreTicks;

    /// <summary>
    /// How strongly the geometry alone asks for reunion, as one continuous curve from the region's
    /// centre outward. Pure and internal so the contract can be sampled either side of the region's
    /// edge directly, rather than inferred from a whole-brain walk that would also have to arrange
    /// sight, regrouping and a stranded body to see it.
    ///
    /// <para>Inside, the pull scales with the region's own normalised distance and reaches the
    /// central-pull weight at the edge. A flat zero inside was the whole of the never-overtakes
    /// defect: the box had no gradient, so a moving player was not itself a reason to move, the body
    /// coasted to whichever edge it entered by, and keeping company scored its wander floor where any
    /// rival offer beat it.</para>
    ///
    /// <para>Outside, the slope rises over the same span it always did, but it is measured on how far
    /// beyond the region's edge the body is rather than on how far it is from the player's body. That
    /// is what removes the step. Measured body-to-body the slope was already partway up at the
    /// region's own leading edge, because a companion standing exactly where the region asks is a
    /// lead plus a half-width from the player — so the curve jumped at the one boundary it had to be
    /// continuous across, and the size of the jump grew with the lead, which is to say with the
    /// screen. The two halves now meet at zero-beyond-the-edge, where the inside gradient is at its
    /// largest, so <c>max</c> of the two is continuous rather than a choice between two regimes; the
    /// ternary that used to pick between them is gone because there is nothing left to pick.</para>
    /// </summary>
    public static float GeometricPull(in FollowPlayerObjective objective, Vector2 feet, bool travelling)
    {
        float inner = MathF.Max(objective.HorizontalComfort, objective.VerticalComfort);
        float outer = MathF.Max(inner + 1f, PlayerIntegration.CompanionPreferences.Current.RecoveryRadius);
        float central = travelling ? Weights.IntentRegionCentralPull * MathF.Min(1f, objective.Pull(feet)) : 0f;
        float far = Consideration.Rising(objective.GapBeyond(feet), outer - inner);
        return MathF.Max(central, far);
    }

    private float CalculateReunionValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        var objective = ctx.Senses.Intent.Objective;
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Chooser.RegroupUrgency;
        bool seen = global::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight.Between(ctx.Npc, ctx.Player);
        bool blocking = p.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(footprint);
        float pull = GeometricPull(objective, ctx.Npc.Bottom, p.IsTravelling);
        if (!seen)
            pull = MathF.Max(pull, 0.3f);
        // Occupying a passage the player is walking is not "already with them": the slope from the
        // comfort box stays at zero while they overlap, and resting would park in the way. A still
        // player aiming a block is the other courtesy, and that one steps aside without a reunion.
        if (blocking && p.IsTravelling)
            pull = MathF.Max(pull, 0.3f);
        float demand = MathF.Min(Weights.KeepCompanyFarCap, MathF.Max(pull, regroup));
        return MathF.Max(demand, hardLeash) * stranded;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        var p = ctx.Senses.Player;
        if (p.IsDead) return PositionRequest.Hold;
        if (reunite)
        {
            ResetLocalMovement();
            // Reunion aims at a place on the player's apparent journey that the companion's own
            // routes reach, not at a point extrapolated from velocity; a paused or working player
            // is met where they stand.
            var meeting = ctx.Companion.Brain.Meeting;
            // The centre, because that is what the parameter is: the resolver roots its own free-space flood at
            // `NearestUsableCorner` of the point handed in. `Bottom` on this body is the centre plus a radius,
            // which for a companion resting one radius clear of a floor is the floor line itself — so the root
            // was chosen around a point on or inside terrain, the flood grew from wherever that landed, and it
            // finished having reached none of the tiles along the player's walk. A finished flood with no
            // candidate reached is reported as a proven absence, so the refusal read `no-reachable-meeting-place`
            // on open ground with the player walking straight at the companion, and reunion fell back to the
            // region's leading edge on every tick of the journey it exists to price.
            meeting.Resolve(ctx.Npc.Center, p, ctx.Senses.Intent.Region, Main.GameUpdateCount);
            return new PositionRequest(RequestKind.WithPlayer, meeting.Destination, MeetingPlace: meeting.HasPlace);
        }
        ctx.Companion.Brain.Meeting.Release();
        if (ctx.Stranded) return new PositionRequest(RequestKind.Roam, ctx.Npc.Bottom);
        // Courtesy. Resting on, or strolling onto, the tiles the player is building on or walking down hands the choice of spot
        // to ordinary follow selection near the player, which prices spots overlapping that footprint down. Only company yields:
        // work, protection and safety keep their spot, which is what pricing courtesy against them means, and a rest that
        // overlaps nothing is left exactly as it was.
        if (p.Interference is Rectangle footprint
            && (PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(footprint)
                || walking && PlayerSense.BodyTiles(goal, ctx.Npc.width, ctx.Npc.height).Intersects(footprint)))
            return new PositionRequest(RequestKind.WithPlayer, p.Bottom);
        // A held goal stays the goal until its time is up, unless it stops being a place worth standing: the player moved away
        // from it, or it became unsafe (an enemy's path now crosses it, the terrain changed under it). Re-rolling every tick is
        // what continuity rules out; keeping a goal that has turned dangerous is what this check rules out.
        if (walking && (!ctx.Senses.Intent.Region.Contains(goal) || !SafeStrollGoal(ctx, MovementQueries.Tile(goal))))
            ResetLocalMovement();
        if (--ticksLeft <= 0) PickLocalMovement(ctx);
        return walking ? PositionRequest.ExactAt(goal) : PositionRequest.Hold;
    }

    private void ResetLocalMovement()
    {
        walking = false;
        ticksLeft = 1;
    }

    /// <summary>
    /// Rest, or stroll to a goal chosen for being somewhere safe to be. The earlier method walked to the player's feet shifted
    /// sideways by a random offset, at the player's own height, so the goal could sit over a lava pool, at the bottom of a
    /// flooded pit or beyond a drop the body cannot climb back from, and it hopped in place at random to look busy. Resting is a
    /// real choice here, not a failure to find a goal, and a pick that finds no safe goal rests.
    /// </summary>
    private void PickLocalMovement(in ActionContext ctx)
    {
        ticksLeft = Main.rand.Next(60, 240);
        if (walking || Main.rand.NextBool(3) || StrollGoal(ctx) is not Vector2 chosen)
        {
            walking = false;
            return;
        }
        walking = true;
        goal = chosen;
    }

    /// <summary>
    /// A safe hoverable cell in the player's neighbourhood: every free cell across the calm band either side of the player, within a
    /// few rows of his feet and at least a short flight away, that passes <see cref="SafeStrollGoal"/>, then one of those at random,
    /// so strolls vary without any cell being preferred. The walker's version traced a walk along the floor because a stroll could
    /// not need a jump; an orb strolls to any free cell the reach flood holds, so the candidates are the cells themselves, and the
    /// flood is what keeps a goal on this side of anything the body cannot come back through.
    /// </summary>
    private Vector2? StrollGoal(in ActionContext ctx)
    {
        Point player = MovementQueries.FeetTile(ctx.Senses.Player.Bottom);
        Point body = MovementQueries.Tile(ctx.Npc.Center);
        int span = (int)(Weights.CalmBandFar * 0.7f / 16f);
        safe.Clear();
        for (int x = player.X - span; x <= player.X + span; x++)
            for (int y = player.Y - Weights.StrollRowsFromPlayer; y <= player.Y + Weights.StrollRowsFromPlayer; y++)
            {
                Point tile = new(x, y);
                // A stroll goal stays inside the player's intent region, because the region is what
                // says the companion is with him: a goal chosen outside it is unsatisfied the moment
                // it is reached, reunion outscores the stroll, and the method flips back and forth on
                // a player who has not moved. The calm band is wider than the region's comfort at
                // rest, so the band alone let the stroll manufacture the reunion it then answered.
                if (Math.Abs(x - body.X) >= Weights.StrollMinimumTiles
                    && ctx.Senses.Intent.Region.Contains(MovementQueries.HoverPoint(tile))
                    && SafeStrollGoal(ctx, tile))
                    safe.Add(tile);
            }
        return safe.Count == 0 ? null : MovementQueries.HoverPoint(safe[Main.rand.Next(safe.Count)]);
    }

    private readonly System.Collections.Generic.List<Point> safe = new();

    /// <summary>
    /// The body hovering at this cell touches no liquid that hurts it. The circle is wider than one tile can promise, so the test is
    /// the circle's own, not the cell's: a free cell beside a lava pool still puts part of the body over the lava.</summary>
    private static bool ClearOfLiquidHazards(Point tile)
    {
        ITileWorld world = MovementQueries.World;
        LiquidImmunity rules = OrbTerrain.Immunity;
        return !CircleContact.Touches(MovementQueries.HoverPoint(tile), (x, y) => OrbTerrain.WetWall(world, x, y, rules));
    }

    /// <summary>
    /// Whether hovering at this cell is a safe place to keep company: free for the body, inside the region the body can fly to and
    /// come home from, clear of liquid that hurts across the body's width, and with predicted enemy exposure within
    /// <see cref="Weights.StrollExposureLimit"/>. A held goal is checked again each tick, so an enemy arriving or a terrain edit
    /// under it ends the stroll.
    /// </summary>
    private static bool SafeStrollGoal(in ActionContext ctx, Point tile)
    {
        if (!MovementQueries.IsHoverable(tile) || !ClearOfLiquidHazards(tile)) return false;
        if (Infrastructure.Position.Positioner.PredictedExposureAt(MovementQueries.HoverPoint(tile), ctx.Senses) > Weights.StrollExposureLimit) return false;
        return ctx.Companion.Brain.Positioner.IsReturnable(ctx.Senses, tile);
    }
}
