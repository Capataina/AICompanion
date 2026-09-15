#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.Brain.Infrastructure.Aiming;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Inventory;
using AICompanion.Companion.PlayerIntegration;

namespace AICompanion.Companion.Weapons;

/// <summary>
/// Compares feasible weapon/target pairs by useful damage, timely threat removal and
/// follow-up attacks. Geometry supplies actual predicted intersections; the outcome
/// evaluator reserves health so an in-flight shot cannot earn its kill twice. The
/// bounded candidate set is ordered by danger before distance. Neither a weapon nor
/// an enemy type owns a special suitability rule. The final, accuracy-adjusted arc is
/// checked again before firing because the forecast is not permission to hit a wall.
///
/// The weapons are whatever the two weapon slots of the gear hold, enumerated again only when
/// the gear's signature changes, and a slot the predicate refuses — an item that stopped passing
/// after it was saved — is skipped here as well, so a refused item never reaches a forecast.
/// </summary>
public sealed class Arsenal
{
    /// <summary>The window the damage is counted over: three seconds, in ticks.</summary>
    public const int HorizonTicks = 180;

    /// <summary>How many bodies one use is walked for, and the cap for unlimited-pierce projectiles and swings.</summary>
    public const int MaxPierceCounted = 8;

    /// <summary>How long a weapon choice is kept before the arcs are simulated again.</summary>
    private const int ChoiceCacheTicks = 12;

    private readonly List<CompanionWeapon> weapons = new();
    private int gearSignature = int.MinValue;

    /// <summary>The weapons in hand, in slot order; empty when both weapon slots are.</summary>
    public IReadOnlyList<CompanionWeapon> Weapons { get { Refresh(Main.LocalPlayer); return weapons; } }

    /// <summary>The furthest any weapon in hand is worth attempting from, px; zero with no weapon.</summary>
    public float MaxReach { get { Refresh(Main.LocalPlayer); return maxReach; } }
    private float maxReach;

    public CompanionWeapon? LastChosen { get; private set; }
    public bool LastShotSolved { get; private set; }

    /// <summary>
    /// What each weapon slot scored at the last choice, the loser included. The rejected option is
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
    /// Why the last tick did or did not use a weapon, as one word: <c>fired</c>, <c>cooldown</c>,
    /// <c>no-arc</c> (a target, and no launch angle that reaches it), <c>no-target</c> (nothing worth
    /// shooting), <c>no-weapon</c> (both weapon slots empty or refused) or <c>hands-busy</c> (a tool is in
    /// the arm). A record that says only whether a shot happened cannot separate "it was reloading"
    /// from "it stood there with a bow it could not fire", and those two want opposite fixes.
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

    /// <summary>
    /// Re-enumerate the weapons when the gear changed. Every choice, hold and cache below refers to
    /// a weapon by slot or by reference, so a changed gear drops them all: a choice made for a bow
    /// that is no longer in hand is not a choice.
    /// </summary>
    private void Refresh(in ActionContext ctx) => Refresh(ctx.Player);

    private void Refresh(Player player)
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
        chosen = null; chosenFor = -1; chosenAt = int.MinValue;
        held = null;
        LastChosen = null;
        failedTarget = -1;
        interventionCheckedAt = int.MinValue;
        fromHereAttacks = new List<EvaluateAttackOutcomes.Attack>();
        Array.Clear(engageCheckedAt);
    }

    /// <summary>The weapon with the highest predicted outcome value against this target from here, or null with nothing in hand.</summary>
    public CompanionWeapon? Choose(in ActionContext ctx, NPC target)
    {
        Refresh(ctx);
        if (weapons.Count == 0)
            return null;
        int now = ctx.Senses.Tick;
        int slot = target.whoAmI;
        ShotState state = ShotState.Capture(ctx, target);
        if (chosenFor == slot && now - chosenAt < ChoiceCacheTicks && chosen != null && state == chosenState)
            return chosen;

        Collect(ctx);
        var options = new List<EvaluateAttackOutcomes.Attack>();
        var forecasts = new EvaluateAttackOutcomes.Attack?[weapons.Count];
        for (int w = 0; w < weapons.Count; w++)
        {
            forecasts[w] = ForecastAttack(ctx, weapons[w], w, target, out _);
            if (forecasts[w] != null) options.Add(forecasts[w]!);
        }
        var targets = AttackTargets(ctx);
        CompanionWeapon best = weapons[0];
        float bestValue = float.NegativeInfinity;
        LastPrimaryExpected = LastSecondaryExpected = 0f;
        for (int w = 0; w < weapons.Count; w++)
        {
            var outcome = forecasts[w] == null ? default : EvaluateAttackOutcomes.Evaluate(forecasts[w]!, options, targets, cooldown, HorizonTicks);
            if (w == 0) LastPrimaryExpected = outcome.Damage; else if (w == 1) LastSecondaryExpected = outcome.Damage;
            // Ties keep the earlier slot, as the authored pair did.
            if (outcome.Value > bestValue) { bestValue = outcome.Value; best = weapons[w]; }
        }
        chosen = best;
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

    public FlightModel? ProfileFor(in ActionContext ctx, NPC? target)
        => target == null ? null : Choose(ctx, target)?.Model;

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
        Refresh(ctx);
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
        Vector2 muzzle = Muzzle(ctx.Npc);
        candidates.Clear();
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
            if (t.Npc != null && t.Npc.active && t.Npc.life > 0 && t.Npc.CanBeChasedBy() && AnyInReach(muzzle, t.Npc))
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
            for (int w = 0; w < weapons.Count; w++)
            {
                var attack = ForecastAttack(ctx, weapons[w], w, t.Npc, out string rejection);
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
            chosen = weapons[winner.Weapon];
            chosenFor = best.whoAmI; chosenAt = now; chosenState = ShotState.Capture(ctx, best);
            LastChosen = chosen;
            LastPrimaryExpected = LastSecondaryExpected = 0f;
            foreach (var attack in attacks)
                if (attack.Target == best.whoAmI)
                {
                    var outcome = EvaluateAttackOutcomes.Evaluate(attack, attacks, targets, cooldown, HorizonTicks);
                    if (attack.Weapon == 0) LastPrimaryExpected = outcome.Damage; else if (attack.Weapon == 1) LastSecondaryExpected = outcome.Damage;
                }
        }
        TargetEvidence = string.Join("|", evidence);
        fromHereAttacks = attacks;
        FromHereAttacksTick = now;
        return best;
    }

    private bool AnyInReach(Vector2 muzzle, NPC target)
    {
        foreach (CompanionWeapon weapon in weapons)
            if (weapon.InReach(muzzle, target)) return true;
        return false;
    }

    private List<EvaluateAttackOutcomes.Attack> fromHereAttacks = new();

    /// <summary>When the retained from-here attacks were last ranked; a held target keeps the older set.</summary>
    public int FromHereAttacksTick { get; private set; }

    /// <summary>
    /// What the hands could make of a target if its first shot can only leave after
    /// <paramref name="accessTicks"/>, valued by the same bounded outcome evaluator that ranks their own
    /// attacks. This is how pursuit asks whether a reposition is worth it without keeping a second
    /// opinion about what an attack is worth.
    ///
    /// A target the hands can already shoot uses its retained, trajectory-checked attacks from the last
    /// ranking. A target that needs a reposition gets one assumed attack per weapon — per-hit damage after
    /// the target's defence, the weapon's use time, a flight time from the current muzzle distance — which
    /// is optimistic about the arc (the positioner proves the real one at the stand) and pessimistic about
    /// the wait, because the hands' own shots during the walk are not counted. The retained from-here
    /// attacks are the continuation after that first shot, so a reposition that delays the arrows a
    /// healthy target would otherwise have taken is charged for them. Zero means the target cannot be
    /// damaged or nothing lands inside the evaluation window; it is not proof the target is worthless.
    /// </summary>
    public float EstimateDelayedAttackValue(in ActionContext ctx, NPC target, int accessTicks, bool shootableFromHere)
    {
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy() || weapons.Count == 0) return 0f;
        var targets = AttackTargets(ctx);
        var alternatives = new List<EvaluateAttackOutcomes.Attack>(fromHereAttacks);
        var firsts = new List<EvaluateAttackOutcomes.Attack>();
        if (shootableFromHere)
            foreach (var attack in fromHereAttacks)
                if (attack.Target == target.whoAmI) firsts.Add(attack);
        if (firsts.Count == 0)
        {
            Vector2 muzzle = Muzzle(ctx.Npc);
            for (int w = 0; w < weapons.Count; w++)
            {
                CompanionWeapon weapon = weapons[w];
                int flight = Math.Max(1, (int)(Vector2.Distance(muzzle, target.Center) / MathF.Max(1f, weapon.Model.Speed)));
                // No push is charged on an assumed attack: its muzzle is a stand nobody has chosen yet, so which side the
                // push would carry the target to is unknown, and the positioner's side preference prices that at the stand.
                var assumed = new EvaluateAttackOutcomes.Attack(w, target.whoAmI, weapon.UseTime, flight,
                    new[] { new EvaluateAttackOutcomes.Hit(target.whoAmI, PerHit(ctx, weapon, target)) });
                firsts.Add(assumed);
                alternatives.Add(assumed);
            }
        }
        int fireAt = Math.Max(cooldown, Math.Max(0, accessTicks));
        float best = 0f;
        foreach (var first in firsts)
            best = MathF.Max(best, EvaluateAttackOutcomes.Evaluate(first, alternatives, targets, fireAt, HorizonTicks).Value);
        return best;
    }

    private EvaluateAttackOutcomes.Attack? ForecastAttack(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target, out string rejection)
        => ForecastFrom(ctx, weapon, slot, target, Muzzle(ctx.Npc), record: true, out rejection);

    /// <summary>
    /// The same forecast the hands fire from, evaluated at an arbitrary muzzle so hunt can ask whether
    /// a pose would actually shoot rather than whether a straight ray would.
    /// </summary>
    private EvaluateAttackOutcomes.Attack? ForecastFrom(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target, Vector2 muzzle, bool record, out string rejection)
    {
        rejection = "outside-reach";
        if (!weapon.InReach(muzzle, target)) return null;
        rejection = "no-clear-trajectory";
        FlightModel model = weapon.Model;
        if (!TrajectoryAimer.TrySolve(muzzle, target, model, out TrajectorySolution solution))
        {
            if (record) BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, null, rejection);
            return null;
        }
        if (record) BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, solution.LaunchVelocity, "solved");
        int crossed = weapon.Hits(muzzle, solution.LaunchVelocity, hostiles, pierced);
        var hits = new List<EvaluateAttackOutcomes.Hit>();
        for (int i = 0; i < Math.Min(crossed, weapon.Pierce); i++)
        {
            float perHit = PerHit(ctx, weapon, pierced[i]);
            hits.Add(new(pierced[i].whoAmI, perHit, InducedDanger(ctx, weapon, pierced[i], muzzle, solution.LaunchVelocity, perHit)));
        }
        rejection = hits.Count == 0 ? "no-damageable-intercept" : "accepted";
        return hits.Count == 0 ? null : new(slot, target.whoAmI, weapon.UseTime, solution.ImpactTick, hits.ToArray());
    }

    /// <summary>Whether any weapon in hand has a real, intercepting use at this target from this muzzle.</summary>
    public bool ShotSolves(in ActionContext ctx, Vector2 muzzle, NPC target)
    {
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return false;
        Collect(ctx);
        for (int w = 0; w < weapons.Count; w++)
            if (ForecastFrom(ctx, weapons[w], w, target, muzzle, record: false, out _) != null) return true;
        return false;
    }

    /// <summary>
    /// The arsenal's outcome value for the best weapon–target pair from this muzzle against one body.
    /// Hunt uses this to steer: it does not pick the pair the hands will fire.
    /// </summary>
    public float BestShotValueFrom(in ActionContext ctx, Vector2 muzzle, NPC target)
    {
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return 0f;
        Collect(ctx);
        var attacks = new List<EvaluateAttackOutcomes.Attack>();
        for (int w = 0; w < weapons.Count; w++)
        {
            var attack = ForecastFrom(ctx, weapons[w], w, target, muzzle, record: false, out _);
            if (attack != null) attacks.Add(attack);
        }
        if (attacks.Count == 0) return 0f;
        var targets = AttackTargets(ctx);
        float best = 0f;
        foreach (var attack in attacks)
            best = MathF.Max(best, EvaluateAttackOutcomes.Evaluate(attack, attacks, targets, cooldown, HorizonTicks).Value);
        return best;
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
    /// The settled horizontal push, px and signed, that the weapon the arsenal last chose would give this target from this
    /// muzzle — the one push question position selection asks, so it can prefer a stand whose shot pushes the target away
    /// from the player and the orb. Zero with no weapon chosen. The damage is the weapon's base damage after armour rather
    /// than the player-modified hit, because this is asked without a context and only decides which of the game's two
    /// knockback branches applies.
    /// </summary>
    public float ExpectedPushFrom(Vector2 muzzle, NPC target, Player player)
    {
        CompanionWeapon? weapon = LastChosen;
        if (weapon == null || !target.active) return 0f;
        int direction = WeaponEffects.PriorDirection(weapon.PushesAwayFromOwner, target.Center.X - muzzle.X, target.Center.X, player.Center.X);
        return WeaponEffects.SettledPush(weapon.ItemType, target, weapon.Knockback,
            WeaponEffects.PriorDamage(weapon.BaseDamage, target) * WeaponEffects.DamageFactor(weapon.ItemType, target.type), direction);
    }

    /// <summary>The muzzle a shot would leave from at a candidate hover: the orb's centre there, the same point <see cref="Muzzle"/> reads off the live body.</summary>
    public static Vector2 MuzzleAt(Vector2 hoverCentre) => hoverCentre;

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
        var hash = new HashCode(); hash.Add(Muzzle(ctx.Npc)); hash.Add(TerrainChanges.Revision); hash.Add(WeaponEffects.Revision);
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
    /// it is subtracted rather than scaled: a defence of 6 costs a 6-damage hit its whole value and
    /// an 18-damage hit a sixth of it, so ignoring it silently favours the weapon that throws many
    /// small hits at exactly the enemies it is worst against. Crit is absent for the reason the
    /// weapon gives: the companion's spawn path carries none. The prior is then scaled by what this
    /// weapon's hits have actually landed on this enemy type, which is where a resistance applied in a
    /// mod's hook shows up; the scaled value keeps the floor of one, because a hit always takes at least
    /// that and the removal estimates divide by it.
    /// </summary>
    private static float PerHit(in ActionContext ctx, CompanionWeapon weapon, NPC target)
        => MathF.Max(1f, WeaponEffects.PriorDamage(weapon.DamagePerHit(ctx), target) * WeaponEffects.DamageFactor(weapon.ItemType, target.type));

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
        Refresh(ctx);
        NPC? target = ctx.Senses.Threats.MostUrgent?.Npc;
        if (target == null || !target.CanBeChasedBy()) return interventionTicks = float.PositiveInfinity;
        int generation = HostileAttackSources.Generation(target);
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
        CompanionWeapon? weapon = Choose(ctx, target);
        if (weapon == null || !TrajectoryAimer.TrySolve(Muzzle(ctx.Npc), target, weapon.Model, out TrajectorySolution solution))
            return interventionTicks = float.PositiveInfinity;
        // Protection needs the threat removed, not merely the first projectile arriving.
        // This remains an optimistic estimate: misses and target motion can only delay it.
        int hits = (int)MathF.Ceiling(target.life / PerHit(ctx, weapon, target));
        return interventionTicks = Math.Max(0, cooldown) + solution.ImpactTick
            + Math.Max(0, hits - 1) * Math.Max(1, weapon.UseTime);
    }

    /// <summary>
    /// Optimistic ticks to remove a target if every attack lands: for each weapon, the hits its life needs
    /// after defence spaced by the weapon's use time, plus one flight from the current distance, and the
    /// faster weapon's answer. Unlike <see cref="EstimateInterventionTicks"/> it asks nothing of the current
    /// arc or cooldown, so it answers whether a fight against this target could ever be short rather than
    /// whether a shot is open now. Infinity for a target no weapon can damage, and with no weapon in hand.
    /// </summary>
    public float EstimateRemovalTicks(in ActionContext ctx, NPC target)
    {
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return float.PositiveInfinity;
        Vector2 muzzle = Muzzle(ctx.Npc);
        float best = float.PositiveInfinity;
        foreach (CompanionWeapon weapon in weapons)
        {
            int hits = (int)MathF.Ceiling(target.life / PerHit(ctx, weapon, target));
            float flight = Vector2.Distance(muzzle, target.Center) / MathF.Max(1f, weapon.Model.Speed);
            best = MathF.Min(best, Math.Max(0, hits - 1) * Math.Max(1, weapon.UseTime) + flight);
        }
        return best;
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
    /// Whether any weapon in hand has a solvable use at the target from where the companion
    /// is now. An unchanged muzzle, target and terrain share a bounded cache; motion expires
    /// it immediately so a brief firing window is not hidden by a stale negative answer.
    /// </summary>
    public bool CanEngage(in ActionContext ctx, NPC target)
    {
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return false;
        int now = ctx.Senses.Tick;
        int slot = target.whoAmI;
        ShotState state = ShotState.Capture(ctx, target);
        if (now - engageCheckedAt[slot] < EngageCacheTicks && engageCheckedAt[slot] != 0 && engageState[slot] == state)
            return engageResult[slot];
        Vector2 muzzle = Muzzle(ctx.Npc);
        bool can = ShotSolves(ctx, muzzle, target);
        engageCheckedAt[slot] = now;
        engageState[slot] = state;
        engageResult[slot] = can;
        return can;
    }

    /// <summary>Face the target and use a weapon if a use exists and the cooldown allows. Returns true on a use.</summary>
    public bool TryFire(in ActionContext ctx, NPC? target)
    {
        Refresh(ctx);
        if (target == null || !target.active || target.life <= 0 || !target.CanBeChasedBy())
        {
            LastShotSolved = false;
            LastFireOutcome = "no-target";
            return false;
        }
        CompanionWeapon? weapon = Choose(ctx, target);
        if (weapon == null)
        {
            LastShotSolved = false;
            LastFireOutcome = "no-weapon";
            return false;
        }
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

        bool solved = TrajectoryAimer.TrySolve(muzzle, target, weapon.Model, out TrajectorySolution solution);
        if (!solved)
        {
            // The choice is a dozen ticks old and the world has moved: the arc that scored is gone.
            // The other weapons are tried before giving up, because standing still holding an unusable
            // weapon is how the companion died with thirteen hostiles on it and a bow in its hand.
            foreach (CompanionWeapon other in weapons)
            {
                if (ReferenceEquals(other, weapon)) continue;
                if (TrajectoryAimer.TrySolve(muzzle, target, other.Model, out TrajectorySolution fallback))
                {
                    weapon = other;
                    chosen = other;
                    LastChosen = other;
                    solution = fallback;
                    solved = true;
                    break;
                }
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

        // A swing has no launch to be imprecise about: the noise is a shot's, and rotating a sector's
        // centre by it only moved which bodies at the sector's edge were struck.
        float noise = weapon.IsSwing ? 0f : (Main.rand.NextFloat() * 2f - 1f) * weapon.AimNoise;
        Vector2 launch = solution.LaunchVelocity.RotatedBy(noise);
        if (!TrajectoryAimer.TryTrace(muzzle, launch, target, weapon.Model, out TrajectorySolution finalShot))
        {
            LastShotSolved = false;
            LastFireOutcome = "no-arc";
            return false;
        }

        FireResult result = weapon.Fire(ctx, muzzle, launch);
        if (result.IsShot && result.ProjectileSlot >= Main.maxProjectiles)
        {
            LastFireOutcome = "projectile-capacity";
            return false;
        }
        // A swing files the same shot record with no projectile slot, so the log reads a swing beside a shot
        // in one vocabulary; a landed swing is counted from the strike itself, since no projectile hit will follow.
        GodsEyeEvents.RecordShot(ctx.Npc, target, result.ProjectileSlot, muzzle, launch, finalShot.ExpectedImpact, weapon.Name, finalShot.ImpactTick, LastAttackValue, LastExpectedKills, LastPreventedHarm);
        if (result.IsShot)
        {
            TrackLandedHits.Register(result.ProjectileSlot, target, weapon.ItemType);
            ProjectileArcs.Register(result.ProjectileSlot, weapon.ProjectileType, launch);
        }
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

    /// <summary>Where a use leaves from: the orb's centre, because an orb has no facing and no hand.</summary>
    public static Vector2 Muzzle(NPC npc) => npc.Center;
}
