#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Behaviours;
using AICompanion.Companion.Brain.BehaviourDiagnostics;
using AICompanion.Companion.Brain.ProjectileAiming;
using AICompanion.Companion.Brain.SharedMovementSystem;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// Compares feasible weapon/target pairs by useful damage, timely threat removal and
/// follow-up attacks. Geometry supplies actual predicted intersections; the outcome
/// evaluator reserves health so an in-flight shot cannot earn its kill twice. The
/// bounded candidate set is ordered by danger before distance. Neither a weapon nor
/// an enemy type owns a special suitability rule. The final, accuracy-adjusted arc is
/// checked again before firing because the forecast is not permission to hit a wall.
/// </summary>
public sealed class Arsenal
{
    /// <summary>The window the damage is counted over: three seconds, in ticks.</summary>
    public const int HorizonTicks = 180;

    /// <summary>How many bodies one arc is walked for, and the cap for unlimited-pierce projectiles.</summary>
    public const int MaxPierceCounted = 8;

    /// <summary>How long a weapon choice is kept before the arcs are simulated again.</summary>
    private const int ChoiceCacheTicks = 12;

    public CompanionWeapon Primary = new BowWeapon();
    public CompanionWeapon Secondary = new ThrowingKnifeWeapon();

    public CompanionWeapon? LastChosen { get; private set; }
    public bool LastShotSolved { get; private set; }

    /// <summary>
    /// What each weapon scored at the last choice, the loser included. The rejected option is
    /// recorded on purpose: the log has always said which weapon was picked and never what the
    /// alternative was worth, so "why is it using that" was unanswerable from the record and had
    /// to be re-derived by hand. Two numbers make the choice auditable without playing the game.
    /// </summary>
    public float LastPrimaryExpected { get; private set; }
    public float LastSecondaryExpected { get; private set; }
    public float LastAttackValue { get; private set; }
    public float LastPreventedHarm { get; private set; }
    public int LastExpectedKills { get; private set; }
    public int CooldownTicks => cooldown;

    /// <summary>
    /// Why the last tick did or did not put a projectile in the air, as one word: <c>fired</c>,
    /// <c>cooldown</c>, <c>no-arc</c> (a target, and no launch angle that reaches it), <c>no-target</c>
    /// (nothing worth shooting) or <c>hands-busy</c> (a tool is in the arm the throw needs). A record
    /// that says only whether a shot happened cannot separate "it was reloading" from "it stood there
    /// with a bow it could not fire", and those two want opposite fixes.
    /// </summary>
    public string LastFireOutcome { get; private set; } = "-";

    /// <summary>The hands are driving a tool this tick, so no shot was even attempted.</summary>
    public void NoteHandsBusy()
    {
        LastFireOutcome = "hands-busy";
        LastShotSolved = false;
    }

    private int cooldown;

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

    private readonly NPC[] pierced = new NPC[MaxPierceCounted];
    private readonly List<NPC> hostiles = new();

    public void Tick()
    {
        if (cooldown > 0)
            cooldown--;
    }

    /// <summary>The weapon with the highest predicted outcome value against this target from here.</summary>
    public CompanionWeapon Choose(in ActionContext ctx, NPC target)
    {
        int now = ctx.Senses.Tick;
        int slot = target.whoAmI;
        ShotState state = ShotState.Capture(ctx, target);
        if (chosenFor == slot && now - chosenAt < ChoiceCacheTicks && chosen != null && state == chosenState)
            return chosen;

        Collect(ctx);
        var options = new List<EvaluateAttackOutcomes.Attack>();
        var a = ForecastAttack(ctx, Primary, 0, target, out _);
        var b = ForecastAttack(ctx, Secondary, 1, target, out _);
        if (a != null) options.Add(a);
        if (b != null) options.Add(b);
        var targets = AttackTargets(ctx);
        var first = a == null ? default : EvaluateAttackOutcomes.Evaluate(a, options, targets, cooldown, HorizonTicks);
        var second = b == null ? default : EvaluateAttackOutcomes.Evaluate(b, options, targets, cooldown, HorizonTicks);
        chosen = first.Value >= second.Value ? Primary : Secondary;
        LastPrimaryExpected = first.Damage;
        LastSecondaryExpected = second.Damage;
        chosenFor = slot;
        chosenAt = now;
        chosenState = state;
        LastChosen = chosen;
        return chosen;
    }

    private CompanionWeapon? chosen;
    private int chosenFor = -1;
    private int chosenAt = int.MinValue;
    private ShotState chosenState;

    public WeaponProfile? ProfileFor(in ActionContext ctx, NPC? target)
        => target == null ? null : Choose(ctx, target).Profile;

    /// <summary>
    /// The best legal first attack over a bounded target shortlist, considering effective damage,
    /// threat removal and follow-up attacks together. A failed arc earns no attack value. A null
    /// result means no considered option solved a useful shot, not proof that every enemy is unreachable.
    ///
    /// Held while feasible unless a more urgent player threat appears. Only a bounded shortlist,
    /// ordered by urgency then distance, is considered: with thirteen hostiles
    /// on screen, scoring all of them costs an order of magnitude more flight simulation than the
    /// whole rest of the brain tick.
    /// </summary>
    public NPC? BestTarget(in ActionContext ctx)
    {
        int now = ctx.Senses.Tick;
        if (held != null && (!held.active || held.life <= 0 || !held.CanBeChasedBy()
            || HostileAttackSources.Generation(held) != heldGeneration))
            held = null;
        ThreatRecord? urgent = ctx.Senses.Threats.MostUrgent;
        ThreatRecord? heldThreat = held == null ? null : ctx.Senses.Threats.Threats.Find(t => ReferenceEquals(t.Npc, held));
        bool newlyUrgent = urgent != null && urgent.Npc != held && urgent.Urgency > (heldThreat?.Urgency ?? 0f);
        int stamp = CombatStamp(ctx);
        if (held != null && now - heldAt < TargetHoldTicks && stamp == heldStamp && !newlyUrgent && CanEngage(ctx, held))
            return held;

        Collect(ctx);
        candidates.Clear();
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Npc != null && t.Npc.active && t.Npc.life > 0 && t.Npc.CanBeChasedBy() && t.DistanceToCompanion <= MathF.Max(Primary.Reach, Secondary.Reach))
                candidates.Add(t);
        candidates.Sort((x, y) =>
        {
            int urgency = MathF.Max(y.Urgency, y.UrgencyToCompanion).CompareTo(MathF.Max(x.Urgency, x.UrgencyToCompanion));
            return urgency != 0 ? urgency : x.DistanceToCompanion.CompareTo(y.DistanceToCompanion);
        });

        NPC? best = null;
        float bestScore = 0f;
        int considered = Math.Min(candidates.Count, MaxTargetsConsidered);
        TargetEvidenceTick = now;
        var evidence = new List<string>();
        var attacks = new List<EvaluateAttackOutcomes.Attack>();
        var targets = AttackTargets(ctx);
        for (int i = 0; i < considered; i++)
        {
            ThreatRecord t = candidates[i];
            for (int w = 0; w < 2; w++)
            {
                var attack = ForecastAttack(ctx, w == 0 ? Primary : Secondary, w, t.Npc, out string rejection);
                if (attack != null) attacks.Add(attack);
                else evidence.Add($"{t.Npc.whoAmI}:{HostileAttackSources.Generation(t.Npc)}:0:0:weapon={w}:{rejection}");
            }
        }
        EvaluateAttackOutcomes.Attack? winner = null;
        EvaluateAttackOutcomes.Outcome winningOutcome = default;
        foreach (var attack in attacks)
        {
            var outcome = EvaluateAttackOutcomes.Evaluate(attack, attacks, targets, cooldown, HorizonTicks);
            float score = outcome.Value;
            evidence.Add(FormattableString.Invariant($"{attack.Target}:{HostileAttackSources.Generation(Main.npc[attack.Target])}:{outcome.Damage:0.000}:weapon={attack.Weapon}:kills={outcome.Kills}:harm={outcome.PreventedHarm:0.000}:value={score:0.000}"));
            if (score > bestScore)
            {
                bestScore = score;
                best = Main.npc[attack.Target]; winner = attack; winningOutcome = outcome;
            }
        }

        held = best;
        heldGeneration = best == null ? 0 : HostileAttackSources.Generation(best);
        heldAt = now;
        heldStamp = stamp;
        LastTargetExpected = winningOutcome.Damage;
        LastAttackValue = winningOutcome.Value;
        LastPreventedHarm = winningOutcome.PreventedHarm;
        LastExpectedKills = winningOutcome.Kills;
        if (winner != null && best != null)
        {
            chosen = winner.Weapon == 0 ? Primary : Secondary;
            chosenFor = best.whoAmI; chosenAt = now; chosenState = ShotState.Capture(ctx, best);
            LastChosen = chosen;
            LastPrimaryExpected = LastSecondaryExpected = 0f;
            foreach (var attack in attacks)
                if (attack.Target == best.whoAmI)
                {
                    var outcome = EvaluateAttackOutcomes.Evaluate(attack, attacks, targets, cooldown, HorizonTicks);
                    if (attack.Weapon == 0) LastPrimaryExpected = outcome.Damage; else LastSecondaryExpected = outcome.Damage;
                }
        }
        TargetEvidence = string.Join("|", evidence);
        return best;
    }

    private EvaluateAttackOutcomes.Attack? ForecastAttack(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target, out string rejection)
    {
        rejection = "outside-reach";
        Vector2 muzzle = Muzzle(ctx.Npc);
        if (Vector2.Distance(muzzle, target.Center) > weapon.Reach) return null;
        rejection = "no-clear-trajectory";
        if (!TrajectoryAimer.TrySolve(muzzle, target, weapon.Profile, out TrajectorySolution solution))
        {
            BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, null, rejection);
            return null;
        }
        BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, solution.LaunchVelocity, "solved");
        int crossed = TrajectoryAimer.PathHits(muzzle, solution.LaunchVelocity, weapon.Profile, hostiles, pierced);
        var hits = new List<EvaluateAttackOutcomes.Hit>();
        for (int i = 0; i < Math.Min(crossed, weapon.Pierce); i++)
            hits.Add(new(pierced[i].whoAmI, PerHit(ctx, weapon, pierced[i])));
        rejection = hits.Count == 0 ? "no-damageable-intercept" : "accepted";
        return hits.Count == 0 ? null : new(slot, target.whoAmI, weapon.UseTime, solution.ImpactTick, hits.ToArray());
    }

    private static List<EvaluateAttackOutcomes.Target> AttackTargets(in ActionContext ctx)
    {
        var targets = new List<EvaluateAttackOutcomes.Target>();
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Npc.CanBeChasedBy()) targets.Add(new(t.Npc.whoAmI, t.Npc.life,
                MathF.Max(t.Urgency, t.UrgencyToCompanion), Math.Max(t.ExpectedDamage, t.Npc.damage)));
        return targets;
    }

    private static int CombatStamp(in ActionContext ctx)
    {
        var hash = new HashCode(); hash.Add(Muzzle(ctx.Npc)); hash.Add(TerrainChanges.Revision);
        foreach (var t in ctx.Senses.Threats.Threats)
        {
            hash.Add(t.Npc.whoAmI); hash.Add(HostileAttackSources.Generation(t.Npc)); hash.Add(t.Npc.life);
            hash.Add(t.Npc.Center); hash.Add(t.Npc.velocity); hash.Add(t.Urgency); hash.Add(t.UrgencyToCompanion);
        }
        return hash.ToHashCode();
    }

    public int TargetEvidenceTick { get; private set; }
    public string TargetEvidence { get; private set; } = "";

    /// <summary>
    /// What one hit actually takes off this hostile, which is the weapon's damage less the armour
    /// in the way. Defence matters to the comparison and not only to the absolute number, because
    /// it is subtracted rather than scaled: a defence of 6 costs a 6-damage knife hit its whole
    /// value and an 18-damage arrow a sixth of it, so ignoring it silently favours the weapon that
    /// throws many small hits at exactly the enemies it is worst against.
    ///
    /// Crit is deliberately absent. It would belong, and the reason it is not here is that these
    /// projectiles are spawned from the companion's own NPC source rather than through the player's
    /// item use, and that path does not carry the player's crit chance — so scoring for a crit the
    /// shot will not get is scoring a fiction. It was in the first version and came out on review.
    /// If crits do turn out to land, this is the one place it goes back.
    /// </summary>
    private static float PerHit(in ActionContext ctx, CompanionWeapon weapon, NPC target)
        => MathF.Max(1f, weapon.DamagePerHit(ctx) - target.defense / 2f);

    /// <summary>How long a target is kept before the candidates are scored again.</summary>
    private const int TargetHoldTicks = 15;

    /// <summary>How many candidates, ordered by urgency then distance, get their arcs simulated.</summary>
    private const int MaxTargetsConsidered = 4;

    /// <summary>The score the held target won with, for the overlay and the telemetry.</summary>
    public float LastTargetExpected { get; private set; }

    private int interventionCheckedAt = int.MinValue;
    private float interventionTicks = float.PositiveInfinity;
    private int interventionCooldown;
    private NPC? interventionTarget;
    private int interventionGeneration;
    private ShotState interventionState;

    private readonly record struct ShotState(Vector2 Muzzle, Vector2 Target, Vector2 Velocity, int Life, int Generation, int Terrain)
    {
        public static ShotState Capture(in ActionContext ctx, NPC target) => new(Arsenal.Muzzle(ctx.Npc),
            target.Center, target.velocity, target.life, HostileAttackSources.Generation(target), TerrainChanges.Revision);
    }

    /// <summary>Optimistic time to remove the current threat, including flight, reload and repeat hits.</summary>
    public float EstimateInterventionTicks(in ActionContext ctx)
    {
        NPC? target = ctx.Senses.Threats.MostUrgent?.Npc;
        if (target == null || !target.CanBeChasedBy()) return interventionTicks = float.PositiveInfinity;
        int generation = global::AICompanion.Companion.Brain.WorldObservation.HostileAttackSources.Generation(target);
        ShotState state = ShotState.Capture(ctx, target);
        if (target == interventionTarget && generation == interventionGeneration && interventionCheckedAt != int.MinValue
            && state == interventionState
            && unchecked(ctx.Senses.Tick - interventionCheckedAt) < ChoiceCacheTicks)
            return Math.Max(0f, interventionTicks - interventionCooldown + cooldown);
        interventionCheckedAt = ctx.Senses.Tick;
        interventionTarget = target;
        interventionGeneration = generation;
        interventionState = state;
        interventionCooldown = cooldown;
        CompanionWeapon weapon = Choose(ctx, target);
        if (!TrajectoryAimer.TrySolve(Muzzle(ctx.Npc), target, weapon.Profile, out TrajectorySolution solution))
            return interventionTicks = float.PositiveInfinity;
        // Protection needs the threat removed, not merely the first projectile arriving.
        // This remains an optimistic estimate: misses and target motion can only delay it.
        int hits = (int)MathF.Ceiling(target.life / PerHit(ctx, weapon, target));
        return interventionTicks = Math.Max(0, cooldown) + solution.ImpactTick
            + Math.Max(0, hits - 1) * Math.Max(1, weapon.UseTime);
    }

    private NPC? held;
    private int heldGeneration;
    private int heldAt = 0;
    private int heldStamp;
    private readonly List<ThreatRecord> candidates = new();

    /// <summary>The hostiles worth simulating against: the threat sense's own list, alive and hostile.</summary>
    private void Collect(in ActionContext ctx)
    {
        hostiles.Clear();
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Npc != null && t.Npc.active && t.Npc.life > 0 && t.Npc.CanBeChasedBy())
                hostiles.Add(t.Npc);
    }

    private readonly int[] engageCheckedAt = new int[Main.maxNPCs];
    private readonly bool[] engageResult = new bool[Main.maxNPCs];
    private readonly ShotState[] engageState = new ShotState[Main.maxNPCs];
    private const int EngageCacheTicks = 20;

    /// <summary>
    /// Whether any equipped weapon has a solvable shot at the target from where the companion
    /// stands now. An unchanged muzzle, target and terrain share a bounded cache; motion expires
    /// it immediately so a brief firing window is not hidden by a stale negative answer.
    /// </summary>
    public bool CanEngage(in ActionContext ctx, NPC target)
    {
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return false;
        int now = ctx.Senses.Tick;
        int slot = target.whoAmI;
        ShotState state = ShotState.Capture(ctx, target);
        if (now - engageCheckedAt[slot] < EngageCacheTicks && engageCheckedAt[slot] != 0 && engageState[slot] == state)
            return engageResult[slot];
        Vector2 muzzle = Muzzle(ctx.Npc);
        bool can = TrajectoryAimer.Solve(muzzle, target, Primary.Profile) != null
            || TrajectoryAimer.Solve(muzzle, target, Secondary.Profile) != null;
        engageCheckedAt[slot] = now;
        engageState[slot] = state;
        engageResult[slot] = can;
        return can;
    }

    /// <summary>Face the target and fire if a shot exists and the cooldown allows. Returns true on a shot.</summary>
    public bool TryFire(in ActionContext ctx, NPC? target)
    {
        if (target == null || !target.active || target.life <= 0 || !target.CanBeChasedBy())
        {
            LastShotSolved = false;
            LastFireOutcome = "no-target";
            return false;
        }
        CompanionWeapon weapon = Choose(ctx, target);
        ctx.Companion.Motor.Face(target.Center.X);
        if (cooldown > 0)
        {
            // No solve is attempted on a cooldown tick, so the solved flag would otherwise report
            // the last tick that did attempt one and read as a live shot for the whole reload.
            LastShotSolved = false;
            LastFireOutcome = "cooldown";
            ctx.Companion.HoldItem(weapon.ItemType);
            return false;
        }

        Vector2 muzzle = Muzzle(ctx.Npc);
        if (FailedTraceStillApplies(ctx, target, muzzle))
        {
            ctx.Companion.HoldItem(weapon.ItemType);
            LastShotSolved = false;
            LastFireOutcome = "no-arc";
            return false;
        }

        bool solved = TrajectoryAimer.TrySolve(muzzle, target, weapon.Profile, out TrajectorySolution solution);
        if (!solved)
        {
            // The choice is a dozen ticks old and the world has moved: the arc that scored is gone.
            // The other weapon is tried before giving up, because standing still holding an unusable
            // weapon is how the companion died with thirteen hostiles on it and a bow in its hand.
            CompanionWeapon other = ReferenceEquals(weapon, Primary) ? Secondary : Primary;
            if (TrajectoryAimer.TrySolve(muzzle, target, other.Profile, out TrajectorySolution fallback))
            {
                weapon = other;
                chosen = other;
                LastChosen = other;
                solution = fallback;
                solved = true;
            }
        }

        ctx.Companion.HoldItem(weapon.ItemType);
        LastShotSolved = solved;
        if (!solved)
        {
            LastFireOutcome = "no-arc";
            RememberFailedTrace(ctx, target, muzzle);
            return false;
        }

        float noise = (Main.rand.NextFloat() * 2f - 1f) * weapon.AimNoise;
        Vector2 launch = solution.LaunchVelocity.RotatedBy(noise);
        if (!TrajectoryAimer.TryTrace(muzzle, launch, target, weapon.Profile, out TrajectorySolution finalShot))
        {
            LastShotSolved = false;
            LastFireOutcome = "no-arc";
            return false;
        }

        int projectileIndex = weapon.Fire(ctx, muzzle, launch);
        if (projectileIndex < 0 || projectileIndex >= Main.maxProjectiles)
        {
            LastFireOutcome = "projectile-capacity";
            return false;
        }
        GodsEyeEvents.RecordShot(ctx.Npc, target, projectileIndex, muzzle, launch, finalShot.ExpectedImpact, weapon.Name, finalShot.ImpactTick, LastAttackValue, LastExpectedKills, LastPreventedHarm);
        cooldown = weapon.UseTime;
        ctx.Companion.StartAnimation(weapon.ItemType, Math.Max(10, weapon.BaseUseTime));
        ctx.Companion.SetAimRotation(launch);
        LastFireOutcome = "fired";
        return true;
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

    private static Vector2 Muzzle(NPC npc) => npc.Center + new Vector2(npc.direction * 10f, -4f);
}
