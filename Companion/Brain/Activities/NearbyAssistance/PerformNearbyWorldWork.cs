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

/// <summary>
/// The tile hand shared by pot breaking and permanent lighting: it works the tile the course's step names, and it
/// still runs the bounded interaction search that describes the method's offer to the fixtures and the recorder.
///
/// <para>**The search chooses nothing the hand does, since 23 September 2026.** Until then `Execute` swung at the
/// search's own `target` while the course flew the body to the census's site, so the hand and the body agreed only
/// when both happened to pick the same nearest thing. What the search still produces is the offer classification, the
/// candidate funnel and `LightUsefulArea`'s player-cursor reference; it and `Score` go together with
/// `VerifyPreparedActivities.PrepareAndScore`, whose fixtures are what still read them.</para>
/// </summary>
public abstract class PerformNearbyWorldWork : CompanionAction
{
    public override Infrastructure.Selection.PurposeFamily Family => Infrastructure.Selection.PurposeFamily.NearbyAssistance;
    /// <summary>The site the last preparation's search would offer. Never what the hand works: that is
    /// <see cref="BoundTile"/>.</summary>
    protected Point? target;
    private ulong nextSearch, retryAfter;
    private bool eligibleLastObservation;
    /// <summary>Whether this search passed over a site because the reach flood had not settled on its working
    /// pose yet, as opposed to having proven anything about it.</summary>
    private bool standUnresolved;
    /// <summary>Whether the tick's planning deadline stopped this search before it had put every site to the
    /// approach query. A cut is an answer that has not arrived, and it is a different one from an unsettled flood:
    /// the flood beside the cut may be finished and holding every answer, which is exactly what the capture of 15
    /// September 2026 showed under the one name both used to share.</summary>
    private bool searchCut;
    // Whether this search proved a site's stand unreachable, or met a stand outside the flood's known radius.
    private bool sawNoReturn, sawBeyondKnownRadius;

    /// <summary>The name for a search that passed over a site whose pose the flood had not claimed yet, while the flood
    /// was still growing. An answer that has not arrived rather than an answer of "no".</summary>
    private const string StandUnresolvedReason = "interaction-stand-not-yet-known-reachable";

    /// <summary>The name for a search the tick's planning deadline stopped. It used to be reported as an unsettled stand,
    /// which it is not: on 3,607 rows of the 15 September capture every site that search had asked was proven
    /// unreachable and the flood beside it was complete, and the offer said the question was not finished.</summary>
    private const string SearchCutReason = "interaction-search-cut-by-planning-deadline";

    /// <summary>The name for a site whose every working pose lies outside the finished flood's known radius. A finished
    /// flood proves absence only inside that radius, so this is not a refusal, and it is not unfinished either: nothing
    /// the flood will do answers it. It answers again when a different flood does, which is when the body has moved.</summary>
    private const string BeyondKnownRadiusReason = "interaction-stand-beyond-known-radius";

    /// <summary>The executor's own funnel stages, declared by a subclass that keeps a funnel after its own candidate
    /// stages, in this order; see <see cref="CandidateFunnel"/>.</summary>
    protected const string StageAllowance = "activity-allowance";
    protected const string StageSearchCut = "search-cut";
    protected const string StageStandNotYetKnown = "stand-not-yet-known";
    protected const string StageStandBeyondKnownRadius = "stand-beyond-known-radius";
    protected const string StageStandUnreachable = "stand-unreachable";
    /// <summary>What a tile that every stage would accept is called when it was not the one offered: a nearer site was, or
    /// the search has not run since. Never a funnel stage, because a searched candidate that passes everything is offered.</summary>
    protected const string StagePassedEveryStage = "passed-every-stage";

    private (int X, int Y) derivedReach;
    private Vector2? preparedTarget;
    private float preparedValue;
    private bool enabledAtPreparation;
    // The last way this method gave up on a target during the current attempt, so the attempt is
    // concluded from what happened to it rather than from the absence of a target afterwards.
    private string? release;
    public override Vector2? ActivityTarget => BoundTile?.ToWorldCoordinates() ?? preparedTarget;
    /// <summary>The bound step's opportunity, so a new target the course hands over is a new purpose for the attempt
    /// lifecycle and the same step carried across ticks is the same purpose. The search's offered tile stands in only
    /// where no step was handed, which is never on a tick this activity holds the body.</summary>
    public override object? ActivityIdentity => Bound is { } step && WorksTileFor(step.Opportunity.Purpose) ? step.Opportunity : target;
    protected abstract bool Enabled(in ActionContext ctx);
    protected abstract bool Candidate(in ActionContext ctx, Point tile);
    protected abstract bool Perform(in ActionContext ctx, Point tile);
    protected abstract float Utility { get; }
    /// <summary>Why this search found no site, where the subclass knows something more specific than "nothing
    /// in the window". Null keeps the shared answer. Every exit about a site's stand is the executor's and has one
    /// shared name; this is for what the subclass's own gathering found.</summary>
    protected virtual (OfferEligibility Eligibility, string Reason)? SearchRefusal(in ActionContext ctx) => null;

    /// <summary>The stage that refuses this tile as a candidate, or null when it is one. A subclass that keeps a funnel
    /// names its stages here; the default is <see cref="Candidate"/> with one name for every refusal.</summary>
    protected virtual string? RefusingStage(in ActionContext ctx, Point tile) => Candidate(ctx, tile) ? null : "candidate-refused";

    /// <summary>The name of the last candidate stage a tile passes before its stand is asked about.</summary>
    protected virtual string CandidateStagePassed => "candidate";

    /// <summary>What the candidate stages read for this tile, for the funnel.</summary>
    protected virtual string CandidateReadings(in ActionContext ctx, Point tile) => "";

    /// <summary>The funnel this executor's search fills, or null for a subclass that keeps none.</summary>
    protected virtual CandidateFunnel? SearchFunnel => null;

    /// <summary>The classification for a method its enabling conditions currently refuse; the
    /// subclass knows whether that refusal is a player setting, missing supplies or the world.</summary>
    protected virtual (OfferEligibility Eligibility, string Reason) DisabledOffer(in ActionContext ctx)
        => ctx.Player.dead ? (OfferEligibility.NoOpportunity, "player-dead") : (OfferEligibility.PolicyForbidden, "interaction-disabled");
    /// <summary>What one observed productive interaction means for this purpose.</summary>
    protected virtual string CompletedEffect => "interaction-effect-observed";
    protected virtual float CandidateCost(Vector2 feet, Point tile) => Vector2.DistanceSquared(feet, tile.ToWorldCoordinates());

    /// <summary>Tiles this search will consider, nearest-first by <see cref="CandidateCost"/>. Pots keep a small
    /// window around the body; lighting overrides this to the work area around the player.</summary>
    protected virtual void GatherSearchTiles(in ActionContext ctx, System.Collections.Generic.List<(float Cost, int Order, Point Tile)> into)
    {
        Point centre = ctx.Npc.Center.ToTileCoordinates();
        for (int x = centre.X - 18; x <= centre.X + 18; x++)
            for (int y = centre.Y - 14; y <= centre.Y + 14; y++)
            {
                Point p = new(x, y);
                if (SearchTileDeferred(p)) continue;
                into.Add((CandidateCost(ctx.Npc.Center, p), into.Count, p));
            }
    }

    /// <summary>Whether this tile is currently deferred for a proven refusal of its stand.</summary>
    protected bool SearchTileDeferred(Point tile) => RefusalDeferred(tile);

    /// <summary>The funnel stage a deferred tile was set aside at, or null when it is not deferred.</summary>
    protected string? DeferredStage(Point tile)
    {
        if (!RefusalDeferred(tile)) return null;
        return refused[tile].BeyondKnownRadius ? StageStandBeyondKnownRadius : StageStandUnreachable;
    }

    // Search-window tiles in the order they are asked, reused across searches: the brain is single-threaded.
    private readonly System.Collections.Generic.List<(float Cost, int Order, Point Tile)> ordered = new();

    /// <summary>
    /// Sites whose stand a finished flood refused, with the flood that refused them and the tick they may be asked again:
    /// a stand proven outside the flood, or one lying beyond the flood's known radius. Without it the same nearest refused
    /// sites spend every search's allowance and a farther site with a way back is never reached, which is the whole
    /// defect this store exists to prevent.
    ///
    /// <para>Kept against the flood rather than the terrain revision, and pruned rather than emptied. Both used to be the
    /// other way, and both starved the search the store was built for: the terrain revision moves on every edit
    /// anywhere, the companion's own torch included, and the store emptied itself whole past a fixed size, so under a
    /// floor of sealed dark pockets every search re-proved the same nearest pockets until the deadline cut it and never
    /// reached a reachable site (15 September 2026, 2,347 rows asking 13 to 64 sites, every one unreachable).</para>
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<Point, (int Generation, ulong Until, bool BeyondKnownRadius)> refused = new();
    /// <summary>Past this many remembered refusals a new one first prunes the stale ones. It bounds the cost of the pass,
    /// not what is remembered: a refusal still true is never dropped to make room.</summary>
    private const int PruneRefusalsAbove = 64;
    private int floodGeneration;

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

    /// <summary>Keep a site whose stand a finished flood refused out of discovery while that flood answers and the wait
    /// has not passed.</summary>
    private void DeferRefusedStand(Point tile, bool beyondKnownRadius)
    {
        if (refused.Count > PruneRefusalsAbove) PruneRefusals();
        refused[tile] = (floodGeneration, Main.GameUpdateCount + (ulong)Infrastructure.Selection.Weights.NearbyWorkNoReturnRetryTicks, beyondKnownRadius);
    }

    private void PruneRefusals()
    {
        ulong now = Main.GameUpdateCount;
        stale.Clear();
        foreach (var (tile, entry) in refused)
            if (entry.Generation != floodGeneration || now >= entry.Until) stale.Add(tile);
        foreach (Point tile in stale) refused.Remove(tile);
    }
    private readonly System.Collections.Generic.List<Point> stale = new();

    /// <summary>Add one asked site and its verdict to the ledger. The count always advances; the text stops at
    /// <see cref="LedgerEntries"/>, so a row stays readable and the two together say "these are the first
    /// twelve of this many" rather than quietly presenting a sample as the whole search.</summary>
    private void RecordAsked(Point tile, Reachability.Reach verdict, bool beyondKnownRadius)
    {
        LastSearchAsked++;
        if (LastSearchAsked > LedgerEntries) return;
        if (ledger.Length > 0) ledger.Append(';');
        ledger.Append(tile.X).Append(',').Append(tile.Y).Append('=').Append(
            verdict switch
            {
                Reachability.Reach.Yes => "reachable",
                Reachability.Reach.No => "unreachable",
                _ => beyondKnownRadius ? "beyond-known-radius" : "not-yet-known",
            });
    }

    private bool RefusalDeferred(Point tile)
    {
        if (!refused.TryGetValue(tile, out var entry)) return false;
        if (entry.Generation == floodGeneration && Main.GameUpdateCount < entry.Until) return true;
        refused.Remove(tile);
        return false;
    }

    /// <summary>Whether a refusal proven on an earlier search still stands, and whether any of those is a stand beyond
    /// the known radius; null when none does.</summary>
    private bool? StandingRefusal()
    {
        bool any = false, beyond = false;
        ulong now = Main.GameUpdateCount;
        foreach (var entry in refused.Values)
        {
            if (entry.Generation != floodGeneration || now >= entry.Until) continue;
            any = true;
            beyond |= entry.BeyondKnownRadius;
        }
        return any ? beyond : null;
    }

    // The offered site's funnel entry waits for its trip, which Prepare prices after the search.
    private bool offeredPending;
    private float offeredCost;
    private string offeredReadings = "";

    public override void Prepare(in ActionContext ctx)
    {
        preparedValue = DiscoverValue(ctx);
        preparedTarget = target?.ToWorldCoordinates();
        // The flight to the site and a moment to use it. This used to be missing, so every nearby interaction —
        // lighting above all — was scored as though it cost no time at all while hunting, mining and collection paid
        // for theirs, and the companion left a slime it was fighting to fly to a far torch site.
        preparedTrip = preparedTarget is { } site
            ? Vector2.Distance(ctx.Npc.Center, site) / OrbPace.MaxSpeed + Infrastructure.Selection.Weights.NearbyInteractionTicks
            : 0f;
        if (offeredPending && target is Point chosen)
            SearchFunnel?.Add("tile", chosen, offeredCost, "stand", "", offeredReadings
                + FormattableString.Invariant($";trip={preparedTrip:0.0};value={preparedValue:0.000}"));
        offeredPending = false;
        if (!enabledAtPreparation) { var (eligibility, reason) = DisabledOffer(ctx); Classify(eligibility, reason); }
        // The exits are ordered by how much of the question is still open. A cut search and an unsettled flood have
        // not answered at all; a site beyond the known radius has an answer that will change once the body moves;
        // a refused stand is proven for as long as its flood answers; and only then does the subclass's own account
        // of what it gathered speak. A search whose gathering itself could not answer (no light measured, a scan the
        // deadline cut) asked no stand at all and says so before any earlier proof, which is about somewhere else.
        else if (target == null && searchCut) Classify(OfferEligibility.Unresolved, SearchCutReason);
        else if (target == null && standUnresolved) Classify(OfferEligibility.Unresolved, StandUnresolvedReason);
        else if (target == null && LastSearchAsked == 0 && SearchRefusal(ctx) is { Eligibility: OfferEligibility.Unresolved } unanswered)
            Classify(unanswered.Eligibility, unanswered.Reason);
        else if (target == null && (sawBeyondKnownRadius || StandingRefusal() == true))
            Classify(OfferEligibility.Deferred, BeyondKnownRadiusReason);
        // A search that reached the end of its list and found nothing, where every site it could find was
        // proven unusable on an earlier search and is still deferred. Without this the offer reports the scan's
        // own absence and the proof is thrown away, which is how "the companion cannot come back from there"
        // became "the placer accepted nothing" one search after it was established.
        else if (target == null && (sawNoReturn || StandingRefusal() != null))
            Classify(OfferEligibility.KnownUnusable, NoReturnReason);
        else if (target == null)
        {
            var (eligibility, reason) = SearchRefusal(ctx) ?? (OfferEligibility.NoOpportunity, "no-candidate-in-search-window");
            Classify(eligibility, reason);
        }
        else Classify(OfferEligibility.Usable, "reachable-interaction");
    }

    public override float Score() => preparedValue;

    private float preparedTrip;
    public override float ForecastTicks() => preparedTrip;

    /// <summary>One interaction is this purpose's whole job, so an observed productive effect
    /// completes it; a named give-up is a failed method unless the target itself stopped qualifying.</summary>
    public override AttemptConclusion ConcludeAttempt(int productiveEffects)
    {
        // The companion's own native call produced the credited effect, so the completion is its own.
        if (productiveEffects > 0) return new(AttemptStatus.Complete, CompletedEffect, AttemptAttribution.Companion);
        if (release is string reason)
            return new(InvalidReleases.Contains(reason) ? AttemptStatus.Invalid : AttemptStatus.Failed, reason);
        return new(AttemptStatus.Attempted, "replaced-before-interaction");
    }

    public override void BeginAttempt() => release = null;

    /// <summary>The give-ups that mean the target itself stopped qualifying, which is an invalid attempt rather than a
    /// failed method: nothing the hand could have done differently would have worked a target that is gone.</summary>
    private static readonly System.Collections.Generic.HashSet<string> InvalidReleases = new(StringComparer.Ordinal)
    {
        TargetNoLongerCandidate, BoundDropLeftWorld,
    };

    /// <summary>The bound tile no longer passes the method's own candidate check at the native call: the pot was broken
    /// by somebody else, the tile is lit now, a torch went in beside it.</summary>
    protected const string TargetNoLongerCandidate = "target-no-longer-candidate";
    /// <summary>The bound drop's world slot no longer holds the drop the course bound.</summary>
    protected const string BoundDropLeftWorld = "bound-drop-left-world";

    protected void Release(string reason) => release = reason;

    // ---- the course's step ------------------------------------------------------------------------------------------

    /// <summary>The tile the step this activity was handed names, or null when it was handed none or a step that is not
    /// tile work. The hand works this tile and no other: the course chose it, and a hand that searched for its own would
    /// put a second chooser behind the first, the body flying to the course's site while the hand swung at another.</summary>
    protected Point? BoundTile { get; private set; }
    private long boundStep;
    /// <summary>Whether this step's one interaction has been made, or its native call refused. One interaction is the
    /// whole job of a pot or a torch, so the hand does nothing more for the step: the course's next observation retires
    /// it, and a refused call is not retried against a site the placer has already said no to.</summary>
    private bool boundSpent;

    /// <summary>Whether this method performs a step of this purpose with its tile hand. Collection answers only for pots,
    /// because a drop is taken by contact; lighting answers for torch sites.</summary>
    protected abstract bool WorksTileFor(string purpose);

    protected override void OnAccept(Infrastructure.Selection.Courses.StepBinding? step)
    {
        if (step == null || !WorksTileFor(step.Opportunity.Purpose))
        {
            BoundTile = null;
            boundStep = 0;
            return;
        }
        if (step.Id == boundStep) return;
        boundStep = step.Id;
        boundSpent = false;
        retryAfter = 0;
        BoundTile = Infrastructure.Selection.ExecuteCourseBinding.WorkTileOf(step.Opportunity);
    }

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
        floodGeneration = ctx.Senses.Reach.FloodGeneration;
        // Everything this search retains about a site was derived under the reach it was computed with: the offered site,
        // the wait before the next search, and the refused stands. A larger reach kept refusing a site whose pit-floor pose
        // had no return although it works the site from the rim. So a reach change releases every store together and this
        // preparation searches again. The capability is this one comparison: a capability added to it releases all of
        // them, where a key added to one store and not the other refuses under the old value for its hold time.
        if (FindToolAccess.Reach != derivedReach)
        {
            derivedReach = FindToolAccess.Reach;
            target = null;
            nextSearch = 0;
            refused.Clear();
        }
        if (target is Point old && (!Candidate(ctx, old) || !AllowsTarget(ctx, old.ToWorldCoordinates())))
        { target = null; }
        if (target == null && Main.GameUpdateCount >= nextSearch)
        {
            nextSearch = Main.GameUpdateCount + 90;
            // Nearest first by the method's own cost, stopping at the first site whose access is proven. Every site in
            // the list is asked, and that is the change: the question behind the approach is membership of a flood
            // one sense has already run, so there is nothing left to ration. While it was a fresh bounded A* per pose
            // this loop stopped after three sites, and because an A* that runs out of expansions answers Unknown rather
            // than No, and an Unknown was passed over without being remembered, the same three nearest sites were asked
            // every retry and the search never advanced. On the 2026-09-14 capture that exit — the budget spent before
            // an answer — was 79% of the lighting offers, and 7,003 of those rows had a finished flood sitting beside
            // them with the answer already in it.
            ordered.Clear();
            standUnresolved = searchCut = sawNoReturn = sawBeyondKnownRadius = false;
            ledger.Clear();
            LastSearchAsked = 0;
            CandidateFunnel? funnel = SearchFunnel;
            funnel?.Begin();
            GatherSearchTiles(ctx, ordered);
            ordered.Sort(static (a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.Order.CompareTo(b.Order));
            int looked = 0;
            foreach (var (cost, _, p) in ordered)
            {
                // The candidate stages are cheap and can run for every tile of a dark floor, so the deadline is read
                // every so often between them as well as before each approach, which is the expensive half.
                if ((++looked & 63) == 0 && LimitPlanningWork.Expired)
                {
                    searchCut = true;
                    funnel?.Add("tile", p, cost, "", StageSearchCut, "");
                    break;
                }
                if (!AllowsTarget(ctx, p.ToWorldCoordinates(), p))
                {
                    funnel?.Add("tile", p, cost, "", StageAllowance, "");
                    continue;
                }
                string? refusedAt = RefusingStage(ctx, p);
                if (refusedAt != null)
                {
                    funnel?.Add("tile", p, cost, null, refusedAt, CandidateReadings(ctx, p));
                    continue;
                }
                string readings = funnel == null ? "" : CandidateReadings(ctx, p);
                Vector2 candidateStand;
                if (FindToolAccess.InReach(ctx.Npc.Center, p)) candidateStand = ctx.Npc.Center;
                else
                {
                    // The loop is bounded by the tick's own planning allowance rather than by a count of sites,
                    // and it is bounded because the reach question becoming free did not make the approach free:
                    // ranking poses is still a scan of every standable tile in reach of the site, each asking the
                    // engine for a line to an exposed face. On a floor dark everywhere that is hundreds of sites,
                    // and with nothing bounding it one preparation measured 25.5 ms against a twelve-millisecond
                    // tick. A cut scan is an answer that has not arrived, reported under its own name and retried
                    // in a rescore; the refusals it proved before the cut are remembered, so the next search starts
                    // past them rather than on them.
                    if (LimitPlanningWork.Expired)
                    {
                        searchCut = true;
                        funnel?.Add("tile", p, cost, CandidateStagePassed, StageSearchCut, readings);
                        break;
                    }
                    // One question, asked once, of the sense. The approach ranks working poses by geometry and
                    // answers reach from the two-way flood, which is membership rather than a search, so the
                    // answer it gives already means "the body can get to this pose and come home from it".
                    var standing = FindToolAccess.Approach(p, ctx.Npc.Center, ctx.Senses.Reach, out candidateStand);
                    // An Unknown from a finished flood can only be a pose outside its known radius, because inside it a
                    // finished flood answers yes or no; nothing that flood will do answers it, so it is remembered like a
                    // refusal rather than re-asked every retry for as long as the body stays put.
                    bool beyond = standing == Reachability.Reach.Unknown && ctx.Senses.Reach.Complete;
                    RecordAsked(p, standing, beyond);
                    if (standing != Reachability.Reach.Yes)
                    {
                        string stage;
                        // A proven No is remembered and an unfinished Unknown is not, and that asymmetry is the whole
                        // correctness of a search over a flood that grows. Remembering an unfinished Unknown writes a
                        // site off on the strength of a flood that had not finished; forgetting a No leaves the nearest
                        // refused sites in front of every later search.
                        if (standing == Reachability.Reach.No)
                        {
                            DeferRefusedStand(p, beyondKnownRadius: false);
                            sawNoReturn = true;
                            stage = StageStandUnreachable;
                        }
                        else if (beyond)
                        {
                            DeferRefusedStand(p, beyondKnownRadius: true);
                            sawBeyondKnownRadius = true;
                            stage = StageStandBeyondKnownRadius;
                        }
                        else
                        {
                            standUnresolved = true;
                            stage = StageStandNotYetKnown;
                        }
                        funnel?.Add("tile", p, cost, CandidateStagePassed, stage, readings + ";stand=" + standing);
                        continue;
                    }
                }
                target = p;
                offeredPending = funnel != null;
                offeredCost = cost;
                offeredReadings = readings;
                break;
            }
            // A search that could not answer waits a fraction of the time a search that answered "nothing
            // here" waits. The long wait exists so a fruitless search is not repeated every tick, and a
            // search whose evidence simply has not arrived yet is not that: the reach region is flooded
            // incrementally and takes a few rescores to settle after a world change, so the first searches
            // after one refuse on "not yet known" while the body keeps moving. Waiting the full cadence
            // then re-asks from wherever the companion has since wandered, which is how a proven site a few
            // tiles away becomes a different, worse site by the time anyone can prove anything about it.
            LastSearchSites = ledger.ToString();
            if (target == null && (standUnresolved || searchCut || SearchRefusal(ctx) is { Eligibility: OfferEligibility.Unresolved }))
                nextSearch = Main.GameUpdateCount + (ulong)Infrastructure.Selection.Weights.NearbyWorkUnresolvedRetryTicks;
        }
        return target == null ? 0f : Utility;
    }
    /// <summary>
    /// Work the tile the course bound, and nothing else. The body is the course's: `ExecuteCourseBinding.RequestFor`
    /// already asked for this step's pose, and the request returned here is discarded by the tick. What is this
    /// activity's is the hand — revalidating the native preconditions against the live world at the moment of the call
    /// (plan tick-order step 9), refusing with a named reason when the bound target has stopped qualifying, and never
    /// substituting another target for it.
    ///
    /// <para>The approach-progress watchdog and the failed-approach deferral that stood here went with the private
    /// search they fed: the deferral only ever kept a tile out of that search, and the watchdog's hold was discarded by
    /// the tick since the course took the body on 21 September 2026, so neither could reach the body any more.</para>
    /// </summary>
    public override PositionRequest Execute(in ActionContext ctx)
    {
        ctx.Companion.HoldItem(ItemID.None);
        if (Bound is not { } step || BoundTile is not Point tile || boundSpent) return PositionRequest.Hold;
        if (!Enabled(ctx) || ctx.Player.dead)
        {
            boundSpent = true;
            Release("method-disabled-at-native-call");
            RefuseStep("method-disabled-at-native-call");
            return PositionRequest.Hold;
        }
        if (!Candidate(ctx, tile))
        {
            boundSpent = true;
            Release(TargetNoLongerCandidate);
            RefuseStep(TargetNoLongerCandidate);
            return PositionRequest.Hold;
        }
        if (!FindToolAccess.InReach(ctx.Npc.Center, tile))
            return Infrastructure.Selection.ExecuteCourseBinding.RequestFor(step, ctx.Npc.Center);
        if (Main.GameUpdateCount < retryAfter) return PositionRequest.Hold;
        retryAfter = Main.GameUpdateCount + 30;
        boundSpent = true;
        if (Perform(ctx, tile)) ctx.Companion.Brain.Activity.RecordWork(tile.ToWorldCoordinates());
        else
        {
            Release("native-interaction-refused");
            RefuseStep("native-interaction-refused");
        }
        return PositionRequest.Hold;
    }

    public override void Exit(in ActionContext ctx) { }

    /// <summary>
    /// Whether this method's native preconditions still hold for a tile at the moment of an in-passing call: the method enabled by its
    /// own policy and measurement, the tile within actual reach of the body, and the method's own candidate check, which carries home
    /// protection and the site rules. It finds nothing — the census is the only discovery and the course the only acceptance — and is
    /// asked of a site the course has already accepted, so the live world can still veto a use the frozen observation allowed (plan
    /// tick-order step 9). The instance asked is a library of this method's rules rather than the activity itself.
    /// </summary>
    internal bool MayActIncidentally(in ActionContext ctx, Point tile)
        // Enabled runs first on every ask, because lighting's candidate check reads the carried-light list its enablement fills.
        => !ctx.Player.dead && Enabled(ctx) && FindToolAccess.InReach(ctx.Npc.Center, tile) && Candidate(ctx, tile);

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
