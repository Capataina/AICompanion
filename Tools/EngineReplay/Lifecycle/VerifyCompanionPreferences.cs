#nullable enable

extern alias live;

using Terraria.ModLoader.IO;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>Portable checks for the scalar preference contract before gameplay reads it each tick.</summary>
internal static class VerifyCompanionPreferences
{
    public static int Run()
    {
        // Every row runs and files a sub-row; the fixture fails afterwards if any did, rather than at the first.
        return RunOneRow.Case(ScalarRoundTrip, "preferences")
            + RunOneRow.Case(MalformedValuesFallBack, "preferences")
            + RunOneRow.Case(DistancesAreFixed, "preferences");
    }

    private static void ScalarRoundTrip()
    {
        var original = new Preferences
        {
            Mining = WorkPolicy.Mimic,
            Chopping = WorkPolicy.Disabled,
            Combat = false,
            PotBreaking = false,
            TorchPlacement = false,
        };
        var tag = new TagCompound();
        original.Save(tag);
        using var stream = new System.IO.MemoryStream();
        TagIO.ToStream(tag, stream);
        stream.Position = 0;
        Preferences copy = Preferences.Load(TagIO.FromStream(stream));
        Require(copy.Mining == WorkPolicy.Mimic && copy.Chopping == WorkPolicy.Disabled, "work policies did not round-trip");
        Require(!copy.Combat && !copy.PotBreaking && !copy.TorchPlacement, "boolean preferences did not round-trip");
    }

    private static void MalformedValuesFallBack()
    {
        Preferences defaults = Preferences.Load(new TagCompound());
        Require(defaults.Mining == WorkPolicy.Opportunistic && defaults.Chopping == WorkPolicy.Opportunistic && defaults.Combat && defaults.PotBreaking && defaults.TorchPlacement, "absent legacy settings did not retain defaults");
        var invalid = new TagCompound { ["mining"] = 99, ["chopping"] = -1 };
        Preferences safe = Preferences.Load(invalid);
        Require(safe.Mining == WorkPolicy.Opportunistic && safe.Chopping == WorkPolicy.Opportunistic, "invalid enum scalars escaped their fallback");
    }

    /// <summary>
    /// The owner's ruling of 26 September 2026: new work within 100 tiles of the player, the flight home past 125, and no
    /// preference that moves either. A save written while the Close/Standard/Free setting existed still carries its
    /// "distanceMode" key, and loading one set to Close or Free must land on the same distances as a save without it.
    /// </summary>
    private static void DistancesAreFixed()
    {
        const float Tile = 16f;
        var fresh = new Preferences();
        Require(fresh.NewActivityRadius == 100 * Tile, $"new work reaches {fresh.NewActivityRadius / Tile} tiles, not 100");
        Require(fresh.RecoveryRadius == 125 * Tile, $"the flight home starts at {fresh.RecoveryRadius / Tile} tiles, not 125");
        Require(fresh.ActiveActivityRadius <= fresh.RecoveryRadius,
            $"a started job is kept to {fresh.ActiveActivityRadius / Tile} tiles, past the {fresh.RecoveryRadius / Tile} where the flight home takes the body");
        foreach (int oldMode in new[] { 0, 2 })
        {
            Preferences loaded = Preferences.Load(new TagCompound { ["distanceMode"] = oldMode });
            Require(loaded.NewActivityRadius == fresh.NewActivityRadius && loaded.RecoveryRadius == fresh.RecoveryRadius
                && loaded.ActiveActivityRadius == fresh.ActiveActivityRadius,
                $"a save carrying the retired distanceMode={oldMode} loaded different distances");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
