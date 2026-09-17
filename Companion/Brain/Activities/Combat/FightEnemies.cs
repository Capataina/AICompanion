#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

namespace AICompanion.Companion.Brain.Activities.Combat;

/// <summary>
/// The one combat stance: one committed attack plan — stand, weapons, targets and aims chosen together —
/// performed until the world stops matching what it was admitted against. There are no guard and hunt sides:
/// protection is what the harm-prevention objective prices, and pursuit is what the plan's travel prices.
/// The hands fire only while this is the running activity; every other job leaves them quiet. See
/// <c>Brain.Engage</c> and <c>FireDueUse</c>.
/// </summary>
public sealed class FightEnemies : CompanionAction, ICandidateFunnelSource
{
    public override string Name => "combat";
    public override PurposeFamily Family => PurposeFamily.Combat;

    /// <summary>An excursion when the offered plan's stand takes real travel; a fight from here is not.</summary>
    public override bool IsExcursion => OfferedPlan is { } plan
        && plan.Current(PlanTick).Verdict.TravelTicks > Weights.CombatLocalTripTicks;

    /// <summary>Serves the player directly when the offered plan engages a body that can reach him.</summary>
    public override bool ServesPlayerDirectly => OfferedPlan != null && ServesPlayer;

    /// <summary>The offered plan's target, so the identity is bound at preparation like every
    /// sibling's: the committed target does not exist until Enter commits, which Select reads after it
    /// stored the identity, so a commit-bound identity re-admits combat on its second tick — a passing
    /// shot then reads as replacing guarding. The committed target is the fallback where no offer stands.</summary>
    public override object? ActivityIdentity => OfferedPlan is { } offered
        ? OfferedPlanTarget(offered) : CommittedPlanTarget;
    public override Vector2? ActivityTarget => CommittedPlanTarget?.Bottom;
    public override PositionRequest? PreparedPositionRequest => OfferedPlan is { } plan
        ? new PositionRequest(RequestKind.FireFrom, plan.Current(PlanTick).Stand.Stand, OfferedPlanTarget(plan))
        : null;

    /// <summary>The plan the last preparation offered, committed or held for entry; null when it offered none.</summary>
    public AttackPlan? OfferedPlan { get; private set; }

    /// <summary>How many plans survived the dominance filter behind the offered one, and whether a cut search offered nothing.</summary>
    public int OfferedFrontSize { get; private set; }
    public bool OfferedCut { get; private set; }

    /// <summary>The offered plan's current segment index, or -1 with no offered plan.</summary>
    public int OfferedSegment { get; private set; } = -1;

    /// <summary>The plan the body is performing, for the hands.</summary>
    public AttackPlan? CommittedPlan => combat?.Planner.Committed;

    /// <summary>What the hands are shooting at: the committed plan's primary target while it lives.</summary>
    public NPC? CommittedPlanTarget => PlanTarget(CommittedPlan);

    /// <summary>
    /// The offered plan's primary target for the pre-nomination method query, which runs before combat wins
    /// anything: the committed target does not exist yet on the tick combat is first nominated, so asking the
    /// query with it refuses every first nomination and combat can never win its way into a commitment.
    /// </summary>
    private static NPC? OfferedPlanTarget(AttackPlan plan) => PlanTarget(plan);

    private static NPC? PlanTarget(AttackPlan? plan)
    {
        if (plan == null || plan.PrimaryTarget < 0 || plan.PrimaryTarget >= Main.maxNPCs)
            return null;
        NPC npc = Main.npc[plan.PrimaryTarget];
        return npc != null && npc.active && npc.life > 0 ? npc : null;
    }

    private CompanionCombat? combat;
    private int PlanTick;
    private bool ServesPlayer;
    private float preparedValue;
    private AttackPlan? preparedPlan;

    private const string StageDeferred = "engagement-deferred";
    private const string StageAllowance = "activity-allowance";
    private const string StageNotChaseable = "not-chaseable";
    private const string StageUnplannable = "unplannable";
    private const string StageOutvalued = "outvalued";

    /// <summary>Declared in the order the preparation meets them, earliest first, so "furthest" means latest check
    /// reached: a threat the search refused got further than one the deferral held, which got further than one the
    /// allowance never admitted.</summary>
    public CandidateFunnel Funnel { get; } = new(6,
        StageNotChaseable, StageAllowance, StageDeferred, StageUnplannable, StageOutvalued,
        CandidateFunnel.Offered);

    private static string Identity(ThreatRecord threat) => FormattableString.Invariant($"npc{threat.Npc.whoAmI}:{threat.Npc.type}");

    public override void Prepare(in ActionContext ctx)
    {
        combat = ctx.Companion.Combat;
        PlanTick = ctx.Senses.Tick;
        preparedValue = 0f;
        OfferedPlan = null;
        OfferedFrontSize = 0;
        OfferedCut = false;
        OfferedSegment = -1;
        ServesPlayer = false;
        Funnel.Begin();

        var positioner = ctx.Companion.Brain.Positioner;
        bool running = ReferenceEquals(ctx.Companion.Brain.Chooser.Current, this);
        ActionContext captured = ctx;
        Func<Vector2, bool> allows = point => AllowsTarget(captured, point);
        IReadOnlyList<EnemyForecast> enemies = combat.EnsureForecast(ctx);

        if (!PlayerIntegration.CompanionPreferences.Current.Combat)
        {
            ReleaseCommitmentForRefusal(ctx, positioner, allows, enemies, running, "combat-disabled");
            Classify(OfferEligibility.PolicyForbidden, "combat-disabled");
            return;
        }
        if (combat.Weapons.Count == 0)
        {
            ReleaseCommitmentForRefusal(ctx, positioner, allows, enemies, running, "no-weapon");
            Classify(OfferEligibility.NoOpportunity, "no-weapon");
            return;
        }
        if (ctx.Senses.Player.IsDead)
        {
            ReleaseCommitmentForRefusal(ctx, positioner, allows, enemies, running, "player-dead");
            Classify(OfferEligibility.NoOpportunity, "player-dead");
            return;
        }
        var eligible = new List<ThreatRecord>();
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
        {
            NPC npc = threat.Npc;
            if (npc == null || !npc.active || npc.life <= 0)
                continue;
            if (!npc.CanBeChasedBy())
            {
                Funnel.Add(Identity(threat), npc.Center.ToTileCoordinates(), threat.DistanceToCompanion, "", StageNotChaseable, "");
                continue;
            }
            if (!AllowsTarget(ctx, npc.Bottom))
            {
                Funnel.Add(Identity(threat), npc.Center.ToTileCoordinates(), threat.DistanceToCompanion, StageNotChaseable, StageAllowance, "");
                continue;
            }
            int generation = HostileAttackSources.Generation(npc);
            if (combat.Planner.IsDeferred(ctx, npc.whoAmI, generation, npc.Center))
            {
                Funnel.Add(Identity(threat), npc.Center.ToTileCoordinates(), threat.DistanceToCompanion, StageAllowance, StageDeferred, "");
                continue;
            }
            eligible.Add(threat);
        }
        if (eligible.Count == 0)
        {
            ReleaseCommitmentForRefusal(ctx, positioner, allows, enemies, running, "no-eligible-target");
            preparedPlan = null;
            Classify(OfferEligibility.NoOpportunity, "no-eligible-target");
            return;
        }

        // A null resolution refuses the stand the plan was priced from, so the next rescore searches afresh.
        if (ctx.Companion.Brain.LastRequest.Kind == RequestKind.FireFrom && positioner.LastResolveFailed)
            combat.Planner.Release("stand-resolution-failed");
        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);

        AttackPlan? plan = combat.Planner.Committed;
        if (plan != null && combat.Planner.Validate(ctx, positioner, allows, running))
        {
            CombatOutcome? fresh = Reevaluate(ctx, combat, enemies, plan, weights);
            if (fresh != null)
            {
                OfferFromPlan(ctx, plan, fresh.Value, weights, frontSize: 1, cut: false);
                return;
            }
            combat.Planner.Release("uses-stopped-solving");
            plan = null;
        }
        if (plan == null && preparedPlan != null && combat.Planner.CheckPrepared(ctx, positioner, allows, preparedPlan))
        {
            CombatOutcome? fresh = Reevaluate(ctx, combat, enemies, preparedPlan, weights);
            if (fresh != null)
            {
                OfferFromPlan(ctx, preparedPlan, fresh.Value, weights, frontSize: 1, cut: false);
                if (running)
                {
                    combat.Planner.Commit(preparedPlan);
                    preparedPlan = null;
                }
                return;
            }
            preparedPlan = null;
        }
        PlanningBudget budget = PlanningBudget.FromMilliseconds(Weights.CombatPlanningMilliseconds);
        SearchAttackPlans.SearchResult result = SearchAttackPlans.SearchDepthOne(ctx, combat, positioner, allows,
            weights, combat.NextPlanId++, ref budget);
        if (result.Plan != null)
        {
            preparedPlan = result.Plan;
            OfferFromPlan(ctx, result.Plan, result.Plan.Outcome, weights, result.FrontSize, cut: false);
            if (running)
            {
                combat.Planner.Commit(result.Plan);
                preparedPlan = null;
            }
            return;
        }
        preparedPlan = null;
        OfferedFrontSize = result.FrontSize;
        OfferedCut = result.Reason == "budget-cut";
        if (result.Eligibility != OfferEligibility.Unresolved)
        {
            // A decided search names the threat it was about and what the planner read, which is what thousands of
            // rows of an undecided stand on a capture cannot say. An undecided search records nothing: no verdict yet.
            foreach (ThreatRecord refused in eligible)
            {
                NPC npc = refused.Npc;
                Funnel.Add(Identity(refused), npc.Center.ToTileCoordinates(), refused.DistanceToCompanion,
                    StageDeferred, StageUnplannable, FormattableString.Invariant($"reason={result.Reason}"));
            }
        }
        Classify(result.Eligibility, result.Reason);
    }

    /// <summary>
    /// The offer from a plan: its weighted outcome mapped into the utility band and lifted by the player's
    /// danger, so a threat on the player takes the body from a vein. The value, the plan and its target
    /// generations are captured together, so repeated <see cref="Score"/> reads cannot retarget. The offered
    /// outcome is stamped onto the offered plan: pricing the score from a re-evaluation while leaving the
    /// plan's search-time vector in place freezes the record's plan columns at the search while the score
    /// moves, and an irrelevance reprice then reads as no reprice at all. The commitment keeps its own
    /// search-time outcome — the offer is this tick's valuation, the commitment its schedule.
    /// </summary>
    private void OfferFromPlan(in ActionContext ctx, AttackPlan plan, CombatOutcome outcome, CombatWeights weights,
        int frontSize, bool cut)
    {
        float weighted = weights.Weighted(outcome);
        OfferedPlan = plan with { Outcome = outcome, Weighted = weighted };
        OfferedFrontSize = frontSize;
        OfferedCut = cut;
        OfferedSegment = Array.IndexOf(plan.Segments, plan.Current(PlanTick));
        preparedValue = Weights.CombatValueScale * Math.Clamp(weighted, 0f, 1f)
            * (1f + Weights.CombatPlayerDangerLift * Math.Clamp(ctx.Senses.Threats.PlayerDanger, 0f, 1f));
        foreach ((int slot, _) in plan.Validity.Targets)
        {
            foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
            {
                if (threat.Npc != null && threat.Npc.whoAmI == slot)
                {
                    if (threat.CanReachPlayer)
                        ServesPlayer = true;
                    Funnel.Add(Identity(threat), threat.Npc.Center.ToTileCoordinates(), threat.DistanceToCompanion,
                        StageOutvalued, "", FormattableString.Invariant($"plan={plan.Id};weighted={weighted:0.000}"));
                }
            }
        }
        Classify(OfferEligibility.Usable, "planned-attack");
    }

    /// <summary>
    /// A refusal's half of commitment hygiene: a plan committed against a target that left eligibility
    /// must be released with the reason validation names, not kept with a stale one while the offer
    /// reads no fight — the hands fire the commitment, and the estimate reads its kill, so a stranded
    /// plan shoots at an unattackable target under infinite protection's name. Validation names a stale
    /// plan itself; a plan that still validates but solves nothing more ends as uses-stopped-solving;
    /// only a plan that still solves is ended by the refusal, under the refusal's own name. Never offers.
    /// </summary>
    private static void ReleaseCommitmentForRefusal(in ActionContext ctx, Positioner positioner,
        Func<Vector2, bool> inAllowance, IReadOnlyList<EnemyForecast> enemies, bool running, string refusal)
    {
        CompanionCombat combat = ctx.Companion.Combat;
        if (combat.Planner.Committed == null)
            return;
        if (combat.Planner.Validate(ctx, positioner, inAllowance, running))
        {
            CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
            AttackPlan plan = combat.Planner.Committed;
            if (Reevaluate(ctx, combat, enemies, plan, weights) == null)
                combat.Planner.Release("uses-stopped-solving");
            else
                combat.Planner.Release(refusal);
        }
    }

    /// <summary>
    /// The committed plan's outcome against this tick's forecast: its remaining uses re-flown from the stand
    /// at their planned aims, so a wall the search never saw prices the plan honestly. Null when no remaining
    /// use solves any more, which invalidates the plan rather than offering a fight that cannot happen.
    /// </summary>
    private static CombatOutcome? Reevaluate(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<EnemyForecast> enemies, AttackPlan plan, CombatWeights weights)
    {
        int tick = ctx.Senses.Tick;
        int horizon = CompanionCombat.HorizonTicks;
        AttackSegment segment = plan.Current(tick);
        var weapons = combat.Weapons;
        var attacks = new List<EvaluateAttackOutcomes.Attack>();
        Vector2 muzzle = CompanionCombat.MuzzleAt(segment.Stand.Stand);
        CombatWorld world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        int knowledge = KnowledgeRevision.Current;
        bool overdueCovered = false;
        for (int i = segment.Uses.Length - 1; i >= 0; i--)
        {
            PlannedUse use = segment.Uses[i];
            bool overdue = use.FireTick <= tick;
            if (overdue && overdueCovered)
                continue;
            if (attacks.Count >= 8)
                break;
            if ((uint)use.WeaponSlot >= (uint)weapons.Count || use.TargetSlot < 0 || use.TargetSlot >= Main.maxNPCs)
                continue;
            NPC target = Main.npc[use.TargetSlot];
            if (target == null || !target.active || target.life <= 0 || !target.CanBeChasedBy())
                continue;
            CompanionWeapon weapon = weapons[use.WeaponSlot];
            if (!weapon.InReach(muzzle, target))
                continue;
            WeaponId id = SimulateUse.Identify(weapon, ctx, use.WeaponSlot);
            int fireTick = Math.Max(0, use.FireTick - tick);
            SimulatedUse sim;
            if (!CacheSimulatedUses.TryGet(id, modifiers, muzzle, use.AimPoint, fireTick, knowledge, world.RefreshCount, out SimulatedUse? cached) || cached == null)
            {
                PlanningBudget simBudget = PlanningBudget.Unbounded();
                sim = SimulateUse.Simulate(id, muzzle, use.AimPoint, use.LaunchDirection, world, enemies, modifiers, fireTick, ref simBudget);
                CacheSimulatedUses.Store(id, modifiers, muzzle, use.AimPoint, fireTick, knowledge, world.RefreshCount, sim);
            }
            else
            {
                sim = cached;
            }
            var aim = new AimCandidate(use.AimPoint, use.LaunchDirection);
            EvaluateAttackOutcomes.Attack? attack = ForecastUses.AttackFromUse(ctx, weapon, use.WeaponSlot, target,
                muzzle, sim, aim, aim, fireTick, out _, out _);
            if (attack == null)
                continue;
            // A re-flown use validates the plan only through its planned target: a trajectory that still hits,
            // but only bodies the plan never targeted, is not the planned fight solving — it is a bystander in
            // the way. Without this a plan whose target left the senses holds on a wrong-body hit, running to
            // its horizon at an unlisted body instead of re-ranking onto the threats the senses actually list.
            // The slot match is sound here because validity already pinned every planned slot's generation.
            bool touchesTarget = false;
            foreach (EvaluateAttackOutcomes.Hit hit in attack.Hits)
                if (hit.Target == use.TargetSlot) { touchesTarget = true; break; }
            if (!touchesTarget)
                continue;
            attacks.Add(attack);
            if (overdue)
                overdueCovered = true;
        }
        if (attacks.Count == 0)
            return null;
        attacks.Reverse();
        float travel = Vector2.Distance(ctx.Npc.Center, segment.Stand.Stand) <= Weights.CombatStandArrivalPx ? 0f
            : Vector2.Distance(ctx.Npc.Center, segment.Stand.Stand) / OrbPace.MaxSpeed;
        float harmAtStand = Positioner.PredictedHarmAt(segment.Stand.Stand, ctx.Senses, ctx.Npc.life);
        float harmAlongTravel = 0f;
        for (int sample = 1; sample <= 4; sample++)
        {
            Vector2 point = Vector2.Lerp(ctx.Npc.Center, segment.Stand.Stand, sample / 5f);
            harmAlongTravel = MathF.Max(harmAlongTravel, Positioner.PredictedHarmAt(point, ctx.Senses, ctx.Npc.life));
        }
        var context = new EvaluateAttackOutcomes.PlanContext((int)travel, harmAtStand, harmAlongTravel,
            Math.Max(1, ctx.Player.statLife), Math.Max(1, ctx.Npc.life),
            Math.Max(1, ctx.Companion.Mana.Max), 0f);
        var evalTargets = ForecastUses.AttackTargets(ctx);
        return EvaluateAttackOutcomes.EvaluateVector(attacks[0], attacks, evalTargets,
            Math.Max(0, combat.CooldownTicks), horizon, context, weights);
    }

    public override float Score() => preparedValue;

    /// <summary>The committed plan's remaining duration, so the chooser's horizon discount prices it.</summary>
    public override float ForecastTicks()
        => OfferedPlan is { } plan ? Math.Max(0, plan.Current(PlanTick).EndTick - PlanTick) : 0f;

    public override void Enter(in ActionContext ctx)
    {
        if (ctx.Companion.Combat.Planner.Committed == null && preparedPlan != null)
        {
            ctx.Companion.Combat.Planner.Commit(preparedPlan);
            preparedPlan = null;
        }
    }

    public override void Exit(in ActionContext ctx)
    {
        ctx.Companion.Combat.Planner.Release("activity-exited");
        preparedPlan = null;
        base.Exit(ctx);
    }

    public override void Suspend(in ActionContext ctx)
    {
        ctx.Companion.Combat.Planner.NoteSuspended(ctx.Senses.Tick);
        // Not base.Suspend: the base exits, and combat's exit releases the plan, but a suspended
        // fight keeps its plan — only the admission is re-captured, so resuming stays the same job.
        AdmitActivity();
    }

    // Attempt-local evidence, written only while combat executes and cleared when an attempt opens.
    private (int Slot, int Generation)[] attemptTargets = Array.Empty<(int Slot, int Generation)>();
    private bool firedAny, hitAny, stallObserved;
    private ulong lastSeenHit;

    public override void BeginAttempt()
    {
        AttackPlan? plan = combat?.Planner.Committed ?? preparedPlan;
        attemptTargets = plan?.Validity.Targets ?? Array.Empty<(int Slot, int Generation)>();
        firedAny = hitAny = stallObserved = false;
    }

    /// <summary>
    /// Progress is what the plan did: a planned use fired, or a planned hit landed on a planned target
    /// generation. The landed-hit ledger keeps only the latest hit, so two hits on one tick collapse to
    /// one — either renews progress, and only the record's invalidation reason could tell them apart.
    /// </summary>
    public override void ObserveOutcome(in ActionContext ctx)
    {
        if (combat == null)
            return;
        if (combat.LastFireOutcome == "fired")
            firedAny = true;
        TrackLandedHits.LandedHit? last = TrackLandedHits.Last;
        if (last != null && last.Value.Tick > lastSeenHit)
        {
            lastSeenHit = last.Value.Tick;
            AttackPlan? plan = combat.Planner.Committed;
            if (plan != null)
            {
                foreach ((int slot, int generation) in plan.Validity.Targets)
                {
                    if (last.Value.HitSlot == slot && last.Value.HitGeneration == generation)
                    {
                        combat.Planner.NoteHitLanded(slot, generation, ctx.Senses.Tick);
                        hitAny = true;
                    }
                }
            }
        }
        if (combat.Planner.Committed == null && combat.Planner.LastInvalidation == "stall")
            stallObserved = true;
    }

    /// <summary>
    /// The attempt's conclusion reads what the plan did. Targets gone after a planned use or hit complete
    /// it, unattributed because the native death hook names no killer; gone before any are invalid, and a
    /// stalled plan is failure.
    /// </summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        bool allGone = attemptTargets.Length > 0;
        foreach ((int slot, int generation) in attemptTargets)
        {
            NPC body = Main.npc[slot];
            if (body != null && body.active && body.life > 0 && HostileAttackSources.Generation(body) == generation)
            {
                allGone = false;
                break;
            }
        }
        if (allGone)
            return firedAny || hitAny
                ? new(AttemptStatus.Complete, "planned-targets-gone-after-attack", AttemptAttribution.Unattributed)
                : new(AttemptStatus.Invalid, "planned-targets-gone-before-attack");
        if (stallObserved)
            return new(AttemptStatus.Failed, "no-planned-progress");
        return firedAny || hitAny
            ? new(AttemptStatus.Partial, "attacked-planned-targets-still-present")
            : new(AttemptStatus.Attempted, "replaced-before-attacking");
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        AttackPlan? plan = ctx.Companion.Combat.Planner.Committed;
        if (plan == null)
            return PositionRequest.Hold;
        AttackSegment segment = plan.Current(ctx.Senses.Tick);
        return new PositionRequest(RequestKind.FireFrom, segment.Stand.Stand, CommittedPlanTarget);
    }
}
