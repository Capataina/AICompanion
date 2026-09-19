#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

public enum NativeEffectAttribution { Unknown, Player, CompanionProjectile, CompanionSwing }

/// <summary>One immutable game-thread observation. Unknown attribution is evidence, not a failed guess at ownership.</summary>
public readonly record struct ObservedEffectReceipt(long Id, long WorldEpoch, ulong OriginTick, string Phase,
    long ObservationOrdinal, int ObjectSlot, int ObjectGeneration, string Kind, int Amount,
    NativeEffectAttribution Attribution, int NativeSourceIdentity);

/// <summary>
/// The sole gameplay receipt queue. Hook phases are supplied by the hook that actually ran; this class never infers a
/// Terraria callback order. A bounded overflow becomes a dirty-state gap, which callers must reobserve before certifying use.
/// </summary>
public static class CollectNativeEffectReceipts
{
    private readonly record struct StrikeKey(int TargetSlot, int TargetGeneration, int SourceSlot);
    private readonly record struct StrikeToken(long Id, StrikeKey Key, NativeEffectAttribution Attribution,
        int PreDispatchDepth, bool EnteredNativeStrike);
    private static readonly Queue<ObservedEffectReceipt> receipts = new();
    private static readonly Dictionary<StrikeKey, Stack<StrikeToken>> pending = new();
    private static readonly Dictionary<(StrikeKey Key, int Depth), ObservedEffectReceipt> completed = new();
    private static readonly Dictionary<long, HashSet<string>> consumers = new();
    private const int Capacity = 256;
    private static long nextId, ordinal, epoch;
    private static int nativeStrikeDepth;
    private static bool strikeHooked;
    private static bool gap;
    private static readonly HashSet<int> dirtyObjects = new();

    public static long Watermark => nextId;
    public static long WorldEpoch => epoch;
    public static long ObservationOrdinal => ordinal;
    public static bool HasGap => gap;
    public static IReadOnlyCollection<int> DirtyObjects => dirtyObjects;
    /// <summary>
    /// Closes the previous callback epoch before taking a new snapshot. Completed receipts and their consumer marks only
    /// serve the synchronous hook traversal that produced them; active pending tokens are never discarded here.
    /// </summary>
    public static long MarkBrainBoundary()
    {
        if (pending.Count == 0) { completed.Clear(); consumers.Clear(); }
        return ++ordinal;
    }

    public static long BeginProjectileStrike(NPC target, Projectile projectile, NativeEffectAttribution attribution)
        => Begin(new StrikeKey(target.whoAmI, HostileAttackSources.Generation(target), projectile.whoAmI), attribution);

    private static long Begin(StrikeKey key, NativeEffectAttribution attribution)
    {
        // All three observer pre-hooks run inside one native dispatch. The first opens it; the later views enrich it.
        if (pending.TryGetValue(key, out Stack<StrikeToken>? stack) && stack.Count > 0)
        {
            StrikeToken top = stack.Peek();
            // All observers of one Modify callback see the same depth. Once the actual native call has begun,
            // that token is closed to pre-hook enrichment and a re-entry at its depth starts a child dispatch.
            if (top.PreDispatchDepth == nativeStrikeDepth && !top.EnteredNativeStrike) return top.Id;
        }
        // A completed key is the preceding physical hit. A new pre-hook, even in the same tick with equal damage, starts anew.
        completed.Remove((key, nativeStrikeDepth));
        long id = ++nextId;
        (stack ??= pending[key] = new Stack<StrikeToken>()).Push(new StrikeToken(id, key, attribution, nativeStrikeDepth, false));
        ++ordinal;
        return id;
    }

    /// <summary>Player direct/item, dash and mount strikes arrive through ModPlayer rather than the projectile callbacks.</summary>
    public static long BeginPlayerStrike(NPC target) => Begin(new StrikeKey(target.whoAmI, HostileAttackSources.Generation(target), -1), NativeEffectAttribution.Player);

    public static ObservedEffectReceipt CompleteProjectileStrike(NPC target, Projectile projectile, int damageDone, string phase)
        => Complete(new StrikeKey(target.whoAmI, HostileAttackSources.Generation(target), projectile.whoAmI), damageDone, phase, "projectile-strike");

    public static ObservedEffectReceipt CompletePlayerStrike(NPC target, int damageDone, string phase)
        => Complete(new StrikeKey(target.whoAmI, HostileAttackSources.Generation(target), -1), damageDone, phase, "player-strike");

    private static ObservedEffectReceipt Complete(StrikeKey key, int damageDone, string phase, string kind)
    {
        ++ordinal;
        if (completed.TryGetValue((key, nativeStrikeDepth), out ObservedEffectReceipt prior)) return prior;
        if (pending.TryGetValue(key, out Stack<StrikeToken>? stack) && stack.Count > 0
            && stack.Peek().PreDispatchDepth == nativeStrikeDepth)
        {
            StrikeToken token = stack.Pop();
            if (stack.Count == 0) pending.Remove(key);
            var receipt = new ObservedEffectReceipt(token.Id, epoch, Main.GameUpdateCount, phase, ordinal, key.TargetSlot,
                key.TargetGeneration, kind, Math.Max(0, damageDone), token.Attribution, key.SourceSlot);
            completed[(key, nativeStrikeDepth)] = receipt;
            Enqueue(receipt);
            return receipt;
        }
        var unmatched = new ObservedEffectReceipt(++nextId, epoch, Main.GameUpdateCount, phase, ordinal, key.TargetSlot,
            key.TargetGeneration, kind, Math.Max(0, damageDone), NativeEffectAttribution.Unknown, key.SourceSlot);
        completed[(key, nativeStrikeDepth)] = unmatched;
        Enqueue(unmatched);
        return unmatched;
    }

    /// <summary>Each consumer acts once on a physical receipt even though its hook may be one of several post views.</summary>
    public static bool TryConsume(long receiptId, string consumer)
        => consumers.TryGetValue(receiptId, out HashSet<string>? seen) ? seen.Add(consumer) : (consumers[receiptId] = new HashSet<string> { consumer }) != null;

    /// <summary>Records a completed non-strike world effect at the owner that observed it, never from a prediction or XP award.</summary>
    public static void RecordEffect(string kind, int objectSlot, int objectGeneration, int amount,
        NativeEffectAttribution attribution, int nativeSourceIdentity = -1)
    {
        ++ordinal;
        Enqueue(new ObservedEffectReceipt(++nextId, epoch, Main.GameUpdateCount, "observed-success", ordinal,
            objectSlot, objectGeneration, kind, Math.Max(0, amount), attribution, nativeSourceIdentity));
    }

    /// <summary>Headless hook-order fixture seam; production hooks use the entity overloads above.</summary>
    public static long BeginSyntheticStrike(int targetSlot, int targetGeneration, int sourceSlot, NativeEffectAttribution attribution)
        => Begin(new StrikeKey(targetSlot, targetGeneration, sourceSlot), attribution);

    /// <summary>Headless representation of a re-entry observed while the native StrikeNPC detour is active.</summary>
    public static long BeginSyntheticNestedStrike(int targetSlot, int targetGeneration, int sourceSlot, NativeEffectAttribution attribution)
    {
        nativeStrikeDepth++;
        return Begin(new StrikeKey(targetSlot, targetGeneration, sourceSlot), attribution);
    }

    public static void EndSyntheticNestedStrike() => nativeStrikeDepth--;

    /// <summary>Headless hook-order fixture seam; distinct repeated begins prove same-tick equal strikes remain distinct.</summary>
    public static ObservedEffectReceipt CompleteSyntheticStrike(int targetSlot, int targetGeneration, int sourceSlot, int damage, string phase)
        => Complete(new StrikeKey(targetSlot, targetGeneration, sourceSlot), damage, phase, "synthetic-strike");

    public static IReadOnlyList<ObservedEffectReceipt> DrainThrough(long watermark)
    {
        var result = new List<ObservedEffectReceipt>();
        while (receipts.Count > 0 && receipts.Peek().Id <= watermark) result.Add(receipts.Dequeue());
        return result;
    }

    /// <summary>Drains by observation order, not receipt ID: nested child receipts can complete before an older parent ID.</summary>
    public static IReadOnlyList<ObservedEffectReceipt> DrainThroughOrdinal(long ordinalWatermark)
    {
        var result = new List<ObservedEffectReceipt>();
        while (receipts.Count > 0 && receipts.Peek().ObservationOrdinal <= ordinalWatermark) result.Add(receipts.Dequeue());
        return result;
    }

    private static void Enqueue(ObservedEffectReceipt receipt)
    {
        if (receipts.Count >= Capacity) { gap = true; dirtyObjects.Add(receipt.ObjectSlot); return; }
        receipts.Enqueue(receipt);
    }

    public static void AcknowledgeGapAfterReobserve() { gap = false; dirtyObjects.Clear(); }
    private static int ObserveNativeStrike(On_NPC.orig_StrikeNPC_HitInfo_bool_bool orig, NPC npc, NPC.HitInfo hit,
        bool fromNet, bool noPlayerInteraction)
    {
        nativeStrikeDepth++;
        MarkNativeBoundary(npc);
        try { return orig(npc, hit, fromNet, noPlayerInteraction); }
        finally { nativeStrikeDepth--; }
    }

    /// <summary>
    /// The detour is the only boundary between a synchronous Modify traversal and a genuine nested strike.
    /// A target can have only one matching unentered pre-token at this depth; ambiguity remains unmarked rather
    /// than guessing which source owns the native call.
    /// </summary>
    private static void MarkNativeBoundary(NPC target)
    {
        StrikeKey? match = null;
        foreach ((StrikeKey key, Stack<StrikeToken> stack) in pending)
        {
            if (key.TargetSlot != target.whoAmI || key.TargetGeneration != HostileAttackSources.Generation(target) || stack.Count == 0) continue;
            StrikeToken top = stack.Peek();
            if (top.PreDispatchDepth != nativeStrikeDepth - 1 || top.EnteredNativeStrike) continue;
            if (match != null) return;
            match = key;
        }
        if (match is not StrikeKey selected) return;
        Stack<StrikeToken> selectedStack = pending[selected];
        StrikeToken token = selectedStack.Pop();
        selectedStack.Push(token with { EnteredNativeStrike = true });
    }

    public static void InstallStrikeHook()
    {
        if (strikeHooked) return;
        strikeHooked = true;
        On_NPC.StrikeNPC_HitInfo_bool_bool += ObserveNativeStrike;
    }

    public static void RemoveStrikeHook()
    {
        if (!strikeHooked) return;
        strikeHooked = false;
        On_NPC.StrikeNPC_HitInfo_bool_bool -= ObserveNativeStrike;
    }

    public static void ResetWorld() { receipts.Clear(); pending.Clear(); completed.Clear(); consumers.Clear(); dirtyObjects.Clear(); gap = false; nativeStrikeDepth = 0; nextId = ordinal = 0; epoch++; }
}

/// <summary>World boundaries invalidate every native identity; this lifecycle hook owns only receipt state.</summary>
public sealed class ResetNativeEffectReceipts : ModSystem
{
    public override void Load() => CollectNativeEffectReceipts.InstallStrikeHook();
    public override void Unload() => CollectNativeEffectReceipts.RemoveStrikeHook();
    public override void OnWorldLoad() => CollectNativeEffectReceipts.ResetWorld();
    public override void OnWorldUnload() => CollectNativeEffectReceipts.ResetWorld();
}
