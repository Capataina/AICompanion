#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// The depth-one search: today's stand candidates with verdicts, the greedy segment from each reachable
/// stand, the dominance filter, and the best weighted survivor. Candidates are evaluated best upper bound
/// first — the value of a weapon's assumed attack with no geometry in the way — so a cut search priced the
/// promising pairs before the budget ran out. A cut search commits nothing: the offer is unresolved, never
/// known-unusable, which is the repository's rule for every bounded search.
/// </summary>
public static class SearchAttackPlans
{
    public sealed record SearchResult(AttackPlan? Plan, OfferEligibility Eligibility, string Reason, int FrontSize,
        IReadOnlyList<RejectedPlan> Rejected, IReadOnlyList<AssessedStand> Assessed, int CandidatesEvaluated, int SimulationsSpent,
        IReadOnlyList<AttackPlan> Front);

    /// <summary>
    /// The audit's replay seam: proposals and verdicts from a snapshot in place of the live generators and
    /// the live flood. Proposals alone re-assess the snapshot's stands against the restored world; verdicts
    /// alone are meaningless without their proposals and read as a missing replay. Both null is the live
    /// search. The verdict replay is what makes the snapshot's decision reproducible at all — the reach
    /// flood is incremental history no snapshot carries, so a replayed assessment would answer NotYet
    /// where the live flood had finished, and row A1's mutation (a snapshot without verdicts) fails
    /// exactly there.
    /// </summary>
    public sealed record SearchOptions(IReadOnlyList<StandProposal>? Proposals = null,
        IReadOnlyList<StandVerdict>? Verdicts = null);

    private static SearchResult Empty(AttackPlan? plan, OfferEligibility eligibility, string reason, int frontSize)
        => new(plan, eligibility, reason, frontSize, Array.Empty<RejectedPlan>(), Array.Empty<AssessedStand>(), 0, 0, Array.Empty<AttackPlan>());

    public static SearchResult SearchDepthOne(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        Func<Vector2, bool> inAllowance, CombatWeights weights, int planId, ref PlanningBudget budget,
        SearchOptions? options = null)
    {
        int tick = ctx.Senses.Tick;
        var weapons = combat.Weapons;
        if (weapons.Count == 0)
            return Empty(null, OfferEligibility.NoOpportunity, "no-weapon", 0);
        IReadOnlyList<EnemyForecast> enemies = combat.EnsureForecast(ctx);
        List<ThreatRecord> targets = ProposalTargets(ctx, combat, inAllowance, out int deferredExcluded);
        if (targets.Count == 0)
        {
            if (deferredExcluded > 0)
                return Empty(null, OfferEligibility.KnownUnusable, "engagement-deferred-no-progress", 0);
            return Empty(null, OfferEligibility.NoOpportunity, "no-eligible-target", 0);
        }

        IReadOnlyList<StandProposal> proposals = options?.Proposals
            ?? ProposeFiringStands.Propose(ctx, combat, enemies, targets, ref budget);
        IReadOnlyList<StandVerdict>? replayed = options?.Proposals != null ? options.Verdicts : null;
        if (replayed != null && replayed.Count != proposals.Count)
            throw new ArgumentException($"a verdict replay needs one verdict per proposal, not {replayed.Count} for {proposals.Count}");
        if (proposals.Count == 0)
            return Empty(null, OfferEligibility.NoOpportunity, "no-eligible-target", 0);

        var verdicts = new List<StandVerdict>(proposals.Count);
        positioner.AssessStands(proposals, ctx.Npc.Center, ctx.Senses, ctx.Npc.life, inAllowance, verdicts);
        var assessed = new List<AssessedStand>(proposals.Count);
        var candidates = new List<AttackPlan>();
        bool sawReachable = false, sawUndecided = false;
        for (int p = 0; p < proposals.Count; p++)
        {
            StandProposal proposal = proposals[p];
            StandVerdict verdict = replayed?[p] ?? verdicts[p];
            assessed.Add(new AssessedStand(proposal, verdict));
            if (verdict.Reach == ReachVerdict.Unreachable)
                continue;
            if (verdict.Reach == ReachVerdict.NotYet)
            {
                sawUndecided = true;
                continue;
            }
            sawReachable = true;
            AttackPlan? plan = SegmentFromStand(ctx, combat, enemies, targets, proposal, verdict, weights,
                planId, ref budget);
            if (plan != null)
                candidates.Add(plan);
            if (budget.Cut)
                return new SearchResult(null, OfferEligibility.Unresolved, "budget-cut", candidates.Count,
                    Array.Empty<RejectedPlan>(), assessed, candidates.Count, budget.Simulations, Array.Empty<AttackPlan>());
        }
        if (!sawReachable)
        {
            if (sawUndecided)
                return new SearchResult(null, OfferEligibility.Unresolved, "stands-undecided", 0,
                    Array.Empty<RejectedPlan>(), assessed, 0, budget.Simulations, Array.Empty<AttackPlan>());
            return new SearchResult(null, OfferEligibility.KnownUnusable, "no-reachable-stand", 0,
                Array.Empty<RejectedPlan>(), assessed, 0, budget.Simulations, Array.Empty<AttackPlan>());
        }
        if (candidates.Count == 0)
        {
            // A reachable stand with no solving use settles nothing about the stands still undecided:
            // answering unusable would report an unanswered search as a proven absence.
            if (sawUndecided)
                return new SearchResult(null, OfferEligibility.Unresolved, "stands-undecided", 0,
                    Array.Empty<RejectedPlan>(), assessed, 0, budget.Simulations, Array.Empty<AttackPlan>());
            return new SearchResult(null, OfferEligibility.KnownUnusable, "no-use-reaches-target", 0,
                Array.Empty<RejectedPlan>(), assessed, 0, budget.Simulations, Array.Empty<AttackPlan>());
        }
        (List<AttackPlan> front, List<(AttackPlan Plan, AttackPlan Dominator, int LostOn)> drops) =
            KeepOnlyUndominated.FilterWithDrops(candidates, plan => plan.Outcome);
        if (System.Environment.GetEnvironmentVariable("AIC_DEBUG_PLAN") == "1")
            foreach (AttackPlan c in candidates)
                System.Console.WriteLine($"  DEBUG cand primary={c.PrimaryTarget} stand={c.Segments[0].Stand.Stand.X:0},{c.Segments[0].Stand.Stand.Y:0} weighted={c.Weighted:0.00} kills={c.TargetKillTicks?.Length ?? 0} travel={c.Segments[0].Verdict.TravelTicks:0} outcome={c.Outcome}");
        AttackPlan best = front[0];
        foreach (AttackPlan plan in front)
            if (plan.Weighted > best.Weighted)
                best = plan;
        return new SearchResult(best, OfferEligibility.Usable, "planned-attack", front.Count,
            BestRejected(front, drops, best, weights), assessed, candidates.Count, budget.Simulations, front);
    }

    /// <summary>
    /// The three best plans the search turned down, by weighted value: drops named with their dominator's
    /// widest win, front survivors the argmax passed over named with the weighted gap's largest term — the
    /// objective where the weight decision turned. The record carries these, not the whole front.
    /// </summary>
    private static IReadOnlyList<RejectedPlan> BestRejected(List<AttackPlan> front,
        List<(AttackPlan Plan, AttackPlan Dominator, int LostOn)> drops, AttackPlan best, CombatWeights weights)
    {
        var rejected = new List<RejectedPlan>(drops.Count + front.Count);
        foreach ((AttackPlan plan, _, int lostOn) in drops)
            rejected.Add(new RejectedPlan(plan, "dominated", CombatOutcome.Name(lostOn)));
        foreach (AttackPlan plan in front)
        {
            if (ReferenceEquals(plan, best))
                continue;
            rejected.Add(new RejectedPlan(plan, "weights", CombatOutcome.Name(WeightedGapTerm(best, plan, weights))));
        }
        rejected.Sort((a, b) => b.Plan.Weighted.CompareTo(a.Plan.Weighted));
        if (rejected.Count > 3)
            rejected.RemoveRange(3, rejected.Count - 3);
        return rejected;
    }

    private static int WeightedGapTerm(AttackPlan best, AttackPlan plan, CombatWeights weights)
    {
        int term = 0;
        float widest = float.NegativeInfinity;
        for (int i = 0; i < CombatOutcome.Count; i++)
        {
            float gap = CombatOutcome.HigherIsBetter(i)
                ? weights[i] * (best.Outcome[i] - plan.Outcome[i])
                : weights[i] * (plan.Outcome[i] - best.Outcome[i]);
            if (gap > widest)
            {
                widest = gap;
                term = i;
            }
        }
        return term;
    }

    /// <summary>
    /// The targets the search proposes stands for: damageable, allowed, not deferred by a stall, capped by
    /// urgency then distance so a crowd cannot turn one tick into a search.
    /// </summary>
    private static List<ThreatRecord> ProposalTargets(in ActionContext ctx, CompanionCombat combat, Func<Vector2, bool> inAllowance,
        out int deferredExcluded)
    {
        deferredExcluded = 0;
        var scored = new List<(ThreatRecord Threat, float Score)>();
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
        {
            NPC npc = threat.Npc;
            if (npc == null || !npc.active || npc.life <= 0 || !npc.CanBeChasedBy())
                continue;
            if (!inAllowance(npc.Bottom))
                continue;
            int generation = HostileAttackSources.Generation(npc);
            if (combat.Planner.IsDeferred(ctx, npc.whoAmI, generation, npc.Center))
            {
                deferredExcluded++;
                continue;
            }
            float score = 0.4f * MathF.Max(threat.Urgency, threat.UrgencyToCompanion)
                + 0.6f * Consideration.Inverse(threat.DistanceToCompanion, Weights.CombatProposalReach);
            if (threat.IsBoss) score += 0.3f;
            scored.Add((threat, score));
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var targets = new List<ThreatRecord>(Math.Min(scored.Count, Weights.CombatMaxProposalTargets));
        for (int i = 0; i < scored.Count && targets.Count < Weights.CombatMaxProposalTargets; i++)
            targets.Add(scored[i].Threat);
        return targets;
    }

    // Phase E proposes through ProposeFiringStands: the seven generators above this file's verdicts.
    // The old set — where the body is, the guard anchor, the hunt approach — went with the positioner's
    // firing-stand scoring, which named the stands instead of the weapons.

    private sealed record CandidateAttack(EvaluateAttackOutcomes.Attack Attack, int WeaponSlot, Vector2 Muzzle, Vector2 AimPoint, Vector2 Launch, int TargetSlot);

    /// <summary>
    /// The greedy segment from one stand: every weapon against the proposal's targets at the aim the
    /// simulator prices best, simulated best upper bound first, the opener the candidate whose continuation
    /// values highest. Null when no use reaches any target from here. Aims compete on simulated damage
    /// only — the simulator is authoritative for geometry, and a learner that says aiming off pays must not
    /// move the shot off the intercept it proved; the learner prices weapons, targets and stands instead,
    /// which is the ruling the weapon-learning row holds.
    /// </summary>
    private static AttackPlan? SegmentFromStand(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<EnemyForecast> enemies, List<ThreatRecord> targets, StandProposal proposal,
        StandVerdict verdict, CombatWeights weights, int planId, ref PlanningBudget budget)
    {
        int tick = ctx.Senses.Tick;
        int horizon = CompanionCombat.HorizonTicks;
        int travel = (int)MathF.Min(verdict.TravelTicks, horizon - 1);
        Vector2 muzzle = CompanionCombat.MuzzleAt(proposal.Stand);
        CombatWorld world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
        var weapons = combat.Weapons;

        var pairs = new List<(int Weapon, ThreatRecord Target, float Bound)>();
        var evalTargets = ForecastUses.AttackTargets(ctx);
        foreach (ThreatRecord threat in targets)
        {
            if (!ProposalServes(proposal, threat.Npc.whoAmI))
                continue;
            List<EvaluateAttackOutcomes.Attack> assumed = ForecastUses.AssumedAttacks(combat, ctx, threat.Npc);
            foreach (EvaluateAttackOutcomes.Attack attack in assumed)
            {
                float bound = EvaluateAttackOutcomes.Evaluate(attack, assumed, evalTargets,
                    Math.Max(0, combat.CooldownTicks - travel), horizon).Value;
                pairs.Add((attack.Weapon, threat, bound));
            }
        }
        pairs.Sort((a, b) => b.Bound.CompareTo(a.Bound));

        var candidates = new List<CandidateAttack>();
        foreach ((int weaponSlot, ThreatRecord threat, _) in pairs)
        {
            if (candidates.Count >= Weights.CombatMaxAttacksPerStand || !budget.Check())
                break;
            NPC npc = threat.Npc;
            CompanionWeapon weapon = weapons[weaponSlot];
            if (!weapon.InReach(muzzle, npc)) continue;
            ForecastUses.AimedUse? aimed = ForecastUses.BestAimUse(ctx, weapon, weaponSlot, npc, muzzle,
                enemies, world, travel, record: false, planning: true, ref budget);
            if (aimed == null || !budget.Check())
                continue;
            EvaluateAttackOutcomes.Attack? attack = ForecastUses.AttackFromUse(ctx, weapon, weaponSlot, npc,
                muzzle, aimed.Value.Use, aimed.Value.Aim, aimed.Value.Intercept, travel, out string rej, out _);
            if (attack != null)
                candidates.Add(new CandidateAttack(attack, weaponSlot, muzzle, aimed.Value.Aim.AimPoint,
                    aimed.Value.Aim.LaunchDirection, npc.whoAmI));
        }
        if (candidates.Count == 0 || budget.Cut)
            return null;

        var attacks = new List<EvaluateAttackOutcomes.Attack>(candidates.Count);
        foreach (CandidateAttack candidate in candidates)
            attacks.Add(candidate.Attack);
        var context = new EvaluateAttackOutcomes.PlanContext(travel, verdict.HarmAtStand, verdict.HarmAlongTravel,
            Math.Max(1, ctx.Player.statLife), Math.Max(1, ctx.Npc.life), Math.Max(1, ctx.Companion.Mana.Max),
            IntegrateCompanyGap(ctx, proposal.Stand, travel, horizon));
        int cooldown = Math.Max(0, combat.CooldownTicks);

        CombatOutcome outcome = default;
        List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>? sequence = null;
        List<(int Target, int Tick)>? kills = null;
        float best = float.NegativeInfinity;
        foreach (CandidateAttack candidate in candidates)
        {
            var trySequence = new List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>();
            var tryKills = new List<(int Target, int Tick)>();
            CombatOutcome tried = EvaluateAttackOutcomes.EvaluateVector(candidate.Attack, attacks, evalTargets,
                cooldown, horizon, context, weights, tick, trySequence, tryKills);
            float value = weights.Weighted(tried);
            if (value > best)
            {
                best = value;
                outcome = tried;
                sequence = trySequence;
                kills = tryKills;
            }
        }
        if (sequence == null || sequence.Count == 0)
            return null;

        var uses = new PlannedUse[sequence.Count];
        var targetSlots = new HashSet<int>();
        for (int i = 0; i < sequence.Count; i++)
        {
            CandidateAttack meta = candidates[0];
            foreach (CandidateAttack candidate in candidates)
                if (ReferenceEquals(candidate.Attack, sequence[i].Attack)) { meta = candidate; break; }
            uses[i] = new PlannedUse(meta.WeaponSlot, meta.Muzzle, meta.AimPoint, meta.Launch, sequence[i].FireTick, meta.TargetSlot);
            targetSlots.Add(meta.TargetSlot);
        }
        var validityTargets = new (int Slot, int Generation)[targetSlots.Count];
        int validityIndex = 0;
        foreach (int slot in targetSlots)
            validityTargets[validityIndex++] = (slot, HostileAttackSources.Generation(Main.npc[slot]));
        float admittedMax = 0f;
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
            admittedMax = MathF.Max(admittedMax, MathF.Max(threat.Urgency, threat.UrgencyToCompanion));
        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        var validity = new PlanValidity(TerrainChanges.Revision, AttackLearning.Revision, validityTargets, admittedMax,
            region.Centre, region.HalfSize, region.GapBeyond(proposal.Stand), tick);
        var segment = new AttackSegment(proposal, verdict, tick + travel, tick + travel, tick + horizon, uses, SegmentEnd.Horizon);
        var killArray = new (int Slot, int Tick)[kills!.Count];
        for (int i = 0; i < kills.Count; i++)
            killArray[i] = kills[i];
        return new AttackPlan(planId, new[] { segment }, outcome, best, validity, BudgetCut: false, killArray);
    }

    private static bool ProposalServes(StandProposal proposal, int slot)
    {
        foreach (int served in proposal.TargetSlots)
            if (served == slot) return true;
        return false;
    }

    /// <summary>
    /// The company gap the segment pays: the region carried forward over the horizon by its own velocity,
    /// the body travelling the straight line then holding the stand, each sample's pixel gap in units of the
    /// region's size, integrated over the horizon. A plan that fights from inside the region pays nothing.
    /// </summary>
    private static float IntegrateCompanyGap(in ActionContext ctx, Vector2 stand, int travel, int horizon)
    {
        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        float size = MathF.Max(1f, 2f * MathF.Max(region.HalfSize.X, region.HalfSize.Y));
        float total = 0f;
        const int step = 30;
        for (int t = 0; t < horizon; t += step)
        {
            var carried = region with { Centre = region.Centre + region.Velocity * t };
            Vector2 body = t < travel && travel > 0
                ? Vector2.Lerp(ctx.Npc.Center, stand, (float)t / travel)
                : stand;
            total += carried.GapBeyond(body) / size * Math.Min(step, horizon - t);
        }
        return total / Math.Max(1, horizon);
    }
}
