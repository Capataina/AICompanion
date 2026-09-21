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
/// receives only its serialized value through <see cref="DecisionFactSnapshot"/>.</summary>
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record AssistanceOpportunityFact(string Domain, string Target, long Generation, int ItemType, int Stack,
    double X, double Y, double? LandingX, double? LandingY, double? ContactX, double? ContactY, string Approach,
    bool WorkAllowed, double Amount, double CensusAmount, string Admission, string Reason, string Detail, int Prefix = 0)
{
    public AssistanceOpportunityFact(string domain, string target, long generation, double x, double y, double amount,
        double censusAmount, string admission, string reason, string detail)
        : this(domain, target, generation, 0, 0, x, y, null, null, null, null, "Unknown", false,
            amount, censusAmount, admission, reason, detail) { }
}
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
    private FrozenDrop[]? frozenDrops;
    private readonly List<AssistanceOpportunityFact> capturedDrops = new();
    private long dropCensusRevision;

    public IReadOnlyList<DecisionFact> Capture(Senses senses, ActionContext context)
    {
        var captured = new List<DecisionFact>();
        captured.AddRange(CaptureDrops(senses, context, new(double.PositiveInfinity)).Facts);
        CapturePots(senses, context, captured);
        CaptureLighting(senses, context, captured);
        return captured.OrderBy(fact => fact.Key).ToArray();
    }

    public void ResetWorld() { itemGenerations.Reset(); factVersions.Clear(); frozenDrops = null; capturedDrops.Clear(); drops = new(); dropCensusRevision = 0; version = 0; }

    /// <summary>Resumable native drop capture. The cursor advances only after a concrete item was
    /// observed, so a borrowed-budget cut reports partial coverage rather than an empty census.</summary>
    public AssistanceCaptureSlice CaptureDrops(Senses senses, ActionContext context, DecisionWorkBudget budget)
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
        string coverageText = JsonSerializer.Serialize(coverage with { BudgetCut = false });
        var coverageKey = new FactKey("collect-coverage", "native-census");
        long coverageVersion = factVersions.TryGetValue(coverageKey, out var previous) && previous.Text == coverageText ? previous.Version : ++version;
        factVersions[coverageKey] = (coverageText, coverageVersion);
        facts.Add(new(coverageKey, coverageVersion, new(Text: coverageText), drops.Exhausted ? FactEvidence.Observed : FactEvidence.Unresolved));
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
        var value = new AssistanceOpportunityFact("collect-target", $"item:{item.whoAmI}", generation, item.type, item.stack, target.X, target.Y,
            landing?.X, landing?.Y, !touching && contact is null ? null : hover.X, !touching && contact is null ? null : hover.Y, approach.ToString(), workAllowed,
            Math.Max(0, accepted), item.stack, admission, reason, "identity=observed-slot;replacement-between-observations=unknown", item.prefix);
        return value;
    }

    private void CapturePots(Senses senses, Activities.ActionContext context, List<DecisionFact> facts)
    {
        Point centre = context.Npc.Center.ToTileCoordinates();
        Rectangle scanned = new(centre.X - 18, centre.Y - 14, 37, 29);
        // The scan sweeps its whole window with no cursor and no borrowed allowance, so unlike the
        // drop census it cannot stop part-way and its coverage is observed rather than unresolved.
        // Without this fact DiscoverAssistanceOpportunities("pot-target") reads pot-coverage as
        // Missing, never reports an exhausted census, and — by the rule that optional work does not
        // start on an unanswered search — no pot is ever broken.
        facts.Add(Coverage("pot-coverage", scanned));
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
                string admission = !enabled ? "unusable" : !capacity ? "unusable" : protectedHome ? "unusable"
                    : reach == ReachVerdict.NotYet ? "unknown" : reach == ReachVerdict.Unreachable ? "unusable" : "usable";
                string reason = !enabled ? "pot-breaking-disabled" : !capacity ? "cargo-full-for-unknown-contents"
                    : protectedHome ? "protected" : reach == ReachVerdict.NotYet ? "reach-not-yet"
                    : reach == ReachVerdict.Unreachable ? "reach-unreachable" : "observed-pot-contents-unknown";
                var value = new AssistanceOpportunityFact("pot-target", $"tile:{point.X},{point.Y}", 0, point.X * 16 + 16, point.Y * 16 + 16,
                    1, 1, admission, reason, "contents=unknown;policy=" + enabled + ";capacity=" + capacity + ";origin=2x2");
                facts.Add(Fact("pot-target", value.Target, 0, value));
            }
    }

    private void CaptureLighting(Senses senses, Activities.ActionContext context, List<DecisionFact> facts)
    {
        int work = (int)(global::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowWorkRadius / 16f);
        Point heading = context.Senses.Intent.Region.Heading.ToTileCoordinates();
        Rectangle area = new(heading.X - work, heading.Y - work, 2 * work + 1, 2 * work + 1);
        LightSense.Coverage coverage = LightSense.Coverage.Current();
        if (!coverage.Legacy) area = Rectangle.Intersect(area, coverage.Area);
        // The whole area is swept, so the census is complete for the area it names — but only if that
        // area is a place. In colour mode the window is intersected with the engine's own processed
        // area, which is empty before the engine has ever scanned near the companion, and an empty
        // rectangle sweeps nothing while still reporting a finished sweep. That would be a finished
        // census of a world nobody looked at, which is the one confusion coverage exists to prevent, so
        // an empty area publishes Unresolved and the domain honestly reports an unanswered question.
        facts.Add(Coverage("light-coverage", area, area.Width > 0 && area.Height > 0));
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
                bool candidate = PlaceTorches.Candidate(point) && RecommendTorchPlacement.MayAccept(point);
                if (!candidate) continue;
                var reading = senses.Light.ReadForPlacement(point, coverage);
                ReachVerdict reach = senses.Reach.Reachable(point);
                // Only a placeable tile reaches here, so the admission turns on light and reach alone.
                // A tile the policy or a body refuses is not published at all, which is why there is no
                // `placement-policy-or-contact` arm any more: that reason described the absence of an
                // opportunity rather than a property of one.
                string admission = !reading.IsDark ? reading.Light == LightSense.PlacementLight.Unread ? "unknown" : "unusable"
                    : reach == ReachVerdict.NotYet ? "unknown" : reach == ReachVerdict.Unreachable ? "unusable" : "usable";
                string reason = !reading.IsDark ? reading.Light == LightSense.PlacementLight.Unread ? "light-unread" : "not-persistently-dark"
                    : reach == ReachVerdict.NotYet ? "reach-not-yet" : reach == ReachVerdict.Unreachable ? "reach-unreachable" : "observed-persistent-darkness";
                var value = new AssistanceOpportunityFact("light-target", $"tile:{x},{y}", 0, x * 16 + 8, y * 16 + 8,
                    reading.IsDark ? 1 : 0, reading.IsDark ? 1 : 0, admission, reason, $"light={reading.Light};brightness={reading.Brightness:R};coverage={coverage.Area}");
                facts.Add(Fact("light-target", value.Target, 0, value));
            }
    }

    /// <summary>The census-completeness fact a discovery source reads before it may call its own
    /// enumeration exhausted. It carries the swept area so a reader can tell which world a complete
    /// answer is complete about, and it is versioned by that text so an unchanged window keeps one
    /// version across observations rather than dirtying every dependent estimate each tick.</summary>
    private DecisionFact Coverage(string kind, Rectangle scanned, bool swept = true)
    {
        var key = new FactKey(kind, "native-census");
        // The area is named rather than only the verdict, because "complete" is only meaningful about
        // somewhere. World edges are the one caveat the word "exhaustive" overstates: both scans skip a
        // margin through WorldGen.InWorld, so a window overlapping the edge of the world sweeps slightly
        // less than the rectangle it publishes.
        string text = $"{(swept ? "exhaustive" : "unscanned")};area={scanned.Left},{scanned.Top}:{scanned.Width}x{scanned.Height};world-edge-margin-skipped";
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
