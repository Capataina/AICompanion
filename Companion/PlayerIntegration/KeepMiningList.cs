#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>What a mark on an ore means: the marked ores are left, or only the marked ores are mined.</summary>
public enum MiningListMode
{
    SkipMarked,
    OnlyMarked,
}

/// <summary>
/// The per-character mining list: which ores this character has held, which of them carry a mark, and
/// what a mark means. It is the only allow/deny list the companion has, and it is ores because the
/// card's other behaviours are Off/Mimic/Auto or Off/On.
///
/// <para>An ore is known once the player has held one, recorded when it enters his inventory and never
/// forgotten when he sells or drops the last of it, so the list neither spoils what a world holds nor
/// empties as he trades. Known is only what the card shows; whether the companion mines an ore is
/// <see cref="Allows"/>, which reads the mark and the mode alone, so an ore the player has never held is
/// mined under Skip marked and left under Only marked, exactly like any other unmarked ore.</para>
///
/// <para>Ores are the tiles in <see cref="TileID.Sets.Ore"/>, the same set the miner's own classification
/// reads, so an item counts when it places one of them and the list and the miner cannot disagree about
/// what an ore is. Types are saved by name rather than by number, because a modded tile's number is
/// assigned at load and changes when the mod list does; a name whose mod is not loaded is kept and written
/// back, so unloading a mod does not erase the player's marks on its ores.</para>
/// </summary>
public sealed class CompanionMiningList
{
    private readonly List<int> known = new();
    private readonly HashSet<int> marked = new();
    private readonly List<string> unloadedKnown = new();
    private readonly List<string> unloadedMarked = new();
    private MiningListMode mode;

    /// <summary>Ore tile types in the order this character first held them.</summary>
    public IReadOnlyList<int> Known => known;

    public MiningListMode Mode
    {
        get => mode;
        set { if (mode != value) { mode = value; Revision++; } }
    }

    /// <summary>Moves on every change to a mark or the mode, so a reader holding an answer can tell it is stale.</summary>
    public int Revision { get; private set; }

    public bool IsMarked(int tileType) => marked.Contains(tileType);

    public void SetMarked(int tileType, bool value)
    {
        if (value ? marked.Add(tileType) : marked.Remove(tileType)) Revision++;
    }

    /// <summary>Whether the companion may take this ore as work.</summary>
    public bool Allows(int tileType) => mode == MiningListMode.SkipMarked ? !marked.Contains(tileType) : marked.Contains(tileType);

    /// <summary>The ore tile an item places, or null when it places no ore.</summary>
    public static int? OreTileOf(Item item)
        => !item.IsAir && item.createTile >= 0 && item.createTile < TileID.Sets.Ore.Length && TileID.Sets.Ore[item.createTile]
            ? item.createTile : null;

    /// <summary>Record every ore in the player's inventory, cursor slot included; true when one was new.</summary>
    public bool RecordHeld(Player player)
    {
        bool added = false;
        foreach (Item item in player.inventory)
            if (OreTileOf(item) is int tile && !known.Contains(tile))
            {
                known.Add(tile);
                added = true;
            }
        if (OreTileOf(Main.mouseItem) is int held && !known.Contains(held))
        {
            known.Add(held);
            added = true;
        }
        return added;
    }

    public TagCompound Save()
    {
        var knownNames = new List<string>(unloadedKnown);
        foreach (int tile in known)
            if (NameOf(tile) is string name) knownNames.Add(name);
        var markedNames = new List<string>(unloadedMarked);
        foreach (int tile in marked)
            if (NameOf(tile) is string name) markedNames.Add(name);
        return new TagCompound
        {
            ["known"] = knownNames,
            ["marked"] = markedNames,
            ["mode"] = (int)mode,
        };
    }

    public static CompanionMiningList Load(TagCompound tag)
    {
        var list = new CompanionMiningList();
        try
        {
            foreach (string name in tag.GetList<string>("known"))
            {
                if (TypeOf(name) is int tile) { if (!list.known.Contains(tile)) list.known.Add(tile); }
                else list.unloadedKnown.Add(name);
            }
            foreach (string name in tag.GetList<string>("marked"))
            {
                if (TypeOf(name) is int tile) list.marked.Add(tile);
                else list.unloadedMarked.Add(name);
            }
            int saved = tag.ContainsKey("mode") ? tag.GetInt("mode") : 0;
            list.mode = Enum.IsDefined(typeof(MiningListMode), saved) ? (MiningListMode)saved : MiningListMode.SkipMarked;
        }
        catch (Exception)
        {
            // A malformed list must not stop a character loading; an empty list mines every ore, which is
            // what the companion did before the list existed.
            return new CompanionMiningList();
        }
        return list;
    }

    /// <summary>A vanilla tile by its <see cref="TileID"/> field name, a modded one by its mod-qualified name.</summary>
    private static string? NameOf(int tile)
    {
        if (tile < TileID.Count)
            return TileID.Search.TryGetName(tile, out string name) ? name : null;
        return TileLoader.GetTile(tile)?.FullName;
    }

    private static int? TypeOf(string name)
    {
        if (TileID.Search.TryGetId(name, out int id)) return id;
        try { return ModContent.TryFind(name, out ModTile tile) ? tile.Type : null; }
        catch (Exception) { return null; }
    }
}
