extern alias live;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using AICompanion.Tools.Ledger;
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

    /// <summary>The window property, accumulated over the run whether or not anybody asked for the
    /// per-tick file: the widest box the census ever published, and the worst overhang of a published
    /// site beyond the work window centred on the heading that tick. Both are what the row below grades,
    /// and both are cheap now that the census publishes a bounded set — a hundred-odd comparisons a tick
    /// against the fifteen hundred this would have cost before it was bounded.</summary>
    private static int widestBox, worstOverhang, ticksWithSites, lastTick;

    public static void Reset() { widestBox = 0; worstOverhang = 0; ticksWithSites = 0; lastTick = 0; }

    public static void Observe(int tick, DecisionFactSnapshot facts, Point heading)
    {
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
        lastTick = tick;
        if (right >= low)
        {
            ticksWithSites++;
            widestBox = Math.Max(widestBox, Math.Max(right - low + 1, bottom - top + 1));
            // Measured against the sweep the *same snapshot* declares, never against this tick's heading.
            // The first version of this row compared the published sites to
            // `Senses.Intent.Region.Heading` read after the tick, and it reddened the replay at a
            // two-tile overhang — correctly, because it was comparing two clocks. An observation is
            // frozen for the life of a *decision* rather than of a tick, so a snapshot read at tick T
            // may have been swept several ticks earlier and the region has walked since. The coverage
            // fact carries the rectangle the census actually swept, so the containment question has a
            // clock-free answer and the row asks that one.
            Rectangle swept = SweptArea(facts);
            if (swept.Width > 0)
                worstOverhang = Math.Max(worstOverhang, Math.Max(
                    Math.Max(swept.Left - low, right - (swept.Right - 1)),
                    Math.Max(swept.Top - top, bottom - (swept.Bottom - 1))));
        }
        if (Path is not { Length: > 0 } destination) return;
        File.AppendAllText(destination, FormattableString.Invariant($"{tick}\t{facts.ObservationOrdinal}\t{total}\t")
            + string.Join(" ", byKind.Select(p => p.Key + "=" + p.Value.ToString(CultureInfo.InvariantCulture)))
            + FormattableString.Invariant($"\tlight-box={low},{top}..{right},{bottom}\thead={heading.X},{heading.Y}\n"));
    }

    /// <summary>The rectangle the light census says it swept for this observation, off its own coverage
    /// fact, as `area=left,top:WxH`. An empty rectangle where the fact is absent or unparseable, which
    /// the caller reads as "nothing to check" rather than as a zero-sized window.</summary>
    private static Rectangle SweptArea(DecisionFactSnapshot facts)
    {
        if (!facts.TryRead(new FactKey("light-coverage", "native-census"), out DecisionFact coverage))
            return Rectangle.Empty;
        foreach (string part in (coverage.Value.Text ?? "").Split(';'))
        {
            if (!part.StartsWith("area=", StringComparison.Ordinal)) continue;
            string[] halves = part["area=".Length..].Split(':');
            if (halves.Length != 2) return Rectangle.Empty;
            string[] origin = halves[0].Split(','), size = halves[1].Split('x');
            if (origin.Length != 2 || size.Length != 2) return Rectangle.Empty;
            return int.TryParse(origin[0], out int x) && int.TryParse(origin[1], out int y)
                && int.TryParse(size[0], out int w) && int.TryParse(size[1], out int h)
                ? new Rectangle(x, y, w, h) : Rectangle.Empty;
        }
        return Rectangle.Empty;
    }

    /// <summary>
    /// The window property as a graded row rather than a paragraph: the census publishes inside the work
    /// window and holds nothing the window has left behind.
    ///
    /// **This is the row that was missing while a conclusion rested on it.** The growth from 150 facts to
    /// 1,603 in the play of 0.38.13 was written up as a set nobody clears, and refuted by measuring the
    /// published sites' bounding box against the player's intent-region heading — 125 tiles wide, its
    /// corner tracking the heading, holding nothing from a window eighteen hundred ticks earlier. A
    /// measurement taken once by hand is not a property anybody is holding, and a synthetic fixture for
    /// it wants a dark 125-tile window with the engine's own lighting scan driven over it, which no scene
    /// in this tree builds. The replay already has that scene, so the pin goes here.
    ///
    /// Two numbers, each failing in its own direction. The **width** catches a census whose window grew —
    /// a radius change, or a sweep that stopped intersecting the engine's processed area — and it is
    /// graded against the work radius rather than a literal, so moving the radius moves the row. The
    /// **overhang** catches accumulation: a site published outside the rectangle the same snapshot says
    /// it swept is a site from an older sweep, and one tile of it says the census is keeping what it saw.
    ///
    /// The overhang is measured against the snapshot's *own* coverage rectangle rather than against the
    /// tick's heading, and the first version of this row got that wrong and reddened the replay at two
    /// tiles. An observation is frozen for the life of a decision rather than of a tick, so the sites in
    /// a snapshot read at tick T were swept around a region that has since walked — a real skew between
    /// two clocks, not a census holding anything. The coverage fact carries the rectangle, so the
    /// question has an answer that does not depend on when it is asked.
    ///
    /// It refuses to grade a replay that published no light site at all, because a census that published
    /// nothing satisfies both numbers perfectly and would turn an unlit run into a green row.
    /// </summary>
    public static int GradeTheWindow(string suite, string scene, IReadOnlyList<string> tags, string mode)
    {
        const string Case = "the light census publishes inside the work window and holds nothing it has left";
        int radius = (int)(live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowWorkRadius / 16f);
        int window = 2 * radius + 1;
        if (ticksWithSites == 0)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, Case,
                $"no light site was published on any of {lastTick} tick(s), so the window property has no subject "
                    + "here and a pass would be a census that swept nothing");
            return 0;
        }
        string message = $"{scene}: over {ticksWithSites} tick(s) that published a light site the widest box was "
            + $"{widestBox} tiles against a {window}-tile work window, and the worst overhang beyond the sweep "
            + $"that same observation declares was {worstOverhang} tile(s)";
        if (widestBox <= window && worstOverhang <= 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, Case,
                message + " — the census re-sweeps a moving window rather than accumulating inside it",
                mode: mode, tags: tags);
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, Case,
            message + ". A site outside this tick's window is one the window has already left, which is the "
                + "accumulation the dark-window finding ruled out and nothing else was holding",
            mode: mode, tags: tags);
        return 1;
    }
}
