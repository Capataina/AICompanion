#nullable enable

using Terraria;
using Terraria.ID;
using AICompanion.Companion.Brain.WorldObservation;

namespace AICompanion.Companion.Brain.WorldInteractions.Chopping;

/// <summary>
/// Chops a tree the way the player's axe does. The companion owns its own
/// <see cref="HitTile"/>, the game's per-hitter table of cracked tiles, and applies
/// the exact vanilla axe formula from Player.ItemCheck_UseMiningTools: damage is
/// axe power times 1.2 (times 3 on cactus, rounded once), zero if the tile cannot be
/// killed, the tile breaks at 100 accumulated damage, and every non-lethal hit is a
/// KillTile(fail: true) so the game plays the chop sound and dust.
///
/// This is a tool, not a behaviour: the chop action decides which tree and when,
/// and calls <see cref="Swing"/> when it is in position.
/// </summary>
public sealed class TileChopper
{
    /// <summary>The cracked-tile table; drawn by <see cref="TileCracksRenderer"/>.</summary>
    public HitTile HitTile { get; } = new();

    private int swingCooldown;

    /// <summary>The axe the companion swings: the player's, or a copper axe if the player holds none.</summary>
    public static Item AxeFor(Player player)
        => player.HeldItem.axe > 0 ? player.HeldItem : ContentSamples.ItemsByType[ItemID.CopperAxe];

    public bool Ready => swingCooldown <= 0;

    public void Tick()
    {
        if (swingCooldown > 0)
            swingCooldown--;
    }

    /// <summary>Hit the trunk bottom once with the given axe. Returns true if a swing happened.</summary>
    public bool Swing(Microsoft.Xna.Framework.Point trunkBottom, Item axe)
    {
        if (!Ready || WorldProtection.ProtectCompanionHomes.IsProtected(trunkBottom))
            return false;
        swingCooldown = axe.useTime;
        Hit(trunkBottom.X, trunkBottom.Y, axe.axe);
        return true;
    }

    public static bool TreeStands(Microsoft.Xna.Framework.Point trunkBottom)
    {
        Tile tile = Main.tile[trunkBottom.X, trunkBottom.Y];
        return tile.HasTile && Main.tileAxe[tile.TileType];
    }

    private void Hit(int x, int y, int axePower)
    {
        Tile tile = Main.tile[x, y];
        if (!tile.HasTile)
            return;

        int id = HitTile.HitObject(x, y, 1);
        int damage = (int)(axePower * (tile.TileType == TileID.Cactus ? 3 : 1) * 1.2f);
        if (!WorldGen.CanKillTile(x, y))
            damage = 0;

        TileDamageWatcher.CompanionIsHitting = true;
        try
        {
            if (HitTile.AddDamage(id, damage) >= 100)
            {
                HitTile.Clear(id);
                WorldGen.KillTile(x, y);
            }
            else
            {
                WorldGen.KillTile(x, y, fail: true);
            }
        }
        finally
        {
            TileDamageWatcher.CompanionIsHitting = false;
        }
        if (damage != 0)
            HitTile.Prune();
    }
}
