#nullable enable

using Terraria.ModLoader;

namespace AICompanion;

/// <summary>
/// Mod entry point. tModLoader instantiates exactly one <see cref="Mod"/> subclass
/// per mod; everything with behaviour lives in its own ModNPC, ModCommand or
/// ModSystem type under Content/ and Commands/.
/// </summary>
public class AICompanion : Mod
{
    public override void Load()
    {
        Logger.Info("Multi... Player? loaded. Type /companion in chat to spawn a companion.");
    }

    // Every hook of ours logs its unload so that, when Build + Reload dies inside tModLoader's
    // assembly unload with no exception, the log shows whether our side finished first.
    public override void Unload()
    {
        Logger.Info("Mod.Unload: done");
    }
}
