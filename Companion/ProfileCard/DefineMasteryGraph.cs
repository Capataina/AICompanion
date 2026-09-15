#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.ProfileCard;

/// <summary>
/// The mastery preview's one flat tree, generated from one lane template rotated a quarter turn per lane, with every
/// node's content in one data table. The table is the owner's node list as the agreed mock carries it
/// (<c>InterfaceExperiments/companion-card.html</c>, <c>LANES</c> and <c>JUNCTIONS</c>, as of 0f49104), and the suite's
/// mastery rules row reads that file and requires every name, level count, effect and per-level line here to match
/// it, so the two cannot drift. Nothing here changes gameplay, spends a point or is saved.
///
/// <para>Four lanes are what the orb can grow: Combat at the top, then Movement, Survival and Gathering clockwise.
/// Each lane is ten roles: a circle, a circle, then a split whose ranks alternate a diamond on the left beside a
/// circle on the right and a circle on the left beside a diamond on the right, two circles, a meeting diamond and a
/// closing circle. A shared circle sits between neighbouring lanes, opened from either lane's first-rank node. The
/// same edge table governs drawing and learning, so a drawn connection is always a real path.</para>
///
/// <para>Every node is a flat bonus to a number the companion already reads, never a new thing it can do, by the
/// owner's ruling of 15 September 2026, and a diamond opens nothing. The shape says how far a node levels: a circle
/// has five levels, a diamond three, and a weapon slot (Combat's three diamonds) one. A node whose levels do not step
/// evenly carries one line per level instead of a per-level effect.</para>
/// </summary>
public static class DefineMasteryGraph
{
    public enum NodeKind { Circle, Diamond, Shared }

    /// <summary>
    /// A node's content. <c>Purpose</c> and <c>SizedAgainst</c> are kept for balancing and never drawn, by the owner's
    /// ruling. <c>LevelLines</c> is one line per level for a node whose levels do not step evenly, and null otherwise.
    /// </summary>
    public readonly record struct NodeContent(string Name, int Levels, string Effect, string Purpose, string SizedAgainst, int[] Needs, string[]? LevelLines);
    public readonly record struct Node(NodeContent Content, Vector2 Position, int Lane, int Role, NodeKind Kind, int[] Needs);
    public readonly record struct Edge(int From, int To);
    /// <summary>A lane's name, anchored in graph units, with the fraction of the text's box that sits on the anchor.</summary>
    public readonly record struct LaneLabel(string Text, Vector2 Anchor, Vector2 Pivot);

    public static readonly string[] Lanes = { "Combat", "Movement", "Survival", "Gathering" };
    public static readonly Color[] Colors = { new(237, 132, 146), new(104, 213, 220), new(233, 180, 103), new(109, 201, 178) };

    public const int Roles = 10;
    public static readonly int[] DiamondRoles = { 2, 5, 8 };
    /// <summary>How many levels each shape takes, by the owner's ruling; a weapon slot is Combat's diamond.</summary>
    public const int CircleLevels = 5, DiamondLevels = 3, WeaponSlotLevels = 1;
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

    // The mock's own helpers, so a row here reads like its row there: C a five-level circle, D a three-level diamond,
    // A a one-level weapon slot, L a node whose levels each carry their own line.
    private static NodeContent C(string name, string effect, string purpose, string sized, params int[] needs) => new(name, CircleLevels, effect, purpose, sized, needs, null);
    private static NodeContent D(string name, string effect, string purpose, string sized, params int[] needs) => new(name, DiamondLevels, effect, purpose, sized, needs, null);
    private static NodeContent A(string name, string effect, string purpose, string sized, params int[] needs) => new(name, WeaponSlotLevels, effect, purpose, sized, needs, null);
    private static NodeContent L(NodeContent node, params string[] lines) => node with { LevelLines = lines };

    /// <summary>The content, lane by lane in role order; <c>needs</c> name roles in the same lane.</summary>
    private static readonly NodeContent[][] LaneContent =
    {
        new[]
        {
            C("Damage", "+2% damage per level", "Every weapon it holds, the first slot included.", "A Destroyer Emblem-class accessory gives a player 8–15%; a Wrath Potion 10%."),
            C("Attack speed", "+2% attack speed per level", "Every weapon fires or swings a little more often, through the use time the weapons already read.", "Thorium's Bard empowerment steps 3/6/9/12%; the Feral Claws line gives 12% melee speed."),
            A("Extra weapon slot", "Opens one more weapon slot", SlotNote, "Terraria never gives slots as a percentage: a Pygmy Necklace gives one more minion slot."),
            L(C("Piercing", "Shots pass through more enemies, each taking less damage", "The extra enemies come after the weapon's own last hit, which keeps its own damage; a weapon that already pierces without limit, and a swing, gain nothing.", NoRef),
                "Passes through 1 more enemy, which takes 20% damage",
                "Passes through 1 more enemy, which takes 40% damage",
                "Passes through 2 more enemies, taking 60% and 15% damage",
                "Passes through 2 more enemies, taking 70% and 30% damage",
                "Passes through 3 more enemies, taking 75%, 50% and 25% damage"),
            L(C("Extra projectile", "Fires more projectiles after each shot, each weaker", "Each extra projectile leaves a fifth of the weapon's own reload after the one before, so a bow's arrows never overlap.", NoRef),
                "A second projectile at 30% damage",
                "A second projectile at 40% damage",
                "Two more projectiles, at 40% and 15% damage",
                "Two more projectiles, at 45% and 20% damage",
                "Three more projectiles, at 50%, 25% and 10% damage"),
            A("Extra weapon slot", "Opens one more weapon slot", SlotNote, "Calamity's Statis' Blessing gives two minion slots, and was cut down from three.", 2),
            C("Critical strike", "+1% critical strike chance per level", "More of its hits crit.", "A Lucky reforge gives a player 4% per accessory."),
            C("Projectile speed", "+4% projectile speed per level", "Shots reach moving targets sooner, so fewer miss.", "A Magic Quiver gives arrows 120% velocity, but velocity is that item's whole purpose."),
            A("Extra weapon slot", "Opens one more weapon slot", SlotNote, "Vanilla reaches eleven minion slots from one, always one or two at a time.", 2, 5),
            C("Armour piercing", "Ignores 2% of an enemy's defence per level", "A share rather than a flat amount, so it still counts against an enemy with enormous defence.", "Terraria's armour penetration is flat: a Shark Tooth Necklace ignores 5 defence whatever the enemy has."),
        },
        new[]
        {
            C("Top speed", "+2% top speed per level", "It keeps up when you sprint.", "An Aglet gives a player 5%, an Anklet of the Wind 10%."),
            C("Acceleration", "+3% acceleration per level", "It reaches top speed and slows down sooner.", "Terraria does not publish acceleration as a percentage; the number is a first guess."),
            D("Return flight", "+10% speed flying back to you from far away per level", "Being left behind costs less time.", NoRef),
            C("Job rush", "+3% acceleration flying to a job per level", "It gets moving sooner when there is work to go to.", NoRef),
            C("Steady hull", "5% less knockback taken per level", "Hits shove it off course less.", "Calamity's Asgard's Valor gives full knockback immunity in one item."),
            D("Overdrive", "+4% top speed per level", "A bigger step of the same speed, so a long trip takes less time.", "An Anklet of the Wind gives a player 10%."),
            C("Return thrust", "+5% acceleration flying back to you per level", "It gets up to speed sooner when it has to come home.", NoRef),
            C("Thrusters", "+2% acceleration per level", "Quicker to get up to speed and to slow down.", NoRef),
            D("Errand speed", "+5% top speed flying to a job per level", "Work across the screen starts sooner.", NoRef),
            C("Top speed", "+2% top speed per level", "With the first Top speed node and Overdrive, +32% in all.", "Hermes Boots and an Anklet of the Wind together give a player well past that."),
        },
        new[]
        {
            C("Max life", "+10 life per level", "On top of the life it mirrors from you.", "A Life Crystal gives a player 20."),
            C("Plating", "1% less damage taken per level", "Every hit hurts a little less.", "A Worm Scarf gives 17% and the Chlorophyte set 5%; Terraria keeps unconditional damage reduction small."),
            D("Guardian link", "5% of the damage you take goes to the companion per level", "It takes part of every hit for you, wherever it is.", "A Paladin's Shield absorbs 25% of a teammate's damage within 50 tiles, at most once every 0.67 s."),
            C("Regeneration", "+0.5 life per second per level", "It heals between fights.", "A Band of Regeneration gives a player 1 life per second."),
            C("Quick reboot", "Revives 3 seconds sooner per level", "From 20 seconds down to 5 at level 5.", "Nothing in Terraria shortens respawn: a player waits 10 s, or 15 s in Expert."),
            D("Lifeline", "1% of the damage it deals heals you per level", "Its fighting keeps you standing.", "Vampire Knives heal a player from 7.5% of their own damage, capped per second."),
            C("Last stand", "3% less damage taken per level while below half life", "Harder to finish off.", "A Frozen Turtle Shell gives a player 25%, but only below half life."),
            C("Thick hull", "Debuffs on it wear off 10% faster per level", "Poison and fire hold on to it less.", NoRef),
            D("Emergency repair", "Below 10% life it heals to full, once every 3, 2, then 1 minute", "One big hit cannot take it out of a fight.", "Like Calamity's revival accessories, but once a cooldown rather than once a life."),
            C("Toughness", "+2 defence per level", "On top of the defence it mirrors from you.", "A Cobalt Shield gives a player 1 defence; a Warrior emblem none."),
        },
        new[]
        {
            C("Tool speed", "+4% mining and chopping speed per level", "Ore comes out and trees come down faster.", "A Mining Potion and an Ancient Chisel each give a player 25%."),
            C("Timber", "2% chance per level that a tree drops twice its wood", "Trees give a little more.", "Sized like Lucky strike."),
            D("Vein beam", "Breaking an ore also breaks one more touching ore per level", "Veins come out in fewer passes.", "No Terraria item raises ore per tile; big tools break an area instead, like the Picksaw's 3×3."),
            C("Tool reach", "+1 tile of tool reach per level", "Breaks ore and wood and places torches from further away.", "A Toolbelt gives a player 1 tile; an Extendo Grip 3."),
            C("Lucky strike", "2% chance per level that an ore drops twice", "Veins give a little more.", "Calamity's and Thorium's double-drop effects sit around 5–10%."),
            D("Tractor field", "Pulls loot in from 6 more tiles per level", "Fewer drops left behind: 6, 12, then 18 tiles.", "A Treasure Magnet stretches a player's pickup range from about 2.6 to 12 tiles."),
            C("Prospector's eye", "Ore within 3 more tiles per level glows for you", "You see the veins it is flying past.", "A Spelunker Potion lights every ore on a player's screen."),
            C("Flashlight reach", "+10% flashlight reach per level", "Lights dark places from further away; the beam is brightest at the source and fades toward its end.", "No published light radius for the Mining Helmet or Shine Potion was found; the number is a first guess."),
            L(D("Bag space", "More bag slots", "Carries more before you empty it: 120 slots become 200.", NoRef),
                "+15 bag slots",
                "+25 more bag slots",
                "+40 more bag slots"),
            C("Plunder", "Enemies drop 5% more loot per level", "Every drop from every enemy, a mod's included, is more likely: a 1% drop is about 1.25% at level 5.", "Terraria's luck gives some drops a second roll; nothing in the game raises every drop by a share."),
        },
    };

    /// <summary>A shared circle between lane i and lane i+1, in lane order.</summary>
    private static readonly NodeContent[] JunctionContent =
    {
        C("Impact", "+5% knockback dealt per level", "Shared by Combat and Movement.", "A Titan Glove gives a player 100% knockback."),
        C("Shock absorbers", "+0.1 seconds of invincibility after a hit per level", "Shared by Movement and Survival.", "A Cross Necklace gives a player 1 second."),
        C("Trap sense", "10% less damage from traps per level", "Shared by Survival and Gathering.", NoRef),
        C("Scavenger", "3% more coins from enemies it kills per level", "Shared by Gathering and Combat.", "A Lucky Coin raises a player's coin drops."),
    };

    public static readonly Node[] Nodes;
    /// <summary>-1 is the centre, which is always open.</summary>
    public static readonly Edge[] Edges;
    public static readonly LaneLabel[] Labels;
    /// <summary>The smallest distance between any two node centres, in graph units; it must exceed a node's diameter.</summary>
    public static readonly float NearestPair;
    public static int FirstShared => Lanes.Length * Roles;

    /// <summary>A weapon slot is a Combat diamond: it opens a slot and takes one level.</summary>
    public static bool IsWeaponSlot(Node node) => node.Lane == 0 && node.Kind == NodeKind.Diamond;

    /// <summary>
    /// What a node's panel says at a learned level: an even node its per-level effect; an uneven node the line for the
    /// next level to learn, or its last line once full.
    /// </summary>
    public static string NextLevelLine(int node, int level)
    {
        NodeContent content = Nodes[node].Content;
        return content.LevelLines is { } lines ? lines[Math.Clamp(level, 0, lines.Length - 1)] : content.Effect;
    }

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
                    Array.IndexOf(DiamondRoles, role) >= 0 ? NodeKind.Diamond : NodeKind.Circle, needs));
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
