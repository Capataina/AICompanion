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

/// <summary>
/// Per-character state that has to outlive a session: whether this character has
/// summoned a companion (so it spawns on every world enter without the command),
/// where the health bar was dragged to, and the companion's bag. Also the place
/// player input about the companion is read: the right-click that opens the bag and
/// the overlay key. Singleplayer only: the one instance that matters is Main.LocalPlayer's.
/// </summary>
public partial class CompanionPlayer : ModPlayer
{
    public bool HasCompanion;

    /// <summary>Health bar position in screen pixels, or null for the default top-centre.</summary>
    public Vector2? HealthBarPosition;

    public CompanionInventory Bag { get; private set; } = new();

    public override void SaveData(TagCompound tag)
    {
        tag["hasCompanion"] = HasCompanion;
        if (HealthBarPosition is Vector2 p)
            tag["healthBar"] = p;
        tag["bag"] = Bag.Save();
    }

    public override void LoadData(TagCompound tag)
    {
        HasCompanion = tag.GetBool("hasCompanion");
        HealthBarPosition = tag.ContainsKey("healthBar") ? tag.Get<Vector2>("healthBar") : null;
        Bag = new CompanionInventory();
        if (tag.ContainsKey("bag"))
            Bag.Load(tag.GetCompound("bag"));
    }

    public override void OnEnterWorld()
    {
        bool spawned = false;
        if (HasCompanion && CompanionNPC.Find() == null)
            spawned = CompanionNPC.Spawn(Player) < Main.maxNPCs;
        Mod.Logger.Info($"OnEnterWorld: hasCompanion={HasCompanion} spawned={spawned} bagItems={Bag.Count}");
    }

}
