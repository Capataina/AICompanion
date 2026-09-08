#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Work;

namespace AICompanion.Brain.Senses;

/// <summary>
/// Knows when the player is really damaging a tree, because the game tells us: every
/// axe hit, including the ones that only crack the tile, goes through WorldGen.KillTile
/// and so through this GlobalTile hook. Hits made by the companion's own chopper are
/// excluded by a flag it raises around its own call. A modded axe-sword swung at a
/// boss never hits a tree tile, so it never counts, which is the point.
/// </summary>
public sealed class TreeDamageWatcher : GlobalTile
{
    private const int RememberTicks = 45;

    /// <summary>Raised by the companion's chopper around its own KillTile calls.</summary>
    public static bool CompanionIsHitting;

    private static Point? lastTree;
    private static int lastHitTick;
    private static int tick;

    public static void Tick() => tick++;

    /// <summary>The bottom trunk tile the player hit within the last three quarters of a second, or null.</summary>
    public static Point? TreeHitByPlayerRecently()
        => lastTree != null && tick - lastHitTick <= RememberTicks ? lastTree : null;

    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (CompanionIsHitting || effectOnly || Main.gameMenu)
            return;
        if (!TreeFinder.IsTreeType(type))
            return;
        lastTree = TreeFinder.TrunkBottom(i, j);
        lastHitTick = tick;
    }
}
