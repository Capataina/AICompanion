#nullable enable

using FindToolAccess = AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using System;
using System.Collections.Generic;
using AICompanion.Companion.Brain.Activities;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>
/// Swings the pickaxe at the one ore tile the course bound, and keeps the account of the vein that tile
/// belongs to. It does not look for ore: the gathering census is the only discovery and the course is the
/// only chooser, so this activity performs the accepted use and nothing else.
///
/// <para>**Two choosers were the defect this shape removes.** Until 23 September 2026 this class ran its own
/// nearest-first search in <c>Prepare</c> and swung at what it found, while the body flew to the pose of the
/// tile the course had bound. The two agreed only when both happened to pick the same nearest ore; when they
/// disagreed the body hovered beside one vein while the hand worked another, or worked nothing because its
/// own target was out of reach from the course's pose. The retained-course plan's tick step 9 is the rule:
/// perform the accepted use, and no tactical fallback may act on a target the course does not believe it
/// chose.</para>
///
/// <para>What is kept is everything that is not choosing. The vein the bound tile belongs to is flooded once
/// when a step first lands on it, so the job can say how much of it the companion removed and whether it was
/// cleared — which is what attempt conclusions and work credit read. Policy, the mining list, the allowance
/// and home protection are checked again at the swing, because the census proved them against an
/// observation that may be several ticks old. A bound tile that stopped being valid is refused by name and
/// never swapped for a neighbour.</para>
/// </summary>
public sealed class MineOre : CompanionAction
{
    public override string Name => "mine";
    public override PurposeFamily Family => PurposeFamily.Gathering;
    public override string[] CourseDomains => new[] { "mine-target" };
    public override Vector2? ActivityTarget => bound?.Tile.ToWorldCoordinates();

    /// <summary>How long a player's ore hit keeps Mimic mining live. The census admits Mimic work under the same
    /// window, because the two answer one question at two moments: whether the player is mining right now.</summary>
    public const int MimicContactTicks = 600;

    /// <summary>The bound step's tile and material, adopted in <see cref="OnAccept"/>.</summary>
    private (Point Tile, int Material)? bound;
    /// <summary>Every tile of the job's vein as it was flooded when the job opened; the conclusion's denominator.</summary>
    private HashSet<Point> jobTiles = new();
    /// <summary>The job's tiles still ore of its material, so the remaining count is what is actually left.</summary>
    private readonly HashSet<Point> patch = new();
    private readonly HashSet<Point> ownRemovals = new();
    private int jobType;
    private int nextJobId = 1;
    private int jobId;
    /// <summary>How many steps this job has refused. It is part of the identity so a refusal closes its attempt
    /// on the next selection even when the course binds the same vein again, which is what makes the refusal's
    /// name the attempt's conclusion rather than a note overwritten by the next swing.</summary>
    private int refusals;
    private string status = "idle";
    /// <summary>The status, and so the refusal's name, when the bound ore is one the player's mining list leaves.</summary>
    private const string LeftByMiningList = "ore left by mining list";
    private const string NoProvenPoseReason = "remaining ore has no reachable working cell";

    private bool swinging;
    /// <summary>What the last approach check proved and the inputs it was proved under, so an unchanged body,
    /// terrain and reach reuse the verdict rather than re-ranking the tile's cells every tick of a flight.</summary>
    private (Point Tile, Point Body, int Revision, (int X, int Y) Reach, Reachability.Reach Verdict)? approachProof;

    public int JobId => jobId;
    public DescribeOreJobEnd? LastConclusion { get; private set; }
    /// <summary>True only while the pickaxe is actually out; the whole flight to the vein is empty-handed.</summary>
    public override bool HandsBusy => swinging;
    /// <summary>
    /// One attempt per vein: the job, not the bound tile. The course binds one use per step, so a vein is many
    /// consecutive steps on different tiles, and an identity keyed on the step's opportunity would close and
    /// reopen an attempt on every tile — worse, the ore opportunity's identity names the vein's first tile, which
    /// changes the moment that tile is mined, so even keying on the opportunity would churn mid-vein. Membership
    /// of the flooded vein is what "the same job" means, so a new identity is exactly a new vein, a different
    /// material, or a refused step.
    /// </summary>
    public override object? ActivityIdentity => jobId > 0 || bound != null ? (jobId, refusals) : null;
    public WorkPolicy Policy => WorkPolicies.Mining;
    public string Status => status;
    public int RemainingTiles => patch.Count;
    public Point? TargetTile => bound?.Tile;
    public Vector2? TargetStandPosition => Bound is { } step ? new Vector2((float)step.Pose.X, (float)step.Pose.Y) : null;
    /// <summary>The native work left on the bound tile at this tick's swing, or null with nothing bound.</summary>
    public Infrastructure.Interactions.RemainingToolWork? RemainingWork { get; private set; }

    /// <summary>
    /// Adopt the tile the course bound. A tile of the vein already under way keeps the job and its attempt;
    /// anything else ends that job and opens one on the bound tile's vein.
    /// </summary>
    protected override void OnAccept(StepBinding? step)
    {
        if (step == null)
        {
            bound = null;
            status = "no course step";
            return;
        }
        if (!GatheringOpportunityBinder.TryReadUse(step, "mine-target", out Point tile, out int material))
            throw new InvalidOperationException(
                $"Mining was handed a step it cannot perform: domain '{step.Opportunity.Domain}', use '{step.NativeUseId}', "
                + $"opportunity '{step.Opportunity.Target}'. A mine step is bound by GatheringOpportunityBinder and names its tile as 'mine-target:x,y:material'.");
        bound = (tile, material);
        if (jobId > 0 && material == jobType && jobTiles.Contains(tile)) return;
        if (jobId > 0) ClearJob("course-bound-another-vein");
        // A tile that is already not ore of its material opens no job: there is no vein to account for, and
        // the swing below refuses it by name.
        if (!OreFinder.IsOreOfType(tile.X, tile.Y, material)) return;
        patch.Clear();
        foreach (Point member in OreFinder.Vein(tile, material)) patch.Add(member);
        jobTiles = new HashSet<Point>(patch);
        ownRemovals.Clear();
        jobType = material;
        jobId = nextJobId++;
        refusals = 0;
        status = "bound";
    }

    /// <summary>
    /// Keeps the job's account true against the world before the course decides: ore removed or transformed by
    /// anyone leaves the working set, and a job the policy, the list or the allowance has ended is concluded with
    /// that as its reason. It chooses nothing; the census and the course do.
    /// </summary>
    public override void Prepare(in ActionContext ctx)
    {
        if (ctx.Senses.Player.IsDead)
        {
            Classify(OfferEligibility.NoOpportunity, "player-dead");
            return;
        }
        if (WorkPolicies.Mining == WorkPolicy.Disabled)
        {
            if (jobId > 0) ClearJob("disabled");
            Classify(OfferEligibility.PolicyForbidden, "mining-disabled");
            return;
        }
        if (WorkPolicies.Mining == WorkPolicy.Mimic && !PlayerIsMining(ctx))
        {
            if (jobId > 0) ClearJob("mimic trigger expired");
            Classify(OfferEligibility.PolicyForbidden, "mimic-awaiting-player-ore-contact");
            return;
        }
        if (patch.Count > 0)
        {
            patch.RemoveWhere(tile => !OreFinder.IsOreOfType(tile.X, tile.Y, jobType));
            if (patch.Count == 0)
            {
                ClearJob("no eligible ore remains");
                if (LastConclusion is { ObservedClear: true }) status = "tracked ore cleared";
            }
        }
        // A mark the player sets on the card while a vein of that ore is under way ends the job, the way turning
        // mining off does: the list is a standing instruction, not a filter on where a new job may start.
        if (jobId > 0 && !WorkPolicies.MinesOre(jobType)) ClearJob(LeftByMiningList);
        if (patch.Count > 0)
        {
            var context = ctx;
            patch.RemoveWhere(tile => !AllowsTarget(context, tile.ToWorldCoordinates())
                || Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(tile));
            if (patch.Count == 0) ClearJob("outside activity range or protected home");
        }
        // Evidence, not a choice: a player who walls the bound ore off mid-flight usually makes the course drop the
        // step before this activity's own swing can see it, and an attempt closed by that replacement would read as
        // work abandoned rather than as a method that failed. Recorded here because the preparation runs before the
        // decision on the same tick, and withdrawn if the ore opens again.
        if (jobId > 0 && bound is { } held && OreFinder.IsOreOfType(held.Tile.X, held.Tile.Y, held.Material)
            && !FindToolAccess.InReach(ctx.Npc.Center, held.Tile))
        {
            if (ApproachVerdict(ctx, held.Tile) == Reachability.Reach.No) attemptSetback ??= (AttemptStatus.Failed, NoProvenPoseReason);
            else if (attemptSetback is { Cause: NoProvenPoseReason }) attemptSetback = null;
        }
        Classify(jobId > 0 ? OfferEligibility.Usable : OfferEligibility.NoOpportunity, jobId > 0 ? "vein-under-way" : "no-course-step");
    }

    /// <summary>The course prices mining; this activity offers no worth of its own, and nothing on the tick reads one.</summary>
    public override float Score() => 0f;

    /// <summary>The bound step's own forecast: its travel and its use, as the binder priced them.</summary>
    public override float ForecastTicks() => Bound is { } step ? (float)(step.TravelTicks + step.UseTicks) : 0f;

    /// <summary>
    /// A cleared tracked vein completes the attempt only when this attempt itself produced an
    /// observed effect: the same empty coordinates reached through the player's pickaxe change the
    /// remaining work and earn the companion nothing. A job ended by lost permission or changed
    /// material is invalid rather than failed, because the method never got to prove itself.
    /// </summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        // A job conclusion counts only if it was captured after this attempt opened.
        if (conclusionSequence > attemptConclusionBase && LastConclusion is { } end)
        {
            if (end.ObservedClear)
            {
                if (productiveEffects == 0) return new(AttemptStatus.Invalid, "tracked-vein-cleared-without-companion-effect");
                // Every tracked site is empty but ore of the same kind still touches one: the vein continues past what the
                // job tracked, so the work is a finished portion. Calling it complete is a completion the world contradicts.
                if (!end.VeinObservedClear) return new(AttemptStatus.Partial, "tracked-portion-clear-vein-continues");
                // Every tracked site removed by the companion's own strikes finished the job alone;
                // any site that vanished some other way means it was finished together.
                return new(AttemptStatus.Complete, "tracked-vein-observed-clear",
                    end.CompanionRemovals == end.Tracked ? AttemptAttribution.Companion : AttemptAttribution.Shared);
            }
            if (productiveEffects > 0) return new(AttemptStatus.Partial, end.Reason);
            if (attemptSetback is { } refused) return new(refused.Status, refused.Cause);
            return new(AttemptStatus.Invalid, end.Reason);
        }
        if (attemptSetback is { } setback)
            return productiveEffects > 0 ? new(AttemptStatus.Partial, setback.Cause) : new(setback.Status, setback.Cause);
        return productiveEffects > 0
            ? new(AttemptStatus.Partial, "replaced-with-vein-remaining")
            : new(AttemptStatus.Attempted, "replaced-before-productive-effect");
    }

    public override void BeginAttempt()
    {
        attemptConclusionBase = conclusionSequence;
        attemptSetback = null;
    }

    // Incremented each time a job conclusion is captured; an attempt reads a conclusion only if the
    // sequence moved after it opened, which is what a same-tick clear-then-rebind needs.
    private int conclusionSequence, attemptConclusionBase;
    // The refusal this attempt ended on, recorded where it happens so the conclusion names it.
    private (AttemptStatus Status, string Cause)? attemptSetback;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        // The pickaxe comes out only in position; on the flight there the hand stays empty, so
        // the torch can hold it in the dark and the other arm can still throw.
        ctx.Companion.HoldItem(ItemID.None);
        swinging = false;
        RemainingWork = null;
        if (bound is not { } b)
        {
            status = "no course step";
            return PositionRequest.Hold;
        }
        Item pickaxe = TileMiner.PickaxeFor(ctx.Player);
        if (RefusalAtTheSwing(ctx, b, pickaxe.pick) is { } refusal)
        {
            Refuse(refusal.Cause, refusal.Status, refusal.Text);
            return PositionRequest.Hold;
        }
        RemainingWork = ctx.Companion.Miner.EstimateRemaining(b.Tile, pickaxe);
        Vector2 pose = TargetStandPosition ?? ctx.Npc.Center;
        if (!FindToolAccess.InReach(ctx.Npc.Center, b.Tile))
        {
            // Still flying to the pose. What is checked on the way is whether the bound tile can be reached
            // at all any more — the player walling it off mid-flight is a failed method, named, rather than a
            // body pressing at a wall until the course notices.
            if (ApproachVerdict(ctx, b.Tile) == Reachability.Reach.No)
            {
                Refuse(NoProvenPoseReason, AttemptStatus.Failed, NoProvenPoseReason);
                return PositionRequest.Hold;
            }
            status = "approaching";
            return PositionRequest.ExactAt(pose, b.Tile);
        }
        status = "mining";
        ctx.Companion.HoldItem(pickaxe.type);
        swinging = true;
        ctx.Companion.Motor.Face(b.Tile.X * 16f + 8f);
        ctx.Companion.ShowBeam(b.Tile.ToWorldCoordinates(8f, 8f));
        if (ctx.Companion.Miner.Swing(b.Tile, pickaxe))
        {
            ctx.Companion.StartAnimation(pickaxe.type, pickaxe.useAnimation);
            if (ctx.Companion.Miner.LastOutcome is { } outcome)
            {
                var owner = ctx.Companion.Brain.Activity;
                // The course's decision identity, the same `choice_id` the recorder's rows carry since schema
                // 0.43.0. `ChopTree` carries the whole account of why this is not `Chooser.EvaluationId`.
                Infrastructure.Diagnostics.GodsEyeEvents.RecordToolEffect(ctx.Npc, "pickaxe", outcome, ctx.Companion.Brain.Course.DecisionId, owner.Id, owner.AttemptOpen ? owner.AttemptId : 0);
                if (outcome.Effect == Infrastructure.Interactions.TileToolEffect.Removed
                    && outcome.Before.Type == jobType && jobTiles.Contains(outcome.Target))
                    ownRemovals.Add(outcome.Target);
                if (outcome.Productive) ctx.Companion.Brain.Activity.RecordWork(b.Tile.ToWorldCoordinates());
            }
        }
        return PositionRequest.Hold;
    }

    /// <summary>
    /// Whether the bound tile may still be struck, asked in the order a player would give up on it: the work
    /// was switched off, Mimic has nothing to mimic, the tile is gone or has changed, the list now leaves it, it
    /// sits in a protected home, the pickaxe cannot damage it, or it has left the allowance. Each is observed
    /// rather than inferred, so each is a proven refusal with its own name.
    /// </summary>
    private (string Cause, AttemptStatus Status, string Text)? RefusalAtTheSwing(in ActionContext ctx, (Point Tile, int Material) b, int pickPower)
    {
        if (WorkPolicies.Mining == WorkPolicy.Disabled)
        {
            if (jobId > 0) ClearJob("disabled before execution");
            return ("work-disabled", AttemptStatus.Invalid, "disabled before execution");
        }
        if (WorkPolicies.Mining == WorkPolicy.Mimic && !PlayerIsMining(ctx))
        {
            if (jobId > 0) ClearJob("mimic trigger expired");
            return ("mimic-awaiting-player-ore-contact", AttemptStatus.Invalid, "mimic trigger expired");
        }
        Tile tile = Main.tile[b.Tile.X, b.Tile.Y];
        if (!OreFinder.IsOre(b.Tile.X, b.Tile.Y))
        {
            patch.Remove(b.Tile);
            return ("bound-tile-no-longer-ore", AttemptStatus.Invalid, "bound tile no longer ore");
        }
        if (tile.TileType != b.Material)
        {
            patch.Remove(b.Tile);
            return ("bound-tile-material-changed", AttemptStatus.Invalid, "bound tile material changed");
        }
        if (!WorkPolicies.MinesOre(b.Material))
        {
            if (jobId > 0) ClearJob(LeftByMiningList);
            return ("ore-left-by-mining-list", AttemptStatus.Invalid, LeftByMiningList);
        }
        if (Infrastructure.Interactions.WorldProtection.ProtectCompanionHomes.IsProtected(b.Tile))
            return ("bound-tile-in-protected-home", AttemptStatus.Invalid, "bound tile in protected home");
        if (!ctx.Companion.Miner.CanMine(b.Tile, pickPower))
            return ("pickaxe-cannot-damage", AttemptStatus.Invalid, "no mineable ore");
        if (!AllowsTarget(ctx, b.Tile.ToWorldCoordinates()))
            return ("bound-tile-outside-allowance", AttemptStatus.Invalid, "outside activity range or protected home");
        return null;
    }

    private static bool PlayerIsMining(in ActionContext ctx)
        => ctx.Senses.Player.MinedOre != null || TileDamageWatcher.TicksSinceOreHit <= MimicContactTicks;

    /// <summary>Whether any free cell still reaches the bound tile, reused while the body's tile, the terrain and
    /// the reach are unchanged. It never picks a different tile: its only use is to refuse this one.</summary>
    private Reachability.Reach ApproachVerdict(in ActionContext ctx, Point tile)
    {
        var key = (tile, MovementQueries.Tile(ctx.Npc.Center), TerrainChanges.Revision, FindToolAccess.Reach);
        if (approachProof is { } proof && (proof.Tile, proof.Body, proof.Revision, proof.Reach) == key) return proof.Verdict;
        var verdict = FindToolAccess.Approach(tile, ctx.Npc.Center, ctx.Senses.Reach, out _);
        approachProof = (key.tile, key.Item2, key.Revision, key.Reach, verdict);
        return verdict;
    }

    /// <summary>Refuse the bound step by name: the attempt records why, the admission is released, and the next
    /// selection closes the attempt with that cause even if the course binds this vein again.</summary>
    private void Refuse(string cause, AttemptStatus attemptStatus, string text)
    {
        attemptSetback = (attemptStatus, cause);
        status = text;
        refusals++;
        RefuseStep(cause);
        ReleaseActivity();
        Classify(cause is "work-disabled" or "mimic-awaiting-player-ore-contact" or "ore-left-by-mining-list"
            ? OfferEligibility.PolicyForbidden : OfferEligibility.KnownUnusable, cause);
    }

    private void ClearJob(string reason)
    {
        if (jobId > 0)
        {
            LastConclusion = DescribeOreJobEnd.Capture(jobId, reason, jobTiles, jobType, ownRemovals.Count);
            conclusionSequence++;
        }
        jobTiles.Clear();
        ownRemovals.Clear();
        patch.Clear();
        jobId = 0;
        refusals = 0;
        status = reason;
        ReleaseActivity();
    }

    public override void Exit(in ActionContext ctx)
    {
        // The vein's account survives interruption; the step does not, because the course binds a fresh one.
        swinging = false;
        bound = null;
        status = "resume requires a course step";
    }
}
