#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.Decision;
using AICompanion.Brain.Navigation;
using AICompanion.Brain.Senses;
using AICompanion.Companion;

namespace AICompanion.Brain.Reflexes;

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
            for (int tick = 4; tick <= Weights.DodgeLookaheadTicks; tick += 4)
            {
                if (!t.PredictedHitbox(tick).Intersects(me))
                    continue;

                // A jump clears anything that is not coming down onto the companion.
                bool fromAbove = t.Npc.velocity.Y > 1f && t.Npc.Center.Y < npc.Center.Y;
                if (!fromAbove && motor.Jump())
                {
                    Active = "dodge-jump";
                    holdTicks = JumpHoldTicks;
                    return true;
                }

                int away = t.Npc.Center.X > npc.Center.X ? -1 : 1;
                Point feet = NavGrid.FeetTile(npc.Bottom);
                bool room = NavGrid.IsBodyClear(feet.X + away, feet.Y) && NavGrid.IsBodyClear(feet.X + 2 * away, feet.Y);
                if (!room)
                {
                    refractory = RefractoryTicks / 3;
                    return false;
                }
                stepDirection = away;
                Active = "step-back";
                holdTicks = StepHoldTicks;
                return true;
            }
        }
        return false;
    }
}
