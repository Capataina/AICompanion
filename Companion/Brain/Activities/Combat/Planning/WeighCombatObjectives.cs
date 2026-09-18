#nullable enable

using System;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// One weight per objective, all non-negative magnitudes; the sign lives in <see cref="CombatWeights.Weighted"/>,
/// which adds what the vector maximises and subtracts what it minimises.
/// </summary>
public readonly record struct CombatWeights(float Damage, float ThreatRemoved, float PlayerHarmPrevented,
    float CompanionHarm, float PushDanger, float CompanyGap, float TimeToFirstDamage, float Mana)
{
    public float Weighted(CombatOutcome outcome)
        => Damage * outcome.DamagePerSecond
            + ThreatRemoved * outcome.ThreatRemoved
            + PlayerHarmPrevented * outcome.PlayerHarmPrevented
            - CompanionHarm * outcome.CompanionHarmTaken
            - PushDanger * outcome.PushDangerAdded
            - CompanyGap * outcome.CompanyGap
            - TimeToFirstDamage * outcome.TimeToFirstDamage
            - Mana * outcome.ManaSpent;

    /// <summary>The weight by objective index, in <see cref="CombatOutcome"/> order, for the sweeps.</summary>
    public float this[int index] => index switch
    {
        0 => Damage,
        1 => ThreatRemoved,
        2 => PlayerHarmPrevented,
        3 => CompanionHarm,
        4 => PushDanger,
        5 => CompanyGap,
        6 => TimeToFirstDamage,
        _ => Mana,
    };

    /// <summary>The same weights with one objective's magnitude scaled, for the audit's weight sweeps.</summary>
    public CombatWeights Scaled(int index, float factor) => index switch
    {
        0 => this with { Damage = Damage * factor },
        1 => this with { ThreatRemoved = ThreatRemoved * factor },
        2 => this with { PlayerHarmPrevented = PlayerHarmPrevented * factor },
        3 => this with { CompanionHarm = CompanionHarm * factor },
        4 => this with { PushDanger = PushDanger * factor },
        5 => this with { CompanyGap = CompanyGap * factor },
        6 => this with { TimeToFirstDamage = TimeToFirstDamage * factor },
        _ => this with { Mana = Mana * factor },
    };
}

/// <summary>
/// The built-in weights from the senses. The shapes are the plan's: prevention and time rise with the
/// player's danger, because a late save is no save; company answers travel and falls as danger rises,
/// because defending him is company; damage and threat fall as the encounter dies; the companion's own
/// harm answers his missing life and his own danger; mana answers the pool's emptiness. Push proximity
/// needs no shape here — the adapter's induced danger already re-weighs urgency at the pushed body.
///
/// Neither prevention nor push can carry the scalar's weight: their vector units divide the
/// scalar's life by the bodies', which prices a full save at two against a full kill near seven and a
/// shove at a hundredth of the save it undoes. Prevention carries nine restoring rescue-over-killing,
/// and push carries nine beside it, keeping the scalar's one-for-one break-even between a shove and a
/// save. The audit's sweeps tune both.
/// </summary>
public static class WeighCombatObjectives
{
    /// <summary>The weights for this tick's senses: the one place the activity, the search and the travelling hands agree on what matters.</summary>
    public static CombatWeights ForSenses(in ActionContext ctx)
    {
        var threats = ctx.Senses.Threats;
        float total = 0f, alive = 0f, playerTotal = 0f, playerAddressed = 0f;
        foreach (ThreatRecord threat in threats.Threats)
        {
            if (!threat.IsChainRepresentative)
                continue;
            float danger = MathF.Max(threat.Urgency, threat.UrgencyToCompanion);
            total += danger;
            if (threat.Npc != null && threat.Npc.active && threat.Npc.life > 0)
                alive += danger;
            float toPlayer = MathF.Max(0f, threat.Urgency);
            playerTotal += toPlayer;
            if (threat.Npc != null && threat.Npc.active && threat.Npc.life > 0 && threat.Npc.CanBeChasedBy())
                playerAddressed += toPlayer;
        }
        float missing = ctx.Npc.lifeMax > 0 ? 1f - ctx.Npc.life / (float)ctx.Npc.lifeMax : 0f;
        float mana = ctx.Companion.Mana.Max > 0 ? ctx.Companion.Mana.Current / ctx.Companion.Mana.Max : 1f;
        return For(threats.PlayerDanger, threats.CompanionDanger, missing,
            ctx.Senses.Intent.Region.IsTravelling, total > 0f ? alive / total : 1f, mana,
            playerTotal > 0f ? playerAddressed / playerTotal : 1f);
    }

    /// <param name="playerDanger">The threat sense's danger to the player, 0..1.</param>
    /// <param name="companionDanger">The threat sense's danger to the body, 0..1.</param>
    /// <param name="missingLifeShare">The companion's missing life as a share of his maximum.</param>
    /// <param name="playerTravelling">Whether the intent region is leading the player somewhere.</param>
    /// <param name="dangerAliveShare">The share of the encounter's danger still on living bodies.</param>
    /// <param name="manaShare">The mana pool's current fill as a share of its maximum.</param>
    /// <param name="dangerAddressedShare">The share of the player's danger on bodies the companion can chase:
    /// waiting is only bad for the danger waiting could answer, so an unchaseable lethal beside him must not
    /// tax a slow plan against what is chaseable the way his own danger does.</param>
    public static CombatWeights For(float playerDanger, float companionDanger, float missingLifeShare,
        bool playerTravelling, float dangerAliveShare, float manaShare, float dangerAddressedShare = 1f)
    {
        float danger = Math.Clamp(playerDanger, 0f, 1f);
        float alive = Weights.CombatAliveShareFloor + (1f - Weights.CombatAliveShareFloor) * Math.Clamp(dangerAliveShare, 0f, 1f);
        float addressed = Math.Clamp(dangerAddressedShare, 0f, 1f);
        float companyTravel = playerTravelling ? Weights.CombatCompanyTravelFactor : 1f / Weights.CombatCompanyTravelFactor;
        return new CombatWeights(
            Damage: Weights.CombatWeightDamage * alive,
            ThreatRemoved: Weights.CombatWeightThreatRemoved * alive,
            PlayerHarmPrevented: Weights.CombatWeightPlayerHarmPrevented * (1f + Weights.CombatDangerWeightLift * danger),
            CompanionHarm: Weights.CombatWeightCompanionHarm * (0.5f + Math.Clamp(missingLifeShare, 0f, 1f) + Math.Clamp(companionDanger, 0f, 1f)),
            PushDanger: Weights.CombatWeightPushDanger,
            CompanyGap: Weights.CombatWeightCompanyGap * companyTravel / (1f + Weights.CombatDangerWeightLift * danger),
            TimeToFirstDamage: Weights.CombatWeightTimeToFirstDamage * (1f + Weights.CombatDangerWeightLift * danger * addressed),
            Mana: Weights.CombatWeightMana * (0.25f + 0.75f * (1f - Math.Clamp(manaShare, 0f, 1f))));
    }
}
