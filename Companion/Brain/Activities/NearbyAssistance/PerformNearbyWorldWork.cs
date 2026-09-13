#nullable enable
using FindToolAccess = AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Infrastructure.Position;
using AICompanion.Companion.Brain.Infrastructure.Movement;

using AICompanion.Companion.Brain.Activities;

namespace AICompanion.Companion.Brain.Activities.NearbyAssistance;

/// <summary>One bounded interaction search shared by pot breaking and permanent lighting.</summary>
public abstract class PerformNearbyWorldWork : CompanionAction
{
    public override Infrastructure.Selection.PurposeFamily Family => Infrastructure.Selection.PurposeFamily.NearbyAssistance;
    protected Point? target;
    private Vector2 stand;
    private ulong nextSearch, retryAfter;
    private bool eligibleLastObservation;
    private bool needsJump, jumped;
    private ulong jumpStarted;
    // Tiles whose approach was tried and did not arrive, with the tick they may be offered again.
    private readonly System.Collections.Generic.Dictionary<Point, ulong> deferred = new();
    private Vector2 approachOrigin;
    private int approachTicks;
    private (int X, int Y) derivedReach;
    private Vector2? preparedTarget;
    private float preparedValue;
    private bool enabledAtPreparation;
    // The last way this method gave up on a target during the current attempt, so the attempt is
    // concluded from what happened to it rather than from the absence of a target afterwards.
    private string? release;
    public override Vector2? ActivityTarget => preparedTarget;
    public override object? ActivityIdentity => target;
    protected abstract bool Enabled(in ActionContext ctx);
    protected abstract bool Candidate(in ActionContext ctx, Point tile);
    protected abstract bool Perform(in ActionContext ctx, Point tile);
    protected abstract float Utility { get; }
    protected virtual bool AllowJump => false;
    /// <summary>Whether a productive interaction keeps this method's job rather than ending it. Lighting a dark
    /// region takes several torches, so it re-nominates from where it now stands instead of releasing the
    /// target and waiting out the search cadence; one pot is one pot, so pot collection does not.</summary>
    protected virtual bool ContinueAfterInteraction => false;
    /// <summary>Why this search found no site, where the subclass knows something more specific than "nothing
    /// in the window". Null keeps the shared answer.</summary>
    protected virtual (OfferEligibility Eligibility, string Reason)? SearchRefusal(in ActionContext ctx) => null;
    /// <summary>Whether the body can reach and return from this stand tile, answered from the reach sense
    /// rather than by a pair of fresh route searches. Null means the subclass has no opinion and the shared
    /// round-trip proof runs instead.</summary>
    protected virtual bool? StandReachable(in ActionContext ctx, Point stand) => null;
    /// <summary>The classification for a method its enabling conditions currently refuse; the
    /// subclass knows whether that refusal is a player setting, missing supplies or the world.</summary>
    protected virtual (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead") : (OfferEligibility.PolicyForbidden, "interaction-disabled");
    /// <summary>What one observed productive interaction means for this purpose.</summary>
    protected virtual string CompletedEffect => "interaction-effect-observed";
    /// <summary>How long a tile whose approach never arrived stays out of the candidate set. Long
    /// enough that the companion leaves the area and does something else, short enough that a tile
    /// made reachable by the player digging through becomes available again in the same visit.</summary>
    private const int DeferFailedApproachTicks = 1800;
    protected virtual float CandidateCost(Vector2 feet, Point tile) => Vector2.DistanceSquared(feet, tile.ToWorldCoordinates());

    /// <summary>Tiles this search will consider, nearest-first by <see cref="CandidateCost"/>. Pots keep a small
    /// window around the body; lighting overrides this to the visible screen of dark air.</summary>
    protected virtual void GatherSearchTiles(in ActionContext ctx, System.Collections.Generic.List<(float Cost, int Order, Point Tile)> into)
    {
        Point centre = ctx.Npc.Center.ToTileCoordinates();
        for (int x = centre.X - 18; x <= centre.X + 18; x++)
            for (int y = centre.Y - 14; y <= centre.Y + 14; y++)
            {
                Point p = new(x, y);
                if (deferred.TryGetValue(p, out ulong until) && Main.GameUpdateCount < until) continue;
                if (NoReturnDeferred(p)) continue;
                into.Add((CandidateCost(ctx.Npc.Bottom, p), into.Count, p));
            }
    }

    /// <summary>Whether this tile is currently deferred for a failed approach or a proven no-return trip.</summary>
    protected bool SearchTileDeferred(Point tile)
    {
        if (deferred.TryGetValue(tile, out ulong until) && Main.GameUpdateCount < until) return true;
        return NoReturnDeferred(tile);
    }

    // Search-window tiles in the order they are asked, reused across searches: the brain is single-threaded.
    private readonly System.Collections.Generic.List<(float Cost, int Order, Point Tile)> ordered = new();
    // Sites whose trip was proven to have no way back, or not enough breath, with the terrain revision and tick they may be
    // asked again. Without it the same nearest one-way site spends the bounded trip checks on every search and a farther site
    // with a way back is never reached.
    private readonly System.Collections.Generic.Dictionary<Point, (int Revision, ulong Until)> noReturn = new();
    // Why the last search found no site, when the reason was the trip rather than the absence of a candidate.
    private (OfferEligibility Eligibility, string Reason)? tripRefusal;

    /// <summary>
    /// Whether the companion can walk to a remote working pose and come back to where it stands, with breath for both legs.
    /// Reaching a pose is not a certificate of leaving it, and the walker search behind <see cref="FindToolAccess.Approach"/>
    /// runs under whichever one-way rule the previous tick's request left set: after a reunion tick it certified a site
    /// reached only by a drop, which the Exact request then refused to plan, so the trip stalled until its progress window
    /// deferred it. The round trip asks both legs under one rule of its own, so the answer does not depend on the last request.
    /// A proven No is deferred until the terrain changes or the wait passes; an undecided return is only skipped for this search.
    /// </summary>
    private bool TripReturns(in ActionContext ctx, Point tile, Vector2 pose)
    {
        var breath = ctx.Companion.Breath;
        var envelope = new Reachability.BreathEnvelope(breath.TicksLeft,
            CharacterBody.CompanionBreath.BreathMax * CharacterBody.CompanionBreath.BreathCDMax,
            CharacterBody.CompanionBreath.RecoverPerTick * CharacterBody.CompanionBreath.BreathCDMax);
        // The round trip holds the per-search allowance unlimited for both of its legs, so on its own it would run to whatever is
        // left of the tick's planning deadline; one trip is given what one navigator route search is given.
        using var allowance = LimitPlanningWork.Narrow(Infrastructure.Selection.Weights.RouteSearchMilliseconds);
        var trip = MovementQueries.RoundTrip(MovementQueries.FeetTile(ctx.Npc.Bottom), MovementQueries.FeetTile(pose), envelope);
        if (trip.Outward == Reachability.Reach.Yes && trip.Return == Reachability.Reach.Yes && trip.Breath == Reachability.Reach.Yes)
            return true;
        if (trip.Return == Reachability.Reach.No || trip.Breath == Reachability.Reach.No)
            DeferRefusedTrip(tile, trip.Return == Reachability.Reach.No ? "interaction-site-has-no-return" : "interaction-trip-exceeds-breath");
        else tripRefusal ??= (OfferEligibility.Unresolved, "interaction-trip-undecided");
        return false;
    }

    /// <summary>Keep a site whose trip was proven impossible (no way back, no breath, or no take-off for the only hop that could
    /// reach it) out of discovery until the terrain changes or the wait passes. A named reason also becomes the offer's refusal.</summary>
    private void DeferRefusedTrip(Point tile, string? reason)
    {
        if (noReturn.Count > 64) noReturn.Clear();
        noReturn[tile] = (TerrainChanges.Revision, Main.GameUpdateCount + (ulong)Infrastructure.Selection.Weights.NearbyWorkNoReturnRetryTicks);
        if (reason != null) tripRefusal = (OfferEligibility.KnownUnusable, reason);
    }

    private bool NoReturnDeferred(Point tile)
    {
        if (!noReturn.TryGetValue(tile, out var entry)) return false;
        if (entry.Revision == TerrainChanges.Revision && Main.GameUpdateCount < entry.Until) return true;
        noReturn.Remove(tile);
        return false;
    }

    /// <summary>A body counts as at rest on its take-off below this horizontal speed. The engine zeroes smaller speeds and the
    /// slowdown snaps to exactly zero, so this is a numerical tolerance, not a tunable; it matches MineOre's.</summary>
    private const float RestSpeed = 0.01f;
    /// <summary>The longest flight the interaction-jump proof simulates; it mirrors the step limit inside
    /// ProveInteractionJump.CanReach, and a jump still airborne past it was never going to deliver the interaction.</summary>
    private const int HopFlightTicks = 90;

    public override void Prepare(in ActionContext ctx)
    {
        preparedValue = DiscoverValue(ctx);
        preparedTarget = target?.ToWorldCoordinates();
        if (!enabledAtPreparation) { var (eligibility, reason) = DisabledOffer(ctx); Classify(eligibility, reason); }
        else if (target == null && tripRefusal is { } refusal) Classify(refusal.Eligibility, refusal.Reason);
        else if (target == null)
        {
            var (eligibility, reason) = SearchRefusal(ctx) ?? (OfferEligibility.NoOpportunity, "no-candidate-in-search-window");
            Classify(eligibility, reason);
        }
        else Classify(OfferEligibility.Usable, "reachable-interaction");
    }

    public override float Score() => preparedValue;

    /// <summary>One interaction is this purpose's whole job, so an observed productive effect
    /// completes it; a named give-up is a failed method unless the target itself stopped qualifying.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        // The companion's own native call produced the credited effect, so the completion is its own.
        if (productiveEffects > 0) return new(AttemptStatus.Complete, CompletedEffect, AttemptAttribution.Companion);
        if (release is string reason)
            return new(reason == "target-no-longer-candidate" ? AttemptStatus.Invalid : AttemptStatus.Failed, reason);
        return new(AttemptStatus.Attempted, "replaced-before-interaction");
    }

    public override void BeginAttempt() => release = null;

    private void Release(string reason) => release = reason;

    private bool RefreshEligibility(in ActionContext ctx)
    {
        if (!Enabled(ctx) || ctx.Player.dead)
        {
            target = null;
            eligibleLastObservation = false;
            return false;
        }
        // Changed policy or supplies invalidate a previously unavailable discovery result.
        // An unchanged eligible method still observes the normal bounded search cadence.
        if (!eligibleLastObservation) nextSearch = 0;
        eligibleLastObservation = true;
        return true;
    }

    private float DiscoverValue(in ActionContext ctx)
    {
        enabledAtPreparation = RefreshEligibility(ctx);
        if (!enabledAtPreparation) return 0f;
        // Everything this executor retains about a site was derived under the reach it was computed with: the held stand, the
        // wait before the next search, the failed-approach deferrals, and the proven refusals in noReturn (no way back and not
        // enough breath from the working pose that reach chose, and no take-off for a hop that reach needed). A smaller reach
        // walked to a stand it could no longer swing from; a larger one waited out the cadence, and kept refusing a site whose
        // pit-floor pose had no return although the larger reach works it from the rim. So a reach change releases every store
        // together and this preparation searches again. The capability is this one comparison: a capability added to it
        // releases all of them, where a key added to one store and not the other refuses under the old value for its hold time.
        if (FindToolAccess.Reach != derivedReach)
        {
            derivedReach = FindToolAccess.Reach;
            target = null;
            nextSearch = 0;
            deferred.Clear();
            noReturn.Clear();
        }
        if (target is Point old && (!Candidate(ctx, old) || !AllowsTarget(ctx, old.ToWorldCoordinates())))
        { target = null; }
        if (target == null && Main.GameUpdateCount >= nextSearch)
        {
            nextSearch = Main.GameUpdateCount + 90;
            tripRefusal = null;
            // Nearest first by the method's own cost, stopping at the first site whose whole trip is proven. This picks the same
            // site the earlier improve-on-best scan picked (ties keep scan order), and it lets the trip proof below be bounded:
            // each RoundTrip is two fresh route searches, so only a few are asked per search. A hop with no take-off is
            // deferred without spending that bound.
            ordered.Clear();
            GatherSearchTiles(ctx, ordered);
            ordered.Sort(static (a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.Order.CompareTo(b.Order));
            int tripsAsked = 0;
            foreach (var (_, _, p) in ordered)
            {
                if (!AllowsTarget(ctx, p.ToWorldCoordinates(), p) || !Candidate(ctx, p)) continue;
                Vector2 candidateStand;
                bool jump = false;
                if (FindToolAccess.InReach(ctx.Npc.Bottom, p)) candidateStand = ctx.Npc.Bottom;
                else if (AllowJump && ProveInteractionJump.CanReach(NavGrid.World, ctx.Companion.Motor.State, body => FindToolAccess.InReach(body.Feet, p)))
                { candidateStand = ctx.Npc.Bottom; jump = true; }
                else
                {
                    // The bound sits on the approach query, which is the expensive half and the one both
                    // proof routes pay. It used to sit on the round trip, and opting lighting into the reach
                    // sense therefore removed the only thing bounding this loop: a sensed site skipped the
                    // counter entirely, so a screen dark everywhere put every candidate through a fresh
                    // bounded A* and one preparation measured 37.6 ms against a twelve-millisecond tick.
                    // Charging the approach rather than the proof bounds both routes by what they actually
                    // cost, and it deliberately tightens the round-trip route too: a site whose approach
                    // cannot be searched was never going to have its trip proven either.
                    if (tripsAsked++ >= Infrastructure.Selection.Weights.NearbyWorkTripChecks) break;
                    var standing = FindToolAccess.Approach(p, ctx.Npc.Bottom, out candidateStand);
                    if (standing == Reachability.Reach.Unknown) continue;
                    // Only a site no standing pose reaches is hopped to, from a take-off the walker reaches: the same order and
                    // the same query mining uses for ceiling ore, so a take-off is admitted here exactly when it would be there.
                    if (standing == Reachability.Reach.No && !AllowJump) continue;
                    if (standing == Reachability.Reach.No)
                    {
                        var hop = FindToolAccess.HopApproach(p, ctx.Npc.Bottom, ctx.Companion.Motor.State, out candidateStand);
                        if (hop == Reachability.Reach.No) DeferRefusedTrip(p, null);
                        if (hop != Reachability.Reach.Yes) continue;
                        jump = true;
                    }
                    // RoundTrip is what the bound is for: two fresh route searches. A hop that never
                    // finds a take-off is deferred without spending it, so a wall of unreachable air
                    // cannot hide a proven pit whose refusal is the named one-way drop.
                    // The reach sense answers the round trip where a subclass opts into it: membership of the
                    // two-way region is what returnable means in this project — everywhere the body can go
                    // and come home from — and one flood has already answered it for every candidate, where
                    // RoundTrip is two fresh route searches per site and is bounded for exactly that reason.
                    // A subclass that has not opted in keeps the route searches, so this is not a change to
                    // pot collection dressed as a change to lighting.
                    if (StandReachable(ctx, MovementQueries.FeetTile(candidateStand)) is bool sensed)
                    {
                        if (!sensed) continue;
                    }
                    else
                    {
                        if (!TripReturns(ctx, p, candidateStand)) continue;
                    }
                }
                target = p; stand = candidateStand; needsJump = jump; jumped = false;
                approachOrigin = ctx.Npc.Bottom; approachTicks = 0;
                break;
            }
            // A search that could not answer waits a fraction of the time a search that answered "nothing
            // here" waits. The long wait exists so a fruitless search is not repeated every tick, and a
            // search whose evidence simply has not arrived yet is not that: the reach region is flooded
            // incrementally and takes a few rescores to settle after a world change, so the first searches
            // after one refuse on "not yet known" while the body keeps moving. Waiting the full cadence
            // then re-asks from wherever the companion has since wandered, which is how a proven site a few
            // tiles away becomes a different, worse site by the time anyone can prove anything about it.
            if (target == null && SearchRefusal(ctx) is { Eligibility: OfferEligibility.Unresolved })
                nextSearch = Main.GameUpdateCount + (ulong)Infrastructure.Selection.Weights.NearbyWorkUnresolvedRetryTicks;
        }
        if (deferred.Count > 0)
        {
            ulong now = Main.GameUpdateCount;
            foreach (Point expired in new System.Collections.Generic.List<Point>(deferred.Keys))
                if (now >= deferred[expired]) deferred.Remove(expired);
        }
        return target == null ? 0f : Utility;
    }
    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (!RefreshEligibility(ctx)) return PositionRequest.Hold;
        if (target is not Point tile) return PositionRequest.Hold;
        if (!Candidate(ctx, tile)) { target = null; Release("target-no-longer-candidate"); return PositionRequest.Hold; }
        if (!FindToolAccess.InReach(ctx.Npc.Bottom, tile))
        {
            BodyState live = ctx.Companion.Motor.State;
            if (needsJump && jumped)
            {
                // This method's own flight: the rising body is what reaches, so it is held; past the proof's own flight
                // length the jump was never going to deliver.
                if (Main.GameUpdateCount - jumpStarted > HopFlightTicks) { target = null; Release("interaction-jump-did-not-deliver"); }
                return PositionRequest.Hold;
            }
            // On the take-off's own feet tile, the hop is re-proved from the live pose before the jump: another behaviour may have
            // moved the body since the take-off was chosen. A body still sliding there fails the proof because its jump drifts, not
            // because the take-off is gone, so it holds until it is at rest. At rest with no proof left, the take-off itself is gone,
            // and the tile leaves discovery; without that, the next preparation re-proves the same take-off from rest, passes, and
            // offers it again for ever. The tile, not a body width, is the test: HopApproach proves a pose at its tile's centre, and a
            // pot take-off proved there failed from one tile short, 16 pixels away, which a body-width tolerance counted as arrived
            // and so declared lost before the walk ever reached it. Short of the tile the body keeps walking, and the progress window
            // below still ends a walk that cannot close.
            if (needsJump && live.OnGround && MovementQueries.FeetTile(ctx.Npc.Bottom) == MovementQueries.FeetTile(stand)
                && Vector2.DistanceSquared(ctx.Npc.Bottom, stand) <= BodyPhysics.Width * BodyPhysics.Width)
            {
                if (MathF.Abs(live.Vx) > RestSpeed) return PositionRequest.Hold;
                if (!ProveInteractionJump.CanReach(NavGrid.World, live, body => FindToolAccess.InReach(body.Feet, tile)))
                {
                    deferred[tile] = Main.GameUpdateCount + DeferFailedApproachTicks;
                    target = null; Release("interaction-jump-lost-take-off"); return PositionRequest.Hold;
                }
                jumped = true; jumpStarted = Main.GameUpdateCount;
                return PositionRequest.Hold with { JumpScale = 1f };
            }
            // Walking to a standing pose or to a take-off; airborne on the way (crossing a ledge or a gap) is still the walk.
            // The approach can fail, and until this existed nothing said so. Score() only ever
            // dropped a target that vanished or left the activity envelope, so a cached stand
            // the body could not walk to was held for ever: on 2026-09-11 that was ticks 18,501
            // to 21,531 on one pot, 3,031 unbroken ticks with the movement system reporting
            // itself stalled on 1,826 of them, which was 97% of every stalled tick in the run.
            // An intent that cannot fail is an intent that cannot be given up, so covering no
            // ground for a full progress window defers this tile and hands the tick back.
            if (Vector2.DistanceSquared(approachOrigin, ctx.Npc.Bottom)
                >= Infrastructure.Selection.Weights.ObjectiveProgressPixels * Infrastructure.Selection.Weights.ObjectiveProgressPixels)
            { approachOrigin = ctx.Npc.Bottom; approachTicks = 0; }
            else if (++approachTicks >= Infrastructure.Selection.Weights.ObjectiveProgressWindowTicks)
            {
                deferred[tile] = Main.GameUpdateCount + DeferFailedApproachTicks;
                Infrastructure.Diagnostics.GodsEyeEvents.RecordWorldInteraction(ctx.Npc, tile, "approach-abandoned",
                    $"no ground covered in {Infrastructure.Selection.Weights.ObjectiveProgressWindowTicks} ticks");
                target = null; nextSearch = Main.GameUpdateCount + 60;
                Release("approach-made-no-progress");
                return PositionRequest.Hold;
            }
            return PositionRequest.ExactAt(stand, tile);
        }
        if (Main.GameUpdateCount >= retryAfter)
        {
            retryAfter = Main.GameUpdateCount + 30;
            bool worked = Perform(ctx, tile);
            if (worked) ctx.Companion.Brain.Chooser.RecordWork(tile.ToWorldCoordinates());
            else Release("native-interaction-refused");
            target = null;
            // A method that works a region rather than a site searches again on the next preparation, from
            // where the body now stands, instead of waiting out the cadence: the wait exists so a search that
            // found nothing is not repeated every tick, and a search that has just succeeded is not that.
            nextSearch = worked && ContinueAfterInteraction ? 0 : Main.GameUpdateCount + 60;
        }
        return PositionRequest.Hold;
    }

    public override void Exit(in ActionContext ctx)
    {
        // A reflex or protective action can change a take-off pose; never resume its old jump.
        if (needsJump) { target = null; }
    }

    /// <summary>
    /// The nearest target this method could act on right now from the body's current pose, for an incidental interaction: the method
    /// enabled by its own policy, supply and measurement, the tile within actual reach, and passing the method's own candidate check,
    /// which carries home protection and the site rules. It never searches beyond reach, never asks for a route and never touches the
    /// method's discovery state, so the instance asked is a library of this method's rules rather than the activity itself.
    /// </summary>
    internal Point? FindIncidentalTarget(in ActionContext ctx, object? excluded)
    {
        // Enabled runs first on every ask, because lighting's candidate check reads the carried-light list its enablement fills.
        if (ctx.Player.dead || !Enabled(ctx)) return null;
        Point feet = MovementQueries.FeetTile(ctx.Npc.Bottom);
        int reachX = Player.tileRangeX + 1, reachY = Player.tileRangeY + 1;
        Point? best = null;
        float bestDistance = float.MaxValue;
        for (int x = feet.X - reachX; x <= feet.X + reachX; x++)
            for (int y = feet.Y - MovementQueries.BodyHeightTiles - reachY; y <= feet.Y + reachY; y++)
            {
                Point p = new(x, y);
                if (Equals(excluded, p)) continue;
                float distance = Vector2.DistanceSquared(ctx.Npc.Center, p.ToWorldCoordinates());
                if (distance >= bestDistance || !FindToolAccess.InReach(ctx.Npc.Bottom, p) || !Candidate(ctx, p)) continue;
                best = p;
                bestDistance = distance;
            }
        return best;
    }

    /// <summary>Act on an incidental target through the method's own native operation, which rechecks permission at mutation, with
    /// <paramref name="note"/> carried into the interaction event so the effect reads as incidental and credited to no activity.</summary>
    internal bool PerformIncidental(in ActionContext ctx, Point tile, string note)
    {
        performNote = note;
        try { return Perform(ctx, tile); }
        finally { performNote = ""; }
    }

    private string performNote = "";

    /// <summary>Empty for the activity's own interaction; for an incidental one, the marker its event detail begins with.</summary>
    protected string PerformNote => performNote;
}
