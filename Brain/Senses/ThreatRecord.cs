#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Brain.Senses;

public enum MovementClass { Walker, Flyer, Phaser }

/// <summary>One hostile as the brain sees it. Rebuilt every tick; the slow parts (reachability, speed history) are carried over by index.</summary>
public sealed class ThreatRecord
{
    public NPC Npc = null!;
    public MovementClass Class;
    public bool Reachable = true;
    public bool Shoots;
    public bool IsBoss;

    /// <summary>Fastest speed seen recently, px/tick; the basis of time-to-player.</summary>
    public float ObservedSpeed = 1f;

    public float DistanceToPlayer;
    public float DistanceToCompanion;

    /// <summary>Ticks until it could touch the player at its observed speed; large when far or slow.</summary>
    public float TicksToPlayer;
    public bool HasSightOnPlayer;

    /// <summary>0..1: how much this threat endangers the player right now.</summary>
    public float Urgency;

    /// <summary>NPC gravity per tick and terminal fall speed, from NPC.UpdateNPC.</summary>
    private const float Gravity = 0.3f;
    private const float MaxFall = 10f;

    /// <summary>Where it will be: straight for flyers and phasers, under gravity for walkers.</summary>
    public Vector2 PredictedPosition(int ticks)
    {
        if (Class != MovementClass.Walker)
            return Npc.Center + Npc.velocity * ticks;
        Vector2 pos = Npc.Center;
        Vector2 vel = Npc.velocity;
        for (int i = 0; i < ticks; i++)
        {
            vel.Y = System.MathF.Min(vel.Y + Gravity, MaxFall);
            pos += vel;
        }
        return pos;
    }

    public Rectangle PredictedHitbox(int ticks)
    {
        Rectangle r = Npc.Hitbox;
        Vector2 lead = PredictedPosition(ticks) - Npc.Center;
        r.Offset((int)lead.X, (int)lead.Y);
        return r;
    }
}
