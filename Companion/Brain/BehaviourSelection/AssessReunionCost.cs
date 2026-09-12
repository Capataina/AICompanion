using System;

namespace AICompanion.Companion.Brain.BehaviourSelection;

/// <summary>Shared separation history and the marginal cost of delaying reunion.
/// No enemy count or companion self-danger is treated as a player-protection need.</summary>
public sealed class AssessReunionCost
{
    private ulong? lastTick;
    private bool previouslyApart;
    public int ApartTicks { get; private set; }
    public float DelayCostPerTick { get; private set; }
    public float Departure { get; private set; }

    public void Observe(ulong tick, bool together, bool playerDead)
    {
        if (lastTick == tick) return;
        if (playerDead || together || lastTick is { } prior && tick < prior) ApartTicks = 0;
        else if (previouslyApart && lastTick is { } previous && tick == previous + 1)
            ApartTicks = ApartTicks == int.MaxValue ? int.MaxValue : ApartTicks + 1;
        // Missing observations do not prove continued separation; retain accumulated
        // evidence without charging the unobserved interval.
        previouslyApart = !together && !playerDead;
        lastTick = tick;
    }

    public void Evaluate(float movingAway, float returnTicks, bool playerDead, bool stranded)
    {
        Departure = Math.Clamp(movingAway / SharedMovementSystem.BodyPhysics.WalkSpeed, 0, 1);
        DelayCostPerTick = playerDead || stranded ? 0
            : Departure * (1 + returnTicks / Weights.RegroupFreeReturnTicks) / Weights.ReunionDelayToleranceTicks
                + ApartTicks / (Weights.ReunionAbsenceScaleTicks * Weights.ReunionAbsenceScaleTicks);
    }
}
