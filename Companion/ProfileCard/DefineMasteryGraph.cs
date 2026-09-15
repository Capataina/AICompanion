#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The mastery preview's one flat tree, generated from one lane template rotated a quarter turn per lane, with every
/// node's content in one data table. It is the native form of the wheel in
/// <c>InterfaceExperiments/companion-card.html</c> as the owner revised it on 15 September 2026, and every name and
/// number in the table is a draft the owner will edit; nothing here changes gameplay, spends a point or is saved.
///
/// <para>Four lanes are what the orb can grow: Combat at the top, then Movement, Survival and Gathering clockwise.
/// Each lane is ten roles: a circle, a circle, then a split whose ranks alternate a diamond on the left beside a
/// circle on the right and a circle on the left beside a diamond on the right, two circles, a meeting diamond and a
/// closing circle. A shared circle sits between neighbouring lanes, opened from either lane's first-rank node. A
/// diamond unlocks something and opens nothing, because the owner removed trees inside diamonds as more than the
/// companion needs; a circle has levels. The same edge table governs drawing and learning, so a drawn connection is
/// always a real path.</para>
/// </summary>
public static class DefineMasteryGraph
{
    public enum NodeKind { Upgrade, Unlock, Shared }

    /// <summary>A node's draft content. <c>SizedAgainst</c> is kept for balancing and never drawn, by the owner's ruling.</summary>
    public readonly record struct NodeContent(string Name, int Levels, string Effect, string Purpose, string SizedAgainst, int[] Needs);
    public readonly record struct Node(NodeContent Content, Vector2 Position, int Lane, int Role, NodeKind Kind, int[] Needs);
    public readonly record struct Edge(int From, int To);
    /// <summary>A lane's name, anchored in graph units, with the fraction of the text's box that sits on the anchor.</summary>
    public readonly record struct LaneLabel(string Text, Vector2 Anchor, Vector2 Pivot);

    public static readonly string[] Lanes = { "Combat", "Movement", "Survival", "Gathering" };
    public static readonly Color[] Colors = { new(237, 132, 146), new(104, 213, 220), new(233, 180, 103), new(109, 201, 178) };

    public const int Roles = 10;
    public static readonly int[] DiamondRoles = { 2, 5, 8 };
    /// <summary>Graph units: a node's radius, where the shared circles sit, and the angle between lanes.</summary>
    public const float NodeRadius = 22, JunctionRadius = 290, LaneSpacing = 90;
    /// <summary>A lane label's text height in graph units, and its gap beyond the lane's last node's edge.</summary>
    public const float LabelHeight = 40, LabelGap = 16;

    /// <summary>
    /// [radius, angular offset in degrees] per role, the same for every lane. The paths open 15 degrees either side of
    /// the lane at radius 250, 13 at 340 and 11 at 430, so the facing pair at each rank is at least 129 units apart; the
    /// closest pairs are the straight steps of 80 units, against 44-unit nodes, and <see cref="NearestPair"/> proves it.
    /// </summary>
    private static readonly (float R, float Deg)[] Template =
        { (80, 0), (160, 0), (250, 15), (250, -15), (340, 13), (340, -13), (430, 11), (430, -11), (520, 0), (600, 0) };
    public static readonly (int From, int To)[] LaneEdges =
        { (-1, 0), (0, 1), (1, 2), (1, 3), (2, 4), (3, 5), (4, 6), (5, 7), (6, 8), (7, 8), (8, 9) };

    private const string NoRef = "Nothing comparable in Terraria, Calamity or Thorium was found; the number is a first guess.";
    private const string SlotNote = "It still fires one weapon at a time, choosing whichever suits the target, so a slot adds choice rather than a second stream of shots.";
    private static NodeContent U(string name, int levels, string effect, string purpose, string sized, params int[] needs) => new(name, levels, effect, purpose, sized, needs);
    private static NodeContent A(string name, string effect, string purpose, string sized, params int[] needs) => new(name, 1, effect, purpose, sized, needs);

    /// <summary>The draft content, lane by lane in role order; <c>needs</c> name roles in the same lane.</summary>
    private static readonly NodeContent[][] LaneContent =
    {
        new[]
        {
            U("Damage", 3, "+3% damage per level", "Every weapon it holds, the first slot included.", "A Destroyer Emblem-class accessory gives a player 8–15%; a Wrath Potion 10%."),
            U("Attack speed", 3, "+3% attack speed per level", "Every weapon fires or swings a little more often.", "Thorium's Bard empowerment steps 3/6/9/12%; the Feral Claws line gives 12% melee speed."),
            A("Second weapon slot", "Opens the second weapon slot", SlotNote, "Terraria never gives slots as a percentage: a Pygmy Necklace gives one more minion slot."),
            U("Piercing", 1, "Its shots pass through one more enemy", "A line of slimes takes one shot for two.", NoRef),
            U("Extra projectile", 1, "Each shot fires one more projectile, at half damage", "Crowds take more hits; a single target takes at most half as much again.", NoRef),
            A("Third weapon slot", "Opens the third weapon slot", SlotNote, "Calamity's Statis' Blessing gives two minion slots, and was cut down from three.", 2),
            U("Critical strike", 2, "+3% critical strike chance per level", "More of its hits crit.", "A Lucky reforge gives a player 4% per accessory."),
            U("Projectile speed", 2, "+10% projectile speed per level", "Shots reach moving targets sooner, so fewer miss.", "A Magic Quiver gives arrows 120% velocity, but velocity is that item's whole purpose."),
            A("Fourth weapon slot", "Opens the fourth weapon slot", SlotNote, "Vanilla reaches eleven minion slots from one, always one or two at a time.", 2, 5),
            U("Damage", 2, "+4% damage per level", "With the first Damage node, +17% in all: about one emblem.", "A Destroyer Emblem-class accessory gives a player 8–15%."),
        },
        new[]
        {
            U("Top speed", 3, "+3% top speed per level", "It keeps up when you sprint.", "An Aglet gives a player 5%, an Anklet of the Wind 10%."),
            U("Acceleration", 3, "+5% acceleration per level", "It reaches top speed sooner.", "Terraria does not publish acceleration as a percentage; the number is a first guess."),
            A("Dash", "A short burst of about 10 tiles, every 4 seconds", "It dashes when it needs to be somewhere fast or out of the way of a hit.", "A Shield of Cthulhu dashes every 0.5 s; Calamity's Asgard's Valor covers 18 blocks."),
            U("Turning", 2, "+8% turn rate per level", "Tighter curves around corners and shots.", NoRef),
            U("Dash cooldown", 2, "The dash comes back 0.5 seconds sooner per level", "Down to 3 seconds.", "A Shield of Cthulhu waits 0.5 s and Master Ninja Gear 1 s, both for a player."),
            A("Slipstream", "+10% top speed while flying behind you as you move", "Following you is easier than chasing you.", NoRef),
            U("Homing flight", 2, "+10% speed per level when flying back from far away", "It rejoins you sooner after being left behind.", NoRef),
            U("Steady hull", 2, "−15% knockback taken per level", "Hits shove it off course less.", "Calamity's Asgard's Valor gives full knockback immunity in one item."),
            A("Afterburner", "After 2 seconds without slowing, +10% top speed until it slows", "Long trips go faster.", "The Panic! buff gives a player 100% speed, but only briefly after a hit."),
            U("Top speed", 2, "+3% top speed per level", "With the first Top speed node, +15% in all.", "An Aglet gives a player 5%, an Anklet of the Wind 10%."),
        },
        new[]
        {
            U("Max life", 3, "+20 life per level", "It survives longer in a fight.", "A Life Crystal gives a player 20."),
            U("Plating", 3, "2% less damage taken per level", "Every hit hurts a little less.", "A Worm Scarf gives 17% and the Chlorophyte set 5%; Terraria keeps unconditional damage reduction small."),
            A("Waterproof", "Flies through water and honey and takes no damage", "Water stops being a wall to its routes.", "Terraria goes from timed breathing to Neptune's Shell, which removes the limit for good."),
            U("Regeneration", 2, "+1 life per second per level", "It heals between fights.", "A Band of Regeneration gives a player 1 life per second."),
            U("Quick reboot", 3, "Revives 5 seconds sooner per level", "Less time without it after it goes down.", "Nothing in Terraria shortens respawn: a player waits 10 s, or 15 s in Expert."),
            A("Magma core", "Flies through lava and takes no damage", "Lava stops being a wall to its routes.", "A Lava Charm gives a player 7 seconds; Calamity's Void Striders give full immunity late in the game."),
            U("Last stand", 2, "5% less damage taken per level while below half life", "Harder to finish off.", "A Frozen Turtle Shell gives a player 25%, but only below half life."),
            U("Thick hull", 2, "Debuffs on it wear off 20% faster per level", "Poison and fire hold on to it less.", NoRef),
            A("Guardian link", "5% of the damage you take goes to the companion while it is within 25 tiles", "It takes a small part of every hit for you.", "A Paladin's Shield absorbs 25% of a teammate's damage within 50 tiles, at most once every 0.67 s."),
            U("Link share", 2, "+5% more of your damage per level", "Stays under a Paladin's Shield even when full.", "A Paladin's Shield absorbs 25%."),
        },
        new[]
        {
            U("Mining speed", 3, "+5% mining speed per level", "Ore comes out faster.", "A Mining Potion and an Ancient Chisel each give a player 25%."),
            U("Chopping speed", 3, "+5% chopping speed per level", "Trees come down faster.", "Sized like mining speed."),
            A("Vein beam", "Breaking an ore also breaks one touching tile of the same ore", "Veins come out in fewer passes.", "No Terraria item raises ore per tile; big tools break an area instead, like the Picksaw's 3×3."),
            U("Tool reach", 2, "+1 tile of tool reach per level", "Works a tile from further away.", NoRef),
            U("Bag space", 2, "+10 bag slots per level", "Carries more before you empty it.", NoRef),
            A("Tractor field", "Pulls loot in from 6 tiles away", "Fewer drops left behind.", "A Treasure Magnet stretches a player's pickup range from about 2.6 to 12 tiles."),
            U("Flashlight reach", 2, "+10% flashlight reach per level", "Lights dark places from further away.", "No published light radius for the Mining Helmet or Shine Potion was found; the number is a first guess."),
            U("Pull range", 2, "+2 tiles of pull range per level", "The tractor field reaches further.", "A Treasure Magnet reaches 12 tiles."),
            A("Prospector's eye", "Sees ore through walls within 15 tiles", "It notices veins it would otherwise fly past.", "Like a Spelunker Potion, for the companion."),
            U("Quick hands", 2, "+3% mining and chopping speed per level", "With the first two nodes, +21% in all.", "A Mining Potion gives a player 25%."),
        },
    };

    /// <summary>A shared circle between lane i and lane i+1, in lane order.</summary>
    private static readonly NodeContent[] JunctionContent =
    {
        U("Strafe", 2, "+3% attack speed per level while flying at top speed", "Shared by Combat and Movement.", NoRef),
        U("Evasive", 2, "+10% speed per level for 1 second after a hit misses it", "Shared by Movement and Survival.", NoRef),
        U("Trap sense", 2, "20% less damage from traps per level", "Shared by Survival and Gathering.", NoRef),
        U("Cutting beam", 1, "Its mining beam deals 10 damage per second to enemies it crosses", "Shared by Gathering and Combat.", NoRef),
    };

    public static readonly Node[] Nodes;
    /// <summary>-1 is the centre, which is always open.</summary>
    public static readonly Edge[] Edges;
    public static readonly LaneLabel[] Labels;
    /// <summary>The smallest distance between any two node centres, in graph units; it must exceed a node's diameter.</summary>
    public static readonly float NearestPair;
    public static int FirstShared => Lanes.Length * Roles;

    static DefineMasteryGraph()
    {
        var nodes = new List<Node>();
        var edges = new List<Edge>();
        var labels = new List<LaneLabel>();
        for (int lane = 0; lane < Lanes.Length; lane++)
        {
            float axis = lane * LaneSpacing - 90;
            for (int role = 0; role < Roles; role++)
            {
                var (r, deg) = Template[role];
                NodeContent content = LaneContent[lane][role];
                int[] needs = Array.ConvertAll(content.Needs, k => lane * Roles + k);
                nodes.Add(new Node(content, Polar(r, axis + deg), lane, role,
                    Array.IndexOf(DiamondRoles, role) >= 0 ? NodeKind.Unlock : NodeKind.Upgrade, needs));
            }
            foreach (var (from, to) in LaneEdges)
                edges.Add(new Edge(from < 0 ? -1 : lane * Roles + from, lane * Roles + to));
            // A label sits beyond its lane's last node, one node radius plus a gap out, with the text's near edge on that
            // point: centred above the top lane, below the bottom one, starting right of the right lane and ending left
            // of the left one. Centring every label on its lane's end put the side lanes' names across their last nodes.
            float reach = Template[Roles - 1].R + NodeRadius + LabelGap;
            Vector2 direction = new(MathF.Round(MathF.Cos(MathHelper.ToRadians(axis))), MathF.Round(MathF.Sin(MathHelper.ToRadians(axis))));
            labels.Add(new LaneLabel(Lanes[lane], Polar(reach, axis), new Vector2(.5f - .5f * direction.X, .5f - .5f * direction.Y)));
        }
        for (int i = 0; i < Lanes.Length; i++)
        {
            int a = i * Roles, c = ((i + 1) % Lanes.Length) * Roles, j = nodes.Count;
            nodes.Add(new Node(JunctionContent[i], Polar(JunctionRadius, i * LaneSpacing - 90 + LaneSpacing / 2), i, -1, NodeKind.Shared, Array.Empty<int>()));
            // Joined to lane i's clockwise first-rank node and lane i+1's counter-clockwise first-rank node.
            edges.Add(new Edge(a + 2, j));
            edges.Add(new Edge(c + 3, j));
        }
        Nodes = nodes.ToArray();
        Edges = edges.ToArray();
        Labels = labels.ToArray();
        float nearest = float.MaxValue;
        for (int i = 0; i < Nodes.Length; i++)
            for (int k = i + 1; k < Nodes.Length; k++)
                nearest = Math.Min(nearest, Vector2.Distance(Nodes[i].Position, Nodes[k].Position));
        NearestPair = nearest;
    }

    /// <summary>
    /// Whether a node can take a level, given every node's learned level: it has a level left, an edge into it comes from
    /// something learned (the centre always counts), and everything it needs is learned. The rule lives with the graph
    /// rather than the page because it is a fact about the tree, so it can be held without drawing anything.
    /// </summary>
    public static bool CanLearn(int[] levels, int node)
    {
        if (levels[node] >= Nodes[node].Content.Levels) return false;
        bool connected = false;
        foreach (Edge edge in Edges)
            if (edge.To == node && (edge.From < 0 || levels[edge.From] > 0)) { connected = true; break; }
        if (!connected) return false;
        foreach (int need in Nodes[node].Needs)
            if (levels[need] <= 0) return false;
        return true;
    }

    /// <summary>A position in graph units, rounded half up the way the mock's own script rounds.</summary>
    private static Vector2 Polar(float r, float degrees)
    {
        float a = MathHelper.ToRadians(degrees);
        return new Vector2(MathF.Floor(r * MathF.Cos(a) + .5f), MathF.Floor(r * MathF.Sin(a) + .5f));
    }
}
