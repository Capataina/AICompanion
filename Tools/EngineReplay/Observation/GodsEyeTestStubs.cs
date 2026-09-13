#nullable enable

using Terraria.ModLoader;

namespace AICompanion
{
    // EngineReplay compiles the real event writer without the mod's UI and TSV system.
    public sealed class AICompanion : Mod { }
}

namespace AICompanion.Companion.Brain.BehaviourDiagnostics
{
    internal static class BrainTelemetry
    {
        internal static double ElapsedMilliseconds => 123.456;
        internal static void RecordPlayerHurt(Terraria.Player.HurtInfo info) { }
    }
}

namespace AICompanion.Companion.PlayerIntegration
{
    // Only the persistent/input half is excluded. The actual OnHurt observer is compiled.
    public partial class CompanionPlayer : ModPlayer { }
}
