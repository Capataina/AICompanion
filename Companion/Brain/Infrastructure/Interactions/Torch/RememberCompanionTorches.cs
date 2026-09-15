#nullable enable
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.Infrastructure.Observation;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;

/// <summary>
/// The world's memory of the companion's own torches: which tiles hold one it placed, and which spots the player cleared of
/// one. Its torches are free (the owner's ruling of 15 September 2026), so a free torch must never become an item — breaking
/// one drops nothing, whoever breaks it — and a spot the player cleared is his decision about that place, which the
/// companion does not reverse by lighting it again.
///
/// <para>A refusal covers the cleared spot and every tile within the game's own torch spacing of it, as though his removal
/// were a torch still standing there. The spot alone is not enough: the torch he removed was placed because the area was
/// dark, the area is still dark, and the next search would put the same torch one tile over. It lasts until the spot holds a
/// tile again — anything he places there is a new decision about the place — and it is saved with the world, because a
/// refusal forgotten at the next load puts back, the moment he returns, the torch he removed.</para>
/// </summary>
public static class CompanionTorches
{
    /// <summary>
    /// How far a torch keeps another away: <c>SmartCursorHelper.Step_Torch</c> refuses a tile with any torch within eight
    /// tiles each way (a 17 by 17 box, as decompiled). Mirrored rather than read, because the step keeps it as a literal; if
    /// the game's spacing changes, a refusal covers a different area than the torch the player removed would have.
    /// </summary>
    public const int SpacingTiles = 8;

    private static readonly HashSet<Point> placed = new(), refused = new();

    public static int Placed => placed.Count;
    public static int Refusals => refused.Count;

    public static void Clear()
    {
        placed.Clear();
        refused.Clear();
    }

    /// <summary>The companion's placer put a torch here.</summary>
    public static void NotePlaced(Point tile)
    {
        placed.Add(tile);
        refused.Remove(tile);
    }

    public static bool IsCompanionTorch(Point tile) => placed.Contains(tile);

    /// <summary>
    /// A tile at <paramref name="i"/>, <paramref name="j"/> of <paramref name="type"/> is being killed; whether it must drop
    /// nothing. A companion torch drops nothing whoever breaks it. When it is actually destroyed its memory goes, and unless
    /// the companion's own hit is what broke it — its pickaxe taking the block a torch stood on — the spot is refused.
    /// </summary>
    public static bool Breaking(int i, int j, int type, bool fail, bool effectOnly)
    {
        Point tile = new(i, j);
        if (effectOnly || !placed.Contains(tile)) return false;
        if (!(type >= 0 && type < TileID.Sets.Torch.Length && TileID.Sets.Torch[type]))
        {
            placed.Remove(tile);                                               // something else stands there now
            return false;
        }
        if (fail) return true;
        placed.Remove(tile);
        if (!TileDamageWatcher.CompanionIsHitting) refused.Add(tile);
        return true;
    }

    /// <summary>Something was placed at this tile by the player: his refusal of that spot, if it was one, is over.</summary>
    public static void TilePlaced(int i, int j) => refused.Remove(new Point(i, j));

    /// <summary>Whether a torch at <paramref name="tile"/> would stand within the game's spacing of a spot the player cleared.
    /// A refusal whose spot now holds a tile is over, and is dropped as it is found.</summary>
    public static bool Refuses(Point tile)
    {
        if (refused.Count == 0) return false;
        refused.RemoveWhere(spot => Main.tile[spot.X, spot.Y].HasTile);
        foreach (Point spot in refused)
            if (System.Math.Abs(spot.X - tile.X) <= SpacingTiles && System.Math.Abs(spot.Y - tile.Y) <= SpacingTiles) return true;
        return false;
    }

    public static void Save(TagCompound tag)
    {
        tag["companionTorchX"] = placed.Select(p => p.X).ToArray();
        tag["companionTorchY"] = placed.Select(p => p.Y).ToArray();
        tag["refusedTorchX"] = refused.Select(p => p.X).ToArray();
        tag["refusedTorchY"] = refused.Select(p => p.Y).ToArray();
    }

    public static void Load(TagCompound tag)
    {
        Clear();
        Read(tag, "companionTorchX", "companionTorchY", placed);
        Read(tag, "refusedTorchX", "refusedTorchY", refused);
    }

    private static void Read(TagCompound tag, string xs, string ys, HashSet<Point> into)
    {
        int[] x = tag.GetIntArray(xs), y = tag.GetIntArray(ys);
        for (int k = 0; k < System.Math.Min(x.Length, y.Length); k++) into.Add(new Point(x[k], y[k]));
    }
}

/// <summary>The tile hooks, delegating to <see cref="CompanionTorches"/>, which owns every decision.</summary>
public sealed class KeepCompanionTorchesFree : GlobalTile
{
    public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
        if (CompanionTorches.Breaking(i, j, type, fail, effectOnly)) noItem = true;
    }

    public override void PlaceInWorld(int i, int j, int type, Item item) => CompanionTorches.TilePlaced(i, j);
}

/// <summary>Saves the memory with the world and clears it between worlds.</summary>
public sealed class SaveCompanionTorches : ModSystem
{
    public override void ClearWorld() => CompanionTorches.Clear();
    public override void SaveWorldData(TagCompound tag) => CompanionTorches.Save(tag);
    public override void LoadWorldData(TagCompound tag) => CompanionTorches.Load(tag);
}
