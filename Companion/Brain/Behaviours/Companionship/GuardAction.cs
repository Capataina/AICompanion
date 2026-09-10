#nullable enable

using Terraria.ID;
using System;
using AICompanion.Companion.Brain.WorldObservation;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;

namespace AICompanion.Companion.Brain.Behaviours.Companionship;

/// <summary>
/// The player is in danger: be next to them with a sight line, and shoot the most
/// urgent threat from there. Outscores everything as danger rises, which is what
/// pulls the companion off a hunt or a loot run when a shooter gets a line on the player.
/// </summary>
public sealed class GuardAction : CompanionAction
{
    public override string Name => "guard";
    public override bool IsExcursion => false;

    private Terraria.NPC? protectedThreat;
    private int generation;
    private float committedPressure;
    private int safeSince = -1;
    public int ProtectedThreatId => protectedThreat?.whoAmI ?? -1;
    public float RetainedPressure => committedPressure;
    public string CommitmentReason { get; private set; } = "no-commitment";

    public override void Enter(in ActionContext ctx)
    {
        var threat = ctx.Senses.Threats.MostUrgent;
        if (threat == null || !threat.Npc.active || threat.Npc.life <= 0
            || Math.Max(ctx.Senses.Threats.ProtectionUrgency, threat.Urgency) <= 0f) return;
        protectedThreat = threat.Npc;
        generation = HostileAttackSources.Generation(threat.Npc);
        committedPressure = Math.Max(ctx.Senses.Threats.ProtectionUrgency, threat.Urgency);
        safeSince = -1;
        CommitmentReason = "protecting-relevant-threat";
    }

    private ThreatRecord? ContinueProtection(in ActionContext ctx)
    {
        if (protectedThreat == null) return null;
        if (ctx.Senses.Player.IsDead || !protectedThreat.active || protectedThreat.life <= 0
            || generation != HostileAttackSources.Generation(protectedThreat))
        {
            Clear("threat-or-player-unavailable");
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

    private void Clear(string reason)
    {
        protectedThreat = null;
        committedPressure = 0f;
        safeSince = -1;
        CommitmentReason = reason;
    }

    public override float Score(in ActionContext ctx)
    {
        var t = ctx.Senses.Threats;
        ContinueProtection(ctx);
        if (protectedThreat == null && ReferenceEquals(ctx.Companion.Brain.Chooser.Current, this))
            Enter(ctx);
        if (ctx.Senses.Player.IsDead)
            return 0f;
        float danger = Math.Max(t.ProtectionUrgency, committedPressure);
        // Scaled past the ordinary 0..1 band because guarding has to be able to interrupt, and an
        // action that tops out at 1 cannot interrupt anything: the running action carries
        // Weights.Commitment, so a following body already at 1 sits at 1.15 and no danger reading
        // could ever displace it. That is not a tuning miss, it is arithmetic, and it is what left
        // the companion holding a torch with the player's danger reading full and slimes on him
        // through the 2026-09-09 underground session. Weights.GuardUrgency owns the ladder and
        // why survive sits above this in turn.
        // Protection is the ability to intervene, not the distance between the allies. Charging
        // closeness again suppressed protection precisely when an enemy reached the player.
        return danger * Weights.GuardUrgency;
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        // Standing between him and them is all this does now; the shooting happens in Brain.Engage.
        var target = ctx.Senses.Threats.MostUrgent?.Npc ?? protectedThreat;
        return new PositionRequest(RequestKind.Guard, ctx.Senses.Player.Bottom, target);
    }
}
