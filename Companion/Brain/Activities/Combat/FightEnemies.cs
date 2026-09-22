#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;

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
    /// <summary>The one course-approved native use. Firing must refuse rather than substitute when it no longer matches.</summary>
    public AcceptedCombatUse? AcceptedUse { get; private set; }
    private AttackPlan? acceptedPlan;

    /// <summary>The native plan associated with the accepted binding.  Course integration passes this
    /// exact plan to firing; it must not recover a different committed plan by target similarity.</summary>
    public AttackPlan? AcceptedPlan => AcceptedUse != null ? acceptedPlan : null;

    public bool ActivateCourseBinding(StepBinding binding, AttackPlan plan)
    {
        if (binding.Method != CombatCourseFacts.Method || binding.Opportunity.Domain != CombatCourseFacts.Domain || combat == null)
            return false;
        PlannedUse use = default;
        int segmentIndex = -1, useIndex = -1;
        for (int segment = 0; segment < plan.Segments.Length && segmentIndex < 0; segment++)
            for (int index = 0; index < plan.Segments[segment].Uses.Length; index++)
                if (StringComparer.Ordinal.Equals(binding.NativeUseId, CombatCourseFacts.UseId(plan, segment, index)))
                {
                    use = plan.Segments[segment].Uses[index];
                    segmentIndex = segment;
                    useIndex = index;
                    break;
                }
        if (segmentIndex < 0 || use.TargetSlot < 0 || use.TargetSlot >= Main.maxNPCs
            || (uint)use.WeaponSlot >= (uint)combat.Weapons.Count)
            return false;
        int generation = HostileAttackSources.Generation(Main.npc[use.TargetSlot]);
        int prefix = Main.LocalPlayer.GetModPlayer<CompanionPlayer>().Gear[(GearSlot)use.WeaponSlot].prefix;
        if (binding.Tool != CombatCourseFacts.ToolId(use.WeaponSlot, combat.Weapons[use.WeaponSlot].ItemType, prefix))
            return false;
        AcceptedUse = new(binding.Id, binding.SnapshotId, use.TargetSlot, generation, use.WeaponSlot,
            binding.NativeUseId, segmentIndex, useIndex, binding.Method);
        acceptedPlan = plan;
        return true;
    }

    public void ClearAcceptedUse(string reason)
    {
        AcceptedUse = null;
        acceptedPlan = null;
    }
    public override string Name => "combat";
    public override PurposeFamily Family => PurposeFamily.Combat;
    public override string[] CourseDomains => new[] { CombatCourseFacts.Domain };

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

    /// <summary>
    /// The cheap legal continuation from the pose the body is actually in, which is the plan's tick-order
    /// step 5 and the one step of it the tree never built.
    ///
    /// A decision spans ticks by design — one travel query can cost more than a tick's leftover
    /// allowance — and until this existed every tick inside one asked the body to keep the player
    /// company. That is not a neutral fallback while a fight is running: selecting a different activity
    /// exits this one, and this one's <see cref="Exit"/> releases the committed plan with
    /// <c>activity-exited</c>, which makes the course's accepted use absent, which releases the course,
    /// which starts the decision again. Measured in the play of 0.38.13: 212 combat attempts at a median
    /// of one tick, 181 of them <c>replaced-before-attacking</c>, 211 plans invalidated
    /// <c>activity-exited</c>, 483 course releases <c>next-use-invalid:accepted-use-not-present</c>, and
    /// twenty shots in a minute.
    ///
    /// The committed plan comes first because it is the thing the body is already performing and its
    /// stand was priced by a decision that finished. The offered plan is second and is the opener the
    /// plan's own sentence asks for — a mechanically available shot established before deeper search,
    /// which the <c>G04 retained opener</c> row proves combat produces even under a cut. Null when
    /// neither exists, and the caller then keeps the player company, which is the honest answer when
    /// there is nothing to continue.
    /// </summary>
    public PositionRequest? Continuation
    {
        get
        {
            AttackPlan? plan = combat?.Planner.Committed ?? OfferedPlan;
            if (plan == null || plan.Segments.Length == 0) return null;
            return new PositionRequest(RequestKind.FireFrom, plan.Current(PlanTick).Stand.Stand, PlanTarget(plan));
        }
    }

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
    private SearchAttackPlans.SearchResult? preparedSearch;
    private SearchAttackPlans.SearchResult? lastSearch;

    /// <summary>The admissible hostiles the standing prepared offer was searched against, sorted, so a
    /// re-price can tell whether the world still holds the same fight. Written only where a prepared plan
    /// is born; see the comment there for why it is never cleared.</summary>
    private (int Slot, int Generation)[] preparedTargets = Array.Empty<(int, int)>();

    private bool SameAdmissibleTargets(List<(int Slot, int Generation)> admissible)
    {
        if (preparedTargets.Length != admissible.Count) return false;
        for (int i = 0; i < preparedTargets.Length; i++)
            if (preparedTargets[i] != admissible[i]) return false;
        return true;
    }

    /// <summary>
    /// The priced attack front, which is combat's whole contribution to the course observation: combat is
    /// the one domain whose opportunities are a tactical search rather than a world scan, so a shot that
    /// is not in here is a shot the course cannot discover, weigh or order.
    ///
    /// **It is the last search's front, not the last tick's, and knowing when it refreshes is the whole
    /// of reading this field.** It is assigned on one line, where a fresh `SearchAttackPlans.Search`
    /// returns; every re-pricing path — a validated commitment, a checked prepared offer — returns above
    /// that line and leaves it standing. So while either short-circuit holds, the course is being offered
    /// the world as the last search found it.
    ///
    /// For a *prepared* offer that is now bounded: the offer may only be re-priced while the admissible
    /// hostiles are unchanged, so an arrival or a departure forces a search and the front cannot fall
    /// behind what combat can see. For a *commitment* it is deliberately unbounded, because re-searching
    /// under a commitment is opportunistic replacement and this tree does not build it — the consequence
    /// is that a hostile arriving mid-fight has no priced use until that fight ends, and that is a
    /// property rather than an oversight.
    /// </summary>
    public SearchAttackPlans.SearchResult? LastSearch => lastSearch;
    /// <summary>The freshest worth a re-pricing ever established for this exact plan, and the plan it
    /// belongs to. A plan's own <c>Outcome</c> is written once, when the search commits it, and never
    /// refreshed — so a held plan offered on an allowance cut used to fall all the way back to what its
    /// original search paid for it, discarding every better-informed number priced since. The key is
    /// reference identity rather than the plan id, because identity cannot leak one plan's price into
    /// another's even if ids are ever reused.</summary>
    private AttackPlan? lastPricedPlan;
    private CombatOutcome lastPricedOutcome;
    private int lastSnapshotTick = -120;
    private int snapshotForPlan = -1;

    /// <summary>What this plan is worth on a tick that could not re-price it: the last re-price that
    /// succeeded for this same plan, or the search's own number when none has. Never another plan's.</summary>
    private CombatOutcome WorthOf(AttackPlan plan)
        => ReferenceEquals(lastPricedPlan, plan) ? lastPricedOutcome : plan.Outcome;

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
        MaybeSnapshot(ctx, combat, running);

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
        var admissible = new List<(int Slot, int Generation)>();
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
        {
            NPC npc = threat.Npc;
            if (npc == null || !npc.active || npc.life <= 0)
                continue;
            if (!threat.IsChainRepresentative)
                continue;
            if (!npc.CanBeChasedBy())
            {
                Funnel.Add(Identity(threat), npc.Center.ToTileCoordinates(), threat.DistanceToCompanion, "", StageNotChaseable, "");
                continue;
            }
            if (!ChainAllowed(ctx, threat))
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
            admissible.Add((npc.whoAmI, generation));
        }
        admissible.Sort();
        if (eligible.Count == 0)
        {
            ReleaseCommitmentForRefusal(ctx, positioner, allows, enemies, running, "no-eligible-target");
            preparedPlan = null;
            preparedSearch = null;
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
            DecisionWorkBudget heldBudget = LimitPlanningWork.Current;
            ReevaluateAttackPlan.Repricing fresh = ReevaluateAttackPlan.Reevaluate(ctx, combat, enemies, plan, weights, ref heldBudget);
            if (fresh.Priced)
            {
                lastPricedPlan = plan;
                lastPricedOutcome = fresh.Outcome!.Value;
                OfferFromPlan(ctx, plan, fresh.Outcome!.Value, weights, frontSize: 1, cut: false);
                return;
            }
            if (fresh.Unresolved)
            {
                // The allowance ran out before the held plan could be re-flown. Validate has already
                // checked its stand, its targets and its admission this tick, so nothing about the
                // world says this fight is over — only that there was no time to re-price it. It keeps
                // the body at the freshest worth anything ever established for this plan, which is the
                // last successful re-price and only falls back to the search's own number on a plan no
                // re-price has yet survived.
                OfferFromPlan(ctx, plan, WorthOf(plan), weights, frontSize: 1, cut: true);
                return;
            }
            combat.Planner.Release("uses-stopped-solving");
            plan = null;
        }
        // A prepared plan is an offer nobody has taken, and it may only be re-priced while the world it
        // was searched against still has the same admissible hostiles in it. That is the whole of this
        // condition and it is the difference between a stale front and a current one.
        //
        // Without it, a hostile that arrives and happens not to disturb the held offer is never priced
        // at all: `CheckPrepared` keeps passing, the re-price returns before the line that refreshes what
        // the census publishes, and the course is offered the same targets for ever. Reproduced on an
        // ordinary floor — two hostiles in reach, collecting winning the body, a third hostile placed
        // away from the line of fire — where the front stayed at plan 1 for sixty ticks and published
        // uses for two of three hostiles present, the third absent. That is the shape the play of
        // 0.38.13 recorded as `offered plan=237` unchanged for five hundred ticks while five hostiles
        // spawned and `combat=usable:3` never moved. It only bites on an arrival the offer survives: a
        // hostile that walks into the shot invalidates the re-price on its own, which is why the first
        // two geometries this was reproduced in came back green.
        //
        // The *committed* path deliberately keeps its short-circuit. A commitment is a commitment, and
        // re-searching under one is opportunistic replacement, which this tree does not build — the
        // consequence, named rather than discovered, is that a hostile arriving during a committed fight
        // still gets no priced use until that fight ends.
        if (plan == null && preparedPlan != null && SameAdmissibleTargets(admissible)
            && combat.Planner.CheckPrepared(ctx, positioner, allows, preparedPlan))
        {
            DecisionWorkBudget preparedBudget = LimitPlanningWork.Current;
            ReevaluateAttackPlan.Repricing fresh = ReevaluateAttackPlan.Reevaluate(ctx, combat, enemies, preparedPlan, weights, ref preparedBudget);
            if (fresh.Unresolved)
            {
                // Same rule one step down: a prepared plan the allowance could not re-price is not a
                // prepared plan that stopped solving, so it is offered at the freshest worth it has
                // rather than discarded and re-searched from nothing on the next tick.
                OfferFromPlan(ctx, preparedPlan, WorthOf(preparedPlan), weights, frontSize: 1, cut: true);
                return;
            }
            if (fresh.Priced)
            {
                lastPricedPlan = preparedPlan;
                lastPricedOutcome = fresh.Outcome!.Value;
                OfferFromPlan(ctx, preparedPlan, fresh.Outcome!.Value, weights, frontSize: 1, cut: false);
                if (running)
                {
                    CommitAndRecord(ctx, combat, preparedPlan, preparedSearch, null, weights, running);
                    preparedPlan = null;
                    preparedSearch = null;
                }
                return;
            }
            preparedPlan = null;
            preparedSearch = null;
        }
        DecisionWorkBudget budget = LimitPlanningWork.Current;
        SearchAttackPlans.SearchResult result = SearchAttackPlans.Search(ctx, combat, positioner, allows,
            weights, combat.NextPlanId++, ref budget);
        lastSearch = result;
        if (result.Plan != null)
        {
            preparedPlan = result.Plan;
            preparedSearch = result;
            // The admissible set this offer was searched against, recorded with the offer itself. It is
            // written only here, where a prepared plan is born, and never cleared: the gate above reads
            // it only when `preparedPlan` is non-null, and `preparedPlan` is never non-null without
            // having passed through this line — so a leftover set cannot be consulted.
            preparedTargets = admissible.ToArray();
            OfferFromPlan(ctx, result.Plan, result.Plan.Outcome, weights, result.FrontSize, cut: result.Cut);
            if (running)
            {
                CommitAndRecord(ctx, combat, result.Plan, result, budget, weights, running);
                preparedPlan = null;
                preparedSearch = null;
            }
            return;
        }
        preparedPlan = null;
        preparedSearch = null;
        OfferedFrontSize = result.FrontSize;
        OfferedCut = result.Cut;
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
        bool hits = outcome.DamagePerSecond > 0f && outcome.TimeToFirstDamage < 1f - 1e-5f;
        if (!hits)
        {
            preparedValue = 0f;
            Classify(OfferEligibility.KnownUnusable, "no-damage-in-horizon");
            return;
        }
        float unit = Math.Clamp(weighted, 0f, 1f);
        float lift = 1f + Weights.CombatPlayerDangerLift * Math.Clamp(ctx.Senses.Threats.PlayerDanger, 0f, 1f);
        preparedValue = Weights.WanderFloor + Weights.CombatAboveWander
            + (1f - Weights.WanderFloor) * Weights.CombatValueScale * unit * lift;
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

    /// <summary>A chain is in range when any of its bodies is. The head may sit around a corner while a segment is next to us.</summary>
    private bool ChainAllowed(in ActionContext ctx, ThreatRecord representative)
    {
        foreach (ThreatRecord member in ctx.Senses.Threats.Threats)
        {
            if (member.ChainHead != representative.ChainHead)
                continue;
            if (member.Npc != null && member.Npc.active && AllowsTarget(ctx, member.Npc.Bottom))
                return true;
        }
        return AllowsTarget(ctx, representative.Npc.Bottom);
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
            DecisionWorkBudget validationBudget = LimitPlanningWork.Current;
            // Only a priced refusal earns the "uses stopped solving" reason. A cut cannot tell whether
            // they still solve, so the release keeps the caller's own reason rather than asserting
            // something the allowance never let it find out.
            if (ReevaluateAttackPlan.Reevaluate(ctx, combat, enemies, plan, weights, ref validationBudget).Refused)
                combat.Planner.Release("uses-stopped-solving");
            else
                combat.Planner.Release(refusal);
        }
    }

    /// <summary>
    /// Commit with the record the audit replays from: the plan's committed event carrying the search's front
    /// and rejected plans, then the decision's snapshot. A prepared plan committed ticks after its search
    /// carries that search's verdicts — what it was decided on — with a fresh budget, because the spent one
    /// priced a tick that has passed; the snapshot says so by its rescore trigger.
    /// </summary>
    private void CommitAndRecord(in ActionContext ctx, CompanionCombat combat, AttackPlan plan,
        SearchAttackPlans.SearchResult? search, DecisionWorkBudget? spent, CombatWeights weights, bool combatRunning)
    {
        combat.Planner.Commit(plan);
        lastSnapshotTick = ctx.Senses.Tick;
        snapshotForPlan = plan.Id;
        if (!GodsEyeEvents.Active)
            return;
        int front = search?.FrontSize ?? 1;
        IReadOnlyList<RejectedPlan> rejected = search?.Rejected ?? Array.Empty<RejectedPlan>();
        GodsEyeEvents.RecordCombatPlan(ctx.Npc, plan.Id, "committed", plan.Segments[0].Stand.Stand, front,
            DescribeAttackPlan.Detail(plan, rejected, front, "none"));
        // Snapshots are the 120-tick hold dump and the mark key, not every commit: last night wrote
        // 189 KB per new plan, 546 MB in one session, because the hold never held.
    }

    /// <summary>
    /// The bounded-rate snapshot while a plan is committed, and the inspector's mark: every 120 ticks the
    /// decision's input with the plan it still holds, so the audit grades the hold as well as the search.
    /// A mark writes even with no plan committed, because "that moment" is also why no fight was offered.
    /// Verdicts are the latest search's — what the brain knew when it last asked — because no search runs
    /// on the rescore tick itself; the commit's own snapshot carries the committing search.
    /// </summary>
    private void MaybeSnapshot(in ActionContext ctx, CompanionCombat combat, bool running)
    {
        bool mark = ExportCombatSnapshot.MarkRequested;
        if (mark)
            ExportCombatSnapshot.MarkRequested = false;
        if (!GodsEyeEvents.Active)
            return;
        AttackPlan? committed = combat.Planner.Committed;
        if (committed != null && committed.Id != snapshotForPlan)
            snapshotForPlan = committed.Id;
        if (!mark && (committed == null || ctx.Senses.Tick - lastSnapshotTick < 120))
            return;
        CombatWeights weights = WeighCombatObjectives.ForSenses(ctx);
        DecisionWorkBudget budget = LimitPlanningWork.Current;
        GodsEyeEvents.RecordCombatSnapshot(ctx.Npc, committed?.Id ?? -1, mark ? "mark" : "rescore",
            ExportCombatSnapshot.Build(ctx, combat, committed, lastSearch, weights, budget, AllowanceRadius(), running));
        lastSnapshotTick = ctx.Senses.Tick;
    }

    public override float Score() => preparedValue;

    /// <summary>
    /// The committed plan's remaining duration, so the chooser's horizon discount prices it. The last
    /// segment's end, not the current one's: the body is occupied until the plan ends, and pricing only
    /// the current segment would let a plan evade the duration discount by splitting into short legs.
    /// </summary>
    public override float ForecastTicks()
        => OfferedPlan is { } plan && plan.Segments.Length > 0
            ? Math.Max(0, plan.Segments[^1].EndTick - PlanTick) : 0f;

    public override void Enter(in ActionContext ctx)
    {
        if (ctx.Companion.Combat.Planner.Committed == null && preparedPlan != null)
        {
            // Running is literal, not read: the chooser calls Enter before assigning Current, so the
            // current-activity read still names the outgoing holder on this tick. Combat won the body.
            CommitAndRecord(ctx, ctx.Companion.Combat, preparedPlan, preparedSearch, null,
                WeighCombatObjectives.ForSenses(ctx), combatRunning: true);
            preparedPlan = null;
            preparedSearch = null;
        }
    }

    public override void Exit(in ActionContext ctx)
    {
        ClearAcceptedUse("activity-exited");
        ctx.Companion.Combat.Planner.Release("activity-exited");
        preparedPlan = null;
        preparedSearch = null;
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
