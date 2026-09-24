#nullable enable

extern alias live;

using Terraria.ModLoader.IO;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using Distance = live::AICompanion.Companion.PlayerIntegration.CompanionDistanceMode;
using WorkPolicy = live::AICompanion.Companion.Brain.Activities.WorkPolicy;

/// <summary>Portable checks for the scalar preference contract before gameplay reads it each tick.</summary>
internal static class VerifyCompanionPreferences
{
    public static int Run()
    {
        // Every row runs and files a sub-row; the fixture fails afterwards if any did, rather than at the first.
        return RunOneRow.Case(ScalarRoundTrip, "preferences")
            + RunOneRow.Case(MalformedValuesFallBack, "preferences")
            + RunOneRow.Case(DistanceOrdering, "preferences");
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
            DistanceMode = Distance.Free,
        };
        var tag = new TagCompound();
        original.Save(tag);
        using var stream = new System.IO.MemoryStream();
        TagIO.ToStream(tag, stream);
        stream.Position = 0;
        Preferences copy = Preferences.Load(TagIO.FromStream(stream));
        Require(copy.Mining == WorkPolicy.Mimic && copy.Chopping == WorkPolicy.Disabled, "work policies did not round-trip");
        Require(!copy.Combat && !copy.PotBreaking && !copy.TorchPlacement && copy.DistanceMode == Distance.Free, "boolean or distance preferences did not round-trip");
    }

    private static void MalformedValuesFallBack()
    {
        Preferences defaults = Preferences.Load(new TagCompound());
        Require(defaults.Mining == WorkPolicy.Opportunistic && defaults.Chopping == WorkPolicy.Opportunistic && defaults.Combat && defaults.PotBreaking && defaults.TorchPlacement && defaults.DistanceMode == Distance.Standard, "absent legacy settings did not retain defaults");
        var invalid = new TagCompound { ["mining"] = 99, ["chopping"] = -1, ["distanceMode"] = 42 };
        Preferences safe = Preferences.Load(invalid);
        Require(safe.Mining == WorkPolicy.Opportunistic && safe.Chopping == WorkPolicy.Opportunistic && safe.DistanceMode == Distance.Standard, "invalid enum scalars escaped their fallback");
    }

    private static void DistanceOrdering()
    {
        var close = new Preferences { DistanceMode = Distance.Close };
        var standard = new Preferences { DistanceMode = Distance.Standard };
        var free = new Preferences { DistanceMode = Distance.Free };
        Require(close.NewActivityRadius < standard.NewActivityRadius && standard.NewActivityRadius < free.NewActivityRadius, "new activity radius does not increase close to free");
        Require(close.RecoveryRadius < standard.RecoveryRadius && standard.RecoveryRadius < free.RecoveryRadius, "recovery radius does not increase close to free");
        Require(close.ActiveActivityRadius < standard.ActiveActivityRadius && standard.ActiveActivityRadius < free.ActiveActivityRadius, "active activity radius does not increase close to free");
        Require(close.FollowComfortScale < standard.FollowComfortScale && standard.FollowComfortScale < free.FollowComfortScale, "follow comfort scale does not increase close to free");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
