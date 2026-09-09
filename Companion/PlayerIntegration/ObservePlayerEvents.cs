#nullable enable

using Terraria;
using AICompanion.Companion.Brain.BehaviourDiagnostics;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>Player events observed at their authoritative engine callbacks.</summary>
public partial class CompanionPlayer
{
    /// <summary>
    /// Terraria has calculated the final damage, cause and knockback here. The next telemetry row
    /// consumes that exact event; inferring it from a later life value would make each property a guess.
    /// </summary>
    public override void OnHurt(Player.HurtInfo info)
    {
        BrainTelemetry.RecordPlayerHurt(info);
        GodsEyeEvents.RecordPlayerDamage(Player, info);
    }

}
