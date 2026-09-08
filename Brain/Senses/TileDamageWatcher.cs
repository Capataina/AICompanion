#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Work;

namespace AICompanion.Brain.Senses;

/// <summary>
/// Knows when the player is really damaging a tree or an ore, because the game tells
/// us: every axe or pickaxe hit, including the ones that only crack the tile, goes
/// through WorldGen.KillTile and so through this GlobalTile hook. Hits made by the
/// companion's own tools are excluded by a flag they raise around their own call. A
/// modded axe-sword swung at a boss never hits a tree tile, so it never counts.
///
/// The clock advances from the world update, not from the companion, so the memory
/// ages while the companion is downed or absent, and it resets on every world load.
/// </summary>
public sealed class TileDamageWatcher : GlobalTile
{
    private const int RememberTicks = 45;

    /// <summary>Raised by the companion's chopper and miner around their own KillTile calls.</summary>
    public static bool CompanionIsHitting;

    private static Point? lastTree;
    private static int lastTreeTick = -1000;
    private static Point? lastOre;
    private static int lastOreType;
    private static int lastOreTick = -1000;
    private static int tick;

    /// <summary>The bottom trunk tile the player hit within the last three quarters of a second, or null.</summary>
    public static Point? TreeHitByPlayerRecently()
        => lastTree != null && tick - lastTreeTick <= RememberTicks ? lastTree : null;

    /// <summary>The ore tile the player hit within the last three quarters of a second, with its tile type, or null.</summary>
    public static (Point Tile, int Type)? OreHitByPlayerRecently()
        => lastOre is Point p && tick - lastOreTick <= RememberTicks ? (p, lastOreType) : null;

    /// <summary>Ticks since the player last hit any ore; large when never.</summary>
    public static int TicksSinceOreHit => tick - lastOreTick;

    internal static void Advance() => tick++;

    internal static void Reset()
    {
        lastTree = null;
        lastTreeTick = -1000;
        lastOre = null;
        lastOreTick = -1000;
        tick = 0;
        CompanionIsHitting = false;
    }

    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (CompanionIsHitting || effectOnly || Main.gameMenu)
            return;
        if (TreeFinder.IsTreeType(type))
        {
            lastTree = TreeFinder.TrunkBottom(i, j);
            lastTreeTick = tick;
        }
        else if (TileID.Sets.Ore[type])
        {
            lastOre = new Point(i, j);
            lastOreType = type;
            lastOreTick = tick;
        }
    }
}

/// <summary>Drives the watcher's clock from the world update and clears it between worlds.</summary>
public sealed class TileDamageClock : ModSystem
{
    public override void PostUpdateEverything() => TileDamageWatcher.Advance();
    public override void OnWorldLoad() => TileDamageWatcher.Reset();
    public override void OnWorldUnload() => TileDamageWatcher.Reset();
}
