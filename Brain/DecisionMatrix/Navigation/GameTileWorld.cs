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
        if (Main.tileSolidTop[t.TileType])
            return t.TileFrameY == 0 ? TileShape.Platform : TileShape.Air;
        if (!Main.tileSolid[t.TileType])
            return TileShape.Air;
        if (t.IsHalfBlock)
            return TileShape.Half;
        // The game's slope ids 1..4 are the enum's own values; 0 is no slope.
        int slope = (int)t.Slope;
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
