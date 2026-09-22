#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AICompanion.Companion.Brain.Infrastructure.Observation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Computation;
using AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

namespace AICompanion.Companion.Brain.Infrastructure.Selection.Opportunities;

/// <summary>Pure census readers for native assistance capture. They enumerate every captured
/// site in deterministic key order and retain their own cursor across a cut; no source may read
/// Terraria or Senses after capture.</summary>
public sealed class DiscoverAssistanceOpportunities : IOpportunitySource
{
    private readonly string domain;
    private sealed class SnapshotCursor { public long Epoch = long.MinValue; public DecisionFact[] Sites = Array.Empty<DecisionFact>(); }
    private readonly ConditionalWeakTable<DecisionWorkCursor, SnapshotCursor> snapshots = new();
    public DiscoverAssistanceOpportunities(string domain) => this.domain = domain;
    public string Name => domain;

    public OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        SnapshotCursor snapshot = snapshots.GetValue(cursor, static _ => new SnapshotCursor());
        DecisionFact[] sites = facts.Facts.Where(f => f.Key.Kind == domain).OrderBy(f => f.Key).ToArray();
        bool prefixUnchanged = cursor.Offset <= sites.Length && cursor.Offset <= snapshot.Sites.Length;
        for (int i = 0; prefixUnchanged && i < cursor.Offset; i++)
            // Field-for-field rather than by digest: this runs over the already-examined prefix on every
            // slice, so asking each site for its hash would compute one per site per tick and defeat the
            // point of a fact not computing its digest until something needs it.
            prefixUnchanged = sites[i].Key == snapshot.Sites[i].Key && sites[i].SameObservationAs(snapshot.Sites[i]);
        if (snapshot.Epoch != facts.WorldEpoch || !prefixUnchanged || cursor.Exhausted && sites.Length > cursor.Offset) cursor.Rescan();
        snapshot.Epoch = facts.WorldEpoch;
        snapshot.Sites = sites;
        var coverageKey = new FactKey(domain.Replace("-target", "-coverage", StringComparison.Ordinal), "native-census");
        bool complete = facts.TryRead(coverageKey, out var capture) && capture.Evidence == FactEvidence.Observed;
        var examined = new List<Opportunity>();
        while (cursor.Offset < sites.Length && budget.TrySpend(Name))
        {
            DecisionFact raw = sites[(int)cursor.Offset];
            cursor.Advance();
            var reader = facts.Track();
            reader.Read(coverageKey);
            DecisionFact observed = reader.Read(raw.Key);
            AssistanceOpportunityFact? site = JsonSerializer.Deserialize<AssistanceOpportunityFact>(observed.Value.Text);
            if (site == null || site.Domain != domain || site.Target != raw.Key.Identity || site.Generation != raw.Key.Generation)
                throw new InvalidOperationException("Captured assistance fact did not retain its identity.");
            OpportunityAdmission admission = site.Admission switch
            {
                "usable" => OpportunityAdmission.KnownUsable,
                "unusable" => OpportunityAdmission.KnownUnusable,
                "unknown" => OpportunityAdmission.Unresolved,
                _ => throw new InvalidOperationException("Unknown captured assistance admission: " + site.Admission),
            };
            NeedKind need = domain switch
            {
                "collect-target" => NeedKind.Loot,
                "light-target" => NeedKind.Illumination,
                "pot-target" => NeedKind.Container,
                _ => throw new InvalidOperationException("Unsupported assistance domain: " + domain),
            };
            var key = new OpportunityKey(domain, domain == "collect-target" ? "collect" : domain == "light-target" ? "light" : "break-pot",
                site.Target, site.Generation);
            examined.Add(new Opportunity(key, observed.Version, new(site.ContactX ?? site.X, site.ContactY ?? site.Y), admission, site.Reason,
                new[] { new UsefulNeed(new(need, site.Target, site.Generation), Math.Max(0, site.Amount), Math.Max(1, site.CensusAmount), admission == OpportunityAdmission.KnownUsable ? 1 : 0) },
                new[] { key.Purpose }, reader.Manifest(), raw.Key));
        }
        if (cursor.Offset == sites.Length) cursor.Complete();
        return new(examined, new(Name, facts.WorldEpoch, cursor.Offset, sites.Length, cursor.Exhausted && complete,
            budget.Cut && cursor.Offset < sites.Length,
            "snapshot-facts;cursor=" + cursor.Offset));
    }
}
