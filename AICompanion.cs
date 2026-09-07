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
}
