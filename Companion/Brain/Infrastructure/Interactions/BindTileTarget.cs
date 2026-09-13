#nullable enable

using Microsoft.Xna.Framework;
using Terraria;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions;

/// <summary>A prepared tile's coordinate and material, independent of native use permission.
/// This detects disappearance and material replacement, not an unobserved same-type incarnation.</summary>
public readonly record struct BindTileTarget(Point Tile, int Type)
{
    public static BindTileTarget? Capture(Point point)
        => WorldGen.InWorld(point.X, point.Y, 5) && Main.tile[point.X, point.Y].HasTile
            ? new(point, Main.tile[point.X, point.Y].TileType) : null;

    public string Rejection => !WorldGen.InWorld(Tile.X, Tile.Y, 5) || !Main.tile[Tile.X, Tile.Y].HasTile
        ? "prepared-tile-unavailable"
        : Main.tile[Tile.X, Tile.Y].TileType != Type ? "prepared-tile-material-changed" : "";
}
