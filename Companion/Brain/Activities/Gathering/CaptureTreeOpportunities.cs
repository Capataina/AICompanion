#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Interactions;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using AICompanion.Companion.Brain.Infrastructure.Interactions.WorldProtection;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;
using AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

namespace AICompanion.Companion.Brain.Activities.Gathering;

/// <summary>Discovers physical trunks before nearest-tree selection. Each cell spends the borrowed
/// allowance; only a finished finite census supplies a denominator. Native axe work is the damage
/// left on the bottom trunk, not tree height, speculative wood drops or the number of branches.</summary>
public sealed class CaptureTreeOpportunities
{
    private readonly Dictionary<Point, GatheringOpportunityFact> trees = new();
    private readonly Dictionary<Point, (int Material, long Generation)> identities = new();
    private readonly Dictionary<FactKey, DecisionFact> versions = new();
    private HashSet<Point> previous = new();
    private Rectangle? area;
    private long offset, generation, revision;
    /// <summary>How far through re-approaching the known trunks against a changed body or flood this
    /// capture has got; a cursor because the re-answer spends the decision's own allowance like the sweep.</summary>
    private int reanswer;
    /// <summary>The round's trunk keys, taken once when it opens rather than re-sorted per site.</summary>
    private Point[]? reanswerKeys;
    /// <summary>The offset every block up to which has already been charged, so a block that is paid
    /// stays paid across captures.</summary>
    private long blockPaidThrough;
    /// <summary>The swept window unioned with every trunk bottom the census holds, which is what a
    /// terrain edit is tested against: a trunk's bottom can lie below the cell that found it.</summary>
    private Rectangle held;
    private int terrainRevision;
    private long lastAttempt;
    private (int Type, int Prefix, int Power, int UseTime) tool;
    private WorkPolicy policy;
    private (int Generation, bool Complete, Point Body, Point? PlayerTree, bool Chopping, float Allowance) geometry;

    public IReadOnlyList<DecisionFact> Capture(in ActionContext context, DecisionWorkBudget budget)
    {
        // The admission rule's own radius, not a second number beside it: see
        // CompanionPreferences.WorkCensusRadiusTiles for why the two drifted apart and what it cost.
        int radius = PlayerIntegration.CompanionPreferences.Current.WorkCensusRadiusTiles;
        Point centre = context.Senses.Intent.Region.Heading.ToTileCoordinates();
        Rectangle wanted = new(centre.X - radius, centre.Y - radius, radius * 2 + 1, radius * 2 + 1);
        Item axe = TileChopper.AxeFor(context.Player);
        var signature = (axe.type, (int)axe.prefix, axe.axe, axe.useTime);
        long attempt = context.Companion.Chopper.LastOutcome?.Attempt ?? -1;
        var currentGeometry = (context.Senses.Reach.FloodGeneration, context.Senses.Reach.Complete,
            context.Npc.Center.ToTileCoordinates(), context.Senses.Player.ChoppedTree, context.Senses.Player.IsChoppingTree,
            PlayerIntegration.CompanionPreferences.Current.NewActivityRadius);
        bool changed = area != wanted || tool != signature || policy != WorkPolicies.Chopping || attempt != lastAttempt;
        // Tested against what the census holds rather than what it swept. `TreeFinder.TrunkAt` walks down
        // from the scanned cell to the trunk's bottom, so a tree found at the window's lower edge is held
        // by a tile below it, and an axe there would otherwise change the census with nothing reopening.
        // `Admit` reads that bottom tile and nothing else, so the union of the window with every held
        // bottom is exact rather than a guessed margin. The ore census carries the same rule for the same
        // reason, where a vein flood reaches outside the window that seeded it.
        if (!changed && area is Rectangle)
            changed = TerrainChanges.Edits.ChangedSince(terrainRevision, (x, y) => held.Contains(x, y)) != TerrainEditVerdict.Unchanged;
        if (changed)
        {
            area = wanted; offset = 0; reanswer = 0; reanswerKeys = null; blockPaidThrough = 0; held = wanted;
            trees.Clear();
            tool = signature; policy = WorkPolicies.Chopping; lastAttempt = attempt;
            geometry = currentGeometry;
            terrainRevision = TerrainChanges.Revision;
        }
        // A moved body or a new flood changes every admission in a finished scan and none of its
        // discoveries: where a trunk stands has nothing to do with where the companion is. This used to
        // reopen the sweep, which re-found trunks it already knew at the price of the whole window every
        // time the body moved; it re-approaches the known trunks instead. The ore census carries the
        // measurement and the reasoning. Changes while a finite scan is still running are left to the
        // binder's own validation rather than erasing the cursor every tick.
        else if (area is Rectangle priorArea && offset == (long)priorArea.Width * priorArea.Height && geometry != currentGeometry)
        {
            // The round's keys once, not a fresh sort of the whole census per site. A round may span
            // ticks, so its trunks are proved against the body as it was when each was read; the ore
            // census carries why that is inherited from the sweep rather than introduced here.
            reanswerKeys ??= trees.Keys.OrderBy(key => key.X).ThenBy(key => key.Y).ToArray();
            while (reanswer < reanswerKeys.Length && budget.TrySpend("capture-tree-reanswer"))
            {
                Point bottom = reanswerKeys[reanswer];
                if (!trees.TryGetValue(bottom, out GatheringOpportunityFact? stale)) { reanswer++; continue; }
                trees[bottom] = Admit(context, axe, bottom, stale.Material, stale.Generation);
                reanswer++;
            }
            if (reanswer >= reanswerKeys.Length) { geometry = currentGeometry; reanswer = 0; reanswerKeys = null; }
        }
        long total = (long)wanted.Width * wanted.Height;
        while (offset < total)
        {
            // A block of cells per unit, one trunk per unit — the ore census's rule and its reason, which
            // `CaptureGatheringOpportunities.ScanBlockCells` carries: a cell with no tree in it is a tile-set
            // lookup, and a cell with one pays for a trunk walk, a reach query and a native work estimate.
            // Paid once and stays paid, so a trunk reached with no allowance left costs nothing to retry;
            // the ore census carries the livelock the modulo form produced at an allowance of one.
            if (offset >= blockPaidThrough)
            {
                if (!budget.TrySpend("capture-tree-block")) break;
                blockPaidThrough = offset + CaptureGatheringOpportunities.ScanBlockCells;
            }
            Point cell = new(wanted.Left + (int)(offset % wanted.Width), wanted.Top + (int)(offset / wanted.Width));
            offset++;
            // The square's corners are outside the allowance disc and can never be admitted; skipping
            // them is free under the block already paid for, and keeps refused trunks out of the census
            // denominator that scales every site's worth.
            float allowanceRadius = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
            if (Vector2.DistanceSquared(cell.ToWorldCoordinates(), context.Senses.Intent.Region.Heading)
                > allowanceRadius * allowanceRadius) continue;
            if (TreeFinder.TrunkAt(cell) is not Point bottom || trees.ContainsKey(bottom)) continue;
            if (!budget.TrySpend("capture-tree-trunk")) { offset--; break; }
            held = Rectangle.Union(held, new Rectangle(bottom.X, bottom.Y, 1, 1));
            int material = Main.tile[bottom.X, bottom.Y].TileType;
            if (!identities.TryGetValue(bottom, out var identity) || identity.Material != material || !previous.Contains(bottom))
                identities[bottom] = identity = (material, ++generation);
            trees[bottom] = Admit(context, axe, bottom, material, identity.Generation);
        }
        bool complete = offset == total;
        var facts = new List<DecisionFact>();
        if (complete)
        {
            previous = trees.Keys.ToHashSet();
            foreach (GatheringOpportunityFact tree in trees.Values.OrderBy(tree => tree.Target, StringComparer.Ordinal))
            {
                double amount = trees.Values.Where(other => other.Material == tree.Material).Sum(other => other.RemainingAmount);
                GatheringOpportunityFact value = tree with { CensusAmount = amount };
                facts.Add(Fact(new("chop-target", value.Target, value.Generation), value,
                    value.Admission == "unknown" ? FactEvidence.Unresolved : FactEvidence.Observed));
            }
        }
        facts.Add(Fact(new("chop-coverage", "native-census"),
            new GatheringCoverageFact("chop-coverage", 0, offset, complete, $"{wanted.Left},{wanted.Top}:{wanted.Width}x{wanted.Height}"),
            complete ? FactEvidence.Observed : FactEvidence.Unresolved));
        facts.Add(new(GatheringOpportunityBinder.ReadyKey("chop-target"), 0,
            new(Amount: context.Companion.Chopper.CooldownTicks > 0 ? (double)Main.GameUpdateCount + context.Companion.Chopper.CooldownTicks : 0),
            FactEvidence.Observed));
        return facts.OrderBy(fact => fact.Key).ToArray();
    }

    /// <summary>
    /// What one known trunk is worth right now: its approach against the body and flood of this moment, its
    /// native remaining work, and the policy ladder that admits or refuses it. One function, asked by the
    /// sweep that finds a trunk and by the re-answer a moved body forces on a trunk already found — the ore
    /// census's <c>Admit</c> and its reason, which is that two copies of an admission ladder drift.
    /// </summary>
    private static GatheringOpportunityFact Admit(in ActionContext context, Item axe, Point bottom, int material, long generation)
    {
        WorkPolicy policy = WorkPolicies.Chopping;
        RemainingToolWork? remaining = context.Companion.Chopper.EstimateRemaining(bottom, axe);
        Reachability.Reach reach = FindToolAccess.Approach(bottom, context.Npc.Center, context.Senses.Reach, out Vector2 stand);
        float allowance = PlayerIntegration.CompanionPreferences.Current.NewActivityRadius;
        bool local = Vector2.DistanceSquared(bottom.ToWorldCoordinates(), context.Senses.Intent.Region.Heading) <= allowance * allowance;
        bool protectedHome = ProtectCompanionHomes.IsProtected(bottom);
        bool mimic = policy == WorkPolicy.Mimic;
        bool triggered = !mimic || context.Senses.Player.IsChoppingTree;
        bool playerTree = mimic && context.Senses.Player.ChoppedTree == bottom;
        string reason = policy == WorkPolicy.Disabled ? "chopping-disabled"
            : !triggered ? "mimic-awaiting-player-tree-contact" : playerTree ? "player-active-trunk"
            : !local ? "outside-new-work-allowance" : protectedHome ? "protected-home"
            : remaining is null ? "axe-cannot-damage-trunk"
            : reach == Reachability.Reach.Unknown ? "approach-not-yet"
            : reach == Reachability.Reach.No ? "approach-unreachable" : "observed-native-tree";
        string admission = reason == "observed-native-tree" ? "usable" : reason == "approach-not-yet" ? "unknown" : "unusable";
        return new("chop-target", $"tree:{material}:{bottom.X},{bottom.Y}", generation,
            bottom.X, bottom.Y, material, OpportunityPurposes.Chop, remaining?.DamageRemaining ?? 0, 0, admission, reason,
            $"axe={axe.axe};policy={policy};reach={reach};native-bottom-work=true", stand.X, stand.Y,
            remaining is { } work ? new(axe.type, axe.prefix, axe.axe, axe.useTime, work.DamagePerHit, work.DamageRemaining) : null);
    }

    public void Invalidate() => area = null;
    public void ResetWorld()
    {
        trees.Clear(); identities.Clear(); versions.Clear(); previous.Clear(); area = null;
        offset = generation = revision = 0; reanswer = 0; reanswerKeys = null; blockPaidThrough = 0; held = default;
    }

    private DecisionFact Fact(FactKey key, object value, FactEvidence evidence)
    {
        FactValue content = new(Text: JsonSerializer.Serialize(value));
        if (versions.TryGetValue(key, out DecisionFact? prior) && prior.Value == content && prior.Evidence == evidence) return prior;
        return versions[key] = new(key, ++revision, content, evidence);
    }
}
