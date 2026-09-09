#nullable enable
using Terraria;
using Terraria.ID;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>Live terrain and the engine collision backend used by movement predictions.</summary>
public sealed class GameTileWorld : ITileWorld, IBodySimulationWorld
{
    public int Revision => TerrainChanges.Revision;
    public bool InWorld(int x, int y) => WorldGen.InWorld(x, y, 5);
    public bool PassThrough(int x, int y)
    {
        if (!InWorld(x, y)) return false;
        Tile tile = Main.tile[x, y];
        return tile.HasTile && !tile.IsActuated && Main.tileSolidTop[tile.TileType]
            && (Main.tileSolid[tile.TileType] || tile.TileFrameY == 0);
    }

    public TileShape Shape(int x, int y)
    {
        if (!InWorld(x, y)) return TileShape.Solid;
        Tile tile = Main.tile[x, y];
        if (!tile.HasTile || tile.IsActuated) return TileShape.Air;
        bool platform = PassThrough(x, y);
        if (!platform && !Main.tileSolid[tile.TileType]) return TileShape.Air;
        if (tile.IsHalfBlock) return TileShape.Half;
        int slope = (int)tile.Slope;
        return slope != 0 ? (TileShape)slope : platform ? TileShape.Platform : TileShape.Solid;
    }

    public bool Water(int x, int y) => InWorld(x, y) && Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType != LiquidID.Lava;
    public bool Lava(int x, int y) => InWorld(x, y) && Main.tile[x, y].LiquidAmount > 0 && Main.tile[x, y].LiquidType == LiquidID.Lava;
    public BodyState Simulate(BodyState state, Controls controls, MovementCapabilities capabilities)
        => SimulateTerrariaBody.Step(state, controls, capabilities);
}
