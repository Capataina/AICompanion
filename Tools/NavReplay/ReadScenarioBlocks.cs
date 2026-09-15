#nullable enable

using System;
using System.Collections.Generic;

/// <summary>
/// A scenario file is one or more blocks, each a header line, optional entity and marker lines,
/// and then the rows of the tile window, separated by blank lines. This is the one reader of
/// that shape in the tool, so the mirror and the extractor agree on where a block starts.
/// </summary>
internal static class ReadScenarioBlocks
{
    internal static bool IsHeaderLine(string line)
        => line.StartsWith("tick ", StringComparison.Ordinal) || line.StartsWith("scenario ", StringComparison.Ordinal);

    internal static bool IsExtraLine(string line)
        => line.StartsWith("companion ", StringComparison.Ordinal) || line.StartsWith("player ", StringComparison.Ordinal)
        || line.StartsWith("threat ", StringComparison.Ordinal) || line.StartsWith("trail ", StringComparison.Ordinal)
        || line.StartsWith("markers ", StringComparison.Ordinal);

    internal static IEnumerable<(int, List<string>)> Blocks(string[] lines)
    {
        var block = new List<string>();
        int index = 0;
        bool inRows = false;
        foreach (string line in lines)
        {
            if (line.Length == 0)
            {
                if (inRows && block.Count > 0)
                {
                    yield return (index++, block);
                    block = new List<string>();
                    inRows = false;
                }
                continue;
            }
            block.Add(line);
            if (!IsHeaderLine(line) && !IsExtraLine(line))
                inRows = true;
        }
        if (block.Count > 0)
            yield return (index, block);
    }
}
