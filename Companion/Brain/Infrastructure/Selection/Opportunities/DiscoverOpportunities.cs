#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>Each slice resumes the next source. A cut does not reset that source or its
/// siblings; sources, in turn, retain a cursor through their concrete sites and regions.</summary>
public sealed class DiscoverOpportunities
{
    private readonly IOpportunitySource[] sources;
    private readonly DecisionWorkCursor[] cursors;
    private readonly Dictionary<OpportunityKey, Opportunity> candidates = new();
    private readonly Dictionary<string, OpportunityCoverage> coverage = new(StringComparer.Ordinal);
    private readonly DecisionStorage<OpportunityKey, Opportunity> storage;
    private int next;
    private long epoch = -1;
    public DiscoverOpportunities(IEnumerable<IOpportunitySource> sources, int capacity)
    {
        this.sources = sources.ToArray();
        if (this.sources.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != this.sources.Length)
            throw new ArgumentException("Opportunity sources need unique stable names.", nameof(sources));
        cursors = this.sources.Select(_ => new DecisionWorkCursor()).ToArray();
        // Grouped by domain, with every source guaranteed its share of the capacity. Ungrouped, this store
        // was a preference for whichever domain mints the most candidates: measured on 21 September 2026,
        // lighting minted 138 sites in a dark area against one each from mining, chopping and collection,
        // and all three of those were evicted while lighting kept all sixty-four slots — a companion that
        // lights and cannot discover a vein, a trunk or a drop at all. The chooser this replaced had the
        // same property under a different name, as `ScheduleOpportunityQueries`' per-family preparation
        // share, and the course lost it in the migration rather than deciding against it.
        storage = new(capacity, key => key.Domain, this.sources.Length);
        storage.Evicted += key =>
        {
            candidates.Remove(key);
            if (coverage.TryGetValue(key.Domain, out var old)) coverage[key.Domain] = old with { Evicted = old.Evicted + 1 };
        };
    }
    public IReadOnlyList<Opportunity> Candidates => Array.AsReadOnly(candidates.Values.OrderBy(c => c.Key).ToArray());
    public IReadOnlyList<OpportunityCoverage> Coverage => Array.AsReadOnly(coverage.Values.OrderBy(c => c.Source).ToArray());
    public int NextSource => next;

    public void Continue(DecisionFactSnapshot facts, DecisionWorkBudget budget, IEnumerable<OpportunityKey> pinned)
    {
        if (epoch != facts.WorldEpoch)
        {
            epoch = facts.WorldEpoch; candidates.Clear(); storage.Clear(); coverage.Clear(); next = 0;
            foreach (var cursor in cursors) cursor.Bind(epoch, "world-epoch");
        }
        var pins = pinned.ToHashSet();
        foreach (var key in candidates.Keys) storage.Pin(key, pins.Contains(key));
        if (sources.Length == 0 || budget.Exhausted) return;
        for (int visited = 0; visited < sources.Length; visited++)
        {
            // Dispatch does no source work. Spending here would make a one-operation
            // slice rotate forever without permitting any source to examine a site.
            if (budget.Exhausted) return;
            int index = next;
            next = (next + 1) % sources.Length;
            var result = sources[index].Continue(facts, cursors[index], budget);
            // The slice's own coverage, carrying forward the evictions this domain has suffered. A source
            // reports what it examined and cannot know what the store then threw away, so overwriting the
            // row wholesale reset the eviction count to zero on every slice — which made the one number
            // that says "this domain's candidates are being discarded" unreadable by construction, and it
            // is the number AIC-448 was diagnosed by. The count is per world epoch, like the store.
            long evicted = coverage.TryGetValue(sources[index].Name, out var previous) ? previous.Evicted : 0;
            coverage[sources[index].Name] = result.Coverage with { Evicted = result.Coverage.Evicted + evicted };
            foreach (var candidate in result.Examined)
            {
                if (candidate.Key.Domain != sources[index].Name)
                    throw new InvalidOperationException("A source published another domain's opportunity.");
                if (storage.Put(candidate.Key, candidate, pins.Contains(candidate.Key))) candidates[candidate.Key] = candidate;
                else coverage[sources[index].Name] = coverage[sources[index].Name] with
                { Evicted = coverage[sources[index].Name].Evicted + 1 };
            }
            // Finished sources rotate back through their real finite set on a later call.
            // Eviction therefore loses cache coverage, not the ability ever to see that site again.
            if (cursors[index].Exhausted) cursors[index].Rescan();
        }
    }
}
