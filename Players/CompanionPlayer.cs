#nullable enable

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Content;

namespace AICompanion.Players;

/// <summary>
/// Per-character state that has to outlive a session: whether this character has
/// summoned a companion (so it spawns on every world enter without the command),
/// and where the player dragged the companion health bar to.
/// Singleplayer only: there is exactly one of these that matters, on Main.LocalPlayer.
/// </summary>
public class CompanionPlayer : ModPlayer
{
    public bool HasCompanion;

    /// <summary>Health bar position in UI pixels, or null for the default top-centre.</summary>
    public Vector2? HealthBarPosition;

    public override void SaveData(TagCompound tag)
    {
        tag["hasCompanion"] = HasCompanion;
        if (HealthBarPosition is Vector2 p)
            tag["healthBar"] = p;
    }

    public override void LoadData(TagCompound tag)
    {
        HasCompanion = tag.GetBool("hasCompanion");
        HealthBarPosition = tag.ContainsKey("healthBar") ? tag.Get<Vector2>("healthBar") : null;
    }

    public override void OnEnterWorld()
    {
        if (HasCompanion && Companion.Find() == null)
            Companion.Spawn(Player);
    }
}
