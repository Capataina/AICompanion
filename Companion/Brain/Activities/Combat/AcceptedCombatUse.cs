#nullable enable

using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Activities.Combat;

/// <summary>
/// The only combat-use authority the firing interaction may consume. It is deliberately a
/// one-use record: a live re-aim that changes tool or target must be accepted as another binding,
/// never silently overwrite the course's declared action.
/// </summary>
public sealed record AcceptedCombatUse(long BindingId, long SnapshotId, int TargetSlot, int TargetGeneration,
    int WeaponSlot, string UseId, int SegmentIndex, int UseIndex, string Method);
