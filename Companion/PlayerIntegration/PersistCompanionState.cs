#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.Infrastructure.Diagnostics;
using AICompanion.Companion.CharacterBody;
using AICompanion.Companion.DiagnosticsConfiguration;
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

    /// <summary>The four gear slots: two weapons, a pickaxe and an axe, handed over by this character.</summary>
    public CompanionGear Gear { get; private set; } = new();

    /// <summary>The saved choices currently backing <see cref="CompanionPreferences.Current"/>.</summary>
    public CompanionPreferences Preferences { get; private set; } = new();

    /// <summary>The companion's level and the bar toward the next, earned from kills and work by the companion and by this
    /// character; the notch and the card draw it.</summary>
    public Progression.CompanionExperience Experience { get; private set; } = new();

    public override void SaveData(TagCompound tag)
    {
        tag["hasCompanion"] = HasCompanion;
        if (HealthBarPosition is Vector2 p)
            tag["healthBar"] = p;
        tag["bag"] = Bag.Save();
        tag["gear"] = Gear.Save();
        tag["experience"] = Experience.Save();
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
        Gear = new CompanionGear();
        if (tag.ContainsKey("gear"))
            Gear.Load(tag.GetCompound("gear"));
        Experience = tag.ContainsKey("experience")
            ? Progression.CompanionExperience.Load(tag.GetCompound("experience"))
            : new Progression.CompanionExperience();
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

    }

    public override void OnEnterWorld()
    {
        // The static work-policy readers are evaluated by the brain after spawning. Point them
        // at this character before that happens so changing worlds cannot use another save's UI.
        CompanionPreferences.Current = Preferences;
        CompanionDiagnosticsConfig.Current.EnableBrainInspector = true;
        CompanionDiagnosticsConfig.Current.RecordTelemetry = true;
        BrainOverlay.Layers = BrainOverlay.AllLayers;
        bool spawned = false;
        if (HasCompanion && CompanionNPC.Find() == null)
            spawned = CompanionNPC.Spawn(Player) < Main.maxNPCs;
        Mod.Logger.Info($"OnEnterWorld: hasCompanion={HasCompanion} spawned={spawned} bagItems={Bag.Count}");
    }

}
