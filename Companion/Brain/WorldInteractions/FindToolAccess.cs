#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.SharedMovementSystem;

namespace AICompanion.Companion.Brain.WorldInteractions;

/// <summary>Shared tile-tool range, exposed-face access and reachable working positions.
/// Native material and mutation permission remain owned by the individual tool.</summary>
public static class FindToolAccess
{
    /// <summary>The player's own reach (the game keeps it as a static set from the local player each frame), so accessories that extend it extend the companion's.</summary>
    private static int ReachX => Player.tileRangeX;
    private static int ReachY => Player.tileRangeY;

    /// <summary>
    /// A standable feet tile within reach of the tile whose eye has a line to it and that the
    /// walker can reach from <paramref name="fromFeet"/>, nearest to the tile first.
    /// </summary>
    public static Reachability.Reach Approach(Point tile, Vector2 fromFeet, out Vector2 stand)
    {
        // Tool access at the actual pose needs no route to a representative standing node.
        // Requiring that route can reject usable reach or move the body out of a working pose.
        if (InReach(fromFeet, tile))
        {
            stand = fromFeet;
            return Reachability.Reach.Yes;
        }
        Vector2 tileCentre = tile.ToWorldCoordinates(8f, 8f);
        Point from = MovementQueries.FeetTile(fromFeet);
        Vector2? best = null;
        bool unknown = false;
        float bestDist = float.MaxValue;
        for (int dx = -ReachX; dx <= ReachX; dx++)
        {
            for (int dy = -ReachY; dy <= ReachY + 2; dy++)
            {
                int x = tile.X + dx, y = tile.Y + dy;
                if (!MovementQueries.IsStandable(x, y))
                    continue;
                Vector2 feet = MovementQueries.FeetWorld(new Point(x, y));
                if (!InReach(feet, tile) || !InReach(feet + new Vector2(-8, 0), tile) || !InReach(feet + new Vector2(8, 0), tile))
                    continue;
                float d = Vector2.DistanceSquared(feet + Eye, tileCentre);
                Reachability.Reach reach = MovementQueries.WalkerReach(from, new Point(x, y));
                if (reach == Reachability.Reach.Unknown)
                    unknown = true;
                if (d < bestDist && reach == Reachability.Reach.Yes)
                {
                    bestDist = d;
                    best = feet;
                }
            }
        }
        stand = best ?? default;
        return best != null ? Reachability.Reach.Yes : unknown ? Reachability.Reach.Unknown : Reachability.Reach.No;
    }

    private static readonly Vector2 Eye = new(0f, -30f);

    /// <summary>Whether a swing from <paramref name="feet"/> can reach <paramref name="tile"/>: inside the player's native reach box and with a line to one exposed face.</summary>
    public static bool InReach(Vector2 feet, Point tile)
    {
        Vector2 eye = feet + Eye;
        Vector2 tileCentre = tile.ToWorldCoordinates(8f, 8f);
        return System.MathF.Abs(eye.X - tileCentre.X) <= ReachX * 16f + 8f
            && System.MathF.Abs(eye.Y - tileCentre.Y) <= ReachY * 16f + 8f
            && HasLineToExposedFace(eye, tile);
    }

    private static bool HasLineToExposedFace(Vector2 eye, Point tile)
    {
        // PickTile applies damage and native kill permission without enforcing tool reach here.
        // The companion supplies both range and occlusion so its closed-set
        // ability does not mine through a wall. CanHitLine includes a solid destination tile,
        // therefore the actual target is an adjacent open tile on an exposed tile face.
        foreach (Point side in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
        {
            Point face = tile + side;
            if (!WorldGen.InWorld(face.X, face.Y, 5) || WorldGen.SolidTile(face.X, face.Y))
                continue;
            if (Collision.CanHitLine(eye, 1, 1, face.ToWorldCoordinates(8f, 8f), 1, 1))
                return true;
        }
        return false;
    }
}
