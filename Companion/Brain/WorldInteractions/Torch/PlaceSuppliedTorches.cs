#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.WorldInteractions.Torch;

/// <summary>Native permanent torch placement with a successful-placement-only inventory debit.</summary>
public static class PlaceSuppliedTorches
{
    public static Item? Supply(Item[] bag, Item[] player)
    {
        foreach (Item item in bag) if (Usable(item)) return item;
        foreach (Item item in player) if (Usable(item)) return item;
        return null;
    }
    private static bool Usable(Item item) => item != null && !item.IsAir && item.stack > 0
        && item.consumable && item.createTile > -1 && item.createTile < TileID.Sets.Torch.Length && TileID.Sets.Torch[item.createTile];

    public static bool Candidate(Point tile)
    {
        if (!WorldGen.InWorld(tile.X, tile.Y, 6) || WorldProtection.ProtectCompanionHomes.IsProtected(tile)) return false;
        Tile t = Main.tile[tile.X, tile.Y];
        if (t.HasTile || t.LiquidAmount > 0) return false;
        return true; // Smart Cursor owns attachment geometry; this gate owns our no-edit policy.
    }

    public static bool Place(Point tile, Item[] bag, Player player, out string source)
    {
        source = "no supply";
        Item? item = Supply(bag, player.inventory);
        if (item == null) return false;
        source = System.Array.IndexOf(bag, item) >= 0 ? "companion bag" : "player inventory";
        if (!Candidate(tile) || !RecommendTorchPlacement.Accepts(tile, item, player)) return false;
        WorldGen.PlaceTile(tile.X, tile.Y, item.createTile, mute: false, forced: false, plr: player.whoAmI, style: item.placeStyle);
        Tile placed = Main.tile[tile.X, tile.Y];
        if (!placed.HasTile || placed.TileType != item.createTile) return false;
        item.stack--;
        if (item.stack <= 0) item.TurnToAir();
        return true;
    }

}
