#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// The one committed attack plan per companion. It stays committed while all of these hold: its current
/// segment's stand still has a reachable verdict and its allowance admission still describes it; every body
/// it targets is still alive, or was killed by the plan; a new hostile or a real urgency jump (not a
/// pixel of creep) has not appeared; the intent region has not moved so far that the plan's
/// company gap doubled; and the hands have made progress — a planned use fired or a planned hit landed —
/// within the stall window. A plan is never replaced merely because a rival scores higher on one rescore,
/// which is the walker's lesson that a destination is kept by membership rather than by a bonus.
/// </summary>
public sealed class CommitAttackPlan
{
    /// <summary>The plan the body is performing, or null when the next rescore searches afresh.</summary>
    public AttackPlan? Committed { get; private set; }

    /// <summary>Why the last plan ended, or <c>no-commitment</c> when none ever did.</summary>
    public string LastInvalidation { get; private set; } = "no-commitment";

    /// <summary>Bodies a stalled plan deferred, with the tick they wait until and the poses that reopen them early.</summary>
    private readonly Dictionary<(int Slot, int Generation), (int Until, Vector2 Target, Vector2 Body, int Terrain)> deferred = new();

    /// <summary>Bodies a planned hit has landed on under the committed plan, by slot and generation: killed by the plan.</summary>
    private readonly HashSet<(int Slot, int Generation)> hitByPlan = new();

    /// <summary>
    /// The tick the committed plan last made progress. It lives here rather than on the plan because the plan
    /// is the search's immutable product: writing progress into it copies the record and the commitment no
    /// longer holds the plan the search returned. Seeded from the plan's search tick at commit.
    /// </summary>
    private int progressTick;

    /// <summary>
    /// The body the commitment records against, bound from the body's own defaults: every release writes the
    /// plan's invalidated event, wherever the release came from. Unbound headless, where no writer is open.
    /// </summary>
    private NPC? bound;

    /// <summary>The segment the record last saw: a validation on a later one writes the advanced event.</summary>
    private int recordedSegment = -1;

    public void Bind(NPC companion) => bound = companion;

    /// <summary>
    /// Bodies a stalled plan defers, with ticks remaining rather than absolute waits: the audit restores
    /// into its own tick space, where the live absolute tick is meaningless. Terrain is carried, and the
    /// snapshot stamps the live revision beside it: the restore rebuilds the live equality or inequality
    /// against its own revision, so a deferral the player's digging reopened reopens in the replay too.
    /// </summary>
    public IReadOnlyDictionary<(int Slot, int Generation), (int Remaining, Vector2 Target, Vector2 Body, int Terrain)> ExportDeferred(int nowTick)
    {
        var copy = new Dictionary<(int Slot, int Generation), (int Remaining, Vector2 Target, Vector2 Body, int Terrain)>(deferred.Count);
        foreach (var (key, failure) in deferred)
            copy[key] = (failure.Until - nowTick, failure.Target, failure.Body, failure.Terrain);
        return copy;
    }

    /// <summary>Bodies a planned hit has landed on under the committed plan: killed by the plan.</summary>
    public IReadOnlyCollection<(int Slot, int Generation)> ExportHits() => hitByPlan;

    /// <summary>The commitment's progress tick for the snapshot: -1 with no plan committed. A plan
    /// beside -1 is a capture written before the stamp, whose stall check the runner cannot grade.</summary>
    public int ExportProgressTick() => Committed == null ? -1 : progressTick;

    /// <summary>Install the stamped progress after committing the shifted plan: Commit reseeds the
    /// search-time value, and the snapshot carries where the live commitment's clock had reached.</summary>
    public void AssumeProgress(int tick) => progressTick = tick;

    public void AssumeDeferred(int slot, int generation, int remaining, Vector2 target, Vector2 body, int nowTick, int terrain)
        => deferred[(slot, generation)] = (nowTick + remaining, target, body, terrain);

    public void AssumeHit(int slot, int generation) => hitByPlan.Add((slot, generation));

    public void Commit(AttackPlan plan)
    {
        Committed = plan;
        hitByPlan.Clear();
        progressTick = plan.Validity.LastProgressTick;
        recordedSegment = -1;
        // LastInvalidation is untouched: it names why the last plan ended, and a fresh commitment
        // overwriting it would hide the segment-complete or stall the record is owed for the old one.
        // The committed event is the committer's to write: it holds the search's front and rejected
        // plans, which this commitment never sees.
    }

    /// <summary>Ends the commitment with the reason the record reads, writing the plan's invalidated event first.</summary>
    public void Release(string reason)
    {
        if (Committed != null && bound != null)
        {
            AttackPlan ending = Committed;
            int segment = Math.Clamp(recordedSegment, 0, ending.Segments.Length - 1);
            GodsEyeEvents.RecordCombatPlan(bound, ending.Id, "invalidated", ending.Segments[segment].Stand.Stand, -1,
                DescribeAttackPlan.Detail(ending, Array.Empty<RejectedPlan>(), -1, reason));
        }
        Committed = null;
        LastInvalidation = reason;
    }

    /// <summary>A planned use left the hand: progress, whatever it hits.</summary>
    public void NoteUseFired(int tick)
    {
        if (Committed == null) return;
        progressTick = tick;
    }

    /// <summary>A hit landed on a body the plan targets: progress, and the kill is the plan's if the body goes.</summary>
    public void NoteHitLanded(int slot, int generation, int tick)
    {
        hitByPlan.Add((slot, generation));
        NoteUseFired(tick);
    }

    /// <summary>
    /// Safety suspended the activity: the plan is kept and the suspension is not a stall, so progress is
    /// renewed rather than aged. The body cannot fire while something else owns it; counting that as no
    /// progress would end every plan the evade layer touched.
    /// </summary>
    public void NoteSuspended(int tick) => NoteUseFired(tick);

    /// <summary>Whether a stalled plan still defers this body: the wait has not elapsed and neither body nor terrain moved on.</summary>
    public bool IsDeferred(in ActionContext ctx, int slot, int generation, Vector2 targetCentre)
    {
        var key = (slot, generation);
        if (!deferred.TryGetValue(key, out var failure))
            return false;
        bool unchanged = ctx.Senses.Tick < failure.Until && failure.Terrain == TerrainChanges.Revision
            && Vector2.DistanceSquared(failure.Target, targetCentre) < 32f * 32f
            && Vector2.DistanceSquared(failure.Body, ctx.Npc.Bottom) < 32f * 32f;
        if (unchanged)
            return true;
        deferred.Remove(key);
        return false;
    }

    /// <summary>
    /// Whether the committed plan still describes the world. Every failure releases it with the validity
    /// condition that ended it, so the record names what changed rather than that a score moved. The stall
    /// clock runs only while combat runs: hands that never held the body cannot have stalled it.
    /// </summary>
    public bool Validate(in ActionContext ctx, Positioner positioner, Func<Vector2, bool> inAllowance, bool running)
    {
        AttackPlan? plan = Committed;
        if (plan == null)
            return false;
        if (!CheckPlan(ctx, positioner, inAllowance, plan, running, progressTick, out string reason))
        {
            if (reason == "stall")
                DeferTargets(ctx, plan);
            Release(reason);
            return false;
        }
        int segment = Array.IndexOf(plan.Segments, plan.Current(ctx.Senses.Tick));
        if (bound != null && recordedSegment >= 0 && segment != recordedSegment)
            GodsEyeEvents.RecordCombatPlan(bound, plan.Id, "advanced", plan.Segments[segment].Stand.Stand, -1,
                DescribeAttackPlan.Detail(plan, Array.Empty<RejectedPlan>(), -1, "none"));
        recordedSegment = segment;
        return true;
    }

    /// <summary>
    /// The same checks for a prepared-but-uncommitted plan, without releasing or deferring anything: a plan
    /// that never ran stalls nothing and defers nobody. Outlives its tick only while combat does not run.
    /// </summary>
    public bool CheckPrepared(in ActionContext ctx, Positioner positioner, Func<Vector2, bool> inAllowance, AttackPlan plan)
        => CheckPlan(ctx, positioner, inAllowance, plan, running: false, plan.Validity.LastProgressTick, out _);

    private bool CheckPlan(in ActionContext ctx, Positioner positioner, Func<Vector2, bool> inAllowance,
        AttackPlan plan, bool running, int lastProgress, out string reason)
    {
        int tick = ctx.Senses.Tick;
        AttackSegment segment = plan.Current(tick);

        // The segment ended: its targets are gone by the plan's own hand, or the horizon ran out.
        if (SegmentEnded(ctx, plan, segment))
        {
            reason = "segment-complete";
            return false;
        }

        // The stand still has a reachable verdict, and its allowance admission still describes it.
        // Reachability is re-read only when travel is still needed: the body within arrival tolerance
        // of the stand is already there, and no flood, however young or newly rooted, can unprove that.
        // Without the exception every plan searched from here would invalidate on the next rescore in a
        // fixture whose flood never grew — and churn in play whenever the flood re-rooted.
        Point tile = MovementQueries.Tile(segment.Stand.Stand);
        bool arrived = Vector2.DistanceSquared(ctx.Npc.Center, segment.Stand.Stand)
            <= Weights.CombatStandArrivalPx * Weights.CombatStandArrivalPx;
        if (!arrived && positioner.ReachOf(tile) != ReachVerdict.Reachable)
        {
            reason = "stand-unreachable";
            return false;
        }
        if (segment.Verdict.InAllowance && !inAllowance(segment.Stand.Stand))
        {
            reason = "stand-left-allowance";
            return false;
        }

        // The learner has not revised since the search: a plan priced before the lesson re-searches.
        if (AttackLearning.Revision != plan.Validity.KnowledgeRevision)
        {
            reason = "knowledge-revised";
            return false;
        }

        // Every targeted body still alive, or killed by the plan.
        foreach ((int slot, int generation) in plan.Validity.Targets)
        {
            NPC body = Main.npc[slot];
            bool alive = body != null && body.active && body.life > 0 && HostileAttackSources.Generation(body) == generation;
            if (!alive && !hitByPlan.Contains((slot, generation)))
            {
                reason = "target-gone-unplanned";
                return false;
            }
        }

        // A known hostile may creep; a new body or a real urgency jump re-searches.
        // Pixel-closer 0.01 ticks were last night's 32 plans per second of combat.
        var admitted = plan.Validity.Hostiles;
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
        {
            float urgency = MathF.Max(threat.Urgency, threat.UrgencyToCompanion);
            bool known = false;
            if (admitted != null)
            {
                int slot = threat.Npc.whoAmI;
                int generation = HostileAttackSources.Generation(threat.Npc);
                foreach ((int s, int g) in admitted)
                    if (s == slot && g == generation)
                    {
                        known = true;
                        break;
                    }
            }
            else
                known = true;
            if (!known && urgency > Weights.CombatNewHostileUrgency)
            {
                reason = "new-urgent-hostile";
                return false;
            }
            if (known && urgency > plan.Validity.AdmittedMaxUrgency + Weights.CombatUrgencyHoldSlack)
            {
                reason = "new-urgent-hostile";
                return false;
            }
        }

        // A known hostile that is still itself may still have left the place the plan was priced at, and
        // nothing above notices: the target checks ask whether a body is alive and the same generation, the
        // urgency checks ask how loud it is, and a teleport changes neither. The stand, the weapon and the
        // aim were all chosen against where these bodies were, so this is the commitment's spatial
        // invalidation — the same law the retained searches keep, applied to the bodies rather than to the
        // terrain.
        //
        // The allowance is the body's own admitted speed over the ticks since, plus the observer's
        // continuity ball. A body travelling at the pace the plan saw is inside it by construction, and so
        // is one that reverses, because the allowance is a radius rather than a predicted point — which is
        // deliberate, since a walker turning round is ordinary motion and the churn this check must not
        // cause. What falls outside is displacement the body's own motion cannot explain: a teleport, a
        // knockback that throws it tiles, an observation gap across which nothing was watched. Sharing
        // ContinuityPixels with the observer is the point rather than a convenience: past that line the
        // observer has already thrown the body's forecast history away, so the plan is holding geometry
        // priced against a track that no longer exists.
        var admittedMotion = plan.Validity.AdmittedMotion;
        if (admittedMotion != null)
        {
            int elapsed = Math.Max(0, tick - plan.Validity.LastProgressTick);
            foreach ((int slot, Vector2 centre, float speed) in admittedMotion)
            {
                NPC body = Main.npc[slot];
                if (body == null || !body.active || body.life <= 0)
                    continue;
                float allowance = PredictObservedMotion.ContinuityPixels + speed * elapsed;
                if (Vector2.DistanceSquared(body.Center, centre) > allowance * allowance)
                {
                    reason = "hostile-moved-off-its-track";
                    return false;
                }
            }
        }

        // No release for the player walking away from the stand: a fight is never charged for its distance from
        // him (the owner's ruling of 25 September 2026), so there is no priced gap for his walking to make stale,
        // and a fight he walks away from is finished rather than dropped. `AdmittedCompanyGap` is still recorded,
        // for the combat snapshot, and decides nothing.

        // The hands made progress within the stall window. The clock runs from the segment's start rather
        // than the search: the travel the plan ordained is the plan working, and a far stand would otherwise
        // stall en route. A use fired during travel still counts — progress is progress.
        int idleSince = Math.Max(lastProgress, segment.StartTick);
        if (running && tick - idleSince > Weights.CombatPlanStallTicks)
        {
            reason = "stall";
            return false;
        }
        reason = "valid";
        return true;
    }

    private bool SegmentEnded(in ActionContext ctx, AttackPlan plan, AttackSegment segment)
    {
        if (ctx.Senses.Tick >= segment.EndTick)
            return true;
        // All of the segment's targeted bodies gone after the plan hit them: the segment did its work.
        // A body gone without a hit — a reused slot's new occupant, which fails the generation match
        // while standing alive and well — is not done work; it falls through to target-gone-unplanned
        // below, which names what changed rather than calling it complete.
        bool anyTarget = false;
        foreach ((int slot, int generation) in plan.Validity.Targets)
        {
            NPC body = Main.npc[slot];
            bool alive = body != null && body.active && body.life > 0 && HostileAttackSources.Generation(body) == generation;
            if (alive || !hitByPlan.Contains((slot, generation)))
                return false;
            anyTarget = true;
        }
        return anyTarget;
    }

    /// <summary>A stall defers every body the stalled plan targeted, not only the primary, because changing target is not progress.</summary>
    private void DeferTargets(in ActionContext ctx, AttackPlan plan)
    {
        foreach ((int slot, int generation) in plan.Validity.Targets)
        {
            NPC body = Main.npc[slot];
            Vector2 centre = body != null && body.active ? body.Center : Vector2.Zero;
            deferred[(slot, generation)] = (ctx.Senses.Tick + Weights.CombatDeferRetryTicks, centre, ctx.Npc.Bottom,
                TerrainChanges.Revision);
        }
    }
}
