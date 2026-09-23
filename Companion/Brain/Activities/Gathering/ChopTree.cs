#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using AICompanion.Companion.Brain.Infrastructure.Interactions;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>
/// Swings the axe at the one trunk the course bound. It does not look for trees: the trunk census is the
/// only discovery, and the preference for a trunk the player is not cutting is decided there, where every
/// trunk is known (<see cref="CaptureTreeOpportunities"/>).
///
/// <para>Until 23 September 2026 this ran its own nearest-trunk search from two origins and swung at what it
/// found while the body flew to the course's trunk; <see cref="MineOre"/> carries the account of why two
/// choosers were the defect. What stays here is what is not choosing: the player's axe contact ageing under
/// every policy, the Mimic and policy gates at the swing, home protection and actual tool reach at the native
/// call, the hand reserved only while the axe is out, and the evidence an attempt concludes from.</para>
/// </summary>
public sealed class ChopTree : CompanionAction
{
    public override string Name => "chop";
    public override PurposeFamily Family => PurposeFamily.Gathering;
    public override string[] CourseDomains => new[] { "chop-target" };
    public override Vector2? ActivityTarget => bound?.Bottom.ToWorldCoordinates();
    /// <summary>
    /// The bound trunk and its material, and how many steps on it were refused. The course binds one swing per
    /// step, so consecutive swings at one trunk are one attempt; the material is part of it because a different
    /// tree type at the same coordinate is a different tree. A refusal is part of it so the refused step's
    /// attempt closes with that cause even when the course binds the same trunk again.
    /// </summary>
    public override object? ActivityIdentity => bound is { } b ? (b.Bottom, b.Material, refusals) : null;
    /// <summary>True only while the axe is actually out; the whole flight to the tree is empty-handed.</summary>
    public override bool HandsBusy => swinging;
    private bool swinging;
    /// <summary>Retain the tree, release its tool phase; the next step arrives with the next selection.</summary>
    public override void Exit(in ActionContext ctx)
    {
        swinging = false;
        bound = null;
    }

    /// <summary>How long the player's axe contact keeps Mimic chopping live after his last hit.</summary>
    private const int KeepJobTicks = 120;

    private (Point Bottom, int Material)? bound;
    private int refusals;
    /// <summary>The last approach verdict and the body, terrain and reach it was proved under, reused while they hold.</summary>
    private ((Point Trunk, Point Body, int Revision, (int X, int Y) Reach) Key, Reachability.Reach Verdict)? approachProof;
    // Starts expired: no player axe contact has been observed, so nothing is being mimicked yet.
    private int sincePlayerHit = KeepJobTicks + 1;

    /// <summary>Whether the player's axe contact still licenses Mimic chopping: his current contact, or one within
    /// <see cref="KeepJobTicks"/>. The trunk census reads this rather than keeping a window of its own, because until
    /// 23 September 2026 it read only the watcher's 45-tick current contact while this hand kept the job for 120, so under
    /// Mimic the census withdrew the trunk three quarters of a second after his last swing and the companion stopped.
    /// Read after <see cref="Prepare"/>, which ages it, and the tick prepares every activity before it decides.</summary>
    internal bool MimicContactLive => sincePlayerHit <= KeepJobTicks;
    /// <summary>What the last swing did or why it refused, for the record and for rows that read the refusal by name.</summary>
    public string Status { get; private set; } = "idle";
    /// <summary>The native work left on the bound trunk at this tick's swing, or null with nothing bound.</summary>
    public RemainingToolWork? RemainingWork { get; private set; }

    protected override void OnAccept(StepBinding? step)
    {
        if (step == null)
        {
            bound = null;
            Status = "no course step";
            return;
        }
        if (!GatheringOpportunityBinder.TryReadUse(step, "chop-target", out Point bottom, out int material))
            throw new InvalidOperationException(
                $"Chopping was handed a step it cannot perform: domain '{step.Opportunity.Domain}', use '{step.NativeUseId}', "
                + $"opportunity '{step.Opportunity.Target}'. A chop step is bound by GatheringOpportunityBinder and names its trunk as 'chop-target:x,y:material'.");
        if (bound != (bottom, material)) refusals = 0;
        bound = (bottom, material);
    }

    /// <summary>
    /// Ages the player's axe contact, which has to happen on every tick under every policy — Disabled included —
    /// because it starts expired and a contact made just before chopping was switched off must have aged by the
    /// time Mimic comes back. It chooses nothing.
    /// </summary>
    public override void Prepare(in ActionContext ctx)
    {
        var p = ctx.Senses.Player;
        if (p.IsChoppingTree) sincePlayerHit = 0;
        else if (sincePlayerHit <= KeepJobTicks) sincePlayerHit++;
        // Evidence, not a choice. Whatever makes the bound trunk stop being work — the player starting on it, a bed
        // placed beside it, the policy switched, a wall put up mid-flight — usually makes the course drop the step
        // on this same tick, before the swing can see it, and an attempt closed by that replacement would read as
        // work abandoned rather than say why. The preparation runs before the decision, so it records the reason
        // here with the same ladder the swing refuses by; it never picks another trunk.
        if (bound is { } held && release == null)
        {
            if (p.ChoppedTree == held.Bottom) release = "player-took-trunk";
            else if (RefusalAtTheSwing(ctx, held) is { } reason) release = reason;
            else if (!FindToolAccess.InReach(ctx.Npc.Center, held.Bottom) && ApproachVerdict(ctx, held.Bottom) == Reachability.Reach.No)
                release = "approach-not-established";
        }
        if (p.IsDead) Classify(OfferEligibility.NoOpportunity, "player-dead");
        else if (WorkPolicies.Chopping == WorkPolicy.Disabled) Classify(OfferEligibility.PolicyForbidden, "chopping-disabled");
        else if (WorkPolicies.Chopping == WorkPolicy.Mimic && !p.IsChoppingTree && sincePlayerHit > KeepJobTicks)
            Classify(OfferEligibility.PolicyForbidden, "mimic-awaiting-player-tree-contact");
        else Classify(bound != null ? OfferEligibility.Usable : OfferEligibility.NoOpportunity, bound != null ? "trunk-bound" : "no-course-step");
    }

    /// <summary>The course prices chopping; this activity offers no worth of its own, and nothing on the tick reads one.</summary>
    public override float Score() => 0f;

    /// <summary>The bound step's own forecast: its travel and its use, as the binder priced them.</summary>
    public override float ForecastTicks() => Bound is { } step ? (float)(step.TravelTicks + step.UseTicks) : 0f;

    // Attempt-local evidence, cleared when an attempt opens: the trunk this attempt swung at, whether
    // its own strike removed it, and the last way it gave the tree up.
    private Point? attemptTrunk;
    private bool attemptFelled;
    private string? release;

    public override void BeginAttempt()
    {
        attemptTrunk = null;
        attemptFelled = false;
        release = null;
    }

    /// <summary>A trunk that stopped standing after this attempt's productive strikes completes it,
    /// attributed to the companion only when its own strike was the removal. The same disappearance
    /// without strikes is someone else's work. A release is a failed method only when the approach
    /// itself could not be established; lost permission or changed material is invalid.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        if (attemptTrunk is Point trunk && !TileChopper.TreeStands(trunk))
            return productiveEffects > 0
                ? new(AttemptStatus.Complete, "trunk-no-longer-stands", attemptFelled ? AttemptAttribution.Companion : AttemptAttribution.Shared)
                : new(AttemptStatus.Invalid, "trunk-gone-without-companion-effect");
        if (release is string reason)
            return new(productiveEffects > 0 ? AttemptStatus.Partial
                : reason == "approach-not-established" ? AttemptStatus.Failed : AttemptStatus.Invalid, reason);
        return productiveEffects > 0
            ? new(AttemptStatus.Partial, "replaced-with-trunk-standing")
            : new(AttemptStatus.Attempted, "replaced-before-productive-effect");
    }

    public override PositionRequest Execute(in ActionContext ctx)
    {
        // The axe comes out only in position; on the flight there the hand stays empty, so the
        // torch can hold it in the dark.
        ctx.Companion.HoldItem(ItemID.None);
        swinging = false;
        RemainingWork = null;
        if (bound is not { } b)
        {
            Status = "no course step";
            return PositionRequest.Hold;
        }
        attemptTrunk = b.Bottom;
        // A trunk the player has started on is recorded as his by the preparation, so an attempt the course moves
        // off it concludes with that cause. Under Opportunistic the census withdraws his trunk when another is
        // free and the course moves; when his is the only tree the companion shares it, which is the census's
        // call rather than this one's, so only Mimic refuses it at the swing.
        if (RefusalAtTheSwing(ctx, b) is { } refusal)
        {
            Refuse(refusal);
            return PositionRequest.Hold;
        }
        Item axe = TileChopper.AxeFor(ctx.Player);
        RemainingWork = ctx.Companion.Chopper.EstimateRemaining(b.Bottom, axe);
        if (!FindToolAccess.InReach(ctx.Npc.Center, b.Bottom))
        {
            // A trunk walled off mid-flight is a failed method, named, rather than a body pressing at a wall
            // until the course notices; the check never picks another tree.
            if (ApproachVerdict(ctx, b.Bottom) == Reachability.Reach.No)
            {
                Refuse("approach-not-established");
                return PositionRequest.Hold;
            }
            Status = "approaching";
            Vector2 pose = Bound is { } step ? new Vector2((float)step.Pose.X, (float)step.Pose.Y) : ctx.Npc.Center;
            return PositionRequest.ExactAt(pose, b.Bottom);
        }
        Status = "chopping";
        ctx.Companion.HoldItem(axe.type);
        swinging = true;
        ctx.Companion.Motor.Face(b.Bottom.X * 16f + 8f);
        ctx.Companion.ShowBeam(b.Bottom.ToWorldCoordinates(8f, 8f));
        if (ctx.Companion.Chopper.Swing(b.Bottom, axe))
        {
            ctx.Companion.StartAnimation(axe.type, axe.useAnimation);
            if (ctx.Companion.Chopper.LastOutcome is { } outcome)
            {
                var owner = ctx.Companion.Brain.Activity;
                // The course's decision identity, which is the same `choice_id` the recorder's rows carry
                // since schema 0.43.0. This passed `Chooser.EvaluationId` until 22 September 2026, and the
                // chooser stopped advancing that counter when `0bb2c8a` took it off the tick, so any
                // `tool-effect` occurrence written after that carried identity zero and could not be joined
                // to the decision that caused it. **No capture demonstrates it**: all 43 event sidecars since
                // the switch contain zero `tool-effect` occurrences, the last one carrying any being
                // `2026-09-16_09-44-42-164`, pre-switch, where the id equals the tick. So this is a defect
                // reasoned from the source with no observed instance, and the row that would have caught it
                // does not exist — it would assert that a session in which the companion breaks a tile writes
                // a `tool-effect` naming the decision that bound the work, which is one assertion over the
                // mining evidence scene's existing capture in `VerifyAttemptEvidenceProducers`.
                Infrastructure.Diagnostics.GodsEyeEvents.RecordToolEffect(ctx.Npc, "axe", outcome, ctx.Companion.Brain.Course.DecisionId, owner.Id, owner.AttemptOpen ? owner.AttemptId : 0);
                if (outcome.Effect == Infrastructure.Interactions.TileToolEffect.Removed && outcome.Target == b.Bottom) attemptFelled = true;
                if (outcome.Productive) ctx.Companion.Brain.Activity.RecordWork(b.Bottom.ToWorldCoordinates());
            }
        }
        return PositionRequest.Hold;
    }

    /// <summary>
    /// Whether the bound trunk may still be struck: the work was switched off, Mimic has nothing to mimic or
    /// the trunk is the player's, the tree is gone or has become another tree, it sits in a protected home, or
    /// it has left the allowance. Each is observed at the call, and none of them is answered by picking a
    /// different tree.
    /// </summary>
    private string? RefusalAtTheSwing(in ActionContext ctx, (Point Bottom, int Material) b)
    {
        var p = ctx.Senses.Player;
        if (WorkPolicies.Chopping == WorkPolicy.Disabled) return "work-disabled";
        if (WorkPolicies.Chopping == WorkPolicy.Mimic)
        {
            if (!p.IsChoppingTree && sincePlayerHit > KeepJobTicks) return "mimic-awaiting-player-tree-contact";
            if (p.ChoppedTree == b.Bottom) return "player-took-trunk";
        }
        if (!TileChopper.TreeStands(b.Bottom)) return "bound-trunk-no-longer-stands";
        if (Main.tile[b.Bottom.X, b.Bottom.Y].TileType != b.Material) return "bound-trunk-material-changed";
        if (Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(b.Bottom)) return "outside-allowance-or-protected-home";
        if (!AllowsTarget(ctx, b.Bottom.ToWorldCoordinates())) return "outside-allowance-or-protected-home";
        return null;
    }

    /// <summary>Whether any free cell still reaches the bound trunk, reused while the body's tile, the terrain and
    /// the reach are unchanged. Its only use is to refuse this trunk; it never picks another.</summary>
    private Reachability.Reach ApproachVerdict(in ActionContext ctx, Point trunk)
    {
        var key = (trunk, MovementQueries.Tile(ctx.Npc.Center), TerrainChanges.Revision, FindToolAccess.Reach);
        if (approachProof?.Key != key)
            approachProof = (key, FindToolAccess.Approach(trunk, ctx.Npc.Center, ctx.Senses.Reach, out _));
        return approachProof.Value.Verdict;
    }

    /// <summary>Refuse the bound step by name: the attempt records why, the admission is released, and the next
    /// selection closes the attempt with that cause even if the course binds this trunk again.</summary>
    private void Refuse(string cause)
    {
        // The player's claim on the trunk is the more specific account when both hold, so a refusal only
        // overwrites a release that is not already his.
        if (release != "player-took-trunk" || cause == "player-took-trunk") release = cause;
        Status = cause;
        refusals++;
        RefuseStep(cause);
        ReleaseActivity();
        Classify(cause is "work-disabled" or "mimic-awaiting-player-tree-contact" or "player-took-trunk"
            ? OfferEligibility.PolicyForbidden : OfferEligibility.KnownUnusable, cause);
    }
}
