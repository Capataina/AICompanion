using System;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>A pure consequence evaluator asks its observation owner to extend captured
/// enemy motion. Model revision is part of identity, so another law cannot reuse the answer.</summary>
public readonly record struct CourseEnemyMotionRequest(int Slot, long Generation, int Horizon, long ModelRevision)
{
    public FactKey Key => new("enemy-course-motion",
        FormattableString.Invariant($"{Slot}/ticks:{Horizon}/model:{ModelRevision}"), Generation);
}
