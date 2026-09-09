#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.BehaviourDiagnostics;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.Inventory;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>Player events observed at their authoritative engine callbacks.</summary>
public partial class CompanionPlayer
{
    /// <summary>
    /// Terraria has calculated the final damage, cause and knockback here. The next telemetry row
    /// consumes that exact event; inferring it from a later life value would make each property a guess.
    /// </summary>
    public override void OnHurt(Player.HurtInfo info) => BrainTelemetry.RecordPlayerHurt(info);

}
