#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;

/// <summary>
/// Native permanent torch placement with torches of the companion's own: placing one never uses up an item. The owner
/// ruled on 15 September 2026 that the companion's torches are free, because lighting a cave is its job and a job that
/// stops when a stack runs out reads as the companion giving up. A torch the player has handed over still decides which
/// torch goes in, so a player carrying bone or coloured torches sees those; with none anywhere it places an ordinary torch.
/// </summary>
public static class PlaceTorches
{
    private static Item? ordinary;

    /// <summary>
    /// The torch to place: the first torch item in the companion's bag, then the player's inventory, else an ordinary
    /// torch. Never null. The item is read for its tile and style only and is never debited, so a caller may pass it to
    /// the game's torch step without owning it.
    /// </summary>
    public static Item TorchToPlace(Item[] bag, Item[] player)
    {
        foreach (Item item in bag) if (IsTorch(item)) return item;
        foreach (Item item in player) if (IsTorch(item)) return item;
        if (ordinary == null)
        {
            ordinary = new Item();
            ordinary.SetDefaults(ItemID.Torch);
        }
        return ordinary;
    }

    private static bool IsTorch(Item item) => item != null && !item.IsAir && item.stack > 0
        && item.createTile > -1 && item.createTile < TileID.Sets.Torch.Length && TileID.Sets.Torch[item.createTile];

    public static bool Candidate(Point tile)
    {
        if (!WorldGen.InWorld(tile.X, tile.Y, 6) || WorldProtection.ProtectCompanionHomes.IsProtected(tile)) return false;
        Tile t = Main.tile[tile.X, tile.Y];
        if (t.HasTile || t.LiquidAmount > 0) return false;
        return true; // Smart Cursor owns attachment geometry; this gate owns our no-edit policy.
    }

    /// <summary>
    /// Place the torch <see cref="TorchToPlace"/> names at <paramref name="tile"/>, true only when the resulting tile is that
    /// torch. <paramref name="source"/> says whose torch type went in, for the record. Nothing is used up either way.
    /// </summary>
    public static bool Place(Point tile, Item[] bag, Player player, out string source)
    {
        Item item = TorchToPlace(bag, player.inventory);
        source = System.Array.IndexOf(bag, item) >= 0 ? "companion bag's torch"
            : System.Array.IndexOf(player.inventory, item) >= 0 ? "player's torch"
            : "its own torch";
        if (!Candidate(tile) || !RecommendTorchPlacement.Accepts(tile, item, player)) return false;
        WorldGen.PlaceTile(tile.X, tile.Y, item.createTile, mute: false, forced: false, plr: player.whoAmI, style: item.placeStyle);
        Tile placed = Main.tile[tile.X, tile.Y];
        bool landed = placed.HasTile && placed.TileType == item.createTile;
        if (landed) Progression.CreditWork.CompanionPlacedTorch(tile);
        return landed;
    }
}
