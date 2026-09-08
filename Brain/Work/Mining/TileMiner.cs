#nullable enable

using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using AICompanion.Brain.DecisionMatrix.Senses;

namespace AICompanion.Brain.Work.Mining;

/// <summary>
/// Mines a tile exactly the way the player's pickaxe does, by running the game's own
/// <see cref="Player.PickTile"/> on the companion's drawing-only player: the damage
/// formula with every per-type multiplier and minimum-power gate, a modded tile's
/// power check, the crack table, the break at full damage, and the fail-hit sound and
/// dust are all the game's, so if the player's pickaxe could not break a tile neither
/// can the companion's. The formula itself is private, so the "can this pick damage
/// that tile" question the ore finder asks is answered through a delegate bound to it
/// rather than a copy that drifts.
///
/// A tool, not a behaviour: the mine action decides which tile and when.
/// </summary>
public sealed class TileMiner
{
    private delegate int PickaxeDamage(int x, int y, int pickPower, int hitBufferIndex, Tile tile);

    private readonly Player body;
    private readonly PickaxeDamage damageOf;
    private int swingCooldown;

    /// <summary>The crack table the game's PickTile fills; the cracks renderer draws it.</summary>
    public HitTile HitTile => body.hitTile;

    public TileMiner(Player drawingOnlyPlayer)
    {
        body = drawingOnlyPlayer;
        MethodInfo method = typeof(Player).GetMethod("GetPickaxeDamage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(Player), "GetPickaxeDamage");
        damageOf = (PickaxeDamage)Delegate.CreateDelegate(typeof(PickaxeDamage), body, method);
    }

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
        if (!Ready || !WorldGen.InWorld(tile.X, tile.Y, 5) || !Main.tile[tile.X, tile.Y].HasTile)
            return false;
        swingCooldown = pickaxe.useTime;
        TileDamageWatcher.CompanionIsHitting = true;
        try
        {
            body.PickTile(tile.X, tile.Y, pickaxe.pick);
        }
        finally
        {
            TileDamageWatcher.CompanionIsHitting = false;
        }
        return true;
    }

    /// <summary>Whether this pickaxe can damage the tile at all, from the same gates the player's swing uses.</summary>
    public bool CanMine(Point tile, int pickPower)
    {
        if (!WorldGen.InWorld(tile.X, tile.Y, 5) || !Main.tile[tile.X, tile.Y].HasTile)
            return false;
        // The buffer index only matters for doors and beds, which move the hit; an ore never does.
        return damageOf(tile.X, tile.Y, pickPower, 0, Main.tile[tile.X, tile.Y]) > 0 && WorldGen.CanKillTile(tile.X, tile.Y);
    }
}
