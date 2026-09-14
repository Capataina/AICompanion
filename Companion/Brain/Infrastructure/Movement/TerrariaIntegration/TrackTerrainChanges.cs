using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using System;
using System.Text;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>One invalidation entry point for engine hooks and explicit world interactions.</summary>
public static class TerrainChanges
{
    /// <summary>Where the world was edited, beside how many times. Every announcement carries its
    /// tile, so a consumer holding retained work can ask whether an edit landed anywhere it read
    /// rather than restarting on a counter that moves for the whole loaded world. Doors announce
    /// through <see cref="Changed"/> like everything else, which is why the game's door helper
    /// skipping the ordinary tile hooks costs nothing here.</summary>
    public static readonly TerrainEditLog Edits = new();

    public static int Revision => Edits.Revision;
    public static void Changed(int x, int y)
    {
        Edits.Record(x, y);
        AStar.TileChanged(x, y);
    }
    public static void Reset()
    {
        Edits.Reset();
        AStar.InvalidateEdges();
    }

    // ── doors toggled by anybody ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the door detours are installed. The loader owns this in the game and a fixture owns
    /// it headlessly, because <see cref="ResetTerrainChanges"/> is a <c>ModSystem</c> whose
    /// <c>Load</c> never runs in the replay host — the same absence
    /// <c>VerifyOreWork.InitialiseVanillaTileHooks</c> fills for the tile hook arrays.
    /// </summary>
    private static bool doorsHooked;

    /// <summary>
    /// Make a door toggled by anything at all announce its rows. The game's door helpers write
    /// tiles through neither the placement nor the destruction hook, so a player opening a door, a
    /// town NPC walking through one, or a wire pulsing one changes what the body can walk through
    /// and nothing downstream hears about it. The companion's own toggles were never the gap —
    /// <c>DoorOpener</c> announces those by hand — and every other toggle was invisible.
    ///
    /// <para>Under a world-global counter that gap closed itself: stale knowledge from an
    /// unannounced change was thrown away by the next edit anywhere within seconds. Under the
    /// spatial rule a retained route through that doorway survives until an edit lands inside the
    /// query's own box, which for a player who shuts a door and then mines a screen away is
    /// indefinitely, and the catch is a physical fault at the doorway with nothing pointing at
    /// stale knowledge. Narrowing invalidation is what makes an unannounced change expensive, so
    /// the announcement has to become complete in the same breath.</para>
    ///
    /// <para>The detours are the seam because the swing decision is the game's own: the helpers
    /// refuse a blocked or locked door and report it, so an announcement made before them would
    /// announce toggles that never happened. Each therefore runs the original first and announces
    /// only on a true return.</para>
    /// </summary>
    public static void InstallDoorHooks()
    {
        if (doorsHooked) return;
        doorsHooked = true;
        On_WorldGen.OpenDoor += AnnounceOpenedDoor;
        On_WorldGen.CloseDoor += AnnounceClosedDoor;
        On_WorldGen.ShiftTallGate += AnnounceShiftedGate;
    }

    /// <summary>Undo them. tModLoader removes its own detours when a mod unloads, so in the game
    /// this is belt and braces; headlessly it is the only thing that stops one fixture's hooks
    /// leaking into the next one's revision counts.</summary>
    public static void RemoveDoorHooks()
    {
        if (!doorsHooked) return;
        doorsHooked = false;
        On_WorldGen.OpenDoor -= AnnounceOpenedDoor;
        On_WorldGen.CloseDoor -= AnnounceClosedDoor;
        On_WorldGen.ShiftTallGate -= AnnounceShiftedGate;
    }

    private static bool AnnounceOpenedDoor(On_WorldGen.orig_OpenDoor orig, int i, int j, int direction)
    {
        bool opened = orig(i, j, direction);
        if (opened) DoorRows(i, j);
        return opened;
    }

    private static bool AnnounceClosedDoor(On_WorldGen.orig_CloseDoor orig, int i, int j, bool forced)
    {
        bool closed = orig(i, j, forced);
        if (closed) DoorRows(i, j);
        return closed;
    }

    private static bool AnnounceShiftedGate(On_WorldGen.orig_ShiftTallGate orig, int x, int y, bool closing, bool forced)
    {
        bool shifted = orig(x, y, closing, forced);
        if (shifted) DoorRows(x, y);
        return shifted;
    }

    /// <summary>
    /// The three rows a door occupies, at the column the toggle named. It is the announcement
    /// <c>DoorOpener</c> already makes, deliberately, because one shape of announcement for one
    /// kind of change is what stops the two drifting.
    ///
    /// <para>One column rather than the two an open door spans, and the row the caller named
    /// rather than the door's own top, because every consumer of this record inflates it: a
    /// retained query is sensitive within <c>AStar.ScanReaches</c> of what it read, which is two
    /// dozen columns and a dozen rows, and the edge cache is dropped in that same box. Naming any
    /// tile of a three-tall door therefore reaches every query that read any of it.</para>
    /// </summary>
    private static void DoorRows(int x, int y)
    {
        for (int row = y - 1; row <= y + 1; row++) Changed(x, row);
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

    public override void Load() => TerrainChanges.InstallDoorHooks();
    public override void Unload() => TerrainChanges.RemoveDoorHooks();
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
