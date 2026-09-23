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
    public DiscoverAssistanceOpportunities(string domain)
    {
        if (domain is not ("collect-target" or "light-target"))
            throw new ArgumentOutOfRangeException(nameof(domain), domain,
                "The assistance domains are collect-target (drops and pots) and light-target.");
        this.domain = domain;
    }
    public string Name => domain;

    /// <summary>The census-completeness fact a domain's discovery and binding both read.</summary>
    internal static FactKey CoverageKeyOf(string domain)
        => new(domain.Replace("-target", "-coverage", StringComparison.Ordinal), "native-census");

    /// <summary>
    /// The need an assistance act satisfies, from the site's own purpose, refusing a purpose its domain does not
    /// hold. Collection holds two acts since pots were folded into it on 23 September 2026 — a drop is loot, a pot
    /// is a container whose contents are unknown — and the binder reads the same answer through this one function,
    /// because a need key the source and the binder spell differently is a binding refused as
    /// `assistance-need-missing` for no reason anybody could see.
    /// </summary>
    internal static NeedKind NeedOf(string domain, string purpose) => (domain, purpose) switch
    {
        ("collect-target", OpportunityPurposes.Collect) => NeedKind.Loot,
        ("collect-target", OpportunityPurposes.BreakPot) => NeedKind.Container,
        ("light-target", OpportunityPurposes.Light) => NeedKind.Illumination,
        _ => throw new InvalidOperationException($"The assistance domain '{domain}' does not hold the purpose '{purpose}'."),
    };

    /// <summary>
    /// One captured site as an opportunity, with the manifest of the two facts it rests on: the domain's coverage
    /// and the site itself. Discovery builds every candidate through this, and the course's acceptance of in-passing
    /// work builds its one candidate through it too, so the two cannot disagree about what a site offers.
    /// </summary>
    internal static Opportunity Read(DecisionFactSnapshot facts, string domain, FactKey site)
    {
        FactKey coverageKey = CoverageKeyOf(domain);
        var reader = facts.Track();
        reader.Read(coverageKey);
        DecisionFact observed = reader.Read(site);
        AssistanceOpportunityFact? value = JsonSerializer.Deserialize<AssistanceOpportunityFact>(observed.Value.Text!);
        if (value == null || value.Domain != domain || value.Target != site.Identity || value.Generation != site.Generation)
            throw new InvalidOperationException("Captured assistance fact did not retain its identity.");
        OpportunityAdmission admission = value.Admission switch
        {
            "usable" => OpportunityAdmission.KnownUsable,
            "unusable" => OpportunityAdmission.KnownUnusable,
            "unknown" => OpportunityAdmission.Unresolved,
            _ => throw new InvalidOperationException("Unknown captured assistance admission: " + value.Admission),
        };
        NeedKind need = NeedOf(domain, value.Purpose);
        var key = new OpportunityKey(domain, value.Purpose, value.Target, value.Generation);
        return new Opportunity(key, observed.Version, new(value.ContactX ?? value.X, value.ContactY ?? value.Y), admission, value.Reason,
            new[] { new UsefulNeed(new(need, value.Target, value.Generation), Math.Max(0, value.Amount), Math.Max(1, value.CensusAmount), admission == OpportunityAdmission.KnownUsable ? 1 : 0) },
            new[] { key.Purpose }, reader.Manifest(), site);
    }

    public OpportunitySlice Continue(DecisionFactSnapshot facts, DecisionWorkCursor cursor, DecisionWorkBudget budget)
    {
        SnapshotCursor snapshot = snapshots.GetValue(cursor, static _ => new SnapshotCursor());
        // **In the census's own order, not by key, and that is the whole of what makes the rank reach a
        // decision.** This read `OrderBy(f => f.Key)` until 22 September 2026, and removing it changed
        // nothing on its own: `DecisionFactSnapshot` sorted the entire catalogue by key in its own
        // constructor, so the source was re-imposing an order the snapshot had already imposed and the
        // fix had to land there. The bound fixed membership of the snapshot and nothing else — the store
        // below holds 64 candidates across six domains with a per-domain floor of ten, and above that
        // floor a group competes on recency, so the light sites that survived to pricing were the head of
        // a *tile-key* walk while three documents said they were the nearest to the heading.
        //
        // The consequence to know before changing it back: the rank moves as the heading moves, so the
        // prefix check below now detects a reorder and rescans where a key order was stable. That is the
        // correct answer rather than a cost — a rescan restarts at the *nearest* sites, which is what any
        // decision here can use, and the tail it stops reaching is two orders of magnitude beyond the ten
        // candidates the store keeps for this domain. Completeness is unaffected: `complete` is read from
        // the coverage fact rather than from this cursor.
        DecisionFact[] sites = facts.Facts.Where(f => f.Key.Kind == domain).ToArray();
        bool prefixUnchanged = cursor.Offset <= sites.Length && cursor.Offset <= snapshot.Sites.Length;
        for (int i = 0; prefixUnchanged && i < cursor.Offset; i++)
            // Field-for-field rather than by digest: this runs over the already-examined prefix on every
            // slice, so asking each site for its hash would compute one per site per tick and defeat the
            // point of a fact not computing its digest until something needs it.
            prefixUnchanged = sites[i].Key == snapshot.Sites[i].Key && sites[i].SameObservationAs(snapshot.Sites[i]);
        if (snapshot.Epoch != facts.WorldEpoch || !prefixUnchanged || cursor.Exhausted && sites.Length > cursor.Offset) cursor.Rescan();
        snapshot.Epoch = facts.WorldEpoch;
        snapshot.Sites = sites;
        FactKey coverageKey = CoverageKeyOf(domain);
        bool complete = facts.TryRead(coverageKey, out var capture) && capture.Evidence == FactEvidence.Observed;
        var examined = new List<Opportunity>();
        while (cursor.Offset < sites.Length && budget.TrySpend(Name))
        {
            DecisionFact raw = sites[(int)cursor.Offset];
            cursor.Advance();
            examined.Add(Read(facts, domain, raw.Key));
        }
        if (cursor.Offset == sites.Length) cursor.Complete();
        return new(examined, new(Name, facts.WorldEpoch, cursor.Offset, sites.Length, cursor.Exhausted && complete,
            budget.Cut && cursor.Offset < sites.Length,
            "snapshot-facts;cursor=" + cursor.Offset));
    }
}
