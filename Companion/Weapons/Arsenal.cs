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
using AICompanion.Companion.Brain.Infrastructure.Selection;
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
///
/// Every forecast's hit damage is the arithmetic's prior times what <see cref="AttackLearning"/> has learned that
/// weapon's attacks actually achieve in that context, so weapon, target, stand and aim are ranked by one learned value;
/// every use opens an outcome window in <see cref="ShotOutcomes"/> that teaches the learner when it closes.
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

    /// <summary>The gear's signature as the arsenal last enumerated it, so a consumer holding a choice made for these weapons can tell when they changed.</summary>
    public int GearSignature { get { Refresh(Main.LocalPlayer); return gearSignature; } }

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

    /// <summary>Whether the last forecast was allowed to explore, or was held to the posterior mean by the danger gate.</summary>
    public bool LastForecastExplored { get; private set; } = true;

    /// <summary>The aim offset from the intercept the last shot actually left at, radians, noise included.</summary>
    public float LastAimOffset { get; private set; }

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
        idealCheckedAt = int.MinValue;
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
        if (held != null && now - heldAt < TargetHoldTicks && stamp == heldStamp && !MovedPastHold(ctx) && !newlyUrgent && CanEngage(ctx, held))
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
        RememberHoldPositions(ctx);
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
    /// the target's defence and the learned correction, the weapon's use time, a flight time from the current
    /// muzzle distance — which is optimistic about the arc (the positioner proves the real one at the stand) and
    /// pessimistic about the wait, because the hands' own shots during the walk are not counted. The retained
    /// from-here attacks are the continuation after that first shot, so a reposition that delays the arrows a
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
            foreach (var assumed in AssumedAttacks(ctx, target))
            {
                firsts.Add(assumed);
                alternatives.Add(assumed);
            }
        int fireAt = Math.Max(cooldown, Math.Max(0, accessTicks));
        float best = 0f;
        foreach (var first in firsts)
            best = MathF.Max(best, EvaluateAttackOutcomes.Evaluate(first, alternatives, targets, fireAt, HorizonTicks).Value);
        return best;
    }

    /// <summary>
    /// One assumed attack per weapon at a target with no geometry in the way: the learned per-hit damage, the weapon's use
    /// time, a flight from the current distance at the weapon's launch speed, and no push charge — its muzzle is a stand
    /// nobody has chosen yet, so which side a push would carry the target to is unknown, and a stand's own forecast prices it.
    /// </summary>
    private List<EvaluateAttackOutcomes.Attack> AssumedAttacks(in ActionContext ctx, NPC target)
    {
        var assumed = new List<EvaluateAttackOutcomes.Attack>();
        Vector2 muzzle = Muzzle(ctx.Npc);
        bool explore = Explore(ctx);
        for (int w = 0; w < weapons.Count; w++)
        {
            CompanionWeapon weapon = weapons[w];
            float distance = Vector2.Distance(muzzle, target.Center);
            int flight = Math.Max(1, (int)(distance / MathF.Max(1f, weapon.Model.Speed)));
            var inputs = new ContextInputs(distance, weapon.Reach, (target.velocity - ctx.Npc.velocity).Length(), 0, ctx.Npc.velocity.Length());
            assumed.Add(new EvaluateAttackOutcomes.Attack(w, target.whoAmI, weapon.UseTime, flight,
                new[] { LearnedHit(ctx, weapon, target, PerHit(ctx, weapon, target), 0f, inputs, 0f, explore) }));
        }
        return assumed;
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
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy() || weapons.Count == 0) return 0f;
        int generation = HostileAttackSources.Generation(target);
        if (idealCheckedAt == ctx.Senses.Tick && idealTarget == target.whoAmI && idealGeneration == generation && idealRevision == AttackLearning.Revision)
            return idealValue;
        var targets = AttackTargets(ctx);
        var assumed = AssumedAttacks(ctx, target);
        float best = 0f;
        foreach (var attack in assumed)
            best = MathF.Max(best, EvaluateAttackOutcomes.Evaluate(attack, assumed, targets, cooldown, HorizonTicks).Value);
        idealCheckedAt = ctx.Senses.Tick; idealTarget = target.whoAmI; idealGeneration = generation; idealRevision = AttackLearning.Revision;
        return idealValue = best;
    }

    private EvaluateAttackOutcomes.Attack? ForecastAttack(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target, out string rejection)
        => Forecast(ctx, weapon, slot, target, Muzzle(ctx.Npc), record: true, 0, out rejection, out _);

    /// <summary>The raw inputs of a learning context other than the aim and the debuff, which differ per candidate and per body.</summary>
    private readonly record struct ContextInputs(float Distance, float Reach, float RelativeSpeed, int LaneHostiles, float OrbSpeed)
    {
        public float[] With(float aimOffset, bool debuffedByOther)
            => AttackLearning.Context(Distance, Reach, aimOffset, RelativeSpeed, LaneHostiles, OrbSpeed, OrbPace.MaxSpeed, debuffedByOther);
    }

    /// <summary>What a forecast predicted before any learned correction, and the context it was made in, so a use can be taught against it.</summary>
    private readonly record struct ForecastPrior(float Damage, int Struck, float Charge, ContextInputs Inputs, float AimOffset, bool AimedDebuffed);

    /// <summary>
    /// The same forecast the hands fire from, evaluated at an arbitrary muzzle — and against the target as it will be
    /// <paramref name="targetTickOffset"/> ticks from now — so hunt and position selection can ask whether a pose would
    /// actually shoot rather than whether a straight ray would.
    ///
    /// The geometry is the prior: the solve, the bodies the flight crosses, each hit's damage after armour and the push
    /// charge. The learner then scales each hit's damage by what this weapon has achieved in this context, and picks the
    /// aim. Aim candidates share the intercept's geometry on purpose — the prior for an offset shot is the intercept's
    /// forecast, and only the learner knows whether aiming off it costs anything — so the choice among them is the
    /// candidate whose learned factor on the aimed target is largest, taken before any value is computed. Because the aim
    /// input is the offset's size, a linear model's answer is either the intercept or the widest candidate; a swing has no
    /// launch to aim and a weapon with no evidence has nothing to say, so both keep the intercept.
    /// </summary>
    private EvaluateAttackOutcomes.Attack? Forecast(in ActionContext ctx, CompanionWeapon weapon, int slot, NPC target, Vector2 muzzle,
        bool record, int targetTickOffset, out string rejection, out ForecastPrior prior)
    {
        prior = default;
        rejection = "outside-reach";
        if (!weapon.InReach(muzzle, target)) return null;
        rejection = "no-clear-trajectory";
        FlightModel model = weapon.Model;
        if (!TrajectoryAimer.TrySolve(muzzle, target, model, Math.Max(0, targetTickOffset), out TrajectorySolution solution))
        {
            if (record) BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, null, rejection);
            return null;
        }
        if (record) BrainInspectorSamples.RecordAim(muzzle, target.Center, weapon.Name, solution.LaunchVelocity, "solved");
        int crossed = Math.Min(weapon.Hits(muzzle, solution.LaunchVelocity, hostiles, pierced), weapon.Pierce);
        bool explore = Explore(ctx);
        LastForecastExplored = explore;
        var inputs = new ContextInputs(Vector2.Distance(muzzle, target.Center), weapon.Reach,
            (target.velocity - ctx.Npc.velocity).Length(), Math.Max(0, crossed - 1), ctx.Npc.velocity.Length());
        bool aimedDebuffed = ShotOutcomes.DebuffedByOther(target, weapon.ItemType);
        float aim = 0f;
        if (!weapon.IsSwing && AttackLearning.Evidence(weapon.ItemType) > 0)
        {
            int tick = ctx.Senses.Tick;
            float bestFactor = AttackLearning.Factor(weapon.ItemType, target.type, inputs.With(0f, aimedDebuffed), explore, tick);
            for (int step = 1; step <= Weights.WeaponAimOffsetSteps; step++)
            {
                float offset = step * Weights.WeaponAimOffsetRadians;
                float factor = AttackLearning.Factor(weapon.ItemType, target.type, inputs.With(offset, aimedDebuffed), explore, tick);
                if (factor > bestFactor) { bestFactor = factor; aim = offset; }
            }
        }
        var hits = new List<EvaluateAttackOutcomes.Hit>();
        float priorDamage = 0f, charge = 0f;
        for (int i = 0; i < crossed; i++)
        {
            float perHit = PerHit(ctx, weapon, pierced[i]);
            float danger = InducedDanger(ctx, weapon, pierced[i], muzzle, solution.LaunchVelocity, perHit);
            priorDamage += perHit;
            charge += danger;
            hits.Add(LearnedHit(ctx, weapon, pierced[i], perHit, danger, inputs, aim, explore));
        }
        rejection = hits.Count == 0 ? "no-damageable-intercept" : "accepted";
        if (hits.Count == 0) return null;
        prior = new ForecastPrior(priorDamage, hits.Count, charge, inputs, aim, aimedDebuffed);
        return new(slot, target.whoAmI, weapon.UseTime, solution.ImpactTick, hits.ToArray(), aim);
    }

    /// <summary>
    /// One forecast hit with the learned correction applied: its damage in the body's current state, its damage against a
    /// body the other weapon has debuffed, and the learned chance and length of the debuff this weapon's hit leaves on
    /// that enemy type. With no evidence both damages are the prior exactly.
    /// </summary>
    private static EvaluateAttackOutcomes.Hit LearnedHit(in ActionContext ctx, CompanionWeapon weapon, NPC body, float perHit, float danger,
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

    /// <summary>Whether any weapon in hand has a real, intercepting use at this target from this muzzle.</summary>
    public bool ShotSolves(in ActionContext ctx, Vector2 muzzle, NPC target)
    {
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return false;
        Collect(ctx);
        for (int w = 0; w < weapons.Count; w++)
            if (Forecast(ctx, weapons[w], w, target, muzzle, record: false, 0, out _, out _) != null) return true;
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
        Refresh(ctx);
        if (!target.active || target.life <= 0 || !target.CanBeChasedBy()) return 0f;
        Collect(ctx);
        var attacks = new List<EvaluateAttackOutcomes.Attack>();
        for (int w = 0; w < weapons.Count; w++)
        {
            var attack = Forecast(ctx, weapons[w], w, target, muzzle, record: false, targetTickOffset, out _, out _);
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

    /// <summary>
    /// The facts whose change should change the held choice at once: which hostiles are listed, by slot and spawn generation,
    /// the terrain's revision, and what the weapon-effects table and the attack learner believe. Hostiles are combined by
    /// addition so the list's order, which the threat sense may rebuild every tick, is not a fact. Position is not in it,
    /// because a hash of every centre and velocity changed on every tick anything moved and so the hold was renewed never;
    /// movement large enough to matter is <see cref="MovedPastHold"/>'s. Life and urgency are not in it either: the
    /// companion's own hits advance the learner's and the table's revisions when they land, a more urgent threat already
    /// breaks the hold on its own test, and a wound from the player waits at most the hold's length to be re-ranked.
    /// </summary>
    private static int CombatStamp(in ActionContext ctx)
    {
        int hostiles = 0;
        foreach (var t in ctx.Senses.Threats.Threats)
            hostiles += HashCode.Combine(t.Npc.whoAmI, HostileAttackSources.Generation(t.Npc));
        return HashCode.Combine(hostiles, ctx.Senses.Threats.Threats.Count, TerrainChanges.Revision, WeaponEffects.Revision, AttackLearning.Revision);
    }

    /// <summary>
    /// How far any listed hostile or the muzzle may move from where the ranking saw it before the hold is re-ranked, px:
    /// three tiles. Across a hold a walker covers well under that and a flier can cover more, and past it the ranking's
    /// geometry — which bodies a flight crosses, which side a push lands on — can have changed; below it the held target is
    /// still revalidated by <see cref="CanEngage"/> every tick, so a shot that stopped solving is never kept.
    /// </summary>
    private const float HoldBreakDistance = 48f;

    private bool MovedPastHold(in ActionContext ctx)
    {
        float limit = HoldBreakDistance * HoldBreakDistance;
        if (Vector2.DistanceSquared(Muzzle(ctx.Npc), heldMuzzle) > limit) return true;
        foreach (var t in ctx.Senses.Threats.Threats)
            if (heldCentres.TryGetValue(t.Npc.whoAmI, out Vector2 seen) && Vector2.DistanceSquared(seen, t.Npc.Center) > limit)
                return true;
        return false;
    }

    private void RememberHoldPositions(in ActionContext ctx)
    {
        heldMuzzle = Muzzle(ctx.Npc);
        heldCentres.Clear();
        foreach (var t in ctx.Senses.Threats.Threats)
            heldCentres[t.Npc.whoAmI] = t.Npc.Center;
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
    private Vector2 heldMuzzle;
    private readonly Dictionary<int, Vector2> heldCentres = new();
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

        // The forecast the use is taught against: the prior it predicted and the aim the learner chose. Taken here rather
        // than carried from the choice, because the choice may be a dozen ticks old and the use happens from this muzzle.
        Collect(ctx);
        var forecast = Forecast(ctx, weapon, weapons.IndexOf(weapon), target, muzzle, record: false, 0, out _, out ForecastPrior prior);

        // A swing has no launch to be imprecise about or to aim off: the noise and the offset are a shot's, and rotating a
        // sector's centre only moved which bodies at the sector's edge were struck.
        float aim = weapon.IsSwing || forecast == null ? 0f : prior.AimOffset * (Main.rand.NextBool() ? 1f : -1f);
        float noise = weapon.IsSwing ? 0f : (Main.rand.NextFloat() * 2f - 1f) * weapon.AimNoise;
        Vector2 launch = solution.LaunchVelocity.RotatedBy(aim + noise);
        bool traced = TrajectoryAimer.TryTrace(muzzle, launch, target, weapon.Model, out TrajectorySolution finalShot);
        if (!traced && aim != 0f && TrajectoryAimer.TryClear(muzzle, launch, weapon.Model, solution.ImpactTick))
        {
            // An offset aim is off the intercept on purpose, so reaching the target's box is what the learner is finding
            // out rather than a condition of firing; it is held only to flying clear of terrain for the intercept's flight.
            traced = true;
            finalShot = solution with { LaunchVelocity = launch };
        }
        if (!traced && aim != 0f)
        {
            // An offset that meets a wall falls back to the intercept, where the ordinary check decides.
            aim = 0f;
            launch = solution.LaunchVelocity.RotatedBy(noise);
            traced = TrajectoryAimer.TryTrace(muzzle, launch, target, weapon.Model, out finalShot);
        }
        if (!traced)
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
        LastAimOffset = weapon.IsSwing ? 0f : MathHelper.WrapAngle(launch.ToRotation() - solution.LaunchVelocity.ToRotation());
        if (forecast != null)
            OpenOutcome(weapon, target, prior, forecast.ImpactTicks, LastAimOffset, result);
        cooldown = weapon.UseTime;
        ctx.Companion.StartAnimation(weapon.ItemType, Math.Max(10, weapon.BaseUseTime));
        ctx.Companion.SetAimRotation(launch);
        LastFireOutcome = "fired";
        return true;
    }

    /// <summary>
    /// Open the use's outcome window against what its forecast predicted and the context it was fired in — the aim as it
    /// actually left, noise included, because that is the offset whose outcome is about to be observed. A shot's window
    /// holds its projectile until it and its descendants die; a swing's strikes are already known, so its window closes now.
    /// </summary>
    private static void OpenOutcome(CompanionWeapon weapon, NPC target, in ForecastPrior prior, int impactTicks, float firedAim, FireResult result)
    {
        float[] context = prior.Inputs.With(firedAim, prior.AimedDebuffed);
        int window = ShotOutcomes.Open(weapon.ItemType, target, context, prior.Damage, prior.Struck, prior.Charge, weapon.UseTime, impactTicks, Main.GameUpdateCount);
        if (result.IsShot)
        {
            ShotOutcomes.AddSlot(window, result.ProjectileSlot);
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

    /// <summary>Where a use leaves from: the orb's centre, because an orb has no facing and no hand.</summary>
    public static Vector2 Muzzle(NPC npc) => npc.Center;
}
