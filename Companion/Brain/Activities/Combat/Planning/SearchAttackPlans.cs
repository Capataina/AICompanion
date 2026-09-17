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
    public sealed record SearchResult(AttackPlan? Plan, OfferEligibility Eligibility, string Reason, int FrontSize);

    public static SearchResult SearchDepthOne(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        Func<Vector2, bool> inAllowance, CombatWeights weights, int planId, ref PlanningBudget budget)
    {
        int tick = ctx.Senses.Tick;
        var weapons = combat.Weapons;
        if (weapons.Count == 0)
            return new SearchResult(null, OfferEligibility.NoOpportunity, "no-weapon", 0);
        IReadOnlyList<EnemyForecast> enemies = combat.EnsureForecast(ctx);
        List<ThreatRecord> targets = ProposalTargets(ctx, combat, inAllowance, out int deferredExcluded);
        if (targets.Count == 0)
        {
            if (deferredExcluded > 0)
                return new SearchResult(null, OfferEligibility.KnownUnusable, "engagement-deferred-no-progress", 0);
            return new SearchResult(null, OfferEligibility.NoOpportunity, "no-eligible-target", 0);
        }

        // The approach resolve needs a flight profile or the positioner refuses the request: the
        // longest reach in hand, so the approach considers every stand the best weapon could shoot from.
        // The profile only proposes — every weapon is simulated from the stand before a plan is priced.
        FlightModel? approachProfile = null;
        foreach (CompanionWeapon weapon in weapons)
            if (approachProfile == null || weapon.Model.Reach > approachProfile.Value.Reach)
                approachProfile = weapon.Model;
        List<StandProposal> proposals = ProposeToday(ctx, combat, positioner, targets, approachProfile);
        if (proposals.Count == 0)
            return new SearchResult(null, OfferEligibility.NoOpportunity, "no-eligible-target", 0);

        var candidates = new List<AttackPlan>();
        bool sawReachable = false, sawUndecided = false;
        foreach (StandProposal proposal in proposals)
        {
            StandVerdict verdict = Assess(ctx, positioner, inAllowance, proposal);
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
                return new SearchResult(null, OfferEligibility.Unresolved, "budget-cut", candidates.Count);
        }
        if (!sawReachable)
        {
            if (sawUndecided)
                return new SearchResult(null, OfferEligibility.Unresolved, "stands-undecided", 0);
            return new SearchResult(null, OfferEligibility.KnownUnusable, "no-reachable-stand", 0);
        }
        if (candidates.Count == 0)
        {
            // A reachable stand with no solving use settles nothing about the stands still undecided:
            // answering unusable would report an unanswered search as a proven absence.
            if (sawUndecided)
                return new SearchResult(null, OfferEligibility.Unresolved, "stands-undecided", 0);
            return new SearchResult(null, OfferEligibility.KnownUnusable, "no-use-reaches-target", 0);
        }
        List<AttackPlan> front = KeepOnlyUndominated.Filter(candidates, plan => plan.Outcome);
        if (System.Environment.GetEnvironmentVariable("AIC_DEBUG_PLAN") == "1")
            foreach (AttackPlan c in candidates)
                System.Console.WriteLine($"  DEBUG cand primary={c.PrimaryTarget} stand={c.Segments[0].Stand.Stand.X:0},{c.Segments[0].Stand.Stand.Y:0} weighted={c.Weighted:0.00} kills={c.TargetKillTicks?.Length ?? 0} travel={c.Segments[0].Verdict.TravelTicks:0} outcome={c.Outcome}");
        AttackPlan best = front[0];
        foreach (AttackPlan plan in front)
            if (plan.Weighted > best.Weighted)
                best = plan;
        return new SearchResult(best, OfferEligibility.Usable, "planned-attack", front.Count);
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

    /// <summary>
    /// Today's stand candidates: where the body hovers, and per target the guard anchor and the hunt
    /// approach the positioner resolves. Deduplicated to half a tile, so a target at the body's feet does
    /// not price one rock three times. Phase E's generators replace this set; the verdicts and the search
    /// above them stay.
    /// </summary>
    private static List<StandProposal> ProposeToday(in ActionContext ctx, CompanionCombat combat, Positioner positioner,
        List<ThreatRecord> targets, FlightModel? approachProfile)
    {
        var proposals = new List<StandProposal>();
        var seen = new HashSet<(int X, int Y)>();
        Vector2 here = ctx.Npc.Center;
        seen.Add(HalfTile(here));
        var allSlots = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            allSlots[i] = targets[i].Npc.whoAmI;
        proposals.Add(new StandProposal(here, StandReason.Here, -1, allSlots));

        float leash = CompanionPreferences.Current.NewActivityRadius;
        foreach (ThreatRecord threat in targets)
        {
            NPC npc = threat.Npc;
            // The air the enemy is in, not the floor under it: a grounded enemy's feet tile is solid rock,
            // and hovering that tile's centre is hovering inside the floor — unreachable, so the anchor never
            // proposed anything for exactly the enemies that need a reposition.
            Vector2 toThreat = npc.Center - ctx.Senses.Player.Bottom;
            Vector2 anchor = toThreat.LengthSquared() <= leash * leash
                ? npc.Center
                : ctx.Senses.Player.Bottom + Vector2.Normalize(toThreat) * leash;
            Vector2 hover = MovementQueries.HoverPoint(MovementQueries.Tile(anchor));
            if (seen.Add(HalfTile(hover)))
                proposals.Add(new StandProposal(hover, StandReason.GuardAnchor, -1, new[] { npc.whoAmI }));
            if (!combat.TryGetApproach(npc.whoAmI, HostileAttackSources.Generation(npc), npc.Center, ctx.Npc.Center,
                TerrainChanges.Revision, out Vector2? approach))
            {
                approach = positioner.QueryAttackStand(new PositionRequest(RequestKind.LineOfFire, npc.Center, npc), ctx.Senses, approachProfile);
                combat.StoreApproach(npc.whoAmI, HostileAttackSources.Generation(npc), npc.Center, ctx.Npc.Center,
                    TerrainChanges.Revision, approach);
            }
            if (approach is { } resolved && seen.Add(HalfTile(resolved)))
                proposals.Add(new StandProposal(resolved, StandReason.HuntApproach, -1, new[] { npc.whoAmI }));
        }
        return proposals;
    }

    private static (int X, int Y) HalfTile(Vector2 point) => ((int)(point.X / 8f), (int)(point.Y / 8f));

    /// <summary>
    /// One proposal's verdict from what the positioner already owns: the reach sense's three-valued answer
    /// and travel estimate, the predicted exposure at the stand and sampled along the travel line, and the
    /// allowance. Where the body already hovers is reachable with no travel by definition, whatever the flood
    /// has claimed so far — no route is needed to stay. Phase E runs this as one batch; the questions stay.
    /// </summary>
    private static StandVerdict Assess(in ActionContext ctx, Positioner positioner, Func<Vector2, bool> inAllowance,
        StandProposal proposal)
    {
        Vector2 stand = proposal.Stand;
        bool here = proposal.Reason == StandReason.Here;
        Point tile = MovementQueries.Tile(stand);
        ReachVerdict reach = here ? ReachVerdict.Reachable : positioner.ReachOf(tile);
        float travel = 0f;
        if (!here)
        {
            Point feet = MovementQueries.Tile(ctx.Npc.Center);
            travel = positioner.EstimatedTravelTicks(feet, tile)
                ?? Vector2.Distance(ctx.Npc.Center, stand) / OrbPace.MaxSpeed;
        }
        float atStand = Positioner.PredictedHarmAt(stand, ctx.Senses, ctx.Npc.life);
        float alongTravel = 0f;
        for (int sample = 1; sample <= 4; sample++)
        {
            Vector2 point = Vector2.Lerp(ctx.Npc.Center, stand, sample / 5f);
            alongTravel = MathF.Max(alongTravel, Positioner.PredictedHarmAt(point, ctx.Senses, ctx.Npc.life));
        }
        bool allowed = inAllowance(stand);
        string reason = reach switch
        {
            ReachVerdict.Reachable => allowed ? "reachable-stand" : "stand-outside-allowance",
            ReachVerdict.NotYet => "stand-undecided",
            _ => "stand-unreachable",
        };
        return new StandVerdict(stand, reach, MathF.Max(0f, travel), atStand, alongTravel, allowed, reason);
    }

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
                enemies, world, travel, record: false, planning: true);
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
