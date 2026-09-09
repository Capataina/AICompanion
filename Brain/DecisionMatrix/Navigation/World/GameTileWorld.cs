#nullable enable

using Terraria;
using Terraria.ID;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>The live world, read from the game's tile array. The one file under Navigation/ that may name a Terraria type.</summary>
public sealed class GameTileWorld : ITileWorld
{
    public bool InWorld(int x, int y) => WorldGen.InWorld(x, y, 5);

    /// <summary>
    /// The shape the game's own collision uses: an actuated block is skipped by it, a platform
    /// is a top surface only, and a solid block carries its hammered shape in the slope and
    /// half-block bits, which is how a worldgen staircase is walkable although every tile in
    /// it "has a solid tile".
    /// </summary>
    public TileShape Shape(int x, int y)
    {
        if (!InWorld(x, y))
            return TileShape.Solid;
        Tile t = Main.tile[x, y];
        if (!t.HasTile || t.IsActuated)
            return TileShape.Air;
        // The game collides with a tile when tileSolid, or when tileSolidTop with frame 0; a
        // tile that collides and is tileSolidTop behaves as a top surface whatever its frame.
        // Vanilla platforms carry both flags and their style in frameY (18 per style), so a
        // frame test on every solid-top tile turned every non-default platform style into air.
        bool solidTop = Main.tileSolidTop[t.TileType];
        bool solid = Main.tileSolid[t.TileType];
        // The game's slope ids 1..4 are the enum's own values; 0 is no slope.
        int slope = (int)t.Slope;
        if (solidTop && (solid || t.TileFrameY == 0))
        {
            // A platform can be hammered, and then its geometry is the shape and not a flat top.
            // Reading it as a plain platform put the body on a surface the game does not give it:
            // Collision.SlopeCollision acts on any tile with a slope id, and every branch of it
            // that would push a body upward is guarded by `fall && Platforms[type]`, so with no
            // fall-through requested the diagonal applies and the engine rests the body on it. The
            // grid believed a flat tread, the game gave a ramp, and a pose proven on the tread is
            // a pose the body never holds.
            //
            // The cost of being right here is that a hammered platform stops offering a
            // fall-through edge, since a platform is the only thing that edge exists for. That is
            // the correct trade rather than a regression: a body resting on a diagonal is not
            // inside the platform's top band, which is the band the engine's own platform catch
            // tests, so the press would not have carried it through anyway.
            if (t.IsHalfBlock)
                return TileShape.Half;
            return slope == 0 ? TileShape.Platform : (TileShape)slope;
        }
        if (!solid)
            return TileShape.Air;
        if (t.IsHalfBlock)
            return TileShape.Half;
        return slope == 0 ? TileShape.Solid : (TileShape)slope;
    }

    public bool Water(int x, int y)
    {
        if (!InWorld(x, y))
            return false;
        Tile t = Main.tile[x, y];
        return t.LiquidAmount > 0 && t.LiquidType != LiquidID.Lava;
    }

    public bool Lava(int x, int y)
    {
        if (!InWorld(x, y))
            return false;
        Tile t = Main.tile[x, y];
        return t.LiquidAmount > 0 && t.LiquidType == LiquidID.Lava;
    }
}
