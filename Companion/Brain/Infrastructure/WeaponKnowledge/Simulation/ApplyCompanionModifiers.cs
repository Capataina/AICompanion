#nullable enable

namespace AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

/// <summary>What the companion applied to a use at spawn: extra projectiles and added pierce. Recorded on every
/// companion trace, so no learner can absorb a modifier into a type it does not belong to.</summary>
public readonly record struct ModifierState(int ExtraProjectiles, int AddedPierce)
{
    public static readonly ModifierState None = new(0, 0);
    public bool IsNone => ExtraProjectiles == 0 && AddedPierce == 0;
}

/// <summary>
/// The one seam companion-side changes to a use enter by. It reads the mastery bonuses record when that exists
/// and nothing until then, so in phase C every use is unmodified and the seam is the recorded state plus the
/// learners' exclusion of modified traces. Phase G wires the bonuses and the extra spawns' spacing.
/// </summary>
public static class ApplyCompanionModifiers
{
    /// <summary>
    /// A planted state the fixtures set, and production never does. Mastery bonuses do not exist yet —
    /// the wheel is still a preview — so the seam is this plant plus a read that stays empty until a
    /// bonuses record exists. S5 plants +1 extra projectile; the live companion reads None.
    /// </summary>
    public static ModifierState Planted { get; set; } = ModifierState.None;

    /// <summary>What this use carries from the companion's side: the plant if one is set, otherwise none until mastery grants pierce or extra projectiles.</summary>
    public static ModifierState Current() => Planted;
}
