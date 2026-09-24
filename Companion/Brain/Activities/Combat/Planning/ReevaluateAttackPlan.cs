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
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

public static class ReevaluateAttackPlan
{
/// <summary>The profiler section a committed plan's re-pricing runs in.</summary>
private static readonly int RepriceSection = Infrastructure.Diagnostics.BrainSections.Register("reprice");
/// <summary>Which gate emptied the attack list on the last refusal, for a reader who has a released
/// plan and no way to tell which of eight checks dropped its final use. A release reason of
/// "uses-stopped-solving" names the symptom; this names the check. It is only ever read after a
/// <see cref="Repricing.Refused"/>, is overwritten by the next call, and nothing decides on it.</summary>
public static string LastRefusal { get; private set; } = "never-repriced";
/// <summary>
/// What a re-pricing can say, in three answers rather than two. A priced outcome is the plan's worth
/// now. A priced absence — no remaining use solves — invalidates the plan. A cut is neither: the
/// shared allowance ran out before the uses could be re-flown, which says nothing whatever about
/// whether they still solve, and must not release a commitment that nothing has invalidated.
///
/// The two were one answer until this was written, and the cost was live: <c>Cut</c> on a
/// <c>DecisionWorkBudget</c> is sticky, so once anything on a tick had cut the shared allowance every
/// subsequent re-price returned null and combat released its committed plan with the reason
/// "uses-stopped-solving" — a reason that was also false in the record. That is the wait-for-certainty
/// failure the canonical plan's section 2 forbids by name: between execution boundaries a valid
/// current action is retained unless required repair removes its admission or another executable
/// continuation proves a higher future value, and an exhausted allowance is neither.
/// </summary>
public readonly record struct Repricing(CombatOutcome? Outcome, bool Cut)
{
    /// <summary>Priced, and the plan is worth this.</summary>
    public bool Priced => Outcome != null;
    /// <summary>Not priced because the allowance ran out. Keep the plan; do not read it as a refusal.</summary>
    public bool Unresolved => Outcome == null && Cut;
    /// <summary>Priced and refused: no remaining use solves, so the plan is genuinely invalid.</summary>
    public bool Refused => Outcome == null && !Cut;
}

/// <summary>
/// The committed plan's outcome against this tick's forecast: its remaining uses re-flown from the stand
/// at their planned aims, so a wall the search never saw prices the plan honestly. See
/// <see cref="Repricing"/> for why a cut is reported apart from a refusal.
/// The audit replays the hold through the same method: a snapshot's committed plan, shifted into the
/// audit's tick space, re-priced against the restored forecast, so the exhaustive front is graded
/// against what the plan is worth now rather than what the search paid for it then.
/// </summary>
public static Repricing Reevaluate(in ActionContext ctx, CompanionCombat combat,
    IReadOnlyList<EnemyForecast> enemies, AttackPlan plan, CombatWeights weights, ref DecisionWorkBudget budget)
{
    using var section = Infrastructure.Diagnostics.BrainSections.Enter(RepriceSection);
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
    // The gate that refused the use examined most recently, published as LastRefusal only if the
    // whole segment ends up refusing. A segment usually carries one or two uses, so "the last one
    // refused" and "why this plan died" are the same sentence; on a longer segment it is the last
    // word rather than the whole story, which is why nothing decides on it.
    string gate = "the segment carried no uses at all";
    for (int i = segment.Uses.Length - 1; i >= 0; i--)
    {
        PlannedUse use = segment.Uses[i];
        bool overdue = use.FireTick <= tick;
        if (overdue && overdueCovered)
        {
            gate = "an earlier overdue use already covered this one";
            continue;
        }
        if (attacks.Count >= 8)
            break;
        if ((uint)use.WeaponSlot >= (uint)weapons.Count || use.TargetSlot < 0 || use.TargetSlot >= Main.maxNPCs)
        {
            gate = "the planned weapon or target slot is out of range";
            continue;
        }
        NPC target = Main.npc[use.TargetSlot];
        if (target == null || !target.active || target.life <= 0 || !target.CanBeChasedBy())
        {
            gate = "the planned target is dead, gone or no longer chaseable";
            continue;
        }
        CompanionWeapon weapon = weapons[use.WeaponSlot];
        if (!weapon.InReach(muzzle, target))
        {
            gate = "the planned target is outside the planned weapon's reach from the stand";
            continue;
        }
        int fireTick = Math.Max(0, use.FireTick - tick);
        // **The aim is solved for this use's own fire time, not carried from the one it was planned with.**
        //
        // The search solves one aim per weapon-and-target pair, at the tick the segment is entered, and
        // hands it to every use in the sequence — so a segment of three shots a weapon cooldown apart
        // carries one aim point for all three. Proven in `d6b2119`: `USEAIM i=0/1/2` with scheduled fire
        // ticks 17, 77 and 137 all reading `aim=664.0,940.0`. Against a still target that is harmless;
        // against anything moving, the later aims name where it was when the segment began.
        //
        // Re-flying those stale aims here is what made the re-price condemn the plan. The shot is
        // simulated at the right *time* with the wrong *point*, intercepts nothing, the attack list
        // empties, and a `Refused` releases a committed fight — measured as a hold surviving 0 of 3
        // ticks against anything moving faster than about 0.4 px/tick, a quarter of a zombie's walk.
        //
        // The hand never had this problem: `FireDueUse` calls the same `BestAimUse` at the moment of
        // firing and fires *that* point rather than the planned one. So the re-price was condemning
        // plans the hand would have re-aimed and landed, and this makes the two agree rather than
        // introducing anything new. The cost is one aim solve per remaining use of one committed plan
        // per tick, which is bounded by the eight-use cap above and is nothing beside the search that
        // produced the plan.
        // Charged to the shared allowance on purpose, and measured rather than assumed: on a planning
        // cache hit this costs nothing at all, because the search has already solved the best aim for
        // this weapon, target, muzzle and fire tick and BestAimUse returns it. On a miss it is one aim
        // sweep, measured at 21 to 29 operations per use on the intervening-hostile scene of
        // 21 September 2026, against a whole tick's allowance — small enough that no re-price on that
        // scene ever cut or was cut, over 480 consecutive ticks of a held fight.
        ForecastUses.AimedUse? aimed = ForecastUses.BestAimUse(ctx, weapon, use.WeaponSlot, target, muzzle,
            enemies, world, fireTick, record: false, planning: true, ref budget);
        if (budget.Cut)
            return new Repricing(null, Cut: true);
        if (aimed == null)
        {
            gate = "no aim solves for this use at its own fire time";
            continue;
        }
        EvaluateAttackOutcomes.Attack? attack = ForecastUses.AttackFromUse(ctx, weapon, use.WeaponSlot, target,
            muzzle, aimed.Value.Use, aimed.Value.Aim, aimed.Value.Intercept, fireTick, out _, out _);
        if (attack == null)
        {
            // Env-gated because the answer needed here is not "the attack was empty" but *why*: a shot
            // that intercepts nothing at all and a shot that intercepts the wrong body are different
            // defects, and the refusal string cannot tell them apart. AIC-422 is the open case.
            if (System.Environment.GetEnvironmentVariable("AIC_TRACE_REPRICE") != null)
                System.Console.WriteLine($"REPRICE tick={tick} fireTick={fireTick} muzzle={muzzle.X:0.0},{muzzle.Y:0.0} "
                    + $"plannedAim={use.AimPoint.X:0.0},{use.AimPoint.Y:0.0} "
                    + $"solvedAim={aimed.Value.Aim.AimPoint.X:0.0},{aimed.Value.Aim.AimPoint.Y:0.0} target={target.whoAmI} "
                    + $"simHits={aimed.Value.Use.Hits.Count} hitSlots={string.Join("/", System.Linq.Enumerable.Select(aimed.Value.Use.Hits, h => h.Slot))}");
            gate = "the re-flown use produced no attack at all";
            continue;
        }
        // A re-flown use validates the plan only through its planned target: a trajectory that still hits,
        // but only bodies the plan never targeted, is not the planned fight solving — it is a bystander in
        // the way. Without this a plan whose target left the senses holds on a wrong-body hit, running to
        // its horizon at an unlisted body instead of re-ranking onto the threats the senses actually list.
        // The slot match is sound here because validity already pinned every planned slot's generation.
        bool touchesTarget = false;
        foreach (EvaluateAttackOutcomes.Hit hit in attack.Hits)
            if (hit.Target == use.TargetSlot) { touchesTarget = true; break; }
        if (!touchesTarget)
        {
            gate = "the re-flown use still flies, but hits no body the plan targeted";
            continue;
        }
        attacks.Add(attack);
        if (overdue)
            overdueCovered = true;
    }
    if (attacks.Count == 0)
    {
        LastRefusal = gate;
        return new Repricing(null, Cut: false);
    }
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
    return new Repricing(EvaluateAttackOutcomes.EvaluateVector(attacks[0], attacks, evalTargets,
        Math.Max(0, combat.CooldownTicks), horizon, context, weights).Outcome, Cut: false);
}
}
