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
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

public static class ReevaluateAttackPlan
{
/// <summary>
/// The committed plan's outcome against this tick's forecast: its remaining uses re-flown from the stand
/// at their planned aims, so a wall the search never saw prices the plan honestly. Null when no remaining
/// use solves any more, which invalidates the plan rather than offering a fight that cannot happen.
/// The audit replays the hold through the same method: a snapshot's committed plan, shifted into the
/// audit's tick space, re-priced against the restored forecast, so the exhaustive front is graded
/// against what the plan is worth now rather than what the search paid for it then.
/// </summary>
public static CombatOutcome? Reevaluate(in ActionContext ctx, CompanionCombat combat,
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
}
