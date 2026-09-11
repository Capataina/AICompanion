#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.ProfileCard;

/// <summary>Authored coordinates and directed paths. Geometry never depends on a layout solver.</summary>
public static class DefineMasteryGraph
{
    public enum NodeKind { Stat, Ability, Filler }
    public readonly record struct Node(string Name, string Description, Vector2 Position, int Branch, NodeKind Kind, int MaxTier, bool Weapon = false);
    public readonly record struct Edge(int From, int To);
    public static readonly Vector2[] WeaponPositions = { new(-140,25), new(-35,-40), new(70,35), new(145,-30) };
    public static readonly string[] WeaponUpgrades = { "Damage", "Attack speed", "Piercing", "Weapon mechanic" };
    public static readonly Edge[] WeaponEdges = { new(-1,0), new(0,1), new(1,2), new(2,3) };
    public static readonly string[] Branches = { "Tools", "Ranged", "Movement", "Magic", "Survival", "Support", "Gathering", "Melee" };
    public static readonly Color[] Colors = { new(233, 180, 103), new(138, 205, 109), new(104, 213, 220), new(182, 144, 240), new(237, 132, 146), new(231, 217, 137), new(109, 201, 178), new(236, 151, 99) };
    public static readonly Vector2[] Labels = { new(-210,-291), new(295,-249), new(624,-124), new(592,198), new(81,258), new(-422,218), new(-806,88), new(-707,-180) };

    // Six notable nodes followed by four small path ranks in each region. The shapes
    // deliberately fork, turn back inward and rejoin; rows are not radial distances.
    private static readonly Vector2[][] Positions = {
        new[] { new Vector2(-38, -40), new Vector2(-136, -100), new Vector2(-222, -162), new Vector2(-19, -155), new Vector2(-104, -213), new Vector2(-246, -216), new Vector2(-98, -64), new Vector2(-210, -122), new Vector2(-64, -121), new Vector2(-170, -190) },
        new[] { new Vector2(64, -36), new Vector2(205, -86), new Vector2(194, -178), new Vector2(368, -107), new Vector2(326, -196), new Vector2(456, -178), new Vector2(134, -52), new Vector2(171, -133), new Vector2(294, -84), new Vector2(256, -193) },
        new[] { new Vector2(109, -3), new Vector2(314, -22), new Vector2(422, -71), new Vector2(483, 26), new Vector2(573, -38), new Vector2(680, -74), new Vector2(208, -7), new Vector2(355, -52), new Vector2(389, 8), new Vector2(496, -56) },
        new[] { new Vector2(82, 38), new Vector2(277, 71), new Vector2(482, 66), new Vector2(325, 150), new Vector2(491, 178), new Vector2(638, 138), new Vector2(176, 50), new Vector2(384, 53), new Vector2(259, 115), new Vector2(494, 117) },
        new[] { new Vector2(2, 52), new Vector2(83, 137), new Vector2(205, 184), new Vector2(-48, 164), new Vector2(46, 216), new Vector2(184, 233), new Vector2(48, 91), new Vector2(152, 154), new Vector2(0, 132), new Vector2(136, 206) },
        new[] { new Vector2(-82, 41), new Vector2(-242, 87), new Vector2(-306, 171), new Vector2(-466, 114), new Vector2(-435, 190), new Vector2(-560, 171), new Vector2(-168, 50), new Vector2(-248, 134), new Vector2(-355, 86), new Vector2(-366, 175) },
        new[] { new Vector2(-109, 2), new Vector2(-325, 14), new Vector2(-454, 54), new Vector2(-462, -39), new Vector2(-624, 2), new Vector2(-680, 56), new Vector2(-218, 6), new Vector2(-386, 41), new Vector2(-403, -14), new Vector2(-552, 41) },
        new[] { new Vector2(-78, -31), new Vector2(-317, -75), new Vector2(-443, -138), new Vector2(-517, -82), new Vector2(-570, -183), new Vector2(-421, -210), new Vector2(-189, -42), new Vector2(-382, -98), new Vector2(-432, -61), new Vector2(-507, -158) }
    };
    public static readonly Node[] Nodes = BuildNodes();

    // -1 is the free starting point. Every edge has the same meaning for drawing
    // and availability, including cross-region junctions 80–87: ANY incoming rank.
    public static readonly Edge[] Edges = {
        new(-1,0), new(0,6), new(6,1), new(1,7), new(7,2), new(1,8), new(8,3), new(2,9), new(9,4), new(3,4), new(4,5),
        new(-1,10), new(10,16), new(16,11), new(11,17), new(17,12), new(11,18), new(18,13), new(12,19), new(19,14), new(13,14), new(14,15),
        new(-1,20), new(20,26), new(26,21), new(21,27), new(27,22), new(21,28), new(28,23), new(22,29), new(29,24), new(23,24), new(24,25),
        new(-1,30), new(30,36), new(36,31), new(31,37), new(37,32), new(31,38), new(38,33), new(32,39), new(39,34), new(33,34), new(34,35),
        new(-1,40), new(40,46), new(46,41), new(41,47), new(47,42), new(41,48), new(48,43), new(42,49), new(49,44), new(43,44), new(44,45),
        new(-1,50), new(50,56), new(56,51), new(51,57), new(57,52), new(51,58), new(58,53), new(52,59), new(59,54), new(53,54), new(54,55),
        new(-1,60), new(60,66), new(66,61), new(61,67), new(67,62), new(61,68), new(68,63), new(62,69), new(69,64), new(63,64), new(64,65),
        new(-1,70), new(70,76), new(76,71), new(71,77), new(77,72), new(71,78), new(78,73), new(72,79), new(79,74), new(73,74), new(74,75),
        new(3,80), new(12,80), new(80,4), new(80,14),
        new(13,81), new(22,81), new(81,15), new(81,24),
        new(23,82), new(32,82), new(82,24), new(82,35),
        new(33,83), new(42,83), new(83,34), new(83,45),
        new(43,84), new(52,84), new(84,44), new(84,54),
        new(53,85), new(62,85), new(85,55), new(85,64),
        new(63,86), new(73,86), new(86,64), new(86,74),
        new(72,87), new(2,87), new(87,75), new(87,5)
    };

    private static Node[] BuildNodes()
    {
        string[][] names = {
            new[] { "Tool reach", "Tool speed", "Careful work", "Vein reach", "Efficient tools", "Tool mastery" },
            new[] { "Ranged damage", "Ranged speed", "Projectile speed", "Piercing", "Bow mastery", "Ranged mastery" },
            new[] { "Movement speed", "Jump height", "Double jump", "Triple jump", "Dash", "Flight" },
            new[] { "Magic damage", "Mana reserve", "Mana recovery", "Spell reach", "Staff mastery", "Magic mastery" },
            new[] { "Maximum life", "Defence", "Recovery", "Breath reserve", "Swimming", "Resilience" },
            new[] { "Protection reach", "Threat awareness", "Revival speed", "Guard endurance", "Protective skill", "Support mastery" },
            new[] { "Pickup reach", "Inventory capacity", "Gathering speed", "Search reach", "Resource skill", "Gathering mastery" },
            new[] { "Melee damage", "Attack speed", "Melee reach", "Knockback", "Sword mastery", "Melee mastery" }
        };
        string[] smallStats = { "Tool speed", "Ranged damage", "Movement speed", "Magic damage", "Maximum life", "Protection reach", "Pickup reach", "Melee damage" };
        var nodes = new List<Node>();
        for (int branch = 0; branch < 8; branch++)
        for (int n = 0; n < 10; n++)
        {
            bool ability = n < 6 && (branch == 2 ? n >= 2 : branch == 4 ? n == 4 : n >= 4);
            NodeKind kind = n >= 6 ? NodeKind.Filler : ability ? NodeKind.Ability : NodeKind.Stat;
            string name = n >= 6 ? smallStats[branch] + " path" : names[branch][n];
            string description = kind == NodeKind.Filler ? "Small repeatable stat. A cheap path rank in the proposed progression." : ability ? "Ability preview. Opening it here grants no gameplay ability." : "Repeatable stat preview. Opening a rank makes onward paths available.";
            nodes.Add(new(name, description, Positions[branch][n], branch, kind, ability ? 1 : 5, n == 4 && branch is 1 or 3 or 7));
        }
        Vector2[] junctions = { new(82,-198), new(494,-133), new(606,50), new(267,202), new(-160,197), new(-550,101), new(-589,-80), new(-326,-191) };
        for (int i = 0; i < junctions.Length; i++)
            nodes.Add(new("Shared " + smallStats[i].ToLowerInvariant(), "Shared path rank. Open from either incoming path, then continue into either region.", junctions[i], i, NodeKind.Filler, 5));
        return nodes.ToArray();
    }
}
