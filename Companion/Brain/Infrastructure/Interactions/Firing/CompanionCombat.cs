#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.Combat.Planning;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Learning;
using AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.Simulation;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing;

/// <summary>
/// The one object per companion that is combat: the gear enumeration, the shared cooldown, the fire
/// vocabulary, the tick's enemy forecast, the planner and its commitment, and the firing interaction.
/// It replaces the arsenal, whose weapon/target/aim choice moved into the planner and whose trigger
/// moved into <see cref="FireDueUse"/>; what stays here is facts about the hand and the forecast every
/// combat question shares. The only combat surface the rest of the companion references.
/// </summary>
public sealed class CompanionCombat
{
    /// <summary>The window the damage is counted over: three seconds, in ticks.</summary>
    public const int HorizonTicks = 180;

    private readonly List<CompanionWeapon> weapons = new();
    private int gearSignature = int.MinValue;
    private float maxReach;

    /// <summary>The weapons in hand, in slot order; empty when both weapon slots are.</summary>
    public IReadOnlyList<CompanionWeapon> Weapons { get { Refresh(Main.LocalPlayer); return weapons; } }

    /// <summary>The furthest any weapon in hand is worth attempting from, px; zero with no weapon.</summary>
    public float MaxReach { get { Refresh(Main.LocalPlayer); return maxReach; } }

    /// <summary>
    /// Why the last tick did or did not use a weapon, as one word: <c>fired</c>, <c>cooldown</c>,
    /// <c>no-use-worth-firing</c> (combat runs and nothing priced a shot), <c>no-weapon</c> (both weapon
    /// slots empty or refused), <c>hands-busy</c> (a tool is in the arm) or <c>not-fighting</c> (combat is
    /// not the running activity, so the hands never aimed). <c>not-fighting</c> is separate from an empty
    /// plan on purpose, so a quiet weapon during mining is never read as a missing target.
    /// </summary>
    public string LastFireOutcome { get; private set; } = "-";

    internal void NoteFired() => LastFireOutcome = "fired";
    internal void NoteCooldown() => LastFireOutcome = "cooldown";
    internal void NoteNoUseWorthFiring() => LastFireOutcome = "no-use-worth-firing";
    internal void NoteNoWeapon()
    {
        LastFireOutcome = "no-weapon";
        LastShotSolved = false;
    }

    /// <summary>The hands are driving a tool this tick, so no shot was even attempted.</summary>
    public void NoteHandsBusy()
    {
        LastFireOutcome = "hands-busy";
        LastShotSolved = false;
    }

    /// <summary>Combat is not the running activity this tick, so the hands never aimed at anything.</summary>
    public void NoteNotFighting()
    {
        LastFireOutcome = "not-fighting";
        LastShotSolved = false;
    }

    /// <summary>Whether the last firing attempt solved a use, for the telemetry's solved column.</summary>
    public bool LastShotSolved { get; internal set; }

    /// <summary>The aim offset from the intercept the last shot actually left at, radians, noise included.</summary>
    public float LastAimOffset { get; internal set; }

    public int CooldownTicks => cooldown;
    private int cooldown;

    internal int Cooldown { get => cooldown; set => cooldown = value; }

    /// <summary>Restore the cooldown a snapshot carried: the audit's replay prices the same ready tick.</summary>
    public void AssumeCooldown(int ticks) => cooldown = ticks;

    /// <summary>The tick's enemy forecast, built once and read by every forecast, so every weapon and target in one decision meets the same enemies.</summary>
    private int forecastTick = int.MinValue;
    private IReadOnlyList<EnemyForecast> forecastEnemies = Array.Empty<EnemyForecast>();

    /// <summary>The planner's commitment: the plan the body is performing, if any.</summary>
    public CommitAttackPlan Planner { get; } = new();

    /// <summary>The firing interaction: the plan's due use, or the best from where the body is.</summary>
    public FireDueUse Hands { get; } = new();

    /// <summary>The next attack plan's id. Fixtures searching their own plans draw from the same counter, so ids stay unique per companion.</summary>
    public int NextPlanId { get; set; } = 1;

    /// <summary>Ages the reload and closes outcome windows whose bound has elapsed.</summary>
    public void Tick()
    {
        if (cooldown > 0)
            cooldown--;
        ShotOutcomes.Tick(Main.GameUpdateCount);
    }

    /// <summary>
    /// Re-enumerate the weapons when the gear changed. Every choice, hold and cache below refers to
    /// a weapon by slot or by reference, so a changed gear drops them all: a choice made for a bow
    /// that is no longer in hand is not a choice.
    /// </summary>
    public void Refresh(Player player)
    {
        CompanionGear gear = player.GetModPlayer<CompanionPlayer>().Gear;
        int signature = gear.Signature;
        if (signature == gearSignature)
            return;
        gearSignature = signature;
        weapons.Clear();
        foreach (GearSlot slot in new[] { GearSlot.FirstWeapon, GearSlot.SecondWeapon })
        {
            Item item = gear[slot];
            if (!item.IsAir && CompanionGear.Accepts(slot, item, out _))
                weapons.Add(new ItemWeapon(item));
        }
        maxReach = 0f;
        foreach (CompanionWeapon weapon in weapons)
            maxReach = MathF.Max(maxReach, weapon.Reach);
        Planner.Release("gear-changed");
        LastShotSolved = false;
        interventionCheckedAt = int.MinValue;
        idealCheckedAt = int.MinValue;
    }

    /// <summary>
    /// The hostiles worth simulating against, forecast once per tick: the threat sense's own list, alive and
    /// hostile, with their predicted boxes. The simulation cache is cleared with it, because a use cached
    /// against last tick's enemies is a use priced for bodies that have moved.
    /// </summary>
    public IReadOnlyList<EnemyForecast> EnsureForecast(in ActionContext ctx)
    {
        int now = ctx.Senses.Tick;
        if (now != forecastTick)
        {
            forecastTick = now;
            forecastEnemies = ForecastEnemies.FromThreats(ctx.Senses.Threats.Threats);
            CacheSimulatedUses.ClearAtTick(now);
        }
        return forecastEnemies;
    }

    private int interventionCheckedAt = int.MinValue;
    private float interventionTicks = float.PositiveInfinity;

    /// <summary>
    /// The combat planner's predicted kill tick of the most urgent threat, from its committed plan; infinite
    /// with no weapon or no plan. The threat sense reads this as its intervention estimate, so protection
    /// prices the fight the companion is actually performing rather than an arc-free guess.
    /// </summary>
    public float EstimateInterventionTicks(in ActionContext ctx)
    {
        Refresh(ctx.Player);
        if (weapons.Count == 0 || Planner.Committed == null)
            return interventionTicks = float.PositiveInfinity;
        NPC? target = ctx.Senses.Threats.MostUrgent?.Npc;
        if (target == null || !target.CanBeChasedBy())
            return interventionTicks = float.PositiveInfinity;
        if (ctx.Senses.Tick == interventionCheckedAt)
            return interventionTicks;
        interventionCheckedAt = ctx.Senses.Tick;
        foreach ((int slot, int tick) in Planner.Committed.TargetKillTicks ?? Array.Empty<(int Slot, int Tick)>())
            if (slot == target.whoAmI)
                return interventionTicks = Math.Max(0, tick - ctx.Senses.Tick);
        return interventionTicks = float.PositiveInfinity;
    }

    /// <summary>Whether any weapon in hand has a real, intercepting use at this target from this muzzle.</summary>
    public bool ShotSolves(in ActionContext ctx, Vector2 muzzle, NPC target)
    {
        Refresh(ctx.Player);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return false;
        IReadOnlyList<EnemyForecast> enemies = EnsureForecast(ctx);
        for (int w = 0; w < weapons.Count; w++)
            if (ForecastUses.Forecast(ctx, weapons[w], w, target, muzzle, record: false, 0, enemies, out _, out _) != null) return true;
        return false;
    }

    /// <summary>
    /// The arsenal's outcome value for the best attack any weapon in hand could make from this muzzle against one body, as
    /// that body will be <paramref name="targetTickOffset"/> ticks from now. Hunt uses this to steer and position selection
    /// to price a stand: it does not pick the pair the hands will fire. Every weapon is asked, so a stand is worth the
    /// best shot it offers whichever weapon that is.
    /// </summary>
    public float BestShotValueFrom(in ActionContext ctx, Vector2 muzzle, NPC target, int targetTickOffset = 0)
    {
        Refresh(ctx.Player);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return 0f;
        IReadOnlyList<EnemyForecast> enemies = EnsureForecast(ctx);
        var attacks = new List<EvaluateAttackOutcomes.Attack>();
        for (int w = 0; w < weapons.Count; w++)
        {
            var attack = ForecastUses.Forecast(ctx, weapons[w], w, target, muzzle, record: false, targetTickOffset, enemies, out _, out _);
            if (attack != null) attacks.Add(attack);
        }
        if (attacks.Count == 0) return 0f;
        var targets = ForecastUses.AttackTargets(ctx);
        float best = 0f;
        foreach (var attack in attacks)
            best = MathF.Max(best, EvaluateAttackOutcomes.Evaluate(attack, attacks, targets, cooldown, HorizonTicks).Value);
        return best;
    }

    private int idealCheckedAt = int.MinValue;
    private int idealTarget = -1;
    private int idealGeneration;
    private int idealRevision;
    private float idealValue;

    /// <summary>
    /// The value the best weapon in hand would have against this target if geometry were no object: the evaluator's value of
    /// its assumed attacks, with no push charge. Position selection reads a stand's value as a share of this, so a stand is
    /// priced by how much of the best available attack it actually realises rather than by a scale of its own. Cached for the
    /// tick, the target and what the learner believes.
    /// </summary>
    public float IdealShotValue(in ActionContext ctx, NPC target)
    {
        Refresh(ctx.Player);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy() || weapons.Count == 0) return 0f;
        int generation = HostileAttackSources.Generation(target);
        if (idealCheckedAt == ctx.Senses.Tick && idealTarget == target.whoAmI && idealGeneration == generation && idealRevision == AttackLearning.Revision)
            return idealValue;
        var targets = ForecastUses.AttackTargets(ctx);
        var assumed = ForecastUses.AssumedAttacks(this, ctx, target);
        float best = 0f;
        foreach (var attack in assumed)
            best = MathF.Max(best, EvaluateAttackOutcomes.Evaluate(attack, assumed, targets, cooldown, HorizonTicks).Value);
        idealCheckedAt = ctx.Senses.Tick; idealTarget = target.whoAmI; idealGeneration = generation; idealRevision = AttackLearning.Revision;
        return idealValue = best;
    }

    /// <summary>Where a use leaves from: the orb's centre, because an orb has no facing and no hand.</summary>
    public static Vector2 Muzzle(NPC npc) => npc.Center;

    /// <summary>The muzzle a shot would leave from at a candidate hover: the orb's centre there, the same point <see cref="Muzzle"/> reads off the live body.</summary>
    public static Vector2 MuzzleAt(Vector2 hoverCentre) => hoverCentre;
}
