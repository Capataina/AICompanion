#nullable enable
using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace AICompanion.Companion.DiagnosticsConfiguration;

/// <summary>Client-side switches for optional companion diagnostics; both default on for playtest evidence.</summary>
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
        if (!RecordTelemetry) Brain.BehaviourDiagnostics.BrainTelemetry.StopRecording();
        if (!EnableBrainInspector)
        {
            Brain.BehaviourDiagnostics.BrainOverlay.Enabled = false;
            Brain.BehaviourDiagnostics.BrainOverlay.ShowWorld = false;
            Brain.BehaviourDiagnostics.BrainInspectorSamples.Reset();
        }
    }
}
