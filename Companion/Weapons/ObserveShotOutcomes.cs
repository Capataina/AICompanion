#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// What one use of a weapon actually achieved, gathered over the use's whole life and handed to
/// <see cref="AttackLearning"/> when it ends. A shot's window opens when the arsenal fires, holds the projectile slot and
/// every projectile that slot's projectile spawns (and theirs), and closes when every one of them has died or a bound
/// has elapsed; a swing's window opens and closes around its own strikes.
///
/// <para><b>Descendants belong to the shot that made them.</b> A splitting, cluster or star-calling projectile spawns
/// its children from its own AI, and the game hands that spawn an <c>EntitySource_Parent</c> naming the parent
/// projectile. <see cref="AttributeSpawn"/> forgets the child's slot first — a slot reused by any other spawn must never
/// inherit a companion shot — and then, if the parent is a projectile whose slot belongs to an open window, joins the
/// child to that window. So damage landed by a child counts toward the shot that fired the parent, which is what "any
/// weapon, however unusual, is used for what it does" needs. The companion's own spawn names the companion NPC as its
/// parent, not a projectile, so it never attributes itself; the arsenal adds that slot after the spawn has cleared it.
/// The same forget-first discipline as <see cref="TrackLandedHits"/>, one ledger over.</para>
///
/// <para><b>A debuff is read off the struck body, not off the weapon.</b> The NPC modify hook runs before
/// <c>Projectile.StatusNPC</c> applies a projectile's status and the on-hit hook after it (decompiled
/// <c>Projectile.Damage</c>), so buffs kept at the first and compared at the second are exactly what that hit added. A
/// swing goes through <c>Player.ApplyDamageToNPC</c>, which applies no item status at all, so a companion swing is
/// observed the same way and simply never adds one. Every added buff is remembered against the body and the item that
/// added it, which is what <see cref="DebuffedByOther"/> reads when the other weapon is priced.</para>
///
/// <para><b>Damage over time is not counted.</b> Life a debuff burns off after the hit arrives through no hit hook, and
/// crediting a struck body's later life loss to the shot would also credit the player's own attacks. A weapon whose
/// value is its burn is therefore under-credited by exactly its burn; the debuff it applies still reaches the other
/// weapon's context.</para>
///
/// Singleplayer, one ledger per process, session state.
/// </summary>
public static class ShotOutcomes
{
    /// <summary>One closed use: the weapon, its realised yield over the forecast's, the reward, and what went into both.</summary>
    public readonly record struct Outcome(int ItemType, int AimedNpcType, float Ratio, float Reward, float Dealt, int Struck,
        int DebuffsApplied, int LifeTicks, bool Bounded);

    private sealed class Window
    {
        public int Id;
        public int ItemType;
        public int AimedNpcType;
        public float[] Context = Array.Empty<float>();
        public float PredictedDamage;
        public int PredictedStruck;
        public float PredictedCharge;
        public int UseTicks;
        public ulong Opened;
        public readonly HashSet<int> Live = new();
        public float Dealt;
        public readonly Dictionary<(int Slot, int Generation), (int NpcType, int AppliedTicks)> Struck = new();
    }

    private static readonly Dictionary<int, Window> open = new();
    private static readonly int[] slotWindow = new int[Main.maxProjectiles + 1];
    private static int nextId = 1;

    // The buffs each NPC carried just before a hit by an attributed projectile, and which projectile slot (plus one) took it.
    private static readonly int[][] buffTypesBefore = new int[Main.maxNPCs + 1][];
    private static readonly int[][] buffTimesBefore = new int[Main.maxNPCs + 1][];
    private static readonly int[] beforeBy = new int[Main.maxNPCs + 1];

    // Which item added each buff type to each body, per body generation.
    private static readonly Dictionary<int, (int Generation, Dictionary<int, int> ItemByBuff)> addedBy = new();

    public static Outcome? LastClosed { get; private set; }
    public static int OpenCount => open.Count;

    /// <summary>
    /// Open a window for a use that has just happened. The prediction is the forecast's own, before any learned
    /// correction: the sum of its hits' damage, how many bodies it expected to strike, and the push charge it carried.
    /// </summary>
    public static int Open(int itemType, int aimedNpcType, float[] context, float predictedDamage, int predictedStruck,
        float predictedCharge, int useTicks, ulong now)
    {
        int id = nextId++;
        open[id] = new Window
        {
            Id = id, ItemType = itemType, AimedNpcType = aimedNpcType, Context = context, PredictedDamage = predictedDamage,
            PredictedStruck = predictedStruck, PredictedCharge = predictedCharge, UseTicks = Math.Max(1, useTicks), Opened = now,
        };
        return id;
    }

    public static void AddSlot(int windowId, int slot)
    {
        if ((uint)slot >= (uint)slotWindow.Length || !open.TryGetValue(windowId, out Window? window)) return;
        slotWindow[slot] = windowId;
        window.Live.Add(slot);
    }

    /// <summary>The window a projectile slot belongs to, or null.</summary>
    public static int? WindowOf(int slot)
        => (uint)slot < (uint)slotWindow.Length && slotWindow[slot] != 0 && open.ContainsKey(slotWindow[slot]) ? slotWindow[slot] : null;

    /// <summary>
    /// A projectile has just spawned into <paramref name="childSlot"/>. The slot's previous identity is forgotten first;
    /// then, if the spawn's source is a parent projectile whose slot belongs to an open window, the child joins that
    /// window. Returns the window joined, or null.
    /// </summary>
    public static int? AttributeSpawn(int childSlot, IEntitySource? source)
    {
        Forget(childSlot);
        if (source is not EntitySource_Parent { Entity: Projectile parent }) return null;
        if (parent.whoAmI == childSlot || WindowOf(parent.whoAmI) is not int windowId) return null;
        AddSlot(windowId, childSlot);
        return windowId;
    }

    /// <summary>The slot is being reused by a new spawn: it no longer belongs to any window, and a window left with nothing alive closes.</summary>
    public static void Forget(int slot)
    {
        if ((uint)slot >= (uint)slotWindow.Length || slotWindow[slot] == 0) return;
        int id = slotWindow[slot];
        slotWindow[slot] = 0;
        if (open.TryGetValue(id, out Window? window))
        {
            window.Live.Remove(slot);
            if (window.Live.Count == 0) Close(id, Main.GameUpdateCount, bounded: false);
        }
    }

    /// <summary>The projectile in this slot died.</summary>
    public static void Retire(int slot) => Forget(slot);

    /// <summary>An attributed projectile is about to strike this NPC: keep the buffs the strike will be compared against.</summary>
    public static void BeforeStrike(NPC npc, int slot)
    {
        if (WindowOf(slot) == null || (uint)npc.whoAmI >= (uint)beforeBy.Length) return;
        buffTypesBefore[npc.whoAmI] = (int[])npc.buffType.Clone();
        buffTimesBefore[npc.whoAmI] = (int[])npc.buffTime.Clone();
        beforeBy[npc.whoAmI] = slot + 1;
    }

    /// <summary>An attributed projectile's strike landed on this NPC for this much.</summary>
    public static void Landed(NPC npc, int slot, int damageDone)
    {
        if (WindowOf(slot) is not int id || (uint)npc.whoAmI >= (uint)beforeBy.Length) return;
        int[]? types = beforeBy[npc.whoAmI] == slot + 1 ? buffTypesBefore[npc.whoAmI] : null;
        int[]? times = beforeBy[npc.whoAmI] == slot + 1 ? buffTimesBefore[npc.whoAmI] : null;
        beforeBy[npc.whoAmI] = 0;
        Strike(id, npc, damageDone, types, times);
    }

    /// <summary>
    /// One body a use struck, for this much, against the buffs it carried just before (null where they were not kept, in
    /// which case no debuff is inferred). A swing calls this directly around its own strike.
    /// </summary>
    public static void Strike(int windowId, NPC npc, int dealt, int[]? buffTypes, int[]? buffTimes)
    {
        if (!open.TryGetValue(windowId, out Window? window)) return;
        window.Dealt += Math.Max(0, dealt);
        var key = (npc.whoAmI, HostileAttackSources.Generation(npc));
        int applied = buffTypes == null || buffTimes == null ? 0 : AddedBuffTicks(window.ItemType, npc, buffTypes, buffTimes);
        window.Struck[key] = window.Struck.TryGetValue(key, out var seen)
            ? (seen.NpcType, Math.Max(seen.AppliedTicks, applied))
            : (npc.type, applied);
    }

    /// <summary>Close windows whose bound has elapsed.</summary>
    public static void Tick(ulong now)
    {
        if (open.Count == 0) return;
        List<int>? expired = null;
        foreach (Window window in open.Values)
            if (now - window.Opened >= (ulong)Weights.ShotOutcomeWindowTicks)
                (expired ??= new List<int>()).Add(window.Id);
        if (expired != null)
            foreach (int id in expired) Close(id, now, bounded: true);
    }

    /// <summary>
    /// End a use: its realised yield — damage landed and a value per body struck, per second of use — over the yield the
    /// forecast predicted, clamped, taught to the learner against the context the use was fired in, and each struck
    /// body's debuff taught to the debuff rate. A use that struck nothing still teaches its ratio of zero; it teaches no
    /// debuff, since nothing was there to carry one.
    /// </summary>
    public static Outcome? Close(int windowId, ulong now, bool bounded)
    {
        if (!open.Remove(windowId, out Window? window)) return null;
        foreach (int slot in window.Live)
            if (slotWindow[slot] == windowId) slotWindow[slot] = 0;
        float seconds = window.UseTicks / 60f;
        float realised = (window.Dealt + Weights.ShotOutcomeStruckEnemyValue * window.Struck.Count) / seconds;
        float predicted = (window.PredictedDamage + Weights.ShotOutcomeStruckEnemyValue * window.PredictedStruck) / seconds;
        float ratio = predicted <= 0f ? (realised > 0f ? AttackLearning.MaxOutcomeRatio : 1f)
            : Math.Clamp(realised / predicted, 0f, AttackLearning.MaxOutcomeRatio);
        AttackLearning.Observe(window.ItemType, window.AimedNpcType, window.Context, ratio);
        int debuffed = 0;
        foreach (var struck in window.Struck.Values)
        {
            AttackLearning.ObserveDebuff(window.ItemType, struck.NpcType, struck.AppliedTicks > 0, struck.AppliedTicks);
            if (struck.AppliedTicks > 0) debuffed++;
        }
        float reward = realised - Weights.KnockbackInducedDangerWeight * window.PredictedCharge / seconds;
        var outcome = new Outcome(window.ItemType, window.AimedNpcType, ratio, reward, window.Dealt, window.Struck.Count,
            debuffed, (int)Math.Min(int.MaxValue, now - window.Opened), bounded);
        LastClosed = outcome;
        return outcome;
    }

    /// <summary>
    /// Whether this body carries a live buff that a companion hit from an item other than <paramref name="itemType"/>
    /// added — the context the learner prices a weapon's hit in, so a debuff the other weapon applied can make this one's
    /// hits worth more.
    /// </summary>
    public static bool DebuffedByOther(NPC npc, int itemType)
    {
        if (!addedBy.TryGetValue(npc.whoAmI, out var record) || record.Generation != HostileAttackSources.Generation(npc)) return false;
        for (int i = 0; i < npc.buffType.Length; i++)
            if (npc.buffTime[i] > 0 && record.ItemByBuff.TryGetValue(npc.buffType[i], out int by) && by != itemType)
                return true;
        return false;
    }

    /// <summary>Record that a companion hit from this item added this buff to this body; exposed so a fixture can stand a body in the state a real hit leaves.</summary>
    public static void NoteAdded(NPC npc, int buffType, int itemType)
    {
        int generation = HostileAttackSources.Generation(npc);
        if (!addedBy.TryGetValue(npc.whoAmI, out var record) || record.Generation != generation)
            addedBy[npc.whoAmI] = record = (generation, new Dictionary<int, int>());
        record.ItemByBuff[buffType] = itemType;
    }

    public static void Clear()
    {
        open.Clear();
        Array.Clear(slotWindow);
        Array.Clear(beforeBy);
        addedBy.Clear();
        LastClosed = null;
    }

    /// <summary>The longest time any buff this hit added or extended now has left, ticks, noting each against the item; zero where nothing was added.</summary>
    private static int AddedBuffTicks(int itemType, NPC npc, int[] typesBefore, int[] timesBefore)
    {
        int longest = 0;
        for (int i = 0; i < npc.buffType.Length; i++)
        {
            int type = npc.buffType[i], time = npc.buffTime[i];
            if (type <= 0 || time <= 0) continue;
            int had = 0;
            for (int j = 0; j < typesBefore.Length; j++)
                if (typesBefore[j] == type) had = Math.Max(had, timesBefore[j]);
            if (time > had)
            {
                NoteAdded(npc, type, itemType);
                longest = Math.Max(longest, time);
            }
        }
        return longest;
    }
}
