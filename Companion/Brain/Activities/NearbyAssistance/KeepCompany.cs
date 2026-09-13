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

    public override void Enter(in ActionContext ctx) => ResetLocalMovement();

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
        reunite = reunionValue > localValue;
        preparedValue = MathF.Max(reunionValue, localValue);
        if (ctx.Senses.Player.IsDead) Classify(OfferEligibility.NoOpportunity, "player-dead");
        else Classify(OfferEligibility.Usable, reunite ? "reunion-method" : ctx.Stranded ? "sealed-pocket-local-method" : "local-company-method");
    }
    public override float Score() => preparedValue;

    /// <summary>Company is a way of being with the player rather than a job with a finish line, so
    /// its attempts claim execution and never completion.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
        => new(AttemptStatus.Executed, reunite ? "reunion-method-executed" : "local-company-method-executed");

    private float CalculateReunionValue(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsDead)
            return 0f;
        Vector2 ahead = ctx.Companion.Brain.Meeting.Anchor;
        if (ahead == Vector2.Zero) ahead = p.Bottom;
        var objective = new FollowPlayerObjective(p.Bottom, ahead);
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Chooser.RegroupUrgency;
        bool seen = global::AICompanion.Companion.Brain.Infrastructure.Observation.LineOfSight.Between(ctx.Npc, ctx.Player);
        bool blocking = p.Interference is Rectangle footprint
            && PlayerSense.BodyTiles(ctx.Npc.Bottom, ctx.Npc.width, ctx.Npc.height).Intersects(footprint);
        bool inside = !blocking && objective.IsSatisfied(ctx.Npc.Bottom, seen);
        float inner = MathF.Max(objective.HorizontalComfort, objective.VerticalComfort);
        float outer = MathF.Max(inner + 1f, PlayerIntegration.CompanionPreferences.Current.RecoveryRadius);
        float pull = inside ? 0f : Consideration.Rising(ctx.Senses.DistanceToPlayer - inner, outer - inner);
        if (!seen)
            pull = MathF.Max(pull, 0.3f);
        // Occupying a passage the player is walking is not "already with them": the slope from the
        // comfort box stays at zero while they overlap, and resting would park in the way. A still
        // player aiming a block is the other courtesy, and that one steps aside without a reunion.
        if (blocking && p.IsTravelling)
            pull = MathF.Max(pull, 0.3f);
        return MathF.Max(pull, MathF.Max(regroup, hardLeash)) * stranded;
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
            meeting.Resolve(ctx.Npc.Bottom, p, Main.GameUpdateCount);
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
        if (walking && (Vector2.DistanceSquared(goal, p.Bottom) > Weights.CalmBandFar * Weights.CalmBandFar
            || !SafeStrollGoal(ctx, MovementQueries.FeetTile(goal))))
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
    /// A safe standing tile in the player's neighbourhood: every tile the feet can walk to across the calm band either side of the
    /// player, within a few rows of the player's feet and at least a short walk away, that passes <see cref="SafeStrollGoal"/>, then one
    /// of those at random, so strolls vary without any tile being preferred. The walk is traced once each way from the feet, and a walk
    /// holds one standing tile per column, so the candidates are read along it rather than sampled from columns and rows. The earlier
    /// pick sampled a dozen random columns and read rows around the player's feet in each; on a floor rising one row per column only the
    /// few columns within those rows could hold a goal, most samples missed them, and an idle companion on a staircase rested for most of
    /// its window although goals existed. Reading along the walk accepts exactly the tiles that pick accepted, and costs one step per
    /// column plus one safety check per walked tile in the row window.
    /// </summary>
    private Vector2? StrollGoal(in ActionContext ctx)
    {
        Point player = MovementQueries.FeetTile(ctx.Senses.Player.Bottom);
        Point feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        int span = (int)(Weights.CalmBandFar * 0.7f / 16f);
        safe.Clear();
        // The feet tile is read from the body's bottom, and a body wider than a tile rests overhanging the higher neighbour on a
        // staircase, so it can name a row above the feet column's own floor; walking sideways from that row finds the next column
        // two rows down and stops before its first step. The walk starts where the body would settle in its own column.
        int start = WalkStep(feet.X, feet.Y) ?? feet.Y;
        foreach (int direction in stepsBothWays)
        {
            int y = start;
            for (int x = feet.X + direction; Math.Abs(x - player.X) <= span; x += direction)
            {
                if (WalkStep(x, y) is not int next) break;
                y = next;
                Point tile = new(x, y);
                if (Math.Abs(x - feet.X) >= Weights.StrollMinimumTiles && Math.Abs(y - player.Y) <= Weights.StrollRowsFromPlayer
                    && SafeStrollGoal(ctx, tile))
                    safe.Add(tile);
            }
        }
        return safe.Count == 0 ? null : MovementQueries.FeetWorld(safe[Main.rand.Next(safe.Count)]);
    }

    private static readonly int[] stepsBothWays = { -1, 1 };
    private readonly System.Collections.Generic.List<Point> safe = new();

    /// <summary>
    /// The standing row a walk reaches in <paramref name="column"/> from <paramref name="row"/> in the column before, or null where the walk
    /// cannot go on: a supported standing tile within one walk step, with no lava touching the body and the head out of liquid, upward
    /// steps tried first. A goal the returnable region holds can still lie beyond a gap, a pool or a drop that a route jumps, and a jump
    /// the native body does not land drops it into the hazard the goal was chosen to avoid: the first version of the stroll chose only
    /// safe goals and still put the body in lava and in the drop, because it chose them on the far side. A stroll is company, not a
    /// traversal, so it never needs a jump, and a goal is only ever a tile this walk reaches.
    /// </summary>
    private static int? WalkStep(int column, int row)
    {
        for (int dy = -StepRows; dy <= StepRows; dy++)
            if (MovementQueries.IsStandable(column, row + dy) && MovementQueries.IsSupport(column, row + dy + 1) && ClearOfLiquidHazards(new Point(column, row + dy)))
                return row + dy;
        return null;
    }

    /// <summary>
    /// The rows a walk changes between one column and the next: the one-tile step a walking body climbs or drops without a jump. It is
    /// the walk itself rather than a tunable, and both stroll rules read it so they cannot drift apart: <see cref="WalkStep"/> steps
    /// by it, and <see cref="NoDropBeside"/> calls anything deeper than it a drop. Widening it would make strolls climb what the body
    /// needs a jump for.
    /// </summary>
    private const int StepRows = 1;

    /// <summary>
    /// Floor under the tile's own column, and under each column beside it within one walk step of the tile's floor, so the goal is not the
    /// rim of a drop. <c>BodyPhysics.Stand</c> accepts a body overhanging an edge, so a tile beside a pit reads as standable, and the
    /// first version of this rule chose rim tiles as goals that a stroll arrives at walking speed and slides off. That version asked for
    /// floor on the same row under both neighbours, which cannot tell a one-row step from a twelve-row drop: on a floor rising one row per
    /// column, whether blocks or either slope style, every tile failed it, and the companion rested for the whole of an idle window with
    /// no goal in 400 picks. A neighbour one row up is a step the body walks onto, one row down is a step it walks off, and anything
    /// further down is the drop the rule exists for; the depth is the walk's own step, not a threshold fitted to a scene.
    /// </summary>
    private static bool NoDropBeside(Point tile)
        => MovementQueries.IsSupport(tile.X, tile.Y + 1) && FloorWithinStep(tile.X - 1, tile.Y) && FloorWithinStep(tile.X + 1, tile.Y);

    private static bool FloorWithinStep(int column, int feetRow)
    {
        for (int dy = -StepRows; dy <= StepRows; dy++)
            if (MovementQueries.IsSupport(column, feetRow + 1 + dy)) return true;
        return false;
    }

    /// <summary>No lava in any column the body can cover, from its head to the tile under its feet, and the head out of liquid in each.
    /// The body is wider than a tile, so a standable tile beside a pool still puts part of it over the lava; the columns either side are read.</summary>
    private static bool ClearOfLiquidHazards(Point tile)
    {
        int head = tile.Y - MovementQueries.BodyHeightTiles + 1;
        for (int x = tile.X - 1; x <= tile.X + 1; x++)
        {
            if (MovementQueries.IsLiquid(x, head)) return false;
            for (int y = head; y <= tile.Y + 1; y++)
                if (MovementQueries.IsLava(x, y)) return false;
        }
        return true;
    }

    /// <summary>
    /// Whether standing with the feet in this tile is a safe place to keep company: standable and not the rim of a drop (floor under
    /// its own column, and under each neighbour within a walk step), inside the region the body can walk to and come home from (never
    /// beyond a drop it cannot climb back), clear of lava and deep liquid across the body's width, and with predicted enemy exposure
    /// within <see cref="Weights.StrollExposureLimit"/>. A held goal is checked again each tick, so an enemy arriving or a terrain edit
    /// under it ends the stroll.
    /// </summary>
    private static bool SafeStrollGoal(in ActionContext ctx, Point tile)
    {
        if (!MovementQueries.IsStandable(tile.X, tile.Y) || !NoDropBeside(tile) || !ClearOfLiquidHazards(tile)) return false;
        if (Infrastructure.Position.Positioner.PredictedExposureAt(MovementQueries.FeetWorld(tile), ctx.Senses) > Weights.StrollExposureLimit) return false;
        return ctx.Companion.Brain.Positioner.IsReturnable(ctx.Senses, tile);
    }
}
