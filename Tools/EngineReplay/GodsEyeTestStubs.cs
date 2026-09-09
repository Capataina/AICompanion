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
    }
}
