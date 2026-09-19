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
    private int terrainRevision;
    private long lastAttempt;
    private (int Type, int Prefix, int Power, int UseTime) tool;
    private WorkPolicy policy;
    private (int Generation, bool Complete, Point Body, Point? PlayerTree, bool Chopping, float Allowance) geometry;

    public IReadOnlyList<DecisionFact> Capture(in ActionContext context, DecisionWorkBudget budget)
    {
        int radius = ChopTree.SearchRadiusTiles;
        Point centre = context.Senses.Intent.Region.Heading.ToTileCoordinates();
        Rectangle wanted = new(centre.X - radius, centre.Y - radius, radius * 2 + 1, radius * 2 + 1);
        Item axe = TileChopper.AxeFor(context.Player);
        var signature = (axe.type, (int)axe.prefix, axe.axe, axe.useTime);
        long attempt = context.Companion.Chopper.LastOutcome?.Attempt ?? -1;
        var currentGeometry = (context.Senses.Reach.FloodGeneration, context.Senses.Reach.Complete,
            context.Npc.Center.ToTileCoordinates(), context.Senses.Player.ChoppedTree, context.Senses.Player.IsChoppingTree,
            PlayerIntegration.CompanionPreferences.Current.NewActivityRadius);
        bool changed = area != wanted || tool != signature || policy != WorkPolicies.Chopping || attempt != lastAttempt;
        // Reopen completed geometry after movement or a finished flood. Changes while a finite
        // scan is in progress are validated at binding; they cannot repeatedly erase its cursor.
        changed |= area is Rectangle priorArea && offset == (long)priorArea.Width * priorArea.Height && geometry != currentGeometry;
        if (!changed && area is Rectangle existing)
            changed = TerrainChanges.Edits.ChangedSince(terrainRevision, (x, y) => existing.Contains(x, y)) != TerrainEditVerdict.Unchanged;
        if (changed)
        {
            area = wanted; offset = 0; trees.Clear();
            tool = signature; policy = WorkPolicies.Chopping; lastAttempt = attempt;
            geometry = currentGeometry;
            terrainRevision = TerrainChanges.Revision;
        }
        long total = (long)wanted.Width * wanted.Height;
        while (offset < total && budget.TrySpend("capture-tree-cell"))
        {
            Point cell = new(wanted.Left + (int)(offset % wanted.Width), wanted.Top + (int)(offset / wanted.Width));
            offset++;
            if (TreeFinder.TrunkAt(cell) is not Point bottom || trees.ContainsKey(bottom)) continue;
            int material = Main.tile[bottom.X, bottom.Y].TileType;
            if (!identities.TryGetValue(bottom, out var identity) || identity.Material != material || !previous.Contains(bottom))
                identities[bottom] = identity = (material, ++generation);
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
            trees[bottom] = new("chop-target", $"tree:{material}:{bottom.X},{bottom.Y}", identity.Generation,
                bottom.X, bottom.Y, material, "chop", remaining?.DamageRemaining ?? 0, 0, admission, reason,
                $"axe={axe.axe};policy={policy};reach={reach};native-bottom-work=true", stand.X, stand.Y);
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
        return facts.OrderBy(fact => fact.Key).ToArray();
    }

    public void Invalidate() => area = null;
    public void ResetWorld()
    {
        trees.Clear(); identities.Clear(); versions.Clear(); previous.Clear(); area = null;
        offset = generation = revision = 0;
    }

    private DecisionFact Fact(FactKey key, object value, FactEvidence evidence)
    {
        FactValue content = new(Text: JsonSerializer.Serialize(value));
        if (versions.TryGetValue(key, out DecisionFact? prior) && prior.Value == content && prior.Evidence == evidence) return prior;
        return versions[key] = new(key, ++revision, content, evidence);
    }
}
