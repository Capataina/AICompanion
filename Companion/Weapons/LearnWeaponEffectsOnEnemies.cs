#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// What a hit from a weapon actually does to an enemy — how far it pushes and how much of its damage lands — learned
/// from the companion's own landed hits, per weapon item and per enemy type, with the game's own arithmetic as the
/// prior so the first hit is never blind.
///
/// It is learned rather than read because nothing an item says settles it: knockback resistance differs per enemy,
/// bosses resist entirely, and a mod can change a hit's knockback or damage inside a hook no headless reading
/// reaches. The same stance as the arc learner in <c>../Brain/Infrastructure/Aiming/LearnProjectileArcs.cs</c>: a
/// prior from the decompiled game, a correction from what the companion watched happen, medians over a bounded ring
/// so one odd hit cannot drag the answer and a changed enemy is re-learned from what it does now, and session state
/// that is never saved. <see cref="Reset"/> exists for fixtures.
///
/// The key is the weapon's item type rather than the projectile's, because a push is the item's knockback plus its
/// ammo's, so one wooden arrow fired from two bows pushes differently, and a swing has no projectile at all.
///
/// The push is carried as a signed factor on the prior's horizontal velocity change. Signed on purpose: a weapon whose
/// hits push the other way from the direction the prior assumed learns a negative factor, so the direction is learned
/// by the same arithmetic as the magnitude rather than by a rule of its own. Only the horizontal change is learned,
/// because only the horizontal side of an enemy decides whether a push carries it toward the player or the orb; its
/// vertical pop returns it to its floor.
/// </summary>
public static class WeaponEffects
{
    /// <summary>How many samples of each kind a weapon–enemy pair keeps, the newest kept. As many as the arc learner keeps flights, for its reason.</summary>
    public const int SamplesKept = 8;

    /// <summary>
    /// A predicted horizontal change smaller than this cannot be divided by: an enemy already at the velocity the hit
    /// would leave it with (the heavy-hit branch clamps toward the knockback, it does not add past it) says nothing
    /// about the push, and the ratio of two near-zeros is noise that would swing the median.
    /// </summary>
    public const float MinPredictedChange = 0.05f;

    /// <summary>The widest a learned push factor may read, either way; a mod multiplying knockback past this is still read as pushing hard.</summary>
    public const float MaxPushFactor = 3f;

    /// <summary>The widest a learned damage ratio may read; a hit landing four times its prediction is already a different weapon.</summary>
    public const float MaxDamageFactor = 4f;

    private sealed class Record
    {
        public readonly List<float> Push = new();
        public readonly List<float> Damage = new();
    }

    private static readonly Dictionary<(int Item, int Npc), Record> records = new();

    /// <summary>Advances whenever anything learned changes, so a cache keyed on combat state can include what the table believes.</summary>
    public static int Revision { get; private set; }

    /// <summary>
    /// Whether a projectile's hit pushes away from its owner rather than along its own flight. The companion's shots are
    /// owned by the player, so for these the push points away from the player whatever side the orb shoots from. The list
    /// is the game's: <c>Projectile.Damage</c> sets <c>hitDirectionOverride</c> from the owner's centre for types 697, 699,
    /// 707, 708 and 759 and for aiStyles 15 (flails) and 188 to 191 (the 1.4.4 swing projectiles), read from the decompiled
    /// <c>Terraria.Projectile.cs</c> by <c>Tools/decompile.sh</c>. The flails never reach a slot; the swing projectiles
    /// do, which is why this matters.
    /// </summary>
    public static bool PushesAwayFromOwner(int projectileType)
    {
        if (projectileType <= 0) return false;
        if (projectileType is 697 or 699 or 707 or 708 or 759) return true;
        if (!Terraria.ID.ContentSamples.ProjectilesByType.TryGetValue(projectileType, out Projectile? sample)) return false;
        return sample.aiStyle is 15 or (>= 188 and <= 191);
    }

    /// <summary>
    /// The direction the game will push a hit in, before anything learned: away from the player for an owner-side weapon,
    /// otherwise the sign of the flight's horizontal speed — which is the projectile's own <c>direction</c>, set from its
    /// velocity in <c>Projectile.Update</c>, and for a swing the side of the aim, as <see cref="ItemWeapon"/> passes it.
    /// </summary>
    public static int PriorDirection(bool awayFromOwner, float flightX, float targetCentreX, float playerCentreX)
        => awayFromOwner ? (playerCentreX < targetCentreX ? 1 : -1) : (flightX < 0f ? -1 : 1);

    /// <summary>
    /// The game's knockback falloff from <c>NPC.StrikeNPC</c>: each band above 8, 10, 12 and 14 counts for less, and 16
    /// is the ceiling. Crit's extra 40% is left out because the companion's hits carry no crit.
    /// </summary>
    public static float Compress(float knockback)
    {
        float k = knockback;
        if (k > 8f) k = 8f + (k - 8f) * 0.9f;
        if (k > 10f) k = 10f + (k - 10f) * 0.8f;
        if (k > 12f) k = 12f + (k - 12f) * 0.7f;
        if (k > 14f) k = 14f + (k - 14f) * 0.6f;
        return MathF.Min(k, 16f);
    }

    /// <summary>
    /// The velocity the game leaves an enemy with after a hit, from <c>NPC.GetIncomingStrikeModifiers</c> and
    /// <c>NPC.StrikeNPC</c> as decompiled, with no mod hook applied. The knockback is multiplied by the enemy's resist in
    /// the modifiers (plus a tenth on fire), a resist of zero disables it, and the result is compressed. Then one of two
    /// branches, chosen by whether ten times the damage (fifteen in expert) exceeds the enemy's maximum life: a heavy hit
    /// moves the horizontal speed toward the knockback in the hit's direction by up to twice it, clamped at it, and adds
    /// an upward pop; a light hit sets the horizontal speed to the knockback times the hit's direction times the resist a
    /// second time — the resist lands squared on this branch — and sets the vertical to an upward pop times the resist.
    /// </summary>
    public static Vector2 PriorVelocityAfter(Vector2 before, float knockback, int direction, int damage, NPC npc)
    {
        float resist = npc.knockBackResist;
        if (knockback <= 0f || resist == 0f) return before;
        float k = Compress(knockback * (npc.onFire2 ? 1.1f : 1f) * resist);
        if (k <= 0f) return before;
        Vector2 v = before;
        int heavy = damage * (Main.expertMode ? 15 : 10);
        if (heavy > npc.lifeMax)
        {
            if (direction < 0 && v.X > -k)
            {
                if (v.X > 0f) v.X -= k;
                v.X -= k;
                if (v.X < -k) v.X = -k;
            }
            else if (direction > 0 && v.X < k)
            {
                if (v.X < 0f) v.X += k;
                v.X += k;
                if (v.X > k) v.X = k;
            }
            float pop = (npc.type == 185 ? k * 1.5f : k) * (npc.noGravity ? -0.5f : -0.75f);
            if (v.Y > pop)
            {
                v.Y += pop;
                if (v.Y < pop) v.Y = pop;
            }
        }
        else
        {
            v.Y = -k * (npc.noGravity ? 0.5f : 0.75f) * resist;
            v.X = k * direction * resist;
        }
        return v;
    }

    /// <summary>What the arsenal predicts one hit takes off after armour, before anything learned: the damage less half the defence, at least one.</summary>
    public static float PriorDamage(float perHit, NPC npc) => MathF.Max(1f, perHit - npc.defense / 2f);

    /// <summary>The learned signed push factor for this pair, or 1 while the prior stands.</summary>
    public static float PushFactor(int itemType, int npcType)
        => records.TryGetValue((itemType, npcType), out Record? r) && r.Push.Count > 0 ? Median(r.Push) : 1f;

    /// <summary>The learned damage ratio for this pair, or 1 while the prior stands.</summary>
    public static float DamageFactor(int itemType, int npcType)
        => records.TryGetValue((itemType, npcType), out Record? r) && r.Damage.Count > 0 ? Median(r.Damage) : 1f;

    public static int PushEvidence(int itemType, int npcType) => records.TryGetValue((itemType, npcType), out Record? r) ? r.Push.Count : 0;
    public static int DamageEvidence(int itemType, int npcType) => records.TryGetValue((itemType, npcType), out Record? r) ? r.Damage.Count : 0;

    /// <summary>
    /// The horizontal distance a hit is expected to move an enemy beyond where its own motion would have carried it, over
    /// the settle window: the learned share of the prior's horizontal velocity change, held for the window. Holding it is the
    /// model rather than an airtime, because a knocked-back walker keeps the speed until its own AI turns it round, which the
    /// fighter AI takes on the order of the window to do, while its airtime is a couple of ticks and would read every push
    /// as a pixel. The enemy's current velocity is the baseline and its change is all that is counted, so the motion it was
    /// making anyway is never read as the hit's doing. Terrain is not consulted: this is a distance to weigh, not a place to
    /// stand.
    /// </summary>
    public static float SettledPush(int itemType, NPC npc, float knockback, float perHitAfterArmour, int direction)
    {
        Vector2 before = npc.velocity;
        float change = PriorVelocityAfter(before, knockback, direction, (int)MathF.Max(1f, perHitAfterArmour), npc).X - before.X;
        return change * PushFactor(itemType, npc.type) * Weights.KnockbackSettleTicks;
    }

    /// <summary>
    /// One landed hit, observed: the enemy's velocity just before the strike and just after, the knockback and damage the
    /// weapon handed the strike, the direction the prior assumed, and what the game reported the hit dealt. A sample is
    /// skipped where it cannot be read: a crit, whose 40% the prior does not model; a push the prior predicts as nothing;
    /// a hit that dealt nothing.
    /// </summary>
    public static void ObserveHit(int itemType, NPC npc, Vector2 before, Vector2 after, float knockback, int priorDirection,
        float perHitHanded, int damageDealt, bool crit)
    {
        if (itemType <= 0 || damageDealt <= 0 || crit) return;
        if (!records.TryGetValue((itemType, npc.type), out Record? record))
            records[(itemType, npc.type)] = record = new Record();

        float predictedChange = PriorVelocityAfter(before, knockback, priorDirection, damageDealt, npc).X - before.X;
        if (MathF.Abs(predictedChange) >= MinPredictedChange)
            Add(record.Push, Math.Clamp((after.X - before.X) / predictedChange, -MaxPushFactor, MaxPushFactor));

        float predictedDamage = PriorDamage(perHitHanded, npc);
        Add(record.Damage, Math.Clamp(damageDealt / predictedDamage, 0f, MaxDamageFactor));
    }

    /// <summary>Take this push factor as learned for the pair, as if every kept sample had said it. A fixture uses it to hold a weapon to a known push.</summary>
    public static void AssumePush(int itemType, int npcType, float factor)
    {
        if (!records.TryGetValue((itemType, npcType), out Record? record))
            records[(itemType, npcType)] = record = new Record();
        record.Push.Clear();
        record.Push.Add(factor);
        Revision++;
    }

    private static void Add(List<float> samples, float value)
    {
        samples.Add(value);
        if (samples.Count > SamplesKept)
            samples.RemoveRange(0, samples.Count - SamplesKept);
        Revision++;
    }

    private static float Median(List<float> values)
    {
        var sorted = new List<float>(values);
        sorted.Sort();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
    }

    /// <summary>Forget everything learned, so a fixture measures the prior and the learning rather than the previous case's.</summary>
    public static void Reset()
    {
        records.Clear();
        Revision++;
    }
}
