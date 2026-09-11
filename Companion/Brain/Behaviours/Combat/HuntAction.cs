#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.PositionSelection;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.Behaviours.Combat;

/// <summary>
/// Go and kill a reachable hostile. Scores by having a target the weapons can engage
/// and by the player being safe enough to leave; the forecast is the trip to a firing
/// spot, which the chooser charges against the horizon. Distance is charged there and
/// only there: an enemy anywhere on screen is worth the full hunt, because a companion
/// that keeps chopping while a zombie walks across the screen reads as not having seen
/// it, and only a target beyond the screen loses value with range. A second enemy
/// appearing does not end a hunt; only the danger and horizon it changes can.
/// </summary>
public sealed class HuntAction : CompanionAction
{
    public override string Name => "hunt";

    public ThreatRecord? Target { get; private set; }
    public override Vector2? ActivityTarget => Target?.Npc.Bottom;
    public override object? ActivityIdentity => Target?.Npc;
    public int NoProgressTicks { get; private set; }
    public string LastRejection { get; private set; } = "none";
    private Vector2 engagementOrigin;
    private int observedTarget = -1, observedGeneration, observedLife;
    private readonly System.Collections.Generic.Dictionary<(int slot, int generation), (int until, Vector2 target, Vector2 body, int terrain)> deferred = new();
    /// <summary>Every enemy this stalled stretch has aimed at, so the deferral covers the set that produced the stall.</summary>
    private readonly System.Collections.Generic.Dictionary<(int slot, int generation), Vector2> stalled = new();

    public void ObserveOutcome(in ActionContext ctx)
    {
        if (Target == null) { NoProgressTicks = 0; stalled.Clear(); return; }
        NPC enemy = Target.Npc;
        int generation = HostileAttackSources.Generation(enemy);
        // Changing target is not progress, and treating it as progress is what made this whole
        // guard inert. With a crowd on screen the pick alternates between two enemies whose
        // scores sit within noise of each other (observed 2026-09-11: Red Slime, Blue Jellyfish,
        // Red Slime), so the old identity clause reset the counter on nearly every tick and the
        // window never closed — 329 consecutive stationary ticks inside a guard built to stop it
        // after a fraction of that. Only damage to the enemy we were already on, a shot, or the
        // body actually covering ground is progress; a life comparison across two different
        // enemies compares nothing, so it is read only while the identity holds.
        bool sameTarget = observedTarget == enemy.whoAmI && observedGeneration == generation;
        bool progress = (sameTarget && enemy.life < observedLife)
            || ctx.Companion.Arsenal.LastFireOutcome is "fired" or "cooldown"
            || Vector2.DistanceSquared(engagementOrigin, ctx.Npc.Bottom) >= Weights.ObjectiveProgressPixels * Weights.ObjectiveProgressPixels;
        // Every enemy aimed at during the stalled stretch, so the deferral covers the set rather
        // than whichever one happened to be selected on the tick the window closed. Deferring only
        // that one hands each member of an alternating pair a fresh window, which is the same
        // defect one step further out: two targets would buy twice the window and nothing else.
        stalled[(enemy.whoAmI, generation)] = enemy.Center;
        observedTarget = enemy.whoAmI; observedGeneration = generation; observedLife = enemy.life;
        if (progress)
        {
            NoProgressTicks = 0; engagementOrigin = ctx.Npc.Bottom;
            stalled.Clear();
        }
        else if (++NoProgressTicks >= Weights.ObjectiveProgressWindowTicks)
        {
            foreach (var (key, centre) in stalled)
                deferred[key] = (ctx.Senses.Tick + Weights.HuntRetryTicks, centre, ctx.Npc.Bottom,
                    SharedMovementSystem.TerrainChanges.Revision);
            LastRejection = "no-movement-or-attack-progress";
            NoProgressTicks = 0;
            stalled.Clear();
        }
    }

    public override float Score(in ActionContext ctx)
    {
        if (!PlayerIntegration.CompanionPreferences.Current.Hunting) { Target = null; return 0f; }
        Rectangle screen = ScreenWithMargin();
        Target = PickTarget(ctx, screen);
        if (Target == null || ctx.Senses.Player.IsDead)
            return 0f;
        float safe = Consideration.AtLeast(1f - ctx.Senses.Threats.PlayerDanger, 0.1f);
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
        float ownSkin = Consideration.AtLeast(1f - ctx.Senses.Threats.CompanionDanger, 0.05f);
        // Whether a shot is possible at all was absent from this product, so hunting something
        // unhittable scored exactly as well as hunting something killable and the companion spent
        // its day walking at enemies it could not harm. It is graded rather than binary: a target
        // it can already hit is worth more than one it must walk to, and one it must walk to is
        // worth more than one nothing has established yet. Only a proven absence vetoes, and that
        // veto is what stops the hunt being started at all.
        float shot = verdict switch
        {
            Firing.FromHere => 1f,
            Firing.AfterMoving => Weights.HuntRepositionShot,
            Firing.Unknown => Weights.HuntUnprovenShot,
            _ => 0f,
        };
        return safe * near * worth * leash * ownSkin * shot;
    }

    /// <summary>
    /// What the companion would have to do to land a shot on an enemy. Three-and-a-bit valued for
    /// the same reason <see cref="SharedMovementSystem.Reachability.Reach"/> is: a bounded flood
    /// that has not yet grown as far as the target says nothing about whether a firing spot exists
    /// there, and collapsing that into "no" refuses targets for being newly noticed.
    /// </summary>
    private enum Firing
    {
        /// <summary>A weapon already solves a shot from where the companion stands.</summary>
        FromHere,
        /// <summary>Somewhere the walker can reach has a line to the enemy; the hunt is the walk to it.</summary>
        AfterMoving,
        /// <summary>Sighted standing spots exist but the reachable region has not settled, so nothing is established.</summary>
        Unknown,
        /// <summary>No standing position with a line to the enemy exists within weapon reach at all.</summary>
        None,
    }

    private Firing verdict = Firing.Unknown;
    private readonly System.Collections.Generic.Dictionary<(int slot, int generation), (int at, Point origin, int terrain, Firing verdict)> firing = new();
    private const int FiringCacheTicks = 20;
    private const int FiringSampleRadiusTiles = 14;
    // Every other tile. A standable row is one tile high, so a stride this coarse can miss a narrow
    // ledge; that costs an Unknown or a missed opportunity, never a wrong refusal, because a missed
    // sighted tile can only make the answer more cautious.
    private const int FiringSampleStride = 2;

    /// <summary>
    /// Whether a standing position exists that the walker can reach and from which a weapon has a
    /// line to this enemy — the owner's rule, in his words: can I hit it, if not can I move to hit
    /// it, and if neither then it is not worth hunting.
    ///
    /// The sight test is the same straight ray the positioner ranks candidates with, not a full
    /// trajectory solve, because this runs per target and a solve costs up to 48 arcs. It is a
    /// lower bound on an arcing shot, so it under-reports opportunities and never invents one;
    /// the real solve still happens in position selection, on the spot this admits.
    ///
    /// Applying the from-here test alone would have been the obvious change and the wrong one: it
    /// rejects every enemy the companion would simply have had to walk toward, which is most of them.
    /// </summary>
    private Firing FiringOpportunity(in ActionContext ctx, NPC enemy)
    {
        var key = (enemy.whoAmI, HostileAttackSources.Generation(enemy));
        Point origin = SharedMovementSystem.MovementQueries.FeetTile(ctx.Npc.Bottom);
        int terrain = SharedMovementSystem.TerrainChanges.Revision;
        if (firing.TryGetValue(key, out var cached) && cached.origin == origin && cached.terrain == terrain
            && unchecked(ctx.Senses.Tick - cached.at) < FiringCacheTicks)
            return cached.verdict;

        Firing answer = Resolve(ctx, enemy);
        firing[key] = (ctx.Senses.Tick, origin, terrain, answer);
        return answer;
    }

    private static Firing Resolve(in ActionContext ctx, NPC enemy)
    {
        if (ctx.Companion.Arsenal.CanEngage(ctx, enemy))
            return Firing.FromHere;
        var positioner = ctx.Companion.Brain.Positioner;
        float reach = MathF.Max(ctx.Companion.Arsenal.Primary.Profile.Reach, ctx.Companion.Arsenal.Secondary.Profile.Reach);
        // Never wider than the box position selection itself samples: a spot it cannot propose is
        // not a spot the hunt can be walked to, so admitting a target on one would promise a
        // position that never arrives.
        int radius = Math.Min(FiringSampleRadiusTiles, (int)(reach / 16f));
        Point centre = SharedMovementSystem.MovementQueries.FeetTile(enemy.Bottom);
        bool sightedButUnsettled = false;
        for (int dx = -radius; dx <= radius; dx += FiringSampleStride)
        {
            for (int dy = -radius; dy <= radius; dy += FiringSampleStride)
            {
                var tile = new Point(centre.X + dx, centre.Y + dy);
                if (!SharedMovementSystem.MovementQueries.IsStandable(tile.X, tile.Y))
                    continue;
                Vector2 eye = SharedMovementSystem.MovementQueries.FeetWorld(tile) + new Vector2(0f, -30f);
                if (Vector2.Distance(eye, enemy.Center) > reach)
                    continue;
                if (!Terraria.Collision.CanHitLine(eye, 1, 1, enemy.position, enemy.width, enemy.height))
                    continue;
                if (positioner.Reaches(tile))
                    return Firing.AfterMoving;
                // Sighted, and the flood has not proved it either way. Only an exhausted region
                // turns that into an absence; until then it is the search declining to answer.
                if (!positioner.ReachComplete)
                    sightedButUnsettled = true;
            }
        }
        return sightedButUnsettled ? Firing.Unknown : Firing.None;
    }

    private static Rectangle ScreenWithMargin()
        => new((int)Main.screenPosition.X - 200, (int)Main.screenPosition.Y - 200, Main.screenWidth + 400, Main.screenHeight + 400);

    public override float ForecastTicks(in ActionContext ctx)
        => Target == null ? 0f : MathF.Max(0f, Target.DistanceToCompanion - 200f) / Companion.CompanionMotor.WalkSpeed + 60f;

    public override PositionRequest Execute(in ActionContext ctx)
    {
        if (Target == null)
            return PositionRequest.Hold;
        // No firing here. Shooting is not something a mode does, it is what the hands do every
        // tick whatever the feet were told, so it lives in the brain's own tick; hunting is now
        // only the decision to walk toward something. See Brain.Engage.
        return new PositionRequest(RequestKind.LineOfFire, Target.Npc.Center, Target.Npc);
    }

    /// <summary>
    /// Threats endangering the player first, then the nearest one a weapon can reach — and then,
    /// among those, the best one an actual standing position can shoot. A target with no reachable
    /// firing position is passed over rather than selected and stood at, which is the difference
    /// between choosing a fight and discovering one cannot be had.
    ///
    /// Bounded on purpose. Each pass over the threat list is cheap, but establishing a firing
    /// opportunity samples terrain, so a crowd where nothing is engageable must not turn one tick
    /// into a search: after a few refusals the action yields the tick and the chooser picks
    /// something useful instead, which is the outcome a hopeless crowd should produce anyway.
    /// </summary>
    private const int MaxFiringChecksPerTick = 3;

    private ThreatRecord? PickTarget(in ActionContext ctx, Rectangle screen)
    {
        var unshootable = new System.Collections.Generic.HashSet<int>();
        bool refusedForFiring = false;
        for (int attempt = 0; attempt < MaxFiringChecksPerTick; attempt++)
        {
            ThreatRecord? candidate = BestCandidate(ctx, screen, unshootable);
            if (candidate == null)
                break;
            Firing opportunity = FiringOpportunity(ctx, candidate.Npc);
            if (opportunity != Firing.None)
            {
                verdict = opportunity;
                LastRejection = "accepted";
                return candidate;
            }
            unshootable.Add(candidate.Npc.whoAmI);
            refusedForFiring = true;
        }
        // The reason is set after the loop rather than inside it, because each pass over the threat
        // list resets it, so a reason written during one pass is erased by the next and every
        // refusal would report the generic "nothing eligible" instead of the one a session is read
        // for. Whether the companion had nowhere to shoot from is exactly what needs to survive.
        if (refusedForFiring)
            LastRejection = "no-reachable-firing-position";
        verdict = Firing.None;
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
                bool unchanged = ctx.Senses.Tick < failure.until && failure.terrain == SharedMovementSystem.TerrainChanges.Revision
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
