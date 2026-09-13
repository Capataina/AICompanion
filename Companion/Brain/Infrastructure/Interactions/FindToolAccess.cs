#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Movement;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions;

/// <summary>Shared tile-tool range, exposed-face access and reachable working positions.
/// Native material and mutation permission remain owned by the individual tool.</summary>
public static class FindToolAccess
{
    /// <summary>The player's own reach (the game keeps it as a static set from the local player each frame), so accessories that extend it extend the companion's.</summary>
    public static int ReachX => Player.tileRangeX;
    public static int ReachY => Player.tileRangeY;

    /// <summary>The reach every retained access fact is derived under, as one value to key on. Reach is the player's and
    /// moves with accessories, buffs and held items, so a stand, a deferral or a "nothing reachable" verdict kept without
    /// it outlives the reach that made it true: a smaller reach walks to a pose that no longer swings, and a larger one
    /// waits out a cadence for work it could already do.</summary>
    public static (int X, int Y) Reach => (ReachX, ReachY);

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
        // Rank every geometrically usable pose before asking whether the walker can reach it, then
        // ask nearest first and stop at the first yes. Each reach question is a fresh bounded A*
        // search, and asking all of them to keep the nearest yes cost the whole planning allowance
        // on the 2026-09-13 brain-cost scene. The answer is identical: the first yes in ascending
        // distance is the nearest yes, and equal distances keep the scan order the exhaustive loop
        // broke ties with. Only when no pose is reachable does the full scan still run, because
        // "unknown" versus "no" needs every answer.
        poses.Clear();
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
                poses.Add((Vector2.DistanceSquared(feet + Eye, tileCentre), poses.Count, new Point(x, y), feet));
            }
        }
        poses.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Order.CompareTo(b.Order));
        bool unknown = false;
        foreach (var pose in poses)
        {
            Reachability.Reach reach = MovementQueries.WalkerReach(from, pose.Tile);
            if (reach == Reachability.Reach.Yes)
            {
                stand = pose.Feet;
                return Reachability.Reach.Yes;
            }
            unknown |= reach == Reachability.Reach.Unknown;
        }
        stand = default;
        return unknown ? Reachability.Reach.Unknown : Reachability.Reach.No;
    }

    /// <summary>
    /// A tile no standing pose can swing at, reached the way a player reaches a ceiling: walk to a
    /// take-off pose, jump, and swing while the rising body's reach covers the tile. A pose counts
    /// only when the shared body model proves a dry ground jump from rest there brings the tile into
    /// reach and lands back beside the take-off, and only then is the walker asked whether it can get
    /// there, nearest pose first with the same early stop as <see cref="Approach"/>. The
    /// <paramref name="body"/> supplies everything about the companion except where it stands, so a
    /// capability that changes the jump changes the proof.
    /// </summary>
    public static Reachability.Reach HopApproach(Point tile, Vector2 fromFeet, BodyState body, out Vector2 stand)
    {
        stand = default;
        // A tile with no open neighbour has no face a line can reach from any height.
        if (!HasOpenFace(tile))
            return Reachability.Reach.No;
        Vector2 tileCentre = tile.ToWorldCoordinates(8f, 8f);
        Point from = MovementQueries.FeetTile(fromFeet);
        poses.Clear();
        for (int dx = -ReachX; dx <= ReachX; dx++)
        {
            for (int dy = -ReachY; dy <= ReachY + HopRiseTiles; dy++)
            {
                int x = tile.X + dx, y = tile.Y + dy;
                if (!MovementQueries.IsStandable(x, y))
                    continue;
                Vector2 feet = MovementQueries.FeetWorld(new Point(x, y));
                // A pose that already reaches standing belongs to Approach; hopping from it adds nothing.
                if (InReach(feet, tile))
                    continue;
                // A jump only raises the eye, so a tile level with or below the standing eye gets no nearer by
                // rising. Skipping those before any body proof matters beyond cost: on top of a ceiling slab every
                // pose sits above the ore it cannot see, and proving each one ran the scan past the planning
                // deadline, which a cut-short scan must report as Unknown rather than the No it really is.
                if (tileCentre.Y >= feet.Y + Eye.Y)
                    continue;
                poses.Add((Vector2.DistanceSquared(feet + Eye, tileCentre), poses.Count, new Point(x, y), feet));
            }
        }
        poses.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Order.CompareTo(b.Order));
        bool unknown = false;
        foreach (var pose in poses)
        {
            // The proof below starts the body at rest on the take-off, but the body gets there by walking, and
            // BodyPhysics.Stand accepts a pose that overhangs an edge by two pixels. A pose is admitted only if a
            // body still sliding to rest from walking speed, in either direction, keeps some support under it,
            // and only if it is dry, because the proof is a dry jump and a wet body cannot make it.
            if (!SupportedThroughArrivalSlide(pose.Feet.X, pose.Tile.Y) || MovementQueries.IsLiquid(pose.Tile.X, pose.Tile.Y))
                continue;
            // The body simulation does not watch the planning deadline. A scan cut short has established
            // nothing about the poses it never reached, so it is Unknown and never No.
            if (LimitPlanningWork.Expired)
            {
                unknown = true;
                break;
            }
            BodyState rest = body with
            {
                Left = pose.Feet.X - BodyPhysics.Width / 2f, Bottom = pose.Feet.Y, Vx = 0f, Vy = 0f, OnGround = true,
                CollideX = false, Stuck = false, Pinned = false, Wet = false, StairFall = false, LiquidKind = 0,
            };
            if (!ProveInteractionJump.CanReach(NavGrid.World, rest, rising => InReach(rising.Feet, tile)))
                continue;
            Reachability.Reach reach = MovementQueries.WalkerReach(from, pose.Tile);
            if (reach == Reachability.Reach.Yes)
            {
                stand = pose.Feet;
                return Reachability.Reach.Yes;
            }
            unknown |= reach == Reachability.Reach.Unknown;
        }
        return unknown ? Reachability.Reach.Unknown : Reachability.Reach.No;
    }

    /// <summary>How many tiles above standing reach a ground jump can lift the eye: the apex of a jump at
    /// the body's own take-off speed under ordinary gravity, rounded up. Air jumps are not ground hops. The
    /// bound understates the real rise where gravity is reduced: near the sky BodyMotion.GravityAt lowers
    /// gravity, the true apex is higher, and a take-off further below the tile than this is never searched.</summary>
    private static readonly int HopRiseTiles = (int)System.MathF.Ceiling(
        BodyPhysics.JumpVelocity * BodyPhysics.JumpVelocity / (2f * BodyPhysics.Gravity) / 16f);

    /// <summary>How far a body moving at walking speed slides before it stops with no input: StepVelocity takes
    /// the slowdown off every tick, so the distance is v²/2a. It mirrors BodyPhysics' walking speed and slowdown
    /// and must move with them, or a take-off admitted here is one a real arrival slides off.</summary>
    private static readonly float ArrivalSlide = BodyPhysics.WalkSpeed * BodyPhysics.WalkSpeed / (2f * BodyPhysics.Slowdown);

    /// <summary>Whether a body with its feet at <paramref name="feetX"/> in <paramref name="row"/> keeps support
    /// under some column it covers when it comes to rest at that point or an arrival slide either side of it.
    /// Three samples suffice: two supported samples an arrival slide apart leave no room between them for a gap
    /// wide enough to let a body twenty pixels wide fall through.</summary>
    private static bool SupportedThroughArrivalSlide(float feetX, int row)
        => SupportedAt(feetX, row) && SupportedAt(feetX - ArrivalSlide, row) && SupportedAt(feetX + ArrivalSlide, row);

    private static bool SupportedAt(float feetX, int row)
    {
        int first = (int)System.MathF.Floor((feetX - BodyPhysics.Width / 2f) / 16f);
        int last = (int)System.MathF.Floor((feetX + BodyPhysics.Width / 2f - 0.001f) / 16f);
        for (int x = first; x <= last; x++)
            if (NavGrid.IsSupport(x, row + 1))
                return true;
        return false;
    }

    private static bool HasOpenFace(Point tile)
    {
        foreach (Point side in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
        {
            Point face = tile + side;
            if (WorldGen.InWorld(face.X, face.Y, 5) && !WorldGen.SolidTile(face.X, face.Y))
                return true;
        }
        return false;
    }

    // Reused across calls: the brain is single-threaded and Approach never re-enters itself.
    private static readonly System.Collections.Generic.List<(float Distance, int Order, Point Tile, Vector2 Feet)> poses = new();

    private static readonly Vector2 Eye = new(0f, -30f);
    /// <summary>How far above the feet reach is measured from; read from the one offset <see cref="InReach"/> uses, so a drawing of the reach box cannot drift from the test.</summary>
    public static float EyeHeight => -Eye.Y;

    /// <summary>Whether a swing from <paramref name="feet"/> can reach <paramref name="tile"/>: inside the player's native reach box and with a line to one exposed face.</summary>
    public static bool InReach(Vector2 feet, Point tile)
        => InReachBox(feet, tile, ReachX, ReachY) && HasLineToExposedFace(feet + Eye, tile);

    /// <summary>The arithmetic half of <see cref="InReach"/>: whether the eye over <paramref name="feet"/> lies inside the
    /// native reach box of <paramref name="tile"/> for the given reach, with no world query. The success region a tool stand
    /// declares is this box, and diagnostics judge arrival against it without running the line test, which reads live tiles.</summary>
    public static bool InReachBox(Vector2 feet, Point tile, int reachX, int reachY)
    {
        Vector2 eye = feet + Eye;
        Vector2 tileCentre = tile.ToWorldCoordinates(8f, 8f);
        return System.MathF.Abs(eye.X - tileCentre.X) <= reachX * 16f + 8f
            && System.MathF.Abs(eye.Y - tileCentre.Y) <= reachY * 16f + 8f;
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
