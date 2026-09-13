#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Companion.Brain.Infrastructure.Movement;
using AICompanion.Companion.CharacterBody;

namespace AICompanion.Companion.Brain.Infrastructure.Diagnostics;

/// <summary>
/// How long getting somewhere actually took, and where the body stopped on the way.
///
/// The census already counts how many places were asked for and how many were reached, and the edge columns already
/// carry one finished move against its proven ticks. Neither answers the question a playtest actually raises, which is
/// "it took forever to get to me": a journey is tens of moves, every one of which can complete inside its proven time
/// while the journey as a whole takes four times as long, because the time goes into the gaps between moves rather than
/// into the moves. So this watches the whole episode — one continuous stretch of wanting one kind of place — and
/// reports what it cost against two references the record could not otherwise compare it to: what the route's own steps
/// were proven to take, and what the player's body took over the same ground.
///
/// The second reference is the design's own pass line ("if I can get through, it can") applied to time rather than to
/// terrain, and it is the only reference here the brain did not produce. Everything else this folder measures compares
/// the brain against itself and can only ever establish internal consistency.
///
/// Beside it, the stops. A body driven along a route that sits under a walking pace for three ticks or more is either
/// braking on purpose, waiting on a replan, or stuck, and those want opposite fixes — so the reason is attributed from
/// retained state at the moment it happens rather than guessed at afterwards from a row.
///
/// Every number here is read from state the brain already holds. Nothing is planned, floods nothing and times nothing.
/// </summary>
public static class TravelEpisodes
{
    /// <summary>Under this many pixels a tick the body is not travelling. A walk is several pixels a tick, so this is
    /// comfortably below any real movement and above the sub-pixel drift a resting body shows against a slope.</summary>
    private const float StoppedPixelsPerTick = 0.6f;

    /// <summary>Ticks at that speed before it is a stop rather than a turn. Two ticks is a direction change; three is a pause.</summary>
    private const int StoppedTicks = 3;

    /// <summary>How near a player trail tile must be to count as the same place, in tiles, Chebyshev: a body is two tiles
    /// wide and the two actors stand on different feet tiles at the same spot.</summary>
    private const int SamePlaceTiles = 2;

    /// <summary>Player trail entries kept. One entry per change of tile, so this is a long journey's worth and the pair
    /// search over it stays cheap; an episode whose ends fall outside it reports no player comparison rather than a wrong one.</summary>
    private const int TrailEntries = 1024;

    private static ulong observedTick = ulong.MaxValue;
    private static Vector2 lastFeet;
    private static bool hasLastFeet;

    // The open episode. Its identity is the request kind, exactly as the census's is, because the exact tile moves under
    // a request that has not changed and a minute of following is one ask rather than 3,600.
    private static string? episodeKind;
    private static ulong episodeStart;
    private static Point episodeStartTile;
    private static Vector2 episodeStartFeet;
    private static bool episodeReached;
    private static int episodePlannedTicks;
    private static float episodePathTiles;
    private static float episodeTravelledPixels;
    /// <summary>Ticks inside the episode on which the body was downed. They are subtracted from the reported duration and
    /// reported beside it, because the wall-clock span stays recoverable from the two tick stamps either way — so removing
    /// them costs a reader nothing and leaving them in charges a death to the follower.</summary>
    private static int episodeDownedTicks;
    private static int lastEdgeCount;

    // The open stop and the evidence gathered across it, all of it retained state sampled per tick.
    private static int stopTicks;
    private static ulong stopStart;
    private static NavPath? stopPath;
    private static int stopIndex;
    private static long stopSearchId;
    private static bool stopReplanned, stopSameStep, stopAllGrounded, stopAllAirborne, stopNextFromRest;
    private static float stopFastestSideways;

    // Running totals the row carries, so a reader sees the rate without joining the sidecar.
    private static long routeTicks;
    private static double routePixels;
    private static int stops;

    private static readonly List<(Point Tile, ulong Tick)> trail = new();

    /// <summary>Stops so far against minutes of route travel so far, or -1 before any travel has been recorded.</summary>
    public static float StopsPerMinute => routeTicks == 0 ? -1f : stops * 3600f / routeTicks;

    /// <summary>Mean observed speed while on a route, in pixels a tick, or -1 before any travel has been recorded.</summary>
    public static float RouteSpeedMean => routeTicks == 0 ? -1f : (float)(routePixels / routeTicks);

    /// <summary>A new session, a new world, a new companion: nothing here survives one.</summary>
    public static void Reset()
    {
        observedTick = ulong.MaxValue;
        hasLastFeet = false;
        episodeKind = null;
        episodeReached = false;
        episodePlannedTicks = 0;
        episodeDownedTicks = 0;
        episodePathTiles = episodeTravelledPixels = 0f;
        lastEdgeCount = 0;
        stopTicks = 0;
        stopPath = null;
        routeTicks = 0;
        routePixels = 0;
        stops = 0;
        trail.Clear();
    }

    /// <summary>
    /// One observation per tick, from the recorder's own per-tick call. It is driven from there rather than from the
    /// brain so that navigation stays independent of recording, which is the same reason the navigation evidence is
    /// sampled by telemetry rather than written by the navigator.
    /// </summary>
    public static void Watch(CompanionNPC companion)
    {
        ulong tick = Main.GameUpdateCount;
        if (tick == observedTick) return;
        observedTick = tick;

        Brain brain = companion.Brain;
        Navigator navigator = brain.Navigator;
        BodyState observed = companion.Motor.ObservedState;
        Vector2 feet = new(observed.Left + companion.NPC.width / 2f, observed.Bottom);
        // The AI-entry observation is the only position that sees what our own AI phase wrote; npc.position after
        // helpers does not, which is what hid the platform freeze for three attempts.
        float moved = hasLastFeet ? Vector2.Distance(feet, lastFeet) : 0f;
        lastFeet = feet;
        hasLastFeet = true;

        RememberPlayer(NavGrid.FeetTile(brain.Senses.Player.Bottom), tick);

        // A route is being executed when the ordinary travel owner holds the body and the navigator has something to
        // execute. "travel-recovery-clearance" and every safety and recovery owner are deliberately excluded: those
        // branches are not the follower walking a route, and charging their stillness to travel would report a body
        // held by something else as a body that stopped.
        bool onRoute = companion.Motor.ControlSource == "travel"
            && navigator.Status is Navigator.ExecutionStatus.Executable or Navigator.ExecutionStatus.Partial;

        string? kind = companion.Motor.ControlSource switch
        {
            "travel" or "seeking-destination" => brain.LastRequest.Kind.ToString(),
            // A Hold request is the ordinary way an episode ends, and the census treats it the same way.
            "hold" => null,
            // Safety, recovery and downing leave the episode open: an ask does not stop being one because something else
            // took the body for a moment, and an episode interrupted by a dodge is one episode. What the census does with
            // those ticks is not a precedent here, though — it counts asks, and this counts time, so a boundary rule that
            // is right for a count is not automatically right for a duration. Downed ticks are therefore kept inside the
            // episode and subtracted from its duration below, which is the only treatment that keeps the ask whole and
            // keeps a death out of the follower's speed.
            _ => episodeKind,
        };

        // Everything measured this tick belongs to the episode that was open during it, so it is accumulated before the
        // transition. Accumulating afterwards credits a new episode's first tick with displacement the previous one earned,
        // which biases the mean speed of every short episode that follows a fast one.
        if (episodeKind != null && companion.IsDowned)
        {
            // A downed body is not travelling badly, it is not travelling. Its displacement is knockback and its ticks
            // belong to the death, so neither reaches the journey's distance, its proven total or its duration.
            episodeDownedTicks++;
        }
        else if (episodeKind != null)
        {
            episodeTravelledPixels += moved;
            if (navigator.Arrived) episodeReached = true;
            // One finished or faulted move, sticky until the next, so a new one has landed exactly when the count moves.
            // Two edges reported inside one tick leave only the later one readable, which undercounts the proven total
            // rather than inventing one.
            if (navigator.EdgeCount != lastEdgeCount)
            {
                lastEdgeCount = navigator.EdgeCount;
                if (navigator.LastEdge is EdgeReport edge)
                {
                    episodePlannedTicks += edge.Expected;
                    episodePathTiles += Vector2.Distance(new Vector2(edge.From.X, edge.From.Y), new Vector2(edge.Tile.X, edge.Tile.Y));
                }
            }
        }

        if (kind != episodeKind)
        {
            EndEpisode(companion, tick);
            if (kind != null) BeginEpisode(kind, tick, feet);
        }

        if (onRoute)
        {
            routeTicks++;
            routePixels += moved;
        }

        if (onRoute && moved < StoppedPixelsPerTick) HoldStop(brain, observed, tick);
        else EndStop(companion, tick);
    }

    /// <summary>The open episode and the open stop, closed at world or mod unload so a session's last journey is recorded
    /// rather than lost with it. The recorder calls this before it closes the occurrence stream.</summary>
    public static void Close(CompanionNPC? companion)
    {
        if (companion == null) return;
        EndStop(companion, Main.GameUpdateCount);
        EndEpisode(companion, Main.GameUpdateCount);
    }

    private static void BeginEpisode(string kind, ulong tick, Vector2 feet)
    {
        episodeKind = kind;
        episodeStart = tick;
        episodeStartFeet = feet;
        episodeStartTile = NavGrid.FeetTile(feet);
        episodeReached = false;
        episodePlannedTicks = 0;
        episodeDownedTicks = 0;
        episodePathTiles = episodeTravelledPixels = 0f;
    }

    private static void EndEpisode(CompanionNPC companion, ulong tick)
    {
        if (episodeKind == null) return;
        EndStop(companion, tick);
        // The duration is the span less the ticks the body could not travel through. A downing runs into the hundreds of
        // ticks, longer than most journeys, so charging it here would make a death the loudest slow journey in every
        // report — from the one instrument whose whole purpose is to say whether the follower is slow.
        int actual = (int)Math.Max(1, (long)(tick - episodeStart) - episodeDownedTicks);
        Vector2 feet = new(companion.Motor.ObservedState.Left + companion.NPC.width / 2f, companion.Motor.ObservedState.Bottom);
        GodsEyeEvents.RecordRouteEpisode(companion.NPC, episodeKind, episodeReached ? "reached" : "abandoned",
            episodeStart, tick, episodePlannedTicks, actual, episodeDownedTicks,
            Vector2.Distance(episodeStartFeet, feet) / 16f, episodePathTiles,
            episodeTravelledPixels / actual,
            PlayerTicksBetween(episodeStartTile, NavGrid.FeetTile(feet)));
        episodeKind = null;
    }

    private static void HoldStop(Brain brain, in BodyState observed, ulong tick)
    {
        NavPath? path = brain.Navigator.Path;
        if (stopTicks == 0)
        {
            stopStart = tick;
            stopPath = path;
            stopIndex = path?.Index ?? -1;
            stopSearchId = brain.Navigator.SearchId;
            stopReplanned = false;
            stopSameStep = true;
            stopAllGrounded = stopAllAirborne = true;
            stopFastestSideways = 0f;
            // The step about to be performed, which is the one a brake would be braking for.
            stopNextFromRest = path is { Finished: false } && path.Current.FromRest;
        }
        else
        {
            if (!ReferenceEquals(path, stopPath) || brain.Navigator.SearchId != stopSearchId) stopReplanned = true;
            if ((path?.Index ?? -1) != stopIndex) stopSameStep = false;
        }
        stopTicks++;
        stopAllGrounded &= observed.OnGround;
        stopAllAirborne &= !observed.OnGround;
        stopFastestSideways = MathF.Max(stopFastestSideways, MathF.Abs(observed.Vx));
    }

    private static void EndStop(CompanionNPC companion, ulong tick)
    {
        int held = stopTicks;
        stopTicks = 0;
        if (held < StoppedTicks) return;
        stops++;
        // The order is a precedence and not a list of equals. A path replaced under a still body explains the stillness
        // outright, so it is asked first or every replan would be filed as whichever posture the body happened to hold.
        // Airborne comes next because a body in the air is not braking for anything. Only then the two grounded readings,
        // the deliberate one before the passive one, because a brake is a decision and sitting inside a walk step is not.
        string reason = stopReplanned ? "during-replan"
            : stopAllAirborne && stopFastestSideways < StoppedPixelsPerTick ? "airborne-no-sideways-speed"
            // A move proven from a standing start needs the body at rest before it begins, so the ticks spent arriving at
            // rest are the move's own cost rather than a fault. This reason becomes wrong the moment the momentum graph
            // lands and a move can be proven from the speed the body already carries: delete it then, do not leave it.
            : stopAllGrounded && stopNextFromRest ? "brake-before-from-rest-move"
            : stopAllGrounded && stopSameStep ? "inside-walk-step"
            : "other";
        GodsEyeEvents.RecordStop(companion.NPC, stopStart, tick, held, reason,
            stopAllGrounded, stopAllAirborne, stopSameStep, stopReplanned, stopNextFromRest, stopFastestSideways,
            StoppedPixelsPerTick, StoppedTicks);
    }

    private static void RememberPlayer(Point tile, ulong tick)
    {
        if (trail.Count > 0 && trail[^1].Tile == tile) return;
        trail.Add((tile, tick));
        // Trimmed in one block rather than one entry at a time, so the shift is paid once per thousand tiles.
        if (trail.Count >= 2 * TrailEntries) trail.RemoveRange(0, trail.Count - TrailEntries);
    }

    /// <summary>
    /// The ticks the player's own body took between the two ends of this episode, where its recorded trail covered both,
    /// and "-" where it did not. The tightest such pair wins: a player who passed the start twice is being compared on
    /// the crossing that is actually comparable, not on an hour of wandering that happens to span the same two tiles.
    /// </summary>
    private static string PlayerTicksBetween(Point from, Point to)
    {
        long best = -1;
        for (int i = 0; i < trail.Count; i++)
        {
            if (!Near(trail[i].Tile, from)) continue;
            for (int j = i + 1; j < trail.Count; j++)
                if (Near(trail[j].Tile, to))
                {
                    long span = (long)(trail[j].Tick - trail[i].Tick);
                    if (best < 0 || span < best) best = span;
                    break;
                }
        }
        return best < 0 ? "-" : best.ToString(CultureInfo.InvariantCulture);
    }

    private static bool Near(Point a, Point b)
        => Math.Abs(a.X - b.X) <= SamePlaceTiles && Math.Abs(a.Y - b.Y) <= SamePlaceTiles;
}
