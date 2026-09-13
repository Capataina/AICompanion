#nullable enable

using System;
using System.Collections.Generic;

namespace AICompanion.Tools.SessionReport;

/// <summary>A run of consecutive rows, both ends included.</summary>
public readonly record struct Stretch(int Start, int End)
{
    public int Length => End - Start + 1;
}

/// <summary>
/// Almost every finding in this tool is "this condition held for long enough to matter", because a
/// single tick of anything is noise in a sixty-a-second record: a body is stationary for one tick
/// between two steps, a shot is unsolved for one tick while the target crosses a wall. So the
/// checks state a condition and a minimum length, and this turns that into the stretches that met
/// it. The gap allowance exists because a real condition flickers — a stuck body twitches a pixel,
/// a threatened stretch has one tick with no reachable enemy — and a run broken by one row is two
/// findings that each fall under the threshold, which is how a defect hides from its own check.
/// </summary>
public static class FindStretches
{
    public static List<Stretch> Where(int count, Func<int, bool> holds, int minLength, int allowGap = 0)
    {
        var found = new List<Stretch>();
        int start = -1, lastTrue = -1;
        for (int i = 0; i < count; i++)
        {
            if (holds(i))
            {
                if (start < 0)
                    start = i;
                lastTrue = i;
            }
            else if (start >= 0 && i - lastTrue > allowGap)
            {
                if (lastTrue - start + 1 >= minLength)
                    found.Add(new Stretch(start, lastTrue));
                start = -1;
            }
        }
        if (start >= 0 && lastTrue - start + 1 >= minLength)
            found.Add(new Stretch(start, lastTrue));
        return found;
    }

    /// <summary>The largest of a column's values over a stretch, ignoring the cells that hold no number.</summary>
    public static float Max(Column column, Stretch stretch)
    {
        float best = float.NaN;
        for (int i = stretch.Start; i <= stretch.End; i++)
        {
            float v = column.Number[i];
            if (!float.IsNaN(v) && (float.IsNaN(best) || v > best))
                best = v;
        }
        return best;
    }

    /// <summary>The mean of a column's values over a stretch, ignoring the cells that hold no number.</summary>
    public static float Mean(Column column, Stretch stretch)
    {
        double sum = 0;
        int n = 0;
        for (int i = stretch.Start; i <= stretch.End; i++)
        {
            float v = column.Number[i];
            if (float.IsNaN(v) || float.IsInfinity(v))
                continue;
            sum += v;
            n++;
        }
        return n == 0 ? float.NaN : (float)(sum / n);
    }

    /// <summary>How often each distinct value of a text column appears over a stretch, most common first.</summary>
    public static List<KeyValuePair<string, int>> Tally(Column column, Stretch stretch)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = stretch.Start; i <= stretch.End; i++)
            counts[column.Text[i]] = counts.GetValueOrDefault(column.Text[i]) + 1;
        var list = new List<KeyValuePair<string, int>>(counts);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        return list;
    }

    /// <summary>A tally rendered as <c>value×count</c>, the busiest few first, for a finding's detail line.</summary>
    public static string Summarise(Column column, Stretch stretch, int take = 4)
    {
        var list = Tally(column, stretch);
        var parts = new List<string>();
        for (int i = 0; i < list.Count && i < take; i++)
            parts.Add($"{list[i].Key}×{list[i].Value}");
        return string.Join(", ", parts);
    }
}
