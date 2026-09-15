#nullable enable
using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// Bounded comparison of a first attack plus greedy follow-up attacks. Geometry supplies legal
/// hits; this class owns their value. Forecast health reserves damage already assigned to an
/// in-flight shot, preventing both overkill credit and repeated credit for the same kill.
/// It estimates a short horizon, not the enemy's future AI or an optimal combat sequence.
///
/// Harm an enemy would do is credited in proportion to how much of it a hit removes: the kill credit in full, and a
/// share for a wound, scaled by the fraction of the enemy's remaining life the wound takes. A hit that does not kill is
/// also charged for the danger its push adds — the geometry adapter supplies that as a number on the hit, measured on the
/// threat sense's own urgency scale, so this class stays arithmetic. A kill is never charged, because a dead enemy is
/// pushed nowhere.
///
/// A hit may leave its body debuffed, and a debuff one weapon applied may make the other weapon's later hits worth more.
/// The adapter supplies both halves as numbers the learner produced — the chance a hit debuffs and for how long, and what
/// a hit would deal against a body the other weapon has debuffed — and this class carries the state forward through the
/// continuation: once an applied attack's impact lands, its body counts as debuffed by that weapon, with that chance,
/// until the debuff runs out, and a later hit from a different weapon is valued between its plain and its debuffed damage
/// by that chance. That is what lets the evaluator open with the weapon that sets up the other, without a rule naming a
/// pair. A debuff the body already carries when the forecast is made is the adapter's to price, since only it can read
/// the body; this class knows nothing of when that one ends.
/// </summary>
public static class EvaluateAttackOutcomes
{
    public readonly record struct Target(int Id, float Life, float Danger, float ExpectedHarm);

    /// <summary>
    /// One body a use would strike: its damage, and the danger the hit's push would add to the player and the orb — added
    /// urgency times what one of that enemy's hits costs each body, zero where the push adds none. <paramref name="DamageDebuffed"/>
    /// is what the hit would deal against a body the other weapon has debuffed, NaN where it is the same;
    /// <paramref name="DebuffChance"/> and <paramref name="DebuffTicks"/> are how likely this hit leaves its body debuffed and
    /// for how long.
    /// </summary>
    public readonly record struct Hit(int Target, float Damage, float InducedDanger = 0f, float DamageDebuffed = float.NaN,
        float DebuffChance = 0f, int DebuffTicks = 0);

    /// <summary>One use of a weapon at a target. <paramref name="AimOffset"/> is how far off the solver's intercept the hands would aim, radians, as the learner chose it.</summary>
    public sealed record Attack(int Weapon, int Target, int UseTicks, int ImpactTicks, Hit[] Hits, float AimOffset = 0f);
    public readonly record struct Outcome(float Damage, int Kills, float PreventedHarm, float Value);
    private const int MaxAttacks = 16;

    public static Outcome Evaluate(Attack first, IReadOnlyList<Attack> alternatives,
        IReadOnlyList<Target> targets, int cooldown, int horizon)
    {
        var remaining = new Dictionary<int, float>();
        var facts = new Dictionary<int, Target>();
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs = null;
        foreach (Target target in targets) { remaining[target.Id] = target.Life; facts[target.Id] = target; }
        int fireAt = Math.Max(0, cooldown);
        Attack? attack = first;
        Outcome total = default;
        for (int step = 0; step < MaxAttacks && attack != null && fireAt < horizon; step++)
        {
            Outcome result = Value(attack, fireAt, horizon, remaining, facts, ref debuffs, apply: true);
            total = new Outcome(total.Damage + result.Damage, total.Kills + result.Kills,
                total.PreventedHarm + result.PreventedHarm, total.Value + result.Value);
            fireAt += Math.Max(1, attack.UseTicks);
            attack = null;
            float best = 0f;
            foreach (Attack candidate in alternatives)
            {
                Outcome next = Value(candidate, fireAt, horizon, remaining, facts, ref debuffs, apply: false);
                if (next.Value > best) { best = next.Value; attack = candidate; }
            }
        }
        return total;
    }

    private static Outcome Value(Attack attack, int fireAt, int horizon,
        Dictionary<int, float> remaining, Dictionary<int, Target> targets,
        ref Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs, bool apply)
    {
        int impact = fireAt + Math.Max(1, attack.ImpactTicks);
        if (impact >= horizon) return default;
        float timing = 1f - (float)impact / horizon;
        float damage = 0f, prevented = 0f, value = 0f;
        int kills = 0;
        // A weapon adapter aggregates the damage actually predicted along its projectiles;
        // it must never turn projectile count into guaranteed hits on every nearby body.
        for (int index = 0; index < attack.Hits.Length; index++)
        {
            Hit hit = attack.Hits[index];
            if (!remaining.TryGetValue(hit.Target, out float life) || life <= 0f) continue;
            if (!targets.TryGetValue(hit.Target, out Target target)) continue;
            if (!apply)
                for (int previous = 0; previous < index; previous++)
                    if (attack.Hits[previous].Target == hit.Target) life -= Math.Max(0f, Effective(attack.Hits[previous], attack.Weapon, impact, debuffs));
            if (life <= 0f) continue;
            float dealt = Math.Min(life, Math.Max(0f, Effective(hit, attack.Weapon, impact, debuffs)));
            damage += dealt;
            value += dealt * (Weights.AttackDelayedDamageFraction + (1f - Weights.AttackDelayedDamageFraction) * timing);
            if (dealt >= life)
            {
                kills++;
                float harm = Math.Max(0f, target.ExpectedHarm) * Math.Clamp(target.Danger, 0f, 1f) * timing;
                prevented += harm;
                value += harm * Weights.AttackPreventedHarmWeight + Weights.AttackFinishingValue * timing;
            }
            else
            {
                float partial = Math.Max(0f, target.ExpectedHarm) * Math.Clamp(target.Danger, 0f, 1f)
                    * (dealt / life) * Weights.AttackPartialHarmShare * timing;
                prevented += partial;
                value += partial * Weights.AttackPreventedHarmWeight;
                value -= Math.Max(0f, hit.InducedDanger) * Weights.KnockbackInducedDangerWeight * timing;
                if (apply && hit.DebuffChance > 0f && hit.DebuffTicks > 0)
                {
                    debuffs ??= new();
                    var key = (hit.Target, attack.Weapon);
                    float chance = debuffs.TryGetValue(key, out var held) && held.Until > impact
                        ? MathF.Max(held.Chance, hit.DebuffChance) : hit.DebuffChance;
                    debuffs[key] = (Math.Clamp(chance, 0f, 1f), impact + hit.DebuffTicks);
                }
            }
            if (apply) remaining[hit.Target] = life - dealt;
        }
        return new Outcome(damage, kills, prevented, value);
    }

    /// <summary>
    /// What a hit deals at its impact: its plain damage, moved toward its debuffed damage by the chance that a different
    /// weapon's earlier hit in this continuation left the body debuffed and still is at this impact. The weapon's own
    /// debuff never counts, because the debuffed damage is what the learner measured against the other weapon's.
    /// </summary>
    private static float Effective(in Hit hit, int weapon, int impact, Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs)
    {
        if (debuffs == null || float.IsNaN(hit.DamageDebuffed)) return hit.Damage;
        float chance = 0f;
        foreach (var entry in debuffs)
            if (entry.Key.Target == hit.Target && entry.Key.Weapon != weapon && entry.Value.Until > impact)
                chance = MathF.Max(chance, entry.Value.Chance);
        return chance <= 0f ? hit.Damage : hit.Damage + (hit.DamageDebuffed - hit.Damage) * chance;
    }
}
