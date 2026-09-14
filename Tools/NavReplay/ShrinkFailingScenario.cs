#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// A failing scenario cut down to the smallest window that still fails the same way.
///
/// The algorithm is Zeller and Hildebrandt's ddmin (TSE 2002): split the input into chunks, test
/// each chunk and each complement, and narrow until every remaining element is needed. What Regehr
/// et al. (PLDI 2012) add, and what this file takes from them, is that the reduction steps have to
/// be shaped like the thing being reduced — so the elements here are rows, tiles, trail entries and
/// window edges rather than lines of text, and a reduction that produced a scenario the parser
/// could not read would be a wasted test rather than a smaller case.
///
/// **The rule that makes it a reduction rather than a search for a different bug: the signature is
/// computed once from the original and every test is equality against it.** A cut that turns a
/// mislanding into a timeout, or moves the first refused trail tile, has found a second finding and
/// is rejected — a smaller scenario failing differently is not this scenario, smaller.
///
/// The corpus's own validity rule is inherited whole and is why two obvious transforms are absent.
/// A fixture is never altered to make a move succeed, so nothing here may fill a tile with support
/// that was not there. And an interior column is never deleted, only edge-trimmed: deleting one
/// renumbers every tile to its right, so the shrunk file's coordinates would stop naming the same
/// places as the capture it came from, and a fixture whose tiles cannot be pointed at in the world
/// is a fixture nobody can check against the world.
/// </summary>
internal static class ShrinkFailingScenario
{
    /// <summary>
    /// A scenario block held apart so a transform can work on it: the header, the non-tile lines and
    /// the grid, with the window it declares. Rows are kept padded to the full width throughout, so
    /// every transform indexes the same rectangle the parser will read.
    /// </summary>
    internal sealed class Block
    {
        public string Header = "";
        public List<string> Extras = new();
        public List<char[]> Rows = new();
        public int OriginX, OriginY;
        public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
        public int Height => Rows.Count;

        public Block Copy() => new()
        {
            Header = Header,
            Extras = new List<string>(Extras),
            Rows = Rows.Select(r => (char[])r.Clone()).ToList(),
            OriginX = OriginX,
            OriginY = OriginY,
        };

        public List<string> Lines()
        {
            var lines = new List<string> { Header };
            lines.AddRange(Extras);
            lines.AddRange(Rows.Select(r => new string(r)));
            return lines;
        }

        public int Tiles => Rows.Sum(r => r.Count(c => c != '.'));
    }

    internal static Block Read(IReadOnlyList<string> block)
    {
        var model = new Block();
        (int originX, int width) = MirrorScenarioWorlds.Bounds(block);
        model.OriginX = originX;
        var rows = new List<string>();
        foreach (string raw in block)
        {
            string line = raw.TrimEnd('\r');
            if (ReplayOneBlock.IsHeaderLine(line))
            {
                model.Header = line;
                int at = line.IndexOf("window x ", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string[] parts = line[(at + 9)..].Split(new[] { "..", " y ", " " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 2 && int.TryParse(parts[2], out int oy))
                        model.OriginY = oy;
                }
            }
            else if (ReplayOneBlock.IsExtraLine(line))
                model.Extras.Add(line);
            else
                rows.Add(line);
        }
        // Padded once, here, with the same solid the parser would have padded with. Every transform
        // below then works on a true rectangle and a trim can take a column off either edge without
        // first asking whether some row was short of it.
        foreach (string row in rows)
            model.Rows.Add(Enumerable.Range(0, width).Select(x => x < row.Length ? row[x] : '#').ToArray());
        return model;
    }

    /// <summary>The tiles no transform may touch: everywhere an actor stands and everywhere the player's feet were recorded.</summary>
    private static HashSet<Point> Protected(Block block)
    {
        var keep = new HashSet<Point>();
        foreach (string extra in block.Extras)
        {
            if (extra.StartsWith("markers ", StringComparison.Ordinal))
            {
                string[] parts = extra[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < parts.Length; i += 2)
                    if (Parse(parts[i + 1]) is Point m) keep.Add(m);
            }
            else if (extra.StartsWith("trail ", StringComparison.Ordinal))
                foreach (string pair in extra[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (Parse(pair) is Point t) keep.Add(t);
        }
        foreach (string key in new[] { "start", "goal", "npc", "player" })
            if (ReplayOneBlock.HeaderPoint(block.Header, key) is Point h) keep.Add(h);
        return keep;
    }

    private static Point? Parse(string pair)
    {
        string[] xy = pair.Split(',');
        return xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y) ? new Point(x, y) : null;
    }

    /// <summary>What a run of the reducer did, for the row it writes and the report it prints.</summary>
    internal readonly record struct Result(
        string Signature, string Path, int BeforeTiles, int AfterTiles,
        int BeforeWidth, int BeforeHeight, int AfterWidth, int AfterHeight,
        int BeforeTrail, int AfterTrail, int OracleCalls, bool BudgetSpent, string Transforms);

    /// <summary>
    /// Reduce the first failing block of <paramref name="path"/> and write the result beside it.
    /// Returns null when no block in the file fails, which is refused rather than minimised: a
    /// passing scenario has no signature to preserve and ddmin over it would reduce until the
    /// oracle's answer changed for reasons nobody asked about.
    /// </summary>
    internal static Result? Run(string path, bool follow, int budget, Action<string> say)
    {
        string[] lines = File.ReadAllLines(path);
        foreach ((int index, List<string> raw) in ReplayOneBlock.Blocks(lines))
        {
            ReplayOneBlock.Outcome first = ReplayOneBlock.Evaluate(raw, $"{Path.GetFileName(path)}#{index}", follow, (0, -1));
            bool failing = first.Failed || first.Sealed || first.Follow is FollowOutcome.Parked;
            if (!failing)
                continue;
            return Reduce(path, index, raw, first.Signature, follow, budget, say);
        }
        return null;
    }

    private static Result Reduce(string path, int index, List<string> raw, string signature, bool follow, int budget, Action<string> say)
    {
        Block block = Read(raw);
        HashSet<Point> keep = Protected(block);
        int before = block.Tiles, beforeWidth = block.Width, beforeHeight = block.Height;
        int beforeTrail = TrailLength(block);
        int calls = 0;
        var applied = new List<string>();

        bool Holds(Block candidate)
        {
            calls++;
            try
            {
                return ReplayOneBlock.Evaluate(candidate.Lines(), "shrink-probe", follow, (0, -1)).Signature == signature;
            }
            catch (Exception)
            {
                // A reduction that breaks the world badly enough to throw has not preserved the
                // failure, which is the only question being asked. It is a rejected candidate rather
                // than a crash of the reducer, and swallowing it here is the difference between a
                // reduction run that finishes and one that dies three transforms in.
                return false;
            }
        }
        bool Spent() => calls >= budget;

        // The interior passes may spend only part of the budget, and the reserve is not a detail.
        // Run without one, the row and tile passes consumed every call and the window trim — the
        // reduction that actually makes a scenario small enough to read — never ran at all: the
        // first real run came back 202x201 wide having "reduced" nothing a reader would notice.
        int interior = Math.Max(1, budget * 7 / 10);
        bool InteriorSpent() => calls >= interior;

        // Pass one: whole rows emptied. A window cut out of a cave carries most of its terrain above
        // and below the move that failed, and emptying a band of rows removes it in one test where a
        // tile-at-a-time pass would pay a test per tile.
        List<int> allRows = Enumerable.Range(0, block.Height).ToList();
        List<int> keptRows = Ddmin(allRows, subset => Holds(WithRowsFilled(block, allRows.Except(subset), '.', keep)), InteriorSpent);
        var emptied = allRows.Except(keptRows).ToList();
        if (emptied.Count > 0)
        {
            block = WithRowsFilled(block, emptied, '.', keep);
            applied.Add($"{emptied.Count} row-to-air");
            say($"shrink: {emptied.Count} rows emptied, {calls} oracle calls so far");
        }

        // Pass two: the rows pass one could not empty, walled instead. Restricted to rows that are
        // not already uniform, and that restriction is the whole of it: run over every row, this
        // pass refilled with solid the very rows pass one had just emptied — 194 emptied and 189
        // filled back, on a fixture whose failure cared about neither — so the two passes undid each
        // other and the output was larger than the input. A reduction that can reverse another
        // reduction is not a reduction.
        int walled = 0;
        for (int y = 0; y < block.Height && !InteriorSpent(); y++)
        {
            if (block.Rows[y].All(c => c == '.') || block.Rows[y].All(c => c == '#')) continue;
            Block candidate = WithRowsFilled(block, new[] { y }, '#', keep);
            if (!Holds(candidate)) continue;
            block = candidate;
            walled++;
        }
        if (walled > 0)
        {
            applied.Add($"{walled} row-to-solid");
            say($"shrink: {walled} rows walled, {calls} oracle calls so far");
        }

        // Pass three: single tiles to air. Only to air, never to solid — the corpus's rule is that a
        // fixture is not altered to make a move succeed, and adding support is exactly that.
        if (!InteriorSpent())
        {
            List<Point> tiles = new();
            for (int y = 0; y < block.Height; y++)
                for (int x = 0; x < block.Width; x++)
                    if (block.Rows[y][x] != '.' && !keep.Contains(new Point(block.OriginX + x, block.OriginY + y)))
                        tiles.Add(new Point(x, y));
            List<Point> kept = Ddmin(tiles, subset => Holds(WithTilesCleared(block, tiles.Except(subset))), InteriorSpent);
            var cleared = tiles.Except(kept).ToList();
            if (cleared.Count > 0)
            {
                block = WithTilesCleared(block, cleared);
                applied.Add($"{cleared.Count} tile-to-air");
                say($"shrink: {cleared.Count} tiles emptied, {calls} oracle calls so far");
            }
        }

        // Pass four: the trail's tail. A prefix rather than a subset, deliberately — the trail is the
        // player's feet oldest first, and a list with holes in it is not a route anybody walked, so
        // the shortest prefix that still fails is the honest reduction and a binary search finds it
        // in a handful of tests.
        int trailBefore = TrailLength(block);
        if (!Spent() && trailBefore > 0)
        {
            int low = 0, high = trailBefore;
            while (low < high && !Spent())
            {
                int mid = (low + high) / 2;
                if (Holds(WithTrailPrefix(block, mid))) high = mid; else low = mid + 1;
            }
            if (high < trailBefore)
            {
                block = WithTrailPrefix(block, high);
                applied.Add($"trail {trailBefore}->{high}");
                say($"shrink: trail cut from {trailBefore} to {high}, {calls} oracle calls so far");
            }
        }

        // Pass five: the window's own edges, which is what "remove a column" means here. Trimming
        // from the left or the top moves the origin and the header says so; every surviving tile
        // keeps the coordinate it had in the world, which is the property interior deletion loses.
        var trims = new List<string>();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            int cut = 0;
            while (!Spent())
            {
                Block candidate = Trim(block, side, keep);
                if (candidate.Width < 2 || candidate.Height < 2 || ReferenceEquals(candidate, block) || !Holds(candidate))
                    break;
                block = candidate;
                cut++;
            }
            if (cut > 0) trims.Add($"{side} {cut}");
        }
        if (trims.Count > 0)
        {
            applied.Add("trimmed " + string.Join(", ", trims));
            say($"shrink: window trimmed ({string.Join(", ", trims)}), {calls} oracle calls in total");
        }

        string transforms = applied.Count == 0 ? "nothing survived a reduction" : string.Join("; ", applied);
        block.Header = Provenance(block, path, index, signature, transforms);
        string output = Path.Combine(Path.GetDirectoryName(path) ?? ".",
            Path.GetFileNameWithoutExtension(path) + "-shrunk.txt");
        File.WriteAllLines(output, block.Lines());
        return new Result(signature, output, before, block.Tiles, beforeWidth, beforeHeight, block.Width, block.Height,
            beforeTrail, TrailLength(block), calls, Spent(), transforms);
    }

    /// <summary>
    /// The shrunk file's header: the recorded request rewritten to the new window, then the
    /// provenance at the end of the line. The order is not cosmetic — <c>HeaderPoint</c> takes the
    /// first occurrence of each key, so provenance carrying the word "goal" ahead of the real one
    /// would become the goal. It is put last and stripped of every key token besides.
    /// </summary>
    private static string Provenance(Block block, string path, int index, string signature, string transforms)
    {
        string header = block.Header;
        int at = header.IndexOf("window x ", StringComparison.Ordinal);
        string window = $"window x {block.OriginX}..{block.OriginX + block.Width - 1} y {block.OriginY}..{block.OriginY + block.Height - 1}";
        header = at >= 0 ? header[..at] + window : header + " " + window;
        return header + " || reduced from " + Safe(Path.GetFileName(path)) + "#" + index
            + " by " + Safe(transforms) + " || signature " + Safe(signature);
    }

    /// <summary>Provenance text with every header key token removed, so prose can never be read as a recorded position.</summary>
    private static string Safe(string text)
    {
        foreach (string key in new[] { "start ", "goal ", "npc ", "player ", "npcbox ", "window x " })
            text = text.Replace(key, key.TrimEnd() + "_ ", StringComparison.Ordinal);
        return text;
    }

    private static Block WithRowsFilled(Block block, IEnumerable<int> rows, char fill, HashSet<Point> keep)
    {
        Block copy = block.Copy();
        foreach (int y in rows)
        {
            if (y < 0 || y >= copy.Height) continue;
            for (int x = 0; x < copy.Width; x++)
            {
                // A protected tile keeps whatever it carried: an actor's own feet tile is the support
                // it stands on, and replacing it is the 2026-09-08 defect where a marker erased a
                // half block from underneath the body it belonged to.
                if (keep.Contains(new Point(copy.OriginX + x, copy.OriginY + y))) continue;
                copy.Rows[y][x] = fill;
            }
        }
        return copy;
    }

    private static Block WithTilesCleared(Block block, IEnumerable<Point> tiles)
    {
        Block copy = block.Copy();
        foreach (Point t in tiles)
            if (t.Y >= 0 && t.Y < copy.Height && t.X >= 0 && t.X < copy.Width)
                copy.Rows[t.Y][t.X] = '.';
        return copy;
    }

    private static int TrailLength(Block block)
        => block.Extras.Where(e => e.StartsWith("trail ", StringComparison.Ordinal))
                       .Select(e => e[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                       .FirstOrDefault();

    private static Block WithTrailPrefix(Block block, int count)
    {
        Block copy = block.Copy();
        for (int i = 0; i < copy.Extras.Count; i++)
        {
            if (!copy.Extras[i].StartsWith("trail ", StringComparison.Ordinal)) continue;
            string[] pairs = copy.Extras[i][6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            copy.Extras[i] = "trail " + string.Join(' ', pairs.Take(count));
        }
        return copy;
    }

    /// <summary>
    /// One row or column off one edge, refusing any cut that would take a protected tile with it.
    /// Returns the same instance when the cut is refused, which is how the caller's loop stops.
    /// </summary>
    private static Block Trim(Block block, string side, HashSet<Point> keep)
    {
        bool Holds(int x, int y) => keep.Contains(new Point(x, y));
        switch (side)
        {
            case "left":
                if (Enumerable.Range(0, block.Height).Any(y => Holds(block.OriginX, block.OriginY + y))) return block;
                var l = block.Copy();
                l.Rows = l.Rows.Select(r => r.Skip(1).ToArray()).ToList();
                l.OriginX++;
                return l;
            case "right":
                if (Enumerable.Range(0, block.Height).Any(y => Holds(block.OriginX + block.Width - 1, block.OriginY + y))) return block;
                var r2 = block.Copy();
                r2.Rows = r2.Rows.Select(r => r.Take(r.Length - 1).ToArray()).ToList();
                return r2;
            case "top":
                if (Enumerable.Range(0, block.Width).Any(x => Holds(block.OriginX + x, block.OriginY))) return block;
                var t = block.Copy();
                t.Rows.RemoveAt(0);
                t.OriginY++;
                return t;
            default:
                if (Enumerable.Range(0, block.Width).Any(x => Holds(block.OriginX + x, block.OriginY + block.Height - 1))) return block;
                var b = block.Copy();
                b.Rows.RemoveAt(b.Rows.Count - 1);
                return b;
        }
    }

    /// <summary>
    /// Zeller and Hildebrandt's ddmin, over a set whose members are kept rather than removed:
    /// <paramref name="holds"/> is handed the subset to keep and answers whether the failure
    /// survived reducing everything else. The returned set is 1-minimal — no single member can be
    /// dropped from it without the failure changing — which is the guarantee the algorithm actually
    /// makes, and is weaker than "the smallest possible" by design.
    ///
    /// The budget check is the one addition. ddmin is quadratic in the worst case and a captured
    /// window carries thousands of tiles, so a reduction with no ceiling is a run that never ends;
    /// stopping early returns a larger set honestly rather than a smaller one eventually.
    /// </summary>
    internal static List<T> Ddmin<T>(List<T> elements, Func<List<T>, bool> holds, Func<bool> spent)
    {
        var current = new List<T>(elements);
        int granularity = 2;
        while (current.Count >= 2 && !spent())
        {
            List<List<T>> chunks = Split(current, Math.Min(granularity, current.Count));
            bool reduced = false;
            foreach (List<T> chunk in chunks)
            {
                if (spent()) break;
                if (chunk.Count > 0 && holds(chunk))
                {
                    current = chunk;
                    granularity = 2;
                    reduced = true;
                    break;
                }
            }
            if (reduced) continue;
            foreach (List<T> chunk in chunks)
            {
                if (spent()) break;
                var complement = current.Except(chunk).ToList();
                if (complement.Count > 0 && holds(complement))
                {
                    current = complement;
                    granularity = Math.Max(granularity - 1, 2);
                    reduced = true;
                    break;
                }
            }
            if (reduced) continue;
            if (granularity >= current.Count) break;
            granularity = Math.Min(granularity * 2, current.Count);
        }
        return current;
    }

    private static List<List<T>> Split<T>(List<T> elements, int chunks)
    {
        var split = new List<List<T>>();
        for (int i = 0; i < chunks; i++)
        {
            int from = i * elements.Count / chunks, to = (i + 1) * elements.Count / chunks;
            split.Add(elements.GetRange(from, to - from));
        }
        return split;
    }
}
