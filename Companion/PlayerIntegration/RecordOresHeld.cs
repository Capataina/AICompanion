#nullable enable

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>Adds each ore the player holds to this character's mining list.</summary>
public partial class CompanionPlayer
{
    /// <summary>
    /// A scan of the inventory after the tick's item movement, rather than a pickup hook, because an ore
    /// reaches the inventory by pickup, crafting, a chest, a shop and the cursor alike, and one scan of a
    /// fixed fifty-nine slots sees every route. It never removes anything: selling the last ore leaves it known.
    /// </summary>
    public override void PostUpdate() => Preferences.MiningList.RecordHeld(Player);
}
