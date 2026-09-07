#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace AICompanion.Content.Behaviours;

/// <summary>
/// Chops trees the way the player's axe does. The companion owns its own
/// <see cref="HitTile"/>, the game's per-hitter table of cracked tiles, and applies
/// the exact vanilla axe formula from Player.ItemCheck_UseMiningTools: damage is
/// axe power times 1.2 (times 3 on cactus), zero if the tile cannot be killed, the
/// tile breaks at 100 accumulated damage, and every non-lethal hit is a
/// KillTile(fail: true) so the game plays the chop sound and dust.
/// </summary>
public class TileChopper
{
    /// <summary>How many tiles the companion searches for a tree.</summary>
    public const int SearchRadiusTiles = 40;

    /// <summary>The cracked-tile table; drawn by <see cref="TileCracksRenderer"/>.</summary>
    public HitTile HitTile { get; } = new();

    public TreeFinder.ChoppableTree? Target { get; private set; }

    private int swingCooldown;
    private int lostTargetTicks;

    /// <summary>The axe the companion swings: the player's, or a copper axe if the player holds none.</summary>
    public static Item AxeFor(Player player)
        => player.HeldItem.axe > 0 ? player.HeldItem : ContentSamples.ItemsByType[ItemID.CopperAxe];

    /// <summary>True when the player is chopping and a tree the player is not hitting exists nearby.</summary>
    public bool WantsToChop(NPC npc, Player player)
    {
        Point? playersTree = TreeFinder.TreeUnderPlayerAxe(player);
        if (playersTree == null)
        {
            // Keep the current job a short while after the player pauses swinging, so a
            // slow player swing does not make the companion drop the tree every frame.
            if (Target != null && lostTargetTicks++ < 120 && TreeStillStands(Target.Value))
                return true;
            Target = null;
            return false;
        }

        lostTargetTicks = 0;
        if (Target != null && TreeStillStands(Target.Value) && Target.Value.Bottom != playersTree.Value)
            return true;

        Target = TreeFinder.FindNearest(npc.Center, SearchRadiusTiles, playersTree);
        return Target != null;
    }

    /// <summary>
    /// One tick of chopping. Returns true when the companion is in position and swinging,
    /// false when it still needs to walk to <see cref="TreeFinder.ChoppableTree.StandPosition"/>.
    /// Sets <paramref name="animationStarted"/> on the tick a swing begins so the body can animate.
    /// </summary>
    public bool Update(NPC npc, Player player, out bool animationStarted)
    {
        animationStarted = false;
        if (Target is not TreeFinder.ChoppableTree tree)
            return false;

        if (System.MathF.Abs(npc.Center.X - tree.StandPosition.X) > 20f)
            return false;

        npc.direction = npc.spriteDirection = tree.FacingDirection;
        if (swingCooldown-- > 0)
            return true;

        Item axe = AxeFor(player);
        swingCooldown = axe.useTime;
        animationStarted = true;
        Hit(tree.Bottom.X, tree.Bottom.Y, axe.axe);
        if (!TreeStillStands(tree))
            Target = null;
        return true;
    }

    private void Hit(int x, int y, int axePower)
    {
        Tile tile = Main.tile[x, y];
        if (!tile.HasTile)
            return;

        int id = HitTile.HitObject(x, y, 1);
        int damage = (int)(axePower * 1.2f);
        if (tile.TileType == TileID.Cactus)
            damage *= 3;
        if (!WorldGen.CanKillTile(x, y))
            damage = 0;

        if (HitTile.AddDamage(id, damage) >= 100)
        {
            HitTile.Clear(id);
            WorldGen.KillTile(x, y);
        }
        else
        {
            WorldGen.KillTile(x, y, fail: true);
        }
        if (damage != 0)
            HitTile.Prune();
    }

    private static bool TreeStillStands(TreeFinder.ChoppableTree tree)
    {
        Tile tile = Main.tile[tree.Bottom.X, tree.Bottom.Y];
        return tile.HasTile && Main.tileAxe[tile.TileType];
    }
}
