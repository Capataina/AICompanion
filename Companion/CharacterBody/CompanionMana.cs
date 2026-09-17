#nullable enable

using System;
using Terraria;

namespace AICompanion.Companion.CharacterBody;

/// <summary>
/// The companion's mana: a pool mirroring the player's maximum, spent per cast, regenerating on
/// its own, and never refusing a cast. Its whole effect on play is <see cref="DamageFactor"/>: a
/// cast at a full pool lands at full strength and a cast at an empty pool lands at half, on a
/// straight line between. The owner's ruling of 14 September 2026 is that magic tires the
/// companion rather than stopping it, and that the arsenal needs no rule of its own for it,
/// because the arsenal ranks weapons by the damage they will land over its window and a tired
/// staff simply ranks below a fresh bow.
///
/// The pool is per companion and lives on the NPC; it is not persisted, because a session's
/// tiredness is not character state. The maximum follows the player's every tick the way life
/// and defence do (see CompanionNPC.MirrorStats), so a mana crystal reaches the companion on the
/// next tick and nothing is ever equipped on it.
/// </summary>
public sealed class CompanionMana
{
    /// <summary>
    /// The factor an empty pool lands at. The ruling is "about half"; the gradient is linear
    /// from 1 at full to this at empty, so a pool at 20% lands at 0.6.
    /// </summary>
    public const float EmptyDamageFactor = 0.5f;

    /// <summary>
    /// Ticks after a spend before regeneration resumes, so a burst of casts drains rather than
    /// treading water; and the ticks a regeneration from empty to full takes once it resumes.
    /// Both are the companion's own numbers rather than the player's regen curve, because the
    /// player's curve depends on standing still and on accessories the companion does not wear.
    /// </summary>
    public const int RegenDelayTicks = 60;
    public const int RefillTicks = 420;

    public int Max { get; private set; } = 20;
    public float Current { get; private set; } = 20;

    private int ticksSinceSpend = int.MaxValue;

    /// <summary>Full is 1, empty is 0; the notch draws this.</summary>
    public float Fraction => Max <= 0 ? 1f : Math.Clamp(Current / Max, 0f, 1f);

    /// <summary>What a cast lands at right now, from 1 at a full pool to <see cref="EmptyDamageFactor"/> at empty.</summary>
    public float DamageFactor => EmptyDamageFactor + (1f - EmptyDamageFactor) * Fraction;

    /// <summary>
    /// Follow the player's maximum. A raise carries the current pool up by the same amount, a
    /// cut clamps it, mirroring how life is mirrored, so the fraction a player sees does not jump.
    /// </summary>
    public void Sync(Player player)
    {
        int newMax = Math.Max(1, player.statManaMax2);
        if (newMax == Max)
            return;
        Current = Math.Clamp(Current + (newMax - Max), 0f, newMax);
        Max = newMax;
    }

    /// <summary>
    /// Spend a cast's cost. The pool floors at zero and the cast still happens: the caller reads
    /// <see cref="DamageFactor"/> before spending, so the cast that empties the pool lands at the
    /// strength the pool had, and the next one lands at half.
    /// </summary>
    public void Spend(int cost)
    {
        if (cost <= 0)
            return;
        Current = Math.Max(0f, Current - cost);
        ticksSinceSpend = 0;
    }

    /// <summary>Restore the pool a snapshot carried, after <see cref="Sync"/> sets the maximum: the
    /// audit replays the same mana share. Regeneration never runs between, so the delay is untouched.</summary>
    public void Assume(float current) => Current = Math.Clamp(current, 0f, Max);

    /// <summary>Once per tick: wait out the delay after a spend, then refill at a fixed rate.</summary>
    public void Tick()
    {
        if (ticksSinceSpend < int.MaxValue)
            ticksSinceSpend++;
        if (ticksSinceSpend < RegenDelayTicks || Current >= Max)
            return;
        Current = Math.Min(Max, Current + (float)Max / RefillTicks);
    }
}
