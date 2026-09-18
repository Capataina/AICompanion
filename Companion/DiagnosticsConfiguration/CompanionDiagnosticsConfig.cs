#nullable enable
using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace AICompanion.Companion.DiagnosticsConfiguration;

/// <summary>Client-side switches for optional companion diagnostics. Telemetry and the inspector
/// default on so a playtest writes a capture and the drawings are there to watch.</summary>
public sealed class CompanionDiagnosticsConfig : ModConfig
{
    private static readonly CompanionDiagnosticsConfig defaults = new();
    public static CompanionDiagnosticsConfig Current => ModContent.GetInstance<CompanionDiagnosticsConfig>() ?? defaults;
    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(true)]
    public bool EnableBrainInspector { get; set; } = true;

    [DefaultValue(true)]
    public bool RecordTelemetry { get; set; } = true;

    public override void OnChanged()
    {
        if (!RecordTelemetry) Brain.Infrastructure.Diagnostics.BrainTelemetry.StopRecording();
        if (!EnableBrainInspector)
        {
            Brain.Infrastructure.Diagnostics.BrainOverlay.Enabled = false;
            Brain.Infrastructure.Diagnostics.BrainOverlay.ShowWorld = false;
            Brain.Infrastructure.Diagnostics.BrainInspectorSamples.Reset();
        }
    }
}
