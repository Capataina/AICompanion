#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion;

namespace AICompanion.Brain.Navigation;

/// <summary>
/// Gets the companion to a world position. Plans with <see cref="AStar"/> when the goal
/// tile changes or the path goes stale, keeps the path between plans, and drives the
/// motor along it: walk toward the next step, jump when the step is a jump or the body
/// is blocked, let gravity handle drops. Falls back to straight-line walking when no
/// path exists, so a missing path degrades to the old behaviour rather than a freeze.
/// </summary>
public sealed class Navigator
{
    public const int PlanBudget = 1500;
    private const int ReplanInterval = 30;
    private const float ArriveDistance = 12f;

    public NavPath? Path { get; private set; }
    public Point? GoalTile { get; private set; }
    public bool LastPlanFailed { get; private set; }
    public int LastExpansions { get; private set; }

    private int ticksSincePlan = ReplanInterval;
    private int stuckTicks;
    private Vector2 lastPosition;

    /// <summary>Move toward <paramref name="targetFeet"/> this tick. Returns true when arrived.</summary>
    public bool MoveTo(NPC npc, CompanionMotor motor, Vector2 targetFeet)
    {
        if (Vector2.Distance(npc.Bottom, targetFeet) <= ArriveDistance)
        {
            motor.Stop();
            Path = null;
            return true;
        }

        Point start = NavGrid.FeetTile(npc.Bottom);
        Point? goal = NavGrid.NearestStandable(NavGrid.FeetTile(targetFeet), 3);
        ticksSincePlan++;

        bool goalMoved = goal != GoalTile;
        bool stale = Path == null || Path.Finished || ticksSincePlan >= ReplanInterval || stuckTicks > 40;
        if (goal != null && (goalMoved || stale))
        {
            Plan(start, goal.Value);
        }

        if (Path == null || Path.Finished)
        {
            WalkStraight(npc, motor, targetFeet);
            return false;
        }

        Follow(npc, motor);
        TrackStuck(npc);
        return false;
    }

    private void Plan(Point start, Point goal)
    {
        GoalTile = goal;
        ticksSincePlan = 0;
        stuckTicks = 0;
        Point? from = NavGrid.NearestStandable(start, 2);
        if (from == null)
        {
            Path = null;
            LastPlanFailed = true;
            return;
        }
        Path = AStar.Find(from.Value, goal, PlanBudget, out int used);
        LastExpansions = used;
        LastPlanFailed = Path == null;
    }

    private void Follow(NPC npc, CompanionMotor motor)
    {
        NavPath path = Path!;
        NavStep step = path.Current;
        Vector2 stepWorld = NavGrid.FeetWorld(step.Tile);

        // Advance past steps we have reached (feet within a tile of the step's feet point).
        while (!path.Finished && Vector2.Distance(npc.Bottom, NavGrid.FeetWorld(path.Current.Tile)) < 10f)
        {
            path.Index++;
        }
        if (path.Finished)
        {
            motor.Stop();
            return;
        }
        step = path.Current;
        stepWorld = NavGrid.FeetWorld(step.Tile);

        float dx = stepWorld.X - npc.Bottom.X;
        float dy = stepWorld.Y - npc.Bottom.Y; // negative = step is above
        int dir = MathF.Sign(dx) == 0 ? npc.direction : MathF.Sign(dx);

        switch (step.Kind)
        {
            case MoveKind.Jump:
                // Jump first so the arc starts from the current tile, then steer in the air.
                if (motor.OnGround)
                    motor.Jump();
                motor.MoveX(dir * CompanionMotor.WalkSpeed);
                break;
            case MoveKind.Drop:
                motor.MoveX(dir * CompanionMotor.WalkSpeed * 0.8f);
                break;
            default:
                motor.MoveX(dir * CompanionMotor.WalkSpeed);
                if (motor.OnGround && (npc.collideX || dy < -8f))
                    motor.Jump(dy < -40f ? 1f : 0.75f);
                break;
        }
    }

    private void WalkStraight(NPC npc, CompanionMotor motor, Vector2 target)
    {
        float dx = target.X - npc.Bottom.X;
        if (MathF.Abs(dx) < 4f)
        {
            motor.Stop();
            return;
        }
        motor.MoveX(MathF.Sign(dx) * CompanionMotor.WalkSpeed);
        bool targetAbove = target.Y - npc.Bottom.Y < -48f && MathF.Abs(dx) < 128f;
        if (motor.OnGround && (npc.collideX || targetAbove))
            motor.Jump();
    }

    private void TrackStuck(NPC npc)
    {
        if (Vector2.DistanceSquared(npc.position, lastPosition) < 1f)
            stuckTicks++;
        else
            stuckTicks = 0;
        lastPosition = npc.position;
    }

    public void Clear()
    {
        Path = null;
        GoalTile = null;
    }
}
