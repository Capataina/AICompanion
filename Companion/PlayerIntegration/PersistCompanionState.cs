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

    /// <summary>The saved choices currently backing <see cref="CompanionPreferences.Current"/>.</summary>
    public CompanionPreferences Preferences { get; private set; } = new();

    /// <summary>
    /// Which diagnostic drawings this character had switched on, or null for a character that has
    /// never touched them — which keeps <see cref="BrainOverlay"/>'s own defaults rather than
    /// reading an absent tag as every layer off.
    /// </summary>
    private int? overlayLayers;

    public override void SaveData(TagCompound tag)
    {
        tag["hasCompanion"] = HasCompanion;
        if (HealthBarPosition is Vector2 p)
            tag["healthBar"] = p;
        tag["bag"] = Bag.Save();
        var preferences = new TagCompound();
        Preferences.Save(preferences);
        tag["preferences"] = preferences;
        // Read live rather than from the field: the layers are toggled in the overlay panel during
        // play, so the field is only ever the value this session started from.
        tag["overlayLayers"] = BrainOverlay.Layers;
    }

    public override void LoadData(TagCompound tag)
    {
        HasCompanion = tag.GetBool("hasCompanion");
        HealthBarPosition = tag.ContainsKey("healthBar") ? tag.Get<Vector2>("healthBar") : null;
        Bag = new CompanionInventory();
        if (tag.ContainsKey("bag"))
            Bag.Load(tag.GetCompound("bag"));
        try
        {
            Preferences = tag.ContainsKey("preferences")
                ? CompanionPreferences.Load(tag.GetCompound("preferences"))
                : new CompanionPreferences();
        }
        catch (System.Exception)
        {
            // A malformed optional settings compound must not prevent an older character from
            // loading; defaults preserve the behaviour those saves had before settings existed.
            Preferences = new CompanionPreferences();
        }
        overlayLayers = tag.ContainsKey("overlayLayers") ? tag.GetInt("overlayLayers") : null;
    }

    public override void OnEnterWorld()
    {
        // The static work-policy readers are evaluated by the brain after spawning. Point them
        // at this character before that happens so changing worlds cannot use another save's UI.
        CompanionPreferences.Current = Preferences;
        if (overlayLayers is int layers)
            BrainOverlay.Layers = layers;
        bool spawned = false;
        if (HasCompanion && CompanionNPC.Find() == null)
            spawned = CompanionNPC.Spawn(Player) < Main.maxNPCs;
        Mod.Logger.Info($"OnEnterWorld: hasCompanion={HasCompanion} spawned={spawned} bagItems={Bag.Count}");
    }

}
