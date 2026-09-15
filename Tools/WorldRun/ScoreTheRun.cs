extern alias live;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using Reach = live::AICompanion.Companion.Brain.Infrastructure.Observation.ReachVerdict;

/// <summary>
/// What a world run claims, as rows.
///
/// Three things are asked and they are deliberately in this order, because each one is worthless
/// without the one before it. Determinism first: if one route run twice produces two different
/// runs, every other row of that run is a coin toss and the plan says so outright. Then the
/// recorded comparison, which is a calibration rather than a pass — it says how far this build
/// walks before it stops being the build that made the recording. Then the checkpoints, which are
/// the only row that tries to say whether the companion could go where the player went.
///
/// Nothing here grades a measure. A measure carries a number and its direction and the scoreboard
/// judges it against a baseline; a threshold invented in this file would collapse "ever green" into
/// "green now" and could not tell a flake from a regression, which is the thing the ledger exists
/// to keep separate.
/// </summary>
internal static class ScoreTheRun
{
    public const string Instrument = "world-run";

    /// <summary>
    /// How far apart two tracks may be before they count as having diverged, in pixels.
    ///
    /// One body width, the orb's diameter, because that is the smallest separation that means
    /// anything physically: two positions closer than a body are the same body in the same place,
    /// and the first tick at which they are further apart than that is the first tick a watcher
    /// would see two companions rather than one.
    /// </summary>
    private const float ABodyWidth = live::AICompanion.Companion.Brain.Infrastructure.Movement.CircleContact.Diameter;

    /// <summary>
    /// A reference distance printed beside every checkpoint and never its verdict: the follow objective's own vertical
    /// comfort, measured to the nearest point of the player's box. It was the pass line until 15 September 2026, when the
    /// orb began moving about the player's whole region instead of hovering at his side, and a body inside the region is
    /// with him far outside this distance — the recorded route at 615ab50 missed two checkpoints on dry ground by it
    /// (furthest closest approach 138 px) while the same build reached all twenty at 110394a (77.7 px). The distance stays
    /// as the furthest-approach measure because a run that stops coming near the player at all is still worth seeing.
    /// </summary>
    private static float CheckpointReach => live::AICompanion.Companion.Brain.Infrastructure.Selection.Weights.FollowVerticalComfort;

    /// <summary>The player's box at a recorded feet point: the standing player's own width and height.</summary>
    private static Rectangle PlayerBoxAt(Vector2 feet) => new((int)(feet.X - 10f), (int)(feet.Y - 42f), 20, 42);

    private static float NearestDistance(Vector2 point, Rectangle box)
    {
        float dx = MathF.Max(box.Left - point.X, MathF.Max(0f, point.X - box.Right));
        float dy = MathF.Max(box.Top - point.Y, MathF.Max(0f, point.Y - box.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Two passes of one route must produce one run.
    ///
    /// The comparison is on the trace rather than on position alone, because two runs can stand in
    /// the same place for a hundred ticks while disagreeing about what they are doing, and the
    /// disagreement is what would make every later row unrepeatable. The row reports where they
    /// first parted and by how much, so a red is a lead rather than a verdict.
    /// </summary>
    public static int Determinism(string suite, RunTheWorld.Outcome first, RunTheWorld.Outcome second)
    {
        if (first.TraceHash == second.TraceHash)
        {
            EmitLedgerRows.Pass(Instrument, suite, "one route run twice produces one run",
                $"{first.Ticks} ticks, trace hash {first.TraceHash} both times",
                mode: "unbounded-allowances",
                killedBy: "leaving the light scanner's per-process random seed unpinned, or skipping the route-memory and terrain-revision reset between passes");
            return 0;
        }

        int tick = FirstDisagreement(first.Trace, second.Trace);
        float pixels = tick < Math.Min(first.CompanionCentres.Count, second.CompanionCentres.Count)
            ? Vector2.Distance(first.CompanionCentres[tick], second.CompanionCentres[tick]) : float.NaN;
        EmitLedgerRows.Fail(Instrument, suite, "one route run twice produces one run",
            $"the two passes first disagree at step {tick} of {first.Ticks}, {pixels:0.0} px apart; "
            + $"pass one traced {Line(first.Trace, tick)} and pass two traced {Line(second.Trace, tick)}; "
            + "every other row of this run is unconfirmed",
            mode: "unbounded-allowances");
        return 1;
    }

    /// <summary>
    /// How far this build's companion walks before it stops being the companion that made the
    /// recording.
    ///
    /// This is a measure and never a verdict, and the reason is in the numbers rather than in
    /// caution. The source has moved a long way since either capture was recorded, so the run is
    /// *expected* to diverge; a pass line here would be asserting that this build should behave
    /// like an older one. What the number is good for is calibration in the plan's sense: it says
    /// how much of a recorded route this instrument can reproduce at all, and a build that diverges
    /// at the first tick is telling you the instrument is wrong rather than the build.
    ///
    /// The world is a second reason the number is soft. A saved world is the state at the end of
    /// the session, so terrain the player mined after the capture's last row — and every torch
    /// placed since — is present here and was not present then.
    /// </summary>
    public static void RecordedTrackDivergence(string suite, ReadRecordedRoute.Route route, RunTheWorld.Outcome run, string worldNote)
    {
        int steps = Math.Min(route.Count, run.CompanionCentres.Count);
        int firstApart = -1;
        double total = 0, recordedPath = 0, runPath = 0;
        float worst = 0;
        for (int i = 0; i < steps; i++)
        {
            // How far each body actually travelled, because the divergence figure is meaningless
            // without it and reads as its opposite. A window in which the recorded companion never
            // moved — and this capture has long stretches of exactly that, which is the defect the
            // play was reported for — produces a divergence near zero for a run that also stood
            // still, and that number would otherwise be read as a faithful reproduction. The two
            // path lengths are what tell a reader whether anything was reproduced at all.
            if (i > 0)
            {
                recordedPath += Vector2.Distance(route[i].CompanionCentre, route[i - 1].CompanionCentre);
                runPath += Vector2.Distance(run.CompanionCentres[i], run.CompanionCentres[i - 1]);
            }
            // Both are centres: the recorded column and the run's track name the same point of the body.
            float apart = Vector2.Distance(route[i].CompanionCentre, run.CompanionCentres[i]);
            total += apart;
            worst = Math.Max(worst, apart);
            if (firstApart < 0 && apart > ABodyWidth) firstApart = route[i].Tick;
        }

        string moved = recordedPath < ABodyWidth
            ? $"THE RECORDED BODY BARELY MOVED ({recordedPath:0} px over {steps} ticks), so a small divergence here is not a reproduction of anything"
            : $"the recorded body travelled {recordedPath:0} px and this run travelled {runPath:0} px";
        string note = $"{route.Capture} recorded at {route.SourceRevision[..Math.Min(7, route.SourceRevision.Length)]}, schema {route.Schema}; "
            + $"{steps} ticks from {route[0].Tick}; world {worldNote}; {moved}; "
            + $"first parted by more than a body at tick {(firstApart < 0 ? "never" : firstApart.ToString(CultureInfo.InvariantCulture))}, worst {worst:0} px";

        EmitLedgerRows.Measure(Instrument, suite, "mean distance from the recorded companion track",
            steps == 0 ? 0 : total / steps, "px", direction: null, mode: "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "ticks reproduced before the track parts by a body",
            firstApart < 0 ? steps : firstApart - route[0].Tick, "ticks", direction: "up",
            mode: "unbounded-allowances", message: note);
        // The reach sense's verdict boundary is measured from its flood's root, and a travelling body can
        // outrun the root; on the first disc-bounded tree the body sat outside its own known radius for
        // 152 consecutive ticks of this route while the replacement flood grew on the cadence.
        EmitLedgerRows.Measure(Instrument, suite, "ticks the body sat outside the reach sense's known radius",
            run.TicksOutsideKnownRadius, "ticks", direction: "down", mode: "unbounded-allowances",
            message: note + "; nothing near the body can be proven absent on such a tick");
        EmitLedgerRows.Measure(Instrument, suite, "ticks the reach flood read complete",
            run.TicksReachComplete, "ticks", direction: "up", mode: "unbounded-allowances", message: note);
        // The denominator of the row above, emitted as its own row rather than only as prose,
        // because a comparison whose baseline is zero movement has to be visible on the scoreboard
        // and not only to someone who read the message.
        EmitLedgerRows.Measure(Instrument, suite, "distance the recorded companion travelled in this window",
            recordedPath, "px", direction: null, mode: "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "distance this run's companion travelled in this window",
            runPath, "px", direction: null, mode: "unbounded-allowances", message: note);
    }

    /// <summary>
    /// Whether the companion could go where the player went.
    ///
    /// A checkpoint is a tile the player's own track stood on, taken at a cadence rather than every
    /// tick, because consecutive ticks are the same tile and would weight a place the player
    /// loitered in as heavily as a whole journey.
    ///
    /// The filter is the row's reason for existing, and it is the plan's hardest rule: a place the
    /// player reached by wings is not evidence that the companion failed. The kits are read from
    /// the capture's own header and never inferred from the track, so a capture written before that
    /// header existed produces a skip rather than a guess — which is why both of the captures this
    /// instrument was first run against skip here. The counts are still emitted as measures,
    /// because a count is not a verdict and a reader is better off with the numbers plus an honest
    /// "this was not judged" than with nothing.
    /// </summary>
    public static void Checkpoints(string suite, ReadRecordedRoute.Route route, RunTheWorld.Outcome run, int cadence)
    {
        int arrived = 0, missed = 0, plannerSaidNo = 0, plannerUnfinished = 0, underLiquid = 0, total = 0;
        float furthestApproach = 0f;
        int steps = Math.Min(route.Count, run.CompanionCentres.Count);
        var visited = new HashSet<Point>();
        // Water and lava are walls to the orb until a mastery immunity opens them, and they hurt on
        // touch, so a checkpoint the player stood in while swimming is outside the companion's kit
        // in exactly the way a dash is: the body refusing it is the design, not a miss. The flags
        // come out of the run, read off the live body while it existed; the body is gone by now.
        bool immuneToWater = run.ImmuneToWater, immuneToLava = run.ImmuneToLava;

        for (int i = 0; i < steps; i += cadence)
        {
            Point tile = route[i].PlayerFeet.ToTileCoordinates();
            if (!visited.Add(tile)) continue;
            total++;

            // Reached is being with the player while he stood there: on some tick of this checkpoint's own stretch of track,
            // from its step to the next cadence step, the body's centre sat inside his region and the sense read it
            // connected to him. The stretch rather than the whole run, because one entry into the region would otherwise
            // satisfy every checkpoint the run ever passed; the stretch rather than the single tick, because the region is
            // carried ahead of him by his own pace and a body moving about it is on its far side on some ticks. The closest
            // approach to his box over the whole run is still printed and measured, as a reference and not a verdict.
            Rectangle body = PlayerBoxAt(route[i].PlayerFeet);
            float closest = float.PositiveInfinity;
            for (int j = 0; j < steps; j++)
                closest = MathF.Min(closest, NearestDistance(run.CompanionCentres[j], body));
            furthestApproach = MathF.Max(furthestApproach, closest);
            int end = Math.Min(steps, i + cadence), insideTicks = 0, connectedTicks = 0, withTicks = 0;
            for (int j = i; j < end; j++)
            {
                if (run.InsideRegion[j]) insideTicks++;
                if (run.ConnectedToPlayer[j]) connectedTicks++;
                if (run.InsideRegion[j] && run.ConnectedToPlayer[j]) withTicks++;
            }
            bool reached = withTicks > 0;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"CHECKPOINT {total,2} at recorded tick {route[i].Tick} tile {tile.X},{tile.Y}: inside the region on {insideTicks} of {end - i} ticks, "
                + $"connected on {connectedTicks}, both on {withTicks}; closest approach to his box over the run {closest:0.0} px -> {(reached ? "reached" : "MISSED")}"));

            if (reached) { arrived++; continue; }
            missed++;
            if (run.PlannerClaim[i] == Reach.Unreachable) plannerSaidNo++;
            else if (run.PlannerClaim[i] == Reach.NotYet) plannerUnfinished++;
            Tile stood = Main.tile[tile.X, tile.Y];
            bool forbidden = stood.LiquidAmount > 0 && stood.LiquidType switch
            {
                Terraria.ID.LiquidID.Water => !immuneToWater,
                Terraria.ID.LiquidID.Lava => !immuneToLava,
                _ => false, // honey and shimmer only slow the orb
            };
            if (forbidden) underLiquid++;
        }

        string note = $"{total} checkpoints every {cadence} ticks along {route.Capture}, reached when the body is inside the player's region and connected to him on some tick of the checkpoint's own {cadence}-tick stretch";
        EmitLedgerRows.Measure(Instrument, suite, "checkpoints the body reached", arrived, "checkpoints", "up", "unbounded-allowances", message: note);
        EmitLedgerRows.Measure(Instrument, suite, "checkpoints the body never reached", missed, "checkpoints", "down", "unbounded-allowances", message: note);
        // The furthest any checkpoint sat from the orb at the orb's nearest approach over the whole run. It is no longer the
        // verdict — a body moving about the player's region is with him well beyond it — but a run whose body stops coming
        // near the player at all shows here first.
        EmitLedgerRows.Measure(Instrument, suite, "furthest closest approach to any checkpoint", furthestApproach, "px", "down", "unbounded-allowances",
            message: note + $"; measured to the player's box, beside the follow comfort of {CheckpointReach:0} px that was the pass line before the orb moved about his whole region");
        EmitLedgerRows.Measure(Instrument, suite, "unreached checkpoints the planner called unreachable", plannerSaidNo, "checkpoints", "down", "unbounded-allowances",
            message: note + "; the reach sense's verdict on the tile the player's body occupied; a missing edge unless the player's kit explains it");
        EmitLedgerRows.Measure(Instrument, suite, "unreached checkpoints whose flood never finished", plannerUnfinished, "checkpoints", "down", "unbounded-allowances",
            message: note + "; the reach sense had not finished flooding, or the tile lay beyond its known radius, when the player stood there");
        EmitLedgerRows.Measure(Instrument, suite, "unreached checkpoints under water or lava the companion may not enter", underLiquid, "checkpoints", "down", "unbounded-allowances",
            message: note + "; the player swam there and the companion has no immunity for that liquid, so refusing it is the design");

        if (!route.Kits.Known)
        {
            EmitLedgerRows.Skipped(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"{route.Capture} carries no capabilities header, so the player's kit is unknown; "
                + "the plan refuses inferring abilities from the track, and without the kit an unreached checkpoint "
                + "cannot be told apart from one the player flew to. The counts above stand; the verdict does not.",
                tags: new[] { "requires-capabilities-header" });
            return;
        }

        string[] beyond = AbilitiesThePlayerHadAndTheCompanionDoesNot(route.Kits);
        if (beyond.Length > 0)
        {
            EmitLedgerRows.Skipped(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"the player carried {string.Join(" and ", beyond)} and the companion's declared kit does not "
                + $"({DescribeTheCompanionsDeclaredKit()}); every unreached checkpoint is filtered, because nothing "
                + "says which checkpoint needed the ability and the plan refuses inferring that from the track. "
                + $"The capture's line was {route.Kits.Raw}",
                tags: new[] { "outside-the-envelope" });
            return;
        }

        if (missed == underLiquid)
            EmitLedgerRows.Pass(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"the player reached every checkpoint with a kit the companion's own declaration covers "
                + $"({route.Kits.Raw}); the body reached {arrived} of {total} checkpoints and the {underLiquid} it did not "
                + "were under water or lava it may not enter",
                mode: "unbounded-allowances",
                killedBy: "counting a checkpoint as reached on the tick the player stood there rather than at any point in the run");
        else
            EmitLedgerRows.Fail(Instrument, suite, "every unreached checkpoint is outside the companion's kit or a real miss",
                $"the player reached {total} checkpoints with nothing the companion's kit lacks ({route.Kits.Raw}) "
                + $"and the body missed {missed} of them, {underLiquid} under a liquid it may not enter and {missed - underLiquid} on dry ground; "
                + $"the planner called {plannerSaidNo} unreachable and had not finished flooding {plannerUnfinished}",
                mode: "unbounded-allowances");
    }

    /// <summary>
    /// The abilities the recorded player carried that the companion's body does not answer — which
    /// is what "filtered" means, rather than "the player left the ground".
    ///
    /// The orb flies, so anything that carries a player through air — a mount, wings, rocket boots
    /// — is answered by the body itself and filters nothing; what the orb has no answer to is a
    /// dash, which is a burst of pace the motor's cap does not allow. A swim has no player flag to
    /// read and is a liquid immunity on the orb's side rather than a movement, so it is judged per
    /// checkpoint in <see cref="Checkpoints"/> instead: a checkpoint under water or lava the orb may
    /// not enter is excused by name rather than filtering the whole route.
    /// </summary>
    private static string[] AbilitiesThePlayerHadAndTheCompanionDoesNot(ReadRecordedRoute.Kits kits)
        => kits.PlayerDash ? new[] { "a dash" } : Array.Empty<string>();

    private static string DescribeTheCompanionsDeclaredKit()
        => "flying-orb: fly=True, dash=False, water and lava by per-character immunity";

    private static int FirstDisagreement(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        int shared = Math.Min(first.Count, second.Count);
        for (int i = 0; i < shared; i++)
            if (first[i] != second[i]) return i;
        return shared;
    }

    private static string Line(IReadOnlyList<string> trace, int index)
        => index < trace.Count ? trace[index] : "<no such step>";
}
