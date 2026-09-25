#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// For every free tile, how far the nearest wall is, in tiles: a distance transform the route
/// search prices its edges against so the cheapest route runs down the middle of a passage and
/// hugs a wall only where the passage is the wall. It is computed per chunk on demand and kept
/// while the world it describes stands: a chunk is rebuilt when the terrain revision has an edit
/// inside the chunk's own reach, or when the world object itself is replaced. Asking about a tile far from every recent edit costs a
/// dictionary lookup.
///
/// <para>Distances are capped at <see cref="MaxTiles"/>, which is a structural cap and not a
/// tunable: beyond it the corridor-middle preference has nothing left to prefer, and a smaller
/// cap makes every chunk cheaper to build by the square of the difference.</para>
/// </summary>
public sealed class ClearanceField
{
    public const int MaxTiles = 8;
    private const int ChunkSize = 16;

    private sealed class Chunk
    {
        public readonly float[] Values = new float[ChunkSize * ChunkSize];
        public int Revision;
        public ITileWorld World = null!;
        // The bounding box of the tiles Build actually read, which is narrower than the chunk plus its full
        // margin because a free tile's scan stops at its nearest wall. A served chunk reports this box, not
        // the margin box, so a recording wrapper's footprint is exactly what building the chunk through it
        // would have recorded; the margin box would let an edit the query never read invalidate it.
        public int ReadLeft, ReadTop, ReadRight, ReadBottom;
    }

    /// <summary>The field every consumer in the process reads, keyed by the world it was built over.</summary>
    public static readonly ClearanceField Shared = new();

    private readonly Dictionary<(int, int), Chunk> chunks = new();
    private readonly Dictionary<(int, int), int> checkedAt = new();
    public int Builds { get; private set; }

    public void Invalidate()
    {
        chunks.Clear();
        checkedAt.Clear();
        last = null;
    }

    // The chunk served last, and the key, identity and revision it was served under. Consecutive reads land
    // in one chunk far more often than not — the four tiles around a corner, and the eight neighbours a search
    // prices from one node — and each read otherwise costs two dictionary lookups before the revision compare
    // that decides it. Only `Fetch` and `Invalidate` change a chunk or its checked revision, `Fetch` records
    // this after every serve, and `Invalidate` clears it, so a hit returns exactly what the full path would.
    private (int X, int Y, ITileWorld Identity, int Revision, Chunk Chunk)? last;

    /// <summary>Clearance of one tile: zero for a wall, otherwise the distance to the nearest wall tile's centre, capped.</summary>
    public float At(ITileWorld world, int x, int y)
    {
        int cx = FloorDiv(x, ChunkSize), cy = FloorDiv(y, ChunkSize);
        Chunk chunk = Fetch(world, cx, cy);
        return chunk.Values[(y - cy * ChunkSize) * ChunkSize + (x - cx * ChunkSize)];
    }

    /// <summary>The clearance of a corner node: the least of the four tiles around it.</summary>
    public float AtCorner(ITileWorld world, Point corner)
        => MathF.Min(MathF.Min(At(world, corner.X - 1, corner.Y - 1), At(world, corner.X, corner.Y - 1)),
            MathF.Min(At(world, corner.X - 1, corner.Y), At(world, corner.X, corner.Y)));

    private Chunk Fetch(ITileWorld world, int cx, int cy)
    {
        if (last is { } hit && hit.X == cx && hit.Y == cy && ReferenceEquals(hit.Identity, world.CacheIdentity) && hit.Revision == world.Revision)
        {
            NoteServed(world, hit.Chunk);
            return hit.Chunk;
        }
        Chunk served = FetchThroughTheStore(world, cx, cy);
        last = checkedAt.TryGetValue((cx, cy), out int checkedRevision) && checkedRevision == world.Revision
            ? (cx, cy, world.CacheIdentity, checkedRevision, served) : null;
        return served;
    }

    private Chunk FetchThroughTheStore(ITileWorld world, int cx, int cy)
    {
        var key = (cx, cy);
        // A chunk's distances can read the chunk and a margin of MaxTiles round it: a wall that far outside
        // moves a value inside. That box is what the edit record is asked about; a recording world is told
        // the narrower box the build really read.
        int x0 = cx * ChunkSize - MaxTiles, y0 = cy * ChunkSize - MaxTiles;
        int x1 = x0 + ChunkSize + 2 * MaxTiles, y1 = y0 + ChunkSize + 2 * MaxTiles;
        // Keyed on the world's cache identity rather than the object, so a wrapper that only records its
        // reads shares its source's chunks instead of rebuilding them — see ITileWorld.CacheIdentity for
        // the cost that measured.
        ITileWorld identity = world.CacheIdentity;
        if (chunks.TryGetValue(key, out Chunk? chunk))
        {
            if (ReferenceEquals(chunk.World, identity))
            {
                int revision = world.Revision;
                if (checkedAt.TryGetValue(key, out int at) && at == revision) { NoteServed(world, chunk); return chunk; }
                // The edit record is asked once per revision per chunk, and only about the box above.
                if (world.ChangedSince(chunk.Revision, (tx, ty) => tx >= x0 && tx < x1 && ty >= y0 && ty < y1) == TerrainEditVerdict.Unchanged)
                {
                    checkedAt[key] = revision;
                    chunk.Revision = revision;
                    NoteServed(world, chunk);
                    return chunk;
                }
            }
        }
        chunk ??= new Chunk();
        Build(world, cx, cy, chunk);
        chunk.World = identity;
        chunks[key] = chunk;
        checkedAt[key] = chunk.Revision;
        return chunk;
    }

    private void Build(ITileWorld world, int cx, int cy, Chunk chunk)
    {
        Builds++;
        chunk.World = world;
        chunk.Revision = world.Revision;
        int baseX = cx * ChunkSize, baseY = cy * ChunkSize;
        // Every tile of the chunk is read; the neighbour scan below widens the box only as far as it went.
        int readLeft = baseX, readTop = baseY, readRight = baseX + ChunkSize - 1, readBottom = baseY + ChunkSize - 1;
        for (int ly = 0; ly < ChunkSize; ly++)
        {
            for (int lx = 0; lx < ChunkSize; lx++)
            {
                int x = baseX + lx, y = baseY + ly;
                float value = 0f;
                if (OrbTerrain.Free(world, x, y))
                {
                    float best = MaxTiles;
                    for (int dy = -MaxTiles; dy <= MaxTiles; dy++)
                        for (int dx = -MaxTiles; dx <= MaxTiles; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            float distance = MathF.Sqrt(dx * dx + dy * dy);
                            if (distance >= best) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < readLeft) readLeft = nx; else if (nx > readRight) readRight = nx;
                            if (ny < readTop) readTop = ny; else if (ny > readBottom) readBottom = ny;
                            if (!OrbTerrain.Free(world, nx, ny)) best = distance;
                        }
                    value = best;
                }
                chunk.Values[ly * ChunkSize + lx] = value;
            }
        }
        chunk.ReadLeft = readLeft; chunk.ReadTop = readTop; chunk.ReadRight = readRight; chunk.ReadBottom = readBottom;
    }

    private static void NoteServed(ITileWorld world, Chunk chunk)
        => world.NoteRead(chunk.ReadLeft, chunk.ReadTop, chunk.ReadRight, chunk.ReadBottom);

    private static int FloorDiv(int a, int b) => (int)MathF.Floor(a / (float)b);
}
