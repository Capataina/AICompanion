#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Torch;

/// <summary>
/// Uses Smart Cursor's actual torch eligibility and spacing rules without taking over the
/// local player's cursor. SmartCursorLookup reads Main.MouseWorld and writes tileTarget;
/// its torch-only step instead accepts a supplied context. Bind once, fail explicitly if
/// that engine contract changes, and restore the helper's shared scratch list in finally.
///
/// <para>Three questions are asked of that one step, and the game's rule has one home: whether the step accepts a
/// single tile (a reach box one tile wide, with the mouse on it), which tile it would offer the player from his own
/// reach box (his box, the mouse at his centre), and a cheap necessary condition that lets a caller skip the step on
/// tiles it could never accept. The last is a filter and never a decision: it is proved a superset of the step by a
/// fixture, and every tile it passes is still put to the step before anything is placed.</para>
/// </summary>
public static class RecommendTorchPlacement
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type Context = typeof(SmartCursorHelper).GetNestedType("SmartCursorUsageInfo", BindingFlags.NonPublic)
        ?? throw new MissingMemberException("Terraria Smart Cursor torch context is unavailable");
    private static readonly MethodInfo TorchStep = typeof(SmartCursorHelper).GetMethod("Step_Torch", PrivateStatic)
        ?? throw new MissingMethodException("Terraria Smart Cursor torch selector is unavailable");
    private static readonly FieldInfo Targets = typeof(SmartCursorHelper).GetField("_targets", PrivateStatic)
        ?? throw new MissingFieldException("Terraria Smart Cursor scratch ownership changed");
    private static readonly FieldInfo PlayerField = Field("player"), ItemField = Field("item"), MouseField = Field("mouse");
    private static readonly FieldInfo StartX = Field("reachableStartX"), EndX = Field("reachableEndX"), StartY = Field("reachableStartY"), EndY = Field("reachableEndY");

    // One context, one argument array and one scratch list, reused: the brain is single-threaded, the lighting search asks
    // this for hundreds of tiles, and a fresh boxed context per tile was the loudest allocation in the tick.
    private static object? context;
    private static readonly object[] args = new object[3];
    private static readonly List<Tuple<int, int>> scratch = new();

    private static FieldInfo Field(string name) => Context.GetField(name)
        ?? throw new MissingFieldException(Context.FullName, name);

    public static bool Accepts(Point tile, Item torch, Player player)
    {
        // Step_Torch scans an eight-tile neighbourhood without clamping it itself.
        if (!WorldGen.InWorld(tile.X, tile.Y, 10)) return false;
        return Step(player, torch, tile.ToWorldCoordinates(), tile.X, tile.X, tile.Y, tile.Y) == tile;
    }

    /// <summary>
    /// A necessary condition for <see cref="Accepts"/> on an empty tile, from the same step's attachment test: a wall
    /// behind, or a tile on the left, the right or below. The step asks more of each neighbour (its slope, whether it is
    /// solid, whether it takes attachments) and also enforces the spacing between torches, so this passes tiles the
    /// step refuses and never refuses a tile the step accepts. A tile with something in it is outside its domain,
    /// because the companion never places into an occupied tile at all.
    /// </summary>
    public static bool MayAccept(Point tile)
    {
        if (!WorldGen.InWorld(tile.X, tile.Y, 10)) return false;
        return Main.tile[tile.X, tile.Y].WallType > 0
            || Main.tile[tile.X - 1, tile.Y].HasTile
            || Main.tile[tile.X + 1, tile.Y].HasTile
            || Main.tile[tile.X, tile.Y + 1].HasTile;
    }

    /// <summary>
    /// The tile the player's own smart cursor would offer for this torch, with the cursor at his centre: his reach box
    /// exactly as <c>SmartCursorHelper.SmartCursorLookup</c> builds it, and the torch step choosing the accepted tile
    /// nearest the mouse. His cursor is never read or written; null when his reach holds no tile the step accepts.
    /// </summary>
    public static Point? PlayerCursorTorch(Player player, Item torch)
    {
        int boost = torch.tileBoost;
        int startX = (int)(player.position.X / 16f) - Player.tileRangeX - boost + 1;
        int endX = (int)((player.position.X + player.width) / 16f) + Player.tileRangeX + boost - 1;
        int startY = (int)(player.position.Y / 16f) - Player.tileRangeY - boost + 1;
        int endY = (int)((player.position.Y + player.height) / 16f) + Player.tileRangeY + boost - 2;
        startX = Utils.Clamp(startX, 10, Main.maxTilesX - 10);
        endX = Utils.Clamp(endX, 10, Main.maxTilesX - 10);
        startY = Utils.Clamp(startY, 10, Main.maxTilesY - 10);
        endY = Utils.Clamp(endY, 10, Main.maxTilesY - 10);
        return Step(player, torch, player.Center, startX, endX, startY, endY);
    }

    private static Point? Step(Player player, Item torch, Vector2 mouse, int startX, int endX, int startY, int endY)
    {
        object info = context ??= Activator.CreateInstance(Context, nonPublic: true)!;
        PlayerField.SetValue(info, player); ItemField.SetValue(info, torch);
        MouseField.SetValue(info, mouse);
        StartX.SetValue(info, startX); EndX.SetValue(info, endX);
        StartY.SetValue(info, startY); EndY.SetValue(info, endY);
        args[0] = info; args[1] = -1; args[2] = -1;
        object? previousTargets = Targets.GetValue(null);
        try
        {
            scratch.Clear();
            Targets.SetValue(null, scratch);
            TorchStep.Invoke(null, args);
        }
        finally { Targets.SetValue(null, previousTargets); }
        int x = (int)args[1], y = (int)args[2];
        return x < 0 || y < 0 ? null : new Point(x, y);
    }
}
