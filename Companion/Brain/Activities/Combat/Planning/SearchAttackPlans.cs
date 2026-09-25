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
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// The beam over timed segments. Level one proposes firing stands from what the weapons do, assesses
/// them in one batch, prices a greedy segment from each reachable one, and keeps every priced plan.
/// Each deeper level re-runs proposals from the kept prefixes' stands against the enemies the prefix
/// leaves alive, starts segments at arrival and at the prefix's delayed landings, rolls each start
/// from the prefix life as of that start, and values the whole plan. The answer is the best weighted
/// plan among the undominated across every completed level, so a level wins only when moving is worth
/// the travel. A cut inside level one keeps every stand it already priced and offers the best of those.
/// A cut that priced nothing still offers one greedy from-here plan when a use from the body reaches,
/// so a crowd cannot delete combat; only a from-here that also fails is unresolved. A cut past level
/// one keeps the deepest completed level's answer the same way.
/// </summary>
public static class SearchAttackPlans
{
    /// <summary>How deep the beam goes: two timed segments with a third when budget remains.</summary>
    public const int MaxSearchDepth = 3;

    // Profiler sections: the whole attack search, and the enemy forecast it starts from. Stand proposal, the
    // uses it simulates and the aims those solve open their own sections beneath this one.
    private static readonly int AttackSearchSection = Infrastructure.Diagnostics.BrainSections.Register("attack-search");
    private static readonly int ForecastSection = Infrastructure.Diagnostics.BrainSections.Register("forecast");

    /// <summary>
    /// How many of a prefix's delayed landings become segment starts: the earliest landings are the
    /// timings worth naming, and the fan-out stays bounded. Arrival itself is always a start beside them.
    /// </summary>
    private const int MaxDelayedStarts = 4;

    public sealed record SearchResult(AttackPlan? Plan, OfferEligibility Eligibility, string Reason, int FrontSize,
        IReadOnlyList<RejectedPlan> Rejected, IReadOnlyList<AssessedStand> Assessed, int CandidatesEvaluated,
        int SimulationsSpent, IReadOnlyList<AttackPlan> Front, bool Cut = false,
        IReadOnlyList<DeeperAssessedStand>? DeeperAssessed = null);
    /// <summary>
    /// The audit's replay seam: restored level-one proposals with their recorded verdicts, so the
    /// verdict replay re-prices the same stands the live search priced instead of re-proposing, plus
    /// the recorded deeper verdicts matched by origin and stand tiles with the reason, so deeper levels
    /// replay the live flood's answers too. Exhaustive grid construction passes null and proposes freely.
    /// </summary>
    public sealed record SearchOptions(IReadOnlyList<StandProposal>? Proposals = null,
        IReadOnlyList<StandVerdict>? Verdicts = null,
        IReadOnlyList<DeeperAssessedStand>? DeeperVerdicts = null,
        bool ArrivalStartsOnly = false,
        bool DisableBankAims = false);

    private static SearchResult Empty(AttackPlan? plan, OfferEligibility eligibility, string reason, int frontSize,
        bool cut)
        => new(plan, eligibility, reason, frontSize, Array.Empty<RejectedPlan>(), Array.Empty<AssessedStand>(), 0, 0,
            Array.Empty<AttackPlan>(), cut);

    /// <summary>
    /// Level one only, for the callers that pin level-one properties: the audit's self-test scenes and
    /// the planning fixtures' default depth. Live play searches the full beam through <see cref="Search"/>.
    /// </summary>
    public static SearchResult SearchDepthOne(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        Func<Vector2, bool> inAllowance, CombatWeights weights, int planId, ref DecisionWorkBudget budget,
        SearchOptions? options = null)
        => Search(ctx, combat, positioner, inAllowance, weights, planId, ref budget, options, maxDepth: 1);

    public static SearchResult Search(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        Func<Vector2, bool> inAllowance, CombatWeights weights, int planId, ref DecisionWorkBudget budget,
        SearchOptions? options = null, int maxDepth = MaxSearchDepth)
    {
        using var section = Infrastructure.Diagnostics.BrainSections.Enter(AttackSearchSection);
        int tick = ctx.Senses.Tick;
        int horizon = CompanionCombat.HorizonTicks;
        var weapons = combat.Weapons;
        if (weapons.Count == 0)
            return Empty(null, OfferEligibility.NoOpportunity, "no-weapon", 0, budget.Cut);
        IReadOnlyList<EnemyForecast> enemies;
        using (Infrastructure.Diagnostics.BrainSections.Enter(ForecastSection)) enemies = combat.EnsureForecast(ctx);
        List<ThreatRecord> targets = ProposalTargets(ctx, combat, inAllowance, out int deferredExcluded);
        if (targets.Count == 0)
        {
            if (deferredExcluded > 0)
                return Empty(null, OfferEligibility.KnownUnusable, "engagement-deferred-no-progress", 0, budget.Cut);
            return Empty(null, OfferEligibility.NoOpportunity, "no-eligible-target", 0, budget.Cut);
        }

        // G04: establish the legal current-use prefix before broad stand discovery can spend the
        // remaining allowance. This is an ordinary candidate, not a firing fallback: the caller
        // still admits and commits it before the hand may act.
        // Pricing the opener against every proposal target was suspected of starving the guarantee on a
        // crowd and was measured innocent on 21 September 2026, which is why the obvious narrowing —
        // price it against the most urgent target alone — is *not* here.
        //
        // The evidence: with the crowd fixture clearing the simulation cache before every measured
        // search, which is production's regime because `CacheSimulatedUses.ClearAtTick` clears on every
        // tick change, all twelve searches return a usable plan with the opener priced against all of
        // them. Narrowing to one target cut the opener's cost about threefold and moved the failing row
        // from ten of twelve to eleven of twelve — an improvement that turned out to be measuring the
        // fixture's unwarmed first searches rather than the deadline. Two untimed searches ahead of the
        // measured twelve clear it completely, with this code unchanged.
        //
        // So the narrowing would have given up the opener being the best from-here shot in exchange for
        // nothing, and the reason it looked like it worked is worth more than the change would have been.
        var openerSlots = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            openerSlots[i] = targets[i].Npc.whoAmI;
        var opener = new StandProposal(ctx.Npc.Center, StandReason.HereAndCompany, -1, openerSlots);
        var openerVerdicts = new List<StandVerdict>(1);
        positioner.AssessStands(new[] { opener }, ctx.Npc.Center, ctx.Senses, ctx.Npc.life, inAllowance, openerVerdicts);
        List<EvaluateAttackOutcomes.Target> evalTargets = ForecastUses.AttackTargets(ctx);
        BeamNode? openingNode = null;
        if (options?.Proposals == null && openerVerdicts.Count == 1 && openerVerdicts[0].Reach == ReachVerdict.Reachable)
        {
            openingNode = PriceLevelOne(ctx, combat, enemies, evalTargets, targets, opener, openerVerdicts[0],
                weights, planId, tick, horizon, ref budget);
        }
        if (budget.Cut && openingNode == null)
            return new SearchResult(null, OfferEligibility.Unresolved, "budget-cut", 0,
                Array.Empty<RejectedPlan>(), openerVerdicts.Count == 1
                    ? new[] { new AssessedStand(opener, openerVerdicts[0]) } : Array.Empty<AssessedStand>(), 0,
                (int)budget.OperationsUsed, Array.Empty<AttackPlan>(), Cut: true);

        IReadOnlyList<StandProposal> proposals = options?.Proposals
            ?? ProposeFiringStands.Propose(ctx, combat, enemies, targets, ref budget,
                disableBankAims: options?.DisableBankAims ?? false);
        IReadOnlyList<StandVerdict>? replayed = options?.Proposals != null ? options.Verdicts : null;
        if (replayed != null && replayed.Count != proposals.Count)
            throw new ArgumentException($"a verdict replay needs one verdict per proposal, not {replayed.Count} for {proposals.Count}");
        if (proposals.Count == 0 && openingNode == null)
            return Empty(null, OfferEligibility.NoOpportunity, "no-eligible-target", 0, budget.Cut);

        var verdicts = new List<StandVerdict>(proposals.Count);
        positioner.AssessStands(proposals, ctx.Npc.Center, ctx.Senses, ctx.Npc.life, inAllowance, verdicts);
        var assessed = new List<AssessedStand>(proposals.Count);
        var pool = new List<BeamNode>();
        if (openingNode != null)
        {
            pool.Add(openingNode);
            assessed.Add(new(opener, openerVerdicts[0]));
        }
        bool sawReachable = openingNode != null, sawUndecided = false;
        for (int p = 0; p < proposals.Count; p++)
        {
            if (budget.Exhausted) break;
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
            BeamNode? node = PriceLevelOne(ctx, combat, enemies, evalTargets, targets, proposal, verdict, weights,
                planId, tick, horizon, ref budget);
            if (node != null)
                pool.Add(node);
            if (budget.Cut)
                break;
        }
        if (pool.Count == 0)
        {
            if (budget.Cut)
            {
                return new SearchResult(null, OfferEligibility.Unresolved, "budget-cut", 0,
                    Array.Empty<RejectedPlan>(), assessed, 0, (int)budget.OperationsUsed, Array.Empty<AttackPlan>(),
                    Cut: true);
            }
            if (!sawReachable)
            {
                if (sawUndecided)
                    return new SearchResult(null, OfferEligibility.Unresolved, "stands-undecided", 0,
                        Array.Empty<RejectedPlan>(), assessed, 0, (int)budget.OperationsUsed, Array.Empty<AttackPlan>(), budget.Cut);
                return new SearchResult(null, OfferEligibility.KnownUnusable, "no-reachable-stand", 0,
                    Array.Empty<RejectedPlan>(), assessed, 0, (int)budget.OperationsUsed, Array.Empty<AttackPlan>(), budget.Cut);
            }
            // A reachable stand with no solving use settles nothing about the stands still undecided:
            // answering unusable would report an unanswered search as a proven absence.
            if (sawUndecided)
                return new SearchResult(null, OfferEligibility.Unresolved, "stands-undecided", 0,
                    Array.Empty<RejectedPlan>(), assessed, 0, (int)budget.OperationsUsed, Array.Empty<AttackPlan>(), budget.Cut);
            return new SearchResult(null, OfferEligibility.KnownUnusable, "no-use-reaches-target", 0,
                Array.Empty<RejectedPlan>(), assessed, 0, (int)budget.OperationsUsed, Array.Empty<AttackPlan>(), budget.Cut);
        }

        var deeperAssessed = new List<DeeperAssessedStand>();
        List<BeamNode> frontier = TopBeam(pool);
        int depth = 1;
        while (depth < maxDepth)
        {
            if (!budget.Check())
                break;
            List<BeamNode> next = ExpandFrontier(ctx, combat, positioner, inAllowance, weights, enemies,
                evalTargets, targets, frontier, planId, tick, horizon, options, deeperAssessed,
                ref budget);
            if (budget.Cut)
                break;
            if (next.Count == 0)
                break;
            foreach (BeamNode node in next)
                pool.Add(node);
            frontier = TopBeam(next);
            depth++;
        }

        var plans = new List<AttackPlan>(pool.Count);
        foreach (BeamNode node in pool)
            plans.Add(node.Plan);
        (List<AttackPlan> front, List<(AttackPlan Plan, AttackPlan Dominator, int LostOn)> drops) =
            KeepOnlyUndominated.FilterWithDrops(plans, plan => plan.Outcome);
        if (System.Environment.GetEnvironmentVariable("AIC_DEBUG_PLAN") == "1")
            foreach (AttackPlan c in plans)
                System.Console.WriteLine($"  DEBUG cand primary={c.PrimaryTarget} segs={c.Segments.Length} stands={string.Join("+", System.Linq.Enumerable.Select(c.Segments, s => $"{s.Stand.Stand.X:0},{s.Stand.Stand.Y:0}/{s.Stand.Reason}@{s.StartTick}"))} weighted={c.Weighted:0.00} kills={c.TargetKillTicks?.Length ?? 0} uses={string.Join("+", System.Linq.Enumerable.Select(c.Segments, s => s.Uses.Length + ":" + string.Join(",", System.Linq.Enumerable.Select(s.Uses, u => u.WeaponSlot))))} travel={c.Segments[0].Verdict.TravelTicks:0} outcome={c.Outcome}");
        AttackPlan best = front[0];
        foreach (AttackPlan plan in front)
            if (plan.Weighted > best.Weighted)
                best = plan;
        if (budget.Cut)
            best = best with { BudgetCut = true };
        return new SearchResult(best, OfferEligibility.Usable, "planned-attack", front.Count,
            BestRejected(front, drops, best, weights), assessed, pool.Count, (int)budget.OperationsUsed, front, budget.Cut,
            deeperAssessed);
    }

    /// <summary>
    /// A priced prefix with what the next level prices against: the plan, each segment's attacks with
    /// their absolute fire ticks, each segment's pricing context, and the prefix's kill ticks, earliest
    /// first. The next level reads landings off the attacks and life off a fixed re-roll, never off the
    /// plan's uses, which carry no flight times.
    /// </summary>
    private sealed record BeamNode(AttackPlan Plan,
        List<List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>> Pairs,
        List<EvaluateAttackOutcomes.PlanContext> Contexts,
        List<(int Target, int Tick)> Kills);

    /// <summary>
    /// The beam-width plans worth extending: the undominated by weighted value, best first. A dominated
    /// prefix's extension is likely dominated too, so the beam spends its levels on the front.
    /// </summary>
    private static List<BeamNode> TopBeam(List<BeamNode> nodes)
    {
        var plans = new List<AttackPlan>(nodes.Count);
        foreach (BeamNode node in nodes)
            plans.Add(node.Plan);
        List<AttackPlan> front = KeepOnlyUndominated.Filter(plans, plan => plan.Outcome);
        var beam = new List<BeamNode>(front.Count);
        foreach (AttackPlan plan in front)
            foreach (BeamNode node in nodes)
                if (ReferenceEquals(node.Plan, plan))
                {
                    beam.Add(node);
                    break;
                }
        beam.Sort((a, b) => b.Plan.Weighted.CompareTo(a.Plan.Weighted));
        if (beam.Count > Weights.CombatBeamWidth)
            beam.RemoveRange(Weights.CombatBeamWidth, beam.Count - Weights.CombatBeamWidth);
        return beam;
    }

    private static List<BeamNode> ExpandFrontier(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        Func<Vector2, bool> inAllowance, CombatWeights weights, IReadOnlyList<EnemyForecast> enemies,
        List<EvaluateAttackOutcomes.Target> evalTargets, List<ThreatRecord> targets, List<BeamNode> frontier,
        int planId, int tick, int horizon, SearchOptions? options,
        List<DeeperAssessedStand> deeperAssessed, ref DecisionWorkBudget budget)
    {
        var next = new List<BeamNode>();
        foreach (BeamNode node in frontier)
        {
            List<BeamNode> extended = ExpandPrefix(ctx, combat, positioner, inAllowance, weights, enemies,
                evalTargets, targets, node, planId, tick, horizon, options, deeperAssessed, ref budget);
            if (budget.Cut)
                return next;
            foreach (BeamNode child in extended)
                next.Add(child);
        }
        return next;
    }

    /// <summary>
    /// One prefix extended by one segment: the body fires an early cut of the prefix, departs, and
    /// the next segment starts at arrival or waits for a prefix landing. Departing only after the
    /// greedy fill's last fire left every travelling continuation arriving after the horizon, so a
    /// far-then-close plan and a grenade-then-pierce plan could never beat staying. The earliest
    /// fires are the cuts worth naming; the proposal set is the enemies still alive at the first of
    /// them, and each cut re-prices remaining life as of that cut. A cut budget aborts the level.
    /// </summary>
    private static List<BeamNode> ExpandPrefix(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        Func<Vector2, bool> inAllowance, CombatWeights weights, IReadOnlyList<EnemyForecast> enemies,
        List<EvaluateAttackOutcomes.Target> evalTargets, List<ThreatRecord> targets, BeamNode node, int planId,
        int tick, int horizon, SearchOptions? options,
        List<DeeperAssessedStand> deeperAssessed, ref DecisionWorkBudget budget)
    {
        var extended = new List<BeamNode>();
        AttackPlan prefix = node.Plan;
        AttackSegment last = prefix.Segments[^1];
        Vector2 origin = last.Stand.Stand;
        int horizonEnd = tick + horizon;
        var departures = new List<int>();
        foreach (PlannedUse use in last.Uses)
        {
            if (use.FireTick >= horizonEnd)
                continue;
            if (!departures.Contains(use.FireTick))
                departures.Add(use.FireTick);
            if (departures.Count >= MaxDelayedStarts)
                break;
        }
        if (departures.Count == 0)
            return extended;
        int earliest = departures[0];
        (_, Dictionary<int, float> remAtPropose, _) = ValuationAt(node, evalTargets, horizon, tick, earliest);
        var alive = new List<ThreatRecord>();
        foreach (ThreatRecord threat in targets)
            if (remAtPropose.TryGetValue(threat.Npc.whoAmI, out float life) && life > 0f)
                alive.Add(threat);
        if (alive.Count == 0)
            return extended;
        var rolled = new List<EnemyForecast>(enemies.Count);
        foreach (EnemyForecast enemy in enemies)
            rolled.Add(enemy.RolledCopy(remAtPropose.TryGetValue(enemy.Slot, out float life) ? life : enemy.Life));
        List<StandProposal> proposals = ProposeFiringStands.Propose(ctx, combat, rolled, alive, ref budget, origin,
            disableBankAims: options?.DisableBankAims ?? false);
        if (budget.Cut)
            return extended;
        var verdicts = new List<StandVerdict>(proposals.Count);
        positioner.AssessStands(proposals, origin, ctx.Senses, ctx.Npc.life, inAllowance, verdicts);
        IReadOnlyList<DeeperAssessedStand>? recorded = options?.DeeperVerdicts;
        for (int p = 0; p < proposals.Count; p++)
        {
            StandProposal proposal = proposals[p];
            StandVerdict? replayedDeeper = MatchDeeper(recorded, origin, proposal);
            StandVerdict verdict = replayedDeeper ?? verdicts[p];
            if (replayedDeeper == null)
                verdict = verdict with { TravelTicks = LegTravelTicks(positioner, origin, proposal.Stand) };
            deeperAssessed.Add(new DeeperAssessedStand(origin, proposal, verdict));
            if (verdict.Reach != ReachVerdict.Reachable)
                continue;
            Vector2 muzzle = CompanionCombat.MuzzleAt(proposal.Stand);
            CombatWorld world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
            foreach (int departAbs in departures)
            {
            int arrivalAbs = departAbs + (int)MathF.Min(verdict.TravelTicks, horizonEnd - departAbs);
            if (arrivalAbs >= horizonEnd)
                continue;
            int lastFireAbs = last.StartTick;
            int lastUseTicks = 1;
            foreach ((EvaluateAttackOutcomes.Attack attack, int fire) in node.Pairs[^1])
                if (fire <= departAbs && fire >= lastFireAbs)
                {
                    lastFireAbs = fire;
                    lastUseTicks = Math.Max(1, attack.UseTicks);
                }
            List<int> starts = DelayedStarts(node, arrivalAbs, horizonEnd, options?.ArrivalStartsOnly ?? false);
            int handsAtArrival = Math.Max(0, lastFireAbs + lastUseTicks - arrivalAbs);
            Dictionary<int, int>? throwsAtArrival = AfterUses(StartingThrows(combat.Weapons), node.Pairs, arrivalAbs);
            SimmedStand simmed = SimulateStand(ctx, combat, rolled, evalTargets, alive, proposal, muzzle,
                arrivalAbs - tick, handsAtArrival, horizon, world, ref budget, throwsAtArrival);
            if (budget.Cut)
                return extended;
            if (simmed.Candidates.Count == 0)
                continue;
            foreach (int startAbs in starts)
            {
                int horizonRem = horizonEnd - startAbs;
                if (horizonRem <= 0)
                    continue;
                (EvaluateAttackOutcomes.Valuation prefixAt, Dictionary<int, float> remAt,
                    Dictionary<(int Target, int Weapon), (float Chance, int Until)>? marksAt) =
                    ValuationAt(node, evalTargets, horizon, tick, startAbs);
                int cooldown = Math.Max(0, lastFireAbs + lastUseTicks - startAbs);
                Dictionary<int, int>? throwsAtStart = AfterUses(StartingThrows(combat.Weapons), node.Pairs, startAbs);
                PricedPiece? piece = PriceFromStart(ctx, evalTargets, simmed, proposal, verdict, weights, startAbs,
                    arrivalAbs, cooldown, travelCtx: 0, horizonRem, remAt, tick, horizon, gapForContext: 0f,
                    ShiftMarks(marksAt, startAbs - tick), usesLeft: throwsAtStart);
                if (piece == null)
                    continue;
                EvaluateAttackOutcomes.Valuation whole = EvaluateAttackOutcomes.Combine(prefixAt, piece.Value,
                    evalTargets, 0f, startAbs - tick);
                var segments = new AttackSegment[prefix.Segments.Length + 1];
                for (int i = 0; i < prefix.Segments.Length; i++)
                    segments[i] = prefix.Segments[i];
                PlannedUse[] keptUses = KeptUses(prefix.Segments[^1].Uses, departAbs);
                segments[^2] = segments[^2] with
                {
                    EndTick = departAbs,
                    EndsWhen = SegmentEnd.NextSegmentWorthMore,
                    Uses = keptUses,
                };
                segments[^1] = piece.Segment;
                var seen = new HashSet<int>();
                var unionTargets = new List<(int Slot, int Generation)>();
                foreach ((int slot, int generation) in prefix.Validity.Targets)
                    if (seen.Add(slot))
                        unionTargets.Add((slot, generation));
                foreach (int slot in piece.TargetSlots)
                    if (seen.Add(slot))
                        unionTargets.Add((slot, HostileAttackSources.Generation(Main.npc[slot])));
                PlayerIntentRegion region = ctx.Senses.Intent.Region;
                PlanValidity validity = prefix.Validity with
                {
                    Targets = unionTargets.ToArray(),
                    AdmittedCompanyGap = region.GapBeyond(piece.Segment.Stand.Stand),
                };
                var seenKills = new HashSet<int>();
                var kills = new List<(int Target, int Tick)>();
                foreach ((int target, int killTick) in node.Kills)
                    if (seenKills.Add(target) && killTick <= startAbs)
                        kills.Add((target, killTick));
                foreach ((int target, int killTick) in piece.Kills)
                    if (seenKills.Add(target))
                        kills.Add((target, killTick));
                var killArray = new (int Slot, int Tick)[kills.Count];
                for (int i = 0; i < kills.Count; i++)
                    killArray[i] = kills[i];
                float weighted = weights.Weighted(whole.Outcome);
                var plan = new AttackPlan(planId, segments, whole.Outcome, weighted, validity, BudgetCut: false,
                    killArray, AttackPlan.NameByDanger(segments, evalTargets));
                var cutPairs = new List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>();
                foreach ((EvaluateAttackOutcomes.Attack attack, int fire) in node.Pairs[^1])
                    if (fire <= departAbs)
                        cutPairs.Add((attack, fire));
                var pairs = new List<List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>>(node.Pairs.Count + 1);
                for (int i = 0; i < node.Pairs.Count - 1; i++)
                    pairs.Add(node.Pairs[i]);
                pairs.Add(cutPairs);
                pairs.Add(piece.Pairs);
                var contexts = new List<EvaluateAttackOutcomes.PlanContext>(node.Contexts) { piece.Context };
                extended.Add(new BeamNode(plan, pairs, contexts, kills));
            }
            }
        }
        return extended;
    }

    private static PlannedUse[] KeptUses(PlannedUse[] uses, int departAbs)
    {
        int n = 0;
        foreach (PlannedUse use in uses)
            if (use.FireTick <= departAbs)
                n++;
        var kept = new PlannedUse[n];
        int w = 0;
        foreach (PlannedUse use in uses)
            if (use.FireTick <= departAbs)
                kept[w++] = use;
        return kept;
    }

    /// <summary>
    /// The prefix's valuation and remaining life as of one start tick: each segment's uses whose shots
    /// have landed by then, rolled in order through the segments' own contexts. The body still fires the
    /// prefix whole — the truncation is valuation-only, what the next segment may assume done when it
    /// starts firing. Nothing more of the prefix is assumed to land after the start: a segment's roll
    /// cannot price through in-flight damage, so the delayed starts exist to wait past it instead.
    /// </summary>
    private static (EvaluateAttackOutcomes.Valuation Value, Dictionary<int, float> Remaining,
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? Debuffs) ValuationAt(
        BeamNode node, IReadOnlyList<EvaluateAttackOutcomes.Target> evalTargets, int horizon, int tick, int startAbs)
    {
        Dictionary<int, float>? remaining = null;
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? marks = null;
        EvaluateAttackOutcomes.Valuation? acc = null;
        for (int i = 0; i < node.Pairs.Count; i++)
        {
            var subset = new List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>();
            foreach ((EvaluateAttackOutcomes.Attack attack, int fire) in node.Pairs[i])
                if (fire + Math.Max(1, attack.ImpactTicks) <= startAbs)
                    subset.Add((attack, fire - tick));
            var ending = new Dictionary<(int Target, int Weapon), (float Chance, int Until)>();
            EvaluateAttackOutcomes.Valuation piece = EvaluateAttackOutcomes.RollFixed(subset, evalTargets, horizon,
                node.Contexts[i], tick, initialRemaining: remaining, initialDebuffs: marks, endingDebuffs: ending);
            remaining = piece.RemainingLife;
            marks = ending.Count == 0 ? null : ending;
            acc = acc == null ? piece : EvaluateAttackOutcomes.Combine(acc, piece, evalTargets, 0f, 0);
        }
        return (acc!, remaining!, marks);
    }

    /// <summary>
    /// The prefix's live marks carried into a piece's frame: the prefix rolls search-relative, the piece
    /// from its own start, so every expiry moves back by the start's offset and what already expired is
    /// dropped. Until is exclusive against impacts at one or later, so an expiry at or before the start
    /// marks nothing the piece can price.
    /// </summary>
    private static Dictionary<(int Target, int Weapon), (float Chance, int Until)>? ShiftMarks(
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? marks, int shift)
    {
        if (marks == null)
            return null;
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? moved = null;
        foreach (var entry in marks)
        {
            int until = entry.Value.Until - shift;
            if (until > 0)
                (moved ??= new Dictionary<(int Target, int Weapon), (float Chance, int Until)>())[entry.Key] = (entry.Value.Chance, until);
        }
        return moved;
    }

    /// <summary>
    /// The starts worth pricing from one stand: arrival, then the prefix's distinct landings after
    /// arrival and before the horizon's end, earliest first. Waiting past a landing is how the next
    /// segment avoids firing at what the prefix already killed.
    /// </summary>
    private static List<int> DelayedStarts(BeamNode node, int arrivalAbs, int horizonEnd, bool arrivalOnly)
    {
        var landings = new List<int>();
        if (!arrivalOnly)
        {
            foreach (List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)> pairs in node.Pairs)
                foreach ((EvaluateAttackOutcomes.Attack attack, int fire) in pairs)
                {
                    int landing = fire + Math.Max(1, attack.ImpactTicks);
                    if (landing > arrivalAbs && landing < horizonEnd && !landings.Contains(landing))
                        landings.Add(landing);
                }
            landings.Sort();
            if (landings.Count > MaxDelayedStarts)
                landings.RemoveRange(MaxDelayedStarts, landings.Count - MaxDelayedStarts);
        }
        landings.Insert(0, arrivalAbs);
        return landings;
    }

    /// <summary>
    /// The leg's travel in ticks: the flood's costs count from its root, so the leg differs the stand's
    /// cost and the origin's, floored by the straight line, which is also the whole answer while either
    /// end is unreached. The difference is capped at four times the air line so a hop on the far side
    /// of a wall the flood rounded the long way cannot exceed the horizon and skip the segment.
    /// </summary>
    private static float LegTravelTicks(Positioner positioner, Vector2 origin, Vector2 stand)
    {
        if (Vector2.DistanceSquared(origin, stand) <= 64f)
            return 0f;
        Point oTile = MovementQueries.Tile(origin);
        float? toStand = positioner.EstimatedTravelTicks(oTile, MovementQueries.Tile(stand));
        float? toOrigin = positioner.EstimatedTravelTicks(oTile, oTile);
        float straight = Vector2.Distance(origin, stand) / MathF.Max(0.1f, OrbPace.MaxSpeed);
        if (toStand == null || toOrigin == null)
            return straight;
        float leg = MathF.Abs(toStand.Value - toOrigin.Value);
        // Flood costs are from the body's root, not this origin. A short hop on the far side of a
        // wall the flood rounded the long way would exceed the horizon and skip the segment. The
        // holonomic body flies the air line; the flood may only make a real corridor cost more,
        // never many times the straight hop.
        return MathF.Min(straight * 4f, MathF.Max(straight, leg));
    }

    private static StandVerdict? MatchDeeper(IReadOnlyList<DeeperAssessedStand>? recorded, Vector2 origin,
        StandProposal proposal)
    {
        if (recorded == null)
            return null;
        Point oTile = MovementQueries.Tile(origin);
        Point sTile = MovementQueries.Tile(proposal.Stand);
        foreach (DeeperAssessedStand entry in recorded)
            if (MovementQueries.Tile(entry.Origin) == oTile
                && MovementQueries.Tile(entry.Proposal.Stand) == sTile
                && entry.Proposal.Reason == proposal.Reason)
                return entry.Verdict;
        return null;
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
            if (!threat.IsChainRepresentative)
                continue;
            if (!ChainInAllowance(ctx, threat, inAllowance))
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

    private static bool ChainInAllowance(in ActionContext ctx, ThreatRecord representative, Func<Vector2, bool> inAllowance)
    {
        foreach (ThreatRecord member in ctx.Senses.Threats.Threats)
        {
            if (member.ChainHead != representative.ChainHead)
                continue;
            if (member.Npc != null && member.Npc.active && inAllowance(member.Npc.Bottom))
                return true;
        }
        return inAllowance(representative.Npc.Bottom);
    }

    // Phase E proposes through ProposeFiringStands: the seven generators above this file's verdicts.
    // The old set — where the body is, the guard anchor, the hunt approach — went with the positioner's
    // firing-stand scoring, which named the stands instead of the weapons.

    private sealed record CandidateAttack(EvaluateAttackOutcomes.Attack Attack, int WeaponSlot, Vector2 Muzzle, Vector2 AimPoint, Vector2 Launch, int TargetSlot);

    /// <summary>One stand's simulated attacks, priced at every start the level names rather than re-simulated per start.</summary>
    private sealed record SimmedStand(List<CandidateAttack> Candidates);

    /// <summary>
    /// One priced segment: the segment, its valuation in its own start-relative frame, its attacks with
    /// absolute fire ticks, its impact-stamped kills, the context it was priced in, and its target slots.
    /// </summary>
    private sealed record PricedPiece(AttackSegment Segment, EvaluateAttackOutcomes.Valuation Value,
        List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)> Pairs,
        List<(int Target, int Tick)> Kills, EvaluateAttackOutcomes.PlanContext Context, HashSet<int> TargetSlots);

    /// <summary>
    /// The greedy segment from one stand at level one: every weapon against the proposal's targets at the
    /// aim the simulator prices best, simulated best upper bound first, the opener the candidate whose
    /// continuation values highest. Null when no use reaches any target from here. Aims compete on
    /// simulated damage only — the simulator is authoritative for geometry, and a learner that says aiming
    /// off pays must not move the shot off the intercept it proved; the learner prices weapons, targets
    /// and stands instead, which is the ruling the weapon-learning row holds.
    /// </summary>
    private static BeamNode? PriceLevelOne(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<EnemyForecast> enemies, List<EvaluateAttackOutcomes.Target> evalTargets,
        List<ThreatRecord> targets, StandProposal proposal, StandVerdict verdict, CombatWeights weights,
        int planId, int tick, int horizon, ref DecisionWorkBudget budget)
    {
        int travel = (int)MathF.Min(verdict.TravelTicks, horizon - 1);
        Vector2 muzzle = CompanionCombat.MuzzleAt(proposal.Stand);
        CombatWorld world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
        // No company gap: by the owner's ruling of 25 September 2026 a fight is never charged for the time the orb
        // spends away from the player, so no stand is priced below another, or dominated by it, for being further
        // from him. On the play that prompted the ruling, the three stands nearest the zombies below the player
        // were each dropped as `dominated:company-gap`, and the one kept arrived too late to land a hit.
        const float gap = 0f;
        Dictionary<int, int>? throws = StartingThrows(combat.Weapons);
        SimmedStand simmed = SimulateStand(ctx, combat, enemies, evalTargets, targets, proposal, muzzle, travel,
            Math.Max(0, combat.CooldownTicks - travel), horizon, world, ref budget, throws);
        if (simmed.Candidates.Count == 0 || budget.Cut)
            return null;
        PricedPiece? piece = PriceFromStart(ctx, evalTargets, simmed, proposal, verdict, weights, tick,
            tick + travel, Math.Max(0, combat.CooldownTicks), travel, horizon, remainingAt: null, tick, horizon,
            gap, usesLeft: throws);
        if (piece == null)
            return null;

        var validityTargets = new (int Slot, int Generation)[piece.TargetSlots.Count];
        int validityIndex = 0;
        foreach (int slot in piece.TargetSlots)
            validityTargets[validityIndex++] = (slot, HostileAttackSources.Generation(Main.npc[slot]));
        float admittedMax = 0f;
        var hostiles = new (int Slot, int Generation)[ctx.Senses.Threats.Threats.Count];
        // Where every admitted body was and how fast, so the commitment can notice one that leaves the place
        // it was priced at. Speed is the observed magnitude rather than the vector: the allowance has to
        // survive a walker turning round, which is ordinary motion and not a new fight.
        var motion = new (int Slot, Vector2 Centre, float Speed)[ctx.Senses.Threats.Threats.Count];
        int hostileIndex = 0;
        foreach (ThreatRecord threat in ctx.Senses.Threats.Threats)
        {
            admittedMax = MathF.Max(admittedMax, MathF.Max(threat.Urgency, threat.UrgencyToCompanion));
            hostiles[hostileIndex] = (threat.Npc.whoAmI, HostileAttackSources.Generation(threat.Npc));
            motion[hostileIndex++] = (threat.Npc.whoAmI, threat.Npc.Center, threat.Npc.velocity.Length());
        }
        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        var validity = new PlanValidity(TerrainChanges.Revision, AttackLearning.Revision, validityTargets, admittedMax,
            region.Centre, region.HalfSize, region.GapBeyond(proposal.Stand), tick, hostiles, motion);
        var killArray = new (int Slot, int Tick)[piece.Kills.Count];
        for (int i = 0; i < piece.Kills.Count; i++)
            killArray[i] = piece.Kills[i];
        float weighted = weights.Weighted(piece.Value.Outcome);
        var plan = new AttackPlan(planId, new[] { piece.Segment }, piece.Value.Outcome, weighted, validity,
            BudgetCut: false, killArray, AttackPlan.NameByDanger(new[] { piece.Segment }, evalTargets));
        return new BeamNode(plan,
            new List<List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>> { piece.Pairs },
            new List<EvaluateAttackOutcomes.PlanContext> { piece.Context }, piece.Kills);
    }

    /// <summary>
    /// Every weapon against the proposal's targets, bound-sorted, simulated best upper bound first up to
    /// the per-stand cap: the aims every start of this stand prices. The bound's cooldown is the hands'
    /// busy remainder when firing could start; the simulations predict the enemy at the fire tick, so a
    /// deeper stand's aims lead the arrival rather than the search tick.
    /// A specialised generator named this stand for one weapon — the area drop, the pierce line, the
    /// floor flank, the bank — so only that weapon is priced here. BestRange and HereAndCompany stay
    /// unbound: they are "any weapon" stands, and binding them collapsed the goons-then-close plan
    /// onto fighting from here.
    /// </summary>
    private static SimmedStand SimulateStand(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<EnemyForecast> enemies, IReadOnlyList<EvaluateAttackOutcomes.Target> evalTargets,
        List<ThreatRecord> targets, StandProposal proposal, Vector2 muzzle, int fireTickForSim, int boundCooldown,
        int horizon, CombatWorld world, ref DecisionWorkBudget budget, Dictionary<int, int>? usesLeft)
    {
        var weapons = combat.Weapons;
        var pairs = new List<(int Weapon, ThreatRecord Target, float Bound)>();
        bool bind = BindsWeapon(proposal.Reason) && proposal.WeaponSlot >= 0;
        foreach (ThreatRecord threat in targets)
        {
            if (!ProposalServes(proposal, threat.Npc.whoAmI))
                continue;
            List<EvaluateAttackOutcomes.Attack> assumed = ForecastUses.AssumedAttacks(combat, ctx, threat.Npc);
            var legal = new List<EvaluateAttackOutcomes.Attack>(assumed.Count);
            foreach (EvaluateAttackOutcomes.Attack attack in assumed)
            {
                if (bind && attack.Weapon != proposal.WeaponSlot)
                    continue;
                if (usesLeft != null && usesLeft.TryGetValue(attack.Weapon, out int remaining) && remaining <= 0)
                    continue;
                // AboveArea named the drop for the area weapon. Pricing that throw from here, a
                // ranging peak, or a flank makes a one-segment contact kill that the grenade-then-
                // pierce plan can never beat.
                if (proposal.Reason != StandReason.AboveArea && proposal.Reason != StandReason.AuditGrid
                    && HasArea(weapons[attack.Weapon]))
                    continue;
                legal.Add(attack);
            }
            foreach (EvaluateAttackOutcomes.Attack attack in legal)
            {
                float bound = EvaluateAttackOutcomes.Evaluate(attack, legal, evalTargets, boundCooldown,
                    horizon, usesLeft).Value;
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
                enemies, world, fireTickForSim, record: false, planning: true, ref budget);
            if (aimed == null || !budget.Check())
                continue;
            EvaluateAttackOutcomes.Attack? attack = ForecastUses.AttackFromUse(ctx, weapon, weaponSlot, npc,
                muzzle, aimed.Value.Use, aimed.Value.Aim, aimed.Value.Intercept, fireTickForSim, out string rej, out _);
            if (attack != null)
                candidates.Add(new CandidateAttack(attack, weaponSlot, muzzle, aimed.Value.Aim.AimPoint,
                    aimed.Value.Aim.LaunchDirection, npc.whoAmI));
        }
        return new SimmedStand(candidates);
    }

    /// <summary>
    /// The greedy segment from one stand at one start: the candidates alive as of the start, the opener
    /// the candidate whose continuation values highest. Null when nothing is alive to shoot or no use
    /// fits the remaining horizon. The context's travel counts from the start — zero once the start waits
    /// past arrival — and the stamps are absolute from the start.
    /// </summary>
    private static PricedPiece? PriceFromStart(in ActionContext ctx,
        IReadOnlyList<EvaluateAttackOutcomes.Target> evalTargets, SimmedStand simmed, StandProposal proposal,
        StandVerdict verdict, CombatWeights weights, int startAbs, int arrivalAbs, int cooldown, int travelCtx,
        int horizonRem, Dictionary<int, float>? remainingAt, int tick, int horizon, float gapForContext,
        Dictionary<(int Target, int Weapon), (float Chance, int Until)>? initialDebuffs = null,
        int maxSteps = EvaluateAttackOutcomes.MaxAttacks,
        Dictionary<int, int>? usesLeft = null)
    {
        var context = new EvaluateAttackOutcomes.PlanContext(travelCtx, verdict.HarmAtStand,
            verdict.HarmAlongTravel, Math.Max(1, ctx.Player.statLife), Math.Max(1, ctx.Npc.life),
            Math.Max(1, ctx.Companion.Mana.Max), gapForContext);
        var candidates = new List<CandidateAttack>(simmed.Candidates.Count);
        foreach (CandidateAttack candidate in simmed.Candidates)
            if (remainingAt == null
                || (remainingAt.TryGetValue(candidate.TargetSlot, out float life) && life > 0f))
                candidates.Add(candidate);
        if (candidates.Count == 0)
            return null;
        var attacks = new List<EvaluateAttackOutcomes.Attack>(candidates.Count);
        foreach (CandidateAttack candidate in candidates)
            attacks.Add(candidate.Attack);

        List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>? sequence = null;
        List<(int Target, int Tick)>? kills = null;
        EvaluateAttackOutcomes.Valuation? valued = null;
        float best = float.NegativeInfinity;
        foreach (CandidateAttack candidate in candidates)
        {
            var trySequence = new List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>();
            var tryKills = new List<(int Target, int Tick)>();
            EvaluateAttackOutcomes.Valuation tried = EvaluateAttackOutcomes.EvaluateVector(candidate.Attack,
                attacks, evalTargets, cooldown, horizonRem, context, weights, startAbs, trySequence, tryKills,
                initialRemaining: remainingAt, initialDebuffs: initialDebuffs, maxSteps: maxSteps,
                usesLeft: usesLeft);
            float value = weights.Weighted(tried.Outcome);
            if (value > best)
            {
                best = value;
                valued = tried;
                sequence = trySequence;
                kills = tryKills;
            }
        }
        if (sequence == null || sequence.Count == 0 || valued == null)
            return null;

        var uses = new PlannedUse[sequence.Count];
        var targetSlots = new HashSet<int>();
        for (int i = 0; i < sequence.Count; i++)
        {
            CandidateAttack meta = candidates[0];
            foreach (CandidateAttack candidate in candidates)
                if (ReferenceEquals(candidate.Attack, sequence[i].Attack)) { meta = candidate; break; }
            float targetDamage = 0f;
            foreach (EvaluateAttackOutcomes.Hit hit in sequence[i].Attack.Hits)
                if (hit.Target == meta.TargetSlot)
                    targetDamage += hit.Damage;
            uses[i] = new PlannedUse(meta.WeaponSlot, meta.Muzzle, meta.AimPoint, meta.Launch, sequence[i].FireTick,
                meta.TargetSlot, targetDamage, sequence[i].Attack.TargetImpactTicks);
            // The pairing AIC-422 is about: one aim, solved for firing on arrival, handed to a use that
            // may be scheduled a cooldown or more later. Printed rather than asserted because whether
            // the two may differ at all is the open question, and a row asserting either answer would be
            // asserting the fix before it is chosen.
            if (System.Environment.GetEnvironmentVariable("AIC_TRACE_USEAIM") != null)
                System.Console.WriteLine($"USEAIM i={i} aimSolvedAtFireTick={travelCtx} scheduledFireTick={sequence[i].FireTick} "
                    + $"aim={meta.AimPoint.X:0.0},{meta.AimPoint.Y:0.0} target={meta.TargetSlot} impact={sequence[i].Attack.TargetImpactTicks}");
            targetSlots.Add(meta.TargetSlot);
        }
        var segment = new AttackSegment(proposal, verdict, arrivalAbs, Math.Max(arrivalAbs, startAbs),
            tick + horizon, uses, SegmentEnd.Horizon);
        return new PricedPiece(segment, valued, sequence, kills!, context, targetSlots);
    }

    private static bool ProposalServes(StandProposal proposal, int slot)
    {
        foreach (int served in proposal.TargetSlots)
            if (served == slot) return true;
        return false;
    }

    /// <summary>
    /// Specialised generators named the stand for one weapon. BestRange and HereAndCompany did not —
    /// they sample "a weapon peaks here" and "a use reaches from here", and the search still prices
    /// every handed weapon at those rocks.
    /// </summary>
    private static bool BindsWeapon(StandReason reason)
        => reason is StandReason.AboveArea or StandReason.PierceLines
            or StandReason.FloorFlanks or StandReason.BankShots;

    private static bool HasArea(CompanionWeapon weapon)
        => LearnHitResponses.ResponseFor(weapon.ProjectileType).Area.Radius > 0f;

    private static Dictionary<int, int>? StartingThrows(IReadOnlyList<CompanionWeapon> weapons)
    {
        Dictionary<int, int>? throws = null;
        for (int i = 0; i < weapons.Count; i++)
        {
            int n = weapons[i].UsesRemaining;
            if (n >= int.MaxValue)
                continue;
            throws ??= new Dictionary<int, int>();
            throws[i] = n;
        }
        return throws;
    }

    private static Dictionary<int, int>? AfterUses(Dictionary<int, int>? throws,
        IReadOnlyList<List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)>> pairs, int untilAbs)
    {
        if (throws == null)
            return null;
        var next = new Dictionary<int, int>(throws);
        foreach (List<(EvaluateAttackOutcomes.Attack Attack, int FireTick)> segment in pairs)
            foreach ((EvaluateAttackOutcomes.Attack attack, int fire) in segment)
                if (fire < untilAbs && next.ContainsKey(attack.Weapon))
                    next[attack.Weapon] = Math.Max(0, next[attack.Weapon] - 1);
        return next;
    }

}
