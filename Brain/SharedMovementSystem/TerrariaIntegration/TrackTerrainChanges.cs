using Terraria;
using Terraria.ModLoader;

namespace AICompanion.Brain.SharedMovementSystem;

/// <summary>One invalidation entry point for engine hooks and explicit world interactions.</summary>
public static class TerrainChanges
{
    public static int Revision { get; private set; }
    public static void Changed(int x, int y)
    {
        Revision++;
        AStar.TileChanged(x, y);
    }
    public static void Reset()
    {
        Revision++;
        AStar.InvalidateEdges();
    }
}

public sealed class TrackTerrainChanges : GlobalTile
{
    public override void HitWire(int i, int j, int type) => TerrainChanges.Changed(i, j);
    public override bool Slope(int i, int j, int type)
    {
        TerrainChanges.Changed(i, j);
        return true;
    }
    public override void PlaceInWorld(int i, int j, int type, Item item) => TerrainChanges.Changed(i, j);
    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (!fail && !effectOnly) TerrainChanges.Changed(i, j);
    }
}

public sealed class ResetTerrainChanges : ModSystem
{
    public override void OnWorldLoad() => TerrainChanges.Reset();
    public override void OnWorldUnload() => TerrainChanges.Reset();
}
