#nullable enable

using System;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

/// <summary>The cursor belongs to an input revision, not to a tick. An ordinary frame
/// advance cannot erase unfinished work; changed dependencies explicitly replace its epoch.</summary>
public sealed class DecisionWorkCursor
{
    public long Epoch { get; private set; } = -1;
    public long Offset { get; private set; }
    public long CompletedSweeps { get; private set; }
    public long Restarts { get; private set; }
    public bool Exhausted { get; private set; }
    public string RestartReason { get; private set; } = "not-started";

    public bool Bind(long epoch, string reason)
    {
        if (Epoch == epoch) return false;
        Epoch = epoch;
        Offset = 0;
        Exhausted = false;
        Restarts++;
        RestartReason = reason;
        return true;
    }

    public void Advance(long count = 1)
    {
        if (count < 0 || Exhausted) throw new InvalidOperationException("Cannot advance a completed or negative cursor.");
        Offset = checked(Offset + count);
    }

    public void Complete()
    {
        if (!Exhausted) CompletedSweeps++;
        Exhausted = true;
    }

    public void Rescan()
    {
        Offset = 0;
        Exhausted = false;
    }
}
