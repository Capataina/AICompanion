#nullable enable

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// One plan's outcome as eight objectives in natural units, so a weight means the same thing in every
/// fight. Min–max normalisation across a decision's candidates was rejected for exactly that reason: it
/// would make a weight's meaning change with whichever candidates happened to be generated.
///
/// Finishing low-health enemies is not a separate objective: a wound that kills removes all of that body's
/// threat, so <see cref="ThreatRemoved"/> already prices it above a wound that does not.
/// </summary>
public readonly record struct CombatOutcome(
    /// <summary>Higher: health-capped damage over the plan, per second, in units of the encounter's life at risk.</summary>
    float DamagePerSecond,
    /// <summary>Higher: over bodies, danger times fraction of life removed, kills counted in full.</summary>
    float ThreatRemoved,
    /// <summary>Higher: expected hits on the player that removed bodies no longer land, in units of his life.</summary>
    float PlayerHarmPrevented,
    /// <summary>Lower: predicted damage to the body at its stands and along its travel, in units of its life.</summary>
    float CompanionHarmTaken,
    /// <summary>Lower: danger the plan's pushes add toward either body, in threat-urgency times hit-cost units.</summary>
    float PushDangerAdded,
    /// <summary>Lower: time-integrated distance outside the player's predicted intent region, in units of the region's size times the horizon.</summary>
    float CompanyGap,
    /// <summary>Lower: travel plus cooldown plus flight to the first landed hit, in units of the horizon.</summary>
    float TimeToFirstDamage,
    /// <summary>Lower: mana the plan's uses spend, in units of the pool.</summary>
    float ManaSpent)
{
    /// <summary>Each objective's measurement noise: two plans closer than this on an objective are not different plans.</summary>
    public static CombatOutcome Tolerances => new(
        DamagePerSecond: 0.005f,
        ThreatRemoved: 0.01f,
        PlayerHarmPrevented: 0.005f,
        CompanionHarmTaken: 0.005f,
        PushDangerAdded: 0.01f,
        CompanyGap: 0.01f,
        TimeToFirstDamage: 0.005f,
        ManaSpent: 0.005f);

    /// <summary>The objective's value by index, in declaration order, for the dominance filter and the sweeps.</summary>
    public float this[int index] => index switch
    {
        0 => DamagePerSecond,
        1 => ThreatRemoved,
        2 => PlayerHarmPrevented,
        3 => CompanionHarmTaken,
        4 => PushDangerAdded,
        5 => CompanyGap,
        6 => TimeToFirstDamage,
        _ => ManaSpent,
    };

    public const int Count = 8;

    /// <summary>Whether a larger value is better on the objective at this index; the first three maximise, the rest minimise.</summary>
    public static bool HigherIsBetter(int index) => index <= 2;
}
