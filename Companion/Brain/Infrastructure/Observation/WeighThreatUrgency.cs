#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// The one rule that turns a threat's hit, its body's remaining life and its arrival time into urgency, as pure
/// arithmetic over values the caller already holds. The threat sense reads it about where each hostile is; the
/// arsenal reads it about where a hit's push would leave the hostile, so the danger a push adds is measured on the
/// same scale and by the same rule as the danger the hostile already carries — a second copy of this arithmetic
/// would drift from the first the next time either was tuned, and the difference between them would be charged
/// to a shot as danger nobody caused.
///
/// weight(effective damage against a quarter of life left, boss) × closeness(ticks to the body) × sight factor.
/// Closeness is 1 at contact and fades to 0 around six seconds out; a shooter with a sight line is treated as
/// already there, and a body the hostile cannot see is half as urgent.
/// </summary>
public static class ThreatUrgency
{
    /// <summary>
    /// How heavily one hit weighs on the body it would land on: a hit removing a quarter of the life that body has
    /// left is full weight, so four such hits would down it. Against remaining life rather than maximum, the same
    /// attack weighs more on a body already hurt, which is what lets a small attack be tolerable at full health and
    /// worth escaping at low health. At full health with no defence this equals the raw-damage-over-a-quarter-of-
    /// maximum share it replaced, so every threshold tuned against that share keeps its meaning for an unhurt,
    /// unarmoured body. The 0.2 floor arrived with the repository's import and no recorded reason; it keeps a
    /// near-harmless hostile from reading as no threat at all.
    /// </summary>
    public static float DamageShare(float effectiveDamage, int lifeLeft)
        => MathHelper.Clamp(effectiveDamage / MathF.Max(1f, lifeLeft * 0.25f), 0.2f, 1f);

    /// <summary>1 at contact, 0 at six seconds out, linear between.</summary>
    public static float Closeness(float ticksToBody) => MathHelper.Clamp(1f - ticksToBody / 360f, 0f, 1f);

    /// <summary>
    /// Urgency to the player. A shooter with a sight line keeps most of its urgency at any range, so there is no
    /// early exit on closeness here, unlike <see cref="ToCompanion"/>.
    /// </summary>
    public static float ToPlayer(float effectiveDamage, int lifeLeft, bool isBoss, float ticksToBody, bool shoots, bool sees)
    {
        float weight = isBoss ? 1f : DamageShare(effectiveDamage, lifeLeft);
        float closeness = Closeness(ticksToBody);
        if (shoots && sees)
            closeness = MathF.Max(closeness, 0.8f);
        return weight * closeness * (sees ? 1f : 0.5f);
    }

    /// <summary>
    /// Urgency to the companion. A threat out of closeness range is no urgency even if it shoots and sees, because
    /// the threat sense decides whether to pay for the companion's sight test only after closeness says it could
    /// matter; a sight the sense never tested cannot be allowed to count, so the rule returns before reading it.
    /// </summary>
    public static float ToCompanion(float effectiveDamage, int lifeLeft, bool isBoss, float ticksToBody, bool shoots, bool sees)
    {
        float closeness = Closeness(ticksToBody);
        if (closeness <= 0f)
            return 0f;
        float weight = isBoss ? 1f : DamageShare(effectiveDamage, lifeLeft);
        if (shoots && sees)
            closeness = MathF.Max(closeness, 0.8f);
        return weight * closeness * (sees ? 1f : 0.5f);
    }
}
