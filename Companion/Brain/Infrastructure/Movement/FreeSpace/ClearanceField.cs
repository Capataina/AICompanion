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
/// inside the chunk's own reach, when the immunities change what counts as a wall, or when the
/// world object itself is replaced. Asking about a tile far from every recent edit costs a
/// dictionary lookup.
///
/// <para>Distances are capped at <see cref="MaxTiles"/>, which is a structural cap and not a
/// tunable: beyond it the corridor-middle preference has nothing left to prefer, and a smaller
/// cap makes every chunk cheaper to build by the square of the difference.</para>
/// </summary>
public sealed class ClearanceField
{
    public const int MaxTiles = 6;
    private const int ChunkSize = 16;

    private sealed class Chunk
    {
        public readonly float[] Values = new float[ChunkSize * ChunkSize];
        public int Revision;
        public LiquidImmunity Immunity;
        public ITileWorld World = null!;
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
    }

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
        var key = (cx, cy);
        if (chunks.TryGetValue(key, out Chunk? chunk))
        {
            bool sameRules = ReferenceEquals(chunk.World, world) && chunk.Immunity == OrbTerrain.Immunity;
            if (sameRules)
            {
                int revision = world.Revision;
                if (checkedAt.TryGetValue(key, out int at) && at == revision) return chunk;
                // The edit record is asked once per revision per chunk, and only about the box the
                // chunk's distances read: a wall MaxTiles outside the chunk moves a value inside it.
                int x0 = cx * ChunkSize - MaxTiles, y0 = cy * ChunkSize - MaxTiles;
                int x1 = x0 + ChunkSize + 2 * MaxTiles, y1 = y0 + ChunkSize + 2 * MaxTiles;
                if (world.ChangedSince(chunk.Revision, (tx, ty) => tx >= x0 && tx < x1 && ty >= y0 && ty < y1) == TerrainEditVerdict.Unchanged)
                {
                    checkedAt[key] = revision;
                    chunk.Revision = revision;
                    return chunk;
                }
            }
        }
        chunk ??= new Chunk();
        Build(world, cx, cy, chunk);
        chunks[key] = chunk;
        checkedAt[key] = chunk.Revision;
        return chunk;
    }

    private void Build(ITileWorld world, int cx, int cy, Chunk chunk)
    {
        Builds++;
        chunk.World = world;
        chunk.Immunity = OrbTerrain.Immunity;
        chunk.Revision = world.Revision;
        int baseX = cx * ChunkSize, baseY = cy * ChunkSize;
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
                            if (!OrbTerrain.Free(world, x + dx, y + dy)) best = distance;
                        }
                    value = best;
                }
                chunk.Values[ly * ChunkSize + lx] = value;
            }
        }
    }

    private static int FloorDiv(int a, int b) => (int)MathF.Floor(a / (float)b);
}
