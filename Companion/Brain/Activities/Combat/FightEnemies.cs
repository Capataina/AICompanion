#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.Combat;

/// <summary>
/// The one combat stance: guarding's threat binding and urgency lift with hunting's admissibility,
/// firing-position access, progress and stall deferral, offered as the larger of the two old values
/// under their existing terms, scaled by <see cref="Weights.CombatValueScale"/>. Which side won is
/// remembered for the tick: it picks the request (a leashed guard stand or a line-of-fire approach),
/// the attempt's evidence and whose terms the time and separation charges read. Ties keep the guard
/// side, as the old family kept the lower action index, except at zero where the more informative
/// refusal wins the record instead. The hands fire only while this is the running
/// activity; every other job leaves them quiet. See <c>Brain.Engage</c>.
/// </summary>
public sealed class FightEnemies : CompanionAction, ICandidateFunnelSource
{
    public override string Name => "combat";
    public override PurposeFamily Family => PurposeFamily.Combat;
    public override bool IsExcursion => guardWon ? false : !localHunt;
    public override bool ServesPlayerDirectly => guardWon;

    /// <summary>Which side's offer won this preparation: guarding's threat or hunting's target.</summary>
    public bool GuardWon => guardWon;
    private bool guardWon;

    public override object? ActivityIdentity => guardWon ? guardPrepared?.Enemy : Target?.Npc;
    public override Vector2? ActivityTarget => guardWon ? guardPrepared?.Bottom : huntPrepared?.Bottom;
    public override PositionRequest? PreparedPositionRequest => guardWon
        ? (guardPrepared is { } guard ? new PositionRequest(RequestKind.Guard, guard.Anchor, guard.Enemy) : null)
        : (huntPrepared is { } hunt ? new PositionRequest(RequestKind.LineOfFire, hunt.Centre, hunt.Enemy) : null);

    /// <summary>The companion's shared firing-opportunity query; both sides read the same answers through it.</summary>
    private readonly ResolveFiringOpportunity firingAccess;

    /// <summary>Combat that owns its own query, for callers that construct one activity alone.</summary>
    public FightEnemies() : this(new ResolveFiringOpportunity()) { }

    public FightEnemies(ResolveFiringOpportunity firingAccess) => this.firingAccess = firingAccess;

    // The guard side: a bound threat, its pressure, and what an intervention against it is worth.
    // Carried over from ProtectPlayer with its terms unchanged; only the offer it feeds is new.

    private readonly record struct GuardCandidate(Terraria.NPC Enemy, int Generation, Vector2 Bottom,
        Vector2 Anchor, float Pressure);
    private GuardCandidate? guardPrepared;

    private Terraria.NPC? protectedThreat;
    private int guardGeneration;
    private float committedPressure;
    private int safeSince = -1;
    /// <summary>The guard side's prepared threat, or null when the side prepared nothing: the activity's
    /// identity is the winning side's, so a reader that wants the guard side's own target reads this.</summary>
    public Terraria.NPC? GuardTarget => guardPrepared?.Enemy;
    public int ProtectedThreatId => protectedThreat?.whoAmI ?? -1;
    public float RetainedPressure => committedPressure;
    public string CommitmentReason { get; private set; } = "no-commitment";

    /// <summary>The arsenal's optimistic time to remove the prepared threat, and the share of protection
    /// value that leaves once reaching a firing position is added to it: one while access plus removal is
    /// within <see cref="Weights.GuardUsefulRemovalTicks"/>, less in proportion beyond it, zero when no
    /// reachable position can shoot the threat. Infinity and a share of one for a threat no weapon can damage.</summary>
    public float RemovalTicks { get; private set; } = float.PositiveInfinity;
    public float InterventionUsefulness { get; private set; } = 1f;

    /// <summary>What the shared firing-opportunity query answered for the prepared threat, or null when it was
    /// not asked because no weapon can damage the threat.</summary>
    public FiringAccess? Access { get; private set; }

    /// <summary>The access time the share counted: zero from here, the travel estimate after moving, infinity for
    /// a proven absence, and NaN when nothing was counted — an unsettled region or an unasked query.</summary>
    public float AccessTicks { get; private set; } = float.NaN;

    /// <summary>Guarding's side of the last preparation, under its existing terms and unscaled:
    /// protection pressure times the intervention share times the urgency ladder.</summary>
    public float GuardValue => guardValue;
    private float guardValue;

    private ThreatRecord? ContinueProtection(in ActionContext ctx)
    {
        if (protectedThreat == null) return null;
        if (ctx.Senses.Player.IsDead || !protectedThreat.active || protectedThreat.life <= 0
            || guardGeneration != HostileAttackSources.Generation(protectedThreat))
        {
            ClearGuard("threat-or-player-unavailable", ctx.Senses.Player.IsDead);
            return null;
        }
        ThreatRecord? record = ctx.Senses.Threats.Threats.Find(t => ReferenceEquals(t.Npc, protectedThreat));
        bool relevant = record != null && record.CanReachPlayer &&
            (record.Urgency >= Weights.GuardReleasePressure || record.Shoots && record.HasSightOnPlayer);
        if (relevant)
        {
            safeSince = -1;
            CommitmentReason = "threat-still-relevant";
        }
        else
        {
            if (safeSince < 0) safeSince = ctx.Senses.Tick;
            if (ctx.Senses.Tick - safeSince >= Weights.GuardClearTicks)
            {
                ClearGuard("sustained-clearance");
                return null;
            }
            CommitmentReason = "confirming-clearance";
        }
        return record;
    }

    private void ClearGuard(string reason, bool playerDead = false)
    {
        // One record of how this attempt's commitment ended, written where it ends; a later Enter
        // for a different threat rewrites CommitmentReason but never this.
        if (protectedThreat != null) guardAttemptClear = (reason, playerDead);
        protectedThreat = null;
        committedPressure = 0f;
        safeSince = -1;
        CommitmentReason = reason;
    }

    private (string Reason, bool PlayerDead)? guardAttemptClear;

    /// <summary>
    /// Guarding's terms, unchanged: protection pressure for the most urgent threat, worth the harm an
    /// intervention can actually remove over the time it takes to reach a firing position and finish.
    /// Returns the side's value and its classification; a threat no weapon can damage keeps its urgency
    /// with no removal estimated, and a proven absence of any firing position closes the side.
    /// </summary>
    private float EvaluateGuard(in ActionContext ctx, out OfferEligibility eligibility, out string reason)
    {
        var t = ctx.Senses.Threats;
        var target = t.MostUrgent?.Npc;
        if (target == null || !target.active || target.life <= 0) target = protectedThreat;
        guardPrepared = null;
        guardValue = 0;
        RemovalTicks = float.PositiveInfinity;
        Access = null;
        AccessTicks = float.NaN;
        InterventionUsefulness = 1f;
        if (ctx.Senses.Player.IsDead || target == null || !target.active || target.life <= 0)
        {
            eligibility = OfferEligibility.NoOpportunity;
            reason = ctx.Senses.Player.IsDead ? "player-dead" : "no-threat";
            return 0f;
        }
        float currentPressure = ReferenceEquals(target, t.MostUrgent?.Npc) ? t.MostUrgent.Urgency : 0f;
        float retainedPressure = ReferenceEquals(target, protectedThreat) ? committedPressure : 0f;
        float danger = Math.Max(t.ProtectionUrgency, Math.Max(currentPressure, retainedPressure));
        if (danger <= 0)
        {
            eligibility = OfferEligibility.NoOpportunity;
            reason = "no-protection-pressure";
            return 0f;
        }
        // Whether a useful intervention position exists is decided by method admission at
        // nomination, so the prepared offer is honestly unresolved until that query answers.
        eligibility = OfferEligibility.Unresolved;
        reason = "intervention-destination-requires-admission";
        Vector2 toThreat = target.Bottom - ctx.Senses.Player.Bottom;
        float leash = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        Vector2 anchor = toThreat.LengthSquared() <= leash * leash
            ? target.Bottom
            : ctx.Senses.Player.Bottom + Vector2.Normalize(toThreat) * leash;
        guardPrepared = new(target, HostileAttackSources.Generation(target), target.Bottom, anchor, danger);
        // Scaled past the ordinary 0..1 band because guarding has to be able to interrupt, and an
        // action that tops out at 1 cannot interrupt anything: the running action carries
        // Weights.Commitment, so a following body already at 1 sits at 1.15 and no danger reading
        // could ever displace it. That is not a tuning miss, it is arithmetic, and it is what left
        // the companion holding a torch with the player's danger reading full and slimes on him
        // through the 2026-09-09 underground session. Weights.GuardUrgency owns the ladder and
        // its interruption scale. Shared safety owns personal escape independently.
        // Protection is the ability to intervene, not the distance between the allies. Charging
        // closeness again suppressed protection precisely when an enemy reached the player.
        // Protection is worth the harm an intervention can actually remove. A threat the weapons would
        // need longer than GuardUsefulRemovalTicks to remove is guarded over in proportion, so a boss's
        // life bar stops being the most protection-worthy thing on screen while the small enemies on the
        // player keep their full value — the owner's "no version where it stands between you and the Eye
        // of Cthulhu and that helps", reached from the fight's length rather than from a boss flag. A
        // threat no weapon can damage has no removal to estimate and keeps its urgency; its avoidance
        // belongs to shared safety, so the firing query is not asked for it at all.
        //
        // An intervention's time includes getting to where it can be made (Proposal 1's P03: movement,
        // cooldown and projectile time all count), so the share is taken over access plus removal. Access
        // is the same answer hunting reads for this enemy: zero from here, the walk to the nearest
        // reachable sighted tile after moving. An unsettled region is not evidence of absence, so it counts
        // no access and the share is removal's alone, the same three-valued rule hunting admissibility
        // follows; its straight-line guess is hunting's to price and never lowers protection. A proven
        // absence of any firing position is access that never arrives, so the share is zero: guarding
        // yields to shared safety and company when the companion provably cannot shoot the threat, and
        // getting away from such a threat is shared safety's job, not guarding's.
        RemovalTicks = ctx.Companion.Arsenal.EstimateRemovalTicks(ctx, target);
        if (float.IsFinite(RemovalTicks))
        {
            var (verdict, ticks) = firingAccess.Resolve(ctx, target);
            Access = verdict;
            AccessTicks = verdict switch
            {
                FiringAccess.FromHere => 0f,
                FiringAccess.AfterMoving => ticks,
                FiringAccess.None => float.PositiveInfinity,
                _ => float.NaN,
            };
            if (verdict == FiringAccess.None)
            {
                InterventionUsefulness = 0f;
                // The one place guarding closes itself, and it rests entirely on what None means in
                // ResolveFiringOpportunity: a completed sweep of every sampled stand, never a solve cap that ran
                // out. If that ever returns None from an exhausted bound again, this line writes off protection
                // the companion could have given.
                eligibility = OfferEligibility.KnownUnusable;
                reason = "no-reachable-firing-position";
                return 0f;
            }
            float total = RemovalTicks + (float.IsFinite(AccessTicks) ? AccessTicks : 0f);
            InterventionUsefulness = total > Weights.GuardUsefulRemovalTicks ? Weights.GuardUsefulRemovalTicks / total : 1f;
        }
        return guardValue = danger * InterventionUsefulness * Weights.GuardUrgency;
    }

    // The hunt side: which hostile is worth approaching, how the approach is going, and which
    // stalled engagements are deferred. Carried over from PursueAttackOpportunity unchanged.

    public ThreatRecord? Target { get; private set; }
    public int NoProgressTicks { get; private set; }
    public string LastRejection { get; private set; } = "none";
    private Vector2 engagementOrigin;
    private int observedTarget = -1, observedGeneration, observedLife;
    private readonly Dictionary<(int slot, int generation), (int until, Vector2 target, Vector2 body, int terrain)> deferred = new();
    /// <summary>Every enemy this stalled stretch has aimed at, so the deferral covers the set that produced the stall.</summary>
    private readonly Dictionary<(int slot, int generation), Vector2> stalled = new();

    private void ObserveHuntOutcome(in ActionContext ctx)
    {
        if (Target == null) { NoProgressTicks = 0; stalled.Clear(); return; }
        NPC enemy = Target.Npc;
        int generation = HostileAttackSources.Generation(enemy);
        // The hands fire only while combat runs, so a pursuit shot is a shot at this enemy on a
        // tick combat executed — but the arm can still be busy with a tool while combat holds the
        // body, and cooldown describes waiting rather than a new attack. Damage is compared only
        // within one generation; changing targets cannot reset a stalled hunt.
        bool sameTarget = observedTarget == enemy.whoAmI && observedGeneration == generation;
        bool pursuitShot = ReferenceEquals(ctx.Companion.Brain.EngageTarget, enemy)
            && ctx.Companion.Arsenal.LastFireOutcome == "fired";
        bool progress = (sameTarget && enemy.life < observedLife)
            || pursuitShot
            || Vector2.DistanceSquared(engagementOrigin, ctx.Npc.Bottom) >= Weights.ObjectiveProgressPixels * Weights.ObjectiveProgressPixels;
        // Every enemy aimed at during the stalled stretch, so the deferral covers the set rather
        // than whichever one happened to be selected on the tick the window closed. Deferring only
        // that one hands each member of an alternating pair a fresh window, which is the same
        // defect one step further out: two targets would buy twice the window and nothing else.
        stalled[(enemy.whoAmI, generation)] = enemy.Center;
        // Every admissible candidate the last preparation examined joins the stall as well. Target
        // choice values several candidates and keeps the best, so a stationary hunt facing enemies it
        // cannot progress against no longer alternates between them: it keeps one, and deferring only
        // that one hands the next a fresh window of its own — the same two-targets-buy-twice-the-window
        // defect the alternating guard exists for, reached through a steadier choice.
        foreach (var (slot, candidateGeneration, centre) in examinedAdmissible)
            stalled[(slot, candidateGeneration)] = centre;
        observedTarget = enemy.whoAmI; observedGeneration = generation; observedLife = enemy.life;
        pursued = (enemy, generation);
        attacked |= pursuitShot;
        if (progress)
        {
            NoProgressTicks = 0; engagementOrigin = ctx.Npc.Bottom;
            stalled.Clear();
        }
        else if (++NoProgressTicks >= Weights.ObjectiveProgressWindowTicks)
        {
            foreach (var (key, centre) in stalled)
                deferred[key] = (ctx.Senses.Tick + Weights.HuntRetryTicks, centre, ctx.Npc.Bottom,
                    Infrastructure.Movement.TerrainChanges.Revision);
            LastRejection = "no-movement-or-attack-progress";
            failed = true;
            NoProgressTicks = 0;
            stalled.Clear();
        }
    }

    // Attempt-local evidence, written only while the hunt side executes and cleared when an attempt opens.
    private (NPC Enemy, int Generation)? pursued;
    private bool attacked, failed;

    private readonly record struct HuntCandidate(NPC Enemy, int Generation, Vector2 Bottom, Vector2 Centre, float Value, float TripTicks);
    private HuntCandidate? huntPrepared;
    private bool localHunt;
    /// <summary>Hunting's side of the last preparation, under its existing terms and unscaled: nearness,
    /// worth, leash, own skin and shot possibility in one product.</summary>
    public float HuntValue => huntValue;
    private float huntValue;

    /// <summary>
    /// Hunting's terms, unchanged: a target the weapons can engage, scored by having one and by the
    /// player being safe enough to leave, with the forecast as the trip to a firing spot. Distance is
    /// charged in the forecast and only there; a second enemy appearing ends nothing by itself.
    /// </summary>
    private float EvaluateHunt(in ActionContext ctx, out OfferEligibility eligibility, out string reason)
    {
        if (!PlayerIntegration.CompanionPreferences.Current.Combat)
        {
            Target = null;
            Funnel.Begin();
            eligibility = OfferEligibility.PolicyForbidden;
            reason = "combat-disabled";
            huntPrepared = null;
            return huntValue = 0f;
        }
        Rectangle screen = ScreenWithMargin();
        Target = PickTarget(ctx, screen);
        if (Target == null)
        {
            // A refusal with a named cause is a known-unusable method; no candidate at all is absence.
            // An unfinished firing search is neither: it is not a plan, and it is not a proven absence.
            huntPrepared = null;
            if (LastRejection == "firing-position-undecided")
            {
                eligibility = OfferEligibility.Unresolved;
                reason = LastRejection;
            }
            else
            {
                eligibility = LastRejection is "no-reachable-firing-position" or "engagement-deferred-no-progress"
                    ? OfferEligibility.KnownUnusable : OfferEligibility.NoOpportunity;
                reason = LastRejection;
            }
            return huntValue = 0f;
        }
        if (ctx.Senses.Player.IsDead)
        {
            eligibility = OfferEligibility.NoOpportunity;
            reason = "player-dead";
            huntPrepared = null;
            return huntValue = 0f;
        }
        float near = Target.Npc.Hitbox.Intersects(screen)
            ? 1f
            : Consideration.AtLeast(Consideration.Inverse(Target.DistanceToCompanion, Weights.HuntReach), 0.2f);
        float worth = Target.IsBoss ? 0.65f : 0.55f;
        // Two things this used to ignore, both of which killed it.
        //
        // Where the player is. Hunting is opportunistic — something to do when there is little
        // else going on — and it was scored as though the companion stood alone in the world, so
        // a hunt 84 tiles away scored exactly as well as one at his shoulder. Worse, the only
        // safety term was about the *player*, who is safest of all when the companion has wandered
        // off, so straying made hunting score higher. That is a loop with a body at the end of it.
        //
        // Its own skin. Every danger term in the brain read PlayerDanger, so a companion being
        // surrounded 84 tiles out was in a world with no danger in it (2026-09-09, five hits in
        // 330 ticks, danger 0.00 on every one). Hunting now yields as its own danger rises, which
        // is what lets disengaging outscore pressing on.
        float leash = AllowsTarget(ctx, Target.Npc.Bottom) ? 1f : 0f;
        bool local = verdict == FiringAccess.FromHere;
        float ownSkin = local ? 1f : Consideration.AtLeast(1f - ctx.Senses.Threats.CompanionDanger, 0.05f);
        // Whether a shot is possible at all was absent from this product, so hunting something
        // unhittable scored exactly as well as hunting something killable and the companion spent
        // its day walking at enemies it could not harm. It is graded rather than binary: a target
        // it can already hit is worth more than one it must walk to. An unfinished search is not
        // a third grade — PickTarget never selects it — so the only remaining veto is a proven
        // absence, which is what stops the hunt being started at all.
        float shot = verdict switch
        {
            FiringAccess.FromHere => 1f,
            FiringAccess.AfterMoving => Weights.HuntRepositionShot,
            _ => 0f,
        };
        float trip = leash > 0f
            ? (verdict == FiringAccess.FromHere ? 0f
                : MathF.Max(0f, Target.DistanceToCompanion - 200f) / Infrastructure.Movement.OrbPace.MaxSpeed + 60f)
            : 0f;
        preparedKillTicks = leash > 0f ? EstimateKillTicks(ctx, Target.Npc) : 0f;
        localHunt = leash > 0f && (verdict == FiringAccess.FromHere || trip <= Weights.HuntLocalTripTicks);
        huntPrepared = leash > 0f
            ? new(Target.Npc, HostileAttackSources.Generation(Target.Npc), Target.Npc.Bottom, Target.Npc.Center, near * worth * ownSkin * shot, trip)
            : null;
        if (leash == 0f)
        {
            eligibility = OfferEligibility.PolicyForbidden;
            reason = "outside-activity-allowance";
        }
        else
        {
            eligibility = OfferEligibility.Usable;
            reason = verdict switch
            {
                FiringAccess.FromHere => "shot-from-current-position",
                FiringAccess.AfterMoving => "reachable-firing-position",
                _ => "firing-position-undecided",
            };
        }
        return huntValue = near * worth * leash * ownSkin * shot;
    }

    private FiringAccess verdict = FiringAccess.Unknown;

    /// <summary>The arsenal's delayed attack value and the reposition wait of the target this preparation chose.</summary>
    public float PursuitValue { get; private set; }
    public float PursuitAccessTicks { get; private set; }

    /// <summary>Every candidate examined by the last preparation, as slot:generation:verdict:access-ticks:value, in examination order.</summary>
    public string PursuitEvidence { get; private set; } = "";

    private static Rectangle ScreenWithMargin()
        => new((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200, Main.screenWidth + 400, Main.screenHeight + 400);

    /// <summary>The flight to a stand and then the kill. The forecast keeps only the flight, because it is what the
    /// threat horizon and the excursion charge were tuned against; the time term needs the kill too, and it is what
    /// makes a nearly dead enemy worth more than a fresh one without any bonus for having started.</summary>
    private float HuntTaskTicks => huntPrepared is { } offer ? offer.TripTicks + preparedKillTicks : 0f;

    private float preparedKillTicks;

    /// <summary>The target's life over the fastest rate any weapon in the slots deals damage, hits assumed to land and
    /// defence ignored. An optimistic kill time is the right error here: it only has to rank a nearly dead enemy above
    /// a fresh one and a short fight above a long one.</summary>
    private static float EstimateKillTicks(in ActionContext ctx, NPC npc)
    {
        float rate = 0f;
        foreach (var weapon in ctx.Companion.Arsenal.Weapons)
        {
            int damage = weapon.DamagePerHit(ctx);
            if (damage > 0) rate = MathF.Max(rate, damage / (float)Math.Max(1, weapon.UseTime));
        }
        return rate > 0f ? Math.Max(1, npc.life) / rate : Weights.TaskUnknownWorkTicks;
    }

    /// <summary>
    /// Which enemy is worth walking toward. Candidates arrive in the threat list's order — danger to
    /// the player, then nearness — and up to a few are examined: each needs a firing position, and each
    /// that has one is valued by the arsenal as an attack whose first shot waits for the reposition,
    /// zero for a target the hands can already hit and the travel estimate otherwise. The highest value
    /// is pursued. Taking the first admissible candidate instead is what made the feet always follow
    /// the nearest enemy while the hands shot a better one; a short step to remove a dangerous enemy
    /// and a long walk to finish a harmless one are now priced by the same evaluator that ranks shots,
    /// so neither a low health bar nor proximity decides alone. When every examined value is zero —
    /// nothing lands inside the arsenal's window — the first admissible candidate is kept, so a distant
    /// enemy that is still worth approaching is not refused for being distant.
    ///
    /// Existence is the arsenal's real forecast, not a straight ray. Combat does not pick the pair the
    /// hands will fire; it walks toward a pose where that chooser's outcome value is highest, re-ranked
    /// every preparation. An unfinished flood that has already found a solvable stand is a hunt; an
    /// unfinished flood that has found no arc is not. Bounded on purpose: a crowd where nothing is
    /// engageable must not turn one tick into a search.
    /// </summary>
    private const int MaxFiringChecksPerTick = 3;
    private readonly HashSet<int> examined = new();
    private readonly List<(int Slot, int Generation, Vector2 Centre)> examinedAdmissible = new();
    // Every examined threat with a firing verdict, kept until the choice is made so each can be recorded against it.
    private readonly List<(ThreatRecord Threat, FiringAccess Opportunity, float Access, float Value)> admitted = new();

    // The hunt side's stages, in the order a threat meets them.
    private const string StageDeferred = "engagement-deferred";
    private const string StageAllowance = "activity-allowance";
    private const string StageNotChaseable = "not-chaseable";
    private const string StageUnseen = "unseen-and-unreachable";
    private const string StageNoFiringPosition = "no-reachable-firing-position";
    private const string StageFiringUndecided = "firing-position-undecided";
    private const string StageOutvalued = "outvalued";

    /// <summary>
    /// What the last preparation did with each threat it looked at: the ones the candidate filter refused before any shot
    /// was asked about, and each one whose firing position was asked, with the access, the delayed attack value and the
    /// verdict. `firing-position-undecided` as an offer says one examined threat had an open question; the funnel says
    /// which, and whether a nearer threat was refused outright before it.
    /// </summary>
    public CandidateFunnel Funnel { get; } = new(6,
        StageDeferred, StageAllowance, StageNotChaseable, StageUnseen, StageNoFiringPosition, StageFiringUndecided, StageOutvalued,
        CandidateFunnel.Offered);

    private static string Identity(ThreatRecord threat) => FormattableString.Invariant($"npc{threat.Npc.whoAmI}:{threat.Npc.type}");

    private static string FiringReadings(ThreatRecord threat, FiringAccess opportunity, float access, float value)
        => FormattableString.Invariant(
            $"distance={threat.DistanceToCompanion:0};urgency={threat.Urgency:0.000};opportunity={opportunity};access={access:0.0};value={value:0.000}");

    private ThreatRecord? PickTarget(in ActionContext ctx, Rectangle screen)
    {
        // Reused rather than allocated, because this runs on every tick of every hunt.
        examined.Clear();
        examinedAdmissible.Clear();
        admitted.Clear();
        Funnel.Begin();
        bool refusedForFiring = false;
        bool undecidedFiring = false;
        ThreatRecord? chosen = null, firstAdmissible = null;
        FiringAccess chosenVerdict = FiringAccess.None, firstVerdict = FiringAccess.None;
        float chosenValue = 0f, chosenAccess = 0f, firstAccess = 0f;
        var evidence = new StringBuilder();
        for (int attempt = 0; attempt < MaxFiringChecksPerTick; attempt++)
        {
            // The candidate filter's refusals are the same on every pass of a tick, so only the first pass records them.
            ThreatRecord? candidate = BestCandidate(ctx, screen, examined, record: attempt == 0);
            if (candidate == null)
                break;
            examined.Add(candidate.Npc.whoAmI);
            var (opportunity, access) = firingAccess.Resolve(ctx, candidate.Npc);
            float value = opportunity is FiringAccess.None or FiringAccess.Unknown ? 0f
                : ctx.Companion.Arsenal.EstimateDelayedAttackValue(ctx, candidate.Npc, (int)MathF.Min(access, 100_000f), opportunity == FiringAccess.FromHere);
            if (evidence.Length > 0) evidence.Append('|');
            evidence.Append(FormattableString.Invariant(
                $"{candidate.Npc.whoAmI}:{HostileAttackSources.Generation(candidate.Npc)}:{opportunity}:{access:0.0}:{value:0.000}"));
            if (opportunity == FiringAccess.None)
            {
                refusedForFiring = true;
                Funnel.Add(Identity(candidate), candidate.Npc.Center.ToTileCoordinates(), candidate.DistanceToCompanion,
                    "candidate", StageNoFiringPosition, FiringReadings(candidate, opportunity, access, value));
                continue;
            }
            if (opportunity == FiringAccess.Unknown)
            {
                undecidedFiring = true;
                Funnel.Add(Identity(candidate), candidate.Npc.Center.ToTileCoordinates(), candidate.DistanceToCompanion,
                    "candidate", StageFiringUndecided, FiringReadings(candidate, opportunity, access, value));
                continue;
            }
            admitted.Add((candidate, opportunity, access, value));
            examinedAdmissible.Add((candidate.Npc.whoAmI, HostileAttackSources.Generation(candidate.Npc), candidate.Npc.Center));
            if (firstAdmissible == null) { firstAdmissible = candidate; firstVerdict = opportunity; firstAccess = access; }
            if (value > chosenValue) { chosen = candidate; chosenVerdict = opportunity; chosenValue = value; chosenAccess = access; }
        }
        PursuitEvidence = evidence.ToString();
        if (chosen == null && firstAdmissible != null)
        {
            chosen = firstAdmissible; chosenVerdict = firstVerdict; chosenAccess = firstAccess;
        }
        foreach (var (threat, opportunity, access, value) in admitted)
            Funnel.Add(Identity(threat), threat.Npc.Center.ToTileCoordinates(), threat.DistanceToCompanion, "firing-position",
                ReferenceEquals(threat, chosen) ? "" : StageOutvalued, FiringReadings(threat, opportunity, access, value));
        PursuitValue = chosenValue;
        PursuitAccessTicks = chosen == null ? 0f : chosenAccess;
        if (chosen != null)
        {
            verdict = chosenVerdict;
            LastRejection = "accepted";
            return chosen;
        }
        // The reason is set after the loop rather than inside it, because each pass over the threat
        // list resets it, so a reason written during one pass is erased by the next and every
        // refusal would report the generic "nothing eligible" instead of the one a session is read
        // for. Whether the companion had nowhere to shoot from is exactly what needs to survive.
        // An undecided candidate outranks a refused one. Both can appear in one pass, and reporting the refusal
        // brands the whole offer known-unusable on the strength of a different enemy's settled answer, while an
        // enemy whose stand sweep has not finished is a question still open. A proven absence for every examined
        // candidate is the only thing that may close the family, which is the same rule the stand sweep and the
        // positioner's shortlist follow: an exhausted bound is not a negative.
        if (undecidedFiring)
            LastRejection = "firing-position-undecided";
        else if (refusedForFiring)
            LastRejection = "no-reachable-firing-position";
        verdict = FiringAccess.None;
        return null;
    }

    private ThreatRecord? BestCandidate(in ActionContext ctx, Rectangle screen, HashSet<int> unshootable, bool record)
    {
        LastRejection = "no-eligible-target";
        var expired = new List<(int slot, int generation)>();
        foreach (var entry in deferred)
            if (ctx.Senses.Tick >= entry.Value.until) expired.Add(entry.Key);
        foreach (var key in expired) deferred.Remove(key);
        ThreatRecord? best = null;
        float bestScore = 0f;
        foreach (ThreatRecord t in ctx.Senses.Threats.Threats)
        {
            if (unshootable.Contains(t.Npc.whoAmI)) continue;
            var key = (t.Npc.whoAmI, HostileAttackSources.Generation(t.Npc));
            if (deferred.TryGetValue(key, out var failure))
            {
                bool unchanged = ctx.Senses.Tick < failure.until && failure.terrain == Infrastructure.Movement.TerrainChanges.Revision
                    && Vector2.DistanceSquared(failure.target, t.Npc.Center) < 32f * 32f
                    && Vector2.DistanceSquared(failure.body, ctx.Npc.Bottom) < 32f * 32f;
                if (unchanged)
                {
                    LastRejection = "engagement-deferred-no-progress";
                    if (record) Funnel.Add(Identity(t), t.Npc.Center.ToTileCoordinates(), t.DistanceToCompanion, "", StageDeferred, "");
                    continue;
                }
                deferred.Remove(key);
            }
            if (!AllowsTarget(ctx, t.Npc.Bottom, t.Npc))
            {
                if (record) Funnel.Add(Identity(t), t.Npc.Center.ToTileCoordinates(), t.DistanceToCompanion, "", StageAllowance, "");
                continue;
            }
            if (!t.Npc.CanBeChasedBy())
            {
                if (record) Funnel.Add(Identity(t), t.Npc.Center.ToTileCoordinates(), t.DistanceToCompanion, StageAllowance, StageNotChaseable, "");
                continue;
            }
            if (!t.CanReachEither && !t.Npc.Hitbox.Intersects(screen))
            {
                if (record) Funnel.Add(Identity(t), t.Npc.Center.ToTileCoordinates(), t.DistanceToCompanion, StageNotChaseable, StageUnseen, "");
                continue;
            }
            // A sealed-off enemy used to be dropped here unless a weapon solved a shot from where
            // the companion happened to be standing. That test is now both redundant and wrong:
            // redundant because every surviving candidate has its firing opportunity established
            // before selection, and wrong because a from-here test refuses an enemy that a spot
            // three tiles away has a clear line to — which is the case repositioning exists for.
            float score = 0.4f * t.Urgency + 0.6f * Consideration.Inverse(t.DistanceToCompanion, Weights.HuntReach);
            if (t.IsBoss) score += 0.3f;
            // An on-screen enemy is always a candidate: beyond HuntReach the distance term is zero
            // and a calm enemy's urgency is zero too, which vetoed it before the screen rule scored it.
            if (score <= 0f && t.Npc.Hitbox.Intersects(screen)) score = 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    // The merged offer: both sides evaluated, the larger value under its existing terms, scaled.

    private float preparedValue;

    public override void Prepare(in ActionContext ctx)
    {
        ContinueProtection(ctx);
        preparedValue = 0f;
        guardWon = true;
        if (ctx.Companion.Arsenal.Weapons.Count == 0)
        {
            Classify(OfferEligibility.NoOpportunity, "no-weapon");
            return;
        }
        // No stance-level target gate: the hunt side refuses its own inapplicable targets, and the
        // guard side offers on urgency alone — no allowance, no chase predicate — because a threat
        // no weapon can damage keeps its urgency with no removal to estimate. A whole-stance
        // eligible-target gate vetoes exactly that offer.
        // Guard first, as the old registration ordered the two activities: it warms the shared
        // firing-opportunity cache the hunt side reads on the same tick.
        float guard = EvaluateGuard(ctx, out OfferEligibility guardEligibility, out string guardReason);
        float hunt = EvaluateHunt(ctx, out OfferEligibility huntEligibility, out string huntReason);
        // Ties keep the guard side: the old family kept the lower action index, and guard registered
        // first — except at zero, where neither side offers and the record carries the more informative
        // refusal instead: a proven absence outranks an open question, which outranks mere absence.
        guardWon = guard > hunt || (guard == hunt && (guard > 0f || RefusalRank(guardEligibility) >= RefusalRank(huntEligibility)));
        preparedValue = (guardWon ? guard : hunt) * Weights.CombatValueScale;
        if (guardWon)
            Classify(guardEligibility, guardReason);
        else
            Classify(huntEligibility, huntReason);
        if (protectedThreat == null && ReferenceEquals(ctx.Companion.Brain.Chooser.Current, this)) Enter(ctx);
    }

    /// <summary>How informative a zero-value refusal is for the record: a proven absence names what
    /// was established, an open question names what is still asked, and absence or a policy veto names
    /// only that there is nothing to do.</summary>
    private static int RefusalRank(OfferEligibility eligibility) => eligibility switch
    {
        OfferEligibility.KnownUnusable => 3,
        OfferEligibility.Unresolved => 2,
        _ => 1,
    };

    public override float Score() => preparedValue;

    public override float ForecastTicks() => guardWon ? 0f : huntPrepared?.TripTicks ?? 0f;

    public override float TaskTicks() => guardWon ? 0f : HuntTaskTicks;

    public override void Enter(in ActionContext ctx)
    {
        if (guardPrepared is not { } candidate || !IsGuardAvailable(candidate) || ctx.Senses.Player.IsDead) return;
        protectedThreat = candidate.Enemy;
        guardGeneration = candidate.Generation;
        committedPressure = candidate.Pressure;
        safeSince = -1;
        CommitmentReason = "protecting-relevant-threat";
    }

    public override void ObserveOutcome(in ActionContext ctx)
    {
        // Progress accounting belongs to the hunt side's terms: it runs while combat executes as
        // a hunt. A guard standstill is not a stalled approach and must not defer its threat.
        if (!guardWon) ObserveHuntOutcome(ctx);
    }

    public override void BeginAttempt()
    {
        guardAttemptClear = null;
        pursued = null;
        attacked = failed = false;
    }

    /// <summary>
    /// The attempt's conclusion reads whichever side left evidence. A pursued generation gone after
    /// this attempt fired at it completes the hunt, unattributed because the native death hook names
    /// no killer; gone before any pursuit shot it is invalid, and an expired progress window is
    /// failure. Guarding ends complete when the committed threat stopped mattering, by a full
    /// clearance window or by being gone while the player lives; a dead player leaves it invalid.
    /// </summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        if (pursued is { } target
            && (!target.Enemy.active || target.Enemy.life <= 0 || HostileAttackSources.Generation(target.Enemy) != target.Generation))
            return attacked
                ? new(AttemptStatus.Complete, "pursued-target-gone-after-pursuit-attack", AttemptAttribution.Unattributed)
                : new(AttemptStatus.Invalid, "pursued-target-gone-before-pursuit-attack");
        if (failed)
            return new(AttemptStatus.Failed, "no-movement-or-attack-progress");
        if (guardAttemptClear is { } clear)
        {
            if (clear.Reason == "sustained-clearance")
                return new(AttemptStatus.Complete, "threat-irrelevant-for-clearance-window", AttemptAttribution.Unattributed);
            return clear.PlayerDead
                ? new(AttemptStatus.Invalid, "player-unavailable")
                : new(AttemptStatus.Complete, "protected-threat-gone", AttemptAttribution.Unattributed);
        }
        return attacked
            ? new(AttemptStatus.Partial, "attacked-pursued-target-still-present")
            : new(AttemptStatus.Attempted, "replaced-before-attacking-pursued-target");
    }

    private static bool IsGuardAvailable(GuardCandidate candidate)
        => candidate.Enemy.active && candidate.Enemy.life > 0
            && HostileAttackSources.Generation(candidate.Enemy) == candidate.Generation;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (guardWon)
        {
            // Guarding is hunting the threat on a leash, not standing beside the player. Anchoring the
            // position search on the player made the companion hold station next to him and wait: on
            // 2026-09-11 guard took 10,019 ticks, chose no target on 7,690 of them with thirteen to
            // seventeen hostiles reachable, and held its existing spot on 12,592 ticks across the run.
            // Because guard's own release condition is that the threat has stopped being relevant, and
            // nothing was ever shot, it could not end — the player watched it sit beside him through
            // enemies on the platform below. The anchor now walks toward the threat and the leash is
            // what keeps protection local, so "come closer when he is in danger" stays true without
            // meaning "stop looking for what is endangering him".
            if (guardPrepared is not { } candidate || !IsGuardAvailable(candidate) || ctx.Senses.Player.IsDead)
                return PositionRequest.Hold;
            if (!ReferenceEquals(protectedThreat, candidate.Enemy) || guardGeneration != candidate.Generation)
                Enter(ctx);
            return new PositionRequest(RequestKind.Guard, candidate.Anchor, candidate.Enemy);
        }
        if (huntPrepared is not { } offer || !offer.Enemy.CanBeChasedBy()
            || HostileAttackSources.Generation(offer.Enemy) != offer.Generation)
            return PositionRequest.Hold;
        // No firing here. Shooting is what the hands do on a tick combat runs, so it lives in the
        // brain's own tick gated on this activity; combat's execution is only the decision to walk
        // toward something. See Brain.Engage.
        return new PositionRequest(RequestKind.LineOfFire, offer.Centre, offer.Enemy);
    }
}
