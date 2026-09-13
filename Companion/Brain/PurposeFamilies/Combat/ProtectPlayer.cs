#nullable enable

using System;
using AICompanion.Companion.Brain.Behaviours;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;

namespace AICompanion.Companion.Brain.PurposeFamilies.Combat;

/// <summary>
/// Offer positioning against a particular threat to the player. Preparation binds the
/// threat and anchor; the positioner establishes attack access and the independent hands fire.
/// </summary>
public sealed class ProtectPlayer : CompanionAction
{
    public override string Name => "guard";
    public override PurposeFamily Family => PurposeFamily.Combat;
    public override bool IsExcursion => false;
    public override object? ActivityIdentity => prepared?.Enemy;
    public override Vector2? ActivityTarget => prepared?.Bottom;
    public override PositionRequest? PreparedPositionRequest => prepared is { } candidate
        ? new PositionRequest(RequestKind.Guard, candidate.Anchor, candidate.Enemy) : null;

    private readonly record struct Candidate(Terraria.NPC Enemy, int Generation, Vector2 Bottom,
        Vector2 Anchor, float Pressure);
    private Candidate? prepared;

    private Terraria.NPC? protectedThreat;
    private int generation;
    private float committedPressure;
    private int safeSince = -1;
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

    private readonly ResolveFiringOpportunity firingAccess;

    /// <summary>A guard that owns its own query, for callers that construct one activity alone.</summary>
    public ProtectPlayer() : this(new ResolveFiringOpportunity()) { }

    public ProtectPlayer(ResolveFiringOpportunity firingAccess) => this.firingAccess = firingAccess;

    public override void Enter(in ActionContext ctx)
    {
        if (prepared is not { } candidate || !IsAvailable(candidate) || ctx.Senses.Player.IsDead) return;
        protectedThreat = candidate.Enemy;
        generation = candidate.Generation;
        committedPressure = candidate.Pressure;
        safeSince = -1;
        CommitmentReason = "protecting-relevant-threat";
    }

    private ThreatRecord? ContinueProtection(in ActionContext ctx)
    {
        if (protectedThreat == null) return null;
        if (ctx.Senses.Player.IsDead || !protectedThreat.active || protectedThreat.life <= 0
            || generation != HostileAttackSources.Generation(protectedThreat))
        {
            Clear("threat-or-player-unavailable", ctx.Senses.Player.IsDead);
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
                Clear("sustained-clearance");
                return null;
            }
            CommitmentReason = "confirming-clearance";
        }
        return record;
    }

    private void Clear(string reason, bool playerDead = false)
    {
        // One record of how this attempt's commitment ended, written where it ends; a later Enter
        // for a different threat rewrites CommitmentReason but never this.
        if (protectedThreat != null) attemptClear = (reason, playerDead);
        protectedThreat = null;
        committedPressure = 0f;
        safeSince = -1;
        CommitmentReason = reason;
    }

    private (string Reason, bool PlayerDead)? attemptClear;

    public override void BeginAttempt() => attemptClear = null;

    /// <summary>Protection ends complete when the committed threat stopped mattering, either by a
    /// full clearance window or by being gone while the player lives. Nothing observed names who
    /// removed it, so the completion is unattributed; a dead player leaves the attempt invalid.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        if (attemptClear is { } clear)
        {
            if (clear.Reason == "sustained-clearance")
                return new(AttemptStatus.Complete, "threat-irrelevant-for-clearance-window", AttemptAttribution.Unattributed);
            return clear.PlayerDead
                ? new(AttemptStatus.Invalid, "player-unavailable")
                : new(AttemptStatus.Complete, "protected-threat-gone", AttemptAttribution.Unattributed);
        }
        return new(AttemptStatus.Attempted, "replaced-before-threat-cleared");
    }

    private float preparedValue;
    public override void Prepare(in ActionContext ctx)
    {
        var t = ctx.Senses.Threats;
        ContinueProtection(ctx);
        var target = t.MostUrgent?.Npc;
        if (target == null || !target.active || target.life <= 0) target = protectedThreat;
        prepared = null;
        preparedValue = 0;
        if (ctx.Senses.Player.IsDead || target == null || !target.active || target.life <= 0)
        {
            Classify(OfferEligibility.NoOpportunity, ctx.Senses.Player.IsDead ? "player-dead" : "no-threat");
            return;
        }
        float currentPressure = ReferenceEquals(target, t.MostUrgent?.Npc) ? t.MostUrgent.Urgency : 0f;
        float retainedPressure = ReferenceEquals(target, protectedThreat) ? committedPressure : 0f;
        float danger = Math.Max(t.ProtectionUrgency, Math.Max(currentPressure, retainedPressure));
        if (danger <= 0)
        {
            Classify(OfferEligibility.NoOpportunity, "no-protection-pressure");
            return;
        }
        // Whether a useful intervention position exists is decided by method admission at
        // nomination, so the prepared offer is honestly unresolved until that query answers.
        Classify(OfferEligibility.Unresolved, "intervention-destination-requires-admission");
        Vector2 toThreat = target.Bottom - ctx.Senses.Player.Bottom;
        float leash = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        Vector2 anchor = toThreat.LengthSquared() <= leash * leash
            ? target.Bottom
            : ctx.Senses.Player.Bottom + Vector2.Normalize(toThreat) * leash;
        prepared = new(target, HostileAttackSources.Generation(target), target.Bottom, anchor, danger);
        if (protectedThreat == null && ReferenceEquals(ctx.Companion.Brain.Chooser.Current, this)) Enter(ctx);
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
        Access = null;
        AccessTicks = float.NaN;
        InterventionUsefulness = 1f;
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
                Classify(OfferEligibility.KnownUnusable, "no-reachable-firing-position");
            }
            else
            {
                float total = RemovalTicks + (float.IsFinite(AccessTicks) ? AccessTicks : 0f);
                InterventionUsefulness = total > Weights.GuardUsefulRemovalTicks ? Weights.GuardUsefulRemovalTicks / total : 1f;
            }
        }
        preparedValue = danger * InterventionUsefulness * Weights.GuardUrgency;
    }

    public override float Score() => preparedValue;

    private static bool IsAvailable(Candidate candidate)
        => candidate.Enemy.active && candidate.Enemy.life > 0
            && HostileAttackSources.Generation(candidate.Enemy) == candidate.Generation;

    public override PositionRequest Execute(in ActionContext ctx)
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
        if (prepared is not { } candidate || !IsAvailable(candidate) || ctx.Senses.Player.IsDead)
            return PositionRequest.Hold;
        if (!ReferenceEquals(protectedThreat, candidate.Enemy) || generation != candidate.Generation)
            Enter(ctx);
        return new PositionRequest(RequestKind.Guard, candidate.Anchor, candidate.Enemy);
    }
}
