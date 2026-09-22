extern alias live;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Infrastructure.Selection.Courses;

/// <summary>
/// What the frozen observation is made of, per decision, written to a file when `AIC_FACT_KINDS` names
/// one and costing nothing when it does not.
///
/// The play-measures verdicts say the observation is *above a bound*; they cannot say what is in it, and
/// that is the difference between "the brain leaks facts" and "the brain looked at a dark cave". The
/// first reading of the play of 0.38.13 — 150 facts growing to 1,603 and never falling — was written up
/// as something that never lets go, and it was wrong: on the replay of the 22 September capture this
/// probe shows 96% of the growth is `light-target`, and the bounding box of those facts tracks the
/// player's intent region exactly, 125 tiles wide, dropping every tile the window leaves behind. What
/// grows is the *darkness fraction* of a fixed window as the player descends, not a set nobody clears.
///
/// The box is the load-bearing half rather than the count. A per-kind count alone cannot separate a
/// census that re-sweeps a moving window from one that accumulates inside it, because both produce a
/// rising number on a descent; the minimum corner moving with the heading is what only the first can do.
/// </summary>
internal static class CountTheFrozenObservationByKind
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("AIC_FACT_KINDS");

    public static bool Wanted => !string.IsNullOrEmpty(Path);

    public static void Write(int tick, DecisionFactSnapshot facts, Point heading)
    {
        if (Path is not { Length: > 0 } destination) return;
        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int low = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue, total = 0;
        foreach (DecisionFact fact in facts.Facts)
        {
            total++;
            byKind[fact.Key.Kind] = byKind.TryGetValue(fact.Key.Kind, out int had) ? had + 1 : 1;
            if (fact.Key.Kind != "light-target") continue;
            int x = (int)(fact.Value.X / 16f), y = (int)(fact.Value.Y / 16f);
            low = Math.Min(low, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        File.AppendAllText(destination, FormattableString.Invariant($"{tick}\t{facts.ObservationOrdinal}\t{total}\t")
            + string.Join(" ", byKind.Select(p => p.Key + "=" + p.Value.ToString(CultureInfo.InvariantCulture)))
            + FormattableString.Invariant($"\tlight-box={low},{top}..{right},{bottom}\thead={heading.X},{heading.Y}\n"));
    }
}
