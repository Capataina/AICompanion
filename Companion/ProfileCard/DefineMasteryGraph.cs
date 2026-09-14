#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The mastery wheel and the tree every diamond opens, generated from one spoke template and
/// one branch template rather than hand-placed, so the wheel is fourfold symmetric: any spoke
/// turned onto any other lands node for node. Geometry never depends on a layout solver, and
/// the same edge tables govern drawing and availability, so a drawn connection is always a real
/// path. This is the native form of the wheel in <c>InterfaceExperiments/companion-card.html</c>,
/// whose header carries the sizes; every name is a placeholder until the progression is numbered.
///
/// Four lanes are what the orb can grow (the owner's ruling of 14 September 2026): Guarding is
/// its protection duties, Movement is speed, acceleration and the dash, Survival is life, armour,
/// regen, resistances and the water and lava immunities, Gathering is tool use and the bag. The
/// four weapon-class lanes went with the authored weapon kit; a handed weapon is chosen by putting
/// it in a gear slot, and has no tree here.
/// </summary>
public static class DefineMasteryGraph
{
    /// <summary>A circle that repeats, a diamond that is an effect the world applies and opens its own tree, a shared circle between two spokes.</summary>
    public enum NodeKind { Stat, Ability, Shared }
    public readonly record struct Node(string Name, string Description, Vector2 Position, int Branch, NodeKind Kind, int MaxTier);
    public readonly record struct Edge(int From, int To);

    public static readonly string[] Branches = { "Guarding", "Movement", "Survival", "Gathering" };
    public static readonly Color[] Colors = { new(233, 180, 103), new(104, 213, 220), new(237, 132, 146), new(109, 201, 178) };

    /// <summary>
    /// Nineteen roles per spoke in the order the template lists them: the trunk stat, the fork
    /// stat, the left path's eight ranks, the right path's eight ranks, and the tip where the
    /// paths meet. Diamonds alternate sides with a circle-circle rank between each: left ranks
    /// one and five, right ranks three and seven, and the tip.
    /// </summary>
    public const int Roles = 19;
    private static readonly int[] DiamondRoles = { 2, 6, 12, 16, 18 };

    private static readonly string[][] Names =
    {
        // Guarding is effects, never behaviours: a share of the player's damage taken instead, less damage for
        // both the closer they stand, a share of the companion's damage healing the player, its hits cleansing.
        new[] { "Protection reach", "Threat awareness", "Shared pain", "Revival speed", "Vigilance", "Guard endurance", "Close guard", "Steadfast", "Cover", "Watchful",
                "Calm", "Mending", "Lifelink", "Quick revive", "Resolve", "Bulwark", "Cleanse", "Guardian", "Sanctuary" },
        // Movement is what a flying body can grow: how fast, how quickly it gets there, and the dash. No jumps, no flight rank, because the orb already flies.
        new[] { "Speed", "Acceleration", "Dash", "Top speed", "Sharp turn", "Sustained pace", "Chain dash", "Quick stop", "Drift control", "Burst",
                "Momentum", "Steady climb", "Slipstream", "Recovery speed", "Overtake", "Dash cooldown", "Phase dash", "Cruise", "Flight mastery" },
        // Survival carries the two immunities as diamonds: water first, lava later, the owner's order.
        new[] { "Maximum life", "Defence", "Quick respawn", "Life regen", "Armour", "Thick skin", "Water immunity", "Knockback resistance", "Poison resistance", "Iron will",
                "Steady hull", "Burn resistance", "Second wind", "Chill resistance", "Bounce back", "Last stand", "Lava immunity", "Endurance", "Resilience" },
        // Gathering is all tool use: the drill, its reach and its light, the pickup and the bag.
        new[] { "Mining speed", "Chopping speed", "Torch hat", "Tool reach", "Careful work", "Vein reach", "Brighter hat", "Pickup reach", "Bag capacity", "Coin sense",
                "Search reach", "Keen eye", "Wide beam", "Ore sense", "Wood sense", "Quick hands", "Night sight", "Sorting", "Auto torch" },
    };

    /// <summary>
    /// Radius and angular offset per role. The offsets are the mock's eight-spoke ones widened
    /// threefold, because four spokes at the mock's fork read as a plus sign with empty quadrants
    /// (rendered and looked at on 15 September 2026 at 1.6 times, still a cross); at three times
    /// each spoke is a leaf whose paths open to 36 degrees and the four leaves fill the disc.
    /// Facing paths of neighbouring spokes still keep 18 degrees between them, about 100 units
    /// at their nearest rank, and the shared node between two spokes sits a short edge from both
    /// fourth ranks. The fixture proves no two nodes come within a node's width.
    /// </summary>
    private const float OffsetWiden = 3f;
    private static readonly (float R, float Deg)[] Template =
    {
        (100, 0), (180, 2),
        (250, 10), (320, 12), (390, 12), (460, 11), (530, 10), (600, 9), (670, 8), (740, 7),
        (250, -10), (320, -12), (390, -12), (460, -11), (530, -10), (600, -9), (670, -8), (740, -7),
        (820, 0),
    };
    private static readonly (int From, int To)[] SpokeEdges =
    {
        (-1, 0), (0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 9), (9, 18),
        (1, 10), (10, 11), (11, 12), (12, 13), (13, 14), (14, 15), (15, 16), (16, 17), (17, 18),
    };
    public const float LabelRadius = 910f, JunctionRadius = 495f;
    /// <summary>The square the whole wheel, labels included, fits in; the viewer fits this to its viewport.</summary>
    public const float Extent = 2 * (LabelRadius + 70);

    public static readonly Node[] Nodes;
    /// <summary>-1 is the free centre. Every edge has the same meaning for drawing and availability.</summary>
    public static readonly Edge[] Edges;
    public static readonly Vector2[] Labels;
    /// <summary>The first shared node's index; the spokes' own nodes come before it, <see cref="Roles"/> per spoke.</summary>
    public static int FirstShared => Branches.Length * Roles;

    static DefineMasteryGraph()
    {
        var nodes = new List<Node>();
        var edges = new List<Edge>();
        float spacing = 360f / Branches.Length;
        for (int branch = 0; branch < Branches.Length; branch++)
        {
            float axis = branch * spacing - 90;
            for (int role = 0; role < Roles; role++)
            {
                var (r, deg) = Template[role];
                bool diamond = Array.IndexOf(DiamondRoles, role) >= 0;
                nodes.Add(new Node(Names[branch][role],
                    diamond ? "Ability preview. Opening it here grants no gameplay ability; clicking it again opens its own upgrades."
                            : "Repeatable stat preview. Opening a rank makes onward paths available.",
                    Polar(r, axis + deg * OffsetWiden), branch, diamond ? NodeKind.Ability : NodeKind.Stat, diamond ? 1 : 5));
            }
            foreach (var (from, to) in SpokeEdges)
                edges.Add(new Edge(from < 0 ? -1 : branch * Roles + from, branch * Roles + to));
        }
        // A shared circle between spoke i and spoke i+1 joins i's left path to i+1's right path at
        // their fourth rank and opens the fifth rank of each, so a player can cross between lanes
        // mid-way rather than only from the centre.
        for (int i = 0; i < Branches.Length; i++)
        {
            int a = i * Roles, c = ((i + 1) % Branches.Length) * Roles, j = nodes.Count;
            nodes.Add(new Node("Shared " + Names[i][0].ToLowerInvariant(),
                "Shared path rank. Open from either incoming path, then continue into either region.",
                Polar(JunctionRadius, i * spacing - 90 + spacing / 2), i, NodeKind.Shared, 5));
            edges.Add(new Edge(a + 5, j)); edges.Add(new Edge(c + 13, j));
            edges.Add(new Edge(j, a + 6)); edges.Add(new Edge(j, c + 14));
        }
        Nodes = nodes.ToArray();
        Edges = edges.ToArray();
        Labels = new Vector2[Branches.Length];
        for (int branch = 0; branch < Branches.Length; branch++)
            Labels[branch] = Polar(LabelRadius, branch * spacing - 90);
        (SubNodes, SubEdges, SubLabels) = BuildSubTree();
    }

    // ---- the tree every diamond opens: fifteen points in the wheel's own grammar ----

    /// <summary>
    /// The opened diamond sits filled at the centre where the wheel has its root; three lines at a
    /// third of a turn each, a circle that forks into an unconnected pair of circles which meet
    /// again at a diamond at the tip, and a shared circle in each gap opened from either
    /// neighbouring pair. Four roles per line: rank one, the pair, the tip.
    /// </summary>
    public const int SubRoles = 4, SubLines = 3;
    public static readonly string[] SubLineNames = { "Strength", "Speed", "Reach" };
    private static readonly string[][] SubNames =
    {
        new[] { "Strength I", "Strength II", "Efficiency", "Strength mastery" },
        new[] { "Speed I", "Speed II", "Recovery", "Speed mastery" },
        new[] { "Reach I", "Reach II", "Control", "Reach mastery" },
    };
    private static readonly (float R, float Deg)[] SubTemplate = { (120, 0), (230, -22), (230, 22), (340, 0) };
    public const float SubJunctionRadius = 230f, SubLabelRadius = 410f;
    public const float SubExtent = 2 * (SubLabelRadius + 70);

    /// <summary>The fifteen nodes of any diamond's tree; the opened diamond's branch colours them.</summary>
    public static readonly Node[] SubNodes;
    public static readonly Edge[] SubEdges;
    public static readonly Vector2[] SubLabels;
    public static int FirstSubShared => SubLines * SubRoles;

    private static (Node[] Nodes, Edge[] Edges, Vector2[] Labels) BuildSubTree()
    {
        var list = new List<Node>();
        var edgeList = new List<Edge>();
        var labels = new Vector2[SubLines];
        for (int line = 0; line < SubLines; line++)
        {
            float axis = line * 120 - 90;
            int first = list.Count;
            for (int role = 0; role < SubRoles; role++)
            {
                var (r, deg) = SubTemplate[role];
                bool tip = role == SubRoles - 1;
                list.Add(new Node(SubNames[line][role], tip ? "Mastery of this line. Preview only." : "Upgrade rank. Preview only.",
                    Polar(r, axis + deg), line, tip ? NodeKind.Ability : NodeKind.Stat, tip ? 1 : 5));
            }
            // the centre to rank one, rank one to each of the pair, each of the pair to the tip; the
            // pair is deliberately not joined to itself
            edgeList.Add(new Edge(-1, first)); edgeList.Add(new Edge(first, first + 1)); edgeList.Add(new Edge(first, first + 2));
            edgeList.Add(new Edge(first + 1, first + 3)); edgeList.Add(new Edge(first + 2, first + 3));
            labels[line] = Polar(SubLabelRadius, axis);
        }
        for (int i = 0; i < SubLines; i++)
        {
            // a shared circle between line i and line i+1, opened from the clockwise node of i's
            // pair or the counter-clockwise node of i+1's pair
            int a = i * SubRoles, c = ((i + 1) % SubLines) * SubRoles, j = list.Count;
            list.Add(new Node($"Shared {SubLineNames[i].ToLowerInvariant()} and {SubLineNames[(i + 1) % SubLines].ToLowerInvariant()}",
                "Shared rank. Open from either neighbouring line. Preview only.", Polar(SubJunctionRadius, i * 120 - 90 + 60), i, NodeKind.Shared, 5));
            edgeList.Add(new Edge(a + 2, j)); edgeList.Add(new Edge(c + 1, j));
        }
        return (list.ToArray(), edgeList.ToArray(), labels);
    }

    private static Vector2 Polar(float r, float degrees)
    {
        float a = MathHelper.ToRadians(degrees);
        return new Vector2(MathF.Round(r * MathF.Cos(a)), MathF.Round(r * MathF.Sin(a)));
    }
}
