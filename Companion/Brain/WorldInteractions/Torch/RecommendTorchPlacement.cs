#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;

namespace AICompanion.Companion.Brain.WorldInteractions.Torch;

/// <summary>
/// Uses Smart Cursor's actual torch eligibility and spacing rules without taking over the
/// local player's cursor. SmartCursorLookup reads Main.MouseWorld and writes tileTarget;
/// its torch-only step instead accepts a supplied context. Bind once, fail explicitly if
/// that engine contract changes, and restore the helper's shared scratch list in finally.
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

    private static FieldInfo Field(string name) => Context.GetField(name)
        ?? throw new MissingFieldException(Context.FullName, name);

    public static bool Accepts(Point tile, Item torch, Player player)
    {
        // Step_Torch scans an eight-tile neighbourhood without clamping it itself.
        if (!WorldGen.InWorld(tile.X, tile.Y, 10)) return false;
        object context = Activator.CreateInstance(Context, nonPublic: true)!;
        PlayerField.SetValue(context, player); ItemField.SetValue(context, torch);
        MouseField.SetValue(context, tile.ToWorldCoordinates());
        StartX.SetValue(context, tile.X); EndX.SetValue(context, tile.X);
        StartY.SetValue(context, tile.Y); EndY.SetValue(context, tile.Y);
        object? previousTargets = Targets.GetValue(null);
        object[] args = { context, -1, -1 };
        try
        {
            Targets.SetValue(null, new List<Tuple<int, int>>());
            TorchStep.Invoke(null, args);
            return (int)args[1] == tile.X && (int)args[2] == tile.Y;
        }
        finally { Targets.SetValue(null, previousTargets); }
    }
}
