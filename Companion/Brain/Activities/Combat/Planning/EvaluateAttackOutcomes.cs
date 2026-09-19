#nullable enable
using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

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
    /// urgency times what one of that enemy's hits costs each body, zero where the push adds none. <paramref name="InducedDanger"/>
    /// is the total both bodies; <paramref name="InducedDangerToCompanion"/> carries the orb's half apart, so the vector
    /// normalises each half by its own body's life and the player's half is the difference. The scalar prices the total.
    /// <paramref name="DamageDebuffed"/> is what the hit would deal against a body the other weapon has debuffed, NaN where
    /// it is the same; <paramref name="DebuffChance"/> and <paramref name="DebuffTicks"/> are how likely this hit leaves its
    /// body debuffed and for how long.
    /// </summary>
    public readonly record struct Hit(int Target, float Damage, float InducedDanger = 0f, float DamageDebuffed = float.NaN,
        float DebuffChance = 0f, int DebuffTicks = 0, float InducedDangerToCompanion = 0f);

    /// <summary>One use of a weapon at a target. <paramref name="AimOffset"/> is how far off the solver's intercept the hands would aim, radians, as the learner chose it.</summary>
    public sealed record Attack(int Weapon, int Target, int UseTicks, int ImpactTicks, Hit[] Hits, float AimOffset = 0f,
        float ManaCost = 0f, int TargetImpactTicks = -1);
    public readonly record struct Outcome(float Damage, int Kills, float PreventedHarm, float Value);

    /// <summary>
    /// A priced piece of a fight: the objective vector plus what the next piece prices against — the
    /// raw damage dealt, the last impact's search-relative tick, and the enemies' remaining life after
    /// it. The beam rolls one segment's valuation into the next one's starting state, so a later
    /// segment never re-kills what an earlier one already removed.
    /// </summary>
    public sealed record Valuation(CombatOutcome Outcome, float DamageDealt, int LastImpactTick,
        Dictionary<int, float> RemainingLife);

    /// <summary>
    /// What the vector needs beyond the attacks themselves, all as numbers so this class still reads no world:
    /// the ticks to reach the stand, the predicted harm at the stand and along the travel as shares of the
    /// companion's life, the bodies' lives, the mana pool, and the company gap the planner integrated over
    /// the segment, already in region-size times horizon units.
    /// </summary>
    public readonly record struct PlanContext(int TravelTicks, float StandHarm, float TravelHarm,
        float PlayerLife, float CompanionLife, float ManaPool, float CompanyGap);

    public const int MaxAttacks = 16;

    public static Outcome Evaluate(Attack first, IReadOnlyList<Attack> alternatives,
        IReadOnlyList<Target> targets, int cooldown, int horizon,
        Dictionary<int, int>? usesLeft = null)
    {
        var remaining = new Dictionary<int, float>();
        var facts = new Dictionary<int, Target>();
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs = null;
        Dictionary<int, int>? throws = usesLeft == null ? null : new Dictionary<int, int>(usesLeft);
        foreach (Target target in targets) { remaining[target.Id] = target.Life; facts[target.Id] = target; }
        int fireAt = Math.Max(0, cooldown);
        Attack? attack = first;
        Outcome total = default;
        for (int step = 0; step < MaxAttacks && attack != null && fireAt < horizon; step++)
        {
            if (throws != null && throws.TryGetValue(attack.Weapon, out int remainingThrows) && remainingThrows <= 0)
                break;
            Outcome result = Value(attack, fireAt, horizon, remaining, facts, ref debuffs, apply: true);
            total = new Outcome(total.Damage + result.Damage, total.Kills + result.Kills,
                total.PreventedHarm + result.PreventedHarm, total.Value + result.Value);
            SpendThrow(throws, attack.Weapon);
            fireAt += Math.Max(1, attack.UseTicks);
            attack = null;
            float best = 0f;
            foreach (Attack candidate in alternatives)
            {
                if (throws != null && throws.TryGetValue(candidate.Weapon, out int left) && left <= 0)
                    continue;
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
                    * (dealt / target.Life) * Weights.AttackPartialHarmShare * timing;
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
    /// The same greedy continuation as <see cref="Evaluate"/>, but the continuation takes the largest weighted
    /// marginal gain and the result is the objective vector: every simulated hit applied in tick order to the
    /// plan's own copy of the enemies, overkill and duplicate pellets earning nothing twice. The scalar
    /// <see cref="Evaluate"/> stays beside it while the sides price offers; it goes when they do.
    ///
    /// The planner reads the continuation back through the collectors: <paramref name="sequence"/> takes the
    /// attacks the continuation applied with their absolute fire ticks, and <paramref name="killTicks"/> the
    /// absolute tick each target died at. Both are written only on the applied path, never on a marginal probe.
    ///
    /// <paramref name="initialRemaining"/> starts the roll from an earlier piece's ending life instead of
    /// full life; the beam threads one segment's remainder into the next. Null is the whole fight from
    /// full life, which is every level-one call. <paramref name="initialDebuffs"/> is the same threading
    /// for debuff marks: an earlier piece's burst debuffs the bodies its hits wounded, and this piece's
    /// later hits are priced with the boosted damage — without it a grenade-then-bow plan prices its bow
    /// as if the burst never marked anything, and firing at arrival always wins. Marks are in this roll's
    /// own frame, copied on entry because the roll extends them. Null is no earlier piece.
    /// <paramref name="usesLeft"/> is remaining throws per weapon slot: a consumable's stack, copied on
    /// entry. Null is unlimited, which is every non-consumable and every caller that does not plan a
    /// throw. A slot missing from the map is unlimited; a slot at zero is skipped, so a second grenade
    /// is never priced after the stack is spent.
    /// </summary>
    public static Valuation EvaluateVector(Attack first, IReadOnlyList<Attack> alternatives,
        IReadOnlyList<Target> targets, int cooldown, int horizon, PlanContext context, CombatWeights weights,
        int searchTick = 0, ICollection<(Attack Attack, int FireTick)>? sequence = null,
        ICollection<(int Target, int Tick)>? killTicks = null,
        Dictionary<int, float>? initialRemaining = null,
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? initialDebuffs = null,
        int maxSteps = MaxAttacks,
        Dictionary<int, int>? usesLeft = null)
    {
        var remaining = new Dictionary<int, float>();
        var facts = new Dictionary<int, Target>();
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs =
            initialDebuffs == null ? null : new Dictionary<(int Target, int Weapon), (float Chance, int Until)>(initialDebuffs);
        Dictionary<int, int>? throws = usesLeft == null ? null : new Dictionary<int, int>(usesLeft);
        float encounterLife = 0f;
        foreach (Target target in targets)
        {
            remaining[target.Id] = initialRemaining != null && initialRemaining.TryGetValue(target.Id, out float lifeLeft)
                ? Math.Max(0f, lifeLeft) : target.Life;
            facts[target.Id] = target;
            encounterLife += Math.Max(0f, target.Life);
        }
        encounterLife = Math.Max(1f, encounterLife);
        // Search-relative ticks throughout: the first use fires when the hands are ready AND the body
        // has arrived, so a stand a flight away does not schedule — or stamp, or price — uses before the
        // body is there to fire them. Callers pass the raw cooldown; the travel comes from the context.
        int fireAt0 = Math.Max(cooldown, context.TravelTicks);
        int fireAt = fireAt0;
        float damage = 0f, threat = 0f, prevented = 0f, push = 0f, mana = 0f;
        int firstLanded = -1, lastImpact = fireAt0;
        Attack? attack = first;
        int steps = Math.Clamp(maxSteps, 1, MaxAttacks);
        for (int step = 0; step < steps && attack != null && fireAt < horizon; step++)
        {
            if (throws != null && throws.TryGetValue(attack.Weapon, out int remainingThrows) && remainingThrows <= 0)
                break;
            AttackParts parts = ValueParts(attack, fireAt, horizon, remaining, facts, ref debuffs, context, apply: true, searchTick, killTicks);
            sequence?.Add((attack, searchTick + fireAt));
            SpendThrow(throws, attack.Weapon);
            damage += parts.Damage; threat += parts.Threat; prevented += parts.Prevented; push += parts.Push;
            mana += Math.Max(0f, attack.ManaCost);
            if (parts.Damage > 0f)
            {
                if (firstLanded < 0) firstLanded = parts.FirstImpact;
                lastImpact = Math.Max(lastImpact, parts.LastImpact);
            }
            fireAt += Math.Max(1, attack.UseTicks);
            attack = null;
            float best = 0f;
            foreach (Attack candidate in alternatives)
            {
                if (throws != null && throws.TryGetValue(candidate.Weapon, out int left) && left <= 0)
                    continue;
                AttackParts next = ValueParts(candidate, fireAt, horizon, remaining, facts, ref debuffs, context, apply: false, searchTick, killTicks);
                float gain = weights.Weighted(Marginal(next, candidate, fireAt, horizon, context, encounterLife));
                if (gain > best) { best = gain; attack = candidate; }
            }
        }
        int duration = Math.Max(1, lastImpact);
        return new Valuation(Assemble(damage, threat, prevented, push, mana, firstLanded, duration,
            context, horizon, encounterLife), damage, duration, remaining);
    }

    /// <summary>
    /// One vector from rolled totals: damage over the span in encounter-life units, prevention in player
    /// life, harm over the stand and travel spans, the gap the caller integrated, first damage timed.
    /// Both the greedy continuation and the fixed-sequence roll below assemble through here, so a
    /// truncated prefix prices in the same units as the continuation it came from.
    /// </summary>
    private static CombatOutcome Assemble(float damage, float threat, float prevented, float push, float mana,
        int firstLanded, int duration, PlanContext context, int horizon, float encounterLife)
    {
        float playerLife = Math.Max(1f, context.PlayerLife);
        return new CombatOutcome(
            DamagePerSecond: damage / (duration / 60f) / encounterLife,
            ThreatRemoved: threat,
            PlayerHarmPrevented: prevented / playerLife,
            CompanionHarmTaken: (context.StandHarm * Math.Max(0, duration - context.TravelTicks) + context.TravelHarm * context.TravelTicks) / 60f,
            PushDangerAdded: push,
            CompanyGap: Math.Max(0f, context.CompanyGap),
            TimeToFirstDamage: firstLanded < 0 ? 1f : Math.Clamp(firstLanded / (float)Math.Max(1, horizon), 0f, 1f),
            ManaSpent: mana / Math.Max(1f, context.ManaPool));
    }

    /// <summary>
    /// A fixed sequence rolled in order: the beam's truncated prefix, whose uses were chosen by an
    /// earlier greedy run and are re-priced here without re-choosing. Fire ticks are search-relative,
    /// as the continuation's cursor reads them. Returns the vector with the raw damage, the last
    /// impact and the ending life, so the next piece starts where this one ended.
    /// <paramref name="initialDebuffs"/> seeds the roll with an earlier subset's live marks, in this
    /// roll's frame, copied on entry; <paramref name="endingDebuffs"/> takes the marks live at the
    /// roll's end, in the same frame, which the beam threads into the next subset or piece beside the
    /// ending life. Both null is a standalone roll, which marks and prices debuffs only within itself.
    /// </summary>
    public static Valuation RollFixed(IReadOnlyList<(Attack Attack, int FireTick)> uses,
        IReadOnlyList<Target> targets, int horizon, PlanContext context, int searchTick,
        Dictionary<int, float>? initialRemaining = null, ICollection<(int Target, int Tick)>? killTicks = null,
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? initialDebuffs = null,
        IDictionary<(int Target, int Weapon), (float Chance, int Until)>? endingDebuffs = null)
    {
        var remaining = new Dictionary<int, float>();
        var facts = new Dictionary<int, Target>();
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs =
            initialDebuffs == null ? null : new Dictionary<(int Target, int Weapon), (float Chance, int Until)>(initialDebuffs);
        float encounterLife = 0f;
        foreach (Target target in targets)
        {
            remaining[target.Id] = initialRemaining != null && initialRemaining.TryGetValue(target.Id, out float left)
                ? Math.Max(0f, left) : target.Life;
            facts[target.Id] = target;
            encounterLife += Math.Max(0f, target.Life);
        }
        encounterLife = Math.Max(1f, encounterLife);
        float damage = 0f, threat = 0f, prevented = 0f, push = 0f, mana = 0f;
        int firstLanded = -1, lastImpact = 0;
        foreach ((Attack attack, int fireAt) in uses)
        {
            if (fireAt < 0 || fireAt >= horizon)
                continue;
            AttackParts parts = ValueParts(attack, fireAt, horizon, remaining, facts, ref debuffs, context,
                apply: true, searchTick, killTicks);
            damage += parts.Damage; threat += parts.Threat; prevented += parts.Prevented; push += parts.Push;
            mana += Math.Max(0f, attack.ManaCost);
            if (parts.Damage > 0f)
            {
                if (firstLanded < 0) firstLanded = parts.FirstImpact;
                lastImpact = Math.Max(lastImpact, parts.LastImpact);
            }
        }
        int duration = Math.Max(1, lastImpact);
        if (endingDebuffs != null && debuffs != null)
            foreach (var entry in debuffs)
                endingDebuffs[entry.Key] = entry.Value;
        return new Valuation(Assemble(damage, threat, prevented, push, mana, firstLanded, duration,
            context, horizon, encounterLife), damage, duration, remaining);
    }

    /// <summary>
    /// Two valuations in tick order combined into the whole plan's: damage over the joint span in the
    /// same encounter-life units, removal, prevention, push, harm and mana summed, the gap the caller's
    /// joint integral. <paramref name="secondOffsetTicks"/> carries the second piece's frame into the
    /// first's — its impact ticks count from its own start, so the joint span offsets them. First damage
    /// is the first piece's whenever it dealt any: the second's fraction is normalised to its own shorter
    /// horizon, so a minimum would read a later impact as earlier. The second must have been rolled from
    /// the first's ending life, so nothing is removed twice.
    /// Debuff marks cross the same way, but before the second rolls rather than here: the beam seeds the
    /// second's roll with the first's live marks, because a merge after rolling could not re-price the
    /// second's already-valued hits. Combine itself carries no marks, and needs none.
    /// </summary>
    public static Valuation Combine(Valuation first, Valuation second, IReadOnlyList<Target> targets,
        float companyGap, int secondOffsetTicks)
    {
        float encounterLife = 0f;
        foreach (Target target in targets)
            encounterLife += Math.Max(0f, target.Life);
        encounterLife = Math.Max(1f, encounterLife);
        int duration = Math.Max(1, Math.Max(first.LastImpactTick, secondOffsetTicks + second.LastImpactTick));
        var outcome = new CombatOutcome(
            DamagePerSecond: (first.DamageDealt + second.DamageDealt) / (duration / 60f) / encounterLife,
            ThreatRemoved: first.Outcome.ThreatRemoved + second.Outcome.ThreatRemoved,
            PlayerHarmPrevented: first.Outcome.PlayerHarmPrevented + second.Outcome.PlayerHarmPrevented,
            CompanionHarmTaken: first.Outcome.CompanionHarmTaken + second.Outcome.CompanionHarmTaken,
            PushDangerAdded: first.Outcome.PushDangerAdded + second.Outcome.PushDangerAdded,
            CompanyGap: Math.Max(0f, companyGap),
            TimeToFirstDamage: first.DamageDealt > 0f ? first.Outcome.TimeToFirstDamage : second.Outcome.TimeToFirstDamage,
            ManaSpent: first.Outcome.ManaSpent + second.Outcome.ManaSpent);
        return new Valuation(outcome, first.DamageDealt + second.DamageDealt, duration,
            new Dictionary<int, float>(second.RemainingLife));
    }

    /// <summary>One attack's raw parts: what it deals, removes, prevents and adds, and when its hits land.</summary>
    private readonly record struct AttackParts(float Damage, float Threat, float Prevented, float Push,
        int FirstImpact, int LastImpact);

    /// <summary>
    /// One attack as a marginal vector for the continuation choice: its damage over its own span, its removal
    /// and prevention, its push, its mana. The stand-level terms are zero here on purpose — every continuation
    /// from this stand shares the stand, so they cannot rank uses against each other.
    /// </summary>
    private static CombatOutcome Marginal(AttackParts parts, Attack attack, int fireAt, int horizon,
        PlanContext context, float encounterLife)
    {
        int span = Math.Max(1, attack.UseTicks + attack.ImpactTicks);
        return new CombatOutcome(
            DamagePerSecond: parts.Damage / (span / 60f) / encounterLife,
            ThreatRemoved: parts.Threat,
            PlayerHarmPrevented: parts.Prevented / Math.Max(1f, context.PlayerLife),
            CompanionHarmTaken: 0f,
            PushDangerAdded: parts.Push,
            CompanyGap: 0f,
            TimeToFirstDamage: parts.Damage > 0f ? Math.Clamp(parts.FirstImpact / (float)Math.Max(1, horizon), 0f, 1f) : 1f,
            ManaSpent: Math.Max(0f, attack.ManaCost) / Math.Max(1f, context.ManaPool));
    }

    private static AttackParts ValueParts(Attack attack, int fireAt, int horizon,
        Dictionary<int, float> remaining, Dictionary<int, Target> targets,
        ref Dictionary<(int Target, int Weapon), (float Chance, int Until)>? debuffs, PlanContext context, bool apply,
        int searchTick = 0, ICollection<(int Target, int Tick)>? killTicks = null)
    {
        int impact = fireAt + Math.Max(1, attack.ImpactTicks);
        if (impact >= horizon) return default;
        float timing = 1f - (float)impact / horizon;
        float damage = 0f, threat = 0f, prevented = 0f, push = 0f;
        int first = -1, last = impact;
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
            if (dealt <= 0f) continue;
            damage += dealt;
            float danger = Math.Clamp(target.Danger, 0f, 1f);
            if (dealt >= life)
            {
                threat += danger;
                prevented += Math.Max(0f, target.ExpectedHarm) * danger * timing;
                if (apply) killTicks?.Add((hit.Target, searchTick + impact));
            }
            else
            {
                threat += danger * (dealt / target.Life);
                prevented += Math.Max(0f, target.ExpectedHarm) * danger
                    * (dealt / target.Life) * Weights.AttackPartialHarmShare * timing;
                // Each half in its own body's life shares, the units prevention is in: an absolute push
                // beside a normalised prevention overprices the shove by the body's life, and the planner
                // then flies a hundred ticks round a pillar it can shoot past to avoid pushing a zombie a
                // few pixels toward the player. Discounted like the harm it accompanies, as the scalar is.
                float toCompanion = Math.Max(0f, hit.InducedDangerToCompanion);
                float toPlayer = Math.Max(0f, hit.InducedDanger - hit.InducedDangerToCompanion);
                push += (toPlayer / Math.Max(1f, context.PlayerLife)
                    + toCompanion / Math.Max(1f, context.CompanionLife)) * timing;
                if (apply && hit.DebuffChance > 0f && hit.DebuffTicks > 0)
                {
                    debuffs ??= new();
                    var key = (hit.Target, attack.Weapon);
                    float chance = debuffs.TryGetValue(key, out var held) && held.Until > impact
                        ? MathF.Max(held.Chance, hit.DebuffChance) : hit.DebuffChance;
                    debuffs[key] = (Math.Clamp(chance, 0f, 1f), impact + hit.DebuffTicks);
                }
            }
            if (first < 0) first = impact;
            if (apply) remaining[hit.Target] = life - dealt;
        }
        return new AttackParts(damage, threat, prevented, push, first, last);
    }

    private static void SpendThrow(Dictionary<int, int>? throws, int weapon)
    {
        if (throws != null && throws.ContainsKey(weapon))
            throws[weapon] = Math.Max(0, throws[weapon] - 1);
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
