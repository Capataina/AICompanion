#nullable enable
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.BehaviourDiagnostics;

/// <summary>Small, opt-in retained facts from computations the inspector must not rerun.</summary>
public static class BrainInspectorSamples
{
    public readonly record struct Reflex(ulong Tick, int UnsafeTick, BodyState Body);
    public readonly record struct Aim(ulong Tick, Vector2 Muzzle, Vector2 Target, string Weapon, Vector2? Launch, string Outcome);
    private const int Capacity = 8;
    public readonly record struct Trace(ulong Tick, Vector2[] Points, bool Accepted, string Reason);
    public static readonly System.Collections.Generic.Queue<Trace> AimTraces = new();
    public static readonly System.Collections.Generic.Queue<Trace> MovementTraces = new();
    public static Reflex? LastReflex { get; private set; }
    public static Aim? LastAim { get; private set; }
    public static void RecordReflex(ulong tick, int unsafeTick, BodyState body)
    {
        if (!BrainOverlay.MayCapture || !BrainOverlay.ShowMovement) return;
        LastReflex = new Reflex(tick, unsafeTick, body);
    }
    public static void RecordAim(ulong tick, Vector2 muzzle, Vector2 target, string weapon, Vector2? launch, string outcome)
    {
        if (!BrainOverlay.MayCapture || !BrainOverlay.ShowAiming) return;
        LastAim = new Aim(tick, muzzle, target, weapon, launch, outcome);
    }
    public static void RecordMovement(Controls controls, Vector2[] points, float score)
        => Add(MovementTraces, points, score < float.MaxValue, controls + ";score=" + score);
    public static void RecordTrace(Vector2[] points, string outcome)
        => Add(AimTraces, points, outcome == "target intercepted", outcome);
    private static void Add(System.Collections.Generic.Queue<Trace> traces, Vector2[] points, bool accepted, string reason)
    {
        if (traces.Count >= Capacity) traces.Dequeue();
        traces.Enqueue(new Trace(Terraria.Main.GameUpdateCount, points, accepted, reason));
    }
    public static void Reset() { AimTraces.Clear(); MovementTraces.Clear(); LastReflex = null; LastAim = null; }
}
