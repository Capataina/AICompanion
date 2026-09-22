extern alias live;

using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using DecisionWorkBudget = live::AICompanion.Companion.Brain.Infrastructure.Selection.Computation.DecisionWorkBudget;
using FightEnemies = live::AICompanion.Companion.Brain.Activities.Combat.FightEnemies;
using ActionContext = live::AICompanion.Companion.Brain.Activities.ActionContext;
using SearchPlans = live::AICompanion.Companion.Brain.Activities.Combat.Planning.SearchAttackPlans;
using WeighCombat = live::AICompanion.Companion.Brain.Activities.Combat.Planning.WeighCombatObjectives;
using ThreatRecord = live::AICompanion.Companion.Brain.Infrastructure.Observation.ThreatRecord;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
using OfferEligibility = live::AICompanion.Companion.Brain.Activities.OfferEligibility;
using RequestKind = live::AICompanion.Companion.Brain.Infrastructure.Position.RequestKind;
using StandProposal = live::AICompanion.Companion.Brain.Activities.Combat.Planning.StandProposal;
using StandReason = live::AICompanion.Companion.Brain.Activities.Combat.Planning.StandReason;
using StandVerdict = live::AICompanion.Companion.Brain.Infrastructure.Position.StandVerdict;
using CachePlanned = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CachePlannedSims;
using CacheSimulated = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation.CacheSimulatedUses;

/// <summary>
/// G11's smallest deterministic contract: tactical work borrows the one decision allowance and a cut
/// survives a second consumer. This deliberately exercises no Terraria state, so an exhausted budget
/// cannot be hidden behind a warmed simulator cache or a wall-clock race.
/// </summary>
internal static class VerifyRetainedCombatBudget
{
    public static int SharedAllowanceCutsEveryConsumer()
    {
        long tick = 0;
        var decision = new DecisionWorkBudget(10_000, operationAllowance: 2, () => tick, frequency: 1_000);
        Require(decision.TrySpend("combat-tactical-search"), "the first tactical expansion must borrow the tick allowance");
        Require(decision.TrySpend("course-repair"), "a sibling consumer must see the same remaining operation");
        Require(!decision.TrySpend("combat-tactical-search"), "combat must stop once a sibling spent the final operation");
        Require(decision.Cut && decision.FirstCutSubsystem == "combat-tactical-search",
            "the cut must be attributed to the consumer that found it");
        Require(decision.OperationsUsed == 2 && decision.RemainingOperations == 0,
            "the shared allowance must not mint a hidden combat operation");

        Console.WriteLine("retained combat budget: one borrowed allowance cuts tactical work after a sibling spend");
        return 0;
    }

    /// <summary>
    /// G04's live seam: an already-useful bow shot is priced before broad stand generation. The first
    /// run prices only that native stand through the replay seam, so it measures the opener's deterministic
    /// operation cost rather than a completed broad search. The second admits one extra broad operation,
    /// which must cut the broad search while combat still commits the opener and hands use that same budget.
    /// </summary>
    public static int AUsefulOpenerSurvivesABroaderSearchCut()
    {
        var measurementScene = CreateUsefulScene();
        DecisionWorkBudget measurement = new(double.PositiveInfinity, long.MaxValue, () => 0, 1);
        SearchPlans.SearchResult measured;
        var opener = new StandProposal(measurementScene.Companion.NPC.Center, StandReason.HereAndCompany, -1, new[] { 30 });
        var openerVerdict = new StandVerdict(opener.Stand, ReachVerdict.Reachable, 0f, 0f, 0f, true, "fixture-current-stand");
        using (CombatFixture.BeginDecision(measurement))
        {
            measured = SearchPlans.SearchDepthOne(measurementScene.Context, measurementScene.Companion.Combat, measurementScene.Companion.Brain.Positioner,
                _ => true, WeighCombat.ForSenses(measurementScene.Context), measurementScene.Companion.Combat.NextPlanId++, ref measurement,
                new SearchPlans.SearchOptions(new[] { opener }, new[] { openerVerdict }));
        }
        Require(measured.Plan != null && measurement.OperationsUsed > 0,
            "premise: the body stand must price a mechanically useful opening shot");

        // The measurement uses a real planning solve, which dual-writes the static planned and per-tick
        // simulation caches. The cut run must earn its own opener rather than accept that unlimited solve.
        CachePlanned.Clear();
        CacheSimulated.Clear();
        var scene = CreateUsefulScene();
        var fight = scene.Companion.Brain.Actions.OfType<FightEnemies>().Single();
        scene.Companion.Brain.Activity.Select(fight, scene.Context);
        DecisionWorkBudget cut = new(double.PositiveInfinity, measurement.OperationsUsed + 1, () => 0, 1);
        bool fired;
        using (CombatFixture.BeginDecision(cut))
        {
            fight.Prepare(scene.Context);
            Require(cut.Cut, "the extra broad-search operation must exhaust the same borrowed allowance");
            Require(fight.OfferedCut && fight.OfferedPlan != null && scene.Companion.Combat.Planner.Committed != null,
                $"a cut after a useful opener must still offer and commit combat; cut={fight.OfferedCut} plan={fight.OfferedPlan != null}");
            Require(fight.PreparedPositionRequest?.Kind == RequestKind.FireFrom,
                "the accepted opener must keep its FireFrom movement request instead of suppressing the body choice");
            fired = scene.Companion.Combat.Hands.Fire(scene.Context, scene.Companion.Combat, scene.Companion.Combat.Planner.Committed);
        }

        Require(fired, "the accepted opener must fire at the first mechanically available hand boundary");

        scene.Player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear.Slots[0] = new Item();
        DecisionWorkBudget noWeaponBudget = new(double.PositiveInfinity, long.MaxValue, () => 0, 1);
        using (CombatFixture.BeginDecision(noWeaponBudget))
            fight.Prepare(scene.Context);
        Require(fight.Eligibility == OfferEligibility.NoOpportunity && fight.EligibilityReason == "no-weapon",
            $"an empty hand must explain its refusal as no-weapon; got {fight.Eligibility}/{fight.EligibilityReason}");

        scene.Player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        DecisionWorkBudget blockedBudget = new(double.PositiveInfinity, long.MaxValue, () => 0, 1);
        using (CombatFixture.BeginDecision(blockedBudget))
        {
            SearchPlans.SearchResult blocked = SearchPlans.Search(scene.Context, scene.Companion.Combat,
                scene.Companion.Brain.Positioner, _ => false, WeighCombat.ForSenses(scene.Context),
                scene.Companion.Combat.NextPlanId++, ref blockedBudget);
            Require(blocked.Plan == null && blocked.Eligibility == OfferEligibility.NoOpportunity
                && blocked.Reason == "no-eligible-target",
                $"a blocked target must explain refusal rather than minting an opener; got {blocked.Eligibility}/{blocked.Reason}");
        }

        Console.WriteLine($"G04 retained opener: opener operations={measurement.OperationsUsed}, cut after={cut.OperationsUsed}, committed FireFrom and fired={fired}");
        return 0;
    }

    /// <summary>The use payload comes from the same simulator hit trace that made the opener usable.
    /// A nonzero attacking plan must therefore export a nonzero target allocation before course capture;
    /// combat cannot become a known usable, zero-effect opportunity.</summary>
    public static int PlannedUseCarriesSimulatorTargetDamage()
    {
        var scene = CreateUsefulScene();
        var opener = new StandProposal(scene.Companion.NPC.Center, StandReason.HereAndCompany, -1, new[] { 30 });
        var verdict = new StandVerdict(opener.Stand, ReachVerdict.Reachable, 0f, 0f, 0f, true, "fixture-current-stand");
        var budget = new DecisionWorkBudget(double.PositiveInfinity, long.MaxValue, () => 0, 1);
        SearchPlans.SearchResult result;
        using (CombatFixture.BeginDecision(budget))
            result = SearchPlans.SearchDepthOne(scene.Context, scene.Companion.Combat, scene.Companion.Brain.Positioner,
                _ => true, WeighCombat.ForSenses(scene.Context), scene.Companion.Combat.NextPlanId++, ref budget,
                new SearchPlans.SearchOptions(new[] { opener }, new[] { verdict }));
        Require(result.Plan != null && result.Plan.Segments.SelectMany(segment => segment.Uses)
                .Any(use => use.TargetSlot == 30 && float.IsFinite(use.ExpectedTargetDamage) && use.ExpectedTargetDamage > 0f),
            "A simulator-confirmed bow opener lost its target hit amount before course capture.");
        return 0;
    }

    private static (live::AICompanion.Companion.CharacterBody.CompanionNPC Companion, Player Player, ActionContext Context) CreateUsefulScene()
    {
        var companion = VerifyCompanionLifecycle.Create();
        Player player = Main.LocalPlayer;
        player.dead = false;
        player.active = true;
        player.statLife = player.statLifeMax2 = 100;
        player.statManaMax2 = 20;
        player.Bottom = companion.NPC.Bottom;
        player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear.Slots[0].SetDefaults(ItemID.WoodenBow);
        player.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>().Gear.Slots[1] = new Item();

        NPC zombie = Main.npc[30];
        zombie.SetDefaults(NPCID.Zombie);
        zombie.whoAmI = 30;
        zombie.active = true;
        zombie.velocity = Vector2.Zero;
        zombie.Bottom = companion.NPC.Bottom + new Vector2(160f, 0f);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Update(companion.NPC, player);
        companion.Brain.Senses.Threats.Threats.Clear();
        companion.Brain.Senses.Threats.Threats.Add(new ThreatRecord
        {
            Npc = zombie,
            ChainHead = zombie.whoAmI,
            IsChainRepresentative = true,
            CanReachPlayer = true,
            CanReachCompanion = true,
            DistanceToPlayer = Vector2.Distance(zombie.Center, player.Center),
            DistanceToCompanion = Vector2.Distance(zombie.Center, companion.NPC.Center),
            Urgency = .5f,
            UrgencyToCompanion = .5f,
        });
        return (companion, player, new ActionContext(companion, companion.Brain.Senses));
    }

    private static void Require(bool condition, string claim)
    {
        if (!condition) throw new InvalidOperationException("Retained combat budget regression: " + claim);
    }
}
