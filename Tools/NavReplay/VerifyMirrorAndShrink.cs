#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// The corpus tools checking themselves, because everything they report rests on a transform nobody
/// can eyeball. A mirrored corpus is only evidence about the planner if the reflection is exact:
/// get the axis wrong by one column, or forget that a short row is padded with solid on the right,
/// and every scenario disagrees with itself and the harness reports a left-right asymmetry in the
/// movement core that is really a bug in this folder.
///
/// So the first case is the algebraic one — reflecting twice returns the original, on every block in
/// the committed corpus, compared as text. It is the check that cannot be satisfied by a plausible
/// transform: an axis off by one, a dropped glyph flip and an unpadded row all survive a single
/// mirror and none of them survives two.
/// </summary>
internal static class VerifyMirrorAndShrink
{
    public static int Run()
    {
        int failures = 0;
        failures += MirroringTwiceIsIdentity();
        failures += ADirectionalGlyphLandsMirrored();
        failures += APaddedRowKeepsItsWallOnTheOutside();
        failures += ASymmetricWorldWalksBothWays();
        failures += DdminKeepsWhatTheFailureNeeds();
        failures += AShrunkFileReproducesItsRecordedSignature();
        if (failures == 0)
            Console.WriteLine("mirror and shrink: double reflection is identity over the committed corpus, slope glyphs and short-row padding reflect, a symmetric world walks both ways, ddmin is 1-minimal and budget-honouring, and a shrunk file replays its own signature");
        return failures;
    }

    private static int Fail(string what)
    {
        Console.WriteLine($"   mirror/shrink: {what}");
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
            foreach ((int index, List<string> block) in ReplayOneBlock.Blocks(File.ReadAllLines(file)))
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
    /// A world that is its own reflection must give the same verdict both ways. This is the weakest
    /// of the six and it is here for one reason: it exercises the whole path — transform, parse,
    /// plan, flood — so a transform that produces a block the parser rejects is caught as a verdict
    /// difference rather than as a silent skip.
    /// </summary>
    private static int ASymmetricWorldWalksBothWays()
    {
        var block = new List<string>
        {
            "tick 0 a flat floor: start 2,2 goal 6,2 expansions 0 npc 2,2 player 6,2 window x 0..8 y 0..3",
            ".........",
            ".........",
            ".........",
            "#########",
        };
        ReplayOneBlock.Outcome plain = ReplayOneBlock.Evaluate(block, "symmetry-plain", false, (0, -1));
        ReplayOneBlock.Outcome mirrored = ReplayOneBlock.Evaluate(MirrorScenarioWorlds.Mirror(block), "symmetry-mirrored", false, (0, -1));
        if (plain.Class != "PASS")
            return Fail($"a flat floor did not walk at all, so the comparison tests nothing: {plain.Class} {plain.Skip}");
        if (plain.Class != mirrored.Class)
            return Fail($"a left-right symmetric world gave {plain.Class} one way and {mirrored.Class} the other");
        if (plain.Main.Path?.Steps.Count != mirrored.Main.Path?.Steps.Count)
            return Fail($"a symmetric world planned {plain.Main.Path?.Steps.Count} steps one way and {mirrored.Main.Path?.Steps.Count} the other");
        return 0;
    }

    /// <summary>
    /// ddmin over a set with one known-necessary member. The test says the failure survives exactly
    /// when that member is kept, so a correct reducer returns it alone; a reducer that stops at its
    /// first successful chunk returns more, and one that ignores its test returns everything.
    /// </summary>
    private static int DdminKeepsWhatTheFailureNeeds()
    {
        var elements = Enumerable.Range(0, 32).ToList();
        int calls = 0;
        List<int> kept = ShrinkFailingScenario.Ddmin(elements, subset => { calls++; return subset.Contains(17); }, () => false);
        if (kept.Count != 1 || kept[0] != 17)
            return Fail($"ddmin kept {kept.Count} elements ({string.Join(",", kept.Take(8))}) where one was needed");
        if (calls == 0)
            return Fail("ddmin returned an answer without consulting its test");
        // The budget is honoured: with the test refused from the first call, nothing may be dropped.
        List<int> unbudgeted = ShrinkFailingScenario.Ddmin(elements, _ => true, () => true);
        if (unbudgeted.Count != elements.Count)
            return Fail($"ddmin reduced {elements.Count - unbudgeted.Count} elements after its budget was spent");
        return 0;
    }

    /// <summary>
    /// A shrunk file, re-read through the ordinary path, still fails the way the reducer recorded.
    /// This is the case that makes the whole tool worth having: a reduction whose output cannot be
    /// replayed has produced a smaller file and no smaller finding, and the header's signature would
    /// be a claim nobody checked.
    /// </summary>
    private static int AShrunkFileReproducesItsRecordedSignature()
    {
        // A pit with no way out and a goal on the far rim: the flood closes inside the window, which
        // is a SEALED verdict, and a scenario twice the size around it reduces to roughly this.
        var block = new List<string>
        {
            "tick 0 a pit the body cannot climb out of: start 2,8 goal 13,8 expansions 0 npc 2,8 player 13,8 window x 0..15 y 0..9",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "..........#.....",
            "###.......#.####",
        };
        string folder = Path.Combine(Path.GetTempPath(), "aic-navreplay-shrink-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "pit.txt");
        try
        {
            File.WriteAllLines(path, block);
            ReplayOneBlock.Outcome before = ReplayOneBlock.Evaluate(block, "pit", false, (0, -1));
            if (before.Class == "PASS")
                return Fail("the shrink fixture walks, so there is no failure to preserve and the case tests nothing");
            ShrinkFailingScenario.Result? result = ShrinkFailingScenario.Run(path, follow: false, budget: 400, _ => { });
            if (result is not ShrinkFailingScenario.Result shrunk)
                return Fail("the reducer found no failing block in a scenario that fails");
            string[] written = File.ReadAllLines(shrunk.Path);
            var blocks = ReplayOneBlock.Blocks(written).ToList();
            if (blocks.Count != 1)
                return Fail($"the shrunk file parsed as {blocks.Count} blocks rather than one");
            ReplayOneBlock.Outcome after = ReplayOneBlock.Evaluate(blocks[0].Item2, "pit-shrunk", false, (0, -1));
            if (after.Signature != shrunk.Signature)
                return Fail($"the shrunk file does not reproduce its own recorded signature: wrote {shrunk.Signature}, replays as {after.Signature}");
            // The size that must not grow is the window, not the tile count: walling a row the air
            // pass could not empty makes the row uniform and legitimately raises the count of
            // non-air tiles, while a window that grew would mean a reduction added world.
            if (shrunk.AfterWidth * shrunk.AfterHeight > shrunk.BeforeWidth * shrunk.BeforeHeight)
                return Fail($"the reduction grew the window from {shrunk.BeforeWidth}x{shrunk.BeforeHeight} to {shrunk.AfterWidth}x{shrunk.AfterHeight}");
            // The trim pass must have been reachable. It is last and it is the reduction a reader
            // actually feels, so a budget that never reaches it is the defect the reserve exists to
            // stop, and a case that did not check it would not have caught the first run doing
            // exactly that.
            if (shrunk.AfterWidth * shrunk.AfterHeight == shrunk.BeforeWidth * shrunk.BeforeHeight && !shrunk.BudgetSpent)
                return Fail("the window was never trimmed although the budget was not spent, so the trim pass is unreachable");
            return 0;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }
}
