#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.Brain.Infrastructure.Selection;

namespace AICompanion.Companion.Brain.Infrastructure.Observation;

/// <summary>What the body can get to, as a sense every consumer reads rather than a private
/// answer one component owns.</summary>
public enum ReachVerdict
{
    /// <summary>In the region flooded from the feet with the edges that have no way back refused.</summary>
    Reachable,
    /// <summary>The flood has not finished and has not claimed this tile: unknown, not absent.</summary>
    NotYet,
    /// <summary>The flood ran out of region before its budget and never claimed this tile.</summary>
    Unreachable,
}

/// <summary>
/// Where the body can walk to, flooded once per cadence from its feet and read by everyone who
/// needs the answer. Two floods, exactly as the positioner ran them before this was a sense: one
/// that refuses the edges with no way back, which is the region meaning "everywhere the body can
/// go and come home from", and a raw one that allows them, advanced only to test the one exception
/// — a player standing somewhere the body can only drop into. Being stuck is not having no way back
/// to the take-off; it is having no way to the player (Caner's own definition, 2026-09-08), so a
/// place he is standing in is by definition not somewhere the companion strands itself.
///
/// <para>The sense is refreshed by the positioner's resolve rather than by <see cref="Senses.Update"/>,
/// and that is deliberate: <see cref="AStar.AllowLava"/> and the one-way rule are set per request by
/// the brain tick, so a flood run at the top of the tick would answer every consumer under the
/// previous tick's rule. Consumers that read between resolves therefore read the flood the last
/// resolve left, which is a tick or two stale and still the best answer to "where can I get to".</para>
///
/// <para>The tri-state is the whole point of exposing it. A tile missing from an unfinished flood is
/// unknown rather than absent, so an activity that refuses <see cref="ReachVerdict.NotYet"/> is
/// declining to start on an unanswered search rather than asserting the place does not exist.</para>
/// </summary>
public sealed class ReachSense
{
    /// <summary>How many ticks a flood is reused for. It mirrors the positioner's rescore interval,
    /// because the region a candidate is scored against and the region that candidate came from have
    /// to be the same one; if the two drift a spot is admitted against a region it was never in.</summary>
    private const int RefloodTicks = 12;

    private HashSet<Point>? scored;
    private HashSet<Point>? returnable;
    private HashSet<Point>? raw;
    private ContinueRouteSearch? returnSearch, rawSearch;
    private bool floodLava;
    private int sinceFlood = RefloodTicks;

    /// <summary>How many times the floods have been thrown away and started again this session. It
    /// counts the discard rather than the advance, so a cadence that keeps growing one flood does
    /// not register: this is the number a terrain edit used to drive from anywhere in the world.</summary>
    public int Refloods { get; private set; }

    /// <summary>Whether the two-way flood ran out of region before its budget, so a tile outside it is truly
    /// absent. This is the flood <see cref="Reachable"/> answers from, and the two are deliberately tied: a
    /// verdict of absent must rest on the exhaustion of the set it was looked up in, never on another set's.</summary>
    public bool Complete { get; private set; }

    /// <summary>The same question about <see cref="ScoredTiles"/>, which on a player-only-one-way tick is the
    /// raw region rather than the two-way one. The positioner scores candidates against that set and so asks
    /// this rather than <see cref="Complete"/>; every consumer asking whether the body can go somewhere and
    /// come back asks <see cref="Complete"/>. Two names because they are two facts, and they diverge on
    /// exactly the tick it matters.</summary>
    public bool ScoredComplete { get; private set; }

    /// <summary>
    /// The refusing flood did not hold the player, so the region being scored is the raw one and the
    /// companion is willing to go somewhere it cannot come back from. True is not a fault: it is the
    /// companion following the player into a place he chose to be. It is worth recording because it is
    /// the one state where prevention is deliberately switched off.
    /// </summary>
    public bool PlayerOnlyOneWay { get; private set; }

    /// <summary>Wall-clock of the last flood, for the telemetry.</summary>
    public double LastFloodMs { get; private set; }

    /// <summary>How many tiles the body can reach at all, and how many of those it can come home from.</summary>
    public int AnyCount => scored?.Count ?? 0;
    public int TwoWayCount => returnable?.Count ?? 0;

    /// <summary>The region the positioner scores candidates against: the two-way one, or the raw one on the
    /// tick the player-only-one-way exception is granted.</summary>
    public bool InScoredRegion(Point tile) => scored != null && scored.Contains(tile);

    /// <summary>Every tile in the scored region, for a caller that draws from it rather than asking about
    /// one tile. Empty rather than null before the first flood, so no caller has to know the difference.</summary>
    public IReadOnlyCollection<Point> ScoredTiles => (IReadOnlyCollection<Point>?)scored ?? System.Array.Empty<Point>();

    /// <summary>Whether the body can walk to this feet tile and come home from it.</summary>
    public bool Returnable(Point tile) => returnable != null && returnable.Contains(tile);

    /// <summary>
    /// Reachable, not yet known, or proven absent — against the two-way region, which is what
    /// returnable means in this project: everywhere the body can go and come home from.
    /// </summary>
    public ReachVerdict Reachable(Point tile)
        => Returnable(tile) ? ReachVerdict.Reachable
            : Complete ? ReachVerdict.Unreachable
            : ReachVerdict.NotYet;

    /// <summary>
    /// Membership of the flood that allows edges with no way back. The raw flood is only advanced on a
    /// tick where the two-way one failed to hold the player, because that is the only question it is
    /// asked, so a false here means "not proven one-way reachable", never "proven unreachable".
    /// </summary>
    public bool ReachableOneWay(Point tile) => raw != null && raw.Contains(tile);

    public float? EstimatedTravelTicks(Point from, Point tile) => rawSearch?.EstimatedTicks(from, tile)
        ?? returnSearch?.EstimatedTicks(from, tile);

    /// <summary>
    /// Age the region by one resolve. The cadence counts resolves rather than game ticks, and that is
    /// load-bearing rather than incidental: the flood is bounded per advance and grows across successive
    /// resolves, so a caller that resolves repeatedly inside one tick is asking for the region to keep
    /// expanding, and a cadence keyed to the tick would advance it once and never finish. It is called
    /// by the resolve that owns the cadence and never by the readers, so a consumer asking a question
    /// during a hold cannot age the region out from under the scoring it will be compared against.
    /// </summary>
    public void Age() => sinceFlood++;

    /// <summary>
    /// Reflood if the cadence has passed or the world has been edited inside the region either flood
    /// has explored. The terrain check lives here rather than in the caller so every consumer of the
    /// sense — not only the one that happens to drive the cadence — reads a region that survived the
    /// dig, and it expires the cadence rather than waiting for the reuse test further down, because
    /// an edit the flood read has to land on the next resolve and not up to a cadence later.
    ///
    /// <para>Both floods are asked, not only the two-way one. The raw flood allows edges with no way
    /// back, so it can have explored further than the two-way flood and can hold stale work the
    /// two-way flood's own bounds say nothing about.</para>
    /// </summary>
    public void Refresh(Senses senses)
    {
        if (returnSearch?.Valid == false || rawSearch?.Valid == false)
            sinceFlood = RefloodTicks;
        if (scored != null && sinceFlood < RefloodTicks)
            return;
        sinceFlood = 0;

        Point? feet = MovementQueries.NearestStandable(MovementQueries.FeetTile(senses.Companion.Bottom), 2);
        if (feet == null)
        {
            // In the air or inside something: keep the last flood, which is a tick or two stale
            // and still the best answer to "where can I get to" until the body lands.
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Reuse needs generated connectivity in both directions, not a distance allowance.
        // A body can cross a one-way boundary while moving only one tile.
        if (returnSearch == null || !returnSearch.Valid || floodLava != AStar.AllowLava || !returnSearch.CanReuseFrom(feet.Value))
        {
            floodLava = AStar.AllowLava;
            Refloods++;
            returnSearch?.Dispose(); rawSearch?.Dispose();
            returnSearch = new ContinueRouteSearch(feet.Value, null, AStar.AllowLava, false);
            rawSearch = new ContinueRouteSearch(feet.Value, null, AStar.AllowLava, true);
        }
        returnSearch.Advance(Weights.ReachFloodBudget, Weights.PositionReachMilliseconds / 2d);
        returnable = returnSearch.Reached;
        bool complete = returnSearch.Finished && returnSearch.Stop == AStar.SearchStopReason.Exhausted;
        bool? scoredComplete = null;
        scored = returnable;
        raw = null;

        // The raw region has to be read before the exception is granted, because "he is not in the
        // returnable region" is also true of a player the body cannot reach at all — walled off behind
        // a sand fall, which is a state the mod supports until he digs the body out. Opening the tier
        // there refuses nothing and buys nothing: it hands the roam a region full of drops with no way
        // back, so the pocket gets deeper, and it asserts a one-way route to a player no route reaches.
        // The exception is for the drop that leads to him, so it is granted only where the region
        // without the refusal actually holds him.
        Point? player = MovementQueries.NearestStandable(MovementQueries.FeetTile(senses.Player.Bottom), 2);
        PlayerOnlyOneWay = false;
        if (player is Point p && !returnable.Contains(p))
        {
            rawSearch!.Advance(Weights.ReachFloodBudget, Weights.PositionReachMilliseconds / 2d);
            HashSet<Point> anyReach = new(rawSearch.Reached);
            bool rawComplete = rawSearch.Finished && rawSearch.Stop == AStar.SearchStopReason.Exhausted;
            // Both bounded searches prove membership, but may spend their work on different
            // branches. Preserve all previously proven reachable tiles when opening the tier.
            anyReach.UnionWith(returnable);
            raw = anyReach;
            if (anyReach.Contains(p))
            {
                // Only the scored region moves to the one-way set, and only its completeness moves with it.
                // `complete` stays the two-way flood's, because that is the flood `Reachable` answers from,
                // and grading a tile Unreachable on a *different* flood's exhaustion is the same
                // not-yet-is-not-no error one layer up — now with teeth, since a proven No is remembered for
                // the no-return wait, so one tile missing from an unfinished two-way flood would be written
                // off for that whole period on the strength of the raw flood having finished.
                scored = anyReach;
                scoredComplete = rawComplete;
                PlayerOnlyOneWay = true;
            }
        }

        LastFloodMs = clock.Elapsed.TotalMilliseconds;
        Complete = complete;
        ScoredComplete = scoredComplete ?? complete;
    }
}
