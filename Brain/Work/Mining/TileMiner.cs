#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Brain.Work.Mining;

/// <summary>
/// Mines a tile the way the player's pickaxe does: the damage formula is
/// Player.GetPickaxeDamage copied (per-type multipliers, the minimum-power gates, a
/// modded tile's MineResist), the crack table is the same HitTile the chopper uses so
/// the cracks renderer shows both, the tile breaks at 100 accumulated damage, and
/// every non-lethal hit is a KillTile(fail: true) for the sound and dust.
///
/// A tool, not a behaviour: the mine action decides which tile and when.
/// </summary>
public sealed class TileMiner
{
    private readonly HitTile hitTile;
    private int swingCooldown;

    public TileMiner(HitTile sharedHitTile) => hitTile = sharedHitTile;

    /// <summary>The pickaxe the companion swings: the player's, or a copper pickaxe if the player holds none.</summary>
    public static Item PickaxeFor(Player player)
        => player.HeldItem.pick > 0 ? player.HeldItem : ContentSamples.ItemsByType[ItemID.CopperPickaxe];

    public bool Ready => swingCooldown <= 0;

    public void Tick()
    {
        if (swingCooldown > 0)
            swingCooldown--;
    }

    /// <summary>Hit the tile once with the given pickaxe. Returns true if a swing happened.</summary>
    public bool Swing(Point tile, Item pickaxe)
    {
        if (!Ready)
            return false;
        swingCooldown = pickaxe.useTime;
        Hit(tile.X, tile.Y, pickaxe.pick);
        return true;
    }

    /// <summary>Whether this pickaxe can damage the tile at all, from the same gates the player's swing uses.</summary>
    public static bool CanMine(Point tile, int pickPower)
    {
        if (!WorldGen.InWorld(tile.X, tile.Y, 5) || !Main.tile[tile.X, tile.Y].HasTile)
            return false;
        return PickaxeDamage(tile.X, tile.Y, pickPower, Main.tile[tile.X, tile.Y]) > 0;
    }

    private void Hit(int x, int y, int pickPower)
    {
        Tile tile = Main.tile[x, y];
        if (!tile.HasTile)
            return;

        int id = hitTile.HitObject(x, y, 1);
        int damage = PickaxeDamage(x, y, pickPower, tile);
        if (!WorldGen.CanKillTile(x, y))
            damage = 0;
        if (Main.getGoodWorld)
            damage *= 2;

        TileDamageWatcher.CompanionIsHitting = true;
        try
        {
            if (hitTile.AddDamage(id, damage) >= 100)
            {
                hitTile.Clear(id);
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
            hitTile.Prune();
    }

    /// <summary>Player.GetPickaxeDamage, with the tile type numbers as the game has them.</summary>
    private static int PickaxeDamage(int x, int y, int pickPower, Tile tile)
    {
        int type = tile.TileType;
        int damage = Main.tileNoFail[type] ? 100 : 0;
        ModTile? modTile = TileLoader.GetTile(type);
        if (modTile != null)
            damage += (int)(pickPower / modTile.MineResist);
        else if (Main.tileDungeon[type] || type == TileID.Ebonstone || type == TileID.Crimstone || type == TileID.Pearlstone || type == TileID.Hellstone)
            damage += pickPower / 2;
        else if (type == TileID.Tombstones)
            damage += pickPower * 2;
        else if (type == TileID.Meteorite || type == TileID.Chlorophyte)
            damage += Main.getGoodWorld ? pickPower / 4 : pickPower / 3;
        else if (type == TileID.LihzahrdBrick)
            damage += pickPower / 4;
        else if (type == TileID.Cobalt || type == TileID.Palladium)
            damage += pickPower / 2;
        else if (type == TileID.Mythril || type == TileID.Orichalcum)
            damage += pickPower / 3;
        else if (type == TileID.Adamantite || type == TileID.Titanium)
            damage += pickPower / 4;
        else if (type == TileID.LihzahrdAltar)
            damage += pickPower / 5;
        else
            damage += pickPower;

        if (type == TileID.LihzahrdAltar && pickPower < 200) damage = 0;
        else if ((type == TileID.Ebonstone || type == TileID.Crimstone) && pickPower < 65) damage = 0;
        else if (type == TileID.Pearlstone && pickPower < 65) damage = 0;
        else if (type == TileID.Meteorite && pickPower < 50) damage = 0;
        else if ((type == TileID.Demonite || type == TileID.Crimtane) && y > Main.worldSurface && pickPower < 55) damage = 0;
        else if (type == TileID.Obsidian && pickPower < 55) damage = 0;
        else if (type == TileID.Hellstone && pickPower < 65 && y >= Main.UnderworldLayer) damage = 0;
        else if ((type == TileID.LihzahrdBrick || type == TileID.LihzahrdAltar) && pickPower < 210) damage = 0;
        else if (Main.tileDungeon[type] && pickPower < 100 && y > Main.worldSurface && (x < Main.maxTilesX * 0.35 || x > Main.maxTilesX * 0.65)) damage = 0;
        else if (type == TileID.Cobalt && pickPower < 100) damage = 0;
        else if (type == TileID.Mythril && pickPower < 110) damage = 0;
        else if (type == TileID.Adamantite && pickPower < 150) damage = 0;
        else if (type == TileID.Chlorophyte && pickPower < 200) damage = 0;
        else if (type == TileID.Palladium && pickPower < 100) damage = 0;
        else if (type == TileID.Orichalcum && pickPower < 110) damage = 0;
        else if (type == TileID.Titanium && pickPower < 150) damage = 0;
        return damage;
    }
}
