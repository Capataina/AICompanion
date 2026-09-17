#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Activities.Combat.Planning;

/// <summary>
/// Why a stand was proposed. Phase D proposes only today's stands — where the body is and the two
/// stands today's activity would take — so only those reasons exist; phase E's generators add theirs.
/// The reason travels into the record so an audit can say which generator a better stand needed.
/// </summary>
public enum StandReason
{
    /// <summary>Fighting from where the body already hovers: no travel, no arrival wait.</summary>
    Here,
    /// <summary>Today's guard stand: the leashed point between the player and the threat.</summary>
    GuardAnchor,
    /// <summary>Today's hunt stand: the positioner's line-of-fire resolution toward the target.</summary>
    HuntApproach,
    /// <summary>One cell of the audit's exhaustive grid: never proposed live, only ever re-searched.</summary>
    AuditGrid,
}

/// <summary>Why a segment ends: its targets died, the next segment is worth more, or the horizon ran out.</summary>
public enum SegmentEnd
{
    TargetsDead,
    NextSegmentWorthMore,
    Horizon,
}

/// <summary>
/// One use the plan fires: the weapon slot, the muzzle, aim and launch direction the simulation priced,
/// the tick to fire it, and the target slot. The launch travels with the aim so a re-evaluation against a
/// new forecast re-flies the same use rather than re-solving it.
/// </summary>
public readonly record struct PlannedUse(int WeaponSlot, Vector2 Muzzle, Vector2 AimPoint, Vector2 LaunchDirection, int FireTick, int TargetSlot);

/// <summary>
/// One stand proposed for verdict: where, why, which weapon it serves when it serves one, and the hostile
/// slots it means to engage. Deduplicated to half a tile before verdicts, so two generators naming the same
/// rock do not price it twice.
/// </summary>
public readonly record struct StandProposal(Vector2 Stand, StandReason Reason, int WeaponSlot, int[] TargetSlots);

/// <summary>
/// What the plan was admitted against: the terrain it was searched on, the learner's revision, the targets
/// with their spawn generations, the highest urgency to either body it saw, the intent region it priced
/// company against with the gap it admitted, and the search tick, which is the progress the commitment seeds
/// its own clock from — later progress lives on the commitment, never on this immutable product. The commitment holds while
/// all of these still describe the world; any one failing re-searches rather than re-scores, because a
/// destination is kept by membership, never by a bonus. The learner's revision is admitted because a plan
/// that outlives what a shot just taught visibly ignores its own evidence: three seconds of firing a weapon
/// the last volley proved wrong.
/// </summary>
public sealed record PlanValidity(int TerrainRevision, int KnowledgeRevision, (int Slot, int Generation)[] Targets,
    float AdmittedMaxUrgency, Vector2 RegionCentre, Vector2 RegionHalfSize, float AdmittedCompanyGap,
    int LastProgressTick);

/// <summary>
/// One segment of a plan: the stand held while its uses fire, the verdict that admitted it, when the body
/// arrives and when the segment's firing starts and ends, the uses in fire order, and why the segment ends.
/// The next segment begins when the body arrives at its stand, or later if waiting for an effect of the
/// previous segment is worth more. Uses during travel between stands are fired from points sampled along the
/// straight line at the weapon's use cadence; the positioner's route is not searched per plan, so the record
/// names that approximation where it matters.
/// </summary>
public sealed record AttackSegment(StandProposal Stand, Infrastructure.Position.StandVerdict Verdict,
    int ArriveTick, int StartTick, int EndTick, PlannedUse[] Uses, SegmentEnd EndsWhen);

/// <summary>
/// A plan: a short timeline of segments, its objective vector, its weighted value under the weights that
/// chose it, what it was admitted against, and whether the budget cut its search. Depth one in phase D —
/// one segment — so commitment and the offer run on the vector before the timed sequences of phase E.
/// TargetKillTicks carries the evaluator's predicted kill tick per target slot, absolute, for the threat
/// sense's intervention estimate: protection prices the fight the plan performs.
/// </summary>
public sealed record AttackPlan(int Id, AttackSegment[] Segments, CombatOutcome Outcome, float Weighted,
    PlanValidity Validity, bool BudgetCut, (int Slot, int Tick)[]? TargetKillTicks = null)
{
    /// <summary>The segment the body is working now: the first whose end has not passed.</summary>
    public AttackSegment Current(int tick)
    {
        foreach (AttackSegment segment in Segments)
            if (tick < segment.EndTick)
                return segment;
        return Segments[^1];
    }

    /// <summary>The plan's primary target: the first segment's first use's target, or -1 with no uses.</summary>
    public int PrimaryTarget => Segments.Length > 0 && Segments[0].Uses.Length > 0 ? Segments[0].Uses[0].TargetSlot : -1;
}

/// <summary>One proposal with the verdict the search read: the snapshot carries the whole assessed set,
/// so the audit replays the decision against the same reach answers rather than a fresh flood.</summary>
public readonly record struct AssessedStand(StandProposal Proposal, Infrastructure.Position.StandVerdict Verdict);

/// <summary>
/// One plan the search turned down: whether the dominance filter dropped it or the weights did, and the
/// objective it lost on — the dominator's widest win for a drop, the weighted gap's largest term for a
/// front survivor the argmax passed over. The record carries the three best, so a decision names its
/// nearest alternatives rather than only its winner.
/// </summary>
public sealed record RejectedPlan(AttackPlan Plan, string Reason, string LostOn);
