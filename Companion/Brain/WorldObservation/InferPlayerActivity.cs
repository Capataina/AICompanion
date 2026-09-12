using System;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Brain.WorldObservation;

/// <summary>Bounded observed displacement distinguishes a journey from repeated local motion.
/// Confidence describes evidence for continued travel, never knowledge of a destination.</summary>
public sealed class InferPlayerActivity
{
    private readonly Vector2[] steps = new Vector2[Weights.PlayerIntentHistoryTicks];
    private readonly bool[] work = new bool[Weights.PlayerIntentHistoryTicks];
    private int next;
    private int count;
    private Vector2 previous;
    private ulong lastTick;
    private bool observed;

    public Vector2 Travel { get; private set; }
    public float Confidence { get; private set; }
    public float LocalWorkFraction { get; private set; }
    public int Samples => count;
    public string Interpretation { get; private set; } = "unobserved";

    public void Observe(Vector2 position, Vector2 velocity, bool localWork, bool dead, ulong tick)
    {
        if (observed && tick == lastTick) return;
        bool consecutive = observed && tick > lastTick && tick - lastTick == 1;
        Vector2 displacement = position - previous;
        // A missing interval or a position correction is not an observed traversed path.
        // Native movement can differ from velocity around collision, so allow a tile-sized
        // discrepancy rather than requiring exact equality with the engine's velocity.
        bool correction = displacement.Length() > velocity.Length() + Weights.PlayerIntentCorrectionSlack;
        if (!consecutive || dead || correction)
        {
            Array.Clear(steps);
            Array.Clear(work);
            next = count = 0;
            Travel = Vector2.Zero;
            Confidence = LocalWorkFraction = 0;
            Interpretation = dead ? "dead" : "unobserved";
        }
        else
        {
            steps[next] = displacement;
            work[next] = localWork;
            next = (next + 1) % steps.Length;
            count = Math.Min(count + 1, steps.Length);
            Vector2 net = Vector2.Zero;
            float distance = 0;
            int workSamples = 0;
            for (int i = 0; i < count; i++)
            {
                net += steps[i];
                distance += steps[i].Length();
                if (work[i]) workSamples++;
            }
            LocalWorkFraction = workSamples / (float)count;
            float coherence = distance > 0 ? Math.Clamp(net.Length() / distance, 0, 1) : 0;
            float support = Math.Min(1, count / (float)Weights.PlayerIntentEvidenceTicks);
            Confidence = coherence * support * (1 - LocalWorkFraction * Weights.PlayerIntentWorkDiscount);
            Travel = net / count * Confidence;
            Interpretation = Travel.LengthSquared() > Weights.PlayerIntentTravelSpeed * Weights.PlayerIntentTravelSpeed
                ? "travelling" : distance > 0 || workSamples > 0 ? "local-activity" : "paused";
        }
        observed = true;
        previous = position;
        lastTick = tick;
    }
}
