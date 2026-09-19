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
using Budget = live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.DecisionWorkBudget;
using PlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using PositionRequest = live::AICompanion.Companion.Brain.Infrastructure.Position.PositionRequest;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;

/// <summary>
/// The combat fixtures' shared hand: a plan searched under an unbounded budget with every stand and
/// target allowed, committed and fired once. The old fixtures drove the arsenal's choice directly; the
/// choice is the planner's now, so the rows that assert what leaves the muzzle search a real plan first.
/// Everything allowed because these rows are about the hands and the learner, not the allowance.
/// </summary>
internal static class CombatFixture
{
    public sealed record FiredUse(bool Fired, CombatWeapon? Weapon, AttackPlan? Plan);

    /// <summary>
    /// Gives a direct fixture entry point the ambient decision allowance that a live brain tick owns.
    /// Nested calls borrow an already-running tick unchanged; a fixture that starts outside a tick gets
    /// one deterministic, operation-unbounded allowance and tears down only the allowance it created.
    /// </summary>
    public static IDisposable BeginDecision(Budget? fixtureBudget = null) => new FixtureDecision(fixtureBudget);

    private sealed class FixtureDecision : IDisposable
    {
        private readonly bool ownsBudget;

        public FixtureDecision(Budget? fixtureBudget)
        {
            if (PlanningWork.IsActive)
                return;
            PlanningWork.Begin(fixtureBudget ?? new Budget(double.PositiveInfinity, long.MaxValue, () => 0, 1));
            ownsBudget = true;
        }

        public void Dispose()
        {
            if (ownsBudget)
                PlanningWork.End();
        }
    }

    /// <summary>
    /// One plan searched the way the activity searches, but unbounded and allow-all. Depth is the
    /// row's independent variable: level-one rows pin one segment, the beam rows pass two or three.
    /// </summary>
    public static AttackPlan? Search(CompanionNPC companion, C ctx, int maxDepth = 1)
    {
        using var decision = BeginDecision();
        var combat = companion.Combat;
        var weights = Weigh.ForSenses(ctx);
        Budget budget = PlanningWork.Current;
        // Prime the reach region to completion before searching: the search answers on the flood's
        // verdicts, and no fixture here ever resolves anything, so without priming every stand but
        // the body's reads undecided and no reposition is ever priced — a state live play leaves
        // after one tick. The actor matrix primes the same way for the same reason.
        var brain = companion.Brain;
        var primeHome = new PositionRequest(RequestKind.WithPlayer, ctx.Player.Bottom);
        brain.Positioner.Resolve(primeHome, brain.Senses);
        for (int i = 0; i < 3000 && !brain.Positioner.ReachComplete; i++)
            brain.Positioner.Resolve(primeHome, brain.Senses);
        SearchPlans.SearchResult result = SearchPlans.Search(ctx, combat, companion.Brain.Positioner,
            _ => true, weights, combat.NextPlanId++, ref budget, maxDepth: maxDepth);
        return result.Plan;
    }

    /// <summary>Search, commit and fire once, returning what the hand did and with which weapon.</summary>
    public static FiredUse FireOnce(CompanionNPC companion, C ctx)
    {
        using var decision = BeginDecision();
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
