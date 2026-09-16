#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
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
/// The geometry adapter between the simulator and the outcome evaluator: aims priced as uses, uses
/// converted to attacks with the learned correction, pushes charged on the threat sense's own scale.
/// Moved out of the arsenal in its split so the planner and the firing interaction share the one
/// conversion rather than each owning a copy that could disagree about what a use is worth.
/// </summary>
public static class ForecastUses
{
    /// <summary>The raw inputs of a learning context other than the aim and the debuff, which differ per candidate and per body.</summary>
    public readonly record struct ContextInputs(float Distance, float Reach, float RelativeSpeed, int LaneHostiles, float OrbSpeed, float WidestAim)
    {
        public float[] With(float aimOffset, bool debuffedByOther)
            => AttackLearning.Context(Distance, Reach, aimOffset, WidestAim, RelativeSpeed, LaneHostiles, OrbSpeed, OrbPace.MaxSpeed, debuffedByOther);
    }

    /// <summary>What a forecast predicted before any learned correction, and the context it was made in, so a use can be taught against it.</summary>
    public readonly record struct ForecastPrior(float Damage, int Struck, float Charge, ContextInputs Inputs, float AimOffset, bool AimedDebuffed);

    /// <summary>One weapon's best simulated use at one target: the aim it leaves at, the intercept it is measured from, the use, and what the use puts on the target.</summary>
    public readonly record struct AimedUse(AimCandidate Aim, AimCandidate Intercept, SimulatedUse Use, float TargetDamage);

    /// <summary>
    /// The best use one weapon has at one target from one muzzle: every aim the solver proposes, each simulated
    /// against the tick's enemy forecast — from the cache where another question already priced it — and the one
    /// that puts the most prior damage on the target wins, breaking ties toward the most damage anywhere. The
    /// intercept is only the fallback all aims are measured from, never the answer by privilege: a bank that lands
    /// where the intercept meets a wall wins outright. Null when no aim's use strikes anything.
    /// </summary>
    public static AimedUse? BestAimUse(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target,
        Vector2 muzzle, IReadOnlyList<EnemyForecast> enemies, CombatWorld world, int fireTick, bool record,
        bool planning = false)
    {
        EnemyForecast? forecast = null;
        foreach (EnemyForecast enemy in enemies)
            if (enemy.Slot == target.whoAmI) { forecast = enemy; break; }
        if (forecast == null) return null;
        WeaponId id = SimulateUse.Identify(weapon, ctx, slot);
        ModifierState modifiers = ApplyCompanionModifiers.Current();
        int knowledge = KnowledgeRevision.Current;
        if (planning && CachePlannedSims.TryGetBest(id, modifiers, target.whoAmI, HostileAttackSources.Generation(target),
            muzzle, fireTick, knowledge, world.RefreshCount, enemies, out AimedUse? planned))
            return planned;
        PlanningBudget budget = PlanningBudget.Unbounded();
        IReadOnlyList<AimCandidate> aims = SolveAims.For(id, muzzle, forecast, world, enemies, fireTick, ref budget);
        if (aims.Count == 0) return null;
        AimCandidate intercept = aims[0];
        AimedUse? best = null;
        foreach (AimCandidate aim in aims)
        {
            SimulatedUse? use;
            if (planning)
            {
                if (!CachePlannedSims.TryGet(id, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, enemies, out use) || use == null)
                {
                    PlanningBudget simBudget = PlanningBudget.Unbounded();
                    use = SimulateUse.Simulate(id, muzzle, aim.AimPoint, aim.LaunchDirection, world, enemies, modifiers, fireTick, ref simBudget);
                    CachePlannedSims.Store(id, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, enemies, use);
                    CacheSimulatedUses.Store(id, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, use);
                }
            }
            else if (!CacheSimulatedUses.TryGet(id, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, out use) || use == null)
            {
                PlanningBudget simBudget = PlanningBudget.Unbounded();
                use = SimulateUse.Simulate(id, muzzle, aim.AimPoint, aim.LaunchDirection, world, enemies, modifiers, fireTick, ref simBudget);
                CacheSimulatedUses.Store(id, modifiers, muzzle, aim.AimPoint, fireTick, knowledge, world.RefreshCount, use);
            }
            float onTarget = 0f;
            foreach (SimHit hit in use.Hits)
                if (hit.Slot == target.whoAmI) onTarget += hit.Damage;
            if (record)
            {
                bool lands = onTarget > 0f;
                foreach (IReadOnlyList<Vector2> path in use.Paths)
                    if (path.Count > 1)
                        BrainInspectorSamples.RecordTrace(new List<Vector2>(path).ToArray(), lands ? "target intercepted" : "no hit on the target");
            }
            var candidate = new AimedUse(aim, intercept, use, onTarget);
            if (best == null || onTarget > best.Value.TargetDamage + 1e-6f
                || (MathF.Abs(onTarget - best.Value.TargetDamage) <= 1e-6f && use.TotalDamage > best.Value.Use.TotalDamage))
                best = candidate;
        }
        if (best == null || best.Value.Use.Hits.Count == 0) best = null;
        if (planning)
            CachePlannedSims.StoreBest(id, modifiers, target.whoAmI, HostileAttackSources.Generation(target),
                muzzle, fireTick, knowledge, world.RefreshCount, enemies, best);
        return best;
    }

    /// <summary>
    /// The same forecast the hands fire from, evaluated at an arbitrary muzzle — and against the target as it will be
    /// <paramref name="targetTickOffset"/> ticks from now — so hunt and position selection can ask whether a pose would
    /// actually shoot rather than whether a straight ray would.
    ///
    /// The geometry is the prior: the solve, the bodies the flight crosses, each hit's damage after armour and the push
    /// charge. The aim is the solver's best — the most simulated damage on the target, ties to the most anywhere —
    /// and the learner scales each hit's damage by what this weapon has achieved in this context; it no longer moves
    /// the shot, and the aim offset it is taught against is the one the winning aim left at. The intercept is only
    /// what the winner is measured from; a swing has no launch to be imprecise about, so the solver offers it its
    /// single aim and the noise is a shot's.
    /// </summary>
    public static EvaluateAttackOutcomes.Attack? Forecast(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target, Vector2 muzzle,
        bool record, int targetTickOffset, IReadOnlyList<EnemyForecast> enemies, out string rejection, out ForecastPrior prior)
    {
        prior = default;
        rejection = "outside-reach";
        if (!weapon.InReach(muzzle, target)) return null;
        int fireTick = Math.Max(0, targetTickOffset);
        CombatWorld world = CombatWorld.Current(muzzle, ctx.Player.Center, TerrainChanges.Revision);
        AimedUse? aimed = BestAimUse(ctx, weapon, slot, target, muzzle, enemies, world, fireTick, record);
        if (aimed == null)
        {
            rejection = "no-clear-trajectory";
            if (record) BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, null, rejection);
            return null;
        }
        SimulatedUse use = aimed.Value.Use;
        AimCandidate aim = aimed.Value.Aim;
        if (record) BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name,
            aim.LaunchDirection * weapon.Model.Speed, "solved");
        return AttackFromUse(ctx, weapon, slot, target, muzzle, use, aim, aimed.Value.Intercept, fireTick,
            out rejection, out prior);
    }

    /// <summary>
    /// One simulated use as the attack the evaluator prices: the learned correction on every damageable hit,
    /// the aim offset measured from the intercept, and the prior the outcome window is taught against. The
    /// planner calls this per aim rather than per best aim, because its candidate uses are every weapon times
    /// every aim the solver offers; the legacy best-aim forecast above calls it once for its winner.
    /// </summary>
    public static EvaluateAttackOutcomes.Attack? AttackFromUse(in ActionContext ctx, CompanionWeapon weapon, int slot,
        NPC target, Vector2 muzzle, SimulatedUse use, AimCandidate aim, AimCandidate intercept, int fireTick,
        out string rejection, out ForecastPrior prior)
    {
        prior = default;
        bool explore = Explore(ctx);
        var inputs = new ContextInputs(Vector2.Distance(muzzle, target.Center), weapon.Reach,
            (target.velocity - ctx.Npc.velocity).Length(), Math.Max(0, use.Struck - 1), ctx.Npc.velocity.Length(),
            LearnVolleyShapes.SpreadCone(weapon.ItemType));
        bool aimedDebuffed = ShotOutcomes.DebuffedByOther(target, weapon.ItemType);
        float aimOffset = MathHelper.WrapAngle(aim.LaunchDirection.ToRotation() - intercept.LaunchDirection.ToRotation());
        var hits = new List<EvaluateAttackOutcomes.Hit>();
        float priorDamage = 0f, charge = 0f;
        foreach (SimHit sim in use.Hits)
        {
            if ((uint)sim.Slot >= (uint)Main.maxNPCs) continue;
            NPC body = Main.npc[sim.Slot];
            if (body == null || !body.active || body.life <= 0 || !body.CanBeChasedBy()) continue;
            float danger = InducedDanger(ctx, weapon, body, muzzle, aim.LaunchDirection, sim.Damage);
            priorDamage += sim.Damage;
            charge += danger;
            hits.Add(LearnedHit(ctx, weapon, body, sim.Damage, danger, inputs, aimOffset, explore));
        }
        rejection = hits.Count == 0 ? "no-damageable-intercept" : "accepted";
        if (hits.Count == 0) return null;
        prior = new ForecastPrior(priorDamage, hits.Count, charge, inputs, aimOffset, aimedDebuffed);
        return new(slot, target.whoAmI, weapon.UseTime, Math.Max(1, use.ImpactTick - fireTick), hits.ToArray(), aimOffset,
            ManaCost: weapon.ManaCost);
    }

    /// <summary>
    /// One forecast hit with the learned correction applied: its damage in the body's current state, its damage against a
    /// body the other weapon has debuffed, and the learned chance and length of the debuff this weapon's hit leaves on
    /// that enemy type. With no evidence both damages are the prior exactly.
    /// </summary>
    public static EvaluateAttackOutcomes.Hit LearnedHit(in ActionContext ctx, CompanionWeapon weapon, NPC body, float perHit, float danger,
        ContextInputs inputs, float aim, bool explore)
    {
        int item = weapon.ItemType;
        float chance = AttackLearning.DebuffChance(item, body.type);
        int ticks = AttackLearning.DebuffTicks(item, body.type);
        if (AttackLearning.Evidence(item) == 0)
            return new(body.whoAmI, perHit, danger, float.NaN, chance, ticks);
        int tick = ctx.Senses.Tick;
        bool debuffed = ShotOutcomes.DebuffedByOther(body, item);
        float now = AttackLearning.Factor(item, body.type, inputs.With(aim, debuffed), explore, tick);
        float ifDebuffed = debuffed ? now : AttackLearning.Factor(item, body.type, inputs.With(aim, true), explore, tick);
        // The push is charged at the share of the forecast the weapon has been seen to land, the same factor its damage takes,
        // so a weapon learned to miss is not charged for pushes it will not deliver. The factor is capped at one for the
        // charge because what lands beyond the forecast is damage — a child projectile, a debuff paying off — and each
        // hit's push is already learned per hit by the weapon-effects table.
        return new(body.whoAmI, perHit * now, danger * MathF.Min(1f, now), perHit * ifDebuffed, chance, ticks);
    }

    /// <summary>
    /// Whether decisions may explore this tick. Thompson sampling explores by drawing coefficients, which occasionally ranks
    /// a worse attack first; that is the price of learning and it is not worth paying while something dangerous is on either
    /// body, so above the declared danger every forecast uses the posterior mean. The danger is the threat sense's own
    /// urgency, the larger of the two bodies' for the most urgent threat.
    /// </summary>
    public static bool Explore(in ActionContext ctx)
    {
        float danger = 0f;
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            danger = MathF.Max(danger, MathF.Max(t.Urgency, t.UrgencyToCompanion));
        return danger <= Weights.WeaponExploreDangerCeiling;
    }

    /// <summary>
    /// The danger one hit's push adds, as the threat sense would weigh it: the target is displaced by the push the
    /// weapon-effects table expects in the direction the game will push it, and its urgency to the player and to the orb
    /// is re-weighed at the displaced centre by <see cref="ThreatUrgency"/>, the rule the threat sense itself uses. The orb
    /// is weighed as standing at <paramref name="muzzle"/>, because a forecast is asked about candidate stands as well as
    /// about the live body, and a push toward the stand the orb would be firing from is the push that comes back at it; its
    /// life and its sight stay the live body's, which are the same wherever it hovers. Only an
    /// increase is charged, per body, times what one of that enemy's hits takes off that body — so a push away from both
    /// bodies costs nothing, and the danger the enemy already carried is never charged, since both sides of the difference
    /// hold its speed, its sight and its reach as the sense decided them this tick. A body the enemy cannot reach, and a
    /// dead player, are not charged at all. An enemy the threat sense does not list carries no urgency to add to.
    ///
    /// Sight is held at what the sense measured, which under-charges a push that brings an enemy into a sight line it
    /// lacked; the alternative is a line-of-sight test per hit per forecast.
    /// </summary>
    public static float InducedDanger(in ActionContext ctx, CompanionWeapon weapon, NPC target, Vector2 muzzle, Vector2 launch, float perHit)
    {
        ThreatRecord? threat = null;
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (ReferenceEquals(t.Npc, target)) { threat = t; break; }
        if (threat == null) return 0f;
        Player player = ctx.Player;
        int direction = WeaponEffects.PriorDirection(weapon.PushesAwayFromOwner, launch.X, target.Center.X, player.Center.X);
        float shift = WeaponEffects.SettledPush(weapon.ItemType, target, weapon.Knockback, perHit, direction);
        if (shift == 0f) return 0f;
        Vector2 pushed = target.Center + new Vector2(shift, 0f);
        float charge = 0f;
        if (!player.dead && threat.CanReachPlayer)
        {
            float before = ThreatUrgency.ToPlayer(threat.EffectiveDamageToPlayer, player.statLife, threat.IsBoss,
                threat.TicksToPlayer, threat.Shoots, threat.HasSightOnPlayer);
            float after = ThreatUrgency.ToPlayer(threat.EffectiveDamageToPlayer, player.statLife, threat.IsBoss,
                Vector2.Distance(pushed, player.Center) / threat.ObservedSpeed, threat.Shoots, threat.HasSightOnPlayer);
            charge += MathF.Max(0f, after - before) * threat.EffectiveDamageToPlayer;
        }
        if (threat.CanReachCompanion)
        {
            NPC body = ctx.Npc;
            float before = ThreatUrgency.ToCompanion(threat.EffectiveDamageToCompanion, body.life, threat.IsBoss,
                Vector2.Distance(target.Center, muzzle) / threat.ObservedSpeed, threat.Shoots, threat.HasSightOnCompanion);
            float after = ThreatUrgency.ToCompanion(threat.EffectiveDamageToCompanion, body.life, threat.IsBoss,
                Vector2.Distance(pushed, muzzle) / threat.ObservedSpeed, threat.Shoots, threat.HasSightOnCompanion);
            charge += MathF.Max(0f, after - before) * threat.EffectiveDamageToCompanion;
        }
        return charge;
    }

    /// <summary>
    /// What one hit actually takes off this hostile, which is the weapon's damage less the armour
    /// in the way. Defence matters to the comparison and not only to the absolute number, because
    /// it is subtracted rather than scaled: a defence of 6 costs a 6-damage hit its whole value and
    /// an 18-damage hit a sixth of it, so ignoring it silently favours the weapon that throws many
    /// small hits at exactly the enemies it is worst against. Crit is absent for the reason the
    /// weapon gives: the companion's spawn path carries none. The prior is then scaled by what this
    /// weapon's hits have actually landed on this enemy type, which is where a resistance applied in a
    /// mod's hook shows up; the scaled value keeps the floor of one, because a hit always takes at least
    /// that and the removal estimates divide by it.
    /// </summary>
    public static float PerHit(in ActionContext ctx, CompanionWeapon weapon, NPC target)
        => MathF.Max(1f, WeaponEffects.PriorDamage(weapon.DamagePerHit(ctx), target) * WeaponEffects.DamageFactor(weapon.ItemType, target.type));

    public static List<EvaluateAttackOutcomes.Target> AttackTargets(in ActionContext ctx)
    {
        var targets = new List<EvaluateAttackOutcomes.Target>();
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Npc.CanBeChasedBy()) targets.Add(new(t.Npc.whoAmI, t.Npc.life,
                MathF.Max(t.Urgency, t.UrgencyToCompanion), Math.Max(t.ExpectedDamage, t.Npc.damage)));
        return targets;
    }

    /// <summary>
    /// One assumed attack per weapon at a target with no geometry in the way: the learned per-hit damage, the weapon's use
    /// time, a flight from the current distance at the weapon's launch speed, and no push charge — its muzzle is a stand
    /// nobody has chosen yet, so which side a push would carry the target to is unknown, and a stand's own forecast prices it.
    /// </summary>
    public static List<EvaluateAttackOutcomes.Attack> AssumedAttacks(CompanionCombat combat, in ActionContext ctx, NPC target)
    {
        var assumed = new List<EvaluateAttackOutcomes.Attack>();
        Vector2 muzzle = CompanionCombat.Muzzle(ctx.Npc);
        bool explore = Explore(ctx);
        var weapons = combat.Weapons;
        for (int w = 0; w < weapons.Count; w++)
        {
            CompanionWeapon weapon = weapons[w];
            float distance = Vector2.Distance(muzzle, target.Center);
            int flight = Math.Max(1, (int)(distance / MathF.Max(1f, weapon.Model.Speed)));
            var inputs = new ContextInputs(distance, weapon.Reach, (target.velocity - ctx.Npc.velocity).Length(), 0, ctx.Npc.velocity.Length(),
                LearnVolleyShapes.SpreadCone(weapon.ItemType));
            assumed.Add(new EvaluateAttackOutcomes.Attack(w, target.whoAmI, weapon.UseTime, flight,
                new[] { LearnedHit(ctx, weapon, target, PerHit(ctx, weapon, target), 0f, inputs, 0f, explore) }));
        }
        return assumed;
    }
}
