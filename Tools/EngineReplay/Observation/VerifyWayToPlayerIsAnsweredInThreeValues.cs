extern alias live;

using Microsoft.Xna.Framework;
using AICompanion.Tools.Ledger;

using ClearanceField = live::AICompanion.Companion.Brain.Infrastructure.Movement.ClearanceField;
using CornerGraph = live::AICompanion.Companion.Brain.Infrastructure.Movement.CornerGraph;
using DistanceMode = live::AICompanion.Companion.PlayerIntegration.CompanionDistanceMode;
using FreeSpaceSearch = live::AICompanion.Companion.Brain.Infrastructure.Movement.FreeSpaceSearch;
using ITileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.ITileWorld;
using LimitPlanningWork = live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork;
using MovementQueries = live::AICompanion.Companion.Brain.Infrastructure.Movement.MovementQueries;
using Navigator = live::AICompanion.Companion.Brain.Infrastructure.Movement.Navigator;
using PlayerIntentRegionSense = live::AICompanion.Companion.Brain.Infrastructure.Observation.PlayerIntentRegionSense;
using Preferences = live::AICompanion.Companion.PlayerIntegration.CompanionPreferences;
using ReachVerdict = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;
using TextTileWorld = live::AICompanion.Companion.Brain.Infrastructure.Movement.TextTileWorld;
using Weights = live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights;

/// <summary>
/// Whether the body has a way to the player inside his region is answered in three values, from a flood that is resumed on every
/// tick until it finishes, and never read as a proof before it has. A sentinel's review of the company lane found the first build ran
/// that flood to exhaustion in one call on every tick the player's corner or the snapped region changed — four to ten milliseconds of
/// the tick's twelve, rebuilt on more than half the ticks of a walking player — and read a flood the live deadline had stopped as
/// connected, never resuming it, for as long as the player stood still.
///
/// <para>Each scene drives the sense's own numeric update and its way-to-the-player step over a text world, the way the brain tick
/// drives them. Pass lines, declared before the fix:</para>
/// <list type="bullet">
/// <item>a standing player whose region holds more corners than one slice closes: on the first tick the answer is not yet, and it
/// becomes reachable within one tick more than the slices the flood needs, then stays reachable;</item>
/// <item>a body inside the region's box behind a sealed wall: never reachable on any tick, and cut off with the latch lost by the
/// end;</item>
/// <item>a walking player with the body beside him in open air: never cut off on any tick, and after the first proof never
/// unanswered for longer than a flood's worth of slices running.</item>
/// </list>
/// <para>The cost is measured under production allowances beside a reconstruction of the first build's rebuild-to-exhaustion, and
/// filed as measures rather than asserted, because a millisecond figure depends on the machine.</para>
/// </summary>
internal static class VerifyWayToPlayerIsAnsweredInThreeValues
{
    private const string Instrument = "engine-replay", Suite = "EngineReplay";

    public static int Run()
    {
        var preferences = Preferences.Current;
        DistanceMode held = preferences.DistanceMode;
        bool lifted = LimitPlanningWork.Unbounded;
        ITileWorld? previous = null;
        try { previous = MovementQueries.World; } catch (InvalidOperationException) { }
        var failures = new List<string>();
        void Each(string scene, Action check)
        {
            try { check(); }
            catch (InvalidOperationException e) { failures.Add($"{scene}: {e.Message}"); }
        }
        try
        {
            LimitPlanningWork.Unbounded = true;
            Each("a standing player", () => { preferences.DistanceMode = DistanceMode.Free; AStandingPlayersFloodIsResumedUntilAnswered(); });
            Each("a sealed wall", () => { preferences.DistanceMode = DistanceMode.Standard; ABodyBehindASealedWallIsNeverReadReachable(); });
            Each("a walking player", () => { preferences.DistanceMode = DistanceMode.Standard; AWalkingPlayerIsNeverReadCutOff(); });
            preferences.DistanceMode = DistanceMode.Free;
            MeasureTheCostUnderProductionAllowances();
        }
        finally
        {
            preferences.DistanceMode = held;
            LimitPlanningWork.End();
            LimitPlanningWork.Unbounded = lifted;
            FreeSpaceSearch.WorldOverride = null;
            if (previous != null) MovementQueries.World = previous;
            ClearanceField.Shared.Invalidate();
        }
        if (failures.Count > 0)
        {
            Console.WriteLine($"RED way to the player: {string.Join(" | ", failures)}");
            return 1;
        }
        Console.WriteLine("way to the player: resumed until answered, never proven early, cut off behind a sealed wall and never cut off beside a walking player");
        return 0;
    }

    private static TextTileWorld Plug(int width, int height, Func<int, int, bool>? solid = null)
    {
        var rows = new List<string>();
        for (int y = 0; y < height; y++)
        {
            char[] row = new char[width];
            for (int x = 0; x < width; x++)
                row[x] = y == 0 || y == height - 1 || x == 0 || x == width - 1 || (solid?.Invoke(x, y) ?? false) ? '#' : '.';
            rows.Add(new string(row));
        }
        var world = new TextTileWorld(0, 0, rows);
        MovementQueries.World = world;
        FreeSpaceSearch.WorldOverride = world;
        ClearanceField.Shared.Invalidate();
        return world;
    }

    /// <summary>How many corners a flood of the sense's own shape closes for this region, run to exhaustion: the grown box snapped
    /// to whole tiles, rooted at the corner nearest the player. It mirrors the sense's bounds so the premise is about the flood the
    /// sense grows rather than about a guess at the region's size.</summary>
    private static int CornersInRegion(ITileWorld world, PlayerIntentRegionSense sense, Vector2 player)
    {
        var region = sense.Region;
        float grow = Navigator.SettleRadius + 16f;
        int left = (int)MathF.Floor((region.Centre.X - region.HalfSize.X - grow) / 16f) * 16;
        int top = (int)MathF.Floor((region.Centre.Y - region.HalfSize.Y - grow) / 16f) * 16;
        int right = (int)MathF.Ceiling((region.Centre.X + region.HalfSize.X + grow) / 16f) * 16;
        int bottom = (int)MathF.Ceiling((region.Centre.Y + region.HalfSize.Y + grow) / 16f) * 16;
        var flood = new FreeSpaceSearch(world, CornerGraph.NearestUsable(world, player, 2)!.Value, null, priceClearance: false)
        {
            Bounds = new Rectangle(left, top, right - left + 1, bottom - top + 1),
        };
        flood.Advance(int.MaxValue);
        return flood.Reached.Count;
    }

    private static void AStandingPlayersFloodIsResumedUntilAnswered()
    {
        var world = Plug(300, 120);
        var sense = new PlayerIntentRegionSense();
        Vector2 player = new(150 * 16 + 8, 60 * 16), body = player + new Vector2(-64f, -40f);
        // Held, not walking: since the reshape the lead chases the held key rather than the observed
        // pace, so a standing player with no held direction reads no lead and no growth, and the base
        // region holds fewer corners than one slice closes. Holding a direction slides the box with no
        // tile progress, the way the reshape says it does, and the led grown region is what the flood
        // is resumed across. The assertions below are unchanged: they are about the flood, not the lead.
        Vector2 held = new(Weights.IntentRegionHeldPace, 0f);
        for (int tick = 0; tick < 600; tick++)
            sense.Update(body, player, Vector2.Zero, false, false, 60, held);
        int corners = CornersInRegion(world, sense, player);
        int slices = (int)MathF.Ceiling(corners / (float)Weights.PlayerSideFloodExpansions);
        Require(slices >= 2, $"premise: the region must hold more corners than one slice closes, or nothing is unfinished; {corners} corners against {Weights.PlayerSideFloodExpansions}");
        var verdicts = new List<ReachVerdict>();
        for (int tick = 0; tick < 20; tick++)
        {
            sense.Update(body, player, Vector2.Zero, false, false, 60, held);
            verdicts.Add(sense.ObserveWayToPlayer(body, player));
        }
        string ledger = $"{corners} corners, {slices} slices; verdicts {string.Join(",", verdicts.Select(v => v.ToString()[0]))}";
        Require(verdicts[0] == ReachVerdict.NotYet, $"an unfinished flood must answer not yet on the first tick; {ledger}");
        int first = verdicts.IndexOf(ReachVerdict.Reachable);
        Require(first >= 0 && first <= slices, $"the flood must be resumed until it is answered, reachable within {slices + 1} ticks; {ledger}");
        Require(verdicts.Skip(first).All(v => v == ReachVerdict.Reachable), $"a standing player's answered flood must stay answered; {ledger}");
        Console.WriteLine($"way to the player: a standing player's flood answered on tick {first}; {ledger}");
    }

    private static void ABodyBehindASealedWallIsNeverReadReachable()
    {
        const int wallColumn = 147;
        var world = Plug(300, 120, (x, _) => x == wallColumn);
        var sense = new PlayerIntentRegionSense();
        Vector2 player = new(150 * 16 + 8, 60 * 16), body = new((wallColumn - 3) * 16 + 8, 60 * 16 - 40);
        var verdicts = new List<ReachVerdict>();
        bool inBox = false;
        for (int tick = 0; tick < 20; tick++)
        {
            sense.Update(body, player, Vector2.Zero, false, false, 60);
            inBox = sense.Region.Contains(body);
            verdicts.Add(sense.ObserveWayToPlayer(body, player));
        }
        string ledger = $"verdicts {string.Join(",", verdicts.Select(v => v.ToString()[0]))}; body in the box {inBox}; inside {sense.Inside}";
        Require(inBox, $"premise: the body must be inside the region's box, or the wall decides nothing; {ledger}");
        Require(!verdicts.Contains(ReachVerdict.Reachable), $"a body behind a sealed wall must never be read reachable; {ledger}");
        Require(verdicts[^1] == ReachVerdict.Unreachable && !sense.Inside, $"a body behind a sealed wall must end cut off and not with the player; {ledger}");
        Console.WriteLine($"way to the player: behind a sealed wall; {ledger}");
    }

    private static void AWalkingPlayerIsNeverReadCutOff()
    {
        var world = Plug(400, 120);
        var sense = new PlayerIntentRegionSense();
        Vector2 start = new(60 * 16 + 8, 60 * 16), step = new(3f, 0f);
        sense.Update(start + new Vector2(-48f, -48f), start, step, true, false, 60);
        int slices = (int)MathF.Ceiling(CornersInRegion(world, sense, start) / (float)Weights.PlayerSideFloodExpansions);
        int cutOff = 0, notYet = 0, longestUnanswered = 0, run = 0, first = -1;
        for (int tick = 0; tick < 600; tick++)
        {
            Vector2 player = start + step * tick, body = player + new Vector2(-48f, -48f);
            sense.Update(body, player, step, true, false, 60);
            ReachVerdict verdict = sense.ObserveWayToPlayer(body, player);
            if (verdict == ReachVerdict.Unreachable) cutOff++;
            if (verdict == ReachVerdict.Reachable && first < 0) first = tick;
            if (first >= 0 && verdict == ReachVerdict.NotYet) { notYet++; run++; longestUnanswered = Math.Max(longestUnanswered, run); }
            else run = 0;
        }
        string ledger = $"first proof at tick {first}; cut off on {cutOff} ticks; unanswered on {notYet} ticks after it, longest run {longestUnanswered}; a flood takes {slices} slices";
        Require(first >= 0 && cutOff == 0, $"a body beside a walking player in open air must be proven reachable and never read cut off; {ledger}");
        Require(longestUnanswered <= slices + 1, $"after the first proof the answer must never be missing for longer than a flood's worth of slices; {ledger}");
        Console.WriteLine($"way to the player: beside a walking player; {ledger}");
    }

    /// <summary>
    /// The flood's cost per tick under the live deadline, for a player walking at a running pace in the largest distance mode: the
    /// sense as built, and a reconstruction of the first build — a fresh flood run to exhaustion in one call whenever the player's
    /// corner or the snapped region changed — timed on the same ticks under the same deadline.
    /// </summary>
    private static void MeasureTheCostUnderProductionAllowances()
    {
        var world = Plug(900, 160);
        LimitPlanningWork.Unbounded = false;
        var sense = new PlayerIntentRegionSense();
        var reference = new PlayerIntentRegionSense();
        Vector2 start = new(80 * 16 + 8, 80 * 16), step = new(6f, 0f);
        var now = new List<double>();
        var before = new List<double>();
        int rebuilds = 0, stoppedShort = 0;
        Point? lastRoot = null;
        Rectangle lastBounds = default;
        for (int tick = 0; tick < 600; tick++)
        {
            Vector2 player = start + step * tick, body = player + new Vector2(-48f, -48f);
            LimitPlanningWork.Restart(Weights.TotalPlanningMilliseconds);
            sense.Update(body, player, step, true, false, 60);
            sense.ObserveWayToPlayer(body, player);
            now.Add(sense.LastFloodMs);
            LimitPlanningWork.End();

            LimitPlanningWork.Restart(Weights.TotalPlanningMilliseconds);
            reference.Update(body, player, step, true, false, 60);
            var region = reference.Region;
            float grow = Navigator.SettleRadius + 16f;
            int left = (int)MathF.Floor((region.Centre.X - region.HalfSize.X - grow) / 16f) * 16;
            int top = (int)MathF.Floor((region.Centre.Y - region.HalfSize.Y - grow) / 16f) * 16;
            int right = (int)MathF.Ceiling((region.Centre.X + region.HalfSize.X + grow) / 16f) * 16;
            int bottom = (int)MathF.Ceiling((region.Centre.Y + region.HalfSize.Y + grow) / 16f) * 16;
            var bounds = new Rectangle(left, top, right - left + 1, bottom - top + 1);
            Point? root = CornerGraph.NearestUsable(world, player, 2);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (root != null && (root != lastRoot || bounds != lastBounds))
            {
                var flood = new FreeSpaceSearch(world, root.Value, null, priceClearance: false) { Bounds = bounds };
                flood.Advance(int.MaxValue);
                rebuilds++;
                if (flood.Stop != FreeSpaceSearch.StopReason.Exhausted) stoppedShort++;
                lastRoot = root;
                lastBounds = bounds;
            }
            before.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            LimitPlanningWork.End();
        }
        LimitPlanningWork.Unbounded = true;
        double P(List<double> values, double share) { var sorted = values.OrderBy(v => v).ToList(); return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * share))]; }
        const string mode = "production-allowances";
        const string name = "the way to the player is resumed until it is answered and never read as a proof before it is";
        // "down" is the direction the scoreboard reads; this row said "lower" until 24 September 2026, which
        // the scoreboard prints as a measure with no direction declared good.
        string[] timed = { EmitLedgerRows.TimedTag, EmitLedgerRows.SampledTag };
        EmitLedgerRows.Measure(Instrument, Suite, name + ": worst tick, ms, as built", now.Max(), "ms", "down", mode, timed);
        EmitLedgerRows.Measure(Instrument, Suite, name + ": 90th percentile tick, ms, as built", P(now, 0.9), "ms", "down", mode, timed);
        EmitLedgerRows.Measure(Instrument, Suite, name + ": worst tick, ms, rebuilt to exhaustion", before.Max(), "ms", "down", mode, timed);
        EmitLedgerRows.Measure(Instrument, Suite, name + ": 90th percentile tick, ms, rebuilt to exhaustion", P(before, 0.9), "ms", "down", mode, timed);
        Console.WriteLine($"MEASURE way to the player cost, walking 6 px/tick on Free under the live deadline: as built worst {now.Max():0.00} ms, p90 {P(now, 0.9):0.00} ms, median {P(now, 0.5):0.00} ms; "
            + $"rebuilt to exhaustion worst {before.Max():0.00} ms, p90 {P(before, 0.9):0.00} ms, median {P(before, 0.5):0.00} ms, rebuilt on {rebuilds} of 600 ticks, stopped short of exhaustion on {stoppedShort}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
