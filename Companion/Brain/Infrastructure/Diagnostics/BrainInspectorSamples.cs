#nullable enable
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>Small, opt-in retained facts from computations the inspector must not rerun.</summary>
public static class BrainInspectorSamples
{
    public readonly record struct Reflex(ulong Tick, int UnsafeTick, OrbState Body);
    public readonly record struct Aim(ulong Tick, Vector2 Muzzle, Vector2 Target, string Weapon, Vector2? Launch, string Outcome);
    internal const int Capacity = 8;
    public readonly record struct Trace(ulong Tick, Vector2[] Points, bool Accepted, string Reason);
    public static readonly System.Collections.Generic.Queue<Trace> AimTraces = new();
    public static readonly System.Collections.Generic.Queue<Trace> MovementTraces = new();
    public static Reflex? LastReflex { get; private set; }
    public static Aim? LastAim { get; private set; }
    public static void RecordReflex(ulong tick, int unsafeTick, OrbState body)
    {
        if (!BrainOverlay.MayCapture || !BrainOverlay.ShowMovement) return;
        LastReflex = new Reflex(tick, unsafeTick, body);
    }
    public static void RecordAim(Vector2 muzzle, Vector2 target, string weapon, Vector2? launch, string outcome)
    {
        if (!BrainOverlay.MayCapture || !BrainOverlay.ShowAiming) return;
        LastAim = new Aim(Terraria.Main.GameUpdateCount, muzzle, target, weapon, launch, outcome);
    }
    public static void RecordMovement(Controls controls, Vector2[] points, float score)
        => Add(MovementTraces, points, score < float.MaxValue, controls + ";score=" + score);
    public static void RecordTrace(Vector2[] points, string outcome)
    {
        if (!BrainOverlay.MayCapture || !BrainOverlay.ShowAiming) return;
        Add(AimTraces, points, outcome == "target intercepted", outcome);
    }
    private static void Add(System.Collections.Generic.Queue<Trace> traces, Vector2[] points, bool accepted, string reason)
    {
        if (traces.Count >= Capacity) traces.Dequeue();
        traces.Enqueue(new Trace(Terraria.Main.GameUpdateCount, points, accepted, reason));
    }
    /// <summary>
    /// Ticks of brain cost kept for the cost strip: one column per tick at sixty a second, so the strip is the last second of
    /// thinking and nothing older. It is a ring rather than a growing list because the drawing only ever reads the last second,
    /// and a session-long history of a value nobody plots is memory spent on nothing.
    /// </summary>
    internal const int CostTicks = 60;
    private static readonly float[] cost = new float[CostTicks];
    private static int costCount, costNext;

    /// <summary>How many of the ring's ticks hold a sample; below <see cref="CostTicks"/> only in the first second after a reset.</summary>
    public static int CostSamples => costCount;

    /// <summary>The kept ticks oldest first, so index zero is the left-hand column.</summary>
    public static float CostAt(int index) => cost[(costNext - costCount + index + 2 * CostTicks) % CostTicks];

    public static float LastCost => costCount == 0 ? 0f : CostAt(costCount - 1);

    /// <summary>One tick's decide, position and navigate laps, already measured by the brain. Nothing here times anything.</summary>
    public static void RecordCost(float milliseconds)
    {
        cost[costNext] = milliseconds;
        costNext = (costNext + 1) % CostTicks;
        if (costCount < CostTicks) costCount++;
    }

    public static void Reset()
    {
        AimTraces.Clear(); MovementTraces.Clear(); LastReflex = null; LastAim = null;
        System.Array.Clear(cost); costCount = costNext = 0;
    }
}
