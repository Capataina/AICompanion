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
/// <see cref="ForecastCourseCompanionship"/>. Predicted contact harm runs for real too, and since
/// 21 September 2026 it runs for <em>both</em> bodies: every hostile in the frozen census is asked for
/// modelled motion through <see cref="CourseEnemyMotionRequest"/>, that motion becomes timed melee
/// geometry through <see cref="ProjectMeleeContactGeometry"/> once per victim — not once per hostile,
/// because native melee geometry is victim-dependent and one victim's attack rectangle, multiplier and
/// hit channel are not another's — and first contact against each body's per-tick boxes becomes a
/// <see cref="PredictedHarm"/> event through <see cref="ForecastContactHarm"/>. That is what makes a
/// course comparable on safety at all: until it existed every candidate carried the same unknown harm,
/// so a route through a pack and a route around it priced identically.
///
/// <b>The player's path comes from his own motion track</b>, captured by <see cref="CapturedPlayerMotion"/>
/// and extended by the same law that forecasts a hostile's, so the two cannot disagree about physics. It
/// is the same for every candidate — he goes where he is going whatever the companion does — which is
/// why pricing it alone changes no ranking, and why the other half had to land with it: <b>a course's own
/// predicted kills truncate the threats they remove</b>, read off the course's effects rather than from a
/// second source that could disagree with the projection describing it.
///
/// <b>The tail is the forecast's own verdict now, where it was pinned unresolved.</b> Every route to an
/// unresolved answer the pin protected against still exists and still reports itself — a census the
/// allowance could not finish, a victim capture it could not produce, a hostile whose motion answers
/// unresolved, a trajectory that runs out before the horizon, an unsupported hit channel — so the
/// zero-cost completed candidate the Courses contract forbids stays unreachable. A snapshot without the
/// player's facts degrades rather than refusing: the companion is priced exactly as before, his geometry
/// is an unsupported empty, no player actor is added and the census reads incomplete. Refusing outright
/// was the first version and is strictly worse than the state before he was priced at all, because it
/// leaves <em>nobody's</em> harm priced.
///
/// What it still cannot express is a threat that keeps hitting: first contact ends that actor's
/// continuation, because immunity, knockback and hit hooks need a successor model.
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
    /// <summary>The player's own per-tick boxes over the horizon, sampled from his frozen motion track.
    /// Retained beside the companion's trajectory because it is resumable in exactly the same way and
    /// for the same reason: a budget cut must not restart it from tick zero.</summary>
    private PredictObservedMotion.CapturedMotion? playerMotion;
    private readonly Dictionary<(int Slot, long Generation), ProjectMeleeContactGeometry> playerGeometries = new();
    private bool motionUnresolved;
    /// <summary>Whether this order's harm pass had the player's own facts to work from. Kept because the
    /// reason line is written after the forecast is built and a resumed slice must report the same
    /// coverage the built forecast actually has.</summary>
    private bool playerWasModelled;
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

        // Harm to both bodies: the companion's over the very trajectory companionship just walked, and
        // the player's over his own predicted path, with any hostile this course kills stopping where
        // the course's own effects say it dies.
        HarmPass harm = PriceContactHarm(facts, company, successor, steps, budget);
        if (harm.Pending)
            return new(ProjectionStatus.Pending, null, harm.Reason, null, harm.RequiredMotion);

        // The manifest carries what the pricing actually read. An empty one asserts a calculation with
        // no captured inputs, which publication is entitled to believe — so companionship's manifest and
        // the harm pass's reads go over together, which is what lets a changed region, travel fact,
        // census, victim capture or enemy-motion answer dirty this cost later.
        var dependencies = new DependencyManifest(company.Dependencies.Reads.Concat(harmReads));
        // The tail is whatever the forecast honestly found, where it used to be pinned unresolved on
        // every course because nothing modelled the player. Both bodies are modelled now, so the pin
        // would be the lie rather than the safeguard: a course that clears the room could never be
        // proven better than one that walks past it, which is the behaviour the harm term exists for.
        // Every route to an unresolved answer the pin was protecting against is still there and still
        // reports itself — a census the allowance could not finish, a hostile whose motion is
        // unresolved, a trajectory that runs out before the horizon, an unsupported hit channel — so a
        // resolved tail now means the forecast actually covered the horizon for both actors.
        var projection = new CourseProjection(steps, harm.Harm, company.Intervals,
            company.EndTick, company.NominallyRejoined, tailUnresolved: harm.TailUnresolved,
            consequenceDependencies: dependencies);
        return new(ProjectionStatus.Complete, projection, harm.Reason);
    }

    /// <summary>One harm pass's outcome. <paramref name="Pending"/> and a non-empty
    /// <paramref name="RequiredMotion"/> are the suspension; otherwise the harm list is final for this
    /// order, however much of it the census and the model queue were able to answer.</summary>
    private readonly record struct HarmPass(bool Pending, IReadOnlyList<PredictedHarm> Harm, string Reason,
        IReadOnlyList<CourseEnemyMotionRequest>? RequiredMotion = null, bool TailUnresolved = true);

    private static HarmPass Suspend(string reason, IReadOnlyList<CourseEnemyMotionRequest> motion)
        => new(true, Array.Empty<PredictedHarm>(), reason, motion);
    /// <summary>A pass that could not price at all. The tail stays unresolved, because an empty harm
    /// list from a read that failed is the one thing that must never read as safety.</summary>
    private static HarmPass Priced(IReadOnlyList<PredictedHarm> harm, string reason)
        => new(false, harm, reason);
    private static HarmPass Priced(IReadOnlyList<PredictedHarm> harm, string reason, bool tailUnresolved)
        => new(false, harm, reason, null, tailUnresolved);

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
    private HarmPass PriceContactHarm(DecisionFactSnapshot facts, CourseCompanionshipResult company,
        ProjectedCourseState successor, IReadOnlyList<StepBinding> steps, DecisionWorkBudget budget)
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
            // The player's two reads, and their absence degrades rather than refuses. Returning here the
            // way the companion's reads do would be a strictly worse answer than the one this class gave
            // before the player was priced at all: it would leave *nobody's* harm priced, so a course
            // flying through a pack would cost the same as one going round it, which is the one thing
            // the harm term exists to prevent. A snapshot without these facts therefore prices the
            // companion exactly as it used to and leaves the player unmodelled, with the unsupported
            // geometry and the unresolved tail that go with it.
            CapturedContactVictim? playerVictim =
                Read(facts, CapturedContactVictim.Key(HarmActor.Player)) is { Evidence: FactEvidence.Observed } playerFact
                    ? JsonSerializer.Deserialize<CapturedContactVictim>(playerFact.Value.Text) : null;
            CapturedPlayerMotion? playerTrack =
                Read(facts, CapturedPlayerMotion.Key) is { Evidence: FactEvidence.Observed } playerMotionFact
                    ? JsonSerializer.Deserialize<CapturedPlayerMotion>(playerMotionFact.Value.Text) : null;
            bool playerModelled = playerWasModelled = playerVictim != null && playerTrack != null;

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

            // The player's own boxes, extended by the same motion law a hostile's are, so the two cannot
            // disagree about physics. His path does not depend on the course — he goes where he is going
            // whatever the companion does — so this is identical work for every candidate order, and it
            // is still recomputed per order rather than cached across them, because the retained state
            // is keyed on the order and sharing it would be the leak that keyed everything here in the
            // first place. A horizon it cannot cover leaves the list short, which the forecast already
            // reads as an unresolved tail rather than as a player standing still.
            ContactBox[] playerBoxes = Array.Empty<ContactBox>();
            if (playerModelled)
            {
                playerMotion ??= PredictObservedMotion.RestoreCaptured(playerTrack!.Motion, playerTrack.Width, playerTrack.Height);
                if (!playerMotion.Continue(harmHorizon, budget))
                    return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
                playerBoxes = playerMotion.Samples
                    .Select(centre => new ContactBox(centre.X - playerTrack!.Width * .5f, centre.Y - playerTrack.Height * .5f,
                        playerTrack.Width, playerTrack.Height))
                    .ToArray();
            }

            // Which hostiles this course's own effects say it kills, and when. Read off the effects
            // rather than off a second source: a combat step's effect carries the target as a
            // `HostileLife` need and its delta carries the life left after the hit, so the tick the
            // remaining life first reaches zero is the tick the course claims the kill. Nothing else in
            // the tree could answer it without being able to disagree with the projection it describes.
            var kills = KilledByThisCourse(steps);

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
                // The same hostile against the player, which is a different projection and not a copy:
                // native melee geometry is victim-dependent, so the attack rectangle, the damage
                // multiplier and the hit channel can all differ by who is being hit. The base channel
                // stays -1 for the same reason it does above — the census carries no per-enemy native
                // channel — and for a player that is the honest read rather than a shortcut: it means
                // the ordinary immunity gate applies first, which is what `Player.Update_NPCCollision`
                // does when no special channel was already in force, and a channel the geometry then
                // selects keeps that gate, which the projector handles.
                if (playerModelled)
                    playerGeometries[(enemy.Slot, enemy.Generation)] = new ProjectMeleeContactGeometry(enemy.Shape, motion,
                        playerBoxes, playerVictim!.Defence, enemy.Damage,
                        baseChannel: -1, playerVictim.OrdinaryReadyTick, playerVictim.ChannelReadyTicks, supported: true);
            }
            if (missing.Count > 0) return Suspend("enemy-motion-pending", missing);

            foreach (CapturedContactEnemy enemy in census.Enemies)
            {
                if (!geometries.TryGetValue((enemy.Slot, enemy.Generation), out var projection)) continue;
                ContactGeometry? geometry = projection.Continue(budget);
                if (geometry == null) return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
                // Unsupported rather than an empty supported geometry when he is not modelled, because an
                // empty supported one reads as "the player is never touched", which is the lie this
                // whole pass is built to avoid.
                ContactGeometry playerGeometry = new(Array.Empty<ContactSample>(), Supported: false);
                if (playerModelled)
                {
                    if (!playerGeometries.TryGetValue((enemy.Slot, enemy.Generation), out var againstPlayer)) continue;
                    ContactGeometry? projected = againstPlayer.Continue(budget);
                    if (projected == null) return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
                    playerGeometry = projected;
                }
                threats.Add(new(enemy.Slot, enemy.Generation,
                    ToPlayer: playerGeometry, ToCompanion: geometry,
                    KilledAtTick: kills.TryGetValue((enemy.Slot, enemy.Generation), out double at) ? at : null));
            }

            var actors = new List<ContactActor>
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
                };
            // The player, priced from tick zero rather than from the course's start: the companion's
            // prefix is unpriced because this forecast was never handed its trajectory, and the player
            // has no prefix to be missing — his path is his own and covers the whole horizon. His live
            // immunity is still the floor. He is an actor only when he was modelled; handing the
            // forecast an actor with no boxes would make every tick of him an unresolved gap rather than
            // the honest silence of a body nobody asked about.
            if (playerModelled)
                actors.Add(new ContactActor(HarmActor.Player, playerVictim!.ContactEnabled ? playerVictim.Life : 0,
                    playerVictim.OrdinaryReadyTick, playerBoxes));
            contact = new ForecastContactHarm(actors,
                threats, harmHorizon,
                // The census's own completeness, where this was pinned false because the player was
                // unpriced. He is priced now, so pinning it would be the lie: a partial census is still
                // reported as partial by the census itself, and that is the honest input. A snapshot
                // without his facts is not complete whatever the census says, because a body nobody
                // modelled is exactly the gap the pin used to stand for.
                censusComplete: census.Complete && playerModelled);
        }

        ContactHarmResult? result = contact.Continue(budget);
        if (result == null) return Suspend("budget-cut", Array.Empty<CourseEnemyMotionRequest>());
        string coverage = motionUnresolved ? "some-enemy-motion-unresolved" : "every-census-enemy-modelled";
        string bodies = playerWasModelled ? "both-bodies-priced" : "companion-priced;player-unmodelled";
        // A hostile dropped for unresolved motion understates harm, so the tail cannot be resolved on a
        // pass that dropped one however complete the forecast's own bookkeeping says it was.
        return Priced(result.Harm, $"companionship-priced;{bodies};{coverage}",
            result.TailUnresolved || motionUnresolved);
    }

    /// <summary>
    /// Which hostiles this course's own effects say it kills, and the tick each dies on.
    ///
    /// A combat step's predicted effect names its target as a <see cref="NeedKind.HostileLife"/> need
    /// carrying the slot and generation, and its delta carries the life that would be left after the
    /// hit. So a kill is the first effect whose delta leaves nothing, and its completion tick is when.
    /// Reading it from the course rather than from the world is the point: this is what the course
    /// *claims*, and pricing a course against its own claim is what makes fighting worth anything. A
    /// second source — the census's life, say — could disagree with the projection it is describing, and
    /// the disagreement would show up as a preference nobody could explain.
    ///
    /// The earliest kill wins where several steps hit one target, because the hostile is gone from that
    /// tick and later hits on it are the course over-claiming rather than a second death.
    /// </summary>
    private static Dictionary<(int Slot, long Generation), double> KilledByThisCourse(IReadOnlyList<StepBinding> steps)
    {
        var kills = new Dictionary<(int, long), double>();
        foreach (StepBinding step in steps)
            foreach (PredictedEffect effect in step.Effects)
            {
                if (effect.Need.Kind != NeedKind.HostileLife) continue;
                if (!int.TryParse(effect.Need.Identity, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int slot)) continue;
                // The delta on the target's own fact carries the life left; anything at or below zero is
                // the course saying this hostile does not survive the hit. The nominal tick is when,
                // because that is the time the rest of the projection is expressed in — taking the
                // latest would make a kill remove harm it is not yet entitled to remove, and taking the
                // earliest would make it remove harm before the hit could have landed.
                foreach (EffectDelta delta in effect.Delta)
                {
                    if (delta.Value.Amount > 0) continue;
                    var key = (slot, effect.Need.Generation);
                    if (!kills.TryGetValue(key, out double at) || effect.NominalTick < at) kills[key] = effect.NominalTick;
                }
            }
        return kills;
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
        geometries.Clear(); playerGeometries.Clear(); threats.Clear(); harmReads.Clear();
        contact = null; trajectory = null; playerMotion = null; motionUnresolved = false; harmHorizon = 0;
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
