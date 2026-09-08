#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Brain.Decision;
using AICompanion.Brain.Senses;
using AICompanion.Companion;

namespace AICompanion.Brain.Reflexes;

/// <summary>
/// The fast path that skips scoring: when a threat's predicted hitbox will meet the
/// companion within a few ticks and a jump clears it, jump; when something is in
/// melee reach, step back. A reflex takes the body for a few ticks and hands it back.
/// </summary>
public sealed class Reflexes
{
    private int holdTicks;
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
            return true;
        }
        Active = null;

        Rectangle me = npc.Hitbox;
        foreach (ThreatRecord t in senses.Threats.Threats)
        {
            if (!t.Reachable || t.Npc.velocity.LengthSquared() < 1f)
                continue;
            for (int tick = 4; tick <= Weights.DodgeLookaheadTicks; tick += 4)
            {
                if (!t.PredictedHitbox(tick).Intersects(me))
                    continue;
                // Incoming from the side at body height: a jump clears it. From above: step back.
                bool fromSide = System.MathF.Abs(t.Npc.velocity.Y) < System.MathF.Abs(t.Npc.velocity.X);
                if (fromSide && motor.Jump())
                {
                    Active = "dodge-jump";
                    holdTicks = 8;
                    return true;
                }
                stepDirection = t.Npc.Center.X > npc.Center.X ? -1 : 1;
                Active = "step-back";
                holdTicks = 10;
                return true;
            }
        }
        return false;
    }
}
