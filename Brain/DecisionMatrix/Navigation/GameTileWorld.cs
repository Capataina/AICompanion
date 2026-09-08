#nullable enable

using Terraria;
using Terraria.ID;

namespace AICompanion.Brain.DecisionMatrix.Navigation;

/// <summary>The live world, read from the game's tile array. The one file under Navigation/ that may name a Terraria type.</summary>
public sealed class GameTileWorld : ITileWorld
{
    public bool InWorld(int x, int y) => WorldGen.InWorld(x, y, 5);

    public bool Solid(int x, int y)
    {
        if (!InWorld(x, y))
            return true;
        Tile t = Main.tile[x, y];
        // An actuated block is drawn but not collided with, and the game's own collision skips it.
        return t.HasTile && !t.IsActuated && Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType];
    }

    public bool Support(int x, int y)
    {
        if (!InWorld(x, y))
            return false;
        Tile t = Main.tile[x, y];
        return t.HasTile && !t.IsActuated && (Main.tileSolid[t.TileType] || Main.tileSolidTop[t.TileType]);
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
