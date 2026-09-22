extern alias live;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using AICompanion.Tools.Ledger;
using live::AICompanion.Companion.CharacterBody;

/// <summary>
/// The whole brain, run for as long as it takes for a slow climb to show, and sampled once per
/// decision rather than once per tick.
///
/// <b>Why it exists.</b> The play of 0.38.13 on 22 September 2026 grew its frozen observation from 150
/// facts to 1,603 over thirty-three seconds and never fell back, with fifty second-generation
/// collections in a minute; nothing reproduced the growth headlessly and no instrument in this tree runs
/// the brain long enough to see a slow climb at all. Every fixture is seconds. The longest capture this
/// machine holds is six minutes, and the soak is meant to run for an hour, so its player has to be
/// generated — `DriveASeededScene` is that player.
///
/// <b>It is not `RunTheWorld.Play`, and the reason is the measurement.</b> That loop is the right one
/// for every row in this folder that compares two passes or scores a track, and it retains six lists
/// with one entry per tick — centres, a trace line, a planner claim, two region flags and a `PlayTick`
/// struct. Over an hour those are hundreds of megabytes of deliberate retention, which would sit inside
/// the very number this instrument reports. So this is a second loop, doing the same five things in the
/// same order, holding nothing per tick and aggregating per decision. That is a third copy of the tick
/// order in this repository and the folder guide says so; the alternative — an allocation verdict
/// measuring the harness — is worse than the duplication.
///
/// <b>The clock is the game's own.</b> Every comparison run here lifts the planning allowances so two
/// passes are comparable; this one keeps them, for the play measures' reason: the brain a player meets
/// is one being cut by its deadline, and a soak that gave it all the time it wanted would be soaking a
/// brain nobody has played. Everything timed here therefore carries the production-clock tag and is a
/// measure rather than a pass line, which is this suite's standing rule about wall clocks.
/// </summary>
internal static class RunTheSoak
{
    /// <summary>
    /// How many decisions a growth window holds.
    ///
    /// The verdict is about a trend rather than a spike, so it reads the minimum of a window rather
    /// than any single decision: one decision carrying a large census is ordinary, and a window whose
    /// *floor* has risen is the shape the play showed. Fifty is about ten seconds of decisions at the
    /// rate a soak produces them, which is long enough that a busy stretch ends inside it.
    /// </summary>
    private const int GrowthWindowDecisions = 50;

    /// <summary>
    /// How much the floor of the last window may exceed the floor of the first.
    ///
    /// <b>This is a bound taken from a measurement rather than a declaration the brain makes.</b> There
    /// is no fact budget anywhere in `Selection/` — `AuditDecisionContracts.MaximumFactsPerDecision` says
    /// the same thing about its own 512 — so what is available is the play: 150 facts climbing to 1,603
    /// and never falling. A quarter of the audit's bound catches a climb of that shape long before the
    /// audit's own tripwire would, and leaves ordinary churn alone: on the surface soak measured below,
    /// the window floors move by tens rather than hundreds.
    ///
    /// The row prints the two floors and every window's floor beside them, so a red is read as a curve
    /// rather than as a threshold.
    /// </summary>
    private const int GrowthSlackFacts = 128;

    /// <summary>
    /// How many windows the floor may climb through without once falling back.
    ///
    /// This is the brief's own rule — "a rolling window's minimum must fall back at least once per N
    /// decisions" — and it is asserted beside <see cref="GrowthSlackFacts"/> rather than instead of it,
    /// because the two catch different shapes: a store leaking one fact per decision stays inside the
    /// bound for 128 decisions while never falling back once, and a run whose census swings widely falls
    /// back constantly while still crossing the bound. Twenty windows is a thousand decisions, so the
    /// rule reads as "the quietest decision of some thousand must be quieter than the quietest of the
    /// thousand before it, at least once per thousand". Measured: a clean 7,200-tick soak at seed 1 fell
    /// back 13 times over 51 windows against the 2 this requires, and the planted static leak fell back 0
    /// times over 36 windows against 1. Below twenty windows the rule asks for nothing and the bound is
    /// the whole verdict, which the row's message says out loud.
    /// </summary>
    private const int RequiredFallbackSpanWindows = 20;

    /// <summary>
    /// How much managed memory the run may end holding over its warmed baseline, after a forced
    /// collection at both ends, in bytes.
    ///
    /// The baseline is taken *after* the warm-up decisions below rather than at the start, because the
    /// JIT, the simulation caches, the clearance chunks and the audit's own last-observed map all fill
    /// on the opening decisions and would read as a leak against a baseline taken before them —
    /// `VerifyWhatEachDecisionCosts` throws a warm-up pass away for the same reason.
    ///
    /// Sixty-four megabytes is the declared line and it is deliberately generous: what the play showed
    /// was retention that scaled with the session, so the thing to catch is a run that ends far above
    /// where it settled, not one that ends a few megabytes up. The row prints the actual delta, the two
    /// absolute figures and the per-generation collection counts, so a number near the line is visible
    /// as such rather than as a pass.
    /// </summary>
    private const long MemorySlackBytes = 64L * 1024 * 1024;

    /// <summary>How many decisions are thrown away before the memory baseline is taken.</summary>
    private const int WarmUpDecisions = 25;

    /// <summary>One frame at sixty updates a second, in milliseconds, which is what a tick's whole brain
    /// cost is read against. It is the game's own rate rather than a number chosen here.</summary>
    private const double FrameMilliseconds = 1000d / 60d;

    /// <summary>Where the companion's own body stands in `Main.npc` while the soak runs.
    ///
    /// <b>Without it the capture this run writes is audited against nothing.</b>
    /// `ReadLiveCourseForAudit.Read` reaches the course through `CompanionNPC.Instance`, which scans
    /// `Main.ActiveNPCs` for the registered type, and `PrepareTheHeadlessEngine.AttachCompanion` builds a
    /// body with both halves of the ModNPC attachment and no slot — so the scan finds nothing, the
    /// audit's source returns null on every call, and the capture closes with every decision audited and
    /// zero observations read, which silently reduces six contracts to the two that read the payload
    /// alone. `StageRecordedActors` skips slot 0 by its own guard, so nothing this folder stages
    /// collides with it.</summary>
    private const int CompanionSlot = 0;

    /// <summary>One decision's sample, taken at the tick the decision settled.</summary>
    private readonly record struct DecisionSample(
        int Tick, int Facts, int Candidates, double DecideMsSum, double DecideMsMax, long ManagedBytes);

    public static int Run(string world, int seed, int ticks, string suite, string? recordTo, bool driveLight)
    {
        var loaded = LoadTheSavedWorld.Load(world);
        Console.WriteLine($"WORLD {loaded.Name} {loaded.Width}x{loaded.Height} hash={loaded.Hash} loaded in {loaded.Seconds:0.0}s");

        // The cast first, because it is a walk of the same generator the run will walk: everything it
        // places is placed beside where the bot will actually be standing on the tick it appears, and
        // that cannot be known without the track.
        ReadRecordedActors.Cast cast = DriveASeededScene.BuildTheCast(seed, 1, ticks);
        var stage = new StageRecordedActors(cast, StageRecordedActors.HostileMotion.Native);
        Console.WriteLine("CAST " + cast.Note);

        live::AICompanion.Companion.Brain.Infrastructure.Movement.LimitPlanningWork.Unbounded = false;
        live::AICompanion.Companion.Brain.Infrastructure.Movement.BehaviourCensus.Reset();
        PrepareTheHeadlessEngine.PinEveryRandomSource(seed);
        PrepareTheHeadlessEngine.StartTheWorldClockAt(1);
        PrepareTheHeadlessEngine.PrepareLightServices();

        var scene = new DriveASeededScene(seed, 1);
        ReadRecordedRoute.Step opening = scene.Advance(1, out _);
        CompanionNPC companion = PrepareTheHeadlessEngine.AttachCompanion(opening.CompanionCentre, opening.PlayerFeet);
        PutTheCompanionWhereTheAuditCanFindIt(companion);
        Player player = Main.player[0];
        if (driveLight) PrepareTheHeadlessEngine.WarmTheLightEngine(player.Bottom.ToTileCoordinates(), 80, 60);

        string folder = AttachTheRecorder.Open(recordTo, $"generated-soak-seed-{seed}");
        Console.WriteLine("RECORDER " + AttachTheRecorder.Describe());
        // The audit's source, which nothing in this folder installs. In play it is installed by
        // `BrainTelemetry.Load`, a ModSystem override the loader calls and `AttachTheRecorder` does not;
        // without it the audit counts every decision and reads no observation, and four of the six
        // contracts are inert in the capture. Measured on the first soak run: 600 decisions audited, 0
        // observations read. Installing it from the mod's own entry point would be a change to
        // `AttachTheRecorder`, which belongs to the recorded-scene lane; this run installs it for itself
        // and the return writes the shared fix up.
        live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.ReadLiveCourseForAudit.Install();
        live::AICompanion.Companion.Brain.Infrastructure.Diagnostics.AuditDecisionContracts.Reset();

        var samples = new List<DecisionSample>(Math.Max(64, ticks / 8));
        int overrunTicks = 0;
        long lastDecisionId = -1;
        double decideSum = 0, decideMax = 0;
        long baselineBytes = 0;
        int gen0Start = GC.CollectionCount(0), gen1Start = GC.CollectionCount(1), gen2Start = GC.CollectionCount(2);
        var clock = Stopwatch.StartNew();
        string? threw = null;
        try
        {
            // The whole run is a fresh scene, and `AttachCompanion` already forgot what a previous one
            // learned; this is the second half of that, the actors a previous pass staged.
            stage.Reset();
            for (int index = 0; index < ticks; index++)
            {
                int tick = 1 + index;
                Point? mine = null;
                // The opening step was already taken to place the two bodies; advancing again for it
                // would put the generator one tick ahead of the cast that was built from it, and the
                // hostiles would then arrive one tick away from where the bot actually is.
                ReadRecordedRoute.Step step = index == 0 ? opening : scene.Advance(tick, out mine);
                if (mine is { } target) BreakOneTile(scene, target);
                player.Bottom = step.PlayerFeet;
                player.velocity = step.PlayerVelocity;
                player.dead = false;
                player.statLife = step.PlayerLife;

                stage.BeforeTheBrain(tick);
                PrepareTheHeadlessEngine.AdvanceTheWorldClock();
                if (driveLight) PrepareTheHeadlessEngine.DriveLightOnce(player.Bottom.ToTileCoordinates(), 80, 60);

                companion.AI();
                PrepareTheHeadlessEngine.AdvanceTheNativeBody(companion);
                stage.AfterTheBrain(tick, player);

                var brain = companion.Brain;
                decideSum += brain.DecideMs;
                decideMax = Math.Max(decideMax, brain.DecideMs);
                if (brain.TotalMs > FrameMilliseconds) overrunTicks++;

                var course = brain.Course;
                if (course.DecisionId != lastDecisionId)
                {
                    lastDecisionId = course.DecisionId;
                    samples.Add(new DecisionSample(tick, course.Facts?.Facts.Count ?? 0, course.Candidates.Count,
                        decideSum, decideMax, GC.GetTotalMemory(false)));
                    decideSum = decideMax = 0;
                    if (samples.Count == WarmUpDecisions) baselineBytes = Settled();
                }
            }
        }
        catch (Exception failure)
        {
            threw = $"the soak threw at tick {samples.Count} decision(s) in: {failure}";
        }
        finally
        {
            clock.Stop();
            AttachTheRecorder.Close();
            Main.npc[CompanionSlot] = new NPC { whoAmI = CompanionSlot, active = false };
        }

        long endBytes = Settled();
        if (baselineBytes == 0) baselineBytes = endBytes;
        var gc = (Gen0: GC.CollectionCount(0) - gen0Start, Gen1: GC.CollectionCount(1) - gen1Start, Gen2: GC.CollectionCount(2) - gen2Start);
        Console.WriteLine($"SOAK {ticks} ticks in {clock.Elapsed.TotalSeconds:0.0}s "
            + $"({clock.Elapsed.TotalMilliseconds / Math.Max(1, ticks):0.00} ms/tick) under the production clock, "
            + $"{samples.Count} decision(s), gc {gc.Gen0}/{gc.Gen1}/{gc.Gen2}, "
            + $"{scene.TilesMined} tile(s) mined by the bot"
            + (scene.MiningRetired == null ? "" : $"; mining retired: {scene.MiningRetired}"));

        int failures = 0;
        if (threw != null)
        {
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, "the soak runs to its tick count", threw,
                mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
            failures++;
        }
        failures += GradeTheGrowth(suite, samples, seed, ticks);
        failures += GradeTheMemory(suite, baselineBytes, endBytes, gc, seed);
        failures += GradeTheCapture(suite, folder, seed);
        ReportTheCost(suite, samples, overrunTicks, ticks, clock, scene);
        return failures;
    }

    /// <summary>
    /// The bot's own tile break, through the game's own <c>WorldGen.KillTile</c>.
    ///
    /// It goes through the engine rather than writing <c>Main.tile</c> because the mod's
    /// <c>TrackTerrainChanges</c> is a <c>GlobalTile</c> and <c>KillTile</c>'s announcement through
    /// <c>TileLoader</c> is the only route by which an edit reaches every retained search, route and
    /// clearance chunk that read there — the silence this folder's own loader-hook sweep exists for.
    ///
    /// <b>A throw retires the phase and is named rather than caught and forgotten.</b> This folder's
    /// standing lesson is that a behaviour this instrument has never performed is a stretch of the
    /// engine nobody here has ever run: a player-driven break reaches <c>KillTile_DropBait</c> and
    /// <c>Item.NewItem</c>, neither of which the companion's own mining path takes. The first exception
    /// stops the bot mining for the rest of the run and its text rides out in the tiles-mined measure,
    /// so the run reports reduced churn instead of dying or pretending.
    /// </summary>
    private static void BreakOneTile(DriveASeededScene scene, Point target)
    {
        if (target.X < 1 || target.X >= Main.maxTilesX - 1 || target.Y < 1 || target.Y >= Main.maxTilesY - 1) return;
        if (!Main.tile[target.X, target.Y].HasTile) return;
        try
        {
            Terraria.WorldGen.KillTile(target.X, target.Y);
            scene.CountMinedTile();
        }
        catch (Exception failure)
        {
            scene.RetireMining($"WorldGen.KillTile at {target.X},{target.Y} threw {failure.GetType().Name}: {failure.Message}");
        }
    }

    /// <summary>Managed bytes after a forced collection that waits for finalizers, which is the only
    /// figure two points in a run can be compared on: <c>GetTotalMemory(false)</c> is a sample of a heap
    /// mid-allocation and moves by tens of megabytes between two adjacent ticks.</summary>
    private static long Settled()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    /// <summary>
    /// The growth verdict: the floor of the last window against the floor of the first.
    ///
    /// A rolling *minimum* rather than a mean or a maximum, because the play's signature was a floor
    /// that never came back down — a census that peaks and falls is a brain discovering and retiring
    /// work, and a census whose quietest decision is larger than it was half an hour ago is a store
    /// outliving the observations that filled it.
    /// </summary>
    private static int GradeTheGrowth(string suite, List<DecisionSample> samples, int seed, int ticks)
    {
        // The yield rides out on every run, graded or not, because the verdict below covers a slice of the
        // run rather than the run, and how big that slice is moved 1.8x with machine load on one seed —
        // 1,428 against 2,536 decisions over the same 7,200 ticks. A reader comparing two soak rows needs
        // to see that the second one graded nearly twice as much brain as the first.
        EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, "soak decision yield per thousand ticks",
            samples.Count * 1000d / Math.Max(1, ticks), "decisions/1k ticks",
            mode: "in-suite",
            tags: new[] { EmitLedgerRows.ProductionAllowancesTag, "sampled-under-the-production-clock" },
            message: $"seed {seed}: {samples.Count} decision(s) over {ticks} tick(s); the growth verdict needs "
                + $"{WarmUpDecisions + GrowthWindowDecisions * 2} of them and grades "
                + $"{Math.Max(0, samples.Count - WarmUpDecisions) / GrowthWindowDecisions} window(s) of "
                + $"{GrowthWindowDecisions}. Measured 1,428 against 2,536 decisions over the same 7,200 ticks at one "
                + "seed under different machine load, so this number is the verdict's coverage rather than a "
                + "property of the brain");

        return GradeTheFactFloor(suite, "the frozen observation's floor does not climb over a long run",
            samples.Select(s => s.Facts).ToList(), ticks, $"seed {seed}",
            new[] { EmitLedgerRows.ProductionAllowancesTag }, mode: "in-suite");
    }

    /// <summary>
    /// The growth rule itself, over any run's per-decision fact counts.
    ///
    /// Shared rather than copied because the scene is the variable and the rule is not: the soak drives a
    /// seeded surface bot and the play-measures replay drives the capture that actually leaked, and a
    /// second copy of two thresholds would let one of them be tuned to the run in front of whoever was
    /// looking. The caller owns the case name, the label, the tags and the mode; everything the verdict
    /// decides on lives here.
    /// </summary>
    public static int GradeTheFactFloor(string suite, string @case, IReadOnlyList<int> factsPerDecision,
        int ticks, string label, IReadOnlyList<string> tags, string mode)
    {
        var samples = factsPerDecision;
        string Case = @case;
        // The warm-up decisions are out of the windows for the memory baseline's reason, and it is not a
        // small correction here: discovery is resumable and accumulates, so a brain's first decisions
        // carry almost nothing. Measured on the first soak run, seed 1, 600 ticks: window floors of
        // 12, 149, 150, 150 — the opening window is the census filling rather than a climb, and reading
        // it as the baseline reddens the row on a clean tree by 138 facts.
        if (samples.Count < WarmUpDecisions + GrowthWindowDecisions * 2)
        {
            string reason = $"{samples.Count} decision(s) in {ticks} tick(s) against the "
                + $"{WarmUpDecisions + GrowthWindowDecisions * 2} this verdict needs ({WarmUpDecisions} warm-up plus "
                + $"two windows of {GrowthWindowDecisions}), so it would be reading one window against itself or "
                + "against the census filling. The yield measure beside this row carries the same count, because a "
                + "skip that says only 'not enough' leaves a reader unable to tell a short run from a starved one";
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, Case, reason);
            return 0;
        }

        var floors = new List<int>();
        for (int start = WarmUpDecisions; start + GrowthWindowDecisions <= samples.Count; start += GrowthWindowDecisions)
        {
            int floor = int.MaxValue;
            for (int i = start; i < start + GrowthWindowDecisions; i++) floor = Math.Min(floor, samples[i]);
            floors.Add(floor);
        }
        int first = floors[0], last = floors[^1], peak = samples.Max();
        int fallbacks = 0;
        for (int i = 1; i < floors.Count; i++) if (floors[i] < floors[i - 1]) fallbacks++;

        // Two rules, not one, and the second is the brief's own. The bound catches a fast climb in a short
        // run; the fall-back rule catches the shape the play actually had, which is a floor that never
        // comes back down. Neither implies the other: a store leaking one fact per decision passes the
        // bound for 128 decisions while never falling back once, and a run whose floor swings wildly falls
        // back often while crossing the bound. The first version of this row computed `fallbacks`, printed
        // it, and asserted only the bound, so a strictly monotone floor passed with "0 window(s) fell back"
        // inside a PASS message.
        //
        // The rate rather than "at least one", because a run is graded in windows and a longer run has
        // more of them: one fall-back per RequiredFallbackSpanWindows is one release of the floor every
        // thousand decisions. Measured on a clean 7,200-tick soak at seed 1: 13 fall-backs over 51 windows
        // against the 2 required, and 0 over 36 on the planted leak, which is where the margin comes from.
        int requiredFallbacks = floors.Count / RequiredFallbackSpanWindows;
        string message = $"{label}: window floors {string.Join(" ", floors)} over {floors.Count} window(s) of "
            + $"{GrowthWindowDecisions} decision(s) after {WarmUpDecisions} warm-up decisions; first {first}, last {last}, peak {peak}, "
            + $"{fallbacks} window(s) fell back on the one before against the {requiredFallbacks} required. Two lines: "
            + $"last <= first + {GrowthSlackFacts}, a quarter of AuditDecisionContracts.MaximumFactsPerDecision, "
            + "because there is no declared fact budget in Selection/ to mirror and the play climbed 150 -> 1,603 and "
            + $"never fell; and one fall-back per {RequiredFallbackSpanWindows} window(s), the brief's own rule, "
            + "because a floor that only ever rises is a store outliving its observations however slowly it does it";

        var broken = new List<string>();
        if (last > first + GrowthSlackFacts) broken.Add($"the floor rose by {last - first} facts over the run");
        if (fallbacks < requiredFallbacks)
            broken.Add($"the floor fell back {fallbacks} time(s) against the {requiredFallbacks} required over "
                + $"{floors.Count} window(s)");
        if (broken.Count == 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, Case, message, mode: mode, tags: tags);
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, Case,
            string.Join("; and ", broken) + ". " + message, mode: mode, tags: tags);
        return 1;
    }

    private static int GradeTheMemory(string suite, long baseline, long end, (int Gen0, int Gen1, int Gen2) gc, int seed)
    {
        const string Case = "managed memory comes back to its warmed baseline after the run";
        long delta = end - baseline;
        // Each figure into a local first: concatenating two interpolated strings yields a `string`, so
        // the invariant-formatting wrappers cannot span the concatenation. The suite's guide carries it.
        string baselineMb = (baseline / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);
        string endMb = (end / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);
        string deltaMb = (delta / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);
        string message = $"seed {seed}: {baselineMb} MB after {WarmUpDecisions} warm-up decisions and "
            + $"{endMb} MB at the end, both after a forced blocking gen-2 collection; "
            + $"delta {deltaMb} MB against a declared line of {MemorySlackBytes / 1024 / 1024} MB; "
            + $"collections {gc.Gen0}/{gc.Gen1}/{gc.Gen2}. The baseline is taken after the warm-up because the JIT, "
            + "the simulation caches and the clearance chunks all fill on the opening decisions";
        if (delta <= MemorySlackBytes)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, Case, message,
                mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, Case, message,
            mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
        return 1;
    }

    /// <summary>
    /// The capture the soak wrote, read back for the six kinds the decision audit names.
    ///
    /// It reads the recording rather than the audit's live counters on purpose: the counters say what
    /// fired and the capture says what a person reading this run afterwards would find, which is the
    /// artefact the tripwires exist to put something into. Its premise is the closing line's
    /// <c>audit-observations-read</c>, because a session whose audit read no observation reports four of
    /// the six contracts as unbroken by never having been able to break them.
    /// </summary>
    private static int GradeTheCapture(string suite, string folder, int seed)
    {
        const string Case = "the soak's own capture holds no decision-contract violation";
        string? capture = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (capture == null)
        {
            EmitLedgerRows.Skipped(ScoreTheRun.Instrument, suite, Case, $"the run wrote no capture under {folder}");
            return 0;
        }
        string closing = File.ReadLines(capture).LastOrDefault(l => l.StartsWith("# closing=", StringComparison.Ordinal)) ?? "";
        long read = Field(closing, "audit-observations-read"), audited = Field(closing, "decisions-audited");
        if (read <= 0)
        {
            // A fail rather than a skip, and the distinction is about who is at fault. A skip is for a
            // question this machine cannot ask — no saved world, no committed capture. This run installs
            // `ReadLiveCourseForAudit` itself and places its own body, so nothing about the machine can
            // produce a zero here: the only way to reach it is a defect in the wiring under test, and a
            // skip would report that as "not asked" while the scoreboard stayed green. The fuzzer's
            // premise row already throws on the identical condition; this is the same premise in the same
            // verdict class, which it was not before.
            // A negative count is the field being absent from the closing line rather than a count of
            // minus one, and the two have different causes: absent means the recorder closed without the
            // audit's figures at all, zero means it closed with them and they were zero.
            string counts = read < 0 || audited < 0
                ? $"the closing line of {Path.GetFileName(capture)} carries no decisions-audited or "
                  + "audit-observations-read field at all"
                : $"{audited} decision(s) audited and {read} frozen observation(s) read in {Path.GetFileName(capture)}";
            EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, Case,
                counts + ", "
                + "so four of the six contracts could not have fired whatever this run did and a green row here would "
                + "mean nothing. The soak installs the audit's source and places its own body, so this is a defect in "
                + "the wiring rather than a machine that cannot be asked: look at what ReadLiveCourseForAudit.Read "
                + "returned null for — no companion in Main.npc under the registered type, or no brain on the one "
                + "that is there",
                mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
            return 1;
        }

        string events = Path.ChangeExtension(capture, null) + "-events.jsonl";
        var found = ReadTheViolations(events);
        string counted = found.Count == 0 ? "none"
            : string.Join(", ", found.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value.Count}"));

        // Three of the six are what this row asserts on, and the split is a judgement about what a soak
        // can hold against the brain rather than about what is easy.
        //
        // `decide-overran-allowance` is a wall clock. The soak keeps the production clock on purpose, so
        // a headless host running at six to nine milliseconds a tick with other work on the machine fires
        // it by construction — measured on the first two soak runs, once each — and a verdict on it would
        // be a verdict on how busy this machine was.
        //
        // `accepted-use-absent-next-tick` and `activity-exited-during-decision` fire legitimately when
        // the world removes what a course was holding, and the soak's own staging retires a hostile on
        // its scheduled tick, which is that. The contract carries no fact saying which body the course
        // held, so nothing here can separate the loop it is named for from an ordinary removal.
        // `Tools/EngineReplay/DecisionMaking/FuzzTheDecisionContracts.cs` is where that separation is
        // made, because a generator knows what it did to the world and this run's stager does not report
        // it per firing. Both counts ride in the message.
        string[] asserted = { "census-admitted-binder-refused", "empty-course-beside-usable-work", "fact-count-above-bound" };
        var broken = asserted.Where(found.ContainsKey).ToList();
        string message = $"seed {seed}: {audited} decision(s) audited, {read} observation(s) read, "
            + $"violations in {Path.GetFileName(events)}: {counted}. The verdict is on "
            + string.Join(", ", asserted) + " — a contradiction and a declared bound, neither of which a busy "
            + "machine or a retiring hostile can produce; the other three are reported beside it";
        if (broken.Count == 0)
        {
            EmitLedgerRows.Pass(ScoreTheRun.Instrument, suite, Case, message,
                mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
            return 0;
        }
        EmitLedgerRows.Fail(ScoreTheRun.Instrument, suite, Case,
            message + "; first of each: " + string.Join(" | ", broken.Select(kind => $"{kind}: {found[kind].First}")),
            mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag });
        return 1;
    }

    /// <summary>
    /// Every <c>contract-violation</c> record in a capture's sidecar, by kind, with the first one's own
    /// prose detail.
    ///
    /// It parses the record rather than searching the line for a literal, because the payload's field
    /// order is the writer's business and a grep over it is a contract nobody declared — which is this
    /// tree's own rule about one program reading another's output.
    /// </summary>
    private static Dictionary<string, (int Count, string First)> ReadTheViolations(string events)
    {
        var found = new Dictionary<string, (int Count, string First)>(StringComparer.Ordinal);
        if (!File.Exists(events)) return found;
        foreach (string line in File.ReadLines(events))
        {
            if (line.Length == 0 || line[0] != '{') continue;
            using System.Text.Json.JsonDocument record = System.Text.Json.JsonDocument.Parse(line);
            if (!record.RootElement.TryGetProperty("payload_kind", out var kind) || kind.GetString() != "contract-violation") continue;
            string violation = Payload(record.RootElement, "violation"), detail = Payload(record.RootElement, "detail");
            if (violation.Length == 0) continue;
            found[violation] = found.TryGetValue(violation, out var had)
                ? (had.Count + 1, had.First)
                : (1, detail);
        }
        return found;
    }

    private static string Payload(System.Text.Json.JsonElement record, string name)
        => record.TryGetProperty("payload", out var payload)
            && payload.TryGetProperty("Fields", out var fields)
            && fields.TryGetProperty(name, out var field)
            && field.TryGetProperty("Text", out var text) ? text.GetString() ?? "" : "";

    /// <summary>
    /// What the run cost, as measures and never as a pass line.
    ///
    /// The brief this instrument was built to asked for the frame-overrun share as a verdict. It is a
    /// measure instead, and the reason is this suite's own rule rather than a softening: a wall-clock
    /// threshold here is judged in a regime no row declares, three seats build in this checkout at
    /// once, and the play measures next door already demoted their equivalent for exactly this. What a
    /// verdict would add is a number that goes red when the machine is busy; what the measure adds is
    /// the number itself, tagged with the clock it was taken under.
    /// </summary>
    private static void ReportTheCost(string suite, List<DecisionSample> samples, int overrunTicks, int ticks,
        Stopwatch clock, DriveASeededScene scene)
    {
        void Measure(string name, double value, string unit, string message = "")
            => EmitLedgerRows.Measure(ScoreTheRun.Instrument, suite, name, value, unit,
                mode: "in-suite", tags: new[] { EmitLedgerRows.ProductionAllowancesTag, "sampled-under-the-production-clock" },
                message: message);

        Measure("soak ticks whose whole brain overran a frame", ticks == 0 ? 0 : overrunTicks * 100d / ticks, "%",
            $"{overrunTicks} of {ticks} tick(s) over {FrameMilliseconds:0.00} ms, sampled under the game's own "
            + "allowances with other work on this machine; a share and never a pass line");
        Measure("soak decisions", samples.Count, "count");
        Measure("soak peak facts in one frozen observation", samples.Count == 0 ? 0 : samples.Max(s => s.Facts), "facts");
        Measure("soak median facts per decision", Median(samples.Select(s => (double)s.Facts).ToList()), "facts");
        Measure("soak median opportunities carried per decision", Median(samples.Select(s => (double)s.Candidates).ToList()), "candidates");
        Measure("soak decide cost per decision at p50", Median(samples.Select(s => s.DecideMsSum).ToList()), "ms",
            "the sum over the ticks one decision spanned, not one tick's share; a decision that spans ticks "
            + "has a sum and a maximum and they are different questions");
        Measure("soak worst decide cost on one tick", samples.Count == 0 ? 0 : samples.Max(s => s.DecideMsMax), "ms");
        Measure("soak wall clock per tick", clock.Elapsed.TotalMilliseconds / Math.Max(1, ticks), "ms");
        Measure("soak tiles the bot mined", scene.TilesMined, "tiles",
            scene.MiningRetired == null
                ? "the bot's own terrain churn, which is what invalidates retained searches"
                : "the bot's mining stopped early: " + scene.MiningRetired);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values.Count % 2 == 1 ? values[values.Count / 2]
            : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2d;
    }

    private static long Field(string line, string name)
    {
        foreach (string part in line.Split(';'))
        {
            int equals = part.IndexOf('=');
            if (equals > 0 && part[..equals].TrimStart('#', ' ').EndsWith(name, StringComparison.Ordinal)
                && long.TryParse(part[(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return value;
        }
        return -1;
    }

    /// <inheritdoc cref="CompanionSlot"/>
    private static void PutTheCompanionWhereTheAuditCanFindIt(CompanionNPC companion)
    {
        if (Terraria.ModLoader.ModContent.GetInstance<CompanionNPC>() == null)
            Terraria.ModLoader.ContentInstance.Register(companion);
        companion.NPC.type = Terraria.ModLoader.ModContent.NPCType<CompanionNPC>();
        companion.NPC.active = true;
        companion.NPC.whoAmI = CompanionSlot;
        Main.npc[CompanionSlot] = companion.NPC;
    }
}
