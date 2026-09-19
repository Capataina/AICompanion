#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

public sealed record CapturedEnemyCourseMotion(int Slot, long Generation, int Type, int Width, int Height,
    int CoveredTicks, CoursePoint[] Centres, string Reason);

/// <summary>One native motion query rooted at an observed enemy and terrain revision.
/// Partial computation remains private; completed output is either modelled or unresolved.</summary>
public sealed class CaptureEnemyCourseMotion
{
    private readonly PredictObservedMotion.CapturedMotion motion;
    private readonly ITileWorld terrain;
    private readonly int terrainRevision, horizon, slot, type, width, height;
    private readonly long generation, modelRevision;
    private readonly ulong captureTick;
    private DecisionFact? result;

    public CaptureEnemyCourseMotion(NPC enemy, long generation, ITileWorld terrain, int horizon, long modelRevision)
        : this(CaptureCourseContactCensus.CaptureEnemy(enemy, generation), terrain, horizon, modelRevision) { }

    public CaptureEnemyCourseMotion(CapturedContactEnemy enemy, ITileWorld terrain, int horizon, long modelRevision,
        int? observationTerrainRevision = null)
    {
        if (horizon < 0 || horizon > PredictObservedMotion.MaximumForecastTicks) throw new ArgumentOutOfRangeException(nameof(horizon));
        if (enemy.Slot != enemy.Motion.Slot || enemy.Shape.Type != enemy.Motion.Type
            || enemy.Shape.Position.X != enemy.Motion.Position.X || enemy.Shape.Position.Y != enemy.Motion.Position.Y)
            throw new ArgumentException("Enemy geometry and motion must come from the same captured body.", nameof(enemy));
        this.terrain = terrain; terrainRevision = observationTerrainRevision ?? terrain.Revision;
        captureTick = enemy.Motion.Tick;
        this.horizon = horizon; generation = enemy.Generation; this.modelRevision = modelRevision;
        slot = enemy.Slot; type = enemy.Shape.Type; width = enemy.Shape.Width; height = enemy.Shape.Height;
        motion = PredictObservedMotion.RestoreCaptured(enemy.Motion, width, height);
        Key = new("enemy-course-motion", FormattableString.Invariant($"{slot}/ticks:{horizon}"), generation);
    }

    public FactKey Key { get; }
    public int CoveredTicks => motion.CoveredTicks;
    internal bool BelongsTo(ITileWorld world, int revision, ulong tick)
        => ReferenceEquals(terrain, world) && terrainRevision == revision && captureTick == tick;
    public DecisionFact? Continue(DecisionWorkBudget budget, long maximumOperations = long.MaxValue)
    {
        if (result != null) return result;
        if (maximumOperations <= 0) return null;
        int sliceEnd = (int)Math.Min(horizon, motion.CoveredTicks + Math.Min(maximumOperations, horizon));
        motion.Continue(sliceEnd, budget);
        bool completed = motion.CoveredTicks == horizon;
        // Test against the original observation, including cells first read in this slice.
        // Missing edit history cannot certify a frozen-world prediction either.
        if (terrain.ChangedSince(terrainRevision, motion.ReadContains) != TerrainEditVerdict.Unchanged)
            return Finish(FactEvidence.Unresolved, "enemy-motion-terrain-changed");
        return completed ? Finish(FactEvidence.Modelled, "captured-native-motion") : null;
    }

    private DecisionFact Finish(FactEvidence evidence, string reason)
    {
        var captured = new CapturedEnemyCourseMotion(slot, generation, type, width, height, motion.CoveredTicks,
            motion.Samples.Select(point => new CoursePoint(point.X, point.Y)).ToArray(), reason);
        return result = new(Key, modelRevision, new(Text: JsonSerializer.Serialize(captured)), evidence);
    }
}
