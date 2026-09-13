#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Activities.Combat;

/// <summary>
/// Go and kill a reachable hostile. Scores by having a target the weapons can engage
/// and by the player being safe enough to leave; the forecast is the trip to a firing
/// spot, which the chooser charges against the horizon. Distance is charged there and
/// only there: an enemy anywhere on screen is worth the full hunt, because a companion
/// that keeps chopping while a zombie walks across the screen reads as not having seen
/// it, and only a target beyond the screen loses value with range. A second enemy
/// appearing does not end a hunt; only the danger and horizon it changes can.
/// </summary>
public sealed class PursueAttackOpportunity : CompanionAction
{
    public override string Name => "hunt";
    public override PurposeFamily Family => PurposeFamily.Combat;
    public override bool IsExcursion => !localHunt;

    public ThreatRecord? Target { get; private set; }
    public override Vector2? ActivityTarget => prepared?.Bottom;
    public override object? ActivityIdentity => Target?.Npc;
    public override PositionRequest? PreparedPositionRequest => prepared is { } candidate
        ? new PositionRequest(RequestKind.LineOfFire, candidate.Centre, candidate.Enemy) : null;
    public int NoProgressTicks { get; private set; }
    public string LastRejection { get; private set; } = "none";
    private Vector2 engagementOrigin;
    private int observedTarget = -1, observedGeneration, observedLife;
    private readonly System.Collections.Generic.Dictionary<(int slot, int generation), (int until, Vector2 target, Vector2 body, int terrain)> deferred = new();
    /// <summary>Every enemy this stalled stretch has aimed at, so the deferral covers the set that produced the stall.</summary>
    private readonly System.Collections.Generic.Dictionary<(int slot, int generation), Vector2> stalled = new();

    public override void ObserveOutcome(in ActionContext ctx)
    {
        if (Target == null) { NoProgressTicks = 0; stalled.Clear(); return; }
        NPC enemy = Target.Npc;
        int generation = HostileAttackSources.Generation(enemy);
        // Hands choose independently of pursuit. An incidental shot cannot establish access
        // to this enemy, and cooldown describes waiting rather than a new attack. Damage is
        // compared only within one generation; changing targets cannot reset a stalled hunt.
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

    // Attempt-local evidence, written only while the hunt executes and cleared when an attempt opens.
    private (NPC Enemy, int Generation)? pursued;
    private bool attacked, failed;

    public override void BeginAttempt()
    {
        pursued = null;
        attacked = failed = false;
    }

    /// <summary>The pursued generation disappearing after this attempt fired at it completes the hunt,
    /// unattributed because the native death hook does not name a killer; disappearing before any
    /// pursuit shot leaves the attempt invalid. An expired progress window is failure. Incidental shots
    /// at other enemies are not pursuit evidence here, as in progress accounting.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        if (pursued is { } target
            && (!target.Enemy.active || target.Enemy.life <= 0 || HostileAttackSources.Generation(target.Enemy) != target.Generation))
            return attacked
                ? new(AttemptStatus.Complete, "pursued-target-gone-after-pursuit-attack", AttemptAttribution.Unattributed)
                : new(AttemptStatus.Invalid, "pursued-target-gone-before-pursuit-attack");
        if (failed)
            return new(AttemptStatus.Failed, "no-movement-or-attack-progress");
        return attacked
            ? new(AttemptStatus.Partial, "attacked-pursued-target-still-present")
            : new(AttemptStatus.Attempted, "replaced-before-attacking-pursued-target");
    }

    private readonly record struct Candidate(NPC Enemy, int Generation, Vector2 Bottom, Vector2 Centre, float Value, float TripTicks);
    private Candidate? prepared;
    private bool localHunt;

    public override void Prepare(in ActionContext ctx)
    {
        float value = DiscoverValue(ctx);
        float trip = value > 0 && Target is { } found
            ? (verdict == FiringAccess.FromHere ? 0f
                : MathF.Max(0f, found.DistanceToCompanion - 200f) / Companion.CompanionMotor.WalkSpeed + 60f)
            : 0f;
        localHunt = value > 0 && (verdict == FiringAccess.FromHere || trip <= Weights.HuntLocalTripTicks);
        prepared = value > 0 && Target is { } ready
            ? new(ready.Npc, HostileAttackSources.Generation(ready.Npc), ready.Npc.Bottom, ready.Npc.Center, value, trip)
            : null;
    }

    public override float Score() => prepared?.Value ?? 0f;

    private float DiscoverValue(in ActionContext ctx)
    {
        if (!PlayerIntegration.CompanionPreferences.Current.Hunting)
        {
            Target = null;
            Classify(OfferEligibility.PolicyForbidden, "hunting-disabled");
            return 0f;
        }
        Rectangle screen = ScreenWithMargin();
        Target = PickTarget(ctx, screen);
        if (Target == null)
        {
            // A refusal with a named cause is a known-unusable method; no candidate at all is absence.
            // An unfinished firing search is neither: it is not a plan, and it is not a proven absence.
            if (LastRejection == "firing-position-undecided")
                Classify(OfferEligibility.Unresolved, LastRejection);
            else
                Classify(LastRejection is "no-reachable-firing-position" or "engagement-deferred-no-progress"
                    ? OfferEligibility.KnownUnusable : OfferEligibility.NoOpportunity, LastRejection);
            return 0f;
        }
        if (ctx.Senses.Player.IsDead)
        {
            Classify(OfferEligibility.NoOpportunity, "player-dead");
            return 0f;
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
        if (leash == 0f) Classify(OfferEligibility.PolicyForbidden, "outside-activity-allowance");
        else Classify(OfferEligibility.Usable, verdict switch
        {
            FiringAccess.FromHere => "shot-from-current-position",
            FiringAccess.AfterMoving => "reachable-firing-position",
            _ => "firing-position-undecided",
        });
        return near * worth * leash * ownSkin * shot;
    }

    private FiringAccess verdict = FiringAccess.Unknown;

    /// <summary>The companion's shared firing-opportunity query; guarding reads the same answers through it.</summary>
    private readonly ResolveFiringOpportunity firingAccess;

    /// <summary>A hunt that owns its own query, for callers that construct one activity alone.</summary>
    public PursueAttackOpportunity() : this(new ResolveFiringOpportunity()) { }

    public PursueAttackOpportunity(ResolveFiringOpportunity firingAccess) => this.firingAccess = firingAccess;

    /// <summary>The arsenal's delayed attack value and the reposition wait of the target this preparation chose.</summary>
    public float PursuitValue { get; private set; }
    public float PursuitAccessTicks { get; private set; }

    /// <summary>Every candidate examined by the last preparation, as slot:generation:verdict:access-ticks:value, in examination order.</summary>
    public string PursuitEvidence { get; private set; } = "";

    private static Rectangle ScreenWithMargin()
        => new((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200, Main.screenWidth + 400, Main.screenHeight + 400);

    public override float ForecastTicks()
        => prepared?.TripTicks ?? 0f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (prepared is not { } offer || !offer.Enemy.CanBeChasedBy()
            || HostileAttackSources.Generation(offer.Enemy) != offer.Generation)
            return PositionRequest.Hold;
        // No firing here. Shooting is not something a mode does, it is what the hands do every
        // tick whatever the feet were told, so it lives in the brain's own tick; hunting is now
        // only the decision to walk toward something. See Brain.Engage.
        return new PositionRequest(RequestKind.LineOfFire, offer.Centre, offer.Enemy);
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
    /// Existence is the arsenal's real forecast, not a straight ray. Hunt does not pick the pair the
    /// hands will fire; it walks toward a pose where that chooser's outcome value is highest, re-ranked
    /// every preparation. An unfinished flood that has already found a solvable stand is a hunt; an
    /// unfinished flood that has found no arc is not. Bounded on purpose: a crowd where nothing is
    /// engageable must not turn one tick into a search.
    /// </summary>
    private const int MaxFiringChecksPerTick = 3;
    private readonly System.Collections.Generic.HashSet<int> examined = new();
    private readonly System.Collections.Generic.List<(int Slot, int Generation, Vector2 Centre)> examinedAdmissible = new();

    private ThreatRecord? PickTarget(in ActionContext ctx, Rectangle screen)
    {
        // Reused rather than allocated, because this runs on every tick of every hunt.
        examined.Clear();
        examinedAdmissible.Clear();
        bool refusedForFiring = false;
        bool undecidedFiring = false;
        ThreatRecord? chosen = null, firstAdmissible = null;
        FiringAccess chosenVerdict = FiringAccess.None, firstVerdict = FiringAccess.None;
        float chosenValue = 0f, chosenAccess = 0f, firstAccess = 0f;
        var evidence = new System.Text.StringBuilder();
        for (int attempt = 0; attempt < MaxFiringChecksPerTick; attempt++)
        {
            ThreatRecord? candidate = BestCandidate(ctx, screen, examined);
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
                continue;
            }
            if (opportunity == FiringAccess.Unknown)
            {
                undecidedFiring = true;
                continue;
            }
            examinedAdmissible.Add((candidate.Npc.whoAmI, HostileAttackSources.Generation(candidate.Npc), candidate.Npc.Center));
            if (firstAdmissible == null) { firstAdmissible = candidate; firstVerdict = opportunity; firstAccess = access; }
            if (value > chosenValue) { chosen = candidate; chosenVerdict = opportunity; chosenValue = value; chosenAccess = access; }
        }
        PursuitEvidence = evidence.ToString();
        if (chosen == null && firstAdmissible != null)
        {
            chosen = firstAdmissible; chosenVerdict = firstVerdict; chosenAccess = firstAccess;
        }
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
        if (refusedForFiring)
            LastRejection = "no-reachable-firing-position";
        else if (undecidedFiring)
            LastRejection = "firing-position-undecided";
        verdict = FiringAccess.None;
        return null;
    }

    private ThreatRecord? BestCandidate(in ActionContext ctx, Rectangle screen, System.Collections.Generic.HashSet<int> unshootable)
    {
        LastRejection = "no-eligible-target";
        var expired = new System.Collections.Generic.List<(int slot, int generation)>();
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
                if (unchanged) { LastRejection = "engagement-deferred-no-progress"; continue; }
                deferred.Remove(key);
            }
            if (!AllowsTarget(ctx, t.Npc.Bottom, t.Npc)) continue;
            if (!t.Npc.CanBeChasedBy()) continue;
            if (!t.CanReachEither && !t.Npc.Hitbox.Intersects(screen))
                continue;
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
}
