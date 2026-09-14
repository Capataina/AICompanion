#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

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
    /// <summary>Whether a body standing on row y - 1 is held up by this tile: a solid block or a platform.</summary>
    public static bool IsSupport(int x, int y) => NavGrid.IsSupport(x, y);
    public static bool IsLiquid(int x, int y) => NavGrid.IsLiquid(x, y);
    public static bool IsLava(int x, int y) => NavGrid.IsLava(x, y);
    public static int BodyHeightTiles => NavGrid.BodyHeightTiles;
    public static Reachability.Reach WalkerReach(Point from, Point to) => Reachability.WalkerReach(from, to);
    public static bool WalkerCanReach(Point from, Point to) => Reachability.WalkerCanReach(from, to);
    public static bool WalkerProvenReach(Point from, Point to) => Reachability.WalkerProvenReach(from, to);
    public static Reachability.RoundTripEvidence RoundTrip(Point from, Point to, Reachability.BreathEnvelope breath) => Reachability.RoundTrip(from, to, breath);
    public static bool FlyerCanReach(Point from, Point to, int arriveRadius = 3) => Reachability.FlyerCanReach(from, to, arriveRadius);
    public static HashSet<Point> Region(Point from, int budget, out bool complete, bool refuseOneWay = false) => AStar.Region(from, budget, out complete, refuseOneWay);

    // ── the orb's readings ──────────────────────────────────────────────────────────────────────
    /// <summary>The tile a world point is in.</summary>
    public static Point Tile(Vector2 point) => new((int)System.MathF.Floor(point.X / 16f), (int)System.MathF.Floor(point.Y / 16f));
    public static Vector2 TileCentre(Point tile) => new(tile.X * 16f + 8f, tile.Y * 16f + 8f);
    public static Vector2 CornerWorld(Point corner) => CornerGraph.ToWorld(corner);
    /// <summary>A wall to the orb's contact: a full block, a half block, a slope or a closed door; never a platform.</summary>
    public static bool IsSolidForOrb(int x, int y) => OrbTerrain.Solid(World, x, y);
    /// <summary>Free for the orb under this tick's immunities: not solid and not a forbidden liquid.</summary>
    public static bool IsFreeForOrb(int x, int y) => OrbTerrain.Free(World, x, y);
    public static bool IsWet(int x, int y) => World.InWorld(x, y) && World.LiquidAmount(x, y) > 0;
    /// <summary>Distance from a free tile to the nearest wall, in tiles, capped; zero for a wall.</summary>
    public static float Clearance(int x, int y) => ClearanceField.Shared.At(World, x, y);
    /// <summary>Whether the body fits centred on this corner: its four tiles are free.</summary>
    public static bool IsUsableCorner(Point corner) => CornerGraph.Usable(World, corner);
    /// <summary>The nearest corner the body fits at, within a ring radius of the point, or null.</summary>
    public static Point? NearestUsableCorner(Vector2 point, int radius = 2, bool requireSweep = true) => CornerGraph.NearestUsable(World, point, radius, requireSweep);
}
