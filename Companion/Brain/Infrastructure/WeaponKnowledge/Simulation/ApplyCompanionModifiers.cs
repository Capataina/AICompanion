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
    /// <summary>What this use carries from the companion's side. Empty until mastery lands.</summary>
    public static ModifierState Current() => ModifierState.None;
}
