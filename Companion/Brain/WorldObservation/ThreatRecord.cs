#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.WorldObservation;

public enum MovementClass { Walker, Flyer, Phaser }

/// <summary>One hostile as the brain sees it. Rebuilt every tick; the slow parts (reachability, speed history) are carried over by index.</summary>
public sealed class ThreatRecord
{
    public NPC Npc = null!;
    public MovementClass Class;
    public bool CanReachPlayer = true;
    public bool CanReachCompanion = true;
    public bool CanReachEither => CanReachPlayer || CanReachCompanion;
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

    /// <summary>Ticks for this threat to reach the companion at its observed speed.</summary>
    public float TicksToCompanion;

    /// <summary>Urgency on the same scale as <see cref="Urgency"/>, reckoned about the companion.</summary>
    public float UrgencyToCompanion;

    /// <summary>The same observed-motion forecast used by aiming, constrained by terrain.</summary>
    public Vector2 PredictedPosition(int ticks) => PredictObservedMotion.Predict(Npc, ticks);

    public Rectangle PredictedHitbox(int ticks)
    {
        Rectangle r = Npc.Hitbox;
        Vector2 lead = PredictedPosition(ticks) - Npc.Center;
        r.Offset((int)lead.X, (int)lead.Y);
        return r;
    }
}
