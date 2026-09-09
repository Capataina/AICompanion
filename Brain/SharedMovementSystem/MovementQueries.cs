#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>
/// The movement core's public read surface. Brain systems ask this façade about geometry and
/// reachability instead of coupling to the grid or A*'s mutable search settings.
/// </summary>
public static class MovementQueries
{
    public static ITileWorld World => NavGrid.World;
    public static Point FeetTile(Vector2 bottom) => NavGrid.FeetTile(bottom);
    public static Vector2 FeetWorld(Point tile) => NavGrid.FeetWorld(tile);
    public static Point? NearestStandable(Point around, int radius = 3) => NavGrid.NearestStandable(around, radius);
    public static Point? NearestStandable(Point around, int radius, System.Func<Point, bool> accept) => NavGrid.NearestStandable(around, radius, accept);
    public static bool IsStandable(int x, int y) => NavGrid.IsStandable(x, y);
    public static bool IsBodyClear(int x, int y) => NavGrid.IsBodyClear(x, y);
    public static bool IsBlock(int x, int y) => NavGrid.IsBlock(x, y);
    public static bool IsLiquid(int x, int y) => NavGrid.IsLiquid(x, y);
    public static bool IsLava(int x, int y) => NavGrid.IsLava(x, y);
    public static int BodyHeightTiles => NavGrid.BodyHeightTiles;
    public static Reachability.Reach WalkerReach(Point from, Point to) => Reachability.WalkerReach(from, to);
    public static bool WalkerCanReach(Point from, Point to) => Reachability.WalkerCanReach(from, to);
    public static bool WalkerProvenReach(Point from, Point to) => Reachability.WalkerProvenReach(from, to);
    public static bool FlyerCanReach(Point from, Point to, int arriveRadius = 3) => Reachability.FlyerCanReach(from, to, arriveRadius);
    public static HashSet<Point> Region(Point from, int budget, out bool complete, bool refuseOneWay = false) => AStar.Region(from, budget, out complete, refuseOneWay);
    public static void TerrainChanged(int x, int y) => AStar.TileChanged(x, y);
    public static void InvalidateTerrain() => AStar.InvalidateEdges();
}
