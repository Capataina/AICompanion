#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// The native consequence provider: what a course costs, priced from captured facts.
///
/// It prices two terms. Companionship and the return leg run for real through
/// <see cref="ForecastCourseCompanionship"/>. Predicted contact harm to the <em>companion</em> runs for
/// real too, over the body trajectory that forecast produces: every hostile in the frozen census is
/// asked for modelled motion through <see cref="CourseEnemyMotionRequest"/>, that motion becomes timed
/// melee geometry through <see cref="ProjectMeleeContactGeometry"/>, and first contact against the body's
/// own per-tick boxes becomes a <see cref="PredictedHarm"/> event through <see cref="ForecastContactHarm"/>.
/// That is what makes a course comparable on safety at all: until it existed every candidate carried the
/// same unknown harm, so a route through a pack and a route around it priced identically.
///
/// <b>The tail stays unresolved on every course regardless, and that is deliberate rather than
/// left over.</b> Harm to the <em>player</em> is not priced here — the companion's own body trajectory is
/// the only one this forecast models, and the player's future path is not a thing the course decides — so
/// <see cref="ForecastContactHarm"/> is constructed with an incomplete census on purpose and can never
/// certify that a course is harm-free. An unresolved tail cannot certify strict superiority, so a course
/// may be chosen as a legal first action and can never be <em>proven</em> better than a safer rival.
/// Resolving it would mean claiming an empty harm list is the truth, which is the zero-cost completed
/// candidate the Courses contract forbids by name: every comparison would read "no predicted harm" and
/// the search would actively prefer the most dangerous course on the board.
///
/// So this is a two-kind pending-request state machine. Travel suspends through
/// <see cref="CourseProjectionResult.RequiredTravel"/>; enemy motion suspends through
/// <see cref="CourseProjectionResult.RequiredEnemyMotion"/>; both resume the same candidate on the
/// model-extended snapshot rather than restarting it. Every retained piece is keyed on the order it
/// belongs to, for the reason the leak below records.
/// </summary>
public sealed class ForecastCourseConsequences : ICourseConsequenceForecast
{
    /// <summary>The prediction law this forecast asks enemy motion under. It is part of the model fact's
    /// identity, so a later law cannot silently reuse an answer computed by this one; bump it whenever
    /// <see cref="PredictObservedMotion"/>'s continuation changes what it would return for one enemy.</summary>
    public const long MotionModelRevision = 1;

    private readonly CoursePoint reunionPose;
    private ForecastCourseCompanionship? companionship;
    /// <summary>The order the retained leg belongs to. A companionship leg is only ever valid for the
    /// exact steps and starting state it was built from, so it is keyed on them and rebuilt when they
    /// differ, rather than on a caller remembering to say a new order started.</summary>
    private string? orderKey;

    // The harm pass's own retained state, cleared with the companionship leg because all of it is
    // derived from that leg's trajectory. A geometry projection holds a partly-computed sample list and
    // the harm forecast holds a cursor over actors, ticks and threats, so both resume after a budget cut
    // rather than recomputing from tick zero on every slice.
    private readonly Dictionary<(int Slot, long Generation), ProjectMeleeContactGeometry> geometries = new();
    private readonly List<ContactThreat> threats = new();
    private readonly List<FactRead> harmReads = new();
    private ForecastContactHarm? contact;
    private SampleContactTrajectory? trajectory;
    private bool motionUnresolved;
    private int harmHorizon;

    /// <summary>The travel this forecast is waiting on, for the observation owner's model queue. Null
    /// when nothing is outstanding; the domain cursor stays on the candidate that needs it, so a
    /// completed answer resumes that candidate rather than finding enumeration has moved past it.</summary>
    public CourseTravelRequest? MissingTravel => companionship?.MissingTravel;

    /// <param name="reunionPose">Where the companion is judged to return to. The forecast deliberately
    /// does not choose this — it prices a return to a destination someone else names — so the caller
    /// supplies the player's captured region rather than letting the cost model invent a home.</param>
    public ForecastCourseConsequences(CoursePoint reunionPose) => this.reunionPose = reunionPose;

    public CourseProjectionResult Continue(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor,
        DecisionFactSnapshot facts, CourseComparisonEpisode episode, DecisionWorkCursor cursor,
        DecisionWorkBudget budget)
    {
        // The retained leg belongs to one candidate order and must never be handed to the next one.
        //
        // This is keyed rather than reset by a caller because a caller forgot. The first version of this
        // class exposed a BeginOrder() method for the binder to call, the binder never called it — the
        // method is not on ICourseConsequenceForecast and could not be — and ForecastCourseCompanionship
        // returns its cached terminal result the instant it has one. So every order after the first in a
        // search was handed the first order's intervals, end tick, reunion verdict and dependency
        // manifest verbatim, priced from a pose it had never seen and asking for no travel to reach it.
        // With harm unresolved, companionship is the only cost term there is, so that made every
        // candidate order cost the same and reduced the search to useful effects with no separation cost.
        // A key cannot be forgotten; a call can.
        string key = OrderKey(steps, successor);
        if (orderKey != key) { companionship = null; orderKey = key; ForgetHarm(); }
        companionship ??= new ForecastCourseCompanionship(facts, steps, successor.Pose, successor.Velocity,
            reunionPose, successor.Tick);

        CourseCompanionshipResult company = companionship.Continue(facts, budget);
        if (company.Status == ProjectionStatus.Pending)
            // A suspended leg forwards its typed travel request rather than merely reporting that it
            // stopped. The observation owner answers it and the same candidate resumes; dropping the
            // request would leave the domain cursor parked on a candidate nobody is completing.
            return new(ProjectionStatus.Pending, null, company.Reason,
                companionship.MissingTravel is { } travel ? new[] { travel } : null);

        // Harm to the companion, over the very trajectory companionship just walked.
        HarmPass harm = PriceCompanionHarm(facts, company, successor, budget);
        if (harm.Pending)
            return new(ProjectionStatus.Pending, null, harm.Reason, null, harm.RequiredMotion);

        // The manifest carries what the pricing actually read. An empty one asserts a calculation with
        // no captured inputs, which publication is entitled to believe — so companionship's manifest and
        // the harm pass's reads go over together, which is what lets a changed region, travel fact,
        // census, victim capture or enemy-motion answer dirty this cost later.
        var dependencies = new DependencyManifest(company.Dependencies.Reads.Concat(harmReads));
        // The tail is unresolved on every course, and the reason is the player rather than the census:
        // this pass models the companion's body and nothing models the player's, so no course here can
        // ever be certified harm-free. See the class comment for why resolving it would be the worse lie.
        var projection = new CourseProjection(steps, harm.Harm, company.Intervals,
            company.EndTick, company.NominallyRejoined, tailUnresolved: true,
            consequenceDependencies: dependencies);
        return new(ProjectionStatus.Complete, projection, harm.Reason);
    }

    /// <summary>One harm pass's outcome. <paramref name="Pending"/> and a non-empty
    /// <paramref name="RequiredMotion"/> are the suspension; otherwise the harm list is final for this
    /// order, however much of it the census and the model queue were able to answer.</summary>
    private readonly record struct HarmPass(bool Pending, IReadOnlyList<PredictedHarm> Harm, string Reason,
        IReadOnlyList<CourseEnemyMotionRequest>? RequiredMotion = null);

    private static HarmPass Suspend(string reason, IReadOnlyList<CourseEnemyMotionRequest> motion)
        => new(true, Array.Empty<PredictedHarm>(), reason, motion);
    private static HarmPass Priced(IReadOnlyList<PredictedHarm> harm, string reason)
        => new(false, harm, reason);

    /// <summary>
    /// Predicted contact harm to the companion over the trajectory companionship just produced.
    ///
    /// Two reads gate it and neither is treated as an absence. A census or a victim capture the tick's
    /// allowance could not produce leaves harm unpriced rather than proven zero, because the whole point
    /// of the unresolved tail is that a missing answer must never read as safety. Per hostile, an absent
    /// motion fact is a typed request rather than a verdict; an <c>Unresolved</c> one — a terrain edit
    /// inside what the simulation read, or a horizon nobody finished — drops that hostile from the
    /// threat list, which understates harm and is why the tail could not be resolved even with a
    /// complete census.
    /// </summary>
    private HarmPass PriceCompanionHarm(DecisionFactSnapshot facts, CourseCompanionshipResult company,
        ProjectedCourseState successor, DecisionWorkBudget budget)
    {
        if (contact == null)
        {
            // This block re-runs on every resume until the forecast exists, so the two lists it fills
            // are emptied rather than appended to. Leaving them would give ForecastContactHarm the same
            // hostile slot twice after a budget cut, which it refuses outright; the retained geometries
            // survive that reset on purpose, because each one holds a partly-computed sample list.
            threats.Clear(); harmReads.Clear();

            // The horizon is the whole journey including the return leg, capped by how far the native
            // predictor will model at all. A course longer than the cap is priced over its first stretch
            // and stays unresolved past it, which the tail already says.
            harmHorizon = (int)Math.Clamp(Math.Ceiling(company.EndTick), 0, PredictObservedMotion.MaximumForecastTicks);

            if (Read(facts, CapturedContactCensus.Key) is not { Evidence: FactEvidence.Observed } censusFact
                || JsonSerializer.Deserialize<CapturedContactCensus>(censusFact.Value.Text) is not { } census)
                return Priced(Array.Empty<PredictedHarm>(), "companionship-priced;contact-census-unread");
            if (Read(facts, CapturedContactVictim.Key(HarmActor.Companion)) is not { Evidence: FactEvidence.Observed } victimFact
                || JsonSerializer.Deserialize<CapturedContactVictim>(victimFact.Value.Text) is not { } victim)
                return Priced(Array.Empty<PredictedHarm>(), "companionship-priced;contact-victim-unread");

            // The purpose-built sampler, not a resampling loop of this class's own.
            //
            // The first version of this method hand-rolled one, because a search for this type failed on
            // a shell glob and the empty result was read as absence. It is better than the hand-rolled
            // one in the way that matters: it stops at the last tick the trajectory actually covers and
            // reports incomplete, where the inline version clamped to the final pose and reported a body
            // standing still for the rest of the horizon. `ForecastContactHarm` reads a short box list as
            // an unresolved tail, so a course whose trajectory runs out is honestly unpriced past that
            // point instead of being priced against a body that is not there.
            //
            // It requires the trajectory to start at tick 0, which is its way of saying the timeline is
            // expressed from the projection's own origin. That holds because the companionship leg is
            // built from the state the course starts from; `BindCourseOrder` used to hand the state it
            // ends at, and the comment at that call site owns why.
            trajectory ??= new SampleContactTrajectory(company.BodyTrajectory, victim.Width, victim.Height, harmHorizon);
            if (trajectory.Continue(budget) is not { } sampled)
                return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
            IReadOnlyList<ContactBox> boxes = sampled.Boxes;

            // Every absent motion answer is collected before suspending rather than one per call. A
            // course beside six hostiles would otherwise take six full suspend-and-resume round trips
            // through the model queue, each re-walking the companionship leg to get back here.
            var missing = new List<CourseEnemyMotionRequest>();
            foreach (CapturedContactEnemy enemy in census.Enemies)
            {
                var request = new CourseEnemyMotionRequest(enemy.Slot, enemy.Generation, harmHorizon, MotionModelRevision);
                // The read happens before the retained-geometry skip, and the order is the whole point.
                // `harmReads` is cleared on every re-entry while the forecast is still being built, so a
                // motion fact whose geometry was constructed in an earlier slice — a budget cut, or a
                // queue at capacity answering only some hostiles per slice — was read once, recorded
                // once, and then skipped over on every later pass, leaving it out of the published
                // manifest entirely. A cost that omits an input it consumed cannot be dirtied when that
                // input changes, which is exactly what the manifest exists to make possible.
                DecisionFact fact = Read(facts, request.Key);
                if (geometries.ContainsKey((enemy.Slot, enemy.Generation))) continue;
                if (fact.Evidence == FactEvidence.Missing) { missing.Add(request); continue; }
                if (fact.Evidence != FactEvidence.Modelled
                    || JsonSerializer.Deserialize<CapturedEnemyCourseMotion>(fact.Value.Text) is not { } motion)
                {
                    motionUnresolved = true;
                    continue;
                }
                // Keyed by slot *and* generation, the identity every other component in this pipeline
                // uses — the request key carries the generation, and the model owner indexes its census
                // the same way. Slot alone is safe only while nothing retains geometry across two
                // censuses, and the first path that does would hand a recycled slot a dead hostile's
                // geometry with nothing to notice.
                geometries[(enemy.Slot, enemy.Generation)] = new ProjectMeleeContactGeometry(enemy.Shape, motion, boxes,
                    victim.Defence, enemy.Damage,
                    // The census carries no per-enemy native hit channel, and for a companion victim the
                    // channel only ever selects which attack rectangle the shape resolver returns: the
                    // channel-readiness arithmetic in ProjectMeleeContactGeometry is guarded on
                    // defence.IsPlayer, and the companion's single native immunity slot is its ordinary
                    // one. So the ordinary channel is the right read here and would not be for a player.
                    baseChannel: -1, victim.OrdinaryReadyTick, victim.ChannelReadyTicks, supported: true);
            }
            if (missing.Count > 0) return Suspend("enemy-motion-pending", missing);

            foreach (CapturedContactEnemy enemy in census.Enemies)
            {
                if (!geometries.TryGetValue((enemy.Slot, enemy.Generation), out var projection)) continue;
                ContactGeometry? geometry = projection.Continue(budget);
                if (geometry == null) return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
                threats.Add(new(enemy.Slot, enemy.Generation,
                    // Nothing models the player's own path, so his geometry is an unsupported empty and
                    // no player actor is handed to the forecast at all. Passing an empty *supported*
                    // geometry would read as "the player is never touched", which is the lie.
                    ToPlayer: new(Array.Empty<ContactSample>(), Supported: false), ToCompanion: geometry));
            }

            contact = new ForecastContactHarm(
                new[]
                {
                    // A body the game cannot hurt is priced at zero life, which is the one exemption
                    // `ForecastContactHarm` honours. `GetHurtByOtherNPCs` returns immediately on
                    // dontTakeDamage, dontTakeDamageFromHostiles or immortal, and `CaptureContactVictim`
                    // already froze exactly that as `ContactEnabled` — it simply had no reader. Without
                    // this the downed companion is the worst case rather than an edge one: `EnterDowned`
                    // sets life to 1 and dontTakeDamage true, so every hostile near a downed body priced
                    // as a lethal hit on a course the game would let it fly through untouched.
                    new ContactActor(HarmActor.Companion, victim.ContactEnabled ? victim.Life : 0,
                        // Ticks before this course begins belong to an executed prefix whose trajectory
                        // this forecast was not handed, so they are skipped rather than guessed at. The
                        // victim's own live immunity is the other floor.
                        Math.Max(victim.OrdinaryReadyTick, (int)Math.Ceiling(successor.Tick)), boxes),
                },
                threats, harmHorizon,
                // Never complete, on purpose: the player is unpriced. The class comment owns why.
                censusComplete: false);
        }

        ContactHarmResult? result = contact.Continue(budget);
        if (result == null) return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
        string coverage = motionUnresolved ? "some-enemy-motion-unresolved" : "every-census-enemy-modelled";
        return Priced(result.Harm, $"companionship-priced;companion-harm-priced;{coverage}");
    }

    private DecisionFact Read(DecisionFactSnapshot facts, FactKey key)
    {
        if (!facts.TryRead(key, out DecisionFact fact)) fact = new(key, -1, default, FactEvidence.Missing);
        // A missing answer is a question rather than an input, so it never enters the manifest: recording
        // it would make the cost depend on a fact that does not exist and could never stop being changed.
        if (fact.Evidence != FactEvidence.Missing) harmReads.Add(new(key, fact.Version, fact.Digest, fact.Evidence));
        return fact;
    }

    private void ForgetHarm()
    {
        geometries.Clear(); threats.Clear(); harmReads.Clear();
        contact = null; trajectory = null; motionUnresolved = false; harmHorizon = 0;
    }

    /// <summary>What makes two calls the same order: the exact step sequence, and the state the pricing
    /// starts from. The successor's pose and tick are in it because an identical step list priced from a
    /// different body pose is a different journey, which is precisely the case the leak produced.</summary>
    private static string OrderKey(IReadOnlyList<StepBinding> steps, ProjectedCourseState successor)
    {
        var key = new System.Text.StringBuilder();
        key.Append(successor.Tick).Append('@').Append(successor.Pose.X).Append(',').Append(successor.Pose.Y)
            .Append('/').Append(successor.Velocity.X).Append(',').Append(successor.Velocity.Y).Append(':');
        foreach (StepBinding step in steps) key.Append(step.Id).Append('.');
        return key.ToString();
    }
}
