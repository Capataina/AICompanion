#nullable enable
using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.BehaviourSelection;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// Bounded comparison of a first attack plus greedy follow-up attacks. Geometry supplies legal
/// hits; this class owns their value. Forecast health reserves damage already assigned to an
/// in-flight shot, preventing both overkill credit and repeated credit for the same kill.
/// It estimates a short horizon, not the enemy's future AI or an optimal combat sequence.
/// </summary>
public static class EvaluateAttackOutcomes
{
    public readonly record struct Target(int Id, float Life, float Danger, float ExpectedHarm);
    public readonly record struct Hit(int Target, float Damage);
    public sealed record Attack(int Weapon, int Target, int UseTicks, int ImpactTicks, Hit[] Hits);
    public readonly record struct Outcome(float Damage, int Kills, float PreventedHarm, float Value);
    private const int MaxAttacks = 16;

    public static Outcome Evaluate(Attack first, IReadOnlyList<Attack> alternatives,
        IReadOnlyList<Target> targets, int cooldown, int horizon)
    {
        var remaining = new Dictionary<int, float>();
        var facts = new Dictionary<int, Target>();
        foreach (Target target in targets) { remaining[target.Id] = target.Life; facts[target.Id] = target; }
        int fireAt = Math.Max(0, cooldown);
        Attack? attack = first;
        Outcome total = default;
        for (int step = 0; step < MaxAttacks && attack != null && fireAt < horizon; step++)
        {
            Outcome result = Value(attack, fireAt, horizon, remaining, facts, apply: true);
            total = new Outcome(total.Damage + result.Damage, total.Kills + result.Kills,
                total.PreventedHarm + result.PreventedHarm, total.Value + result.Value);
            fireAt += Math.Max(1, attack.UseTicks);
            attack = null;
            float best = 0f;
            foreach (Attack candidate in alternatives)
            {
                Outcome next = Value(candidate, fireAt, horizon, remaining, facts, apply: false);
                if (next.Value > best) { best = next.Value; attack = candidate; }
            }
        }
        return total;
    }

    private static Outcome Value(Attack attack, int fireAt, int horizon,
        Dictionary<int, float> remaining, Dictionary<int, Target> targets, bool apply)
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
                    if (attack.Hits[previous].Target == hit.Target) life -= Math.Max(0f, attack.Hits[previous].Damage);
            if (life <= 0f) continue;
            float dealt = Math.Min(life, Math.Max(0f, hit.Damage));
            damage += dealt;
            value += dealt * (Weights.AttackDelayedDamageFraction + (1f - Weights.AttackDelayedDamageFraction) * timing);
            if (dealt >= life)
            {
                kills++;
                float harm = Math.Max(0f, target.ExpectedHarm) * Math.Clamp(target.Danger, 0f, 1f) * timing;
                prevented += harm;
                value += harm * Weights.AttackPreventedHarmWeight + Weights.AttackFinishingValue * timing;
            }
            if (apply) remaining[hit.Target] = life - dealt;
        }
        return new Outcome(damage, kills, prevented, value);
    }
}
