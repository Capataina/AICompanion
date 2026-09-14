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
    /// <summary>Whether this search passed over a site because the reach flood had not settled on its working
    /// pose yet, as opposed to having proven anything about it.</summary>
    private bool standUnresolved;

    /// <summary>The shared name for a search that reached the end of its sites with nothing proven and at
    /// least one pose the flood had not claimed yet. A subclass that can say which of its own exits this was
    /// says so through <see cref="SearchRefusal"/>; this is what a subclass with no opinion reports, and it is
    /// an answer that has not arrived rather than an answer of "no".</summary>
    private const string StandUnresolvedReason = "interaction-stand-not-yet-known-reachable";
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
    /// <summary>
    /// What the approach query answered about a site this search then passed over, for a subclass that names
    /// its exits more precisely than the shared ones. It is called with <c>No</c> or <c>Unknown</c> only, and
    /// the two are a real distinction rather than a shade of one: a proven <c>No</c> is remembered, so the
    /// next search advances past the site, while an <c>Unknown</c> is a flood that has not settled and must be
    /// re-asked. Writing an unsettled flood off as a refusal is what kept every search on the same nearest
    /// sites, and it is the failure this whole path exists to have removed.
    /// </summary>
    protected virtual void NoteApproach(in ActionContext ctx, Reachability.Reach verdict) { }
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

    /// <summary>The last refusal this method actually proved about a site, kept for as long as the deferral
    /// that carries it. <see cref="tripRefusal"/> is cleared at the start of every search, which is right for
    /// a claim about *this* search and wrong for a proof: the search that proves a pit has no way back often
    /// also runs out of site budget, so it must report the budget, and by the next search every proven site is
    /// deferred and never rebuilt — leaving the offer to say the placer accepted nothing, an absence, where
    /// the truth is that everything findable was proven unusable. Cleared with the deferrals themselves, so it
    /// cannot outlive the evidence.</summary>
    private (OfferEligibility Eligibility, string Reason)? provenRefusal;

    /// <summary>The shared name for a site the body cannot get to and come home from. It was the round trip's
    /// name for the same fact and it is kept, because the fact did not change when the evidence for it did.</summary>
    private const string NoReturnReason = "interaction-site-has-no-return";

    /// <summary>
    /// What the last discovery search asked and what came back, as <c>x,y=verdict</c> joined by semicolons,
    /// for the telemetry. The offer alone says a search found nothing and cannot say whether it looked at two
    /// sites or two hundred, or whether the answers were refusals or an unsettled flood — which is exactly
    /// the distinction the starvation hid for a whole session, and the reason a reader of the 2026-09-14
    /// capture could see only that the question was never finished. Empty on a preparation that ran no search.
    /// </summary>
    public string LastSearchSites { get; private set; } = "";
    /// <summary>How many sites that search put to the approach query, which is the figure the string is a
    /// sample of: the string is capped and this is not, so a capped string still carries an honest count.</summary>
    public int LastSearchAsked { get; private set; }
    /// <summary>How many entries the ledger keeps. A row is read in a terminal and a search over a wholly dark
    /// floor can ask hundreds; the count beside it carries the rest.</summary>
    private const int LedgerEntries = 12;
    private readonly System.Text.StringBuilder ledger = new();

    /// <summary>Keep a site whose trip was proven impossible (no way back, or no take-off for the only hop that could
    /// reach it) out of discovery until the terrain changes or the wait passes. A named reason also becomes the offer's refusal.</summary>
    private void DeferRefusedTrip(Point tile, string? reason)
    {
        if (noReturn.Count > 64) { noReturn.Clear(); provenRefusal = null; }
        noReturn[tile] = (TerrainChanges.Revision, Main.GameUpdateCount + (ulong)Infrastructure.Selection.Weights.NearbyWorkNoReturnRetryTicks);
        if (reason != null) tripRefusal = provenRefusal = (OfferEligibility.KnownUnusable, reason);
    }

    /// <summary>Add one asked site and its verdict to the ledger. The count always advances; the text stops at
    /// <see cref="LedgerEntries"/>, so a row stays readable and the two together say "these are the first
    /// twelve of this many" rather than quietly presenting a sample as the whole search.</summary>
    private void RecordAsked(Point tile, Reachability.Reach verdict)
    {
        LastSearchAsked++;
        if (LastSearchAsked > LedgerEntries) return;
        if (ledger.Length > 0) ledger.Append(';');
        ledger.Append(tile.X).Append(',').Append(tile.Y).Append('=').Append(
            verdict switch
            {
                Reachability.Reach.Yes => "reachable",
                Reachability.Reach.No => "unreachable",
                _ => "not-yet-known",
            });
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
        // An unsettled flood outranks a refusal proven earlier in the same search, because proving site one
        // unreachable and then meeting a site the flood has not claimed is not "the companion cannot come back
        // from there", it is "the question is not finished". Reported the other way round, the offer and the
        // telemetry both name a proven impossibility for a search that has more to learn, which is the one
        // reading that would stop anyone looking again. A subclass naming its own unresolved exit wins, because
        // it knows which of its exits this was; the shared name is what a subclass with no opinion reports.
        else if (target == null && standUnresolved)
            Classify(OfferEligibility.Unresolved,
                SearchRefusal(ctx) is { Eligibility: OfferEligibility.Unresolved } named ? named.Reason : StandUnresolvedReason);
        else if (target == null && tripRefusal is { } refusal) Classify(refusal.Eligibility, refusal.Reason);
        // A search that reached the end of its list and found nothing, where every site it could find was
        // proven unusable on an earlier search and is still deferred. Without this the offer reports the scan's
        // own absence and the proof is thrown away, which is how "the companion cannot come back from there"
        // became "the placer accepted nothing" one search after it was established.
        else if (target == null && noReturn.Count > 0 && provenRefusal is { } earlier)
            Classify(earlier.Eligibility, earlier.Reason);
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
            // The proof went with the deferrals it described; keeping the sentence after deleting its evidence
            // would report a refusal under a reach that never produced it.
            provenRefusal = null;
        }
        if (target is Point old && (!Candidate(ctx, old) || !AllowsTarget(ctx, old.ToWorldCoordinates())))
        { target = null; }
        if (target == null && Main.GameUpdateCount >= nextSearch)
        {
            nextSearch = Main.GameUpdateCount + 90;
            tripRefusal = null;
            // Nearest first by the method's own cost, stopping at the first site whose access is proven. Every site in
            // the list is asked, and that is the change: the question behind the approach is now membership of a flood
            // one sense has already run, so there is nothing left to ration. While it was a fresh bounded A* per pose
            // this loop stopped after three sites, and because an A* that runs out of expansions answers Unknown rather
            // than No, and an Unknown was passed over without being remembered, the same three nearest sites were asked
            // every retry and the search never advanced. On the 2026-09-14 capture that exit — the budget spent before
            // an answer — was 79% of the lighting offers, and 7,003 of those rows had a finished flood sitting beside
            // them with the answer already in it.
            ordered.Clear();
            standUnresolved = false;
            ledger.Clear();
            LastSearchAsked = 0;
            GatherSearchTiles(ctx, ordered);
            ordered.Sort(static (a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.Order.CompareTo(b.Order));
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
                    // The loop is bounded by the tick's own planning allowance rather than by a count of sites,
                    // and it is bounded because the reach question becoming free did not make the approach free:
                    // ranking poses is still a scan of every standable tile in reach of the site, each asking the
                    // engine for a line to an exposed face. On a floor dark everywhere that is hundreds of sites,
                    // and with nothing bounding it one preparation measured 25.5 ms against a twelve-millisecond
                    // tick. The count is the wrong bound now: it was there to ration route searches, and rationing
                    // by three sites a search is what let three undecided sites hide every site behind them. A
                    // deadline rations the same cost without an ordering, and a cut scan is an answer that has not
                    // arrived — reported Unresolved and retried in a rescore, which is what the sites past the cut
                    // actually are. The general shape, which this file has now met twice: when a cheaper proof
                    // replaces a dearer one, find what the dearer one's bound was standing in front of.
                    if (LimitPlanningWork.Expired) { standUnresolved = true; break; }
                    // One question, asked once, of the sense. The approach ranks working poses by geometry and
                    // answers reach from the two-way flood, which is membership rather than a search, so the
                    // answer it gives already means "the body can get to this pose and come home from it" —
                    // exactly what the round trip that used to run here proved, per site, with two fresh route
                    // searches. There is no second check after it because there is nothing left to check: a
                    // stand the flood claimed is in the flood.
                    var standing = FindToolAccess.Approach(p, ctx.Npc.Bottom, ctx.Senses.Reach, out candidateStand);
                    // Only a site no standing pose reaches is hopped to, from a take-off the flood claims: the same order and
                    // the same query mining uses for ceiling ore, so a take-off is admitted here exactly when it would be there.
                    if (standing == Reachability.Reach.No && AllowJump)
                    {
                        standing = FindToolAccess.HopApproach(p, ctx.Companion.Motor.State, ctx.Senses.Reach, out candidateStand);
                        jump = standing == Reachability.Reach.Yes;
                    }
                    RecordAsked(p, standing);
                    if (standing != Reachability.Reach.Yes)
                    {
                        NoteApproach(ctx, standing);
                        // A proven No is remembered and an Unknown is not, and that asymmetry is the whole
                        // correctness of a search over a flood that grows. Remembering an Unknown writes a site
                        // off for the deferral's whole life on the strength of a flood that had not finished;
                        // forgetting a No leaves the nearest refused sites in front of every later search.
                        if (standing == Reachability.Reach.No)
                        {
                            // The reason has to outlive the scan that found it: once the site is deferred the next
                            // scan never builds it, so an unnamed refusal decays into "the placer accepted nothing"
                            // — an absence, where a proven no-return is knowledge. A subclass that names it more
                            // precisely wins; one that does not gets the shared name rather than silence, which is
                            // what pot collection used to get.
                            DeferRefusedTrip(p, NoReturnReason);
                            if (SearchRefusal(ctx) is { } proven) tripRefusal = provenRefusal = proven;
                        }
                        else standUnresolved = true;
                        continue;
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
            // A search that passed over a site on an unsettled flood is the same kind of result and retries the
            // same way, whether the subclass has a name for it or not. Waiting the full cadence on it re-asks
            // from wherever the body has since walked, which is the failure that turned the J08 lighting trip's
            // site into the wrong one: the scene moves while the evidence is being gathered.
            LastSearchSites = ledger.ToString();
            if (target == null && (standUnresolved || SearchRefusal(ctx) is { Eligibility: OfferEligibility.Unresolved }))
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
