using Terraria;
using Terraria.ModLoader;

namespace AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>One invalidation entry point for engine hooks and explicit world interactions.</summary>
public static class TerrainChanges
{
    /// <summary>Where the world was edited, beside how many times. Every announcement carries its
    /// tile, so a consumer holding retained work — a flood, a route, a clearance chunk — asks
    /// whether an edit landed anywhere it read rather than restarting on a counter that moves for
    /// the whole loaded world. Doors announce through <see cref="Changed"/> like everything else,
    /// which is why the game's door helper skipping the ordinary tile hooks costs nothing here.</summary>
    public static readonly TerrainEditLog Edits = new();

    public static int Revision => Edits.Revision;
    public static void Changed(int x, int y)
    {
        Edits.Record(x, y);
        EditObserved?.Invoke(x, y);
    }

    /// <summary>Told of every announced edit, after the edit log. Null unless the recorder is writing a session,
    /// which is how a capture carries terrain edits as edits rather than as a running count. A delegate rather than
    /// a call, because this file is compiled into the headless tools and the recorder is not.</summary>
    public static System.Action<int, int>? EditObserved { get; set; }

    /// <summary>Everything changed and no tile can be named: a world loading or unloading, or a
    /// fixture rebuilding its scene. Every retained search asks the record and finds its revision
    /// unanswerable; the clearance field is dropped outright.</summary>
    public static void Reset()
    {
        Edits.Reset();
        ClearanceField.Shared.Invalidate();
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
    /// town NPC walking through one, or a wire pulsing one changes what the body can pass through
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
    /// kind of change is what stops the two drifting. One column rather than the two an open door
    /// spans, and the row the caller named rather than the door's own top, because every consumer
    /// of this record inflates what it read by at least a tile, so naming any tile of a three-tall
    /// door reaches every search that read any of it.
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

/// <summary>The world lifecycle: nothing the orb learns about terrain outlives the world it learned it in.</summary>
public sealed class ResetTerrainChanges : ModSystem
{
    public override void Load() => TerrainChanges.InstallDoorHooks();
    public override void Unload() => TerrainChanges.RemoveDoorHooks();
    public override void OnWorldLoad() => TerrainChanges.Reset();
    public override void OnWorldUnload() => TerrainChanges.Reset();
}
