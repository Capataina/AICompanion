#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

public enum MovementClass { Walker, Flyer, Phaser }

/// <summary>One hostile as the brain sees it. Rebuilt every tick; the slow parts (reachability, speed history) are carried over by index.</summary>
public sealed class ThreatRecord
{
    public NPC Npc = null!;
    public MovementClass Class;
    /// <summary>The head of a segmented body, or this NPC when it is not a segment.
    /// Danger, eligibility and proposal caps count one record per chain; pierce still reads every member.</summary>
    public int ChainHead;
    /// <summary>True on exactly one record per <see cref="ChainHead"/>: the head when it is in the list, else the loudest segment.</summary>
    public bool IsChainRepresentative = true;
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
    public float EffectiveTicksToPlayer;
    public float PredictionConfidence;
    public int PredictionSamples;
    public int ExpectedDamage;

    /// <summary>What one hit takes off the player and off the companion after each body's own defence,
    /// by <see cref="EstimateEffectiveDamage"/>: the median hit, never a guaranteed one.</summary>
    public float EffectiveDamageToPlayer;
    public float EffectiveDamageToCompanion;
    public bool HasSightOnPlayer;

    /// <summary>Whether it can see the companion, tested only where it can reach the companion and is close enough for
    /// the answer to matter; false wherever the test was not paid for, which reads the threat as half as urgent.</summary>
    public bool HasSightOnCompanion;

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
