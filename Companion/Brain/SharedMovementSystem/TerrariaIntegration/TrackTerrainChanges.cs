using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using System;
using System.Text;

namespace AICompanion.Companion.Brain.SharedMovementSystem;

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
    private const string ExecutedRoutesKey = "executedRoutes";
    // TagIO stores strings with a signed Int16 byte count. Route JSON can exceed that
    // independently of its edge cap, so this optional archive is always a byte[] instead.
    private const int MaximumRouteArchiveBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public override void OnWorldLoad() { TerrainChanges.Reset(); RememberExecutedRoutes.World.Clear(); }
    public override void OnWorldUnload() { TerrainChanges.Reset(); RememberExecutedRoutes.World.Clear(); }
    public override void SaveWorldData(TagCompound tag)
    {
        tag.Remove(ExecutedRoutesKey);
        byte[] archive = StrictUtf8.GetBytes(RememberExecutedRoutes.World.Save());
        if (archive.Length > MaximumRouteArchiveBytes)
        {
            Mod.Logger.Warn("Discarded oversized companion route memory while saving; routes will be learned again.");
            return;
        }
        tag[ExecutedRoutesKey] = archive;
    }
    public override void LoadWorldData(TagCompound tag)
    {
        RememberExecutedRoutes.World.Clear();
        if (!tag.ContainsKey(ExecutedRoutesKey)) return;
        if (!TryReadArchive(tag[ExecutedRoutesKey], out string archive) || !RememberExecutedRoutes.World.Load(archive))
            Mod.Logger.Warn("Discarded invalid or unsupported companion route memory; routes will be learned again.");
    }

    private static bool TryReadArchive(object? stored, out string archive)
    {
        archive = string.Empty;
        try
        {
            switch (stored)
            {
                case byte[] bytes when bytes.Length <= MaximumRouteArchiveBytes:
                    archive = StrictUtf8.GetString(bytes);
                    return true;
                // Pre-byte-array worlds can be read only while their string was within TagIO's
                // signed-short limit. Larger legacy strings were already malformed on disk.
                case string legacy when StrictUtf8.GetByteCount(legacy) <= short.MaxValue:
                    archive = legacy;
                    return true;
                default:
                    return false;
            }
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
