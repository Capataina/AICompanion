#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Recording;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;

/// <summary>
/// The hands on a tick combat runs: the committed plan's due use when the weapon is ready and the body
/// is within the stand's arrival tolerance, re-simulated from the actual muzzle so the aim is the live
/// one; while the body is travelling, the best use from where the body is, chosen by the same evaluator
/// with the plan's targets. The plan names weapon and target; this names the aim and pulls the trigger.
/// It reads the committed plan it is handed as a parameter and holds no choice of its own.
/// </summary>
public sealed class FireDueUse
{
    // A failed trace is a fact about a particular muzzle, target pose and terrain revision, not a
    // weapon cooldown. Keeping those states separate means a door opening or either body moving can
    // create a shot immediately, while a sealed cave does not spend two full solves every tick.
    private int failedTarget = -1;
    private int failedGeneration;
    private Vector2 failedMuzzle;
    private Vector2 failedTargetCenter;
    private Vector2 failedTargetVelocity;
    private int failedTerrainRevision;
    private int failedUntilTick;
    private const int FailedTraceFreshnessTicks = 6;

    /// <summary>Fire the plan's due use, or the best from here while travelling. Returns true on a use.</summary>
    public bool Fire(in ActionContext ctx, CompanionCombat combat, AttackPlan? plan)
    {
        var weapons = combat.Weapons;
        if (weapons.Count == 0)
        {
            combat.NoteNoWeapon();
            return false;
        }
        if (plan == null)
        {
            combat.NoteNoUseWorthFiring();
            return false;
        }
        int tick = ctx.Senses.Tick;
        AttackSegment segment = plan.Current(tick);
        NPC? primary = PrimaryTarget(plan);
        if (primary != null)
            ctx.Companion.Motor.Face(primary.Center.X);
        if (combat.CooldownTicks > 0)
        {
            combat.NoteCooldown();
            HoldPlanned(ctx, weapons, segment, tick);
            return false;
        }
        Vector2 muzzle = CompanionCombat.Muzzle(ctx.Npc);
        bool arrived = Vector2.DistanceSquared(muzzle, segment.Stand.Stand)
            <= Weights.CombatStandArrivalPx * Weights.CombatStandArrivalPx;
        PlannedUse? due = DueUse(segment, tick);
        if (arrived && due != null)
            return FirePlanned(ctx, combat, plan, segment, due.Value);
        return FireBestFromHere(ctx, combat, plan, segment);
    }

    private static NPC? PrimaryTarget(AttackPlan plan)
    {
        int slot = plan.PrimaryTarget;
        if (slot < 0 || slot >= Main.maxNPCs)
            return null;
        NPC npc = Main.npc[slot];
        return npc != null && npc.active && npc.life > 0 ? npc : null;
    }

    /// <summary>The latest use whose fire tick has come: the one the hand owes the plan.</summary>
    private static PlannedUse? DueUse(AttackSegment segment, int tick)
    {
        PlannedUse? due = null;
        foreach (PlannedUse use in segment.Uses)
            if (use.FireTick <= tick)
                due = use;
        return due;
    }

    private static void HoldPlanned(in ActionContext ctx, IReadOnlyList<CompanionWeapon> weapons, AttackSegment segment, int tick)
    {
        foreach (PlannedUse use in segment.Uses)
        {
            if (use.FireTick < tick)
                continue;
            if ((uint)use.WeaponSlot < (uint)weapons.Count)
                ctx.Companion.HoldItem(weapons[use.WeaponSlot].ItemType);
            return;
        }
    }

    /// <summary>
    /// The plan's due use, re-solved from the live muzzle: the same weapon at the same target, so the aim
    /// is the live one rather than the search's. The other weapon is tried before giving up, because standing
    /// still holding an unusable weapon is how the companion died with thirteen hostiles on it and a bow in
    /// its hand.
    /// </summary>
    private bool FirePlanned(in ActionContext ctx, CompanionCombat combat, AttackPlan plan, AttackSegment segment, PlannedUse due)
    {
        var weapons = combat.Weapons;
        if ((uint)due.WeaponSlot >= (uint)weapons.Count || due.TargetSlot < 0 || due.TargetSlot >= Main.maxNPCs)
        {
            combat.NoteNoUseWorthFiring();
            return false;
        }
        NPC target = Main.npc[due.TargetSlot];
        if (target == null || !target.active || target.life <= 0 || !target.CanBeChasedBy())
        {
            combat.NoteNoUseWorthFiring();
            return false;
        }
        CompanionWeapon weapon = weapons[due.WeaponSlot];
        int weaponSlot = due.WeaponSlot;
        Vector2 muzzle = CompanionCombat.Muzzle(ctx.Npc);
        if (FailedTraceStillApplies(ctx, target, muzzle))
        {
            ctx.Companion.HoldItem(weapon.ItemType);
            combat.NoteNoUseWorthFiring();
            return false;
        }
        IReadOnlyList<EnemyForecast> enemies = combat.EnsureForecast(ctx);
        CombatWorld world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
        PlanningBudget aimBudget = PlanningBudget.Unbounded();
        ForecastUses.AimedUse? aimed = ForecastUses.BestAimUse(ctx, weapon, weaponSlot, target, muzzle, enemies, world, 0, record: true,
            planning: false, ref aimBudget);
        if (aimed == null)
        {
            for (int w = 0; w < weapons.Count; w++)
            {
                if (w == weaponSlot) continue;
                aimed = ForecastUses.BestAimUse(ctx, weapons[w], w, target, muzzle, enemies, world, 0, record: true,
                    planning: false, ref aimBudget);
                if (aimed != null)
                {
                    weapon = weapons[w];
                    weaponSlot = w;
                    break;
                }
            }
        }
        ctx.Companion.HoldItem(weapon.ItemType);
        combat.LastShotSolved = aimed != null;
        if (aimed == null)
        {
            combat.NoteNoUseWorthFiring();
            RememberFailedTrace(ctx, target, muzzle);
            return false;
        }
        int useIndex = UseIndex(segment, due);
        return Release(ctx, combat, plan, segment, useIndex, weapon, weaponSlot, target, muzzle, aimed.Value);
    }

    /// <summary>
    /// The best use from where the body is while it travels: one best-aim attack per weapon at each of the
    /// plan's targets, the opener the same evaluator values highest. The plan's targets bound the question —
    /// travelling hands do not open new fronts.
    /// </summary>
    private bool FireBestFromHere(in ActionContext ctx, CompanionCombat combat, AttackPlan plan, AttackSegment segment)
    {
        var weapons = combat.Weapons;
        IReadOnlyList<EnemyForecast> enemies = combat.EnsureForecast(ctx);
        Vector2 muzzle = CompanionCombat.Muzzle(ctx.Npc);
        var attacks = new List<EvaluateAttackOutcomes.Attack>();
        var aimedByTarget = new Dictionary<int, (CompanionWeapon Weapon, int Slot, ForecastUses.AimedUse Aimed)>();
        var seen = new HashSet<int>();
        foreach (PlannedUse use in segment.Uses)
        {
            if (!seen.Add(use.TargetSlot))
                continue;
            if (use.TargetSlot < 0 || use.TargetSlot >= Main.maxNPCs)
                continue;
            NPC target = Main.npc[use.TargetSlot];
            if (target == null || !target.active || target.life <= 0 || !target.CanBeChasedBy())
                continue;
            PlanningBudget aimBudget = PlanningBudget.Unbounded();
            for (int w = 0; w < weapons.Count; w++)
            {
                ForecastUses.AimedUse? aimed = ForecastUses.BestAimUse(ctx, weapons[w], w, target, muzzle, enemies,
                    CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision), 0, record: false,
                    planning: false, ref aimBudget);
                if (aimed == null) continue;
                EvaluateAttackOutcomes.Attack? attack = ForecastUses.AttackFromUse(ctx, weapons[w], w, target, muzzle,
                    aimed.Value.Use, aimed.Value.Aim, aimed.Value.Intercept, 0, out _, out _);
                if (attack == null) continue;
                attacks.Add(attack);
                if (!aimedByTarget.ContainsKey(use.TargetSlot))
                    aimedByTarget[use.TargetSlot] = (weapons[w], w, aimed.Value);
            }
        }
        if (attacks.Count == 0)
        {
            combat.NoteNoUseWorthFiring();
            HoldPlanned(ctx, weapons, segment, ctx.Senses.Tick);
            return false;
        }
        var evalTargets = ForecastUses.AttackTargets(ctx);
        var context = TravelContext(ctx, muzzle);
        CombatWeights weights = FightWeights(ctx, combat);
        EvaluateAttackOutcomes.Attack? winner = null;
        float best = 0f;
        foreach (EvaluateAttackOutcomes.Attack attack in attacks)
        {
            CombatOutcome outcome = EvaluateAttackOutcomes.EvaluateVector(attack, attacks, evalTargets,
                combat.CooldownTicks, CompanionCombat.HorizonTicks, context, weights);
            float value = weights.Weighted(outcome);
            if (value > best)
            {
                best = value;
                winner = attack;
            }
        }
        if (winner == null || !aimedByTarget.TryGetValue(winner.Target, out var solved))
        {
            combat.NoteNoUseWorthFiring();
            HoldPlanned(ctx, weapons, segment, ctx.Senses.Tick);
            return false;
        }
        NPC winnerTarget = Main.npc[winner.Target];
        ctx.Companion.HoldItem(solved.Weapon.ItemType);
        combat.LastShotSolved = true;
        return Release(ctx, combat, plan, segment, -1, solved.Weapon, solved.Slot, winnerTarget, muzzle, solved.Aimed);
    }

    /// <summary>The travel context prices the use from here: no travel, the live exposure, no company gap beyond now.</summary>
    private static EvaluateAttackOutcomes.PlanContext TravelContext(in ActionContext ctx, Vector2 muzzle)
    {
        float harm = Infrastructure.Position.Positioner.PredictedHarmAt(muzzle, ctx.Senses, ctx.Npc.life);
        return new EvaluateAttackOutcomes.PlanContext(0, harm, harm,
            Math.Max(1, ctx.Player.statLife), Math.Max(1, ctx.Npc.life), Math.Max(1, ctx.Companion.Mana.Max), 0f);
    }

    private static CombatWeights FightWeights(in ActionContext ctx, CompanionCombat combat)
        => WeighCombatObjectives.ForSenses(ctx);

    private static int UseIndex(AttackSegment segment, PlannedUse due)
    {
        for (int i = 0; i < segment.Uses.Length; i++)
            if (segment.Uses[i].Equals(due))
                return i;
        return 0;
    }

    /// <summary>
    /// The trigger both paths share: noise on a shot's launch, never on a swing's sector; the forecast the
    /// use is taught against, taken here because the plan's may be ticks old; the shot record carrying the
    /// plan's identity so prediction pairs with flight; and the outcome window the learner closes.
    /// </summary>
    private bool Release(in ActionContext ctx, CompanionCombat combat, AttackPlan plan, AttackSegment segment, int useIndex,
        CompanionWeapon weapon, int weaponSlot, NPC target, Vector2 muzzle, ForecastUses.AimedUse aimed)
    {
        var forecast = ForecastUses.AttackFromUse(ctx, weapon, weaponSlot, target, muzzle,
            aimed.Use, aimed.Aim, aimed.Intercept, 0, out _, out ForecastUses.ForecastPrior prior);

        // A swing has no launch to be imprecise about: the noise is a shot's, and rotating a sector's
        // centre only moved which bodies at the sector's edge were struck. No re-trace follows, because
        // the sim that chose this aim already flew it through terrain.
        float noise = weapon.IsSwing ? 0f : (Main.rand.NextFloat() * 2f - 1f) * weapon.AimNoise;
        Vector2 launch = aimed.Aim.LaunchDirection.RotatedBy(noise) * weapon.Model.Speed;

        FireResult result = weapon.Fire(ctx, muzzle, launch, aimed.Aim.AimPoint);
        if (result.IsShot && result.ProjectileSlot >= Main.maxProjectiles)
        {
            combat.NoteNoUseWorthFiring();
            return false;
        }
        // A swing files the same shot record with no projectile slot, so the log reads a swing beside a shot
        // in one vocabulary; a landed swing is counted from the strike itself, since no projectile hit will follow.
        // The predicted hits with their ticks and damage, and the simulation's cache identity, pair the
        // prediction with the shot-event records that follow it in the knowledge audit.
        var predicted = new System.Text.StringBuilder();
        foreach (SimHit hit in aimed.Use.Hits)
        {
            if (predicted.Length > 0)
                predicted.Append('+');
            predicted.Append(FormattableString.Invariant($"{hit.Slot}@{hit.Tick}:{hit.Damage:0.0}"));
        }
        Point muzzleTile = MovementQueries.Tile(muzzle);
        string sim = FormattableString.Invariant($"m={muzzleTile.X},{muzzleTile.Y}:a={(int)(aimed.Aim.AimPoint.X / 8f)},{(int)(aimed.Aim.AimPoint.Y / 8f)}:k={KnowledgeRevision.Current}:t={TerrainChanges.Revision}");
        GodsEyeEvents.RecordShot(ctx.Npc, target, result.ProjectileSlot, muzzle, launch, aimed.Aim.AimPoint, weapon.Name,
            aimed.Use.ImpactTick, plan.Weighted, plan.TargetKillTicks?.Length ?? 0, plan.Outcome.PlayerHarmPrevented,
            plan.Id, Array.IndexOf(plan.Segments, segment), useIndex, predicted.ToString(), sim);
        if (result.IsShot)
        {
            // The arc watch for each spawn opened inside Fire, beside its trace; what the hand adds here is
            // the identity the spawn cannot know — what it was aimed at — for the ledger and the spoof.
            foreach (int slot in result.AllSlots)
            {
                TrackLandedHits.Register(slot, target, weapon.ItemType);
                RecordProjectileFlights.NoteCompanionAim(slot, target.Center);
            }
        }
        combat.LastAimOffset = weapon.IsSwing ? 0f : MathHelper.WrapAngle(launch.ToRotation() - aimed.Intercept.LaunchDirection.ToRotation());
        if (forecast != null)
            OpenOutcome(weapon, target, prior, forecast.ImpactTicks, combat.LastAimOffset, result);
        combat.Cooldown = weapon.UseTime;
        ctx.Companion.StartAnimation(weapon.ItemType, Math.Max(10, weapon.BaseUseTime));
        ctx.Companion.SetAimRotation(launch);
        combat.NoteFired();
        combat.Planner.NoteUseFired(ctx.Senses.Tick);
        return true;
    }

    /// <summary>
    /// Open the use's outcome window against what its forecast predicted and the context it was fired in — the aim as it
    /// actually left, noise included, because that is the offset whose outcome is about to be observed. A shot's window
    /// holds its projectile until it and its descendants die; a swing's strikes are already known, so its window closes now.
    /// </summary>
    private static void OpenOutcome(CompanionWeapon weapon, NPC target, in ForecastUses.ForecastPrior prior, int impactTicks, float firedAim, FireResult result)
    {
        float[] context = prior.Inputs.With(firedAim, prior.AimedDebuffed);
        int window = ShotOutcomes.Open(weapon.ItemType, target, context, prior.Damage, prior.Struck, prior.Charge, weapon.UseTime, impactTicks, Main.GameUpdateCount);
        if (result.IsShot)
        {
            foreach (int slot in result.AllSlots)
                ShotOutcomes.AddSlot(window, slot);
            return;
        }
        if (result.Strikes != null)
            foreach (SwingStrike strike in result.Strikes)
                ShotOutcomes.Strike(window, strike.Npc, strike.Dealt, strike.BuffTypesBefore, strike.BuffTimesBefore);
        ShotOutcomes.Close(window, Main.GameUpdateCount, bounded: false);
    }

    private bool FailedTraceStillApplies(in ActionContext ctx, NPC target, Vector2 muzzle)
        => ctx.Senses.Tick < failedUntilTick
            && target.whoAmI == failedTarget
            && HostileAttackSources.Generation(target) == failedGeneration
            && TerrainChanges.Revision == failedTerrainRevision
            && muzzle == failedMuzzle
            && target.Center == failedTargetCenter
            && target.velocity == failedTargetVelocity;

    private void RememberFailedTrace(in ActionContext ctx, NPC target, Vector2 muzzle)
    {
        failedTarget = target.whoAmI;
        failedGeneration = HostileAttackSources.Generation(target);
        failedMuzzle = muzzle;
        failedTargetCenter = target.Center;
        failedTargetVelocity = target.velocity;
        failedTerrainRevision = TerrainChanges.Revision;
        failedUntilTick = ctx.Senses.Tick + FailedTraceFreshnessTicks;
    }
}
