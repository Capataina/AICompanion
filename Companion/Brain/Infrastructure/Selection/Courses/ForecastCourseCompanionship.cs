#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

public readonly record struct CourseTravelRequest(CoursePoint From, CoursePoint Velocity, CoursePoint To)
{
    public FactKey Key => ReadCourseTravel.Key(From, Velocity, To);
}

public sealed record CourseCompanionshipResult(ProjectionStatus Status,
    IReadOnlyList<CompanionshipInterval> Intervals, double EndTick, bool NominallyRejoined,
    DependencyManifest Dependencies, string Reason, IReadOnlyList<TimedCoursePose> BodyTrajectory);

/// <summary>One frozen course's spatial cost, including the proposed return leg. It requests
/// captured native travel rather than performing search inside hypothetical evaluation.</summary>
public sealed class ForecastCourseCompanionship
{
    private readonly StepBinding[] steps;
    private readonly CoursePoint reunionPose;
    private readonly List<CompanionshipInterval> intervals = new();
    private readonly List<TimedCoursePose> trajectory = new();
    private readonly Dictionary<FactKey, FactRead> reads = new();
    private DecisionFactSnapshot snapshot;
    private CapturedCompanionshipRegion? region;
    private CoursePoint pose, velocity;
    private double tick;
    private int leg, sample;
    private CapturedCourseTravel? travel;
    private TimedCoursePose[]? samples;
    private CourseCompanionshipResult? terminal;

    public ForecastCourseCompanionship(DecisionFactSnapshot snapshot, IEnumerable<StepBinding> steps,
        CoursePoint initialPose, CoursePoint initialVelocity, CoursePoint reunionPose, double startTick = 0)
    {
        if (!double.IsFinite(startTick) || startTick < 0) throw new ArgumentOutOfRangeException(nameof(startTick));
        this.snapshot = snapshot; this.steps = steps.ToArray(); this.reunionPose = reunionPose;
        if (this.steps.Any(step => step.WorldEpoch != snapshot.WorldEpoch || step.SnapshotId != snapshot.Id || !step.SufficientlyModelled))
            throw new ArgumentException("Companionship forecasting requires bindings from the frozen snapshot.", nameof(steps));
        pose = initialPose; velocity = initialVelocity; tick = startTick;
        trajectory.Add(new(tick, pose, velocity));
    }

    public CourseTravelRequest? MissingTravel { get; private set; }

    public CourseCompanionshipResult Continue(DecisionFactSnapshot facts, DecisionWorkBudget budget)
    {
        if (!ReferenceEquals(facts, snapshot) && !facts.IsModelExtensionOf(snapshot))
            throw new InvalidOperationException("Companionship forecast inputs may only append completed model answers.");
        snapshot = facts;
        if (terminal != null) return terminal;
        if (region == null)
        {
            if (!budget.TrySpend("course-companionship-input")) return Result(ProjectionStatus.Pending, "budget-cut");
            var input = Read(CapturedCompanionshipRegion.Key);
            if (input.Evidence != FactEvidence.Observed)
                return terminal = Result(ProjectionStatus.Rejected, "companionship-input-unresolved");
            region = JsonSerializer.Deserialize<CapturedCompanionshipRegion>(input.Value.Text);
        }
        while (leg <= steps.Length)
        {
            bool returning = leg == steps.Length;
            CoursePoint destination = returning ? reunionPose : steps[leg].Pose;
            if (travel == null)
            {
                if (!budget.TrySpend("course-companionship-travel")) return Result(ProjectionStatus.Pending, "budget-cut");
                var request = new CourseTravelRequest(pose, velocity, destination);
                var fact = Read(request.Key);
                if (fact.Evidence == FactEvidence.Missing)
                {
                    MissingTravel = request;
                    return Result(ProjectionStatus.Pending, "travel-query-pending");
                }
                MissingTravel = null;
                if (fact.Evidence != FactEvidence.Modelled)
                    return terminal = Result(ProjectionStatus.Rejected, "travel-query-unresolved");
                travel = JsonSerializer.Deserialize<CapturedCourseTravel>(fact.Value.Text);
                if (travel == null || travel.From != pose || travel.InitialVelocity != velocity || travel.To != destination
                    || !double.IsFinite(travel.Ticks) || travel.Ticks < 0)
                    throw new InvalidOperationException("Companionship travel disagrees with its captured query.");
                if (travel.Admission != OpportunityAdmission.KnownUsable)
                    return terminal = Result(ProjectionStatus.Rejected, travel.Reason);
                if (!returning && travel.Ticks != steps[leg].TravelTicks)
                    return terminal = Result(ProjectionStatus.Rejected, "bound-travel-duration-changed");
                samples = travel.TimedRouteSamples?.ToArray();
                if (samples == null || samples.Length == 0 || samples[0].Tick != 0 || samples[0].Position != pose
                    || samples[^1].Tick != travel.Ticks
                    || samples.Zip(samples.Skip(1)).Any(pair => pair.First.Tick >= pair.Second.Tick))
                    return terminal = Result(ProjectionStatus.Rejected, "timed-travel-evidence-unresolved");
                if (!returning && samples[^1].Position != steps[leg].ArrivalPose)
                    return terminal = Result(ProjectionStatus.Rejected, "bound-arrival-pose-changed");
                sample = 0;
            }
            while (sample + 1 < samples!.Length)
            {
                if (!budget.TrySpend("course-companionship-interval")) return Result(ProjectionStatus.Pending, "budget-cut");
                intervals.AddRange(ForecastCompanionshipGap.Between(samples[sample] with { Tick = tick + samples[sample].Tick },
                    samples[sample + 1] with { Tick = tick + samples[sample + 1].Tick }, region.Value));
                sample++;
                AppendPose(samples[sample] with { Tick = tick + samples[sample].Tick });
            }
            if (!budget.TrySpend("course-companionship-use")) return Result(ProjectionStatus.Pending, "budget-cut");
            tick += travel.Ticks;
            if (returning)
            {
                // Native arrival has slack. Test the captured body's final point rather
                // than granting reunion merely because the requested destination was inside.
                pose = samples[^1].Position;
                double projected = Math.Min(tick, region.Value.TravelHorizon);
                double x = pose.X - region.Value.Centre.X - region.Value.Travel.X * projected;
                double y = pose.Y - region.Value.Centre.Y - region.Value.Travel.Y * projected;
                bool inside = !region.Value.PlayerAlive || (Math.Abs(x) <= region.Value.HalfSize.X && Math.Abs(y) <= region.Value.HalfSize.Y);
                return terminal = Result(ProjectionStatus.Complete, inside ? "nominal-reunion" : "return-outside-projected-region", inside);
            }
            var step = steps[leg];
            intervals.AddRange(ForecastCompanionshipGap.Between(new(tick, step.ArrivalPose, step.ArrivalVelocity),
                new(tick + step.UseTicks, step.ArrivalPose, step.ArrivalVelocity), region.Value));
            tick += step.UseTicks; pose = step.ArrivalPose; velocity = step.ArrivalVelocity;
            AppendPose(new(tick, pose, velocity));
            leg++; travel = null; samples = null;
        }
        throw new InvalidOperationException("A companionship forecast must finish through its return leg.");
    }

    private DecisionFact Read(FactKey key)
    {
        if (!snapshot.TryRead(key, out var fact)) fact = new(key, -1, default, FactEvidence.Missing);
        reads[key] = new(key, fact.Version, fact.Digest, fact.Evidence);
        return fact;
    }
    private CourseCompanionshipResult Result(ProjectionStatus status, string reason, bool inside = false)
        => new(status, Array.AsReadOnly(intervals.ToArray()), tick, inside, new(reads.Values), reason,
            Array.AsReadOnly(trajectory.ToArray()));

    private void AppendPose(TimedCoursePose next)
    {
        if (trajectory[^1].Tick == next.Tick)
        {
            if (trajectory[^1].Position != next.Position)
                throw new InvalidOperationException("A course trajectory cannot change position without elapsed time.");
            trajectory[^1] = next;
        }
        else trajectory.Add(next);
    }
}
