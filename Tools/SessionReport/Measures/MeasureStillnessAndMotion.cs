#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// The definitions every measure in this file shares, stated once so the four cannot drift into
/// four different ideas of "moving".
///
/// These floors are the definition of the measures rather than tunables, and they are deliberately
/// not <see cref="MeasureAheadShare"/>'s 1.2 px/tick. That measure asks whether the companion kept
/// ahead of a player who was *travelling*; these ask whether the companion moved while the player
/// did anything more than stand, which is the lower bar the first orb play of 15 September failed
/// visibly. Unifying the two floors would move every denominator in both measures, and a before-
/// number taken under one floor is not comparable with an after-number taken under another.
/// </summary>
internal static class Motion
{
    /// <summary>Above this the player is moving. A walking player reads about three, so one excludes only standing and drifting.</summary>
    public const double PlayerMovingPxPerTick = 1.0;

    /// <summary>Below this the orb is still: under a third of a pixel a tick is a body hovering in place, not travelling.</summary>
    public const double BodyStillPxPerTick = 0.3;

    /// <summary>
    /// A heading is only meaningful on a body actually going somewhere. Below a pixel a tick the
    /// velocity's direction is the residue of a brake, and the angle between two of those swings
    /// through a hundred degrees while the body goes nowhere.
    /// </summary>
    public const double HeadingSpeedFloorPxPerTick = 1.0;

    /// <summary>Stillness this short is the hover between two moves, not a stop anyone sees. Ten ticks is a sixth of a second.</summary>
    public const int ShortestStillRunTicks = 10;

    public static double Length((double X, double Y) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    /// <summary>
    /// The orb-only column these measures name as their schema witness. <c>npc_vel</c> and
    /// <c>npc_px</c> kept their names when the walking body was retired and changed what they mean —
    /// the position moved from the feet to the centre — so a walker capture would run every measure
    /// here to the end and produce confident numbers about a body the game no longer has.
    /// </summary>
    public const string OrbWitness = "desired_vel";

    public const string Tag = "play-2026-09-15";
}

/// <summary>
/// How much of the time the orb sat still while the player moved, who held it still, and how long
/// the longest stretches of stillness ran — the three numbers the first orb play reduces to.
///
/// That play, on 15 September 2026, was hated on sight: the orb sat on most ticks while the player
/// walked, froze for thirteen seconds beside zombies, and moved in stop-start bursts. The report
/// printed no definitive issue and <c>unexplained-stops</c> read zero, because a stop is only an
/// occurrence on a navigator route, and a body held still by a safety response or an arrived hold
/// has no route to stop on. These count the stillness itself, whoever owns it, so a movement
/// rebuild that removes it moves these numbers and a regression moves them back.
///
/// The split by <c>control_source</c> is what makes the share a diagnosis rather than a grade: on
/// that play most of the stillness belonged to combat spacing, not to travel, which a single share
/// could never have said.
/// </summary>
public sealed class MeasureStillness : IMeasure
{
    public string Name => "stillness";
    public string[] Needs => new[] { "tick", "state", "player_vel", "npc_vel", "control_source", Motion.OrbWitness };

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column tick = session["tick"], state = session["state"], player = session["player_vel"], npc = session["npc_vel"], source = session["control_source"];
        int moving = 0, stillWhileMoving = 0;
        var byOwner = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var runs = new List<int>();
        int run = 0;
        long? previousTick = null;

        void CloseRun()
        {
            if (run >= Motion.ShortestStillRunTicks) runs.Add(run);
            run = 0;
        }

        for (int row = 0; row < session.Count; row++)
        {
            long? now = ReadPlay.Tick(tick, row);
            bool alive = ReadPlay.Alive(state, row);
            (double X, double Y)? bodyVelocity = ReadPlay.Pair(npc, row);
            // An unreadable velocity is not a still body: it ends the run rather than extending it,
            // because a run bridged across a cell nobody could read claims stillness nobody saw.
            bool still = alive && bodyVelocity is { } b && Motion.Length(b) < Motion.BodyStillPxPerTick;

            // A gap in the ticks ends a run as surely as movement does: the rows either side are not
            // consecutive moments, so a stretch across them was never observed as one stretch.
            if (run > 0 && (now is null || previousTick is null || now != previousTick + 1)) CloseRun();
            if (still) run++;
            else CloseRun();
            previousTick = now;

            if (!alive || ReadPlay.Pair(player, row) is not { } p || Motion.Length(p) <= Motion.PlayerMovingPxPerTick || bodyVelocity is null) continue;
            moving++;
            if (!still) continue;
            stillWhileMoving++;
            string owner = source.Text[row] is { Length: > 0 } named ? named : "-";
            byOwner[owner] = byOwner.GetValueOrDefault(owner) + 1;
        }
        CloseRun();

        if (moving == 0)
            yield return PlayRow.Skipped(Name + "/share-still-while-player-moves",
                $"the player never moved faster than {Motion.PlayerMovingPxPerTick} px/tick while alive in this capture, so stillness while he moved was never exercised");
        else
        {
            yield return PlayRow.Share(Name + "/share-still-while-player-moves", stillWhileMoving, moving, "down",
                $"alive ticks where the player moved faster than {Motion.PlayerMovingPxPerTick} px/tick and the orb moved slower than {Motion.BodyStillPxPerTick}", Motion.Tag);
            foreach ((string owner, int count) in byOwner)
                yield return PlayRow.Count($"{Name}/still-by-owner/{owner}", count, "ticks", "down",
                    $"of the {stillWhileMoving:n0} still-while-the-player-moves ticks, those whose control_source was {owner}", Motion.Tag);
        }
        yield return PlayRow.Count(Name + "/player-moving-ticks", moving, "ticks", null,
            "alive ticks where the player moved, which is the denominator of the share above", Motion.Tag);
        yield return PlayRow.Count(Name + "/runs", runs.Count, "runs", "down",
            $"maximal stretches of at least {Motion.ShortestStillRunTicks} consecutive alive ticks with the orb still, whether or not the player moved; a dead or downed tick, a moving tick or a gap in the ticks ends one", Motion.Tag);
        yield return PlayRow.Count(Name + "/longest-run", runs.Count == 0 ? 0 : runs.Max(), "ticks", "down",
            "the longest of those stretches, in ticks; sixty is one second", Motion.Tag);
    }
}

/// <summary>
/// How far the orb was from the player while he moved, at the median and at the tail.
///
/// The distance is straight-line from <c>npc_px</c> to <c>player_px</c>, and those are not the same
/// point on their bodies: the orb's is its centre, the player's is his <c>Bottom</c>. So the number
/// carries half the player's height in its vertical part, about twenty-one pixels, on every row
/// alike — which cancels in any comparison between two captures and is why it is measured as
/// written rather than corrected here, where a correction would be a second convention to keep.
/// </summary>
public sealed class MeasureDistanceWhilePlayerMoves : IMeasure
{
    public string Name => "distance";
    public string[] Needs => new[] { "state", "player_vel", "npc_px", "player_px", Motion.OrbWitness };

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column state = session["state"], velocity = session["player_vel"], npc = session["npc_px"], player = session["player_px"];
        var distances = new List<double>();
        for (int row = 0; row < session.Count; row++)
        {
            if (!ReadPlay.Alive(state, row) || ReadPlay.Pair(velocity, row) is not { } v || Motion.Length(v) <= Motion.PlayerMovingPxPerTick) continue;
            if (ReadPlay.Pair(npc, row) is not { } a || ReadPlay.Pair(player, row) is not { } b) continue;
            distances.Add(Motion.Length((a.X - b.X, a.Y - b.Y)));
        }
        if (distances.Count == 0)
        {
            yield return PlayRow.Skipped(Name + "/while-player-moves-p50", "the player never moved while alive in this capture, so there is no distance to take while he moved");
            yield break;
        }
        const string how = "floor-of-rank percentile over alive ticks where the player moved faster than 1 px/tick; orb centre to the player's feet";
        yield return PlayRow.Count(Name + "/while-player-moves-p50", Math.Round(ReadPlay.FloorRank(distances, 0.5), 2), "px", "down",
            $"median distance, {how}, {distances.Count:n0} ticks", Motion.Tag);
        yield return PlayRow.Count(Name + "/while-player-moves-p90", Math.Round(ReadPlay.FloorRank(distances, 0.9), 2), "px", "down",
            $"90th-percentile distance, {how}, {distances.Count:n0} ticks", Motion.Tag);
    }
}

/// <summary>
/// How rough the orb's motion is from one tick to the next: how sharply its heading turns while it
/// travels, and how abruptly its speed changes around any movement at all.
///
/// Stop-start bursts are the shape these catch that a stillness share cannot, because a body that
/// lurches to full speed and brakes to nothing every half second can spend very little time still.
/// Both are read over *pairs* of rows and a pair counts only when both rows are alive and one tick
/// apart, so a death, a downing or a gap in the record never contributes a jump that the body did
/// not make. The percentile is the floor of the rank, and the tail is the number rather than the
/// mean, because a smooth body with rare sharp corners and a jittery one can share a mean.
/// </summary>
public sealed class MeasureMotionSmoothness : IMeasure
{
    public string Name => "smoothness";
    public string[] Needs => new[] { "tick", "state", "npc_vel", Motion.OrbWitness };

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column tick = session["tick"], state = session["state"], npc = session["npc_vel"];
        var headings = new List<double>();
        var speedChanges = new List<double>();
        for (int row = 1; row < session.Count; row++)
        {
            if (!ReadPlay.Alive(state, row) || !ReadPlay.Alive(state, row - 1)) continue;
            if (ReadPlay.Tick(tick, row) is not { } now || ReadPlay.Tick(tick, row - 1) is not { } before || now != before + 1) continue;
            if (ReadPlay.Pair(npc, row - 1) is not { } a || ReadPlay.Pair(npc, row) is not { } b) continue;
            double speedA = Motion.Length(a), speedB = Motion.Length(b);
            if (speedA >= Motion.HeadingSpeedFloorPxPerTick && speedB >= Motion.HeadingSpeedFloorPxPerTick)
            {
                // Atan2 of the cross and dot products rather than an arccosine of their ratio, which
                // loses precision near zero degrees — exactly where a smooth body's turns sit.
                double cross = a.X * b.Y - a.Y * b.X, dot = a.X * b.X + a.Y * b.Y;
                headings.Add(Math.Abs(Math.Atan2(cross, dot)) * 180.0 / Math.PI);
            }
            if (speedA >= Motion.BodyStillPxPerTick || speedB >= Motion.BodyStillPxPerTick)
                speedChanges.Add(Math.Abs(speedB - speedA));
        }

        if (headings.Count == 0)
            yield return PlayRow.Skipped(Name + "/heading-change-p90",
                $"no two consecutive alive ticks both had the orb faster than {Motion.HeadingSpeedFloorPxPerTick} px/tick, so no heading change was measured");
        else
        {
            yield return PlayRow.Count(Name + "/heading-change-p90", Math.Round(ReadPlay.FloorRank(headings, 0.9), 2), "deg/tick", "down",
                $"90th-percentile turn between consecutive alive ticks with the orb faster than {Motion.HeadingSpeedFloorPxPerTick} px/tick on both, floor-of-rank over {headings.Count:n0} pairs", Motion.Tag);
            yield return PlayRow.Count(Name + "/heading-change-p99", Math.Round(ReadPlay.FloorRank(headings, 0.99), 2), "deg/tick", "down",
                $"99th-percentile turn over the same {headings.Count:n0} pairs", Motion.Tag);
        }

        if (speedChanges.Count == 0)
            yield return PlayRow.Skipped(Name + "/speed-change-p90",
                $"no two consecutive alive ticks had the orb faster than {Motion.BodyStillPxPerTick} px/tick on either, so no speed change was measured");
        else
            yield return PlayRow.Count(Name + "/speed-change-p90", Math.Round(ReadPlay.FloorRank(speedChanges, 0.9), 2), "px/tick²", "down",
                $"90th-percentile change of orb speed between consecutive alive ticks where either was faster than {Motion.BodyStillPxPerTick} px/tick, floor-of-rank over {speedChanges.Count:n0} pairs", Motion.Tag);
    }
}

/// <summary>
/// How much of the time a shared safety response owned the body instead of the activity the
/// chooser picked.
///
/// A row is filed for every safety owner whether or not it held a single tick, and that is the
/// point of the fixed set: on 15 September 2026 safety became a layer on the job rather than a
/// response that takes the body, so combat spacing and the collision reflex no longer exist, and a
/// row that vanished with them would read on the scoreboard as a case nobody measured rather than
/// as a share that fell to zero. Beside them, one row counts the ticks the evade layer bent the
/// job's own controls, which is how often safety acted without taking the body at all.
/// </summary>
public sealed class MeasureSafetyShare : IMeasure
{
    public string Name => "safety";
    public string[] Needs => new[] { "state", "control_source" };

    /// <summary>
    /// Every owner a safety response has ever taken the body under, and all three are retired: no safety response takes the
    /// body any more. <c>combat-spacing</c> and <c>combat-reflex</c> went when safety became a layer on the job, and
    /// <c>survival-escape</c> went when the owner ruled on 15 September 2026 that every liquid is air to the orb, which left
    /// the escape nothing to leave. They are still counted because captures made before then hold them, the pinned first orb
    /// play among them. The self-test's producer pin requires every one to be declared retired, to be issued nowhere by the
    /// coordinator, and to stay a suspending owner the grant rules classify, so a revived response is classified on purpose
    /// rather than read as a zero here for ever.
    /// </summary>
    public static readonly string[] SafetyOwners = { "combat-spacing", "combat-reflex", "survival-escape" };

    /// <summary>The owners safety no longer issues, still counted for older captures.</summary>
    public static readonly string[] RetiredOwners = { "combat-spacing", "combat-reflex", "survival-escape" };

    /// <summary>The ordinary owner the coordinator records on a tick the evade layer bent; the self-test pins it as ordinary.</summary>
    public const string EvadeOwner = "evade";

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column state = session["state"], source = session["control_source"];
        int alive = 0, bent = 0;
        var held = SafetyOwners.ToDictionary(owner => owner, _ => 0, StringComparer.Ordinal);
        for (int row = 0; row < session.Count; row++)
        {
            if (!ReadPlay.Alive(state, row)) continue;
            alive++;
            if (held.ContainsKey(source.Text[row])) held[source.Text[row]]++;
            else if (string.Equals(source.Text[row], EvadeOwner, StringComparison.Ordinal)) bent++;
        }
        foreach (string owner in SafetyOwners)
            yield return PlayRow.Share($"{Name}/share-of-ticks/{owner}", held[owner], alive, "down",
                $"alive ticks on which the safety response {owner} owned the body", Motion.Tag);
        // Neither direction is better on its own: a bent tick is a hit avoided without dropping the job, and a high share is a
        // body that rarely flies its job in a straight line. It is read beside the hits the companion took, never alone.
        yield return PlayRow.Share($"{Name}/share-of-ticks-bent/{EvadeOwner}", bent, alive, "none",
            "alive ticks on which the evade layer bent the job's own controls away from a predicted hit, the job keeping the body", Motion.Tag);
    }
}
