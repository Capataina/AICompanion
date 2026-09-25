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
    private readonly int[] sections;
    // Which sources have examined anything since the observation was last marked. A decision may not order
    // a catalogue a source was never asked about, and this is how it can tell.
    private readonly bool[] slicedSinceMark;
    private static readonly int RetireSection = Diagnostics.BrainSections.Register("retire");
    private static readonly int PinSection = Diagnostics.BrainSections.Register("pin");
    private static readonly int StoreSection = Diagnostics.BrainSections.Register("store");
    public DiscoverOpportunities(IEnumerable<IOpportunitySource> sources, int capacity)
    {
        this.sources = sources.ToArray();
        if (this.sources.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != this.sources.Length)
            throw new ArgumentException("Opportunity sources need unique stable names.", nameof(sources));
        cursors = this.sources.Select(_ => new DecisionWorkCursor()).ToArray();
        slicedSinceMark = new bool[this.sources.Length];
        // One profiler section per census, named for its domain, so `decide.course.discovery.light-target` is where
        // a dark cave's cost shows rather than inside one number for every census.
        sections = this.sources.Select(source => Diagnostics.BrainSections.Register(source.Name)).ToArray();
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

    /// <summary>
    /// Starts counting which sources have examined anything against a new observation.
    ///
    /// A source's coverage and its stored candidates outlive the observation they were read from, so a
    /// source this call never reaches still reports the last pass it finished — "complete, nothing here" —
    /// about a world that has since changed. Measured on the replay of the 25 September 2026 capture: a
    /// zombie died at tick 2463, combat committed a plan on the next one in the same tick, and the decision
    /// that started beside it began after the tick's clock had run out, so discovery visited no source at
    /// all. Combat's candidates had just been retired with the dead target, its coverage still read complete,
    /// and the search settled on the empty order with a fight standing ready — three decisions running.
    /// </summary>
    public void MarkObservation() => Array.Clear(slicedSinceMark);

    /// <summary>Whether every source has examined something since <see cref="MarkObservation"/>, so its
    /// candidates and coverage describe the marked observation rather than an earlier one.</summary>
    public bool EverySourceSlicedSinceMark => Array.TrueForAll(slicedSinceMark, sliced => sliced);

    public void Continue(DecisionFactSnapshot facts, DecisionWorkBudget budget, IEnumerable<OpportunityKey> pinned)
    {
        HashSet<OpportunityKey> pins = Retire(facts, pinned);
        if (sources.Length == 0 || budget.Exhausted) return;
        for (int visited = 0; visited < sources.Length; visited++)
        {
            // Dispatch does no source work. Spending here would make a one-operation
            // slice rotate forever without permitting any source to examine a site.
            if (budget.Exhausted) return;
            int index = next;
            next = (next + 1) % sources.Length;
            OpportunitySlice result;
            long offsetBefore = cursors[index].Offset;
            using (Diagnostics.BrainSections.Enter(sections[index])) result = sources[index].Continue(facts, cursors[index], budget);
            // A slice counts unless the allowance cut it before it examined anything: that source has been asked
            // nothing about this observation. One that returned uncut has answered for it even with its cursor
            // standing still — the gathering sources read a census the observation captured, and with none
            // ready they return without advancing, which is their answer rather than a refusal to give one.
            if (!result.Coverage.BudgetCut || cursors[index].Exhausted || cursors[index].Offset != offsetBefore)
                slicedSinceMark[index] = true;
            // The slice's own coverage, carrying forward the evictions this domain has suffered. A source
            // reports what it examined and cannot know what the store then threw away, so overwriting the
            // row wholesale reset the eviction count to zero on every slice — which made the one number
            // that says "this domain's candidates are being discarded" unreadable by construction, and it
            // is the number AIC-448 was diagnosed by. The count is per world epoch, like the store.
            long evicted = coverage.TryGetValue(sources[index].Name, out var previous) ? previous.Evicted : 0;
            coverage[sources[index].Name] = result.Coverage with { Evicted = result.Coverage.Evicted + evicted };
            // Storing what the slice examined is a section of its own, beside the pinning pass above, because on the
            // replayed 22 September capture 12.5% of the whole brain was discovery's own time outside every census
            // and these two are the work that runs there.
            using var storing = Diagnostics.BrainSections.Enter(StoreSection);
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

    /// <summary>
    /// A stored admission is only as good as the observation it was decided against, and this store
    /// outlives observations by design.
    ///
    /// Every candidate names the one fact its own domain's binder reads first
    /// (<see cref="Opportunity.AdmissionEvidence"/>), so the census and the binder can be made to agree
    /// by construction rather than by both being careful. A candidate whose evidence has left the world
    /// leaves the store — the drop was taken, the hostile died, the tile was mined — because a rescan
    /// re-finds it the moment it comes back and an entry nobody can bind is a seat taken from a domain
    /// that could. A candidate whose evidence is present but not observed is served
    /// <see cref="OpportunityAdmission.Unresolved"/> instead: that is the three-valued answer this tree
    /// keeps everywhere, and the difference between "gone" and "not answered yet" is the whole of it.
    ///
    /// Measured on the tail scene of 22 September 2026: without this, a drop removed at tick 150 was
    /// still served usable at tick 499, and every decision in between refused nine orders
    /// <c>assistance-target-unresolved</c> while its own funnel reported three usable drops. The
    /// companion's play of 0.38.13 ended in that state on combat and collection at once, and an empty
    /// course is companionship, so it read as a companion that had stopped doing anything.
    ///
    /// A pinned candidate is downgraded rather than removed, whatever its evidence says: it is a step
    /// the published course still holds, and whether that course survives is
    /// <c>BindOpportunity.ValidateNextUse</c>'s answer rather than discovery's.
    ///
    /// The sweep is not charged to the allowance, deliberately. It is bounded by the store's own
    /// capacity — sixty-four dictionary reads at the very worst — and cutting it half way is the one
    /// outcome that would put the defect back, because the candidates it had not reached yet would go
    /// on claiming an evidence the decision does not hold.
    /// </summary>
    /// <summary>
    /// Pins what the course still holds and retires every admission <paramref name="facts"/> cannot support, without
    /// examining any source. The owner calls it on every freshly captured observation — a tick that carries a retained
    /// course included — and <see cref="Continue"/> calls it before discovery, so the store is always an account of the
    /// observation in hand. Before 25 September 2026 it ran only when a decision began, so on every tick a course was
    /// carried the store still held admissions the world had withdrawn, and the census counts the recorder writes from
    /// it (`course-admitted:combat`) described an older observation than the row they sat on.
    /// </summary>
    public HashSet<OpportunityKey> Retire(DecisionFactSnapshot facts, IEnumerable<OpportunityKey> pinned)
    {
        if (epoch != facts.WorldEpoch)
        {
            epoch = facts.WorldEpoch; candidates.Clear(); storage.Clear(); coverage.Clear(); next = 0;
            foreach (var cursor in cursors) cursor.Bind(epoch, "world-epoch");
        }
        HashSet<OpportunityKey> pins;
        using (Diagnostics.BrainSections.Enter(PinSection))
        {
            pins = pinned.ToHashSet();
            foreach (var key in candidates.Keys) storage.Pin(key, pins.Contains(key));
        }
        using (Diagnostics.BrainSections.Enter(RetireSection)) RetireAdmissionsThisObservationCannotSupport(facts, pins);
        return pins;
    }

    private void RetireAdmissionsThisObservationCannotSupport(DecisionFactSnapshot facts, HashSet<OpportunityKey> pins)
    {
        List<OpportunityKey>? retired = null;
        foreach (var pair in candidates.ToArray())
        {
            Opportunity candidate = pair.Value;
            // A candidate that names no evidence is a harness stand-in rather than a domain's site, and
            // there is nothing to re-read for it. Production's three sources all name one.
            if (string.IsNullOrEmpty(candidate.AdmissionEvidence.Kind)) continue;
            bool present = facts.TryRead(candidate.AdmissionEvidence, out DecisionFact evidence);
            if (present && evidence.Evidence == FactEvidence.Observed) continue;
            if (!present && !pins.Contains(pair.Key))
            {
                (retired ??= new()).Add(pair.Key);
                continue;
            }
            if (candidate.Admission == OpportunityAdmission.Unresolved) continue;
            var downgraded = new Opportunity(candidate.Key, candidate.Revision, candidate.Target,
                OpportunityAdmission.Unresolved, "admission-evidence-" + (present ? evidence.Evidence.ToString().ToLowerInvariant() : "absent"),
                candidate.Needs, candidate.Methods, candidate.Dependencies, candidate.AdmissionEvidence);
            candidates[pair.Key] = downgraded;
            storage.Put(pair.Key, downgraded, pins.Contains(pair.Key));
        }
        if (retired == null) return;
        foreach (OpportunityKey key in retired)
        {
            candidates.Remove(key);
            storage.Remove(key);
            if (coverage.TryGetValue(key.Domain, out var row)) coverage[key.Domain] = row with { Evicted = row.Evicted + 1 };
        }
    }
}
