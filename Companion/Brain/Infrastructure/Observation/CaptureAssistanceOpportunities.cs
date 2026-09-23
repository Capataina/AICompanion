#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.Activities;
using AICompanion.Companion.Brain.Activities.NearbyAssistance;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;
using AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using FindToolAccess = AICompanion.Companion.Brain.Infrastructure.Interactions.FindToolAccess;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>One data-only assistance site. Native capture owns its construction; course discovery
/// receives only its serialized value through <see cref="DecisionFactSnapshot"/>.
///
/// <para><c>Purpose</c> is what the hand does there, written by the census that found the site. It is a
/// field rather than something a reader works out from the domain because one domain holds two acts since
/// 23 September 2026: collection takes a drop by contact and breaks a pot with its hand, both under
/// `collect-target`, and the two differ in the need they satisfy, whether the hand is reserved and what
/// the binding names as its tool. Working the act out from the target's spelling (`item:` against
/// `tile:`) would be a second vocabulary nobody declared.</para></summary>
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record AssistanceOpportunityFact(string Domain, string Purpose, string Target, long Generation, int ItemType, int Stack,
    double X, double Y, double? LandingX, double? LandingY, double? ContactX, double? ContactY, string Approach,
    bool WorkAllowed, double Amount, double CensusAmount, string Admission, string Reason, string Detail, int Prefix = 0)
{
    /// <summary>A tile site. `x`/`y` is the tile the hand acts on; `contact` is where the body hovers
    /// while it does, and the two are different points for any tile with terrain beside it. A null
    /// contact means no pose within the tool's reach could hold the body, which is a site the binder
    /// must refuse rather than one it may fly into.</summary>
    public AssistanceOpportunityFact(string domain, string purpose, string target, long generation, double x, double y, double amount,
        double censusAmount, string admission, string reason, string detail, (double X, double Y)? contact = null)
        : this(domain, purpose, target, generation, 0, 0, x, y, null, null, contact?.X, contact?.Y, "Unknown", false,
            amount, censusAmount, admission, reason, detail) { }
}

/// <summary>What one pot sweep covered. The sweep reads its whole window in one pass with no cursor and no
/// borrowed allowance, so <see cref="Complete"/> is true whenever the sweep ran; it is carried rather than
/// assumed so that collection's coverage fact states both halves of what it is complete about.</summary>
public sealed record PotSweep(IReadOnlyList<DecisionFact> Facts, Rectangle Area, bool Complete);
public readonly record struct AssistanceCaptureCoverage(string Source, long Examined, long Total, bool Exhausted, bool BudgetCut);
public sealed record AssistanceCaptureSlice(IReadOnlyList<DecisionFact> Facts, AssistanceCaptureCoverage Coverage);

/// <summary>Tracks the identity Terraria exposes for an item slot. A replacement becomes a new
/// generation when it is next observed; a slot never seen between two replacements remains explicitly
/// observationally ambiguous rather than borrowing a generation from a stale Item object.</summary>
public sealed class ObserveItemGenerations
{
    private sealed record Slot(Item Item, int Type, long Generation);
    private readonly Dictionary<int, Slot> slots = new();
    private readonly HashSet<int> seen = new();
    private long next;

    public long Observe(Item item, out bool replacementObserved)
    {
        replacementObserved = false;
        if (!slots.TryGetValue(item.whoAmI, out Slot? prior))
        {
            long generation = ++next;
            slots[item.whoAmI] = new(item, item.type, generation);
            return generation;
        }
        if (ReferenceEquals(prior.Item, item) && prior.Type == item.type) return prior.Generation;
        replacementObserved = true;
        long nextGeneration = ++next;
        slots[item.whoAmI] = new(item, item.type, nextGeneration);
        return nextGeneration;
    }

    public void BeginCapture() => seen.Clear();
    public void MarkSeen(Item item) => seen.Add(item.whoAmI);
    public void EndCapture()
    {
        foreach (int slot in slots.Keys.Where(slot => !seen.Contains(slot)).ToArray()) slots.Remove(slot);
    }

    public void Reset() { slots.Clear(); seen.Clear(); next = 0; }
}

/// <summary>Reads the live assistance domains once, before private activity winner filters run.
/// The returned facts are immutable values: discovery cannot observe a later world mutation through
/// an Item, Tile, Sense, or mutable collection reference.</summary>
public sealed class CaptureAssistanceOpportunities
{
    private readonly ObserveItemGenerations itemGenerations = new();
    private long version;
    private readonly Dictionary<FactKey, (string Text, long Version)> factVersions = new();
    private DecisionWorkCursor drops = new();
    private sealed record FrozenDrop(Item Value, long Generation, bool ReplacementObserved);
    /// <summary>
    /// What the placement step answered about each tile, kept between sweeps.
    ///
    /// The step is the expensive question in this capture — it scans an eight-tile neighbourhood per
    /// call — and the light sweep re-asks the same window every tick, so asking it afresh each time cost
    /// about 36 ms of every tick on a floor that is dark throughout, against a 16.67 ms frame. Its
    /// answer only changes when the terrain it reads changes, and this tree already records edits
    /// spatially, so the cache is cleared by an edit inside the swept window rather than by a clock or
    /// by any edit anywhere. A torch the companion places announces its own edit, which is what makes a
    /// newly-occupied spacing neighbourhood re-ask on the next sweep.
    /// </summary>
    private readonly Dictionary<Point, bool> placementStep = new();
    private int placementStepRevision;

    private FrozenDrop[]? frozenDrops;
    private readonly List<AssistanceOpportunityFact> capturedDrops = new();
    private long dropCensusRevision;

    public IReadOnlyList<DecisionFact> Capture(Senses senses, ActionContext context)
    {
        var captured = new List<DecisionFact>();
        PotSweep pots = CapturePots(senses, context);
        captured.AddRange(pots.Facts);
        captured.AddRange(CaptureDrops(senses, context, new(double.PositiveInfinity), pots).Facts);
        CaptureLighting(senses, context, captured);
        return captured.OrderBy(fact => fact.Key).ToArray();
    }

    public void ResetWorld() { itemGenerations.Reset(); factVersions.Clear(); frozenDrops = null; capturedDrops.Clear(); drops = new(); dropCensusRevision = 0; version = 0; }

    /// <summary>Resumable native drop capture. The cursor advances only after a concrete item was
    /// observed, so a borrowed-budget cut reports partial coverage rather than an empty census.
    ///
    /// <para>The coverage fact it publishes is collection's, and collection is two sweeps since pots became
    /// collection work on 23 September 2026: the drop census and <paramref name="pots"/>. It reads complete
    /// only when both are, because a completeness fact that says complete while one half of the domain was
    /// never swept is the silent absence every three-valued answer here exists to refuse — a pot nobody
    /// looked for would read as "looked, nothing there".</para></summary>
    public AssistanceCaptureSlice CaptureDrops(Senses senses, ActionContext context, DecisionWorkBudget budget, PotSweep pots)
    {
        var facts = new List<DecisionFact>();
        if (frozenDrops == null)
        {
            // Copy the finite observation census before discretionary geometry is sliced.
            // An Item reference is live state: even its type can change while a later
            // slice is pending. Only this private clone reaches landing/contact analysis.
            itemGenerations.BeginCapture();
            frozenDrops = senses.Loot.Pickups.Where(p => LootSense.IsWorldDrop(p.Item) && !ItemID.Sets.IsAPickup[p.Item.type])
                .OrderBy(p => p.Item.whoAmI).Select(p =>
                {
                    long generation = itemGenerations.Observe(p.Item, out bool replaced);
                    itemGenerations.MarkSeen(p.Item);
                    Item copy = p.Item.Clone();
                    copy.whoAmI = p.Item.whoAmI;
                    return new FrozenDrop(copy, generation, replaced);
                }).ToArray();
            itemGenerations.EndCapture();
            capturedDrops.Clear();
            drops.Bind(++dropCensusRevision, "completed-census-refresh");
        }
        FrozenDrop[] candidates = frozenDrops;
        while (drops.Offset < candidates.Length && budget.TrySpend("capture-drops"))
        { capturedDrops.Add(CaptureDrop(senses, context, candidates[(int)drops.Offset])); drops.Advance(); }
        if (drops.Offset == candidates.Length)
        {
            drops.Complete();
            var totals = candidates.GroupBy(drop => (drop.Value.type, drop.Value.prefix))
                .ToDictionary(group => group.Key, group => (double)group.Sum(drop => drop.Value.stack));
            foreach (var drop in capturedDrops)
            {
                var value = drop with { CensusAmount = totals[(drop.ItemType, (byte)drop.Prefix)] };
                facts.Add(Fact("collect-target", value.Target, value.Generation, value));
            }
            frozenDrops = null; // Only a completed census admits a new world scan.
        }
        var coverage = new AssistanceCaptureCoverage("capture-drops", drops.Offset, candidates.Length, drops.Exhausted,
            budget.Cut && drops.Offset < candidates.Length);
        // Both halves are named in the text, so a reader can tell which world a complete answer is complete
        // about: the drop cursor's own coverage, then the pot window.
        string coverageText = JsonSerializer.Serialize(coverage with { BudgetCut = false })
            + $";pots={(pots.Complete ? "exhaustive" : "unscanned")};pot-area={pots.Area.Left},{pots.Area.Top}:{pots.Area.Width}x{pots.Area.Height}";
        var coverageKey = new FactKey("collect-coverage", "native-census");
        long coverageVersion = factVersions.TryGetValue(coverageKey, out var previous) && previous.Text == coverageText ? previous.Version : ++version;
        factVersions[coverageKey] = (coverageText, coverageVersion);
        facts.Add(new(coverageKey, coverageVersion, new(Text: coverageText),
            drops.Exhausted && pots.Complete ? FactEvidence.Observed : FactEvidence.Unresolved));
        return new(facts, coverage);
    }

    private AssistanceOpportunityFact CaptureDrop(Senses senses, ActionContext context, FrozenDrop frozen)
    {
        Item item = frozen.Value; long generation = frozen.Generation;
        int accepted = context.Companion.Bag.AcceptableQuantity(item, context.Player); Vector2? landing = CollectNearbyItems.ForecastDropLanding(item);
        Point? contact = landing is Vector2 landed ? CollectNearbyItems.FindDropContactPose(item, landed) : null;
        bool touching = CollectNearbyItems.HasProvenPickupContact(context.Npc.Hitbox, item);
        Vector2 hover = context.Npc.Center;
        // Contact pickup needs no route and must not wait for a reach flood. For a
        // remote drop the contact pose itself, rather than a neighbouring tool pose,
        // is the point whose arrival slack was proven by FindDropContactPose.
        Reachability.Reach approach = touching ? Reachability.Reach.Yes : contact is Point pose
            ? senses.Reach.Reachable(pose) switch
            { ReachVerdict.Reachable => Reachability.Reach.Yes, ReachVerdict.Unreachable => Reachability.Reach.No, _ => Reachability.Reach.Unknown }
            : Reachability.Reach.Unknown;
        if (!touching && contact is Point at) hover = MovementQueries.HoverPoint(at);
        Vector2 target = landing ?? item.Bottom;
        float radius = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        bool workAllowed = Vector2.DistanceSquared(target, senses.Intent.Region.Heading) <= radius * radius
            && Vector2.DistanceSquared(context.Npc.Center, senses.Intent.Region.Heading) <= radius * radius;
        string admission = accepted <= 0 || !workAllowed ? "unusable" : touching ? "usable" : landing is null ? "unknown"
            : contact is null || approach == Reachability.Reach.No ? "unusable" : approach == Reachability.Reach.Unknown ? "unknown" : "usable";
        string reason = accepted <= 0 ? "cargo-capacity" : !workAllowed ? "outside-work-allowance" : touching ? "observed-contact"
            : landing is null ? "landing-undecided" : contact is null ? "no-contact-pose" : approach == Reachability.Reach.Unknown ? "approach-undecided" : approach == Reachability.Reach.No ? "unreachable" : "observed-drop";
        var value = new AssistanceOpportunityFact("collect-target", OpportunityPurposes.Collect, $"item:{item.whoAmI}", generation, item.type, item.stack, target.X, target.Y,
            landing?.X, landing?.Y, !touching && contact is null ? null : hover.X, !touching && contact is null ? null : hover.Y, approach.ToString(), workAllowed,
            Math.Max(0, accepted), item.stack, admission, reason, "identity=observed-slot;replacement-between-observations=unknown", item.prefix);
        return value;
    }

    /// <summary>
    /// Every pot in the window around the body, published as collection work: a pot is a container whose
    /// contents are unknown until the native break produces them, and collection is the job that goes and
    /// gets things (the owner's ruling of 23 September 2026). It publishes under `collect-target` with the
    /// purpose `break-pot`, so the course prices a pot against drops and everything else in one domain and
    /// the activity that performs a drop performs a pot.
    ///
    /// The scan sweeps its whole window with no cursor and no borrowed allowance, so unlike the drop census
    /// it cannot stop part-way; the sweep it returns is complete, and collection's coverage fact carries that
    /// beside the drop cursor's own. There is no `pot-coverage` fact any more — a second completeness for
    /// one domain is the fact a reader would consult for the wrong half.
    /// </summary>
    public PotSweep CapturePots(Senses senses, Activities.ActionContext context)
    {
        var facts = new List<DecisionFact>();
        Point centre = context.Npc.Center.ToTileCoordinates();
        Rectangle scanned = new(centre.X - 18, centre.Y - 14, 37, 29);
        for (int x = centre.X - 18; x <= centre.X + 18; x++)
            for (int y = centre.Y - 14; y <= centre.Y + 14; y++)
            {
                if (!WorldGen.InWorld(x, y, 6)) continue;
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || tile.TileType != TileID.Pots) continue;
                Point point = new(x - tile.TileFrameX / 18 % 2, y - tile.TileFrameY / 18 % 2);
                if (point.X != x || point.Y != y) continue;
                bool protectedHome = ProtectCompanionHomes.IsProtected(point);
                bool enabled = PlayerIntegration.CompanionPreferences.Current.PotBreaking;
                bool capacity = context.Companion.Bag.Count < Inventory.CompanionInventory.Slots;
                ReachVerdict reach = senses.Reach.Reachable(point);
                // A pot is a two-by-two whose origin is its top-left, so the body is placed against the
                // origin tile rather than against the centre the site publishes; the same seam as a
                // torch, and it gets the same answer rather than a second one.
                var contact = WorkingPose(point);
                string admission = !enabled ? "unusable" : !capacity ? "unusable" : protectedHome ? "unusable"
                    : contact == null ? "unusable"
                    : reach == ReachVerdict.NotYet ? "unknown" : reach == ReachVerdict.Unreachable ? "unusable" : "usable";
                string reason = !enabled ? "pot-breaking-disabled" : !capacity ? "cargo-full-for-unknown-contents"
                    : protectedHome ? "protected" : contact == null ? "no-pose-holds-the-body-within-reach"
                    : reach == ReachVerdict.NotYet ? "reach-not-yet"
                    : reach == ReachVerdict.Unreachable ? "reach-unreachable" : "observed-pot-contents-unknown";
                var value = new AssistanceOpportunityFact("collect-target", OpportunityPurposes.BreakPot, $"tile:{point.X},{point.Y}", 0,
                    point.X * 16 + 16, point.Y * 16 + 16,
                    1, 1, admission, reason, "contents=unknown;policy=" + enabled + ";capacity=" + capacity + ";origin=2x2", contact);
                facts.Add(Fact("collect-target", value.Target, 0, value));
            }
        return new PotSweep(facts, scanned, Complete: true);
    }

    private void CaptureLighting(Senses senses, Activities.ActionContext context, List<DecisionFact> facts)
    {
        int work = (int)(global::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowWorkRadius / 16f);
        Point heading = context.Senses.Intent.Region.Heading.ToTileCoordinates();
        Rectangle area = new(heading.X - work, heading.Y - work, 2 * work + 1, 2 * work + 1);
        LightSense.Coverage coverage = LightSense.Coverage.Current();
        if (!coverage.Legacy) area = Rectangle.Intersect(area, coverage.Area);
        // Chosen once for the sweep rather than per tile: which torch goes in does not vary by site, and
        // the step reads the item only for its tile and style. It is the same choice the placer makes,
        // through the same function, so the census cannot admit a site against a torch the placer would
        // not be holding.
        Item torchToPlace = PlaceTorches.TorchToPlace(context.Companion.Bag.Items, context.Player.inventory);
        // The step reads a neighbourhood around each tile, so an edit just outside the swept window can
        // still change an answer inside it; the sensitive region is the window grown by that reach.
        Rectangle sensitive = area;
        sensitive.Inflate(CompanionTorches.SpacingTiles, CompanionTorches.SpacingTiles);
        if (TerrainChanges.Edits.ChangedSince(placementStepRevision, (x, y) => sensitive.Contains(x, y))
            != TerrainEditVerdict.Unchanged)
            placementStep.Clear();
        placementStepRevision = TerrainChanges.Revision;
        // The whole area is swept, so the census is complete for the area it names — but only if that
        // area is a place. In colour mode the window is intersected with the engine's own processed
        // area, which is empty before the engine has ever scanned near the companion, and an empty
        // rectangle sweeps nothing while still reporting a finished sweep. That would be a finished
        // census of a world nobody looked at, which is the one confusion coverage exists to prevent, so
        // an empty area publishes Unresolved and the domain honestly reports an unanswered question.
        // The site the course is working right now is published whatever it ranks, which is hysteresis
        // rather than a favour: the set below is re-ranked from scratch every observation, and an absent
        // unpinned fact is what `RetireAdmissionsThisObservationCannotSupport` retires, so a site that
        // drifted one rank would be torn out from under a course already flying to it.
        string? workingOn = PinnedLightSite(context);
        var swept = new List<RankCensusSitesByWorth.Candidate<AssistanceOpportunityFact>>();
        // A census publishes opportunities, not tiles.
        //
        // This loop used to emit a `light-target` fact for every tile in the window — a 125x125 square
        // at the work radius, so 15,625 facts per brain tick, each carrying an interpolated detail string
        // and a JSON payload, the overwhelming majority of them saying "this is a wall" or "this is lit".
        // The cost was quadratic rather than merely large, because five call sites answer "give me the
        // facts of kind K" with `facts.Facts.Where(...).OrderBy(...)`, one of them inside the combat
        // binder, which therefore re-sorted fifteen thousand irrelevant facts once per binding attempt
        // per candidate order. Discovery then spent one unit of the shared allowance per site, so the
        // light domain drained the whole tick's budget on walls and every other domain starved behind it.
        //
        // Absence is already meaningful here and that is what makes this safe rather than a trim. The
        // `light-coverage` fact records the area that was swept, and `DiscoverAssistanceOpportunities`
        // reads completeness from that rather than from the presence of any particular tile — so a tile
        // with no fact inside a swept area reads as "looked, nothing there", which is exactly the
        // three-valued answer the whole tree is built on. What was being published for a wall was never
        // an opportunity; it was the sweep narrating itself.
        //
        // The tile predicates run first and cheapest, before the light reading and the reach query, so a
        // wall costs two calls instead of four.
        for (int x = area.Left; x < area.Right; x++)
            for (int y = area.Top; y < area.Bottom; y++)
            {
                if (!WorldGen.InWorld(x, y, 10)) continue;
                Point point = new(x, y);
                // `MayAccept` is a filter and never a decision — its own guide says so, and says it
                // deliberately passes tiles the step refuses, a torch within the game's spacing among
                // them. The census stopped at the filter and published those tiles as usable sites; the
                // placer then asked the real step and refused them, so the course would fly to a site
                // and place nothing. Measured on the two-site lighting scene: the course chained to the
                // tile directly above the torch it had just placed, arrived, and held that step for 1798
                // ticks. The census asks what the placer asks, in the placer's own order — the cheap
                // filter first so the step is skipped on the empty tiles it exists to skip, then the
                // step itself, which is the only thing that can say yes.
                if (!PlaceTorches.Candidate(point) || !RecommendTorchPlacement.MayAccept(point)) continue;
                var reading = senses.Light.ReadForPlacement(point, coverage);
                ReachVerdict reach = senses.Reach.Reachable(point);
                // Only a placeable tile reaches here, so the admission turns on light and reach alone.
                // A tile the policy or a body refuses is not published at all, which is why there is no
                // `placement-policy-or-contact` arm any more: that reason described the absence of an
                // opportunity rather than a property of one.
                // A site with no pose the body can hold is refused here rather than published and then
                // refused by travel, because travel's refusal is per order and this one is per site: the
                // same unusable site was otherwise re-bound inside every order that contained it.
                var contact = WorkingPose(point);
                // The placer's own step is asked last, and only of a site every cheaper test has already
                // admitted, because it is by far the most expensive question here — it scans an
                // eight-tile neighbourhood per call — and it can only ever downgrade an answer, never
                // rescue one. Asking it before the light reading spent that scan on tiles refused for
                // being bright, which on a lit screen is nearly all of them.
                //
                // It has to be asked at all because `MayAccept` above is a filter and never a decision:
                // its own guide says it deliberately passes tiles the step refuses, a torch within the
                // game's spacing among them. Stopping at the filter published those tiles as usable
                // sites that the placer then refused, so the course flew to a site and placed nothing —
                // measured as a chain onto the tile directly above the torch just placed, held for 1798
                // ticks. The census asks what the placer asks, through the same function.
                bool cheapUsable = reading.IsDark && contact != null && reach == ReachVerdict.Reachable;
                // A site nobody could afford to ask the step about is unknown, never usable — the same
                // three-valued rule the reach and light senses keep, applied to the one question left.
                // Reading an unaffordable question as a yes is how a companion flies to a site the placer
                // then refuses; reading it as a no would write the site off on evidence nobody gathered.
                bool? stepAnswer = !cheapUsable ? false
                    : placementStep.TryGetValue(point, out bool remembered) ? remembered
                    : !LimitPlanningWork.IsActive || LimitPlanningWork.Current.TrySpend("torch-placement-step")
                        ? placementStep[point] = RecommendTorchPlacement.Accepts(point, torchToPlace, context.Companion.StandIn.Player)
                        : null;
                string admission = !reading.IsDark ? reading.Light == LightSense.PlacementLight.Unread ? "unknown" : "unusable"
                    : contact == null ? "unusable"
                    : reach == ReachVerdict.NotYet ? "unknown" : reach == ReachVerdict.Unreachable ? "unusable"
                    : stepAnswer == null ? "unknown" : stepAnswer == false ? "unusable" : "usable";
                string reason = !reading.IsDark ? reading.Light == LightSense.PlacementLight.Unread ? "light-unread" : "not-persistently-dark"
                    : contact == null ? "no-pose-holds-the-body-within-reach"
                    : reach == ReachVerdict.NotYet ? "reach-not-yet" : reach == ReachVerdict.Unreachable ? "reach-unreachable"
                    : stepAnswer == null ? "placement-step-not-yet-asked"
                    : stepAnswer == false ? "the-placement-step-refuses-this-tile" : "observed-persistent-darkness";
                var value = new AssistanceOpportunityFact("light-target", OpportunityPurposes.Light, $"tile:{x},{y}", 0, x * 16 + 8, y * 16 + 8,
                    reading.IsDark ? 1 : 0, reading.IsDark ? 1 : 0, admission, reason, $"light={reading.Light};brightness={reading.Brightness:R};coverage={coverage.Area}", contact);
                // Ranked by darkness rather than by `Amount`, because a light site's `Amount` is the
                // binary `IsDark` the need arithmetic wants — every usable site scores exactly 1, so a
                // top-K by Amount would be the same arbitrary prefix that tile-identity order already
                // gives, wearing a ranking's name. Brightness is the quantity that actually separates
                // two dark tiles, and the tier ahead of it keeps the ranking answering the right
                // question first: a usable site is never dropped to publish an unusable one. Worth is
                // the last word rather than the first, because within one spacing cell the sites are
                // substitutes and cost decides between them; `RankCensusSitesByWorth` owns that order
                // and the measurement that forced it.
                int tier = admission == "usable" ? 0 : admission == "unknown" ? 1 : 2;
                // Cost is squared tile distance from the heading, which is the anchor every "near the
                // player" measure in this tree already uses and the centre of the window being swept.
                // It decides which sites survive a cut, so it is measured from the heading rather than
                // from the body deliberately: the body moves every tick, and a cut keyed to it would
                // change the published set under a flying companion and retire admissions on ticks when
                // nothing about the world moved.
                double dx = x - heading.X, dy = y - heading.Y;
                swept.Add(new(point, tier, 1 - reading.Brightness, dx * dx + dy * dy, value));
            }
        // The limit is the window's own arithmetic rather than a number: two torches must be more than
        // the placer's spacing apart in both axes, so a window this wide can never usefully hold more
        // than this many of them however dark it is, and publishing past that is spending the decision's
        // budget on answers no placement could take.
        (List<AssistanceOpportunityFact> published, int withheld) = PublishLightSites(swept, work, workingOn);
        foreach (AssistanceOpportunityFact site in published) facts.Add(Fact("light-target", site.Target, 0, site));
        facts.Add(Coverage("light-coverage", area, area.Width > 0 && area.Height > 0, withheld));
    }

    /// <summary>
    /// The light site a published course is working right now, or null when it is working anything else.
    ///
    /// It is a named function rather than two lines at its one call site because it is half of the
    /// hysteresis rule and the half a fixture cannot otherwise reach: a row driving
    /// <c>RankCensusSitesByWorth.PublishTheBest</c> with a keep predicate of its own proves the helper
    /// keeps what it is told to keep, and says nothing about whether the census tells it anything. The
    /// keep predicate and this derivation neutralised independently are two different defects, and until
    /// 22 September 2026 only the first had a witness — neutralising the production call site's predicate
    /// left all seven of the census's rows green.
    /// </summary>
    public static string? PinnedLightSite(ActionContext context)
        => context.Companion.Brain.Course.Last.Binding is { } bound
            && bound.Opportunity.Domain == "light-target" ? bound.Opportunity.Target : null;

    /// <summary>
    /// The production cut: the window's own arithmetic as the bound, and the site a course is working
    /// pinned past it. This is the call `CaptureLighting` makes, so a row driving it drives the bound,
    /// the rank and the pin as one thing rather than re-deciding any of them for itself.
    /// </summary>
    public static (List<AssistanceOpportunityFact> Published, int Withheld) PublishLightSites(
        IReadOnlyList<RankCensusSitesByWorth.Candidate<AssistanceOpportunityFact>> swept, int workTiles, string? workingOn)
        => RankCensusSitesByWorth.PublishTheBest(swept,
            RankCensusSitesByWorth.MostSitesAWindowCanHold(workTiles, CompanionTorches.SpacingTiles),
            site => site.Target == workingOn);

    /// <summary>
    /// Where the body must hover to work a tile, which is not the tile.
    ///
    /// A drop's capture has always published a contact pose, because a pickup happens where the item
    /// lies. A tile site published the tile's own centre and left the contact pose null, and the binder
    /// then handed that centre to travel as a destination — with a comment saying the positioner's
    /// tool-reach proof would admit the hover beside it, which is true and happens far too late.
    /// `CaptureCourseTravel` refuses any destination whose circle overlaps terrain before a positioner
    /// ever sees the binding, so a torch site sitting on a floor was refused by construction: the orb's
    /// radius is ten pixels and a tile is sixteen, so a circle centred in the air tile above a floor
    /// reaches ten pixels down into eight pixels of clearance. Measured on the two-site lighting scene,
    /// that refused 130 of 133 orders with `travel-endpoint-overlaps-terrain` and left the companion
    /// holding one unexecutable course for 1798 ticks.
    ///
    /// The durable shape, which is why this is a capture-side answer rather than a check at the travel
    /// gate: **a work site and a body destination are two different quantities, and one field carrying
    /// both means the two ends of the seam disagree without either being wrong on its own.** The binder
    /// is pure and reads a frozen snapshot, so it cannot do this geometry; the capture runs natively
    /// beside the tile world and can. Every tile domain therefore publishes both — `X`/`Y` stays the
    /// site the hand acts on, and the contact pose is where the body waits while it does.
    ///
    /// Candidates are the site itself first, because a torch in open air with room for the body is the
    /// common case and costs one test, then the eight neighbours, then the ring beyond them. Ordered by
    /// true distance so the nearest usable hover wins, and bounded at two tiles because a pose further
    /// away than the tool's own reach cannot work the tile anyway — `InReach` is the second test rather
    /// than a distance constant, so the bound follows the reach the player actually granted.
    /// </summary>
    private static (double X, double Y)? WorkingPose(Point tile)
    {
        var world = MovementQueries.World;
        if (world == null) return null;
        var site = new Vector2(tile.X * 16 + 8, tile.Y * 16 + 8);
        (double X, double Y)? best = null;
        double bestDistance = double.PositiveInfinity;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                var candidate = new Vector2(site.X + dx * 16, site.Y + dy * 16);
                double distance = Vector2.DistanceSquared(candidate, site);
                if (distance >= bestDistance) continue;
                if (CircleContact.Overlaps(world, candidate)) continue;
                if (!FindToolAccess.InReach(candidate, tile)) continue;
                best = (candidate.X, candidate.Y);
                bestDistance = distance;
            }
        return best;
    }

    /// <summary>The census-completeness fact a discovery source reads before it may call its own
    /// enumeration exhausted. It carries the swept area so a reader can tell which world a complete
    /// answer is complete about, and it is versioned by that text so an unchanged window keeps one
    /// version across observations rather than dirtying every dependent estimate each tick.</summary>
    /// <summary>
    /// What was swept, and — since 22 September 2026 — how much of what was swept is not in the snapshot.
    ///
    /// `withheld` is the third value this census owes, and it is on the coverage fact rather than on the
    /// sites because it is a statement about the *sweep*. A site absent from a swept area used to mean
    /// exactly one thing, "looked, nothing there", which is what let the census stop publishing walls; a
    /// bounded census makes absence mean two things, and without this count a caller cannot tell a
    /// neighbourhood with no dark tile from a neighbourhood whose dark tile lost its bucket. Zero is the
    /// old meaning and is the ordinary case; non-zero says the sweep answered about more than it
    /// published and the remainder is *not yet ranked* rather than absent.
    /// </summary>
    private DecisionFact Coverage(string kind, Rectangle scanned, bool swept = true, int withheld = 0)
    {
        var key = new FactKey(kind, "native-census");
        // The area is named rather than only the verdict, because "complete" is only meaningful about
        // somewhere. World edges are the one caveat the word "exhaustive" overstates: both scans skip a
        // margin through WorldGen.InWorld, so a window overlapping the edge of the world sweeps slightly
        // less than the rectangle it publishes.
        string text = $"{(swept ? "exhaustive" : "unscanned")};area={scanned.Left},{scanned.Top}:{scanned.Width}x{scanned.Height};world-edge-margin-skipped;withheld={withheld}";
        long current = factVersions.TryGetValue(key, out var prior) && prior.Text == text ? prior.Version : ++version;
        factVersions[key] = (text, current);
        return new(key, current, new FactValue(Text: text), swept ? FactEvidence.Observed : FactEvidence.Unresolved);
    }

    private DecisionFact Fact(string kind, string identity, long generation, AssistanceOpportunityFact value)
    {
        var key = new FactKey(kind, identity, generation);
        string text = JsonSerializer.Serialize(value);
        long current = factVersions.TryGetValue(key, out var prior) && prior.Text == text ? prior.Version : ++version;
        factVersions[key] = (text, current);
        return new(key, current, new FactValue(Amount: value.Amount, X: value.X, Y: value.Y, Text: text), FactEvidence.Observed);
    }
}
