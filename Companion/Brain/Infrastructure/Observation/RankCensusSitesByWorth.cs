#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>
/// A census over a moving window publishes the sites worth ordering, not every site it swept.
///
/// **Why a census needs this at all, measured rather than reasoned.** The lighting census publishes one
/// site per placeable dark tile inside the player's work window, which underground is the whole window:
/// 1,540 sites per decision on the 22 September capture's replay. Downstream,
/// `DiscoverAssistanceOpportunities.Continue` spends one allowance unit, two tracked reads and one JSON
/// deserialise **per site**, and `SearchCourseOrders.Enumerate` yields one single-step order per
/// opportunity — so the census sets the decision's cost. Regressed over the whole replay,
/// **decide_ms ≈ 3.505 + 0.003813 × sites**, so 1,540 sites is 5.9 ms of a 12 ms allowance spent before
/// the search prices anything. And it buys nothing: over 2,340 ticks the search **never priced more than
/// three orders in one decision** (1,290 decisions priced one, 684 priced three, 359 priced none), so
/// the budget dies inside discovery and the sites it reached were chosen by `OrderBy(f => f.Key)` —
/// tile-identity order — rather than by anything the census knows about their worth.
///
/// **The bound is the window's own answer rather than a tuned number.** Two torches must be more than
/// `spacing` tiles apart in both axes or the placer refuses the second, so a window of W by H tiles can
/// never usefully hold more than `ceil(W / (spacing+1)) * ceil(H / (spacing+1))` of them however dark it
/// is — 196 for a 125-tile window at spacing 8. That count is the bound, with no constant to tune and
/// nothing to re-derive when the work radius or the spacing moves: publishing more sites than the window
/// could ever use is spending the decision's budget on answers no placement can take. At 3.813 µs a site
/// that is 0.75 ms against 5.9 ms, and the set it publishes is the nearest usable sites rather than the
/// first tiles in key order.
///
/// **The bound is a count and not a grid, and the difference is a behaviour rather than a style.** The
/// same arithmetic can be spent as one site per spacing-disjoint cell, which is tighter and is wrong:
/// it fires in scenes with five candidate tiles in them, and it fires by withholding whichever of two
/// neighbouring sites the course would have chosen. `PublishTheBest` carries the measurement that
/// settled it.
///
/// **K of N is a three-valued answer, which is why `Withheld` exists.** A site outside the published set
/// is *not yet ranked* — it was swept, it is real, and it lost its bucket to a better one — and that is
/// neither "no dark tile exists here" nor "this tile is unusable". The root guide's rule that a bound
/// which ran out is a third value is the same rule: only a completed sweep with nothing found is a
/// proven absence, and a sweep that found more than it published has not answered about what it withheld.
/// The count travels on the coverage fact so a caller can tell the two apart.
///
/// **A site the course is currently working is never withheld.** `Keep` is hysteresis rather than a
/// favour: the published set is re-ranked from scratch every observation, so a site admitted at rank 3
/// that drifts to rank K+1 next tick would vanish from the snapshot, and an absent unpinned fact is
/// exactly what `RetireAdmissionsThisObservationCannotSupport` retires — which would manufacture, on
/// lighting, the churn that retirement rule was written to remove from combat.
/// </summary>
public static class RankCensusSitesByWorth
{
    /// <summary>One swept site awaiting publication: where it is, what it is worth, what reaching it
    /// costs, and the fact it becomes if it survives ranking.
    ///
    /// <para><c>Cost</c> is a monotone proxy for reaching the site — for lighting, squared tile distance
    /// from the intent region's heading — and it is the comparison that actually separates two sites in
    /// one cell. It is measured from the *heading* rather than from the body deliberately: the body moves
    /// every tick, so a representative keyed to it would change under a walking companion and tear
    /// unbound admissions out of the store on ticks when nothing about the world changed, where the
    /// heading is the same anchor the window is centred on and moves only when the window does.</para></summary>
    public readonly record struct Candidate<T>(Point Tile, int Tier, double Worth, double Cost, T Site);

    /// <summary>
    /// The best <paramref name="limit"/> sites of the sweep, plus every site <paramref name="keep"/>
    /// names, with the count of what that withheld.
    ///
    /// **A global cut rather than one site per spacing cell, and that was measured rather than
    /// reasoned.** Bucketing the window on the spacing grid and keeping each cell's best is the tighter
    /// bound and it distorts decisions in scenes nowhere near it: on
    /// `J08 a lighting trip breaks a permitted pot in passing`, a fixture whose whole acceptable set is
    /// a handful of tiles, the course picked `31,50` with everything published and the bucket rule
    /// withheld exactly that tile in favour of `30,51` two tiles away in the same cell — and the
    /// decision did not merely move the torch, it flipped domain, so the companion took a dedicated pot
    /// trip with no activity owning the body and the row reddened. Two sites inside one exclusion are
    /// substitutes in *value* and are not substitutes in what the course prices, which is a route from
    /// the body through a contact pose; a census cannot compute that, so it must not pretend to choose
    /// between them. A global cut never fires below the bound, so every scene that fits publishes
    /// exactly what it swept and no decision moves at all — and the pathological underground case, 1,540
    /// sites where 196 can ever be used, is cut just as hard as bucketing cut it.
    ///
    /// <c>Tier</c> is compared first and lower wins, because a census that dropped a *usable* site to
    /// publish an unusable one would be ranking by the wrong question. Within a tier **lower <c>Cost</c>
    /// wins, ahead of <c>Worth</c>**: the cut keeps the sites nearest the window's own centre, which are
    /// the ones a course flying out of that centre could reach at all, where a worth-first cut would
    /// keep the darkest tiles of a cave the companion is nowhere near and drop the dim ones under its
    /// feet. <c>Worth</c> breaks a cost tie and the tile's own coordinates break a remaining tie, so the
    /// set does not depend on enumeration order.
    ///
    /// <paramref name="limit"/> counts the *unpinned* sites, so pinning can publish more than the bound
    /// by exactly the number of sites a course is working — which is why a consumer bounding this
    /// domain's fact count allows for hysteresis rather than treating the bound as a ceiling.
    /// </summary>
    public static (List<T> Published, int Withheld) PublishTheBest<T>(
        IReadOnlyList<Candidate<T>> swept, int limit, Func<T, bool> keep)
    {
        if (swept.Count == 0) return (new List<T>(), 0);
        var pinned = new List<T>();
        var rest = new List<Candidate<T>>();
        foreach (Candidate<T> candidate in swept)
            if (keep(candidate.Site)) pinned.Add(candidate.Site); else rest.Add(candidate);
        var published = pinned.Concat(rest
            .OrderBy(c => c.Tier).ThenBy(c => c.Cost).ThenByDescending(c => c.Worth)
            .ThenBy(c => c.Tile.X).ThenBy(c => c.Tile.Y)
            .Take(Math.Max(0, limit))
            .Select(c => c.Site)).ToList();
        return (published, swept.Count - published.Count);
    }

    /// <summary>
    /// How many spacing-disjoint sites a square window of <paramref name="radiusTiles"/> can hold, which
    /// is the limit <see cref="PublishTheBest"/> is given and therefore the only honest bound on that
    /// domain's fact count.
    ///
    /// It is computed rather than written down so that moving the work radius or the placer's spacing
    /// moves the bound with them; a tripwire holding a constant somebody derived once is a tripwire that
    /// fires on the next radius change and reports the radius as a leak. Hysteresis can publish one more
    /// than this — the site a course is bound to is kept whatever its rank — so a consumer bounding a
    /// fact count allows for that rather than treating this as a hard ceiling.
    /// </summary>
    public static int MostSitesAWindowCanHold(int radiusTiles, int spacing)
    {
        int side = 2 * Math.Max(0, radiusTiles) + 1, cell = Math.Max(1, spacing + 1);
        int cells = (side + cell - 1) / cell;
        return cells * cells;
    }

}
