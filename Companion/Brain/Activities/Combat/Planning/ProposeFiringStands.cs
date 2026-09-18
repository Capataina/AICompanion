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
        IReadOnlyList<EnemyForecast> enemies, List<ThreatRecord> targets, ref PlanningBudget budget,
        Vector2? origin = null, bool disableBankAims = false)
    {
        var proposals = new List<StandProposal>();
        var seen = new HashSet<(int X, int Y)>();
        var weapons = combat.Weapons;
        var allSlots = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            allSlots[i] = targets[i].Npc.whoAmI;
        // Deeper beam levels re-propose from the previous segment's stand, so "here" and the
        // body-anchored lines mean where the body will be, not where it is. The live body is the
        // default, which is every level-one call.
        Vector2 body = origin ?? ctx.Npc.Center;
        int left = GeneratorCount;
        HereAndCompany(ctx, combat, weapons, enemies, targets, allSlots, body, proposals, seen, ref budget, ref left);
        BestRange(ctx, combat, weapons, enemies, targets, body, proposals, seen, ref budget, ref left);
        PierceLines(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        FloorFlanks(ctx, combat, weapons, enemies, targets, body, proposals, seen, ref budget, ref left);
        AboveArea(ctx, combat, weapons, enemies, targets, proposals, seen, ref budget, ref left);
        if (!disableBankAims)
            BankShots(ctx, combat, weapons, enemies, targets, body, proposals, seen, ref budget, ref left);
        SafeRange(ctx, weapons, targets, proposals, seen);
        RetagAboveDrops(proposals, weapons, targets, enemies);
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

    /// <summary>
    /// This generator's stopping simulation count: spent so far plus its share, saturating rather
    /// than overflowing — under an unbounded allowance the share is <see cref="int.MaxValue"/> and a
    /// plain addition wraps negative past the first spent sim, which reads as already spent and
    /// silently disables every generator after the first for the whole suite.
    /// </summary>
    private static int StopAt(ref PlanningBudget budget, ref int left)
    {
        int share = Share(ref budget, ref left);
        return share >= int.MaxValue - budget.Simulations ? int.MaxValue : budget.Simulations + share;
    }

    private static void Emit(List<StandProposal> proposals, HashSet<(int X, int Y)> seen,
        Vector2 stand, StandReason reason, int weaponSlot, int[] targetSlots)
    {
        if (seen.Add(HalfTile(stand)))
            proposals.Add(new StandProposal(stand, reason, weaponSlot, targetSlots));
    }

    /// <summary>
    /// BestRange's up-line samples the same air AboveArea drops from. Dedup keeps one rock; the reason
    /// still has to be the area drop when a stand sits above the group and a handed weapon has learned
    /// area, or a grenade-then-pierce plan reads as a ranging stand.
    /// </summary>
    private static void RetagAboveDrops(List<StandProposal> proposals, IReadOnlyList<CompanionWeapon> weapons,
        List<ThreatRecord> targets, IReadOnlyList<EnemyForecast> enemies)
    {
        bool anyArea = false;
        for (int w = 0; w < weapons.Count; w++)
            if (LearnHitResponses.ResponseFor(RepresentativeType(weapons[w])).Area.Radius > 0f)
            {
                anyArea = true;
                break;
            }
        if (!anyArea || targets.Count == 0)
            return;
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
        Vector2 centroid = sum / count;
        for (int i = 0; i < proposals.Count; i++)
            if ((proposals[i].Reason == StandReason.BestRange || proposals[i].Reason == StandReason.HereAndCompany)
                && proposals[i].Stand.Y < centroid.Y - 40f
                && MathF.Abs(proposals[i].Stand.X - centroid.X) < 120f)
                proposals[i] = proposals[i] with { Reason = StandReason.AboveArea };
    }

    private static void EmitAbove(List<StandProposal> proposals, HashSet<(int X, int Y)> seen,
        Vector2 stand, int weaponSlot, int[] targetSlots)
    {
        (int X, int Y) key = HalfTile(stand);
        for (int i = 0; i < proposals.Count; i++)
            if (HalfTile(proposals[i].Stand) == key)
            {
                proposals[i] = proposals[i] with
                {
                    Reason = StandReason.AboveArea,
                    WeaponSlot = weaponSlot,
                    TargetSlots = targetSlots,
                };
                return;
            }
        Emit(proposals, seen, stand, StandReason.AboveArea, weaponSlot, targetSlots);
    }

    /// <summary>
    /// BestRange samples the same body-line rocks PierceLines proves as a chain. Dedup keeps one
    /// rock; the reason still has to be the pierce when the probe struck two bodies, or a
    /// grenade-then-pierce plan reads as a ranging stand. Never steal an AboveArea drop — that
    /// generator's retag is the one that names the burst.
    /// </summary>
    private static void EmitPierce(List<StandProposal> proposals, HashSet<(int X, int Y)> seen,
        Vector2 stand, int weaponSlot, int[] targetSlots)
    {
        (int X, int Y) key = HalfTile(stand);
        for (int i = 0; i < proposals.Count; i++)
            if (HalfTile(proposals[i].Stand) == key)
            {
                if (proposals[i].Reason == StandReason.AboveArea)
                    return;
                proposals[i] = proposals[i] with
                {
                    Reason = StandReason.PierceLines,
                    WeaponSlot = weaponSlot,
                    TargetSlots = targetSlots,
                };
                return;
            }
        Emit(proposals, seen, stand, StandReason.PierceLines, weaponSlot, targetSlots);
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
    /// One probe: a use aimed straight at the target's predicted centre, simulated once, damage on the
    /// target summed and distinct bodies counted — plus one lobbed aim when the straight one misses and
    /// the law's gravity engages inside the flight, so an arcing weapon is not judged by a flat shot.
    /// Deliberately not the aim search: the intercept sweep flies dozens of paths per probe, which priced
    /// a hopeless crowd at fifty milliseconds a tick. The probe only asks whether a use reaches; the
    /// search aims every emitted stand truly afterwards. Cached, so a stand the search prices does not
    /// pay twice for the aim the generator already flew.
    /// </summary>
    private static float ProbeYield(CompanionWeapon weapon, int slot, in ActionContext ctx, Vector2 muzzle,
        EnemyForecast target, CombatWorld world, IReadOnlyList<EnemyForecast> enemies,
        ModifierState modifiers, ref PlanningBudget budget, out int bodies, out SimulatedUse? flown)
    {
        bodies = 0;
        flown = null;
        if (!budget.Check())
            return 0f;
        WeaponId id = SimulateUse.Identify(weapon, ctx, slot);
        Vector2 centre = target.PredictedCentre(1);
        Vector2 toTarget = centre - muzzle;
        float dist = toTarget.Length();
        toTarget = dist < 1f ? Vector2.UnitX : toTarget / dist;
        float best = FlyProbe(id, muzzle, centre, toTarget, target.Slot, world, enemies, modifiers,
            ref budget, out bodies, out flown);
        FlightLaw law = FitFlightLaws.LawFor(RepresentativeType(weapon));
        if (best <= 0f && law.Gravity is GravityTerm gravity
            && dist / MathF.Max(1f, weapon.Model.Speed) >= gravity.OnsetUpdate)
        {
            Vector2 lobAim = centre + new Vector2(0f, -0.35f * dist);
            Vector2 lobDir = lobAim - muzzle;
            float lobDist = lobDir.Length();
            lobDir = lobDist < 1f ? -Vector2.UnitY : lobDir / lobDist;
            float lobbed = FlyProbe(id, muzzle, lobAim, lobDir, target.Slot, world, enemies, modifiers,
                ref budget, out int lobBodies, out SimulatedUse? lobFlown);
            if (lobbed > best)
            {
                best = lobbed;
                bodies = lobBodies;
                flown = lobFlown;
            }
        }
        return best;
    }

    private static float FlyProbe(WeaponId id, Vector2 muzzle, Vector2 aimPoint, Vector2 direction,
        int targetSlot, CombatWorld world, IReadOnlyList<EnemyForecast> enemies, ModifierState modifiers,
        ref PlanningBudget budget, out int bodies, out SimulatedUse? flown)
    {
        bodies = 0;
        int knowledge = KnowledgeRevision.Current;
        // The cross-tick planned cache, not the per-tick one: the generators ask the same stands
        // sixty times a second against enemies that mostly stand still, and re-flying every probe
        // every tick spent whole frames deciding not to fight. Fresh sims dual-write to the
        // per-tick cache, so the overlay and the hands read this tick as before.
        if (!CachePlannedSims.TryGet(id, modifiers, muzzle, aimPoint, 0, knowledge, world.RefreshCount,
            enemies, out SimulatedUse? use) || use == null)
        {
            use = SimulateUse.Simulate(id, muzzle, aimPoint, direction, world, enemies, modifiers, 0,
                ref budget);
            CachePlannedSims.Store(id, modifiers, muzzle, aimPoint, 0, knowledge, world.RefreshCount,
                enemies, use);
            CacheSimulatedUses.Store(id, modifiers, muzzle, aimPoint, 0, knowledge,
                world.RefreshCount, use);
        }
        flown = use;
        float damage = 0f;
        var struck = new HashSet<int>();
        foreach (SimHit hit in use.Hits)
        {
            struck.Add(hit.Slot);
            if (hit.Slot == targetSlot)
                damage += hit.Damage;
        }
        bodies = struck.Count;
        return damage;
    }

    /// <summary>
    /// Where the body is — free, always, no probe — and points inside his predicted region from which
    /// a use reaches a target. The region samples are its centre and four cardinals at half extent;
    /// each keeps the targets some weapon's probe lands on from it.
    /// </summary>
    private static void HereAndCompany(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, int[] allSlots, Vector2 body, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        Emit(proposals, seen, body, StandReason.HereAndCompany, -1, allSlots);
        int stopAt = StopAt(ref budget, ref left);
        PlayerIntentRegion region = ctx.Senses.Intent.Region;
        var points = new Vector2[]
        {
            region.Centre,
            region.Centre + new Vector2(region.HalfSize.X / 2f, 0f),
            region.Centre - new Vector2(region.HalfSize.X / 2f, 0f),
            region.Centre + new Vector2(0f, region.HalfSize.Y / 2f),
            region.Centre - new Vector2(0f, region.HalfSize.Y / 2f),
        };
        ModifierState modifiers = ApplyCompanionModifiers.Current();
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
                foreach (ThreatRecord threat in targets)
                {
                    EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
                    if (forecast == null)
                        continue;
                    if (ProbeYield(weapons[w], w, ctx, muzzle, forecast, world, enemies, modifiers,
                        ref budget, out _, out _) > 0f)
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
    /// four distances flown along the line from the target toward the body, toward the player's
    /// side, and straight up, plus two bearings off the body line at two distances, the peak of
    /// each kept when a use reaches. The up line is the vertical the other lines never sample: a
    /// pillar between the body and a grounded target blocks every near-horizontal line, and the
    /// reposition the fight prices is above the target shooting down. A flat long weapon peaks
    /// far, a spread weapon close, against the same lone target — the sim prices the difference,
    /// so the generator names no category.
    /// </summary>
    private static void BestRange(in ActionContext ctx, CompanionCombat combat,
        IReadOnlyList<CompanionWeapon> weapons, IReadOnlyList<EnemyForecast> enemies,
        List<ThreatRecord> targets, Vector2 body, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        int stopAt = StopAt(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        foreach (ThreatRecord threat in targets)
        {
            EnemyForecast? forecast = ForecastFor(enemies, threat.Npc.whoAmI);
            if (forecast == null)
                continue;
            Vector2 centre = forecast.PredictedCentre(GeometryTick);
            Vector2 toBody = body - centre;
            Vector2 toPlayer = ctx.Player.Center - centre;
            if (toBody == Vector2.Zero)
                toBody = Vector2.UnitX;
            if (toPlayer == Vector2.Zero)
                toPlayer = -Vector2.UnitX;
            toBody = Vector2.Normalize(toBody);
            toPlayer = Vector2.Normalize(toPlayer);
            var mains = new Vector2[] { toBody, toPlayer, -Vector2.UnitY };
            var bearings = new Vector2[]
            {
                Vector2.Normalize(toBody.RotatedBy(0.7f)), Vector2.Normalize(toBody.RotatedBy(-0.7f)),
            };
            for (int w = 0; w < weapons.Count; w++)
            {
                // AboveArea owns the drop. A ranging peak beside the group for an area weapon is a
                // contact throw that the search then prices as a one-segment kill, and the grenade-
                // then-pierce plan never leaves the rock.
                if (LearnHitResponses.ResponseFor(RepresentativeType(weapons[w])).Area.Radius > 0f)
                    continue;
                float reach = weapons[w].IsSwing
                    ? SwingStandReach(weapons[w]) : MathF.Max(48f, weapons[w].Reach);
                foreach (Vector2 line in mains)
                    PeakAlongLine(ctx, weapons[w], w, forecast, threat.Npc.whoAmI, centre, line, reach, 4,
                        enemies, modifiers, proposals, seen, ref budget, stopAt);
                foreach (Vector2 line in bearings)
                    PeakAlongLine(ctx, weapons[w], w, forecast, threat.Npc.whoAmI, centre, line, reach, 2,
                        enemies, modifiers, proposals, seen, ref budget, stopAt);
                if (budget.Simulations >= stopAt || !budget.Check())
                    return;
            }
        }
    }

    private static void PeakAlongLine(in ActionContext ctx, CompanionWeapon weapon, int slot,
        EnemyForecast forecast, int targetSlot, Vector2 centre, Vector2 line, float reach, int samples,
        IReadOnlyList<EnemyForecast> enemies, ModifierState modifiers, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, int stopAt)
    {
        Vector2 peak = centre;
        float peakYield = 0f;
        // Reach is capped at 1100, so s/samples starts at 275px. A spread weapon's peak is inside
        // the 160px harm Inverse, and a goons-then-close plan needs a stand closer than 250px; a
        // grid that never asks there cannot name either. Close extras run after the even grid so a
        // flat long weapon keeps its first equal peak (range) and a shotgun with more pellets on
        // the box moves in — including when the even grid missed, which is the close peak.
        int n = samples + 2;
        for (int i = 0; i < n; i++)
        {
            float distance = i < samples ? reach * (i + 1) / samples : i == samples ? 96f : 48f;
            if (distance < 32f || distance > reach || budget.Simulations >= stopAt || !budget.Check())
                continue;
            Vector2 stand = centre + line * distance;
            Vector2 muzzle = CompanionCombat.MuzzleAt(stand);
            var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
            float yield = ProbeYield(weapon, slot, ctx, muzzle, forecast, world, enemies, modifiers,
                ref budget, out _, out _);
            if (yield > peakYield + 1e-6f)
            {
                peakYield = yield;
                peak = stand;
            }
        }
        if (peakYield > 0f)
            Emit(proposals, seen, peak, StandReason.BestRange, slot, new[] { targetSlot });
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
        int stopAt = StopAt(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        for (int w = 0; w < weapons.Count; w++)
        {
            if (!Pierces(weapons[w]))
                continue;
            float reach = MathF.Max(48f, weapons[w].Reach);
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
                            float damage = ProbeYield(weapons[w], w, ctx, muzzle, first, world, enemies,
                                modifiers, ref budget, out int bodies, out _);
                            if (bodies > bestBodies || (bodies == bestBodies && damage > bestDamage + 1e-6f))
                            {
                                bestBodies = bodies;
                                bestDamage = damage;
                                best = stand;
                            }
                        }
                        if (bestBodies >= 2)
                            EmitPierce(proposals, seen, best, w, chain);
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
        List<ThreatRecord> targets, Vector2 body, List<StandProposal> proposals,
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
        int stopAt = StopAt(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        Vector2 across = centroid - body;
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
                    float yield = ProbeYield(weapons[w], w, ctx, muzzle, aimAt, world, enemies, modifiers,
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
        int stopAt = StopAt(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        var group = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
            group[i] = targets[i].Npc.whoAmI;
        for (int w = 0; w < weapons.Count; w++)
        {
            AreaResponse area = LearnHitResponses.ResponseFor(RepresentativeType(weapons[w])).Area;
            if (area.Radius <= 0f)
                continue;
            EnemyForecast? aimAt = ForecastFor(enemies, targets[0].Npc.whoAmI);
            if (aimAt == null)
                continue;
            Vector2 best = centroid;
            float bestYield = 0f;
            bool bestCentred = false;
            foreach (float above in new[] { 64f, 128f, 192f })
            {
                if (budget.Simulations >= stopAt || !budget.Check())
                    return;
                Vector2 stand = centroid - new Vector2(0f, above);
                Vector2 muzzle = CompanionCombat.MuzzleAt(stand);
                var world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
                float yield = ProbeYield(weapons[w], w, ctx, muzzle, aimAt, world, enemies, modifiers,
                    ref budget, out _, out SimulatedUse? flown);
                bool centred = false;
                if (flown != null)
                    foreach (SimDeath death in flown.Deaths)
                        if (Vector2.Distance(death.Position, centroid) <= area.Radius)
                        {
                            centred = true;
                            break;
                        }
                if ((yield > 0f || centred) && (yield > bestYield + 1e-6f || (centred && !bestCentred && yield >= bestYield)))
                {
                    bestYield = yield;
                    best = stand;
                    bestCentred = centred;
                }
            }
            if (bestYield > 0f || bestCentred)
                EmitAbove(proposals, seen, best, w, group);
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
        List<ThreatRecord> targets, Vector2 origin, List<StandProposal> proposals,
        HashSet<(int X, int Y)> seen, ref PlanningBudget budget, ref int left)
    {
        int stopAt = StopAt(ref budget, ref left);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        Vector2 body = CompanionCombat.MuzzleAt(origin);
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
                if (SolveAims.FirstLanding(id, body, forecast, bodyWorld, enemies, 0, ref budget, planning: true) != null)
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
                        if (!CachePlannedSims.TryGet(id, modifiers, muzzle, bank.AimPoint, 1, knowledge,
                            world.RefreshCount, enemies, out SimulatedUse? cached) || cached == null)
                        {
                            use = SimulateUse.Simulate(id, muzzle, bank.AimPoint, bank.LaunchDirection,
                                world, enemies, modifiers, 1, ref budget);
                            CachePlannedSims.Store(id, modifiers, muzzle, bank.AimPoint, 1, knowledge,
                                world.RefreshCount, enemies, use);
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
