extern alias live;
using Terraria.ModLoader.IO;
using PersistWeaponKnowledge = live::AICompanion.Companion.Brain.Infrastructure.WeaponKnowledge.PersistWeaponKnowledge;
using ReplayInputs = live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReplayInputs;

/// <summary>
/// The weapon knowledge a player save carries, installed into this process so a played capture can be reproduced from
/// the belief it began with. The companion's player data writes it as the string `weaponKnowledge` inside the mod's
/// block of the `.tplr`; the save's layout around that block is tModLoader's, so the tag is found by searching the
/// whole compound for that key rather than by naming a path tModLoader is free to change.
///
/// Which save is the right one is the caller's problem and the digest's answer: Terraria keeps the previous save as
/// `.tplr.bak`, so the save written when a play ended holds what that play taught, and the `.bak` holds what it began
/// from — provided nothing else was played in between. The reproduction's first-frame digest check names a mismatch.
/// </summary>
internal static class LoadTheSavedKnowledge
{
    public static string Import(string path)
    {
        TagCompound root = TagIO.FromFile(path);
        string? json = Find(root);
        if (json == null)
            throw new System.IO.InvalidDataException($"{path} holds no weaponKnowledge string anywhere in its tag tree, so it is not a save this mod wrote");
        (int installed, int skipped) = PersistWeaponKnowledge.Import(json);
        return $"{installed} entries installed and {skipped} skipped from {System.IO.Path.GetFileName(path)}; this process now holds {ReplayInputs.DescribeKnowledge()}";
    }

    private static string? Find(object? node)
    {
        switch (node)
        {
            case TagCompound compound:
                foreach (var (key, value) in compound)
                {
                    if (key == "weaponKnowledge" && value is string text) return text;
                    if (Find(value) is { } found) return found;
                }
                return null;
            case System.Collections.IEnumerable list when node is not string:
                foreach (object? item in list)
                    if (Find(item) is { } found) return found;
                return null;
            default:
                return null;
        }
    }
}
