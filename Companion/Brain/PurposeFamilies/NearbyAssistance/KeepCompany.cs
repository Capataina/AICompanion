#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.PurposeFamilies.NearbyAssistance;

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
        Vector2 ahead = p.Predict(45);
        float gap = Vector2.Distance(ctx.Npc.Bottom, ahead);
        float hardLeash = Consideration.Step(ctx.Senses.DistanceToPlayer > Weights.LeashHard, 1f, 0f);
        // A proven sealed pocket changes the available method, not the companion's purpose.
        // The coordinator periodically retries reunion rather than forgetting it forever.
        float stranded = ctx.Stranded ? Weights.StrandedFollowDiscount : 1f;
        float regroup = ctx.Companion.Brain.Chooser.RegroupUrgency;
        var objective = new FollowPlayerObjective(p.Bottom, ahead);
        float horizontalRatio = objective.HorizontalGap(ctx.Npc.Bottom) / objective.HorizontalComfort;
        float verticalRatio = objective.VerticalGap(ctx.Npc.Bottom) / objective.VerticalComfort;
        float objectiveRatio = MathF.Max(horizontalRatio, verticalRatio);
        float objectiveMiss = objectiveRatio > 1f
            ? Consideration.AtLeast(Consideration.Rising(objectiveRatio - 1f, 3f), 0.3f)
            : 0f;
        if (!global::AICompanion.Companion.Brain.WorldObservation.LineOfSight.Between(ctx.Npc, ctx.Player))
            objectiveMiss = MathF.Max(objectiveMiss, 0.3f);
        if (p.IsTravelling)
            return MathF.Max(objectiveMiss, MathF.Max(regroup, MathF.Max(Consideration.AtLeast(Consideration.Rising(gap, Weights.FollowIntentDistance * 2f), 0.3f), hardLeash))) * stranded;

        // Standing player: only worth acting on when the companion has drifted well out of the
        // comfort region. Local occlusion still requires following even inside its axis limits.
        float drifted = Consideration.Rising(ctx.Senses.DistanceToPlayer - Weights.CalmBandFar, 400f) * 0.6f;
        return MathF.Max(objectiveMiss, MathF.Max(regroup, MathF.Max(drifted, hardLeash))) * stranded;
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
            Vector2 anchor = meeting.Resolve(ctx.Npc.Bottom, p, Main.GameUpdateCount);
            return new PositionRequest(RequestKind.WithPlayer, anchor, MeetingPlace: meeting.HasPlace);
        }
        ctx.Companion.Brain.Meeting.Release();
        if (ctx.Stranded) return new PositionRequest(RequestKind.Roam, ctx.Npc.Bottom);
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
    /// A safe standing tile in the player's neighbourhood: random columns across the calm band either side of the player, each
    /// read over a few rows around the player's feet, keeping the tiles that pass <see cref="SafeStrollGoal"/> and lie at least a
    /// short walk from the feet, then one of those at random. Sampled rather than scanned, so a pick costs a bounded read, and random
    /// among the safe ones, so strolls vary without any tile being preferred.
    /// </summary>
    private Vector2? StrollGoal(in ActionContext ctx)
    {
        Point player = MovementQueries.FeetTile(ctx.Senses.Player.Bottom);
        Point feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        int span = (int)(Weights.CalmBandFar * 0.7f / 16f);
        safe.Clear();
        for (int sample = 0; sample < Weights.StrollColumnSamples; sample++)
        {
            int x = player.X + Main.rand.Next(-span, span + 1);
            if (Math.Abs(x - feet.X) < Weights.StrollMinimumTiles) continue;
            for (int y = player.Y - Weights.StrollRowsFromPlayer; y <= player.Y + Weights.StrollRowsFromPlayer; y++)
            {
                Point tile = new(x, y);
                if (SafeStrollGoal(ctx, tile) && WalkableFrom(feet, tile))
                    safe.Add(tile);
            }
        }
        return safe.Count == 0 ? null : MovementQueries.FeetWorld(safe[Main.rand.Next(safe.Count)]);
    }

    private readonly System.Collections.Generic.List<Point> safe = new();

    /// <summary>
    /// Whether the goal is a walk from the feet: every column between them has a supported standing tile within one row of the column
    /// before, with no lava touching the body and the head out of liquid. A goal the returnable region holds can still lie beyond a gap,
    /// a pool or a drop that a route jumps, and a jump the native body does not land drops it into the hazard the goal was chosen to
    /// avoid: the first version of this method chose only safe goals and still put the body in lava and in the drop, because it chose
    /// them on the far side. A stroll is company, not a traversal, so it never needs a jump.
    /// </summary>
    private static bool WalkableFrom(Point feet, Point goal)
    {
        int step = Math.Sign(goal.X - feet.X), y = feet.Y;
        for (int x = feet.X + step; step != 0 && x != goal.X + step; x += step)
        {
            int next = int.MinValue;
            for (int dy = -1; dy <= 1 && next == int.MinValue; dy++)
                if (MovementQueries.IsStandable(x, y + dy) && MovementQueries.IsSupport(x, y + dy + 1) && ClearOfLiquidHazards(new Point(x, y + dy)))
                    next = y + dy;
            if (next == int.MinValue) return false;
            y = next;
        }
        return y == goal.Y;
    }

    /// <summary>
    /// Support under the tile's own column and both columns beside it. <c>BodyPhysics.Stand</c> accepts a body overhanging an edge, so a
    /// tile over a pit next to its wall reads as standable, and the first version of this method chose such tiles as goals; a stroll
    /// arrives there at walking speed and slides off.
    /// </summary>
    private static bool FloorBothSides(Point tile)
        => MovementQueries.IsSupport(tile.X - 1, tile.Y + 1) && MovementQueries.IsSupport(tile.X, tile.Y + 1) && MovementQueries.IsSupport(tile.X + 1, tile.Y + 1);

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
    /// Whether standing with the feet in this tile is a safe place to keep company: standable with floor under both neighbouring
    /// columns, inside the region the body can walk to and come home from (never beyond a drop it cannot climb back), clear of lava and
    /// deep liquid across the body's width, and with predicted enemy exposure within <see cref="Weights.StrollExposureLimit"/>. A held
    /// goal is checked again each tick, so an enemy arriving or a terrain edit under it ends the stroll.
    /// </summary>
    private static bool SafeStrollGoal(in ActionContext ctx, Point tile)
    {
        if (!MovementQueries.IsStandable(tile.X, tile.Y) || !FloorBothSides(tile) || !ClearOfLiquidHazards(tile)) return false;
        if (PositionSelection.Positioner.PredictedExposureAt(MovementQueries.FeetWorld(tile), ctx.Senses) > Weights.StrollExposureLimit) return false;
        return ctx.Companion.Brain.Positioner.IsReturnable(ctx.Senses, tile);
    }
}
