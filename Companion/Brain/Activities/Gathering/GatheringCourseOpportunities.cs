#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Interactions;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Mining;
using AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>One immutable native observation of a mine or chop purpose.  The source below only
/// deserializes this value; live tiles, policies and reachability are deliberately absent there.</summary>
public sealed record GatheringOpportunityFact(string Domain, string Target, long Generation, int TileX, int TileY,
    int Material, string Purpose, double RemainingAmount, double CensusAmount, string Admission, string Reason,
    string Detail, double StandX = 0, double StandY = 0, CapturedToolWork? Work = null);

/// <summary>The native mechanism's next physical application, separate from the complete vein census.</summary>
public sealed record CapturedToolWork(int ItemType, int Prefix, int Power, int UseTime, int DamagePerHit, int DamageRemaining);

/// <summary>Capture coverage is a fact in its own right.  A bounded native scan has observed a
/// prefix, never proved the rest of the rectangle empty.</summary>
public sealed record GatheringCoverageFact(string Domain, long Generation, long Examined, bool Complete, string Bounds);

/// <summary>Native gathering observation, before MineOre and ChopTree choose a retained target.
/// It carries the actual tool, policy, home-protection and approach verdict that existed during
/// capture.  It never borrows TerrainChanges.Revision as an entity generation: a reappearing
/// coordinate after an unobserved absence gets a new observer generation and explicit gap reason.</summary>
public sealed class CaptureGatheringOpportunities
{
    private sealed record Seen(int Material, long Generation);
    private readonly Dictionary<string, Seen> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<FactKey, DecisionFact> factCache = new();
    private HashSet<string> visibleLastCapture = new(StringComparer.Ordinal);
    private readonly HashSet<string> visibleThisCapture = new(StringComparer.Ordinal);
    /// <summary><paramref name="Tiles"/> and <paramref name="VeinComplete"/> are the flood this site came from,
    /// kept so a change of *geometry* — the body moved, a new flood is answering — can re-answer this one
    /// vein's approach instead of rescanning the whole window to find it again. Finding a vein is a sweep
    /// over the search box; re-approaching a known one is a handful of reach queries.</summary>
    private sealed record ObservedOre(string Identity, long Generation, Point Tile, int Material, double Remaining,
        string Admission, string Reason, string Detail, Vector2 Stand, bool Complete, CapturedToolWork? Work,
        Point[] Tiles, bool VeinComplete, bool ReplacementGap);
    private Rectangle? oreArea;
    private long oreOffset;
    /// <summary>How far through re-answering the known veins against a changed body or flood this capture
    /// has got. It is a cursor rather than a loop because the re-answer spends the decision's own allowance.</summary>
    private int oreReanswer;
    private readonly Dictionary<string, ObservedOre> observedOres = new(StringComparer.Ordinal);
    private readonly HashSet<Point> oreVisited = new();
    private int terrainRevision;
    private (int Type, int Prefix, int Power) pickSignature;
    private (WorkPolicy Policy, object List, int Revision) policySignature;
    private long minerAttempt = -1;
    /// <summary>
    /// The reach the ore admissions were proved under. An admission of "no approach" is a claim about the
    /// flood that answered it, and the sense says so itself: a refusal proved from one finished flood
    /// stays true only while that flood is the one answering, which is what <c>FloodGeneration</c> exists
    /// to key on. Without it, an ore read during a young flood keeps its refusal for as long as nothing
    /// else in the signature moves — and nothing else does, because the rest of the signature is the pick,
    /// the policy, the area, a terrain edit and the miner's last attempt, none of which a companion that
    /// is not mining ever changes. The tree capture beside this one has keyed on it since it was written.
    /// </summary>
    private (int Generation, bool Complete, Point Body) oreGeometry;
    private long nextGeneration;
    private long version;

    public IReadOnlyList<DecisionFact> Capture(in ActionContext context, DecisionWorkBudget budget)
    {
        var facts = new List<DecisionFact>();
        CaptureOres(context, budget, facts, visibleThisCapture);
        facts.Add(new(GatheringOpportunityBinder.ReadyKey("mine-target"), 0,
            new(Amount: context.Companion.Miner.CooldownTicks > 0 ? (double)Main.GameUpdateCount + context.Companion.Miner.CooldownTicks : 0), FactEvidence.Observed));
        // Gathering is two domains and this capture published one of them. CaptureTreeOpportunities
        // was driven by nothing but its own fixture, so a wired brain would have seen no chop-target
        // site and no chop-coverage at all — not an empty forest, an unanswered question, which under
        // the rule that optional work does not start on an unanswered search means never chopping.
        // The tree census owns its own slicing and cursor and shares the same borrowed allowance, so
        // it composes here rather than needing a second caller.
        facts.AddRange(trees.Capture(context, budget));
        return facts.OrderBy(fact => fact.Key).ToArray();
    }

    /// <summary>The trunk census this capture composes. It owns its own cursor and slicing and is
    /// reset with the rest, because a world reload must not leave it holding the old world's trunks.</summary>
    private readonly CaptureTreeOpportunities trees = new();

    public void ResetWorld()
    {
        seen.Clear(); factCache.Clear(); visibleLastCapture.Clear(); visibleThisCapture.Clear(); observedOres.Clear(); oreVisited.Clear(); oreArea = null;
        oreOffset = 0; oreReanswer = 0; nextGeneration = 0; version = 0;
        trees.ResetWorld();
    }

    private void CaptureOres(in ActionContext context, DecisionWorkBudget budget, List<DecisionFact> facts, HashSet<string> visible)
    {
        Rectangle area = SearchArea(context.Senses.Intent.Region.Heading,
            PlayerIntegration.CompanionPreferences.Current.WorkCensusRadiusTiles);
        Item pick = TileMiner.PickaxeFor(context.Player);
        var policy = (WorkPolicies.Mining, WorkPolicies.MiningListVersion.List, WorkPolicies.MiningListVersion.Revision);
        bool spatialEdit = oreArea == area && TerrainChanges.Edits.ChangedSince(terrainRevision,
            (x, y) => x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom) != TerrainEditVerdict.Unchanged;
        bool changedInputs = pickSignature != (pick.type, pick.prefix, pick.pick) || policySignature != policy
            || minerAttempt != (context.Companion.Miner.LastOutcome?.Attempt ?? -1);
        long cells = (long)area.Width * area.Height;
        var geometry = (context.Senses.Reach.FloodGeneration, context.Senses.Reach.Complete,
            context.Npc.Center.ToTileCoordinates());
        // A moved body or a new flood invalidates every *admission* in a finished scan, because each was
        // proved against the flood and the body of the moment it was read — but it invalidates none of the
        // *discoveries*, because where the ore is has nothing to do with where the companion is. Those are
        // two questions and reopening the scan answered the cheap one by re-paying for the expensive one:
        // a sweep of the whole window to find veins it already knew about, on every tick the body moved,
        // which is most ticks. The re-answer below asks the reach question of each known vein instead —
        // a handful of queries against a window of sixteen thousand cells — and only a terrain edit inside
        // the window, a changed pick or list, or the window itself moving reopens the sweep.
        //
        // It is deliberately not done mid-scan: a change while the cursor is still running would erase it
        // every tick and the capture would never publish anything at all, and the binder revalidates each
        // site at its own boundary in any case.
        bool changedGeometry = oreOffset == cells && oreGeometry != geometry;
        if (oreOffset > 0 && (spatialEdit || changedInputs)) oreArea = null;
        else if (changedGeometry)
        {
            // Resumable, because the re-answer shares the decision's allowance like everything else: the
            // geometry is only marked answered once every site has been re-read against it, so a cut
            // leaves the rest for the next capture rather than claiming an answer nobody computed.
            while (oreReanswer < observedOres.Count && budget.TrySpend("gathering-native-reanswer"))
            {
                string key = observedOres.Keys.OrderBy(identity => identity, StringComparer.Ordinal).ElementAt(oreReanswer);
                ObservedOre stale = observedOres[key];
                observedOres[key] = Admit(context, pick, stale.Identity, stale.Material, stale.Generation,
                    stale.Tiles, stale.VeinComplete, stale.ReplacementGap);
                oreReanswer++;
            }
            if (oreReanswer >= observedOres.Count) { oreGeometry = geometry; oreReanswer = 0; }
        }
        if (oreArea != area)
        {
            oreArea = area; oreOffset = 0; oreReanswer = 0; observedOres.Clear(); oreVisited.Clear(); visibleThisCapture.Clear();
            terrainRevision = TerrainChanges.Revision;
            pickSignature = (pick.type, pick.prefix, pick.pick);
            policySignature = policy;
            minerAttempt = context.Companion.Miner.LastOutcome?.Attempt ?? -1;
            oreGeometry = geometry;
        }
        while (oreOffset < cells)
        {
            // One budget unit buys a block of empty cells or one vein, because those are the two kinds of
            // work this loop does and they differ by four orders of magnitude: a cell that is not ore is a
            // tile-type read, while a cell that is pays for a flood census of its vein and a reach query per
            // tile. Charged per cell, the sweep's accounted cost was its *area* rather than its work, so
            // widening the window to the admission radius (8,281 cells to 16,129) doubled the charge and the
            // census stopped finishing inside a decision: measured through `--crowd-cost`, decide's median
            // went 4.26 ms to 11.3 ms against its own 12 ms allowance, which is a brain spending every tick
            // of its thinking on a tile scan. Per block it is 63 units for the whole window.
            if (oreOffset % ScanBlockCells == 0 && !budget.TrySpend("gathering-native-capture-block")) break;
            int x = area.Left + (int)(oreOffset % area.Width);
            int y = area.Top + (int)(oreOffset / area.Width);
            oreOffset++;
            Point seed = new(x, y);
            if (!OreFinder.IsOre(x, y) || oreVisited.Contains(seed)) continue;
            if (!budget.TrySpend("gathering-native-capture-vein")) { oreOffset--; break; }
            int material = Main.tile[x, y].TileType;
            OreFinder.VeinCensus vein = OreFinder.CensusVein(seed, material);
            foreach (Point tile in vein.Tiles) oreVisited.Add(tile);
            Point[] tiles = vein.Tiles.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).ToArray();
            string identity = $"ore:{material}:{tiles[0].X},{tiles[0].Y}";
            long generation = Generation(identity, material, visible, out bool replacementGap);
            observedOres[identity] = Admit(context, pick, identity, material, generation, tiles, vein.Complete, replacementGap);
        }
        bool complete = oreOffset == cells;
        // A partial scan proves no denominator.  It publishes only coverage, so it cannot rescale
        // already compared work or manufacture an absence for cells it has not reached.
        if (complete)
        {
            visibleLastCapture = new HashSet<string>(visibleThisCapture, StringComparer.Ordinal);
            foreach (ObservedOre ore in observedOres.Values.OrderBy(ore => ore.Identity))
            {
                double census = observedOres.Values.Where(other => other.Material == ore.Material).Sum(other => other.Remaining);
                var value = new GatheringOpportunityFact("mine-target", ore.Identity, ore.Generation, ore.Tile.X, ore.Tile.Y, ore.Material, "mine",
                    ore.Remaining, census, ore.Admission, ore.Reason, ore.Detail, ore.Stand.X, ore.Stand.Y, ore.Work);
                facts.Add(Fact(value, ore.Complete ? FactEvidence.Observed : FactEvidence.Unresolved));
            }
        }
        facts.Add(Coverage("mine-coverage", oreOffset, complete, area));
    }

    /// <summary>Starts a new native census after an observed tile change while retaining the
    /// generation ledger; the next completed scan can distinguish a continuously observed purpose
    /// from one that disappeared between observations.</summary>
    public void Invalidate() => oreArea = null;

    /// <summary>
    /// What one known vein is worth right now: its best approach, its admission and its remaining native
    /// work, against the body and the flood of this moment. It is one function because it is asked from two
    /// places — the sweep that discovers a vein, and the re-answer that a moved body forces on a vein
    /// already discovered — and two copies of an admission ladder is how the two answers drift into
    /// disagreeing about the same ore.
    /// </summary>
    private static ObservedOre Admit(in ActionContext context, Item pick, string identity, int material,
        long generation, Point[] tiles, bool veinComplete, bool replacementGap)
    {
        var miner = context.Companion.Miner;
        var approaches = new List<(Point Tile, Reachability.Reach Reach, Vector2 Pose)>();
        foreach (Point tile in tiles)
        {
            Reachability.Reach approach = FindToolAccess.Approach(tile, context.Npc.Center, context.Senses.Reach, out Vector2 pose);
            approaches.Add((tile, approach, pose));
        }
        var chosen = approaches.FirstOrDefault(candidate => candidate.Reach == Reachability.Reach.Yes);
        if (chosen.Reach != Reachability.Reach.Yes)
            chosen = approaches.FirstOrDefault(candidate => candidate.Reach == Reachability.Reach.Unknown);
        Point target = chosen.Tile == default ? tiles[0] : chosen.Tile;
        Reachability.Reach reach = chosen.Tile == default ? Reachability.Reach.No : chosen.Reach;
        Vector2 stand = chosen.Tile == default ? default : chosen.Pose;
        bool listed = WorkPolicies.MinesOre(material);
        bool policyEnabled = WorkPolicies.Mining != WorkPolicy.Disabled;
        bool allowed = InNewActivityAllowance(context, target) && !ProtectCompanionHomes.IsProtected(target);
        bool mineable = miner.CanMine(target, pick.pick);
        string admission = !policyEnabled || !listed || !allowed || !mineable ? "unusable"
            : reach == Reachability.Reach.Unknown ? "unknown" : reach == Reachability.Reach.Yes ? "usable" : "unusable";
        string reason = !policyEnabled ? "mining-disabled" : !listed ? "mining-list" : !allowed ? "outside-allowance-or-protected"
            : !mineable ? "pickaxe-cannot-damage" : reach == Reachability.Reach.Unknown ? "approach-not-yet"
            : reach == Reachability.Reach.Yes ? "observed-native-ore" : "approach-unreachable";
        if (replacementGap) reason += ";replacement-observation-gap";
        RemainingToolWork?[] estimates = tiles.Select(tile => miner.EstimateRemaining(tile, pick)).ToArray();
        bool remainingKnown = estimates.All(estimate => estimate != null);
        double remaining = remainingKnown ? estimates.Sum(estimate => estimate!.Value.DamageRemaining) : 0;
        if (!remainingKnown) { admission = "unknown"; reason = "native-remaining-unresolved"; }
        RemainingToolWork? targetWork = miner.EstimateRemaining(target, pick);
        CapturedToolWork? work = targetWork is { } next
            ? new(pick.type, pick.prefix, pick.pick, pick.useTime, next.DamagePerHit, next.DamageRemaining) : null;
        return new(identity, generation, target, material, remaining, admission, reason,
            $"vein-complete={veinComplete};remaining-known={remainingKnown};pick={pick.pick};listed={listed};policy={WorkPolicies.Mining};reach={reach}",
            stand, veinComplete && remainingKnown, work, tiles, veinComplete, replacementGap);
    }

    /// <summary>How many cells of the sweep one budget unit buys. It is a block rather than a cell because a
    /// cell that holds no ore costs a tile-type read; see the charge at the sweep for what charging per cell
    /// did to the decision allowance. The tree census beside this one uses the same constant.</summary>
    internal const int ScanBlockCells = 256;

    private static Rectangle SearchArea(Vector2 centre, int radius) => new((int)(centre.X / 16f) - radius, (int)(centre.Y / 16f) - radius,
        2 * radius + 1, 2 * radius + 1);
    private static bool InNewActivityAllowance(in ActionContext context, Point point)
    {
        float radius = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        return Vector2.DistanceSquared(point.ToWorldCoordinates(), context.Senses.Intent.Region.Heading) <= radius * radius
            && Vector2.DistanceSquared(context.Npc.Bottom, context.Senses.Intent.Region.Heading) <= radius * radius;
    }
    private long Generation(string identity, int material, HashSet<string> visible, out bool replacementGap)
    {
        replacementGap = false;
        if (seen.TryGetValue(identity, out Seen? prior) && prior.Material == material && visibleLastCapture.Contains(identity))
        { visible.Add(identity); return prior.Generation; }
        replacementGap = seen.ContainsKey(identity) && !visibleLastCapture.Contains(identity);
        long generation = ++nextGeneration;
        seen[identity] = new(material, generation); visible.Add(identity);
        return generation;
    }
    private DecisionFact Fact(GatheringOpportunityFact value, FactEvidence evidence = FactEvidence.Observed)
    {
        FactKey key = new(value.Domain, value.Target, value.Generation);
        FactValue factValue = new(value.RemainingAmount, value.TileX * 16 + 8, value.TileY * 16 + 8, JsonSerializer.Serialize(value));
        if (factCache.TryGetValue(key, out DecisionFact? prior) && prior.Value == factValue && prior.Evidence == evidence) return prior;
        return factCache[key] = new DecisionFact(key, ++version, factValue, evidence);
    }
    private DecisionFact Coverage(string domain, long examined, bool complete, Rectangle area)
    {
        var value = new GatheringCoverageFact(domain, 0, examined, complete, $"{area.Left},{area.Top}:{area.Width}x{area.Height}");
        FactKey key = new(domain, "native-census");
        FactValue factValue = new(examined, Text: JsonSerializer.Serialize(value));
        FactEvidence evidence = complete ? FactEvidence.Observed : FactEvidence.Unresolved;
        if (factCache.TryGetValue(key, out DecisionFact? prior) && prior.Value == factValue && prior.Evidence == evidence) return prior;
        return factCache[key] = new DecisionFact(key, ++version, factValue, evidence);
    }
}

/// <summary>Pure gathering census reader.  It cannot see Terraria: every candidate, verdict and
/// denominator comes from a serialized DecisionFact captured before activities select a winner.</summary>
public sealed class GatheringOpportunitySource : IOpportunitySource
{
    private sealed class SnapshotCursor { public long Id = long.MinValue; public FactKey[] Prefix = Array.Empty<FactKey>(); }
    private readonly string domain;
    private readonly ConditionalWeakTable<DecisionWorkCursor, SnapshotCursor> snapshots = new();
    public GatheringOpportunitySource(string domain)
    {
        if (domain is not "mine-target" and not "chop-target") throw new ArgumentOutOfRangeException(nameof(domain));
        this.domain = domain;
    }
    public string Name => domain;

    public OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        SnapshotCursor state = snapshots.GetValue(cursor, static _ => new SnapshotCursor());
        if (state.Id != facts.Id)
        {
            // Snapshots advance every brain tick.  A new envelope with the same consumed immutable
            // prefix must not turn a one-operation source into perpetual first-candidate churn.
            DecisionFact[] current = facts.Facts.Where(fact => fact.Key.Kind == domain).OrderBy(fact => fact.Key).ToArray();
            bool retainsPrefix = cursor.Offset <= current.Length && state.Prefix.Length == cursor.Offset
                && state.Prefix.SequenceEqual(current.Take((int)cursor.Offset).Select(fact => fact.Key));
            if (!retainsPrefix) cursor.Rescan();
            state.Prefix = current.Take((int)cursor.Offset).Select(fact => fact.Key).ToArray();
            state.Id = facts.Id;
        }
        DecisionFact[] sites = facts.Facts.Where(fact => fact.Key.Kind == domain).OrderBy(fact => fact.Key).ToArray();
        string coverageKind = domain == "mine-target" ? "mine-coverage" : "chop-coverage";
        DecisionFact coverageFact = facts.Track().Read(new FactKey(coverageKind, "native-census"));
        GatheringCoverageFact? coverage = string.IsNullOrEmpty(coverageFact.Value.Text) ? null
            : JsonSerializer.Deserialize<GatheringCoverageFact>(coverageFact.Value.Text);
        bool captureComplete = coverage?.Complete == true && coverageFact.Evidence == FactEvidence.Observed;
        var examined = new List<Opportunity>();
        while (cursor.Offset < sites.Length && budget.TrySpend(Name))
        {
            DecisionFact raw = sites[(int)cursor.Offset]; cursor.Advance();
            TrackedFactReader reader = facts.Track();
            DecisionFact observed = reader.Read(raw.Key);
            reader.Read(new FactKey(coverageKind, "native-census"));
            GatheringOpportunityFact? site = JsonSerializer.Deserialize<GatheringOpportunityFact>(observed.Value.Text);
            if (site == null || site.Domain != domain || site.Target != raw.Key.Identity || site.Generation != raw.Key.Generation)
                throw new InvalidOperationException("Captured gathering fact did not retain its stable identity.");
            OpportunityAdmission admission = site.Admission switch
            {
                "usable" => OpportunityAdmission.KnownUsable, "unusable" => OpportunityAdmission.KnownUnusable,
                "unknown" => OpportunityAdmission.Unresolved, _ => throw new InvalidOperationException("Unknown gathering admission: " + site.Admission)
            };
            if (observed.Evidence == FactEvidence.Unresolved || !captureComplete)
                admission = OpportunityAdmission.Unresolved;
            var key = new OpportunityKey(domain, site.Purpose, site.Target, site.Generation);
            double amount = Math.Max(0, site.Work?.DamageRemaining ?? site.RemainingAmount);
            double census = Math.Max(1, site.CensusAmount);
            CoursePoint workingPose = site.StandX != 0 || site.StandY != 0 ? new(site.StandX, site.StandY) : new(site.TileX * 16 + 8, site.TileY * 16 + 8);
            IEnumerable<UsefulNeed> needs = site.Reason == "native-remaining-unresolved"
                ? Array.Empty<UsefulNeed>()
                : new[] { new UsefulNeed(GatheringOpportunityBinder.Need(site), amount, census,
                    admission == OpportunityAdmission.KnownUsable ? 1 : 0) };
            examined.Add(new Opportunity(key, observed.Version, workingPose, admission, site.Reason,
                needs, new[] { site.Purpose }, reader.Manifest()));
        }
        if (captureComplete && cursor.Offset == sites.Length) cursor.Complete();
        state.Prefix = sites.Take((int)cursor.Offset).Select(fact => fact.Key).ToArray();
        return new(examined, new(Name, facts.WorldEpoch, cursor.Offset, sites.Length, cursor.Exhausted && captureComplete,
            budget.Cut && cursor.Offset < sites.Length, "snapshot-facts;capture-complete=" + captureComplete + ";cursor=" + cursor.Offset));
    }
}
