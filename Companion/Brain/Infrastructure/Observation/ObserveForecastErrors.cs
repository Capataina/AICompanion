#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>Residual coverage is keyed by the observed entity generation, model revision and native age; eviction is unknown.</summary>
public sealed class ObserveForecastErrors
{
    public readonly record struct Key(int EntitySlot, int EntityGeneration, long ModelRevision, int Age);
    public readonly record struct Summary(Vector2 Minimum, Vector2 Maximum, double SquaredError, int Samples, bool Covered);
    private readonly record struct Issued(Key Key, Vector2 Predicted, ulong Tick, long Episode);
    private const int Limit = 256;
    private readonly Queue<Issued> issued = new();
    private readonly Dictionary<Key, Summary> summaries = new();
    private readonly Dictionary<(int Slot, int Generation), long> episodes = new();

    public void Issue(Key key, Vector2 predicted, ulong tick)
    {
        long episode = episodes.TryGetValue((key.EntitySlot, key.EntityGeneration), out long value) ? value : 0;
        if (issued.Count == Limit) issued.Dequeue();
        issued.Enqueue(new Issued(key, predicted, tick, episode));
    }

    public bool Observe(int slot, int generation, long modelRevision, int age, Vector2 actual, ulong tick, Vector2 reachableMinimum, Vector2 reachableMaximum)
    {
        Key key = new(slot, generation, modelRevision, age);
        foreach (Issued candidate in issued)
        {
            if (candidate.Key != key || candidate.Tick + (ulong)age != tick) continue;
            Vector2 residual = actual - candidate.Predicted;
            bool discontinuity = actual.X < reachableMinimum.X || actual.X > reachableMaximum.X
                || actual.Y < reachableMinimum.Y || actual.Y > reachableMaximum.Y;
            if (discontinuity) episodes[(slot, generation)] = candidate.Episode + 1;
            Summary prior = summaries.TryGetValue(key, out Summary found) ? found : new(residual, residual, 0, 0, false);
            summaries[key] = new Summary(Vector2.Min(prior.Minimum, residual), Vector2.Max(prior.Maximum, residual),
                prior.SquaredError + residual.LengthSquared(), prior.Samples + 1, true);
            return discontinuity;
        }
        return false;
    }

    public Summary Get(Key key) => summaries.TryGetValue(key, out Summary found)
        ? found : new(Vector2.Zero, Vector2.Zero, 0, 0, false);

    public void Reset() { issued.Clear(); summaries.Clear(); episodes.Clear(); }
}
