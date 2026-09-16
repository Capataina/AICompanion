extern alias live;

using Microsoft.Xna.Framework;
using Terraria;
using C = live::AICompanion.Companion.Brain.Activities.ActionContext;
using CompanionNPC = live::AICompanion.Companion.CharacterBody.CompanionNPC;
using Combat = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionCombat;
using CombatWeapon = live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.CompanionWeapon;
using AttackPlan = live::AICompanion.Companion.Brain.Activities.Combat.Planning.AttackPlan;
using SearchPlans = live::AICompanion.Companion.Brain.Activities.Combat.Planning.SearchAttackPlans;
using Weigh = live::AICompanion.Companion.Brain.Activities.Combat.Planning.WeighCombatObjectives;
using Budget = live::AICompanion.Companion.Brain.Activities.Combat.Planning.PlanningBudget;

/// <summary>
/// The combat fixtures' shared hand: a plan searched under an unbounded budget with every stand and
/// target allowed, committed and fired once. The old fixtures drove the arsenal's choice directly; the
/// choice is the planner's now, so the rows that assert what leaves the muzzle search a real plan first.
/// Everything allowed because these rows are about the hands and the learner, not the allowance.
/// </summary>
internal static class CombatFixture
{
    public sealed record FiredUse(bool Fired, CombatWeapon? Weapon, AttackPlan? Plan);

    /// <summary>One plan searched the way the activity searches, but unbounded and allow-all.</summary>
    public static AttackPlan? Search(CompanionNPC companion, C ctx)
    {
        var combat = companion.Combat;
        var weights = Weigh.ForSenses(ctx);
        Budget budget = Budget.Unbounded();
        SearchPlans.SearchResult result = SearchPlans.SearchDepthOne(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget);
        return result.Plan;
    }

    /// <summary>Search, commit and fire once, returning what the hand did and with which weapon.</summary>
    public static FiredUse FireOnce(CompanionNPC companion, C ctx)
    {
        var combat = companion.Combat;
        AttackPlan? plan = Search(companion, ctx);
        if (plan == null)
        {
            combat.Hands.Fire(ctx, combat, null);
            return new FiredUse(false, null, null);
        }
        combat.Planner.Commit(plan);
        bool fired = combat.Hands.Fire(ctx, combat, plan);
        CombatWeapon? weapon = null;
        if (fired)
        {
            var weapons = combat.Weapons;
            foreach (var segment in plan.Segments)
                foreach (var use in segment.Uses)
                    if ((uint)use.WeaponSlot < (uint)weapons.Count)
                        weapon ??= weapons[use.WeaponSlot];
        }
        return new FiredUse(fired, weapon, plan);
    }

    /// <summary>The plan's opening weapon: what the old choice rows asserted, now read off the plan.</summary>
    public static CombatWeapon? OpeningWeapon(CompanionNPC companion, AttackPlan? plan)
    {
        if (plan == null || plan.Segments.Length == 0 || plan.Segments[0].Uses.Length == 0)
            return null;
        var weapons = companion.Combat.Weapons;
        int slot = plan.Segments[0].Uses[0].WeaponSlot;
        return (uint)slot < (uint)weapons.Count ? weapons[slot] : null;
    }
}
