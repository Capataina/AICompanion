#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Activities;

/// <summary>An activity that records how its last preparation narrowed its candidates down, for the recorder.</summary>
public interface ICandidateFunnelSource
{
    CandidateFunnel Funnel { get; }
}

/// <summary>
/// What one preparation did with each candidate it looked at: which stage it passed last, which stage refused it and
/// the numbers each stage read. An offer string says only why the whole search ended; a funnel says it for the
/// candidates themselves, so "why was this tile never lit" is a row in the record rather than a reconstruction.
///
/// <para>Bounded on purpose, because a search over a dark floor looks at thousands of tiles. It keeps the nearest few
/// candidates by the activity's own cost, and it always keeps the one that got furthest, because the candidate that
/// came closest to being chosen is the one a reader asks about; a nearest-only sample over a lit bubble would hold only
/// tiles refused for being lit. Every candidate is counted by its refusing stage whatever is kept, so the counts are
/// the whole search and the entries are a sample of it, never the other way round.</para>
///
/// <para>Stages are named by the activity and ranked by the order it declares them in, earliest first, with the last
/// name reserved for a candidate that was offered. Several stages may share a rank's meaning (a stand refused three
/// different ways); the rank only decides which candidate got furthest.</para>
/// </summary>
public sealed class CandidateFunnel
{
    /// <summary>One candidate. <paramref name="RefusedAt"/> is empty for the candidate that was offered.</summary>
    public readonly record struct Entry(string Identity, Point Tile, float Cost, string Passed, string RefusedAt, string Readings);

    /// <summary>The name of the stage a chosen candidate reaches; always the last and highest rank.</summary>
    public const string Offered = "offered";

    private readonly string[] stages;
    private readonly int capacity;
    private readonly List<Entry> nearest = new();
    private readonly Dictionary<string, int> counts = new();
    private Entry? best;
    private int bestRank = -1;

    public CandidateFunnel(int capacity, params string[] stages)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (stages.Length == 0 || stages[^1] != Offered)
            throw new ArgumentException($"a funnel's last stage is \"{Offered}\"", nameof(stages));
        this.capacity = capacity;
        this.stages = stages;
    }

    /// <summary>Every candidate the last preparation looked at, by the stage that refused it (<see cref="Offered"/> for
    /// the chosen one).</summary>
    public IReadOnlyDictionary<string, int> Counts => counts;

    /// <summary>The kept sample, nearest first.</summary>
    public IReadOnlyList<Entry> Entries => nearest;

    /// <summary>The candidate that got furthest, nearest first among equals; null when nothing was looked at.</summary>
    public Entry? Best => best;

    /// <summary>How many candidates the last preparation looked at.</summary>
    public int Total { get; private set; }

    /// <summary>Starts a new preparation's record; the previous one is gone.</summary>
    public void Begin()
    {
        nearest.Clear();
        counts.Clear();
        best = null;
        bestRank = -1;
        Total = 0;
    }

    /// <summary>Records one candidate. A stage name the activity did not declare is refused loudly, because a funnel
    /// whose stage ranks silently default is a funnel whose "furthest" means nothing. A null <paramref name="passed"/> is
    /// the stage declared just before the refusing one, which is right for stages a candidate meets in a line; a caller
    /// whose refusing stage is one of several alternatives at the same point names the stage it passed itself.</summary>
    public void Add(string identity, Point tile, float cost, string? passed, string refusedAt, string readings)
    {
        string stage = refusedAt.Length == 0 ? Offered : refusedAt;
        int rank = Array.IndexOf(stages, stage);
        if (rank < 0) throw new ArgumentException($"stage \"{stage}\" is not one this funnel declared", nameof(refusedAt));
        passed ??= rank > 0 ? stages[rank - 1] : "";
        Total++;
        counts[stage] = counts.TryGetValue(stage, out int had) ? had + 1 : 1;
        var entry = new Entry(identity, tile, cost, passed, refusedAt, readings);
        if (rank > bestRank || rank == bestRank && best is { } held && cost < held.Cost)
        {
            best = entry;
            bestRank = rank;
        }
        int at = nearest.Count;
        while (at > 0 && nearest[at - 1].Cost > cost) at--;
        if (at >= capacity) return;
        nearest.Insert(at, entry);
        if (nearest.Count > capacity) nearest.RemoveAt(nearest.Count - 1);
    }

    /// <summary>The stage that refused the candidate that got furthest, or <see cref="Offered"/>, or "-" when the last
    /// preparation looked at nothing: the compact per-tick column.</summary>
    public string BestStage => best is { } b ? (b.RefusedAt.Length == 0 ? Offered : b.RefusedAt) : "-";

    /// <summary>The counts in declared stage order, as <c>stage=count</c> joined by commas; the part of the record that
    /// changes only when the search's outcome does, which is what the event coalesces on.</summary>
    public string Summary()
    {
        var text = new StringBuilder();
        foreach (string stage in stages)
        {
            if (!counts.TryGetValue(stage, out int count)) continue;
            if (text.Length > 0) text.Append(',');
            text.Append(stage).Append('=').Append(count.ToString(CultureInfo.InvariantCulture));
        }
        return text.Length == 0 ? "-" : text.ToString();
    }

    /// <summary>The kept entries and the furthest one, as <c>identity@x,y:passed>refused[readings]</c> joined by '|'.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        void Append(in Entry e)
        {
            if (text.Length > 0) text.Append('|');
            text.Append(e.Identity).Append('@').Append(e.Tile.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(e.Tile.Y.ToString(CultureInfo.InvariantCulture)).Append(':').Append(e.Passed.Length == 0 ? "-" : e.Passed)
                .Append('>').Append(e.RefusedAt.Length == 0 ? Offered : e.RefusedAt).Append('[').Append(e.Readings).Append(']');
        }
        if (best is { } furthest && !nearest.Contains(furthest))
        {
            text.Append("best:");
            Append(furthest);
        }
        foreach (Entry e in nearest) Append(e);
        return text.ToString();
    }
}
