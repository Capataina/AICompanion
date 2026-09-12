#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.WorldInteractions;

/// <summary>A snapshot at a tool-call boundary. Damage belongs to this hitter's native HitTile
/// table; it is not a world-wide tile health value or proof of another actor's contribution.</summary>
public readonly record struct TileToolState(bool Present, int Type, int FrameX, int FrameY, int Damage)
{
    /// <summary>Read a caller-validated world coordinate without allocating a hit-table entry.</summary>
    public static TileToolState Capture(Point target, HitTile hits)
    {
        Tile tile = Main.tile[target.X, target.Y];
        int index = hits.TryFinding(target.X, target.Y, 1);
        return new(tile.HasTile, tile.HasTile ? tile.TileType : -1,
            tile.HasTile ? tile.TileFrameX : -1, tile.HasTile ? tile.TileFrameY : -1,
            index < 0 ? 0 : hits.data[index].damage);
    }
}

public enum TileToolEffect { NoObservedChange, Damaged, Removed, Changed }

/// <summary>Observed before/after state around one accepted native tool call. Attempt identity
/// is local to the owning tool instance; consumers join it with actor, tool and session identity.
/// A removed tile proves removal, not a particular item yield. Changed material/frame does not
/// prove harvesting, and a successful call with no observed change remains an attempt only.</summary>
public readonly record struct TileToolObservation(ulong Tick, long Attempt, Point Target, int ToolItem,
    TileToolState Before, TileToolState After)
{
    public TileToolEffect Effect => Before.Present && !After.Present ? TileToolEffect.Removed
        : Before.Present != After.Present || Before.Type != After.Type
            || Before.FrameX != After.FrameX || Before.FrameY != After.FrameY ? TileToolEffect.Changed
        : After.Damage > Before.Damage ? TileToolEffect.Damaged
        : TileToolEffect.NoObservedChange;

    public bool Productive => Effect is TileToolEffect.Damaged or TileToolEffect.Removed;
}
