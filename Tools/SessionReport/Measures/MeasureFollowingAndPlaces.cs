#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AICompanion.Tools.Ledger;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// Where the companion sat relative to a moving player, which is the one number the "it never
/// overtakes, it stops at the edge of the box" report reduces to.
///
/// The definition is the whole measure and it is worth stating exactly, because the first reading
/// of this capture reported the companion "ahead on none of the moving rows" and that came from a
/// reader's own filter rather than from an instrument. The offset is signed *along the player's
/// direction of travel*: positive means in front of where the player is going, negative means
/// behind. A reading that took the raw x difference would call the companion ahead whenever the
/// player walked left, which is how "ahead" stops meaning anything.
///
/// Rows count only where the player is actually travelling, because the offset between two
/// standing bodies is not a following statistic. The speed floor is the producer's own idea of
/// travelling rather than a number chosen here.
/// </summary>
public sealed class MeasureAheadShare : IMeasure
{
    public string Name => "ahead-share";
    public string[] Needs => new[] { "player_vel", "npc_px", "player_px" };

    /// <summary>
    /// Below this the player is not travelling and the pair's offset says nothing about following.
    /// It is the figure the 0.24.0 read used, kept so the before-number and the after-number are
    /// taken the same way; a different floor would produce a different denominator and the two
    /// would not be comparable.
    /// </summary>
    private const double TravellingPxPerTick = 1.2;

    /// <summary>Four tiles in front, three tiles behind. Tiles are sixteen pixels, which is a fact of the world.</summary>
    private const double AheadPx = 64, BehindPx = 48;

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column velocity = session["player_vel"], npc = session["npc_px"], player = session["player_px"];
        var offsets = new List<double>();
        for (int row = 0; row < session.Count; row++)
        {
            if (ReadPlay.Leading(velocity, row) is not { } vx || Math.Abs(vx) <= TravellingPxPerTick) continue;
            if (ReadPlay.Leading(npc, row) is not { } npcX || ReadPlay.Leading(player, row) is not { } playerX) continue;
            offsets.Add((npcX - playerX) * Math.Sign(vx));
        }

        int moving = offsets.Count;
        if (moving == 0)
        {
            yield return PlayRow.Skipped(Name + "/share-ahead-beyond-64px", "the player never travelled faster than the floor in this capture, so following was never exercised");
            yield break;
        }
        yield return PlayRow.Share(Name + "/share-ahead-beyond-64px", offsets.Count(o => o > AheadPx), moving, "up",
            "rows where the companion led the travelling player by more than four tiles", "R1", "pass-line:ahead-rows-exceed-behind-rows");
        yield return PlayRow.Share(Name + "/share-behind-beyond-48px", offsets.Count(o => o < -BehindPx), moving, "down",
            "rows where the companion trailed the travelling player by more than three tiles", "R1");
        yield return PlayRow.Count(Name + "/median-offset-px", Math.Round(ReadPlay.Median(offsets), 1), "px", "up",
            $"median signed offset along the player's travel direction over {moving:n0} moving rows; negative is behind", "R1");
        yield return PlayRow.Count(Name + "/moving-rows", moving, "rows", null,
            "rows where the player exceeded the travelling floor, which is the denominator of the two shares above", "R1");
    }
}

/// <summary>
/// How much of the time the reach flood had actually finished, which is the denominator under
/// every "it does not know it can reach that" symptom: an activity that refuses an unanswered
/// search is declining to start rather than asserting the world is empty, so a capture where the
/// flood is unfinished half the time is a capture where half the refusals say nothing about the
/// world.
/// </summary>
public sealed class MeasureReachCompleteShare : IMeasure
{
    public string Name => "reach-complete-share";
    public string[] Needs => new[] { "reach_complete" };

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column complete = session["reach_complete"];
        int settled = 0, measured = 0;
        for (int row = 0; row < session.Count; row++)
        {
            float value = complete.Number[row];
            if (float.IsNaN(value)) continue;
            measured++;
            if (value > 0.5f) settled++;
        }
        yield return PlayRow.Share(Name + "/share-complete", settled, measured, "up",
            "rows on which the reachability flood had finished, so a 'cannot reach' answer on the rest is an unfinished search rather than a proven absence",
            "R2", "pass-line:above-90-percent");
    }
}

/// <summary>
/// How often a terrain edit happened, which is the one number R2's whole argument rests on and
/// which no capture on disk can answer: <c>TerrainChanges.Revision</c> is not a recorded column.
///
/// The measure exists anyway, and skips loudly, because that is the difference between a number
/// nobody has and a number nobody asked for. The 426 "terrain edits" in the first read of the
/// 13:27 capture were terrain-snapshot captures on a fixed cadence, counted as edits; a measure
/// that names the column it wants makes the next capture either carry it or say it does not.
/// </summary>
public sealed class MeasureTerrainRevisionRate : IMeasure
{
    public string Name => "terrain-revision-rate";
    public string[] Needs => Array.Empty<string>();

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        Column? revision = session.Find("terrain_revision");
        if (revision == null)
        {
            yield return PlayRow.Skipped(Name + "/revisions-per-minute",
                "terrain_revision column: this capture predates it, so the world-edit rate cannot be read from it. "
                + "Counting terrain-snapshot occurrences instead is what produced the wrong '426 terrain edits' reading — those run on a fixed per-tick cadence and count captures, not edits");
            yield break;
        }
        int changes = 0;
        float last = float.NaN;
        for (int row = 0; row < session.Count; row++)
        {
            float value = revision.Number[row];
            if (float.IsNaN(value)) continue;
            if (!float.IsNaN(last) && value != last) changes++;
            last = value;
        }
        // Rows are one per companion AI tick, and the game runs sixty of those a second.
        double minutes = session.Count / (60.0 * 60.0);
        yield return PlayRow.Count(Name + "/revisions-per-minute", minutes <= 0 ? 0 : Math.Round(changes / minutes, 2), "per minute", "down",
            $"{changes:n0} changes of the world-global terrain revision over {session.Count:n0} rows; every one of them restarts the reach flood and invalidates every retained route", "R2");
    }
}

/// <summary>
/// Whether a journey ended where it was going, read from the census rather than from the rows.
///
/// The census is the right source and the reason is worth keeping: it counts every category
/// whether or not anything happened in it, so a request kind that was asked two hundred times and
/// reached thirteen produces a row, where a detector over the rows would need somebody to have
/// thought of asking. It is also the instrument that does not share the episode observer's own
/// boundary rule, so the two disagreeing is a free consistency check on both.
/// </summary>
public sealed class MeasureJourneysReached : IMeasure
{
    public string Name => "journeys-reached";
    public string[] Needs => Array.Empty<string>();

    public string? Missing(Session session)
        => ReadPlay.Census(session.Path) == null
            ? "behaviour census beside the capture, which is where the asked-and-reached tally per request kind lives"
            : null;

    public IEnumerable<LedgerRow> Rows(Session session)
    {
        string census = ReadPlay.Census(session.Path)!;
        bool any = false;
        foreach (string line in census.Split('\n'))
        {
            // "    WithPlayer     asked   222   reached    13   abandoned   209"
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6 || parts[1] != "asked" || parts[3] != "reached") continue;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int asked)
                || !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int reached)) continue;
            any = true;
            yield return PlayRow.Share($"{Name}/{parts[0]}", reached, asked, "up",
                $"journeys of kind {parts[0]} that reached the place they were asked for, out of {asked:n0} asked", "R8");
        }
        if (!any)
            yield return PlayRow.Skipped(Name + "/any",
                "a 'places asked for' table in the census: the file is present but holds no request-kind lines, so no journey was recorded rather than none reaching");
    }
}
