#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.DecisionMatrix.Decision;
using AICompanion.Brain.DecisionMatrix.Navigation;
using AICompanion.Brain.DecisionMatrix.Senses;
using AICompanion.Companion;

namespace AICompanion.Brain.DecisionMatrix.Reflexes;

/// <summary>
/// The fast path that skips scoring: when a threat's predicted hitbox will meet the
/// companion within a few ticks, jump if that clears it, else step back if there is
/// room. A reflex takes the body for a few ticks, then rests for a refractory period
/// so an enemy that stays adjacent cannot hold the companion in an endless dodge
/// while the chooser never gets to shoot.
/// </summary>
public sealed class Reflexes
{
    private const int JumpHoldTicks = 8;
    private const int StepHoldTicks = 8;
    private const int RefractoryTicks = 45;

    private int holdTicks;
    private int refractory;
    private int stepDirection;

    public string? Active { get; private set; }

    /// <summary>Returns true when a reflex has the body this tick.</summary>
    public bool TryTake(NPC npc, CompanionMotor motor, Senses.Senses senses)
    {
        if (holdTicks > 0)
        {
            holdTicks--;
            if (Active == "step-back")
                motor.MoveX(stepDirection * CompanionMotor.WalkSpeed);
            if (holdTicks == 0)
                refractory = RefractoryTicks;
            return true;
        }
        Active = null;
        if (refractory > 0)
        {
            refractory--;
            return false;
        }

        Rectangle me = npc.Hitbox;
        foreach (ThreatRecord t in senses.Threats.Threats)
        {
            if (!t.Reachable || t.Npc.velocity.LengthSquared() < 1f)
                continue;
            if (!WillHit(t, me))
                continue;

            // Both dodges are simulated against the predicted hitbox before either is taken: the
            // first run jumped at everything, which lifts the body into a flyer at head height.
            int away = t.Npc.Center.X > npc.Center.X ? -1 : 1;
            bool jumpClears = motor.OnGround && !WillHitWhileJumping(t, me, npc);
            bool stepClears = !WillHitWhileStepping(t, me, away, npc);
            Point feet = NavGrid.FeetTile(npc.Bottom);
            bool room = NavGrid.IsBodyClear(feet.X + away, feet.Y) && NavGrid.IsBodyClear(feet.X + 2 * away, feet.Y);

            if (jumpClears && motor.Jump())
            {
                Active = "dodge-jump";
                holdTicks = JumpHoldTicks;
                return true;
            }
            if (stepClears && room)
            {
                stepDirection = away;
                Active = "step-back";
                holdTicks = StepHoldTicks;
                return true;
            }
            // Nothing clears it: take the hit rather than move into it, and do not re-check every tick.
            refractory = RefractoryTicks / 3;
            return false;
        }
        return false;
    }

    /// <summary>The threat's predicted hitbox meets the body standing still at some tick of the lookahead.</summary>
    private static bool WillHit(ThreatRecord t, Rectangle me)
    {
        for (int tick = 2; tick <= Weights.DodgeLookaheadTicks; tick += 2)
            if (t.PredictedHitbox(tick).Intersects(me))
                return true;
        return false;
    }

    private static bool WillHitWhileJumping(ThreatRecord t, Rectangle me, NPC npc)
    {
        for (int tick = 2; tick <= Weights.DodgeLookaheadTicks; tick += 2)
        {
            Rectangle body = me;
            body.Y += (int)CompanionMotor.JumpOffsetAt(tick);
            if (t.PredictedHitbox(tick).Intersects(body))
                return true;
        }
        return false;
    }

    private static bool WillHitWhileStepping(ThreatRecord t, Rectangle me, int away, NPC npc)
    {
        float v = npc.velocity.X;
        float x = 0f;
        for (int tick = 1; tick <= Weights.DodgeLookaheadTicks; tick++)
        {
            v = CompanionMotor.StepVelocity(v, away * CompanionMotor.WalkSpeed);
            x += v;
            if (tick % 2 != 0)
                continue;
            Rectangle body = me;
            body.X += (int)x;
            if (t.PredictedHitbox(tick).Intersects(body))
                return true;
        }
        return false;
    }
}
