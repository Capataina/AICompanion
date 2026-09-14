#nullable enable

using System;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>What a record of recent terrain edits can say about one region of the world.</summary>
public enum TerrainEditVerdict
{
    /// <summary>Nothing the record holds since that revision touched the region asked about.</summary>
    Unchanged,
    /// <summary>Something did, or something happened that invalidates everything (a new world).</summary>
    Changed,
    /// <summary>The revision asked about is older than the record reaches back, so nothing is known
    /// about the gap. A caller must treat this exactly as <see cref="Changed"/>; the two are named
    /// apart only so a diagnostic can tell a real edit from a lost window.</summary>
    TooOld,
}

/// <summary>
/// Where the world was edited, not only that it was. A monotonic revision counter with a bounded
/// ring of the tiles behind each bump, so a consumer holding an old revision can ask whether any
/// of the edits since then landed on a place it cares about.
///
/// <para>A ring of (revision, tile) pairs rather than counters over a chunk grid, and the reason is
/// which side of the question each one charges. The ring's query cost is the number of edits since
/// the asker last looked — typically none, and a handful at a mining player's rate — and the asker
/// stores one integer. Per-chunk counters instead charge the size of the region asked about: a
/// reach flood spanning a few hundred tiles overlaps dozens of chunks, and the query would have to
/// snapshot a counter per overlapped chunk when it starts and compare them all on every check, so
/// the cost grows with the very thing this exists to keep cheap. The ring is also exact where a
/// chunk grid rounds a nearby edit into a region that never contained it.</para>
///
/// <para>The window is the one real limit. An asker that goes unasked while the window's worth of
/// edits lands anywhere in the world falls off the back of the record and is told <see
/// cref="TerrainEditVerdict.TooOld"/>, which is the safe answer and must stay the answer — a silent
/// Unchanged there is a consumer served a region from before a dig it could not see. In practice an
/// asker advances its own revision on every clean answer, so the window only has to cover the gap
/// between one check and the next rather than the asker's whole life.</para>
/// </summary>
public sealed class TerrainEditLog
{
    /// <summary>How many edits the record reaches back. Sized by what a consumer can miss between
    /// two checks rather than by how long a consumer lives, because a clean check moves the
    /// consumer's own revision forward: a player breaking a tile every few ticks would have to
    /// outrun a consumer that has not asked in several seconds before the window is the binding
    /// constraint.</summary>
    public const int Window = 256;

    private readonly (int Revision, int X, int Y)[] entries = new (int, int, int)[Window];
    private int written;
    private int floor;

    /// <summary>The monotonic counter every consumer already keys on. It moves on every recorded
    /// edit and on every wholesale reset, so a consumer that only compares it behaves exactly as it
    /// did before this record existed.</summary>
    public int Revision { get; private set; }

    /// <summary>One tile is no longer what it was.</summary>
    public void Record(int x, int y)
    {
        Revision++;
        entries[written % Window] = (Revision, x, y);
        written++;
    }

    /// <summary>Everything changed and no tile can be named: a world loading or unloading, or a
    /// fixture rebuilding its scene. Every revision from before this point is unanswerable, which is
    /// what <see cref="floor"/> holds, because a ring that simply kept filling would answer
    /// Unchanged for a region nobody edited in a world that no longer exists.</summary>
    public void Reset()
    {
        Revision++;
        floor = Revision;
        written = 0;
    }

    /// <summary>
    /// Has an edit since <paramref name="since"/> landed on a tile <paramref name="sensitive"/>
    /// accepts? The predicate is the caller's own question — a region it has explored, inflated by
    /// how far that region's geometry reads — and it is asked once per edit rather than once per
    /// tile of the region.
    /// </summary>
    public TerrainEditVerdict ChangedSince(int since, Func<int, int, bool> sensitive)
    {
        if (since == Revision) return TerrainEditVerdict.Unchanged;
        if (since < floor) return TerrainEditVerdict.Changed;
        // Everything strictly after (Revision - retained) is still in the ring; anything at or
        // before it has been overwritten and cannot be ruled out.
        int retained = Math.Min(written, Window);
        if (since < Revision - retained) return TerrainEditVerdict.TooOld;
        for (int i = 0; i < retained; i++)
        {
            var entry = entries[(written - 1 - i) % Window];
            if (entry.Revision <= since) break;
            if (sensitive(entry.X, entry.Y)) return TerrainEditVerdict.Changed;
        }
        return TerrainEditVerdict.Unchanged;
    }
}
