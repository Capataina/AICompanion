#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;

/// <summary>
/// One scenario block reflected left to right: the tiles, the actors, the player's trail and the
/// recorded body box. Nothing about a world's physics prefers a direction, so a route the planner
/// proves one way it must prove the other, and a block that walks only one way is an asymmetry in
/// our code rather than a fact about the terrain. That is the metamorphic relation the corpus is
/// doubled by, and it costs one transform instead of a second capture.
///
/// It runs on this corpus and not on the native suite, which is deliberate and is the plan's rule:
/// the one mirror relation implemented over there is the intermittent fixture, and a relation
/// checked against an oracle that disagrees with itself under load tests the oracle.
///
/// The reflection is a pure function of the block's text. It happens before <see
/// cref="TextTileWorld.Parse"/> rather than after, because the parser is the only thing that reads
/// this format and a mirror applied to the parsed world would have to reimplement the marker,
/// trail and header rules it already owns.
/// </summary>
internal static class MirrorScenarioWorlds
{
    /// <summary>
    /// The three glyph pairs that carry a direction. A floor slope rising to the right rises to the
    /// left in the reflection; a ceiling slope solid on one side is solid on the other. Every other
    /// glyph — solid, platform, half block, the two liquids, air and each marker — is symmetric and
    /// survives the reflection unchanged, which is why they are absent from this switch rather than
    /// listed as identities.
    /// </summary>
    internal static char MirrorGlyph(char c) => c switch
    {
        '\\' => '/',
        '/' => '\\',
        '(' => ')',
        ')' => '(',
        '<' => '>',
        '>' => '<',
        _ => c,
    };

    /// <summary>
    /// A tile column reflected inside the window it was captured in. The axis is the window's own
    /// span, so the reflection lands in the same window and every recorded coordinate stays inside
    /// the same bounds — which is what lets the mirrored block keep the original's header.
    /// </summary>
    internal static int MirrorTile(int x, int originX, int width) => 2 * originX + width - 1 - x;

    /// <summary>
    /// A pixel left edge reflected. A tile is a point and a body is a box, so the box's *left* edge
    /// becomes the reflection of its *right* edge: without the width the body lands one box to the
    /// side of where it stood, which is a whole tile and a bit on a twenty-pixel companion.
    /// </summary>
    internal static float MirrorLeft(float left, float boxWidth, int originX, int width)
        => (2 * originX + width) * 16f - left - boxWidth;

    /// <summary>
    /// The block, reflected. Returns a new list; the input is not touched, because the caller runs
    /// the original immediately before or after and a shared list would hand it the reflection.
    /// </summary>
    internal static List<string> Mirror(IReadOnlyList<string> block)
    {
        (int originX, int width) = Bounds(block);
        var mirrored = new List<string>(block.Count);
        foreach (string raw in block)
        {
            string line = raw.TrimEnd('\r');
            if (ReplayOneBlock.IsHeaderLine(line))
                mirrored.Add(MirrorHeader(line, originX, width));
            else if (ReplayOneBlock.IsExtraLine(line))
                mirrored.Add(MirrorExtra(line, originX, width));
            else
                mirrored.Add(MirrorRow(line, width));
        }
        return mirrored;
    }

    /// <summary>
    /// The window the reflection turns about: the origin the header names and the width the rows
    /// actually have. The width is taken from the rows rather than from the header's own
    /// <c>window x A..B</c>, because <see cref="TextTileWorld"/> sizes the world from the longest
    /// row and pads the rest with solid — so a header claiming a wider span than its rows carry
    /// would put the axis outside the world every search can actually see.
    /// </summary>
    internal static (int originX, int width) Bounds(IReadOnlyList<string> block)
    {
        int originX = 0, width = 0;
        foreach (string raw in block)
        {
            string line = raw.TrimEnd('\r');
            if (ReplayOneBlock.IsHeaderLine(line))
            {
                int at = line.IndexOf("window x ", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string[] parts = line[(at + 9)..].Split(new[] { "..", " y ", " " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[0], out int ox))
                        originX = ox;
                }
                continue;
            }
            if (ReplayOneBlock.IsExtraLine(line))
                continue;
            width = Math.Max(width, line.Length);
        }
        return (originX, width);
    }

    /// <summary>
    /// One row of glyphs, padded to the block's width before it is reversed. The padding is the
    /// part that is easy to miss and impossible to see afterwards: the parser fills a short row's
    /// tail with solid, so reversing the row as written moves that wall from the right-hand edge to
    /// the left-hand one and the reflection is of a different world.
    /// </summary>
    internal static string MirrorRow(string row, int width)
    {
        var padded = new StringBuilder(width);
        for (int x = 0; x < width; x++)
            padded.Append(x < row.Length ? row[x] : '#');
        var flipped = new StringBuilder(width);
        for (int x = width - 1; x >= 0; x--)
            flipped.Append(MirrorGlyph(padded[x]));
        return flipped.ToString();
    }

    /// <summary>
    /// The header's recorded positions, reflected in place. Only the four tile keys and the body box
    /// are rewritten and the prose around them is left exactly as it was: the description is what a
    /// person reads to know which failure this block is, and a transform that edited it would make
    /// the reflection unidentifiable. <c>window x A..B</c> is unchanged because the reflection lands
    /// in the same window by construction.
    /// </summary>
    internal static string MirrorHeader(string header, int originX, int width)
    {
        string line = header;
        foreach (string key in new[] { "start", "goal", "npc", "player" })
            line = ReplaceTile(line, key, originX, width);
        return ReplaceBox(line, originX, width);
    }

    private static string ReplaceTile(string line, string key, int originX, int width)
    {
        int at = line.IndexOf(key + " ", StringComparison.Ordinal);
        if (at < 0)
            return line;
        int valueStart = at + key.Length + 1;
        int valueEnd = line.IndexOf(' ', valueStart);
        if (valueEnd < 0) valueEnd = line.Length;
        string value = line[valueStart..valueEnd];
        string[] xy = value.Split(',');
        if (xy.Length != 2 || !int.TryParse(xy[0], out int x) || !int.TryParse(xy[1], out int y))
            return line;
        return line[..valueStart] + $"{MirrorTile(x, originX, width)},{y}" + line[valueEnd..];
    }

    private static string ReplaceBox(string line, int originX, int width)
    {
        const string Key = "npcbox ";
        int at = line.IndexOf(Key, StringComparison.Ordinal);
        if (at < 0)
            return line;
        int valueStart = at + Key.Length;
        int valueEnd = line.IndexOf(' ', valueStart);
        if (valueEnd < 0) valueEnd = line.Length;
        string[] parts = line[valueStart..valueEnd].Split(',');
        if (parts.Length < 2
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float left)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float bottom))
            return line;
        // The recorded width, never a constant: the box is written by the recorder and a hard-coded
        // twenty is a claim about a body this tool does not own.
        float boxWidth = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) ? w : 0f;
        var rebuilt = new List<string>
        {
            MirrorLeft(left, boxWidth, originX, width).ToString("0.0", CultureInfo.InvariantCulture),
            bottom.ToString("0.0", CultureInfo.InvariantCulture),
        };
        rebuilt.AddRange(parts.Skip(2));
        return line[..valueStart] + string.Join(',', rebuilt) + line[valueEnd..];
    }

    /// <summary>
    /// A <c>markers</c> or <c>trail</c> line, reflected pair by pair. Both are lists of tiles and
    /// both are authoritative over the grid — the markers line exists precisely so an actor never
    /// has to overwrite the shape it stands on — so a reflection that moved the tiles and not these
    /// would put the start on the wrong side of its own world.
    /// </summary>
    internal static string MirrorExtra(string line, int originX, int width)
    {
        if (line.StartsWith("markers ", StringComparison.Ordinal))
        {
            string[] parts = line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rebuilt = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i + 1 < parts.Length && parts[i].Length == 1 && TryMirrorPair(parts[i + 1], originX, width, out string moved))
                {
                    rebuilt.Add(parts[i]);
                    rebuilt.Add(moved);
                    i++;
                }
                else
                    rebuilt.Add(parts[i]);
            }
            return "markers " + string.Join(' ', rebuilt);
        }
        if (line.StartsWith("trail ", StringComparison.Ordinal))
            return "trail " + string.Join(' ', line[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => TryMirrorPair(pair, originX, width, out string moved) ? moved : pair));
        // companion, player and threat lines carry no tile list in any committed block. They are
        // passed through unchanged rather than guessed at, because a transform that reflected a
        // format it had never seen would be inventing a world.
        return line;
    }

    private static bool TryMirrorPair(string pair, int originX, int width, out string mirrored)
    {
        mirrored = pair;
        string[] xy = pair.Split(',');
        if (xy.Length != 2 || !int.TryParse(xy[0], out int x) || !int.TryParse(xy[1], out int y))
            return false;
        mirrored = $"{MirrorTile(x, originX, width)},{y}";
        return true;
    }
}
