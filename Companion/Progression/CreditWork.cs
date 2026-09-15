#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.Infrastructure.Interactions.Chopping;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Progression;

/// <summary>
/// Work earns experience at a fixed share of the current bar: an ore tile broken, a tree felled or a torch placed, the
/// companion's at full share and the player's at half.
///
/// The companion's work is credited by its own native calls, where the world edit is observed to have happened — the miner
/// after <c>Player.PickTile</c>, the chopper after the killing <c>WorldGen.KillTile</c> on a trunk bottom, the torch
/// placer after <c>WorldGen.PlaceTile</c> left the torch. The player's is credited from the game's hooks only where his own
/// tool or hand did it: <c>GlobalTile.KillTile</c> for the tile his cursor targets while he swings a pickaxe or an axe, and
/// <c>GlobalTile.PlaceInWorld</c>, which the game calls only from the player's own item placement. World generation,
/// explosions and other NPCs edit tiles through <c>KillTile</c> without his target or his swing, and the companion's own
/// hits raise <see cref="TileDamageWatcher.CompanionIsHitting"/>, so none of them reach the player's credit.
///
/// Ore is <c>TileID.Sets.Ore</c>, the set mining already works from. A tree is felled when its bottom trunk tile is
/// killed, which takes the whole trunk above it, so a tree counts once however many tiles it had.
/// </summary>
public static class CreditWork
{
    public static bool IsOre(int type) => type >= 0 && type < TileID.Sets.Ore.Length && TileID.Sets.Ore[type];

    public static bool IsTorch(int type) => type >= 0 && type < TileID.Sets.Torch.Length && TileID.Sets.Torch[type];

    public static bool IsTrunkBottom(int x, int y, int type) => TreeFinder.IsTreeType(type) && TreeFinder.TrunkBottom(x, y) == new Point(x, y);

    /// <summary>The companion's pickaxe struck <paramref name="tile"/>, which held <paramref name="typeBefore"/>; credited if an ore is gone.</summary>
    public static void CompanionMined(Point tile, bool hadTile, int typeBefore)
    {
        if (!hadTile || !IsOre(typeBefore)) return;
        Tile after = Main.tile[tile.X, tile.Y];
        if (after.HasTile && after.TileType == typeBefore) return;
        Credit("ore", byCompanion: true, tile);
    }

    /// <summary>The companion's axe killed the trunk tile at <paramref name="tile"/>; credited if a trunk bottom is gone.</summary>
    public static void CompanionFelled(Point tile, bool wasTrunkBottom, int typeBefore)
    {
        if (!wasTrunkBottom) return;
        Tile after = Main.tile[tile.X, tile.Y];
        if (after.HasTile && after.TileType == typeBefore) return;
        Credit("tree", byCompanion: true, tile);
    }

    public static void CompanionPlacedTorch(Point tile) => Credit("torch", byCompanion: true, tile);

    /// <summary>Whether the local player's own swing is what is breaking this tile now.</summary>
    public static bool PlayerIsBreaking(int i, int j, bool pickaxe)
    {
        if (Main.gameMenu || WorldGen.gen || TileDamageWatcher.CompanionIsHitting) return false;
        Player player = Main.LocalPlayer;
        // The target tile is static on Player: it is the local player's cursor target, which is the only player here.
        if (player == null || !player.active || player.itemAnimation <= 0 || Player.tileTargetX != i || Player.tileTargetY != j) return false;
        Item held = player.HeldItem;
        return pickaxe ? held.pick > 0 : held.axe > 0;
    }

    public static void PlayerKilledTile(int i, int j, int type, bool fail, bool effectOnly)
    {
        if (fail || effectOnly) return;
        if (IsOre(type) && PlayerIsBreaking(i, j, pickaxe: true)) Credit("ore", byCompanion: false, new Point(i, j));
        else if (PlayerIsBreaking(i, j, pickaxe: false) && IsTrunkBottom(i, j, type)) Credit("tree", byCompanion: false, new Point(i, j));
    }

    public static void PlayerPlaced(int i, int j, int type)
    {
        if (Main.gameMenu || WorldGen.gen || !IsTorch(type)) return;
        Tile placed = Main.tile[i, j];
        if (placed.HasTile && placed.TileType == type) Credit("torch", byCompanion: false, new Point(i, j));
    }

    // Torch tiles already paid for in this world, by either earner. A torch is work that can be undone and redone on the same
    // tile at will — placed, broken, placed again — so a torch credit per placement is experience for nothing, and the owner
    // ruled on 15 September 2026 that a tile's torch is paid once. Kept per world and saved with it, because a reload is
    // otherwise a way to be paid again. Ore and trees are not deduplicated: the owner has not ruled on them.
    private static readonly HashSet<Point> torchTilesPaid = new();

    public static int TorchTilesPaid => torchTilesPaid.Count;
    public static void ForgetPaidTorches() => torchTilesPaid.Clear();

    public static void SavePaidTorches(TagCompound tag)
    {
        tag["paidTorchX"] = torchTilesPaid.Select(p => p.X).ToArray();
        tag["paidTorchY"] = torchTilesPaid.Select(p => p.Y).ToArray();
    }

    public static void LoadPaidTorches(TagCompound tag)
    {
        torchTilesPaid.Clear();
        int[] x = tag.GetIntArray("paidTorchX"), y = tag.GetIntArray("paidTorchY");
        for (int k = 0; k < Math.Min(x.Length, y.Length); k++) torchTilesPaid.Add(new Point(x[k], y[k]));
    }

    private static void Credit(string source, bool byCompanion, Point tile)
    {
        if (source == "torch" && !torchTilesPaid.Add(tile)) return;
        if (CreditKillsAndFights.Ledger() is not { } ledger) return;
        var credit = ledger.CreditWork(byCompanion);
        CreditKillsAndFights.Record(ledger, credit, source, byCompanion ? Striker.Companion : Striker.Player,
            tile.ToWorldCoordinates(), $"tile={tile.X},{tile.Y}");
    }
}

/// <summary>The player's tile hooks, delegating to <see cref="CreditWork"/>.</summary>
public sealed class ObservePlayerWorkForExperience : GlobalTile
{
    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
        => CreditWork.PlayerKilledTile(i, j, type, fail, effectOnly);

    public override void PlaceInWorld(int i, int j, int type, Item item) => CreditWork.PlayerPlaced(i, j, type);
}

/// <summary>Saves the torch tiles already paid for with the world and clears them between worlds.</summary>
public sealed class SavePaidTorchTiles : ModSystem
{
    public override void ClearWorld() => CreditWork.ForgetPaidTorches();
    public override void SaveWorldData(TagCompound tag) => CreditWork.SavePaidTorches(tag);
    public override void LoadWorldData(TagCompound tag) => CreditWork.LoadPaidTorches(tag);
}
