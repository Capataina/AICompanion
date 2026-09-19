#nullable enable

extern alias live;

using Terraria.ModLoader;

namespace AICompanion
{
    // EngineReplay compiles the real event writer without the mod's UI and TSV system.
    public sealed class AICompanion : Mod { }
}

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics
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

namespace AICompanion.Companion.Brain.Infrastructure.Interactions.Firing
{
    // The isolated observer links the real shot ledger instead of compiling a second
    // weapon subsystem. A fixture registering a live shot therefore observes its identity.
    internal static class TrackLandedHits
    {
        public static bool IsCompanionShot(int slot)
            => live::AICompanion.Companion.Brain.Infrastructure.Interactions.Firing.TrackLandedHits.IsCompanionShot(slot);
    }
}
