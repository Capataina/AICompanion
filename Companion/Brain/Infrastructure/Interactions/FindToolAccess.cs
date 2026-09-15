#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions;

/// <summary>Shared tile-tool range, exposed-face access and reachable working positions for a body
/// that hovers. Native material and mutation permission remain owned by the individual tool.</summary>
public static class FindToolAccess
{
    /// <summary>
    /// Whether the body can get to this tile, read from the reach sense and never from a search of this
    /// query's own. One flood has already answered it for every tile in the region, where the walker
    /// query this replaced was a fresh bounded A* per pose: a tile whose poses numbered in the hundreds
    /// cost hundreds of searches, each one able to answer Unknown on its expansion budget, and a caller
    /// that could not remember an Unknown re-asked the same nearest sites until nothing was ever decided.
    /// The three answers survive the translation because they are the same three: in the region is Yes,
    /// an unfinished flood is Unknown and must be re-asked, and a flood that ran out of region is No and
    /// may be remembered.
    /// </summary>
    private static Reachability.Reach Sensed(ReachSense reach, Point tile)
        => reach.Reachable(tile) switch
        {
            ReachVerdict.Reachable => Reachability.Reach.Yes,
            ReachVerdict.NotYet => Reachability.Reach.Unknown,
            _ => Reachability.Reach.No,
        };

    /// <summary>The player's own reach (the game keeps it as a static set from the local player each frame), so accessories that extend it extend the companion's.</summary>
    public static int ReachX => Player.tileRangeX;
    public static int ReachY => Player.tileRangeY;

    /// <summary>The reach every retained access fact is derived under, as one value to key on. Reach is the player's and
    /// moves with accessories, buffs and held items, so a hover, a deferral or a "nothing reachable" verdict kept without
    /// it outlives the reach that made it true: a smaller reach flies to a cell that no longer swings, and a larger one
    /// waits out a cadence for work it could already do.</summary>
    public static (int X, int Y) Reach => (ReachX, ReachY);

    /// <summary>
    /// A cell the body can hover in within reach of the tile, whose centre has a line to an exposed face
    /// of it, and that the body can get to, nearest to the tile first. <paramref name="fromCentre"/> is
    /// the body's own centre, because the place that already reaches needs no journey at all; every other
    /// cell is answered by <paramref name="reach"/>, whose flood was run from that same body. The hover
    /// point returned is inside the cell, at the usable corner nearest the cell's centre.
    /// </summary>
    public static Reachability.Reach Approach(Point tile, Vector2 fromCentre, ReachSense reach, out Vector2 hover)
    {
        // Tool access at the actual pose needs no route to a representative node. Requiring one can
        // reject usable reach or move the body out of a working pose.
        if (InReach(fromCentre, tile))
        {
            hover = fromCentre;
            return Reachability.Reach.Yes;
        }
        Vector2 tileCentre = tile.ToWorldCoordinates(8f, 8f);
        // Rank every geometrically usable cell, then ask the sense nearest first and stop at the first
        // yes: the first yes in ascending distance is the nearest yes. The scan is a membership test
        // rather than a search, so it is cheap even when no cell is reachable — which is the case that
        // matters, because "unknown" versus "no" needs every answer.
        cells.Clear();
        for (int dx = -ReachX; dx <= ReachX; dx++)
        {
            for (int dy = -ReachY; dy <= ReachY; dy++)
            {
                var cell = new Point(tile.X + dx, tile.Y + dy);
                if (!MovementQueries.IsHoverable(cell))
                    continue;
                Vector2 point = MovementQueries.HoverPoint(cell);
                if (!InReach(point, tile))
                    continue;
                cells.Add((Vector2.DistanceSquared(point, tileCentre), cells.Count, cell, point));
            }
        }
        cells.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Order.CompareTo(b.Order));
        bool unknown = false;
        foreach (var cell in cells)
        {
            Reachability.Reach sensed = Sensed(reach, cell.Tile);
            if (sensed == Reachability.Reach.Yes)
            {
                hover = cell.Point;
                return Reachability.Reach.Yes;
            }
            unknown |= sensed == Reachability.Reach.Unknown;
        }
        hover = default;
        return unknown ? Reachability.Reach.Unknown : Reachability.Reach.No;
    }

    // Reused across calls: the brain is single-threaded and Approach never re-enters itself.
    private static readonly System.Collections.Generic.List<(float Distance, int Order, Point Tile, Vector2 Point)> cells = new();

    /// <summary>Reach is measured from the orb's centre, which is where its eye and its tools are; nothing sits above the body.</summary>
    public static float EyeHeight => 0f;

    /// <summary>Whether a swing from a body centred at <paramref name="centre"/> can reach <paramref name="tile"/>: inside the player's native reach box and with a line to one exposed face.</summary>
    public static bool InReach(Vector2 centre, Point tile)
        => InReachBox(centre, tile, ReachX, ReachY) && HasLineToExposedFace(centre, tile);

    /// <summary>The arithmetic half of <see cref="InReach"/>: whether the centre lies inside the native reach box of
    /// <paramref name="tile"/> for the given reach, with no world query. The success region a tool hover declares is this
    /// box, and diagnostics judge arrival against it without running the line test, which reads live tiles.</summary>
    public static bool InReachBox(Vector2 centre, Point tile, int reachX, int reachY)
    {
        Vector2 tileCentre = tile.ToWorldCoordinates(8f, 8f);
        return System.MathF.Abs(centre.X - tileCentre.X) <= reachX * 16f + 8f
            && System.MathF.Abs(centre.Y - tileCentre.Y) <= reachY * 16f + 8f;
    }

    private static bool HasLineToExposedFace(Vector2 eye, Point tile)
    {
        // PickTile applies damage and native kill permission without enforcing tool reach here.
        // The companion supplies both range and occlusion so its closed-set
        // ability does not mine through a wall. A tile walk refuses a solid destination tile,
        // therefore the actual target is an adjacent open tile on an exposed tile face.
        //
        // The walk is Collision.CanHit, the game's own "can this NPC see its target" test: it refuses a
        // cell the walk enters and a step squeezed between two solid full blocks. CanHitLine is not a
        // thin line — every step refuses a solid tile on either side as well, a beam three tiles wide —
        // so it refused every line along a floor and any reach into a one-tile notch whose diagonal
        // neighbour was solid, which a player's swing reaches without trouble. Projectile line of fire
        // keeps CanHitLine, because a projectile has width and a swing does not.
        foreach (Point side in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
        {
            Point face = tile + side;
            if (!WorldGen.InWorld(face.X, face.Y, 5) || WorldGen.SolidTile(face.X, face.Y))
                continue;
            if (Collision.CanHit(eye, 1, 1, face.ToWorldCoordinates(8f, 8f), 1, 1))
                return true;
        }
        return false;
    }
}
