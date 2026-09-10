#nullable enable

using System;
using Terraria.ModLoader.IO;
using AICompanion.Companion.Brain.BehaviourSelection;
using AICompanion.Companion.Brain.Behaviours.Work;

namespace AICompanion.Companion.PlayerIntegration;

/// <summary>The spacing the player wants while the companion is doing ordinary work.</summary>
public enum CompanionDistanceMode
{
    Close,
    Standard,
    Free,
}

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
    public bool Hunting { get; set; } = true;
    public bool PotBreaking { get; set; } = true;
    public bool TorchPlacement { get; set; } = true;
    public CompanionDistanceMode DistanceMode { get; set; } = CompanionDistanceMode.Standard;

    private float RecoveryBase => Weights.FollowRecoveryDistance;
    public float NewActivityRadius => RecoveryBase * (DistanceMode == CompanionDistanceMode.Close ? .5f : DistanceFactor);
    public float RecoveryRadius => RecoveryBase * DistanceFactor;
    public float ActiveActivityRadius => RecoveryRadius * Weights.ActivityContinuationFactor;
    public float FollowComfortScale => DistanceMode switch
    {
        CompanionDistanceMode.Close => .75f,
        CompanionDistanceMode.Free => 1.5f,
        _ => 1f,
    };

    private float DistanceFactor => DistanceMode switch
    {
        CompanionDistanceMode.Close => .75f,
        CompanionDistanceMode.Free => 1.5f,
        _ => 1f,
    };

    public void Save(TagCompound tag)
    {
        tag["mining"] = (int)Mining;
        tag["chopping"] = (int)Chopping;
        // NBT has a byte payload, not a Boolean payload. Keeping saves in native scalar types
        // also makes this contract independent of loader serializer registration order.
        tag["hunting"] = (byte)(Hunting ? 1 : 0);
        tag["potBreaking"] = (byte)(PotBreaking ? 1 : 0);
        tag["torchPlacement"] = (byte)(TorchPlacement ? 1 : 0);
        tag["distanceMode"] = (int)DistanceMode;
    }

    public static CompanionPreferences Load(TagCompound tag)
    {
        var preferences = new CompanionPreferences();
        preferences.Mining = ReadEnum(tag, "mining", WorkPolicy.Opportunistic);
        preferences.Chopping = ReadEnum(tag, "chopping", WorkPolicy.Opportunistic);
        preferences.Hunting = ReadBool(tag, "hunting", true);
        preferences.PotBreaking = ReadBool(tag, "potBreaking", true);
        preferences.TorchPlacement = ReadBool(tag, "torchPlacement", true);
        preferences.DistanceMode = ReadEnum(tag, "distanceMode", CompanionDistanceMode.Standard);
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
