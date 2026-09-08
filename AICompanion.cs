#nullable enable

using Terraria.ModLoader;
using AICompanion.Brain.DecisionMatrix.Navigation;

namespace AICompanion;

/// <summary>
/// Mod entry point. tModLoader instantiates exactly one <see cref="Mod"/> subclass
/// per mod; everything with behaviour lives in its own ModNPC, ModCommand or
/// ModSystem type. The one wiring done here: the navigation core reads tiles through
/// an interface, and the game is the implementation the mod plugs in.
/// </summary>
public class AICompanion : Mod
{
    public override void Load()
    {
        NavGrid.World = new GameTileWorld();
        Logger.Info("Multi... Player? loaded. Type /companion in chat to spawn a companion.");
    }

    // Every hook of ours logs its unload so that, when Build + Reload dies inside tModLoader's
    // assembly unload with no exception, the log shows whether our side finished first.
    public override void Unload()
    {
        Logger.Info("Mod.Unload: done");
    }
}
