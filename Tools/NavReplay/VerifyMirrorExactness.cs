#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The corpus mirror checking itself, because everything reported over a mirrored corpus rests on
/// a transform nobody can eyeball. A mirrored corpus is only evidence about the planner if the
/// reflection is exact: get the axis wrong by one column, or forget that a short row is padded
/// with solid on the right, and every scenario disagrees with itself and the harness reports a
/// left-right asymmetry in the movement core that is really a bug in this folder.
///
/// So the first case is the algebraic one — reflecting twice returns the original, on every block in
/// the committed corpus, compared as text. It is the check that cannot be satisfied by a plausible
/// transform: an axis off by one, a dropped glyph flip and an unpadded row all survive a single
/// mirror and none of them survives two.
/// </summary>
internal static class VerifyMirrorExactness
{
    public static int Run()
    {
        int failures = 0;
        failures += MirroringTwiceIsIdentity();
        failures += ADirectionalGlyphLandsMirrored();
        failures += APaddedRowKeepsItsWallOnTheOutside();
        failures += ASymmetricWorldFloodsBothWays();
        if (failures == 0)
            Console.WriteLine("mirror: double reflection is identity over the committed corpus, slope glyphs and short-row padding reflect, and a symmetric world floods the same both ways");
        return failures;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   mirror: {what}");
        return 1;
    }

    /// <summary>
    /// Mirror twice, get the original back, for every block of every committed scenario. Compared as
    /// text after one normalising pass through the mirror, because the first reflection pads short
    /// rows and the second cannot unpad them — so the fixed point is reached after one application
    /// rather than at the raw file, and comparing against the raw file would report a padding
    /// difference as a broken reflection.
    /// </summary>
    private static int MirroringTwiceIsIdentity()
    {
        string folder = Path.Combine("Tools", "Scenarios");
        if (!Directory.Exists(folder))
            return Fail($"the corpus folder {folder} is not there, so the identity was never checked");
        int blocks = 0, changed = 0;
        foreach (string file in Directory.GetFiles(folder, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach ((int index, List<string> block) in ReadScenarioBlocks.Blocks(File.ReadAllLines(file)))
            {
                blocks++;
                List<string> once = MirrorScenarioWorlds.Mirror(block);
                // A transform that returned its input unchanged would satisfy the double-mirror
                // identity perfectly, so the identity is only evidence beside this: at least one
                // committed block must actually come back different.
                if (!once.SequenceEqual(block.Select(l => l.TrimEnd('\r'))))
                    changed++;
                List<string> thrice = MirrorScenarioWorlds.Mirror(MirrorScenarioWorlds.Mirror(once));
                if (once.Count != thrice.Count)
                    return Fail($"{Path.GetFileName(file)}#{index} changed line count under a double mirror: {once.Count} then {thrice.Count}");
                for (int i = 0; i < once.Count; i++)
                    if (once[i] != thrice[i])
                        return Fail($"{Path.GetFileName(file)}#{index} line {i} is not its own double reflection:\n     once  {once[i]}\n     twice {thrice[i]}");
            }
        }
        if (blocks == 0)
            return Fail("no corpus block was read, so the identity proved nothing");
        return changed == 0 ? Fail($"all {blocks} corpus blocks came back identical, so the transform reflects nothing") : 0;
    }

    /// <summary>
    /// One floor slope in a known column, reflected. The axis and the glyph flip are checked
    /// together against arithmetic done by hand, because the double-mirror identity above holds just
    /// as well for a transform that reflects about the wrong column consistently.
    /// </summary>
    private static int ADirectionalGlyphLandsMirrored()
    {
        // Origin 100, width 6. A '/' at local column 1 must land at local column 4 as '\'.
        var block = new List<string>
        {
            "tick 0 a slope on the left: start 101,1 goal 104,1 npc 101,1 player 104,1 window x 100..105 y 0..1",
            ".#....",
            "./####",
        };
        List<string> mirrored = MirrorScenarioWorlds.Mirror(block);
        if (mirrored[2] != "####\\.")
            return Fail($"a floor slope did not reflect: expected ####\\. and got {mirrored[2]}");
        if (mirrored[1] != "....#.")
            return Fail($"a solid tile did not reflect: expected ....#. and got {mirrored[1]}");
        // 2*100 + 6 - 1 - 101 = 104 and 2*100 + 6 - 1 - 104 = 101, so the two swap.
        if (!mirrored[0].Contains("start 104,1") || !mirrored[0].Contains("goal 101,1"))
            return Fail($"the header's recorded tiles did not reflect: {mirrored[0]}");
        if (MirrorScenarioWorlds.MirrorGlyph('<') != '>' || MirrorScenarioWorlds.MirrorGlyph('(') != ')')
            return Fail("a directional glyph pair is missing from the flip");
        if (MirrorScenarioWorlds.MirrorGlyph('_') != '_' || MirrorScenarioWorlds.MirrorGlyph('=') != '=')
            return Fail("a symmetric glyph was altered by the flip");
        return 0;
    }

    /// <summary>
    /// A short row is padded with solid on the right by the parser, so its reflection must carry
    /// that wall on the left. Written because the unpadded reversal is the natural implementation
    /// and it is wrong in a way no verdict would ever name: it silently moves a wall across the
    /// window.
    /// </summary>
    private static int APaddedRowKeepsItsWallOnTheOutside()
    {
        string mirrored = MirrorScenarioWorlds.MirrorRow("..", 5);
        if (mirrored != "###..")
            return Fail($"a short row's padding did not reflect: expected ###.. and got {mirrored}");
        return 0;
    }

    /// <summary>
    /// A world that is its own reflection must flood the same both ways: the same number of usable
    /// corners from the start, and the goal reached in both. This is the weakest of the four and it
    /// is here for one reason: it exercises the whole path — transform, parse, flood — so a
    /// transform that produces a block the parser rejects is caught as a verdict difference rather
    /// than as a silent skip.
    /// </summary>
    private static int ASymmetricWorldFloodsBothWays()
    {
        var block = new List<string>
        {
            "tick 0 a room: start 2,2 goal 6,2 expansions 0 npc 2,2 player 6,2 window x 0..8 y 0..4",
            "#########",
            "#.......#",
            "#.......#",
            "#.......#",
            "#########",
        };
        (int reached, bool goal) plain = Flood(block);
        (int reached, bool goal) mirrored = Flood(MirrorScenarioWorlds.Mirror(block));
        if (!plain.goal)
            return Fail("a room's flood did not reach its goal at all, so the comparison tests nothing");
        if (plain.goal != mirrored.goal || plain.reached != mirrored.reached)
            return Fail($"a left-right symmetric world flooded {plain.reached} corners and goal={plain.goal} one way, {mirrored.reached} and goal={mirrored.goal} the other");
        return 0;
    }

    private static (int reached, bool goal) Flood(IReadOnlyList<string> block)
    {
        TextTileWorld world = TextTileWorld.Parse(block, out string header, out _);
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
        try
        {
            // The header names the start and the goal outright, as every dump does; the grid carries no marker glyphs here.
            Point start = HeaderTile(header, "start "), goal = HeaderTile(header, "goal ");
            Point? root = CornerGraph.NearestUsable(world, MovementQueries.TileCentre(start), 2, requireSweep: false);
            if (root == null) return (0, false);
            var flood = new FreeSpaceSearch(world, root.Value, null);
            flood.Advance(int.MaxValue);
            bool reachedGoal = CornerGraph.AnyCornerOf(goal, corner => flood.Reached.Contains(corner));
            return (flood.Reached.Count, reachedGoal);
        }
        finally
        {
            FreeSpaceSearch.WorldOverride = null;
            ClearanceField.Shared.Invalidate();
        }
    }

    private static Point HeaderTile(string header, string key)
    {
        int at = header.IndexOf(key, StringComparison.Ordinal);
        string[] xy = header[(at + key.Length)..].Split(' ')[0].Split(',');
        return new Point(int.Parse(xy[0]), int.Parse(xy[1]));
    }
}
