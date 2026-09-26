#nullable enable

using System;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.Infrastructure.Selection;
using AICompanion.Companion.Brain.Activities;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>
/// Per-character choices for optional companion work.  The static entry point deliberately
/// follows the active <see cref="CompanionPlayer"/> instance: gameplay has existing static
/// policy readers, while saves must never leak a previous character's choices into this one.
/// </summary>
public sealed class CompanionPreferences
{
    public static CompanionPreferences Current { get; set; } = new();

    public WorkPolicy Mining { get; set; } = WorkPolicy.Opportunistic;
    public WorkPolicy Chopping { get; set; } = WorkPolicy.Opportunistic;
    /// <summary>Whether the combat stance may run. Saved as "combat"; an older save that only
    /// wrote "hunting" reads from that key, so turning hunting off stays off after the merge.</summary>
    public bool Combat { get; set; } = true;
    public bool PotBreaking { get; set; } = true;
    public bool TorchPlacement { get; set; } = true;
    /// <summary>Which ores this character has held, which carry a mark, and whether a mark means skip or only.</summary>
    public CompanionMiningList MiningList { get; set; } = new();

    // The distances are fixed rather than a preference, by the owner's ruling of 26 September 2026: the Close, Standard
    // and Free following distance went, and an older save's "distanceMode" key is ignored rather than migrated. They stay
    // properties here because every reader already asks this object, and a later preference would come back through it.
    public float NewActivityRadius => Weights.FollowWorkRadius;
    public float RecoveryRadius => Weights.FollowRecoveryDistance;
    public float ActiveActivityRadius => Weights.FollowWorkRadius * Weights.ActivityContinuationFactor;
    /// <summary>
    /// How far, in tiles, a work census must scan around the player's heading for its answer to mean
    /// anything: exactly the radius <c>CompanionAction.AllowsTarget</c> admits a *new* target within,
    /// rounded out. It is derived from <see cref="NewActivityRadius"/> rather than written as its own
    /// number because the two must agree and, written separately, they did not — the ore census scanned
    /// 45 tiles and the trunk census 40 against an allowance of 62.5, so a vein the allowance rule would
    /// have admitted was invisible to the brain that had to admit it, and the smaller number won with
    /// nothing anywhere saying it had. The family chooser hid that for as long as it was the brain,
    /// because it ran a second search centred on the companion's own body; the course runs one, centred
    /// here, so the census window is the whole of what the companion can see.
    /// </summary>
    public int WorkCensusRadiusTiles => (int)MathF.Ceiling(NewActivityRadius / 16f);

    public void Save(TagCompound tag)
    {
        tag["mining"] = (int)Mining;
        tag["chopping"] = (int)Chopping;
        // NBT has a byte payload, not a Boolean payload. Keeping saves in native scalar types
        // also makes this contract independent of loader serializer registration order.
        tag["combat"] = (byte)(Combat ? 1 : 0);
        tag["potBreaking"] = (byte)(PotBreaking ? 1 : 0);
        tag["torchPlacement"] = (byte)(TorchPlacement ? 1 : 0);
        tag["miningList"] = MiningList.Save();
    }

    public static CompanionPreferences Load(TagCompound tag)
    {
        var preferences = new CompanionPreferences();
        preferences.Mining = ReadEnum(tag, "mining", WorkPolicy.Opportunistic);
        preferences.Chopping = ReadEnum(tag, "chopping", WorkPolicy.Opportunistic);
        preferences.Combat = tag.ContainsKey("combat") ? ReadBool(tag, "combat", true) : ReadBool(tag, "hunting", true);
        preferences.PotBreaking = ReadBool(tag, "potBreaking", true);
        preferences.TorchPlacement = ReadBool(tag, "torchPlacement", true);
        preferences.MiningList = tag.ContainsKey("miningList") ? CompanionMiningList.Load(tag.GetCompound("miningList")) : new CompanionMiningList();
        return preferences;
    }

    private static bool ReadBool(TagCompound tag, string name, bool fallback)
    {
        try { return tag.ContainsKey(name) ? tag.GetByte(name) switch { 0 => false, 1 => true, _ => fallback } : fallback; }
        catch (Exception) { return fallback; }
    }

    private static T ReadEnum<T>(TagCompound tag, string name, T fallback) where T : struct, Enum
    {
        try
        {
            int value = tag.ContainsKey(name) ? tag.GetInt(name) : Convert.ToInt32(fallback);
            return Enum.IsDefined(typeof(T), value) ? (T)Enum.ToObject(typeof(T), value) : fallback;
        }
        catch (Exception) { return fallback; }
    }
}
