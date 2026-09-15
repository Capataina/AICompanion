using System.Reflection;
using Terraria.ModLoader;

/// <summary>
/// Turns on one of <see cref="PlayerLoader"/>'s hook lists for the fixture's registered mod player, so the game's own calls
/// into a <see cref="ModPlayer"/> hook (<c>PlayerLoader.SetControls</c> from <c>Player.Update</c>,
/// <c>PlayerLoader.ShiftClickSlot</c> from <c>ItemSlot.OverrideLeftClick</c>) reach the mod's override headless. The loader
/// fills these lists with <c>HookList.Update</c> when mods load, which this shell never does, so without it every such call
/// enumerates nothing and a row driving the game's path would pass without the mod's code ever running.
/// </summary>
internal static class EnableModPlayerHooks
{
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    /// <param name="hookField">The loader's private field, such as <c>HookSetControls</c>.</param>
    /// <returns>A handle that empties the list again when disposed, so a later fixture sees the loader as it found it.</returns>
    public static IDisposable For(string hookField, ModPlayer player)
    {
        object hook = typeof(PlayerLoader).GetField(hookField, StaticPrivate)?.GetValue(null)
            ?? throw new InvalidOperationException($"PlayerLoader has no hook list named {hookField}; the loader changed, so re-read it");
        // HookList.Update validates that each instance's index is its place in the list, which the loader assigns on load.
        typeof(ModPlayer).GetProperty("Index")!.SetValue(player, (ushort)0);
        Update(hook, new List<ModPlayer> { player });
        return new Emptying(() => Update(hook, new List<ModPlayer>()));
    }

    private static void Update(object hook, IReadOnlyList<ModPlayer> instances)
        => hook.GetType().GetMethod("Update", new[] { typeof(IReadOnlyList<ModPlayer>) })!.Invoke(hook, new object[] { instances });

    private sealed class Emptying(Action empty) : IDisposable
    {
        public void Dispose() => empty();
    }
}
