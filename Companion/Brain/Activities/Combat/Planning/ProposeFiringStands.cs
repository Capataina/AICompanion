#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// Stand proposals from what the weapons do: seven generators, each reading knowledge and the enemy
/// forecast, none naming a weapon category. Every generator that probes spends the decision's own
/// budget on its probe sims and stops when its share is spent, so how many each may emit is taken
/// from the budget, never a constant — the share is a fourteenth of what remains per generator with
/// six or fewer left to run, which leaves the search at least half the decision under any allowance
/// and binds nothing under an unbounded one. Proposals are deduplicated to half a tile across all
/// seven, so two generators naming the same rock do not price it twice.
///
/// A probe is one simulated use, never a priced plan: the intercept aim flown from the candidate
/// muzzle, its damage on the target summed. The search prices every emitted stand truly afterwards;
/// the generators only have to name stands worth pricing.
/// </summary>
public static class ProposeFiringStands
{
    private const int GeneratorCount = 7;

    /// <summary>Predicted ticks out the generators draw chains and centroids at: mid-segment, where the fight will be.</summary>
    private const int GeometryTick = 30;

    public static List<StandProposal> Propose(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<EnemyForecast> enemies, List<ThreatRecord> targets, ref PlanningBudget budget)
    {
        var proposals = new List<StandProposal>();
        var seen = new HashSet<(int X, int Y)>();
        var weapons = combat.Weapons;
        var allSlots = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            allSlots[i] = targets[i].Npc.whoAmI;
        int left = GeneratorCount;
        HereAndCompany(ctx, combat, weapons, enemies, targets, allSlots, proposals, seen, ref budget, ref left);
        BestRange(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        PierceLines(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        FloorFlanks(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        AboveArea(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        BankShots(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        SafeRange(ctx, weapons, targets, proposals, seen);
        return proposals;
    }

    /// <summary>
    /// This generator's probe allowance: a fourteenth of what the decision still holds per generator
    /// left to run. Seven generators spending a fourteenth each leave the search half the decision;
    /// under an unbounded budget the share is effectively infinite and the candidate grids bind.
    /// </summary>
    private static int Share(ref PlanningBudget budget, ref int left)
    {
        int remaining = budget.AllowanceSimulations == int.MaxValue ? int.MaxValue
            : Math.Max(0, budget.AllowanceSimulations - budget.Simulations);
        left--;
        return remaining == int.MaxValue ? int.MaxValue : remaining / (2 * GeneratorCount);
    }

    private static void Emit(List<StandProposal> proposals, HashSet<(int X, int Y)> seen,
        Vector2 stand, StandReason reason, int weaponSlot, int[] targetSlots)
    {
        if (seen.Add(HalfTile(stand)))
            proposals.Add(new StandProposal(stand, reason, weaponSlot, targetSlots));
    }

    private static (int X, int Y) HalfTile(Vector2 point) => ((int)(point.X / 8f), (int)(point.Y / 8f));

    private static EnemyForecast? ForecastFor(IReadOnlyList<EnemyForecast> enemies, int slot)
    {
        foreach (EnemyForecast enemy in enemies)
            if (enemy.Slot == slot)
                return enemy;
        return null;
    }

    /// <summary>
    /// The representative projectile a weapon's laws are read through: the volley shape's most common
    /// slot type, or the weapon's fallback before any shape is learned. The same choice
    /// <see cref="SolveAims"/> prices with, so a generator's gate and the search's aims read one law.
    /// </summary>
    private static int RepresentativeType(CompanionWeapon weapon)
    {
        if (!LearnVolleyShapes.HasShape(weapon.ItemType))
            return weapon.ProjectileType;
        VolleyShape shape = LearnVolleyShapes.ShapeFor(weapon.ItemType);
        var counts = new Dictionary<int, int>();
        foreach (VolleySlot slot in shape.Slots)
        {
            int type = slot.ProjectileType ?? weapon.ProjectileType;
            counts[type] = counts.TryGetValue(type, out int n) ? n + 1 : 1;
        }
        int best = weapon.ProjectileType, bestCount = 0;
        foreach ((int type, int count) in counts)
            if (count > bestCount) { best = type; bestCount = count; }
        return best;
    }

    /// <summary>
    /// One probe: the intercept aim from this muzzle, simulated once, damage on the target summed and
    /// distinct bodies counted. Cached, so a stand the search prices afterwards does not pay twice for
    /// the aim the generator already flew.
    /// </summary>
    private static float ProbeYield(WeaponId id, Vector2 muzzle, EnemyForecast target, CombatWorld world,
        IReadOnlyList<EnemyForecast> enemies, ModifierState modifiers, ref PlanningBudget budget,
        out int bodies, out SimulatedUse? flown)
    {
        bodies = 0;
        flown = null;
        IReadOnlyList<AimCandidate> aims = SolveAims.For(id, muzzle, target, world, enemies, 0, ref budget);
        if (aims.Count == 0 || !budget.Check())
            return 0f;
        AimCandidate intercept = aims[0];
        int knowledge = KnowledgeRevision.Current;
        if (!CacheSimulatedUses.TryGet(id, modifiers, muzzle, intercept.AimPoint, 0, knowledge,
            world.RefreshCount, out SimulatedUse? use) || use == null)
        {
            use = SimulateUse.Simulate(id, muzzle, intercept.AimPoint, intercept.LaunchDirection,
                world, enemies, modifiers, 0, ref budget);
            CacheSimulatedUses.Store(id, modifiers, muzzle, intercept.AimPoint, 0, knowledge,
                world.RefreshCount, use);
        }
        flown = use;
        float damage = 0f;
        var struck = new HashSet<int>();
        foreach (SimHit hit in use.Hits)
        {
            struck.Add(hit.Slot);
            if (hit.Slot == target.Slot)
                damage += hit.Damage;
        }
        bodies = struck.Count;
        return damage;
    }

    /// <summary>
    /// Where the body is — free, always, no probe — and points inside his predicted region from which
    /// a use reaches a target. The region samples are its centre and six points at half extent; each
    /// keeps the targets some weapon lands on from it, proved by the cheap true/false rather than a
    /// priced aim.
    /// </summary>
    private static void HereAndCompany(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, int[] allSlots, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        Emit(proposals, seen, ctx.Npc.Center, StandReason.HereAndCompany, -1, allSlots);
        int stopAt = budget.Simulations + Share(ref budget, ref left);
        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        var points = new Vector2[]
        {
            region.Centre,
            region.Centre + new Vector2(region.HalfSize.X / 2f, 0f),
            region.Centre - new Vector2(region.HalfSize.X / 2f, 0f),
            region.Centre + new Vector2(0f, region.HalfSize.Y / 2f),
            region.Centre - new Vector2(0f, region.HalfSize.Y / 2f),
            region.Centre + new Vector2(region.HalfSize.X / 2f, region.HalfSize.Y / 2f),
            region.Centre - new Vector2(region.HalfSize.X / 2f, region.HalfSize.Y / 2f),
        };
        for (int p = 0; p < points.Length; p++)
        {
            Vector2 muzzle = CompanionCombat.MuzzleAt(points[p]);
            var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
            var reached = new List<int>();
            int serving = -1;
            for (int w = 0; w < weapons.Count; w++)
            {
                if (budget.Simulations >= stopAt || !budget.Check())
                    return;
                WeaponId id = SimulateUse.Identify(weapons[w], ctx, w);
                foreach (ThreatRecord threat in targets)
                {
                    EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
                    if (forecast == null)
                        continue;
                    if (SolveAims.FirstLanding(id, muzzle, forecast, world, enemies, 0, ref budget) != null)
                    {
                        reached.Add(threat.Npc.whoAmI);
                        if (serving < 0)
                            serving = w;
                    }
                }
            }
            if (reached.Count > 0)
                Emit(proposals, seen, points[p], StandReason.HereAndCompany, serving, reached.ToArray());
        }
    }

    /// <summary>
    /// For each weapon and each priority target, the distance the simulated yield per use peaks at:
    /// six distances flown along the line from the target toward the body and toward the player's
    /// side, plus three bearings biased off those lines, the peak of each kept when a use reaches.
    /// A flat long weapon peaks far, a spread weapon close, against the same lone target — the sim
    /// prices the difference, so the generator names no category.
    /// </summary>
    private static void BestRange(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        int stopAt = budget.Simulations + Share(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        foreach (ThreatRecord threat in targets)
        {
            EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
            if (forecast == null)
                continue;
            Vector2 centre = forecast.PredictedCentre(GeometryTick);
            Vector2 toBody = ctx.Npc.Center - centre;
            Vector2 toPlayer = ctx.Player.Center - centre;
            if (toBody == Vector2.Zero)
                toBody = Vector2.UnitX;
            if (toPlayer == Vector2.Zero)
                toPlayer = -Vector2.UnitX;
            toBody = Vector2.Normalize(toBody);
            toPlayer = Vector2.Normalize(toPlayer);
            var lines = new Vector2[]
            {
                toBody, toPlayer,
                Vector2.Normalize(toBody.RotatedBy(0.7f)), Vector2.Normalize(toBody.RotatedBy(-0.7f)),
                -Vector2.UnitY,
            };
            for (int w = 0; w < weapons.Count; w++)
            {
                float reach = weapons[w].IsSwing
                    ? SwingStandReach(weapons[w]) : MathF.Max(48f, weapons[w].Reach);
                WeaponId id = SimulateUse.Identify(weapons[w], ctx, w);
                foreach (Vector2 line in lines)
                {
                    Vector2 peak = centre;
                    float peakYield = 0f;
                    for (int s = 1; s <= 6; s++)
                    {
                        if (budget.Simulations >= stopAt || !budget.Check())
                            return;
                        Vector2 stand = centre + line * (reach * s / 6f);
                        Vector2 muzzle = CompanionCombat.MuzzleAt(stand);
                        var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
                        float yield = ProbeYield(id, muzzle, forecast, world, enemies, modifiers,
                            ref budget, out _, out _);
                        if (yield > peakYield + 1e-6f)
                        {
                            peakYield = yield;
                            peak = stand;
                        }
                    }
                    if (peakYield > 0f)
                        Emit(proposals, seen, peak, StandReason.BestRange, w, new[] { threat.Npc.whoAmI });
                }
            }
        }
    }

    private static float SwingStandReach(CompanionWeapon weapon)
        => weapon is ItemWeapon item ? MathF.Max(48f, item.SwingReach + 32f) : 80f;

    /// <summary>
    /// For any weapon whose simulated pierce exceeds one, points on the extension of lines through
    /// chains of predicted bodies — worm segments are a chain, because each is its own forecast — at
    /// three distances along the line, both ends. Each candidate is proved by flying it: the sim must
    /// strike two distinct bodies or the stand is not a pierce line.
    /// </summary>
    private static void PierceLines(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        if (targets.Count < 2)
            return;
        int stopAt = budget.Simulations + Share(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        for (int w = 0; w < weapons.Count; w++)
        {
            if (!Pierces(weapons[w]))
                continue;
            float reach = MathF.Max(48f, weapons[w].Reach);
            WeaponId id = SimulateUse.Identify(weapons[w], ctx, w);
            for (int a = 0; a < targets.Count; a++)
            {
                EnemyForecast? first = ForecastFor(enemies, targets[a].Npc.whoAmI);
                if (first == null)
                    continue;
                for (int b = a + 1; b < targets.Count; b++)
                {
                    EnemyForecast? second = ForecastFor(enemies, targets[b].Npc.whoAmI);
                    if (second == null)
                        continue;
                    Vector2 pa = first.PredictedCentre(GeometryTick);
                    Vector2 pb = second.PredictedCentre(GeometryTick);
                    Vector2 along = pb - pa;
                    if (along == Vector2.Zero)
                        continue;
                    along = Vector2.Normalize(along);
                    Vector2 mid = (pa + pb) / 2f;
                    int[] chain = new[] { targets[a].Npc.whoAmI, targets[b].Npc.whoAmI };
                    foreach (float end in new[] { 1f, -1f })
                    {
                        Vector2 best = mid;
                        int bestBodies = 1;
                        float bestDamage = 0f;
                        foreach (float fraction in new[] { 0.35f, 0.65f, 0.95f })
                        {
                            if (budget.Simulations >= stopAt || !budget.Check())
                                return;
                            Vector2 stand = mid + along * end * reach * fraction;
                            Vector2 muzzle = CompanionCombat.MuzzleAt(stand);
                            var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
                            float damage = ProbeYield(id, muzzle, first, world, enemies, modifiers,
                                ref budget, out int bodies, out _);
                            if (bodies > bestBodies || (bodies == bestBodies && damage > bestDamage + 1e-6f))
                            {
                                bestBodies = bodies;
                                bestDamage = damage;
                                best = stand;
                            }
                        }
                        if (bestBodies >= 2)
                            Emit(proposals, seen, best, StandReason.PierceLines, w, chain);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether a use of this weapon can strike two bodies: infinite or multi penetrate at the sample,
    /// or the learned bodies-per-penetrate saying so. The gate only skips the hopeless; the line sim
    /// proves the rest.
    /// </summary>
    private static bool Pierces(CompanionWeapon weapon)
    {
        int type = RepresentativeType(weapon);
        float ratio = LearnHitResponses.ResponseFor(type).BodiesPerPenetrate;
        if (!ContentSamples.ProjectilesByType.TryGetValue(type, out Projectile? sample) || sample == null)
            return ratio > 1f;
        return sample.penetrate < 0 || sample.penetrate * ratio > 1f;
    }

    /// <summary>
    /// For a law with gravity and a reflecting or stopping floor response, points at the group's two
    /// flanks, low, at three heights the shot arcs down from. Each height is proved by landing: the
    /// sim must strike a group member from the flank or the stand is scenery.
    /// </summary>
    private static void FloorFlanks(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        Vector2 centroid;
        {
            Vector2 sum = Vector2.Zero;
            int count = 0;
            foreach (ThreatRecord threat in targets)
            {
                EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
                if (forecast == null)
                    continue;
                sum += forecast.PredictedCentre(GeometryTick);
                count++;
            }
            if (count == 0)
                return;
            centroid = sum / count;
        }
        int stopAt = budget.Simulations + Share(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        Vector2 across = centroid - ctx.Npc.Center;
        across = across == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(new Vector2(-across.Y, across.X));
        float flank = 64f;
        foreach (ThreatRecord threat in targets)
        {
            EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
            if (forecast != null)
                flank = MathF.Max(flank, Vector2.Distance(forecast.PredictedCentre(GeometryTick), centroid) + 64f);
        }
        var group = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            group[i] = targets[i].Npc.whoAmI;
        for (int w = 0; w < weapons.Count; w++)
        {
            FlightLaw law = FitFlightLaws.LawFor(RepresentativeType(weapons[w]));
            if (law.Gravity == null || (law.Wall.Kind != WallKind.Reflects && law.Wall.Kind != WallKind.Stops))
                continue;
            WeaponId id = SimulateUse.Identify(weapons[w], ctx, w);
            EnemyForecast? aimAt = ForecastFor(enemies, targets[0].Npc.whoAmI);
            if (aimAt == null)
                continue;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector2 best = centroid;
                float bestYield = 0f;
                foreach (float above in new[] { 32f, 80f, 128f })
                {
                    if (budget.Simulations >= stopAt || !budget.Check())
                        return;
                    Vector2 stand = centroid + across * side * flank - new Vector2(0f, above);
                    Vector2 muzzle = CompanionCombat.MuzzleAt(stand);
                    var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
                    float yield = ProbeYield(id, muzzle, aimAt, world, enemies, modifiers,
                        ref budget, out _, out _);
                    if (yield > bestYield + 1e-6f)
                    {
                        bestYield = yield;
                        best = stand;
                    }
                }
                if (bestYield > 0f)
                    Emit(proposals, seen, best, StandReason.FloorFlanks, w, group);
            }
        }
    }

    /// <summary>
    /// For a law with learned area, points above the group from which the drop lands central when its
    /// trigger fires. Proved two ways: a death inside the learned radius of the centroid, or damage on
    /// a group member — the burst and the direct hit both count, because both are the area working.
    /// </summary>
    private static void AboveArea(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        Vector2 centroid;
        {
            Vector2 sum = Vector2.Zero;
            int count = 0;
            foreach (ThreatRecord threat in targets)
            {
                EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
                if (forecast == null)
                    continue;
                sum += forecast.PredictedCentre(GeometryTick);
                count++;
            }
            if (count == 0)
                return;
            centroid = sum / count;
        }
        int stopAt = budget.Simulations + Share(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        var group = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            group[i] = targets[i].Npc.whoAmI;
        for (int w = 0; w < weapons.Count; w++)
        {
            AreaResponse area = LearnHitResponses.ResponseFor(RepresentativeType(weapons[w])).Area;
            if (area.Radius <= 0f)
                continue;
            WeaponId id = SimulateUse.Identify(weapons[w], ctx, w);
            EnemyForecast? aimAt = ForecastFor(enemies, targets[0].Npc.whoAmI);
            if (aimAt == null)
                continue;
            Vector2 best = centroid;
            float bestYield = 0f;
            foreach (float above in new[] { 64f, 128f, 192f })
            {
                if (budget.Simulations >= stopAt || !budget.Check())
                    return;
                Vector2 stand = centroid - new Vector2(0f, above);
                Vector2 muzzle = CompanionCombat.MuzzleAt(stand);
                var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
                float yield = ProbeYield(id, muzzle, aimAt, world, enemies, modifiers,
                    ref budget, out _, out SimulatedUse? flown);
                bool centred = false;
                if (flown != null)
                    foreach (SimDeath death in flown.Deaths)
                        if (Vector2.Distance(death.Position, centroid) <= area.Radius)
                        {
                            centred = true;
                            break;
                        }
                if ((yield > 0f || centred) && yield >= bestYield)
                {
                    bestYield = yield;
                    best = stand;
                }
            }
            if (bestYield > 0f)
                Emit(proposals, seen, best, StandReason.AboveArea, w, group);
        }
    }

    /// <summary>
    /// For a law with a reflecting wall response and a target with no line from the body, points in
    /// his region from which the bank sweep reaches it. The no-line gate is read once per target from
    /// where the body is; each stand then flies the bank aims and keeps the ones that land. A straight
    /// weapon proposes nothing here, which is the point: only a bouncing weapon has this plan.
    /// </summary>
    private static void BankShots(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        int stopAt = budget.Simulations + Share(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        Vector2 body = CompanionCombat.MuzzleAt(ctx.Npc.Center);
        var bodyWorld = CombatWorld.Current(body, ctx.Player.Center, TerrainChanges.Revision);
        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        var points = new Vector2[]
        {
            region.Centre,
            region.Centre + new Vector2(region.HalfSize.X / 2f, 0f),
            region.Centre - new Vector2(region.HalfSize.X / 2f, 0f),
            region.Centre + new Vector2(0f, region.HalfSize.Y / 2f),
            region.Centre - new Vector2(0f, region.HalfSize.Y / 2f),
            region.Centre + new Vector2(region.HalfSize.X / 2f, region.HalfSize.Y / 2f),
            region.Centre - new Vector2(region.HalfSize.X / 2f, region.HalfSize.Y / 2f),
            ctx.Player.Center,
        };
        for (int w = 0; w < weapons.Count; w++)
        {
            if (FitFlightLaws.LawFor(RepresentativeType(weapons[w])).Wall.Kind != WallKind.Reflects)
                continue;
            WeaponId id = SimulateUse.Identify(weapons[w], ctx, w);
            foreach (ThreatRecord threat in targets)
            {
                EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
                if (forecast == null)
                    continue;
                if (budget.Simulations >= stopAt || !budget.Check())
                    return;
                if (SolveAims.FirstLanding(id, body, forecast, bodyWorld, enemies, 0, ref budget) != null)
                    continue;
                int knowledge = KnowledgeRevision.Current;
                foreach (Vector2 point in points)
                {
                    if (budget.Simulations >= stopAt || !budget.Check())
                        return;
                    Vector2 muzzle = CompanionCombat.MuzzleAt(point);
                    var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
                    foreach (AimCandidate bank in SolveAims.BankAims(muzzle, forecast, world, 1))
                    {
                        if (budget.Simulations >= stopAt || !budget.Check())
                            return;
                        SimulatedUse use;
                        if (!CacheSimulatedUses.TryGet(id, modifiers, muzzle, bank.AimPoint, 1, knowledge,
                            world.RefreshCount, out SimulatedUse? cached) || cached == null)
                        {
                            use = SimulateUse.Simulate(id, muzzle, bank.AimPoint, bank.LaunchDirection,
                                world, enemies, modifiers, 1, ref budget);
                            CacheSimulatedUses.Store(id, modifiers, muzzle, bank.AimPoint, 1, knowledge,
                                world.RefreshCount, use);
                        }
                        else
                        {
                            use = cached;
                        }
                        bool lands = false;
                        foreach (SimHit hit in use.Hits)
                            if (hit.Slot == threat.Npc.whoAmI)
                            {
                                lands = true;
                                break;
                            }
                        if (lands)
                        {
                            Emit(proposals, seen, point, StandReason.BankShots, w, new[] { threat.Npc.whoAmI });
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// When the companion's harm weight runs high — missing life and own danger summing past one, the
    /// point the weight shape triples its base — points at the far edge of the longest-reaching
    /// weapon's useful range, on his side of the threats. No probe: the search prices these on the
    /// harm objective, which is the only reason they exist.
    /// </summary>
    private static void SafeRange(in ActionContext ctx, IReadOnlyList<CompanionWeapon> weapons,
        List<ThreatRecord> targets, List<StandProposal> proposals, HashSet<(int X, int Y)> seen)
    {
        float missing = ctx.Npc.lifeMax > 0 ? 1f - ctx.Npc.life / (float)ctx.Npc.lifeMax : 0f;
        if (missing + Math.Clamp(ctx.Senses.Threats.CompanionDanger, 0f, 1f) < 1f)
            return;
        float reach = 0f;
        int longest = -1;
        for (int w = 0; w < weapons.Count; w++)
            if (!weapons[w].IsSwing && weapons[w].Reach > reach)
            {
                reach = weapons[w].Reach;
                longest = w;
            }
        if (longest < 0 || reach <= 0f)
            return;
        Vector2 centroid = Vector2.Zero;
        foreach (ThreatRecord threat in targets)
            centroid += threat.Npc.Center;
        centroid /= Math.Max(1, targets.Count);
        Vector2 away = ctx.Player.Center - centroid;
        away = away == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(away);
        float edge = reach * 0.9f;
        Emit(proposals, seen, centroid + away * edge, StandReason.SafeRange, longest,
            AllSlots(targets));
        Emit(proposals, seen, centroid + away.RotatedBy(1.0f) * edge, StandReason.SafeRange, longest,
            AllSlots(targets));
        Emit(proposals, seen, centroid + away.RotatedBy(-1.0f) * edge, StandReason.SafeRange, longest,
            AllSlots(targets));
    }

    private static int[] AllSlots(List<ThreatRecord> targets)
    {
        var slots = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            slots[i] = targets[i].Npc.whoAmI;
        return slots;
    }
}
