extern alias live;

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using live::AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// A census over a moving window publishes what its window could use, says how much it withheld, and
/// never drops the site a course is working.
///
/// **Why this is a pure row and not a scene.** The property is about the cut, and the live lighting
/// census cannot be driven headlessly to the state that exercises it: the light window is intersected
/// with the engine's own processed area, which is empty until the engine scans near the companion and
/// never does in a fixture, so every headless light sweep publishes nothing and reports Unresolved. The
/// end-to-end half is graded on the play-measures replay instead, as `the light census publishes inside
/// the work window and holds nothing it has left`, which is a real dark cave. What is here is the rule
/// the replay cannot isolate: which sites survive a crowded sweep, and which survive regardless.
///
/// **The first row is the one that earns the file.** A cut that fires below its bound changes decisions
/// in scenes that were never crowded, and it does it by withholding whichever site the course would
/// have picked — which is a defect a bound-shaped row cannot see, because the set it publishes is
/// perfectly well ranked and simply is not the set the course was choosing from.
/// </summary>
internal static class VerifyTheCensusPublishesItsBestSites
{
    public static int Run()
    {
        int red = 0;
        void Row(string name, Action test)
        {
            try { test(); Console.WriteLine("  GREEN " + name); }
            catch (Exception error) { red++; Console.WriteLine("  RED " + name + ": " + error.Message); }
        }
        Row("a sweep that fits its bound publishes every site it swept", ASweepUnderItsBoundIsUntouched);
        Row("a sweep past its bound publishes as many as the window could use", ASweepPastItsBoundIsCut);
        Row("a usable site is never dropped to publish an unusable one", TierOutranksWorth);
        Row("the cut keeps the nearest sites rather than the darkest distant ones", CostOutranksWorth);
        Row("what the cut withheld is counted rather than forgotten", WithheldIsCounted);
        Row("the site a course is bound to survives a cut it would otherwise lose", TheBoundSiteIsNeverWithheld);
        Row("a window's bound is its own spacing rather than a number", TheBoundComesFromTheWindow);
        return red;
    }

    /// <summary>
    /// The regression this file exists for, and the reason the bound is a count rather than a grid.
    ///
    /// Four sites crowd two tiles apart — well inside the placer's own exclusion, so at most one torch
    /// of the four can ever be placed — and the bound is nowhere near. Every one of them is published
    /// anyway, because the census does not know which of two neighbouring sites a course would take: the
    /// course prices a route from the body through a contact pose and the census prices a tile. Bucketing
    /// the window on the spacing grid and publishing one site per bucket is the tighter bound and it
    /// reds `J08 a lighting trip breaks a permitted pot in passing`, where it withheld the tile the
    /// course had been choosing (`31,50`) in favour of its neighbour (`30,51`) and the decision left
    /// lighting for a pot trip no activity owned.
    /// </summary>
    private static void ASweepUnderItsBoundIsUntouched()
    {
        var crowded = new[] { Site(10, 10, 0.2, 9), Site(11, 10, 0.9, 4), Site(12, 11, 0.5, 1), Site(10, 12, 0.1, 16) };
        Require(crowded.All(c => Math.Abs(c.Tile.X - crowded[0].Tile.X) <= Spacing && Math.Abs(c.Tile.Y - crowded[0].Tile.Y) <= Spacing),
            "premise: every site here must sit inside one exclusion, or the row is not about substitutes at all");
        (List<string> published, int withheld) = RankCensusSitesByWorth.PublishTheBest(crowded, 196, _ => false);
        Require(published.Count == 4 && withheld == 0,
            $"a sweep of four against a bound of 196 must publish all four and withhold none; published "
                + $"[{string.Join(" ", published)}] and withheld {withheld}");
    }

    /// <summary>Past the bound the cut fires, and it keeps the cheapest sites: the window can only use so
    /// many and the ones it can use are the ones near its own centre.</summary>
    private static void ASweepPastItsBoundIsCut()
    {
        var swept = Enumerable.Range(0, 8).Select(i => Site(i, 0, 0.5, cost: 100 - i)).ToArray();
        (List<string> published, int withheld) = RankCensusSitesByWorth.PublishTheBest(swept, 3, _ => false);
        Require(published.Count == 3 && withheld == 5,
            $"eight sites against a bound of three publish three and withhold five; published "
                + $"[{string.Join(" ", published)}] and withheld {withheld}");
        Require(published.SequenceEqual(new[] { "7,0", "6,0", "5,0" }),
            $"the three cheapest must be the three kept, nearest first; published [{string.Join(" ", published)}]");
    }

    /// <summary>A usable site beats an unusable one however cheap and however dark the unusable one
    /// reads, because a census that ranked by cost alone would spend its bound on tiles nobody can use
    /// and report the window as covered.</summary>
    private static void TierOutranksWorth()
    {
        var swept = new[] { Unusable(10, 10, 0.99, cost: 0), Site(11, 10, 0.05, cost: 900) };
        (List<string> published, _) = RankCensusSitesByWorth.PublishTheBest(swept, 1, _ => false);
        Require(published.Count == 1 && published[0] == "11,10",
            $"the usable site must survive a cut against a nearer, darker unusable one; published "
                + $"[{string.Join(" ", published)}]");
    }

    /// <summary>
    /// Cost before worth, which decides what a cut keeps when it does fire.
    ///
    /// A worth-first cut keeps the darkest tiles of a cave the companion is nowhere near and drops the
    /// dim ones under its feet, which is the set no course can act on. Swapping the two comparisons reds
    /// this row with the far dark site published over the near one.
    /// </summary>
    private static void CostOutranksWorth()
    {
        var swept = new[] { Site(10, 10, 0.95, cost: 400), Site(11, 10, 0.30, cost: 4) };
        (List<string> published, _) = RankCensusSitesByWorth.PublishTheBest(swept, 1, _ => false);
        Require(published.Count == 1 && published[0] == "11,10",
            $"one slot between a dark site 400 away and a dim one 4 away must go to the near one; published "
                + $"[{string.Join(" ", published)}]");
        var tied = new[] { Site(10, 10, 0.95, cost: 4), Site(11, 10, 0.30, cost: 4) };
        (List<string> byWorth, _) = RankCensusSitesByWorth.PublishTheBest(tied, 1, _ => false);
        Require(byWorth.Count == 1 && byWorth[0] == "10,10",
            $"at equal cost the darker site wins, or worth is not breaking the tie; published "
                + $"[{string.Join(" ", byWorth)}]");
    }

    /// <summary>The count is the third value. A site the cut dropped is *not yet ranked* — swept, real,
    /// and absent from the snapshot — which is neither "no dark tile here" nor "this tile is unusable",
    /// and without the count a consumer cannot tell the first from the second.</summary>
    private static void WithheldIsCounted()
    {
        var five = Enumerable.Range(0, 5).Select(i => Site(i, 0, 0.5, cost: i)).ToArray();
        (List<string> whole, int none) = RankCensusSitesByWorth.PublishTheBest(five, 5, _ => false);
        Require(whole.Count == 5 && none == 0,
            $"a sweep exactly at its bound withholds nothing; published {whole.Count}, withheld {none}");
        (List<string> cut, int withheld) = RankCensusSitesByWorth.PublishTheBest(five, 1, _ => false);
        Require(cut.Count == 1 && withheld == 4,
            $"the same five against a bound of one publish one and withhold four; published {cut.Count}, withheld {withheld}");
        (List<string> nothing, int zero) = RankCensusSitesByWorth.PublishTheBest(Array.Empty<RankCensusSitesByWorth.Candidate<string>>(), 5, _ => false);
        Require(nothing.Count == 0 && zero == 0,
            $"an empty sweep withheld nothing rather than everything; published {nothing.Count}, withheld {zero}");
    }

    /// <summary>
    /// The retirement hazard, which is the one row here about a defect rather than about an ordering.
    ///
    /// `RetireAdmissionsThisObservationCannotSupport` retires an admission whose evidence is absent and
    /// unpinned. The published set is re-cut from scratch every observation, so a site admitted just
    /// inside the bound that drifts just outside it next tick would simply not be in the snapshot — and
    /// an absent unpinned fact is exactly what that rule retires, which would manufacture on lighting
    /// the churn it was written to remove from combat. Hysteresis is the answer rather than pinning the
    /// evidence: the census is the thing that knows the site exists, so it is the thing that must keep
    /// publishing it, and pinning the *binding's* evidence downstream would leave the fact absent and
    /// teach every other reader of the snapshot that the site is gone. Planting `_ => false` for the keep
    /// predicate reds this row and nothing else in this file.
    /// </summary>
    private static void TheBoundSiteIsNeverWithheld()
    {
        var swept = new[] { Site(10, 10, 0.2, cost: 900), Site(11, 10, 0.9, cost: 1), Site(12, 11, 0.5, cost: 4) };
        (List<string> unbound, _) = RankCensusSitesByWorth.PublishTheBest(swept, 2, _ => false);
        Require(!unbound.Contains("10,10"),
            "premise: the site this row pins must be one the cut would otherwise drop, or the row proves nothing");
        (List<string> published, int withheld) = RankCensusSitesByWorth.PublishTheBest(
            swept, 2, site => site == "10,10");
        Require(published.Contains("10,10"),
            $"the site the course is bound to was cut by rank alone, so the next observation retires an "
                + $"admission the world still supports; published [{string.Join(" ", published)}]");
        Require(published.Contains("11,10") && published.Contains("12,11"),
            $"a pinned site is published beside the bound rather than out of it, so pinning costs the window "
                + $"nothing; published [{string.Join(" ", published)}]");
        Require(withheld == 0, $"three swept and three published, so nothing is withheld and {withheld} were counted");
    }

    /// <summary>The bound is the window's own arithmetic, so a wider window or a tighter spacing moves it
    /// without anybody editing a number. 125 tiles at spacing 8 is 14 cells a side.</summary>
    private static void TheBoundComesFromTheWindow()
    {
        Require(RankCensusSitesByWorth.MostSitesAWindowCanHold(62, 8) == 196,
            $"a 125-tile window at spacing 8 holds 14x14 spacing-disjoint sites, not "
                + $"{RankCensusSitesByWorth.MostSitesAWindowCanHold(62, 8)}");
        Require(RankCensusSitesByWorth.MostSitesAWindowCanHold(62, 0) == 125 * 125,
            "a spacing of zero excludes nothing, so every tile of the window is its own neighbourhood");
        Require(RankCensusSitesByWorth.MostSitesAWindowCanHold(124, 8) > RankCensusSitesByWorth.MostSitesAWindowCanHold(62, 8),
            "doubling the work radius must raise the bound, or the bound is not the window's");
    }

    /// <summary>The placer's own exclusion, read from it rather than written here, because a fixture
    /// holding its own copy of a production constant passes while production drifts.</summary>
    private static int Spacing => live::AICompanion.Companion.Brain.Infrastructure.Interactions.Torch
        .CompanionTorches.SpacingTiles;

    private static RankCensusSitesByWorth.Candidate<string> Site(int x, int y, double darkness, double cost = 0)
        => new(new Point(x, y), 0, darkness, cost, $"{x},{y}");

    private static RankCensusSitesByWorth.Candidate<string> Unusable(int x, int y, double darkness, double cost = 0)
        => new(new Point(x, y), 2, darkness, cost, $"{x},{y}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
